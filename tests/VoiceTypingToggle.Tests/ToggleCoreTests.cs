// Unit tests for the pure seams: English-layout selection and the Idle/Dictating
// state machine (ToggleCore), driven with fake system seams — no Win32 involved.

namespace VoiceTypingToggle.Tests;

public class SelectEnglishLayoutTests
{
    const nint EnUs = 0x040B0409; // en-US language id, Finnish physical keyboard klid (as on this machine)
    const nint EnGb = 0x040B0809; // en-GB language id, same klid
    const nint FiFi = 0x040B040B; // fi-FI
    const nint DeDe = 0x04070407; // de-DE

    [Fact]
    public void ExactEnUsWinsOverAnyEnglishFallback()
    {
        nint[] layouts = [EnGb, EnUs, FiFi];
        Assert.Equal(EnUs, ToggleCore.SelectEnglishLayout(layouts, layouts.Length));
    }

    [Fact]
    public void AnyEnglishPrimaryLanguageIsTheFallback()
    {
        nint[] layouts = [FiFi, EnGb];
        Assert.Equal(EnGb, ToggleCore.SelectEnglishLayout(layouts, layouts.Length));
    }

    [Fact]
    public void NoEnglishLayoutReturnsZero()
    {
        nint[] layouts = [FiFi, DeDe];
        Assert.Equal(0, ToggleCore.SelectEnglishLayout(layouts, layouts.Length));
    }
}

public class ToggleCoreTests
{
    const nint EnUs = 0x040B0409;
    const nint EnGb = 0x040B0809;
    const nint Target = 0x1234;

    // Core with no-op fakes: foreground window Target, thread 7 on en-GB, any
    // RequestLayout succeeds and switches the layout, everything else inert.
    // Returns the request log so tests can assert switch-up/restore-down order.
    static (ToggleCore Core, List<(nint hwnd, nint hkl)> Requests) NewCore()
    {
        var requests = new List<(nint hwnd, nint hkl)>();
        nint layout = EnGb;
        var core = new ToggleCore(EnUs)
        {
            GetForeground = () => Target,
            GetThreadId = _ => 7,
            GetLayout = _ => layout,
            RequestLayout = (h, hkl) => { requests.Add((h, hkl)); layout = hkl; return true; },
            SendWinH = () => { },
            SendEscape = () => { },
            RestoreFocus = _ => true,
            Sleep = _ => { },
        };
        return (core, requests);
    }

    [Fact]
    public void StartSwitchesToEnglishThenEntersDictating()
    {
        var (core, requests) = NewCore();

        core.Toggle();

        Assert.True(core.IsDictating);
        Assert.Equal(Target, core.SavedWindow);
        Assert.Equal(EnGb, core.SavedLayout); // the layout actually saved, pre-switch
        Assert.Equal([(Target, EnUs)], requests);
    }

    [Fact]
    public void FailedSwitchStaysIdleWithoutWinH()
    {
        var (core, _) = NewCore();
        core.RequestLayout = (_, _) => false;
        int winH = 0;
        core.SendWinH = () => winH++;

        core.Toggle();

        Assert.False(core.IsDictating);
        Assert.Equal(0, core.SavedLayout);
        Assert.Equal(0, winH);
    }

    [Fact]
    public void StopRestoresSavedLayoutAndFocus()
    {
        var (core, requests) = NewCore();
        int escapes = 0;
        nint? focusTarget = null;
        var restoreOrder = new List<string>();
        var requestLayout = core.RequestLayout;
        core.RequestLayout = (h, hkl) =>
        {
            if (hkl == EnGb)
            {
                restoreOrder.Add("layout");
            }
            return requestLayout(h, hkl);
        };
        var fg = new Queue<nint>([Target, Target, Target, 0]); // start guard, stop guard, escape-retry check, then bar-close drop to 0
        core.GetForeground = () => fg.Dequeue();
        core.SendEscape = () => escapes++;
        core.RestoreFocus = h => { restoreOrder.Add("focus"); focusTarget = h; return true; };

        core.Toggle(); // start
        core.Toggle(); // stop

        Assert.False(core.IsDictating);
        Assert.Equal(0, core.SavedLayout);
        Assert.Equal(2, escapes); // first close + retry while the bar was still there
        Assert.Equal(Target, focusTarget);
        Assert.Equal(["focus", "layout"], restoreOrder);
        Assert.Equal([(Target, EnUs), (Target, EnGb)], requests); // switch up, restore down
    }

    [Fact]
    public void StopSleepsTheEscapeSettle()
    {
        var (core, _) = NewCore();
        var sleeps = new List<int>();
        core.Sleep = sleeps.Add;
        core.GetForeground = () => Target; // stays on the saved window: retry heuristic fires like the accepted stop path

        core.Toggle(); // start
        core.Toggle(); // stop

        Assert.Equal([30], sleeps); // stop-flash: tuned settle (20/15/10 reopen the bar)
    }

    [Fact]
    public void StopArmsWatchdogAndExpiryClearsIt()
    {
        var (core, _) = NewCore();

        core.Toggle(); // start
        core.Toggle(); // stop
        Assert.True(core.StopConfirmPending);

        for (int i = 0; i < 10; i++)
        {
            core.CheckDictationFocus();
        }
        Assert.False(core.StopConfirmPending);
    }

    [Fact]
    public void VoiceUiShowWhileStopPendingRunsCorrectivePass()
    {
        var (core, requests) = NewCore();
        int escapes = 0;
        var focusCalls = new List<nint>();
        var sleeps = new List<int>();
        core.SendEscape = () => escapes++;
        core.RestoreFocus = h => { focusCalls.Add(h); return true; };
        core.Sleep = sleeps.Add;
        var fg = new Queue<nint>([Target, Target, Target, 0]); // start guard, stop guard, stop retry check, corrective retry check (candidate raised)
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start
        core.Toggle(); // stop: 2 escapes (first + retry), focus+layout restored once
        Assert.Equal(2, escapes);
        Assert.Equal([Target], focusCalls);
        Assert.Equal([(Target, EnUs), (Target, EnGb)], requests);
        Assert.Equal([30], sleeps); // fast settle on the normal stop

        core.OnVoiceUiShown(); // the bar reopened: corrective pass

        Assert.Equal(3, escapes); // corrective Escape only (retry check saw the candidate)
        Assert.Equal([Target, Target], focusCalls); // focus restored again
        Assert.Equal([30, 100], sleeps); // corrective pass uses the safe settle
        Assert.True(core.StopConfirmPending); // still armed, ticks reset
    }

    [Fact]
    public void HealDuringEstablishedDictationDoesNotArmWatchdog()
    {
        var (core, _) = NewCore();
        core.IsVoiceUiVisible = () => true;
        var fg = new Queue<nint>([Target, Target, 0xABCD]); // start guard, tick-1 heal check (still on target), tick-2 heal check (left)
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start
        core.CheckDictationFocus(); // timer: bar confirmed -> established dictation
        Assert.False(core.WaitingForBar);

        core.CheckDictationFocus(); // focus left -> heal

        Assert.False(core.IsDictating);
        Assert.False(core.StopConfirmPending); // no watchdog: pointer re-shows must not fire Escapes mid-session
    }

    [Fact]
    public void VoiceUiShowIgnoredWhenNoStopPending()
    {
        var (core, _) = NewCore();
        int escapes = 0;
        core.SendEscape = () => escapes++;
        var fg = new Queue<nint>([Target]);
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start only; nothing pending
        core.OnVoiceUiShown();

        Assert.Equal(0, escapes);
    }

    [Fact]
    public void VoiceUiShowAfterNewStartIsIgnored()
    {
        var (core, _) = NewCore();
        int escapes = 0;
        core.SendEscape = () => escapes++;
        var fg = new Queue<nint>([Target, Target, 0, Target]); // start, stop retry check, corrective retry check, second start
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start
        core.Toggle(); // stop
        core.Toggle(); // user starts a new dictation: pending cleared
        Assert.False(core.StopConfirmPending);
        int before = escapes;

        core.OnVoiceUiShown(); // the new dictation's own popup show

        Assert.Equal(before, escapes); // must not kill the user's new dictation
    }

    [Fact]
    public void VoiceUiShowCorrectionIsBounded()
    {
        var (core, _) = NewCore();
        int escapes = 0;
        core.SendEscape = () => escapes++;
        var fg = new Queue<nint>([Target, Target, 0, 0, 0]);
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start
        core.Toggle(); // stop
        int afterStop = escapes; // 2

        core.OnVoiceUiShown(); // correction 1
        core.OnVoiceUiShown(); // correction 2
        Assert.Equal(afterStop + 2, escapes);
        core.OnVoiceUiShown(); // bound reached: pending cleared, no more corrections
        Assert.False(core.StopConfirmPending);
        Assert.Equal(afterStop + 2, escapes);
    }

    [Fact]
    public void StartConfirmsDictatingWhenBarAppears()
    {
        var (core, _) = NewCore();
        core.IsVoiceUiVisible = () => true;

        core.Toggle(); // start
        Assert.True(core.WaitingForBar);
        Assert.True(core.IsDictating);

        core.CheckDictationFocus(); // timer: bar visible -> launch confirmed

        Assert.False(core.WaitingForBar);
        Assert.True(core.IsDictating);
    }

    [Fact]
    public void StartRemainsDictatingWhenTransientPopupNeverAppears()
    {
        var (core, requests) = NewCore();
        core.IsVoiceUiVisible = () => false; // bar never shows

        core.Toggle(); // start: English switch ok, Win+H sent, bar never confirms
        Assert.True(core.WaitingForBar);
        for (int i = 0; i < 8; i++)
        {
            core.CheckDictationFocus();
        }

        Assert.True(core.IsDictating);
        Assert.False(core.WaitingForBar);
        Assert.Equal(Target, core.SavedWindow);
        Assert.Equal(EnGb, core.SavedLayout);
        Assert.False(core.StopConfirmPending);
        Assert.Equal([(Target, EnUs)], requests); // keep English active until an explicit stop/heal
    }

    [Fact]
    public void StopDuringBarLaunchRunsStopPathAndArmsWatchdog()
    {
        var (core, _) = NewCore();
        int escapes = 0;
        core.SendEscape = () => escapes++;
        var fg = new Queue<nint>([Target, Target, Target]); // start guard, stop guard, retry check
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start (bar still launching)
        Assert.True(core.WaitingForBar);
        core.Toggle(); // stop before the bar showed

        Assert.False(core.IsDictating);
        Assert.False(core.WaitingForBar);
        Assert.Equal(2, escapes); // first + retry (fg stayed on the saved window)
        Assert.True(core.StopConfirmPending); // watchdog catches the late bar
    }

    [Fact]
    public void FocusLeavingDictationTargetRestoresAndGoesIdle()
    {
        var (core, requests) = NewCore();
        var fg = new Queue<nint>([Target, 0]);
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start dictating on Target
        Assert.True(core.IsDictating);

        fg.Enqueue(0xABCD); // user switched apps; the bar auto-closes
        core.CheckDictationFocus();

        Assert.False(core.IsDictating);
        Assert.Equal(0, core.SavedLayout);
        Assert.Equal([(Target, EnUs), (Target, EnGb)], requests); // healed: layout restored
    }

    [Fact]
    public void FocusHealReappliesSavedLayoutWhenOriginalWindowReturns()
    {
        var requests = new List<(nint hwnd, nint hkl)>();
        nint layout = EnGb;
        nint foreground = Target;
        var core = new ToggleCore(EnUs)
        {
            GetForeground = () => foreground,
            GetThreadId = _ => 7,
            GetLayout = _ => layout,
            RequestLayout = (h, hkl) => { requests.Add((h, hkl)); layout = hkl; return true; },
            SendWinH = () => { },
            RestoreFocus = _ => true,
            Sleep = _ => { },
        };

        core.Toggle(); // start on Target: en-GB -> en-US
        foreground = 0xABCD;
        core.CheckDictationFocus(); // heal sends a best-effort restore to the background Target
        Assert.False(core.IsDictating);
        Assert.Equal(EnGb, layout);

        layout = EnUs; // model VS Code reinitializing its input context on refocus
        foreground = Target;
        core.CheckDictationFocus();

        Assert.Equal(EnGb, layout);
        Assert.Equal([(Target, EnUs), (Target, EnGb), (Target, EnGb)], requests);
    }

    [Fact]
    public void OptInFocusHealRestoresTemporaryEnglishOnNewForegroundWindow()
    {
        const nint other = 0xABCD;
        var layouts = new Dictionary<uint, nint> { [7] = EnGb, [8] = EnUs };
        var requests = new List<(nint hwnd, nint hkl)>();
        nint foreground = Target;
        var core = new ToggleCore(EnUs)
        {
            RestoreFocusedLayoutOnFocusLoss = true,
            GetForeground = () => foreground,
            GetThreadId = h => h == Target ? 7u : 8u,
            GetLayout = tid => layouts[tid],
            RequestLayout = (h, hkl) =>
            {
                requests.Add((h, hkl));
                layouts[h == Target ? 7u : 8u] = hkl;
                return true;
            },
            SendWinH = () => { },
            RestoreFocus = _ => true,
            Sleep = _ => { },
        };

        core.Toggle();
        foreground = other;
        core.CheckDictationFocus();

        Assert.False(core.IsDictating);
        Assert.Equal(EnGb, layouts[7]);
        Assert.Equal(EnGb, layouts[8]);
        Assert.Equal([(Target, EnUs), (Target, EnGb), (other, EnGb)], requests);
    }

    [Fact]
    public void StopRetriesUnconfirmedLayoutRestoreOnce()
    {
        var (core, requests) = NewCore();
        nint layout = EnGb;
        int restoreAttempts = 0;
        core.GetLayout = _ => layout;
        core.RequestLayout = (h, hkl) =>
        {
            requests.Add((h, hkl));
            if (hkl == EnUs || ++restoreAttempts == 2)
            {
                layout = hkl;
            }
            return true;
        };
        var fg = new Queue<nint>([Target, Target, Target, 0]);
        core.GetForeground = () => fg.Dequeue();

        core.Toggle();
        core.Toggle();

        Assert.Equal(2, restoreAttempts);
        Assert.Equal([(Target, EnUs), (Target, EnGb), (Target, EnGb)], requests);
        Assert.Equal(EnGb, layout);
    }
}
