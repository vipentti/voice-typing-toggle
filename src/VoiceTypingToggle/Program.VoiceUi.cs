using static NativeMethods;

// Voice Typing UI watchdog: win-event callback, window matching, tracing.
sealed partial class Program
{
    static void OnVoiceUiEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime
    )
    {
        if (hwnd == 0 || idObject != ObjidWindow || !IsVoiceUiWindow(hwnd))
        {
            return;
        }
        TraceAction($"popup-show-0x{hwnd:X}");
        Core.OnVoiceUiShown();
        UpdateTrayTooltip();
        ContinueShutdownIfNeeded();
        Trace.Flush();
    }

    static void TraceAction(string eventName)
    {
        if (!Trace.Enabled)
        {
            return;
        }
        nint foreground = GetForegroundWindow();
        uint foregroundTid = foreground != 0 ? GetWindowThreadProcessId(foreground, out _) : 0;
        nint foregroundHkl = foregroundTid != 0 ? GetKeyboardLayout(foregroundTid) : 0;
        Trace.Write(
            GetTickCount64(),
            eventName,
            foreground,
            foregroundTid,
            foregroundHkl,
            Core.IsDictating,
            Core.WaitingForBar,
            Core.StopConfirmPending
        );
    }

    // stop-flash: the bar's launch confirmation also polls for this window.
    static bool IsVoiceUiWindow(nint hwnd)
    {
        var cls = new char[256];
        int clsLen = GetClassNameW(hwnd, cls, cls.Length);
        if (clsLen <= 0 || new string(cls, 0, clsLen) != "Xaml_WindowedPopupClass")
        {
            return false;
        }
        var title = new char[256];
        int titleLen = GetWindowTextW(hwnd, title, title.Length);
        if (titleLen <= 0 || new string(title, 0, titleLen) != "PopupHost")
        {
            return false;
        }
        return IsTextInputHost(GetWindowThreadProcessId(hwnd, out _));
    }

    // stop-flash: bar-launch confirmation poll (timer-driven, non-blocking).
    // EnumWindows sees hidden windows too, so the matcher is ANDed with
    // IsWindowVisible — the reused TextInputHost popup must not count while hidden.
    static bool IsVoiceUiVisible()
    {
        bool found = false;
        _ = EnumWindows(
            (h, _) =>
            {
                if (IsVoiceUiWindow(h) && IsWindowVisible(h))
                {
                    found = true;
                    return false; // stop enumerating
                }
                return true;
            },
            0
        );
        return found;
    }

    static bool IsTextInputHost(uint pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName == "TextInputHost";
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
