using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Windows.Taskbar;

/// <summary>
/// Locates the taskbar button of the foreground app via UI Automation (the only reliable way on
/// Windows 11, where the taskbar is a XAML app and button rectangles aren't otherwise exposed).
/// Searches every taskbar — the primary (<c>Shell_TrayWnd</c>) and each secondary-monitor taskbar
/// (<c>Shell_SecondaryTrayWnd</c>) — so apps on any monitor are found, and matches the best button
/// to the active window by name. All calls are defensively wrapped: any failure returns
/// <see langword="false"/> so the caller falls back to best-effort positioning.
/// </summary>
public sealed unsafe class UiaTaskbarButtonLocator(ILogger<UiaTaskbarButtonLocator> logger)
{
    private static readonly Guid ClsidCUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    private readonly ILogger<UiaTaskbarButtonLocator> _logger = logger;
    private IUIAutomation? _automation;

    public bool TryGetButtonRect(ActiveWindowInfo active, out PixelRect rect)
    {
        rect = default;
        try
        {
            _automation ??= CreateAutomation();
            if (_automation is null)
            {
                return false;
            }

            string appId = GetWindowAppId(new HWND(active.Handle));

            IUIAutomationCondition anyElement = _automation.CreateTrueCondition();
            // Collect every AUMID match before committing — multiple instances of the same app
            // (e.g. two Chrome windows) share an AUMID, so we must score them all and pick the best.
            var aumidCandidates = new List<(IUIAutomationElement Element, int NameScore)>();
            IUIAutomationElement? bestNameMatch = null;
            int bestNameScore = 0;
            var seenButtons = new List<string>();

            foreach (HWND taskbar in GetTaskbarWindows())
            {
                IUIAutomationElement root = _automation.ElementFromHandle(taskbar);
                IUIAutomationElementArray elements = root.FindAll(TreeScope.TreeScope_Descendants, anyElement);

                for (int i = 0; i < elements.Length; i++)
                {
                    IUIAutomationElement element = elements.GetElement(i);
                    if (element.CurrentControlType != UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId)
                    {
                        continue;
                    }

                    string automationId = element.CurrentAutomationId.ToString();
                    string name = element.CurrentName.ToString();
                    seenButtons.Add($"{name} [id={automationId}]");

                    int nameScore = MatchScore(name, active);
                    if (nameScore > bestNameScore)
                    {
                        bestNameScore = nameScore;
                        bestNameMatch = element;
                    }

                    // AUMID is authoritative — but don't short-circuit, as multiple instances of the
                    // same app share the same AUMID and we need all candidates to pick the right one.
                    if (appId.Length > 0 && automationId.Contains(appId, StringComparison.OrdinalIgnoreCase))
                    {
                        aumidCandidates.Add((element, nameScore));
                    }
                }
            }

            // AUMID candidates are the most reliable signal.  When there is more than one (e.g. two
            // Chrome windows), the name-score disambiguates to the focused instance's button.
            if (aumidCandidates.Count > 0)
            {
                IUIAutomationElement best = aumidCandidates.MaxBy(c => c.NameScore).Element;
                RECT r = best.CurrentBoundingRectangle;
                rect = PixelRect.FromLTRB(r.left, r.top, r.right, r.bottom);
                if (!rect.IsEmpty)
                {
                    return true;
                }
            }

            if (bestNameMatch is null || bestNameScore == 0)
            {
                _logger.LogDebug(
                    "UIA no match for '{Proc}' (title '{Title}', appId '{AppId}'). Buttons seen: {Buttons}",
                    active.ProcessName, active.Title, appId, string.Join(" | ", seenButtons));
            }

            if (bestNameMatch is not null && bestNameScore > 0)
            {
                RECT r = bestNameMatch.CurrentBoundingRectangle;
                rect = PixelRect.FromLTRB(r.left, r.top, r.right, r.bottom);
                return !rect.IsEmpty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UI Automation taskbar lookup failed; using best-effort placement.");
        }

        return false;
    }

    /// <summary>Locates the Start button on any taskbar (its AutomationId is language-neutral "StartButton").</summary>
    public bool TryGetStartButtonRect(out PixelRect rect)
    {
        rect = default;
        try
        {
            _automation ??= CreateAutomation();
            if (_automation is null)
            {
                return false;
            }

            IUIAutomationCondition anyElement = _automation.CreateTrueCondition();
            foreach (HWND taskbar in GetTaskbarWindows())
            {
                IUIAutomationElement root = _automation.ElementFromHandle(taskbar);
                IUIAutomationElementArray elements = root.FindAll(TreeScope.TreeScope_Descendants, anyElement);

                for (int i = 0; i < elements.Length; i++)
                {
                    IUIAutomationElement element = elements.GetElement(i);
                    if (element.CurrentControlType != UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId)
                    {
                        continue;
                    }

                    string automationId = element.CurrentAutomationId.ToString();
                    string name = element.CurrentName.ToString();
                    if (automationId.Equals("StartButton", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Start", StringComparison.OrdinalIgnoreCase))
                    {
                        RECT r = element.CurrentBoundingRectangle;
                        rect = PixelRect.FromLTRB(r.left, r.top, r.right, r.bottom);
                        if (!rect.IsEmpty)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Start-button lookup failed.");
        }

        return false;
    }

    private static List<HWND> GetTaskbarWindows()
    {
        var taskbars = new List<HWND>();

        BOOL Collect(HWND hwnd, LPARAM _)
        {
            string cls = ReadClassName(hwnd);
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                taskbars.Add(hwnd);
            }

            return true;
        }

        _ = PInvoke.EnumWindows(Collect, default);
        return taskbars;
    }

    private static string ReadClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[128];
        int length;
        fixed (char* p = buffer)
        {
            length = PInvoke.GetClassName(hwnd, new PWSTR(p), buffer.Length);
        }

        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    /// <summary>Reads a window's Application User Model ID (empty if it has none).</summary>
    private static string GetWindowAppId(HWND hwnd)
    {
        try
        {
            if (PInvoke.SHGetPropertyStoreForWindow(hwnd, out IPropertyStore store).Failed || store is null)
            {
                return string.Empty;
            }

            PROPERTYKEY key = PInvoke.PKEY_AppUserModel_ID;
            store.GetValue(in key, out PROPVARIANT value);
            try
            {
                Span<char> buffer = stackalloc char[512];
                fixed (char* p = buffer)
                {
                    if (PInvoke.PropVariantToString(value, new PWSTR(p), (uint)buffer.Length).Succeeded)
                    {
                        return new string(p);
                    }
                }
            }
            finally
            {
                _ = PInvoke.PropVariantClear(ref value);
            }
        }
        catch (Exception)
        {
            // No AUMID available; the caller falls back to name matching.
        }

        return string.Empty;
    }

    private IUIAutomation? CreateAutomation()
    {
        HRESULT hr = PInvoke.CoCreateInstance(ClsidCUIAutomation, null, CLSCTX.CLSCTX_INPROC_SERVER, out IUIAutomation automation);
        if (hr.Succeeded)
        {
            return automation;
        }

        _logger.LogDebug("Could not create the UI Automation client (0x{Hr:X}).", hr.Value);
        return null;
    }

    // Taskbar-chrome words that carry no app identity and would cause false matches.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "running", "window", "windows", "pinned", "page", "pages", "more", "and", "the", "new", "tab", "tabs",
    };

    /// <summary>
    /// Scores a taskbar button against the active window by shared significant words (button labels and
    /// window titles rarely share a contiguous substring — e.g. Edge's button "Personal - Edge" vs its
    /// title "Untitled - Personal - Microsoft Edge" — but they do share words). The button with the
    /// highest score wins; 0 = no match.
    /// </summary>
    private static int MatchScore(string buttonName, ActiveWindowInfo active)
    {
        HashSet<string> buttonWords = Words(buttonName);
        if (buttonWords.Count == 0)
        {
            return 0;
        }

        HashSet<string> targetWords = Words(active.Title);
        foreach (string w in Words(active.ProcessName))
        {
            targetWords.Add(w);
        }

        int score = 0;
        foreach (string w in buttonWords)
        {
            if (targetWords.Contains(w))
            {
                score += w.Length;
            }
        }

        return score;
    }

    /// <summary>
    /// Splits text into significant, case-insensitive word tokens (≥3 chars, minus stop words).
    /// Also decomposes CamelCase tokens so "WindowsTerminal" yields both "WindowsTerminal" and
    /// "Terminal", letting process names like <c>WindowsTerminal.exe</c> match a "Terminal" button.
    /// </summary>
    private static HashSet<string> Words(string? text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text))
        {
            return words;
        }

        int i = 0;
        while (i < text.Length)
        {
            if (!IsWordChar(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && IsWordChar(text[i]))
            {
                i++;
            }

            AddTokenWithCamelParts(words, text[start..i]);
        }

        return words;
    }

    private static void AddTokenWithCamelParts(HashSet<string> words, string token)
    {
        if (token.Length >= 3 && !StopWords.Contains(token))
        {
            words.Add(token);
        }

        // Split "WindowsTerminal" → "Windows" + "Terminal" so a process name with a CamelCase
        // compound can still match a shorter taskbar button label.
        int partStart = 0;
        for (int j = 1; j < token.Length; j++)
        {
            if (char.IsUpper(token[j]) && char.IsLower(token[j - 1]))
            {
                string part = token[partStart..j];
                if (part.Length >= 3 && !StopWords.Contains(part))
                {
                    words.Add(part);
                }

                partStart = j;
            }
        }

        if (partStart > 0) // at least one split occurred — add the final segment
        {
            string last = token[partStart..];
            if (last.Length >= 3 && !StopWords.Contains(last))
            {
                words.Add(last);
            }
        }
    }

    // Keep '+' and '#' as part of a token so "Notepad++" and "C#" stay distinct from "Notepad"/"C" —
    // otherwise opening classic Notepad matches the Notepad++ taskbar button (both tokenise to "notepad").
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '+' or '#';
}
