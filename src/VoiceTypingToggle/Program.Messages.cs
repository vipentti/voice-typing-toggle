using static NativeMethods;

// Tray message handlers and point helpers: WindowProc dispatch targets.
sealed partial class Program
{
    static nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmHotkey when wParam == HotkeyId && ShutdownPolicy.Kind is null && ListeningEnabled && HotkeyEnabled:
                TraceAction("hotkey");
                Core.Toggle();
                UpdateTrayTooltip();
                Trace.Flush();
                return 0;
            case WmTimer when wParam == TimerId:
                Core.CheckDictationFocus();
                UpdateTrayTooltip();
                ContinueShutdownIfNeeded();
                Trace.Flush();
                return 0;
            case WmTimer when wParam == WinHHoldTimerId:
                CompleteWinHInjection();
                return 0;
            case WmQueryEndSession:
                TraceAction("query-end-session");
                Core.RestoreIfDictating();
                UpdateTrayTooltip();
                Trace.Flush();
                return 1; // allow shutdown
            case WmCloseKeyStop when ShutdownPolicy.Kind is null:
                // Deferred close-on-key stop: the swallowed physical Enter or
                // Space cannot close the bar itself, so the standard
                // Escape-first stop runs here on the message loop (never from
                // the hook callback).
                TraceAction("close-key-stop");
                if (Core.IsDictating)
                {
                    Core.StopDictation();
                }
                UpdateTrayTooltip();
                Trace.Flush();
                return 0;
            case WmWinHDown when ShutdownPolicy.Kind is null && ListeningEnabled && Core.IsDictating:
                // Async Win+H gesture, step 1: the loop is free (no nested
                // message pump), the low-level hook stays responsive, and the
                // injected right-Win chains to the shell immediately.
                if (!SendKey(VK_RWIN, 0x5B, up: false, useScanCode: false, extended: true))
                {
                    // Win-down was never injected: no gesture is armed, so no
                    // hold timer may fire H/Win-up/Escape into the foreground.
                    TraceAction("winh-win-down-failed");
                    Core.RestoreIfDictating();
                    return 0;
                }
                TraceAction("winh-win-down");
                WinHHoldArmed = true;
                if (SetTimer(AppWindow, WinHHoldTimerId, WinHHoldMs, 0) == 0)
                {
                    // The hold timer could not be created: finish the gesture
                    // synchronously so the injected Win is never left held,
                    // then roll the armed session back.
                    TraceAction("winh-timer-failed");
                    CompleteWinHInjection(forceClose: true);
                    Core.RestoreIfDictating();
                }
                return 0;
            case WmTrayIcon when ShutdownPolicy.Kind is null:
                HandleTrayIconMessage(wParam, lParam);
                return 0;
            case WmContextMenu when ShutdownPolicy.Kind is null:
                ShowTrayMenu(PointFromContextMenu(lParam));
                return 0;
        }
        if (msg == TaskbarCreatedMessage)
        {
            HandleTaskbarRestart();
            return 0;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // Explorer restarts destroy the notification icon and re-broadcast
    // TaskbarCreated. Always react, even while a shutdown drains: either the
    // icon is restored, or the shutdown escalates to fatal tray loss so the
    // application never runs invisibly.
    static void HandleTaskbarRestart()
    {
        TrayIconInstalled = false;
        if (TryAddTrayIcon())
        {
            return;
        }
        ReportTrayIconFailure(isRecreation: true);
        RequestOrderlyShutdown(ShutdownKind.FatalTrayLoss);
    }

    static void HandleTrayIconMessage(nint wParam, nint lParam)
    {
        uint notification = (uint)(ulong)lParam & 0xFFFF;
        if (notification == WmRButtonUp || notification == WmContextMenu)
        {
            // Overflow-hosted icons can report an anchor that differs from the
            // actual pointer. The current cursor reliably gives the requested
            // mouse location for both right-click notification variants.
            ShowTrayMenu(PointFromContextMenu(-1));
        }
        // All other notification messages, including left-click and double-click,
        // intentionally remain inert.
    }

    static POINT PointFromPackedCoordinates(nint packed)
    {
        long value = packed;
        return new POINT
        {
            x = (short)(value & 0xFFFF),
            y = (short)((value >> 16) & 0xFFFF),
        };
    }

    static POINT PointFromContextMenu(nint lParam)
    {
        if (lParam != -1)
        {
            return PointFromPackedCoordinates(lParam);
        }
        return GetCursorPos(out POINT point) ? point : default;
    }

}
