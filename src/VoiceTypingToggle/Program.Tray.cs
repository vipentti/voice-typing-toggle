using System.Runtime.InteropServices;
using static NativeMethods;

// Tray icon plumbing, tray menu, and session-only toggles.
sealed partial class Program
{
    static void ShowTrayMenu(POINT point)
    {
        // A menu activation deliberately moves focus away from the dictation
        // target. Let the existing focus-loss path end and restore that session
        // before the dynamic status is rendered.
        if (Core.IsDictating)
        {
            if (WinHHoldArmed)
            {
                // Resolve an in-flight Win+H hold while the core is still
                // dictating: the gesture completes without Escape and the
                // injected Win is released, so no hold is ever armed when a
                // menu command runs and no Escape is sent into the window
                // that replaced the dictation target.
                CompleteWinHInjection();
            }
            _ = RestoreFocus(AppWindow);
            Core.CheckDictationFocus();
        }
        UpdateTrayTooltip();

        nint menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }
        try
        {
            uint informationalFlags = MfString | MfDisabled | MfGrayed;
            uint subToggleFlags = ListeningEnabled ? MfString : MfString | MfGrayed | MfDisabled;
            _ = AppendMenuW(menu, informationalFlags, 0, "Voice Typing Toggle");
            _ = AppendMenuW(menu, informationalFlags, 0, $"Status: {CurrentStatus}");
            _ = AppendMenuW(menu, informationalFlags, 0, "Hotkey: Ctrl+Alt+H");
            _ = AppendMenuW(menu, MfSeparator, 0, null);
            // Session-only toggles; the checkmarks reflect the live intent
            // state. While listening is disabled the sub-toggles are grayed
            // and unselectable but keep their intent for the re-enable.
            _ = AppendMenuW(menu, MfString | (ListeningEnabled ? MfChecked : MfUnchecked), MenuListeningId, "Enable listening");
            _ = AppendMenuW(menu, subToggleFlags | (HotkeyEnabled ? MfChecked : MfUnchecked), MenuHotkeyId, "Enable Ctrl+Alt+H");
            _ = AppendMenuW(menu, subToggleFlags | (InterceptWinHEnabled ? MfChecked : MfUnchecked), MenuInterceptWinHId, "Intercept Win+H");
            _ = AppendMenuW(menu, subToggleFlags | (EnterCloseEnabled ? MfChecked : MfUnchecked), MenuEnterCloseId, "Close dictation on Enter");
            _ = AppendMenuW(menu, subToggleFlags | (SpaceCloseEnabled ? MfChecked : MfUnchecked), MenuSpaceCloseId, "Close dictation on Space");
            _ = AppendMenuW(menu, MfSeparator, 0, null);
            _ = AppendMenuW(menu, MfString, MenuExitId, "Exit");

            _ = SetForegroundWindow(AppWindow);
            uint command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.x, point.y, 0, AppWindow, 0);
            _ = PostMessageW(AppWindow, WmNull, 0, 0);
            ReturnFocusToNotificationArea();
            if (command == MenuExitId)
            {
                RequestOrderlyShutdown(ShutdownKind.UserExit);
            }
            else if (command == MenuListeningId)
            {
                ToggleListening();
            }
            else if (command == MenuHotkeyId)
            {
                ToggleHotkey();
            }
            else if (command == MenuInterceptWinHId)
            {
                ToggleInterception();
            }
            else if (command == MenuEnterCloseId)
            {
                EnterCloseEnabled = !EnterCloseEnabled;
                TraceAction(EnterCloseEnabled ? "enter-close-on" : "enter-close-off");
            }
            else if (command == MenuSpaceCloseId)
            {
                SpaceCloseEnabled = !SpaceCloseEnabled;
                TraceAction(SpaceCloseEnabled ? "space-close-on" : "space-close-off");
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    // Every disable path guards with this helper: a session can never be
    // stranded. In normal operation the tray-menu heal already ended the
    // session before a command is selected, so the canonical Escape-first stop
    // runs only in the defensive case. No hold handling is needed: an
    // in-flight hold is resolved at tray-menu open while the core is still
    // dictating, so WinHHoldArmed is false whenever a toggle runs.
    static void AbortActiveSessionIfAny()
    {
        if (Core.IsDictating)
        {
            TraceAction("toggle-abort-stop");
            Core.StopDictation();
        }
    }

    static void ToggleListening()
    {
        if (ListeningEnabled)
        {
            AbortActiveSessionIfAny();
            ListeningEnabled = false;
            if (HotkeyRegistered)
            {
                _ = UnregisterHotKey(AppWindow, HotkeyId);
                HotkeyRegistered = false;
            }
            if (KeyboardHook != 0)
            {
                UninstallKeyboardHook();
            }
            if (FocusTimerRunning)
            {
                _ = KillTimer(AppWindow, TimerId);
                FocusTimerRunning = false;
            }
            _ = KillTimer(AppWindow, WinHHoldTimerId);
            TraceAction("listening-off");
        }
        else
        {
            // Re-enable is all-or-nothing at the hotkey: registration is the
            // only failure that rejects it, so register first while the master
            // is still disabled. On failure clear the hotkey intent and leave
            // the disabled state untouched (no rollback needed). On success
            // apply the rest in dependency order: timer, hook, then enable.
            if (HotkeyEnabled && !HotkeyRegistered && !RegisterHotKey(AppWindow, HotkeyId, ModControl | ModAlt, 'H'))
            {
                HotkeyEnabled = false;
                MessageBoxW(AppWindow, "Could not register the Ctrl+Alt+H hotkey (it may be in use by another program).",
                    "Voice Typing Toggle", 0x10);
                TraceAction("listening-reenable-hotkey-failed");
            }
            else
            {
                ListeningEnabled = true;
                FocusTimerRunning = SetTimer(AppWindow, TimerId, FocusWatchIntervalMs, 0) != 0;
                if (InterceptWinHEnabled && KeyboardHook == 0)
                {
                    TryInstallKeyboardHook(); // failure clears the intercept intent (nonfatal)
                }
                TraceAction("listening-on");
            }
        }
        UpdateTrayTooltip();
        Trace.Flush();
    }

    static void ToggleHotkey()
    {
        if (HotkeyEnabled)
        {
            AbortActiveSessionIfAny();
            HotkeyEnabled = false;
            if (HotkeyRegistered)
            {
                _ = UnregisterHotKey(AppWindow, HotkeyId);
                HotkeyRegistered = false;
            }
            TraceAction("hotkey-off");
        }
        else if (RegisterHotKey(AppWindow, HotkeyId, ModControl | ModAlt, 'H'))
        {
            HotkeyRegistered = true;
            HotkeyEnabled = true;
            TraceAction("hotkey-on");
        }
        else
        {
            // Direct registration failure: item stays unchecked, listening
            // and all other listening machinery stay as they are.
            MessageBoxW(AppWindow, "Could not register the Ctrl+Alt+H hotkey (it may be in use by another program).",
                "Voice Typing Toggle", 0x10);
            TraceAction("hotkey-register-failed");
        }
        UpdateTrayTooltip();
        Trace.Flush();
    }

    static void ToggleInterception()
    {
        if (KeyboardHook != 0)
        {
            UninstallKeyboardHook(); // native Win+H behavior returns untouched
            InterceptWinHEnabled = false;
            TraceAction("intercept-off");
        }
        else
        {
            InterceptWinHEnabled = true;
            TryInstallKeyboardHook(); // failure clears the intent again (nonfatal)
        }
        UpdateTrayTooltip();
        Trace.Flush();
    }

    static string CurrentStatus => !ListeningEnabled ? "Disabled" : Core.IsDictating ? "Dictating" : "Idle";

    static string TrayTooltip => $"Voice Typing Toggle: {CurrentStatus}";

    static bool TryAddTrayIcon()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = AppWindow,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = AppIcon,
            szTip = TrayTooltip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
        if (!ShellNotifyIconW(NimAdd, ref data))
        {
            return false;
        }

        data.uTimeoutOrVersion = NotifyIconVersion4;
        if (ShellNotifyIconW(NimSetVersion, ref data))
        {
            TrayIconInstalled = true;
            return true;
        }

        _ = ShellNotifyIconW(NimDelete, ref data);
        return false;
    }

    static void UpdateTrayTooltip()
    {
        if (!TrayIconInstalled)
        {
            return;
        }
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = AppWindow,
            uID = TrayIconId,
            uFlags = NifTip | NifShowTip,
            szTip = TrayTooltip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
        _ = ShellNotifyIconW(NimModify, ref data);
    }

    static void ReturnFocusToNotificationArea()
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
        _ = ShellNotifyIconW(NimSetFocus, ref data);
    }

    static void ReportTrayIconFailure(bool isRecreation)
    {
        string text = isRecreation
            ? "Voice Typing Toggle could not restore its notification icon after Explorer restarted. The application will close."
            : "Voice Typing Toggle could not add its notification icon. The application will close.";
        MessageBoxW(AppWindow, text, "Voice Typing Toggle", 0x10);
    }

}
