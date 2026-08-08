// Pure toggle state machine, free of Win32 calls: every system access goes
// through an injected seam so tests can drive transitions with fakes. Program
// wires the real P/Invokes; the tests wire fakes.
internal sealed class ToggleCore
{
    const int PollIntervalMs = 10;   // T5: measured switches complete in <1 ms; 10 ms keeps polling cheap
    const int SwitchTimeoutMs = 100; // T5: 100x margin over observed <1 ms switches; unhonored apps never switch
    const uint LangEnUs = 0x0409;
    const int EscapeRetryMs = 100;      // T7: Escape needs ~100 ms settle before the bar's close moves focus

    public nint EnglishLayout { get; }
    public bool IsDictating { get; private set; }
    public nint SavedLayout { get; private set; }
    public nint SavedWindow { get; private set; }

    public Func<nint> GetForeground { get; set; } = static () => 0;
    public Func<nint, uint> GetThreadId { get; set; } = static _ => 0;
    public Func<uint, nint> GetLayout { get; set; } = static _ => 0;
    public Func<nint, nint, bool> RequestLayout { get; set; } = static (_, _) => false;
    public Action SendWinH { get; set; } = static () => { };
    public Action SendEscape { get; set; } = static () => { };
    public Func<nint, bool> RestoreFocus { get; set; } = static _ => false;
    public Action<int> Sleep { get; set; } = static _ => { };

    public ToggleCore(nint englishLayout) => EnglishLayout = englishLayout;

    public void Toggle()
    {
        nint hwnd = GetForeground();
        if (hwnd == 0)
        {
            return;
        }
        if (IsDictating)
        {
            StopDictation();
        }
        else
        {
            StartDictation(hwnd);
        }
    }

    public void StartDictation(nint hwnd)
    {
        uint tid = GetThreadId(hwnd);
        nint current = GetLayout(tid);

        // Fail closed: only start voice typing after the English layout is confirmed active.
        if (current != EnglishLayout &&
            (!RequestLayout(hwnd, EnglishLayout) || !WaitForLayout(tid, EnglishLayout, SwitchTimeoutMs)))
        {
            return; // stay Idle
        }
        SavedLayout = current;
        SavedWindow = hwnd;
        IsDictating = true;
        SendWinH();
    }

    public void StopDictation()
    {
        SendEscape();
        Sleep(EscapeRetryMs);
        // Retry only if the bar is still there: when Escape #1 worked, the bar's close
        // moves foreground away from the saved window — a second Escape would then hit
        // whatever the shell raised (an unrelated app).
        if (GetForeground() == SavedWindow)
        {
            SendEscape(); // apps like terminals consume the first Escape as a control char
        }
        // Do not wait for the bar-close foreground drop: RestoreFocus attaches to
        // whichever shell candidate appears and reclaims the saved window directly.
        if (SavedWindow != 0)
        {
            _ = RestoreFocus(SavedWindow);
        }
        RestoreLayout(SavedWindow, SavedLayout);
        SavedLayout = 0;
        SavedWindow = 0;
        IsDictating = false;
    }

    // Self-healing: the Voice Typing bar auto-closes on any focus change, so once
    // focus leaves the dictation target the state is stale — restore and go Idle.
    public void CheckDictationFocus()
    {
        if (IsDictating && GetForeground() != SavedWindow)
        {
            RestoreIfDictating();
        }
    }

    public void RestoreIfDictating()
    {
        if (!IsDictating)
        {
            return;
        }
        nint target = SavedWindow != 0 ? SavedWindow : GetForeground();
        RestoreLayout(target, SavedLayout);
        SavedLayout = 0;
        SavedWindow = 0;
        IsDictating = false;
    }

    bool WaitForLayout(uint tid, nint expected, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (GetLayout(tid) == expected)
            {
                return true;
            }
            Sleep(PollIntervalMs);
        }
        return false;
    }

    void RestoreLayout(nint target, nint expected)
    {
        if (target == 0 || expected == 0)
        {
            return;
        }
        uint tid = GetThreadId(target);
        if (GetLayout(tid) == expected)
        {
            return;
        }
        if (RequestLayout(target, expected) && WaitForLayout(tid, expected, SwitchTimeoutMs))
        {
            return;
        }
        _ = RequestLayout(target, expected); // one retry for input-context reinitialization
        _ = WaitForLayout(tid, expected, SwitchTimeoutMs);
    }

    // Pure selection logic: exact en-US first, then any English primary language.
    public static nint SelectEnglishLayout(nint[] layouts, int count)
    {
        nint fallback = 0;
        for (int i = 0; i < count; i++)
        {
            uint lang = (uint)layouts[i] & 0xFFFF;
            if (lang == LangEnUs)
            {
                return layouts[i];
            }
            if ((lang & 0xFF) == 0x09 && fallback == 0)
            {
                fallback = layouts[i];
            }
        }
        return fallback;
    }
}
