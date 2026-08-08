// Pure toggle state machine, free of Win32 calls: every system access goes
// through an injected seam so tests can drive transitions with fakes. Program
// wires the real P/Invokes; the tests wire fakes.
internal sealed class ToggleCore
{
    const int PollIntervalMs = 10;   // T5: measured switches complete in <1 ms; 10 ms keeps polling cheap
    const int SwitchTimeoutMs = 100; // T5: 100x margin over observed <1 ms switches; unhonored apps never switch
    const uint LangEnUs = 0x0409;
    const int EscapeRetryMs = 30;          // stop-flash: Escape settle; restore lands ~+31-47 ms (tuned; 20/15/10 reopen the bar), see .tmp/stop-flash-findings.md
    const int StopConfirmEscapeRetryMs = 100; // stop-flash watchdog: corrective passes use the proven-safe settle — reliability over speed, the flash already happened
    const int StopConfirmMaxCorrections = 2; // stop-flash watchdog: bounded corrective passes per stop
    const int StopConfirmTimeoutTicks = 10;  // ~2.5 s at the 250 ms focus-watch cadence (covers the slow bar launch)
    const int BarWaitTimeoutTicks = 8;       // ~2 s at the 250 ms cadence: bound for the bar to appear after Win+H

    public nint EnglishLayout { get; }
    public bool IsDictating { get; private set; }
    public bool StopConfirmPending { get; private set; } // watchdog: bar may have reopened after stop; corrective pass armed
    public bool WaitingForBar { get; private set; }       // launch not yet confirmed: the bar has not shown after Win+H
    public nint SavedLayout { get; private set; }
    public nint SavedWindow { get; private set; }

    public Func<nint> GetForeground { get; set; } = static () => 0;
    public Func<nint, uint> GetThreadId { get; set; } = static _ => 0;
    public Func<uint, nint> GetLayout { get; set; } = static _ => 0;
    public Func<nint, nint, bool> RequestLayout { get; set; } = static (_, _) => false;
    public Action SendWinH { get; set; } = static () => { };
    public Action SendEscape { get; set; } = static () => { };
    public Func<nint, bool> RestoreFocus { get; set; } = static _ => false;
    public Func<bool> IsVoiceUiVisible { get; set; } = static () => false; // stop-flash: bar window shown yet? (timer-confirmed launch)
    public Action<int> Sleep { get; set; } = static _ => { };

    // Watchdog state: the stop sequence may have raced the bar's variable
    // teardown, letting it reopen and listen again; Program reports TextInputHost
    // popup shows via OnVoiceUiShown while a stop is pending.
    nint stopConfirmWindow;
    nint stopConfirmLayout;
    int stopConfirmCorrections;
    int stopConfirmTicksLeft;
    int barWaitTicksLeft;

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
        StopConfirmPending = false; // a new dictation supersedes any pending stop confirmation
        WaitingForBar = true;       // confirm the bar appeared via the 250 ms timer; never block the message loop
        barWaitTicksLeft = BarWaitTimeoutTicks;
        SendWinH();
    }

    public void StopDictation()
    {
        ArmStopConfirm();
        WaitingForBar = false;
        RunStopSequence(corrective: false);
        SavedLayout = 0;
        SavedWindow = 0;
        IsDictating = false;
    }

    void ArmStopConfirm()
    {
        StopConfirmPending = true;
        stopConfirmWindow = SavedWindow;
        stopConfirmLayout = SavedLayout;
        stopConfirmCorrections = StopConfirmMaxCorrections;
        stopConfirmTicksLeft = StopConfirmTimeoutTicks;
    }

    // The Escape/settle/retry/restore body shared by the normal stop and the
    // watchdog's corrective passes. Uses the stopConfirm* snapshot so a later
    // start cannot disturb a pending correction.
    void RunStopSequence(bool corrective)
    {
        SendEscape();
        Sleep(corrective ? StopConfirmEscapeRetryMs : EscapeRetryMs);
        // Retry only if the bar is still there: when Escape #1 worked, the bar's close
        // moves foreground away from the saved window — a second Escape would then hit
        // whatever the shell raised (an unrelated app).
        if (GetForeground() == stopConfirmWindow)
        {
            SendEscape(); // apps like terminals consume the first Escape as a control char
        }
        // Do not wait for the bar-close foreground drop: RestoreFocus attaches to
        // whichever shell candidate appears and reclaims the saved window directly.
        if (stopConfirmWindow != 0)
        {
            _ = RestoreFocus(stopConfirmWindow);
        }
        RestoreLayout(stopConfirmWindow, stopConfirmLayout);
    }

    // stop-flash watchdog: the bar reopened while our stop was pending.
    // Re-close and re-restore (bounded); ignore shows once the pending window
    // expires or a new dictation starts.
    public void OnVoiceUiShown()
    {
        // Hard invariant: never correct while a dictation is active — the
        // "Listening..." pointer re-shows mid-dictation on a ~5 s cadence and
        // must not trigger Escapes.
        if (!StopConfirmPending || IsDictating)
        {
            return;
        }
        if (stopConfirmCorrections-- <= 0)
        {
            StopConfirmPending = false;
            return;
        }
        RunStopSequence(corrective: true); // safe settle: a racy correction would just reopen the bar again
        stopConfirmTicksLeft = StopConfirmTimeoutTicks; // fresh expiry window for any further reopen
    }

    // Self-healing: the Voice Typing bar auto-closes on any focus change, so once
    // focus leaves the dictation target the state is stale — restore and go Idle.
    // Also confirms the bar launch (timer-based, non-blocking) and expires the
    // stop-flash watchdog when no reopen appears in time.
    public void CheckDictationFocus()
    {
        if (IsDictating && GetForeground() != SavedWindow)
        {
            RestoreIfDictating();
        }
        if (WaitingForBar)
        {
            if (IsVoiceUiVisible())
            {
                WaitingForBar = false; // launch confirmed: the bar is up
            }
            else if (--barWaitTicksLeft <= 0)
            {
                AbortStart(); // no bar in time: fail closed, watchdog catches a very late one
            }
        }
        if (StopConfirmPending && --stopConfirmTicksLeft <= 0)
        {
            StopConfirmPending = false;
        }
    }

    void AbortStart()
    {
        WaitingForBar = false;
        RestoreLayout(SavedWindow, SavedLayout);
        ArmStopConfirm();
        SavedLayout = 0;
        SavedWindow = 0;
        IsDictating = false;
    }

    public void RestoreIfDictating()
    {
        if (!IsDictating)
        {
            return;
        }
        bool wasWaitingForBar = WaitingForBar;
        nint target = SavedWindow != 0 ? SavedWindow : GetForeground();
        RestoreLayout(target, SavedLayout);
        WaitingForBar = false;
        if (wasWaitingForBar)
        {
            // Launch race only: a bar that shows late must still be closed.
            // During established dictation a focus change closes the bar itself;
            // arming here would let the routine pointer re-show trigger Escapes.
            ArmStopConfirm();
        }
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
