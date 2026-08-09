// Unit tests for the pure seams: English-layout selection and the Idle/Dictating
// state machine (ToggleCore), driven with fake system seams — no Win32 involved.

namespace VoiceTypingToggle.Tests;

public class DiagnosticTraceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    public void DisabledSettingsHaveNoTracePath(string? setting)
    {
        Assert.Null(DiagnosticTrace.ResolvePath(setting, Path.GetTempPath()));
    }

    [Fact]
    public void OneUsesTheLocalAppDataTracePath()
    {
        string localAppData = Path.Combine(Path.GetTempPath(), "local-app-data");

        string? path = DiagnosticTrace.ResolvePath("1", localAppData);

        Assert.Equal(Path.GetFullPath(Path.Combine(localAppData, "VoiceTypingToggle", "trace.csv")), path);
    }

    [Fact]
    public void ExplicitTracePathIsPreservedAsAnAbsolutePath()
    {
        string requested = Path.Combine(Path.GetTempPath(), "custom-vtt-trace.csv");

        string? path = DiagnosticTrace.ResolvePath(requested, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(requested), path);
    }

    [Fact]
    public void EnabledTraceWritesHeaderAndMetadataRow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vtt-trace-{Guid.NewGuid():N}.csv");
        try
        {
            using (DiagnosticTrace trace = DiagnosticTrace.Create(path, Path.GetTempPath()))
            {
                Assert.True(trace.Enabled);
                trace.Write(123, "test-event", 0x1234, 7, 0x040B040B, true, false, true);
                trace.Flush();
            }

            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Equal("tick,event,foreground,foregroundTid,foregroundHkl,isDictating,waitingForBar,stopConfirmPending", lines[0]);
            Assert.Equal("123,test-event,0x1234,7,0x40B040B,True,False,True", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class SelectEnglishLayoutTests
{
    const nint EnUs = 0x040B0409; // en-US language id, Finnish physical keyboard klid (as on this machine)
    const nint EnUsUsKbd = 0x04090409; // en-US language id, US physical keyboard klid
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

    [Fact]
    public void PreferredKlidWinsOverOtherExactEnUs()
    {
        nint[] layouts = [EnUsUsKbd, EnUs, FiFi];
        Assert.Equal(EnUs, ToggleCore.SelectEnglishLayout(layouts, layouts.Length, 0x040B0000));
    }

    [Fact]
    public void PreferredKlidWithNoMatchFallsBackToGeneralSelection()
    {
        nint[] layouts = [EnUsUsKbd, EnUs, FiFi];
        Assert.Equal(EnUsUsKbd, ToggleCore.SelectEnglishLayout(layouts, layouts.Length, 0x04070000)); // de-DE klid: no en-US on it
    }
}

public class ShutdownDecisionTests
{
    [Fact]
    public void PendingStopKeepsShutdownWaiting()
    {
        var decision = new ShutdownDecision();
        Assert.Equal(ShutdownAction.Wait, decision.Begin(ShutdownKind.UserExit, false, true));
        Assert.Equal(ShutdownAction.Wait, decision.Advance(false, true, false));
    }

    [Fact]
    public void StableStopPermitsTeardown()
    {
        var decision = new ShutdownDecision();
        decision.Begin(ShutdownKind.UserExit, true, false);
        Assert.Equal(ShutdownAction.Complete, decision.Advance(false, false, false));
    }

    [Fact]
    public void UnconfirmedUserExitCancelsAfterBoundedCorrections()
    {
        var decision = new ShutdownDecision();
        decision.Begin(ShutdownKind.UserExit, true, false);
        Assert.Equal(ShutdownAction.Correct, decision.Advance(false, false, true));
        Assert.Equal(ShutdownAction.Correct, decision.Advance(false, false, true));
        Assert.Equal(ShutdownAction.CancelUserExit, decision.Advance(false, false, true));
        decision.Cancel(); // the coordinator cancels only with the tray icon installed again
        Assert.Null(decision.Kind);
    }

    [Fact]
    public void ExplorerRestartDuringUserExitUpgradesToFatalShutdown()
    {
        var decision = new ShutdownDecision();
        decision.Begin(ShutdownKind.UserExit, true, false); // user Exit while dictating
        decision.Advance(false, false, true);               // bar still visible after watchdog expiry

        // Explorer restarts mid-drain and the icon cannot be recreated: the
        // coordinator escalates instead of letting the Exit cancel later.
        Assert.Equal(ShutdownAction.Wait, decision.Begin(ShutdownKind.FatalTrayLoss, false, false));
        Assert.Equal(ShutdownKind.FatalTrayLoss, decision.Kind);

        // The drain continues with a fresh budget and now fails closed.
        Assert.Equal(ShutdownAction.Correct, decision.Advance(false, false, true));
        Assert.Equal(ShutdownAction.Correct, decision.Advance(false, false, true));
        Assert.Equal(ShutdownAction.ForceFatalShutdown, decision.Advance(false, false, true));
    }

    [Fact]
    public void FatalTrayLossForcesShutdownAfterBoundedCorrections()
    {
        var decision = new ShutdownDecision();
        decision.Begin(ShutdownKind.FatalTrayLoss, true, false);
        decision.Advance(false, false, true);
        decision.Advance(false, false, true);
        Assert.Equal(ShutdownAction.ForceFatalShutdown, decision.Advance(false, false, true));
    }
}

public class ToggleCoreTests
{
    const nint EnUs = 0x040B0409;
    const nint EnUsUsKbd = 0x04090409;
    const nint FiFi = 0x040B040B; // fi-FI: the non-English starting layout of the fixture
    const nint Target = 0x1234;

    // Core with no-op fakes: foreground window Target, thread 7 on en-GB, any
    // RequestLayout succeeds and switches the layout, everything else inert.
    // Returns the request log so tests can assert switch-up/restore-down order.
    static (ToggleCore Core, List<(nint hwnd, nint hkl)> Requests) NewCore()
    {
        var requests = new List<(nint hwnd, nint hkl)>();
        nint layout = FiFi;
        var core = new ToggleCore(EnUs)
        {
            GetForeground = () => Target,
            GetThreadId = _ => 7,
            GetLayout = _ => layout,
            RequestLayout = (h, hkl) => { requests.Add((h, hkl)); layout = hkl; return true; },
            RequestLayoutBounded = (h, hkl) => { requests.Add((h, hkl)); layout = hkl; return true; },
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
        Assert.Equal(FiFi, core.SavedLayout); // the layout actually saved, pre-switch
        Assert.Equal([(Target, EnUs)], requests);
    }

    [Fact]
    public void RaceStartSkipsRequestWhenEnglishVariantAlreadyActive()
    {
        var (core, requests) = NewCore();
        core.GetLayout = _ => EnUsUsKbd; // en-US on the US keyboard, not the core's EnglishLayout

        core.StartDictationRace(Target);

        Assert.True(core.IsDictating);
        Assert.Equal(EnUsUsKbd, core.SavedLayout); // the actual layout is saved and later restored
        Assert.Empty(requests); // no switch: any English primary language suffices
    }

    [Fact]
    public void StartSkipsRequestWhenEnglishVariantAlreadyActive()
    {
        var (core, requests) = NewCore();
        core.GetLayout = _ => EnUsUsKbd;

        core.Toggle();

        Assert.True(core.IsDictating);
        Assert.Equal(EnUsUsKbd, core.SavedLayout);
        Assert.Empty(requests);
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
            if (hkl == FiFi)
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
        Assert.Equal([(Target, EnUs), (Target, FiFi)], requests); // switch up, restore down
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
    public void CorrectPendingStopRunsCanonicalSavedStopCorrection()
    {
        var (core, requests) = NewCore();
        int escapes = 0;
        var focusCalls = new List<nint>();
        var sleeps = new List<int>();
        core.SendEscape = () => escapes++;
        core.RestoreFocus = h => { focusCalls.Add(h); return true; };
        core.Sleep = sleeps.Add;
        var fg = new Queue<nint>([Target, Target, Target, 0]); // start guard, stop guard, stop retry check, corrective retry check
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start
        core.Toggle(); // stop: snapshot armed, 2 escapes
        Assert.Equal(2, escapes);

        core.CorrectPendingStop(); // shutdown drain: bar still visible

        Assert.Equal(3, escapes); // corrective Escape only (retry check saw the candidate)
        Assert.Equal([Target, Target], focusCalls); // focus restored via the snapshot
        Assert.Equal([30, 100], sleeps); // fast stop settle, then the corrective safe settle
        Assert.Equal([(Target, EnUs), (Target, FiFi)], requests); // layout restored via the snapshot
    }

    [Fact]
    public void CorrectPendingStopIgnoredWhileDictating()
    {
        var (core, _) = NewCore();
        int escapes = 0;
        core.SendEscape = () => escapes++;
        var fg = new Queue<nint>([Target]);
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start only; no stop snapshot, dictating must never be corrected
        core.CorrectPendingStop();

        Assert.Equal(0, escapes);
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
        Assert.Equal([(Target, EnUs), (Target, FiFi)], requests);
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
        Assert.Equal(FiFi, core.SavedLayout);
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
        Assert.Equal([(Target, EnUs), (Target, FiFi)], requests); // healed: layout restored
    }

    [Fact]
    public void FocusHealReappliesSavedLayoutWhenOriginalWindowReturns()
    {
        var requests = new List<(nint hwnd, nint hkl)>();
        nint layout = FiFi;
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
        Assert.Equal(FiFi, layout);

        layout = EnUs; // model VS Code reinitializing its input context on refocus
        foreground = Target;
        core.CheckDictationFocus();

        Assert.Equal(FiFi, layout);
        Assert.Equal([(Target, EnUs), (Target, FiFi), (Target, FiFi)], requests);
    }

    [Fact]
    public void OptInFocusHealRestoresTemporaryEnglishOnNewForegroundWindow()
    {
        const nint other = 0xABCD;
        var layouts = new Dictionary<uint, nint> { [7] = FiFi, [8] = EnUs };
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
        Assert.Equal(FiFi, layouts[7]);
        Assert.Equal(FiFi, layouts[8]);
        Assert.Equal([(Target, EnUs), (Target, FiFi), (other, FiFi)], requests);
    }

    [Fact]
    public void RaceStartSwitchesViaBoundedSeamAndSkipsWinH()
    {
        var (core, requests) = NewCore();
        core.RequestLayout = (_, _) => throw new InvalidOperationException("race-start must use the bounded seam");
        int winH = 0;
        core.SendWinH = () => winH++;

        core.StartDictationRace(Target);

        Assert.True(core.IsDictating);
        Assert.Equal(Target, core.SavedWindow);
        Assert.Equal(FiFi, core.SavedLayout); // the layout actually saved, pre-switch
        Assert.Equal(0, winH); // no injected Win+H: the native press opens the bar
        Assert.Equal([(Target, EnUs)], requests); // switched via the bounded seam
    }

    [Fact]
    public void RaceStartLayoutFailureStaysIdleWithoutWinHOrSession()
    {
        var (core, _) = NewCore();
        core.RequestLayoutBounded = (_, _) => false;
        int winH = 0;
        core.SendWinH = () => winH++;

        core.StartDictationRace(Target);

        Assert.False(core.IsDictating);
        Assert.Equal(0, core.SavedLayout);
        Assert.Equal(0, core.SavedWindow);
        Assert.Equal(0, winH);
    }

    [Fact]
    public void RaceStartSkipsRequestWhenEnglishAlreadyActive()
    {
        var (core, requests) = NewCore();
        core.GetLayout = _ => EnUs;

        core.StartDictationRace(Target);

        Assert.True(core.IsDictating);
        Assert.Equal(Target, core.SavedWindow);
        Assert.Equal(EnUs, core.SavedLayout);
        Assert.Empty(requests);
    }

    [Fact]
    public void NativeStopArmsWatchdogWithoutEscapeAndDefersRestore()
    {
        var (core, requests) = NewCore();
        int escapes = 0;
        var focusCalls = new List<nint>();
        core.SendEscape = () => escapes++;
        core.RestoreFocus = h => { focusCalls.Add(h); return true; };

        core.Toggle(); // start (injected path)
        core.StopDictationNative();

        Assert.False(core.IsDictating);
        Assert.True(core.StopConfirmPending);
        Assert.Equal(0, escapes); // no Escape before the chained physical event
        Assert.Empty(focusCalls); // restoration deferred, not synchronous
        Assert.Equal([(Target, EnUs)], requests);

        core.CheckDictationFocus(); // tick 1: settle still in progress (two-tick settle)
        Assert.Empty(focusCalls);
        Assert.Equal([(Target, EnUs)], requests);

        core.CheckDictationFocus(); // tick 2: deferred restore after the native-close settle

        Assert.Equal([Target], focusCalls);
        Assert.Equal([(Target, EnUs), (Target, FiFi)], requests); // layout restored via the snapshot
        Assert.False(core.IsDictating);
    }

    [Fact]
    public void NativeStopRestoreDoesNotFireOnImmediatelyFollowingTick()
    {
        var (core, _) = NewCore();
        var focusCalls = new List<nint>();
        core.RestoreFocus = h => { focusCalls.Add(h); return true; };

        core.Toggle();
        core.StopDictationNative();
        core.CheckDictationFocus(); // timer callback arrives immediately after arming

        Assert.Empty(focusCalls); // one callback must not be treated as one full settle interval
    }

    [Fact]
    public void StartAfterNativeStopCancelsPendingRestoreSameWindow()
    {
        var (core, requests) = NewCore();
        int escapes = 0;
        var focusCalls = new List<nint>();
        core.SendEscape = () => escapes++;
        core.RestoreFocus = h => { focusCalls.Add(h); return true; };

        core.Toggle();              // session A
        core.StopDictationNative(); // pending native-stop restore armed
        core.Toggle();              // session B on the same window before any tick
        core.CheckDictationFocus(); // stale restore must not fire during session B

        Assert.True(core.IsDictating);
        Assert.Empty(focusCalls);
        Assert.Equal([(Target, EnUs)], requests); // no stale FiFi restore
    }

    [Fact]
    public void StartOnOtherWindowAfterNativeStopCancelsPendingRestore()
    {
        const nint other = 0xABCD;
        var (core, requests) = NewCore();
        var focusCalls = new List<nint>();
        nint foreground = Target;
        core.GetForeground = () => foreground;
        core.RestoreFocus = h => { focusCalls.Add(h); return true; };

        core.Toggle();               // session A on Target
        core.StopDictationNative();
        foreground = other;
        core.Toggle();               // session B on other, before any tick
        core.CheckDictationFocus();

        Assert.True(core.IsDictating);
        Assert.Equal(other, core.SavedWindow);
        Assert.Empty(focusCalls); // stale restore must not pull focus back to the old target
        Assert.Equal([(Target, EnUs)], requests); // no stale FiFi restore; session B found English already active
    }

    [Fact]
    public void NativeStopCorrectsWhenPopupVisibleWhilePending()
    {
        var (core, requests) = NewCore();
        int escapes = 0;
        var focusCalls = new List<nint>();
        var sleeps = new List<int>();
        var visible = new Queue<bool>([true, false]); // positive evidence on tick 1 only
        core.IsVoiceUiVisible = () => visible.Dequeue();
        core.SendEscape = () => escapes++;
        core.RestoreFocus = h => { focusCalls.Add(h); return true; };
        core.Sleep = sleeps.Add;
        var fg = new Queue<nint>([Target, Target, 0, Target]); // start guard, tick-1 focus-watch read, corrective retry check (candidate raised), tick-2 focus-watch read
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start
        core.StopDictationNative();
        core.CheckDictationFocus(); // tick 1: popup visible -> bounded corrective pass

        Assert.Equal(1, escapes); // corrective Escape only (retry check saw the candidate)
        Assert.Equal([100], sleeps); // corrective passes use the safe settle
        Assert.Equal([Target], focusCalls);
        Assert.Equal([(Target, EnUs), (Target, FiFi)], requests);
        Assert.True(core.StopConfirmPending); // still armed, ticks reset

        core.CheckDictationFocus(); // tick 2: popup gone, deferred restore runs (restore already done by the pass)

        Assert.Equal(1, escapes);
        Assert.Equal([Target, Target], focusCalls);
        Assert.Equal([(Target, EnUs), (Target, FiFi)], requests); // no extra layout request: already restored
    }

    [Fact]
    public void NativeStopAbsenceIsInconclusiveAndExpiresWithoutCorrection()
    {
        var (core, _) = NewCore();
        int escapes = 0;
        core.SendEscape = () => escapes++;

        core.Toggle(); // start
        core.StopDictationNative();
        for (int i = 0; i < 11; i++)
        {
            core.CheckDictationFocus(); // popup never observed: no correction, deferred restore runs
        }

        Assert.Equal(0, escapes); // absence is never treated as positive evidence
        Assert.False(core.StopConfirmPending); // expired via the existing watchdog semantics
        Assert.False(core.IsDictating);
    }

    [Fact]
    public void NativeStopShowEventRunsBoundedCorrection()
    {
        var (core, _) = NewCore();
        int escapes = 0;
        core.SendEscape = () => escapes++;
        var fg = new Queue<nint>([Target, 0, 0]); // start guard, corrective retry checks
        core.GetForeground = () => fg.Dequeue();

        core.Toggle(); // start
        core.StopDictationNative();
        core.OnVoiceUiShown(); // the bar reopened
        core.OnVoiceUiShown(); // correction 2
        core.OnVoiceUiShown(); // bound reached: pending cleared, no more corrections

        Assert.Equal(2, escapes);
        Assert.False(core.StopConfirmPending);
    }

    [Fact]
    public void StopRetriesUnconfirmedLayoutRestoreOnce()
    {
        var (core, requests) = NewCore();
        nint layout = FiFi;
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
        Assert.Equal([(Target, EnUs), (Target, FiFi), (Target, FiFi)], requests);
        Assert.Equal(FiFi, layout);
    }
}
