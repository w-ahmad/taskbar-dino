using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace TaskbarDino.Win32;

/// <summary>
/// Locates the taskbar button of the foreground app via UI Automation — the only reliable way on
/// Windows 11, where the taskbar is a XAML app and button rectangles are not otherwise exposed.
/// Every taskbar (primary and per-monitor) is searched with one cached, filtered query each: the
/// provider returns only Button elements with Name/AutomationId/BoundingRectangle pre-fetched, so
/// a lookup is a couple of cross-process round trips rather than hundreds.
/// </summary>
/// <remarks>
/// Matching: the window's App User Model ID (AUMID) against each button's AutomationId
/// ("Appid: &lt;AUMID&gt;") is authoritative — it is what makes Edge PWAs, Store and pinned apps
/// work, whose titles never mention the app. Multiple instances share an AUMID, so the best
/// title-word overlap breaks ties. Without an AUMID match, shared significant words between the
/// button label and the window title/process name decide (labels and titles rarely share a
/// contiguous substring). '+' and '#' stay inside tokens so "Notepad++" ≠ "Notepad".
/// </remarks>
internal sealed unsafe class TaskbarButtonLocator : IDisposable
{
    private static readonly Guid ClsidCUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    private static readonly string[] StopWords =
    [
        "running", "window", "windows", "pinned", "page", "pages", "more", "and", "the", "new", "tab", "tabs",
    ];

    private IUIAutomation* _automation;
    private IUIAutomationCondition* _buttons;
    private IUIAutomationCacheRequest* _cache;
    private bool _unavailable;

    private readonly List<Button> _scratch = new(32);

    private readonly record struct Button(string Name, string AutomationId, PixelRect Rect);

    public bool TryGetButtonRect(ActiveWindow active, out PixelRect rect)
    {
        rect = default;
        if (!EnsureAutomation())
        {
            return false;
        }

        try
        {
            string appId = GetWindowAppId(active.Handle);
            nint windowMonitor = Monitors.FromWindow(active.Handle).Handle;
            CollectButtons();

            int bestAumidScore = -1, bestNameScore = 0;
            PixelRect aumidRect = default, nameRect = default;
            foreach (Button b in _scratch)
            {
                // Windows can show the same button on every taskbar; prefer the one on the window's own monitor.
                int nameScore = MatchScore(b.Name, active) + (Monitors.FromPoint(b.Rect.Center).Handle == windowMonitor ? 1 : 0);
                if (appId.Length > 0 && b.AutomationId.Contains(appId, StringComparison.OrdinalIgnoreCase) && nameScore > bestAumidScore)
                {
                    bestAumidScore = nameScore;
                    aumidRect = b.Rect;
                }

                if (nameScore > bestNameScore)
                {
                    bestNameScore = nameScore;
                    nameRect = b.Rect;
                }
            }

            if (bestAumidScore >= 0 && !aumidRect.IsEmpty)
            {
                rect = aumidRect;
                return true;
            }

            if (bestNameScore > 0 && !nameRect.IsEmpty)
            {
                rect = nameRect;
                return true;
            }

            Log.Info($"No taskbar button for '{active.ProcessName}' \"{active.Title}\" (appId '{appId}', {_scratch.Count} buttons seen)");
        }
        catch (Exception ex)
        {
            Log.Warn($"UI Automation lookup failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>Locates the Start button (AutomationId "StartButton" on every language).</summary>
    public bool TryGetStartButtonRect(out PixelRect rect)
    {
        rect = default;
        if (!EnsureAutomation())
        {
            return false;
        }

        try
        {
            CollectButtons();
            foreach (Button b in _scratch)
            {
                if (b.AutomationId.Equals("StartButton", StringComparison.OrdinalIgnoreCase) && !b.Rect.IsEmpty)
                {
                    rect = b.Rect;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Start button lookup failed: {ex.Message}");
        }

        return false;
    }

    public void Dispose()
    {
        Com.Release(ref _cache);
        Com.Release(ref _buttons);
        Com.Release(ref _automation);
    }

    private bool EnsureAutomation()
    {
        if (_automation is not null)
        {
            return true;
        }

        if (_unavailable)
        {
            return false;
        }

        try
        {
            IUIAutomation* automation;
            PInvoke.CoCreateInstance(in ClsidCUIAutomation, null, CLSCTX.CLSCTX_INPROC_SERVER, out automation).ThrowOnFailure();
            _automation = automation;

            // Only Button elements, with the three properties we read fetched in the same call.
            VARIANT value = default;
            value.Anonymous.Anonymous.vt = VARENUM.VT_I4;
            value.Anonymous.Anonymous.Anonymous.lVal = (int)UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId;
            _buttons = _automation->CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, value);

            _cache = _automation->CreateCacheRequest();
            _cache->AddProperty(UIA_PROPERTY_ID.UIA_NamePropertyId);
            _cache->AddProperty(UIA_PROPERTY_ID.UIA_AutomationIdPropertyId);
            _cache->AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
            _cache->AutomationElementMode = AutomationElementMode.AutomationElementMode_None;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"UI Automation unavailable: {ex.Message}");
            Dispose();
            _unavailable = true;
            return false;
        }
    }

    /// <summary>Fills <see cref="_scratch"/> with every button on every taskbar.</summary>
    private void CollectButtons()
    {
        _scratch.Clear();
        foreach (HWND taskbar in TaskbarWindows.All())
        {
            IUIAutomationElement* root = null;
            IUIAutomationElementArray* elements = null;
            try
            {
                root = _automation->ElementFromHandle(taskbar);
                elements = root->FindAllBuildCache(TreeScope.TreeScope_Descendants, _buttons, _cache);
                int length = elements->Length;
                for (int i = 0; i < length; i++)
                {
                    IUIAutomationElement* element = elements->GetElement(i);
                    if (element is null)
                    {
                        continue;
                    }

                    try
                    {
                        string name = Com.TakeString(element->CachedName);
                        string automationId = Com.TakeString(element->CachedAutomationId);
                        RECT r = element->CachedBoundingRectangle;
                        _scratch.Add(new Button(name, automationId, PixelRect.FromLTRB(r.left, r.top, r.right, r.bottom)));
                    }
                    finally
                    {
                        element->Release();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Taskbar enumeration failed: {ex.Message}");
            }
            finally
            {
                Com.Release(ref elements);
                Com.Release(ref root);
            }
        }
    }

    /// <summary>Reads a window's Application User Model ID (empty if it has none).</summary>
    private static string GetWindowAppId(HWND hwnd)
    {
        IPropertyStore* store = null;
        try
        {
            if (PInvoke.SHGetPropertyStoreForWindow(hwnd, out store).Failed || store is null)
            {
                return string.Empty;
            }

            PROPERTYKEY key = PInvoke.PKEY_AppUserModel_ID;
            PROPVARIANT value = default;
            store->GetValue(&key, &value);

            try
            {
                Span<char> buffer = stackalloc char[512];
                fixed (char* p = buffer)
                {
                    if (PInvoke.PropVariantToString(&value, p, (uint)buffer.Length).Succeeded)
                    {
                        return new string(p);
                    }
                }
            }
            finally
            {
                _ = PInvoke.PropVariantClear(&value);
            }
        }
        catch (Exception)
        {
            // No AUMID available; the caller falls back to name matching.
        }
        finally
        {
            Com.Release(ref store);
        }

        return string.Empty;
    }

    /// <summary>Scores a button label against the active window by shared significant words (0 = no match).</summary>
    private static int MatchScore(string buttonName, ActiveWindow active)
    {
        if (buttonName.Length == 0)
        {
            return 0;
        }

        int score = 0;
        foreach (Range r in Tokens(buttonName))
        {
            ReadOnlySpan<char> word = buttonName.AsSpan(r);
            if (ContainsWord(active.Title, word) || ContainsWord(active.ProcessName, word))
            {
                score += word.Length;
            }
        }

        return score;
    }

    private static bool ContainsWord(string text, ReadOnlySpan<char> word)
    {
        foreach (Range r in Tokens(text))
        {
            if (text.AsSpan(r).Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Significant word tokens (≥3 chars, not stop words) of a string, including CamelCase parts so
    /// "WindowsTerminal" also yields "Terminal". Allocation-free apart from the result list.
    /// </summary>
    private static List<Range> Tokens(string text)
    {
        var ranges = new List<Range>(8);
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

            AddIfSignificant(ranges, text, start, i);

            int partStart = start;
            for (int j = start + 1; j < i; j++)
            {
                if (char.IsUpper(text[j]) && char.IsLower(text[j - 1]))
                {
                    AddIfSignificant(ranges, text, partStart, j);
                    partStart = j;
                }
            }

            if (partStart > start)
            {
                AddIfSignificant(ranges, text, partStart, i);
            }
        }

        return ranges;
    }

    private static void AddIfSignificant(List<Range> ranges, string text, int start, int end)
    {
        if (end - start < 3)
        {
            return;
        }

        ReadOnlySpan<char> token = text.AsSpan(start, end - start);
        foreach (string stop in StopWords)
        {
            if (token.Equals(stop, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        ranges.Add(new Range(start, end));
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '+' or '#';
}
