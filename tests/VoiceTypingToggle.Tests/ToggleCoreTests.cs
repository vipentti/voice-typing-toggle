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
        var core = new ToggleCore(EnUs)
        {
            GetForeground = () => Target,
            GetThreadId = _ => 7,
            GetLayout = _ => requests.Count > 0 ? EnUs : EnGb,
            RequestLayout = (h, hkl) => { requests.Add((h, hkl)); return true; },
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
        var fg = new Queue<nint>([Target, Target, Target, 0]); // start guard, stop guard, escape-retry check, then bar-close drop to 0
        core.GetForeground = () => fg.Dequeue();
        core.SendEscape = () => escapes++;
        core.RestoreFocus = h => { focusTarget = h; return true; };

        core.Toggle(); // start
        core.Toggle(); // stop

        Assert.False(core.IsDictating);
        Assert.Equal(0, core.SavedLayout);
        Assert.Equal(2, escapes); // first close + retry while the bar was still there
        Assert.Equal(Target, focusTarget);
        Assert.Equal([(Target, EnUs), (Target, EnGb)], requests); // switch up, restore down
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
}
