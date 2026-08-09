using System.Runtime.InteropServices;
using static NativeMethods;

// Shutdown orchestration: drain, escalate, teardown (ShutdownDecision drives policy).
sealed partial class Program
{
    static void RequestOrderlyShutdown(ShutdownKind reason)
    {
        ShutdownAction initialAction = ShutdownPolicy.Begin(
            reason,
            Core.IsDictating,
            Core.StopConfirmPending
        );
        TraceAction(
            reason == ShutdownKind.UserExit ? "user-exit-requested" : "fatal-tray-loss-requested"
        );
        if (initialAction == ShutdownAction.Wait && !FocusTimerRunning)
        {
            // Listening may be disabled (focus-watch timer killed). A pending
            // stop confirmation drains only from that timer, so re-arm it for
            // the shutdown drain; CompleteShutdown kills it as usual.
            FocusTimerRunning = SetTimer(AppWindow, TimerId, FocusWatchIntervalMs, 0) != 0;
            TraceAction("shutdown-watch-timer-rearmed");
        }
        if (Core.IsDictating)
        {
            Core.Toggle(); // normal stop keeps the watchdog armed for late popups
            UpdateTrayTooltip();
        }
        if (initialAction == ShutdownAction.Complete)
        {
            CompleteShutdown();
        }
    }

    static void ContinueShutdownIfNeeded()
    {
        if (ShutdownPolicy.Kind is null)
        {
            return;
        }
        ShutdownAction action = ShutdownPolicy.Advance(
            Core.IsDictating,
            Core.StopConfirmPending,
            IsVoiceUiVisible()
        );
        if (action == ShutdownAction.Wait)
        {
            return;
        }
        if (action == ShutdownAction.Complete)
        {
            CompleteShutdown();
            return;
        }
        if (action == ShutdownAction.Correct)
        {
            Core.CorrectPendingStop(); // canonical saved-stop correction
            return;
        }
        if (action == ShutdownAction.CancelUserExit)
        {
            if (!TrayIconInstalled)
            {
                // Cancellation must not leave the app running invisibly: the
                // lost icon upgrades the user exit to fatal tray loss.
                RequestOrderlyShutdown(ShutdownKind.FatalTrayLoss);
                return;
            }
            ShutdownPolicy.Cancel();
            if (!ListeningEnabled && FocusTimerRunning)
            {
                // The drain re-armed the focus-watch timer while listening was
                // disabled; the disabled state owns no timers, so kill it again.
                _ = KillTimer(AppWindow, TimerId);
                FocusTimerRunning = false;
                TraceAction("disabled-cancel-timer-killed");
            }
            MessageBoxW(
                AppWindow,
                "Voice Typing could not be confirmed closed. Exit was cancelled so monitoring can continue.",
                "Voice Typing Toggle",
                0x10
            );
            return;
        }
        MessageBoxW(
            AppWindow,
            "Voice Typing could not be confirmed closed after the notification icon was lost. It may require manual dismissal.",
            "Voice Typing Toggle",
            0x10
        );
        Core.CorrectPendingStop(); // canonical last-ditch close before teardown
        CompleteShutdown();
    }

    static void CompleteShutdown()
    {
        if (ShutdownPolicy.Kind is null)
        {
            return;
        }

        if (TrayIconInstalled)
        {
            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = AppWindow,
                uID = TrayIconId,
                szTip = string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
            };
            _ = ShellNotifyIconW(NimDelete, ref data);
            TrayIconInstalled = false;
        }
        if (FocusTimerRunning)
        {
            _ = KillTimer(AppWindow, TimerId);
            FocusTimerRunning = false;
        }
        if (WinHHoldArmed)
        {
            // The injected right-Win is still down. Finish the gesture
            // (H + Win up + Escape) instead of a bare Win-up, which would open
            // the Start menu.
            CompleteWinHInjection();
        }
        _ = KillTimer(AppWindow, WinHHoldTimerId);
        if (VoiceUiHook != 0)
        {
            _ = UnhookWinEvent(VoiceUiHook);
            VoiceUiHook = 0;
        }
        if (KeyboardHook != 0)
        {
            UninstallKeyboardHook();
        }
        if (HotkeyRegistered)
        {
            _ = UnregisterHotKey(AppWindow, HotkeyId);
            HotkeyRegistered = false;
        }
        if (AppWindow != 0)
        {
            _ = DestroyWindow(AppWindow);
            AppWindow = 0;
        }
        PostQuitMessage(0);
        Trace.Flush();
    }

    // A background process cannot normally take foreground; attaching our input
    // queue to the CURRENT foreground thread's grants its last-input right (the
    // classic trick — attaching to the target alone is denied intermittently).
}
