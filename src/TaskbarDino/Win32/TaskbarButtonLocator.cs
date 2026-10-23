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
/// Every taskbar (primary and per-monitor) is searched with one cached, Button-filtered query, so a
/// lookup is a couple of cross-process round trips rather than hundreds, and the scan allocates
/// nothing on the managed heap (it runs every few seconds for the lifetime of the process).
/// </summary>
/// <remarks>
/// Matching: the window's App User Model ID (AUMID) against each button's AutomationId
/// ("Appid: &lt;AUMID&gt;") is authoritative — it is what makes Edge PWAs, Store and pinned apps
/// work, whose titles never mention the app. Multiple instances share an AUMID, so the best
/// title-word overlap (and the button on the window's own monitor) breaks ties. Without an AUMID
/// match, shared significant words between the button label and the window title/process name
/// decide. '+' and '#' stay inside tokens so "Notepad++" ≠ "Notepad".
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

    // The AUMID of the last window asked about, so repeated polls for the same window don't re-query it.
    private HWND _appIdWindow;
    private string _appId = string.Empty;

    public bool TryGetButtonRect(ActiveWindow active, out PixelRect rect)
    {
        rect = default;
        if (!EnsureAutomation())
        {
            return false;
        }

        try
        {
            if (active.Handle != _appIdWindow)
            {
                _appIdWindow = active.Handle;
                _appId = GetWindowAppId(active.Handle);
            }

            Scan scan = Run(active, _appId);
            if (scan.AumidScore >= 0 && !scan.AumidRect.IsEmpty)
            {
                rect = scan.AumidRect;
                return true;
            }

            if (scan.NameScore > 0 && !scan.NameRect.IsEmpty)
            {
                rect = scan.NameRect;
                return true;
            }

            Log.Info($"No taskbar button for '{active.ProcessName}' \"{active.Title}\" (appId '{_appId}', {scan.Buttons} buttons seen)");
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
            Scan scan = Run(null, string.Empty);
            rect = scan.StartRect;
            return !rect.IsEmpty;
        }
        catch (Exception ex)
        {
            Log.Warn($"Start button lookup failed: {ex.Message}");
            return false;
        }
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

    /// <summary>Result of one pass over every taskbar button.</summary>
    private struct Scan
    {
        public int Buttons;
        public int AumidScore;     // -1 = no AUMID match
        public PixelRect AumidRect;
        public int NameScore;
        public PixelRect NameRect;
        public PixelRect StartRect;
    }

    /// <summary>One pass over all taskbars: scores every button against <paramref name="active"/> and spots the Start button.</summary>
    private Scan Run(ActiveWindow? active, string appId)
    {
        var scan = new Scan { AumidScore = -1 };
        nint windowMonitor = active is null ? 0 : Monitors.FromWindow(active.Handle).Handle;

        foreach (HWND taskbar in TaskbarWindows.All())
        {
            nint taskbarMonitor = Monitors.FromWindow(taskbar).Handle;
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

                    BSTR name = default, automationId = default;
                    try
                    {
                        name = element->CachedName;
                        automationId = element->CachedAutomationId;
                        RECT r = element->CachedBoundingRectangle;
                        var rect = PixelRect.FromLTRB(r.left, r.top, r.right, r.bottom);
                        scan.Buttons++;

                        ReadOnlySpan<char> id = automationId.AsSpan();
                        if (scan.StartRect.IsEmpty && id.Equals("StartButton", StringComparison.OrdinalIgnoreCase))
                        {
                            scan.StartRect = rect;
                        }

                        if (active is null)
                        {
                            continue;
                        }

                        // Windows can show the same button on every taskbar; prefer the one on the window's own monitor.
                        int score = MatchScore(name.AsSpan(), active) + (taskbarMonitor == windowMonitor ? 1 : 0);
                        if (appId.Length > 0 && id.Contains(appId, StringComparison.OrdinalIgnoreCase) && score > scan.AumidScore)
                        {
                            scan.AumidScore = score;
                            scan.AumidRect = rect;
                        }

                        if (score > scan.NameScore)
                        {
                            scan.NameScore = score;
                            scan.NameRect = rect;
                        }
                    }
                    finally
                    {
                        PInvoke.SysFreeString(name);
                        PInvoke.SysFreeString(automationId);
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

        return scan;
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

    /// <summary>Scores a button label against the active window by shared significant words (0 = no match). Allocation-free.</summary>
    private static int MatchScore(ReadOnlySpan<char> buttonName, ActiveWindow active)
    {
        int score = 0;
        var words = new TokenEnumerator(buttonName);
        while (words.MoveNext())
        {
            ReadOnlySpan<char> word = buttonName[words.Current];
            if (ContainsWord(active.Title, word) || ContainsWord(active.ProcessName, word))
            {
                score += word.Length;
            }
        }

        return score;
    }

    private static bool ContainsWord(string text, ReadOnlySpan<char> word)
    {
        var tokens = new TokenEnumerator(text);
        while (tokens.MoveNext())
        {
            if (text.AsSpan(tokens.Current).Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Yields the significant word tokens of a string (≥3 chars, not stop words) and, for CamelCase
    /// tokens, their parts too ("WindowsTerminal" → "WindowsTerminal", "Windows", "Terminal") so a
    /// process name can match a shorter taskbar label. A ref struct, so enumeration allocates nothing.
    /// </summary>
    private ref struct TokenEnumerator(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _pos;          // scan position in the text
        private int _tokenStart;   // current whole token
        private int _tokenEnd;
        private int _partStart;    // next CamelCase part to yield inside the token
        private bool _inParts;

        public Range Current { get; private set; }

        public bool MoveNext()
        {
            while (true)
            {
                if (_inParts)
                {
                    // Yield successive CamelCase parts of the current token.
                    int j = _partStart + 1;
                    while (j < _tokenEnd && !(char.IsUpper(_text[j]) && char.IsLower(_text[j - 1])))
                    {
                        j++;
                    }

                    int start = _partStart;
                    _partStart = j;
                    if (j >= _tokenEnd)
                    {
                        _inParts = false;
                        if (start == _tokenStart)
                        {
                            continue; // no split occurred: the whole token was already yielded
                        }
                    }

                    if (Significant(start, j))
                    {
                        Current = new Range(start, j);
                        return true;
                    }

                    continue;
                }

                // Next whole token.
                while (_pos < _text.Length && !IsWordChar(_text[_pos]))
                {
                    _pos++;
                }

                if (_pos >= _text.Length)
                {
                    return false;
                }

                _tokenStart = _pos;
                while (_pos < _text.Length && IsWordChar(_text[_pos]))
                {
                    _pos++;
                }

                _tokenEnd = _pos;
                _partStart = _tokenStart;
                _inParts = true;
                if (Significant(_tokenStart, _tokenEnd))
                {
                    Current = new Range(_tokenStart, _tokenEnd);
                    return true;
                }
            }
        }

        private readonly bool Significant(int start, int end)
        {
            if (end - start < 3)
            {
                return false;
            }

            ReadOnlySpan<char> token = _text[start..end];
            foreach (string stop in StopWords)
            {
                if (token.Equals(stop, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '+' or '#';
    }
}
