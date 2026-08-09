using System.Runtime.InteropServices;
using static NativeMethods;

// Synthetic input: keys, the Win+H gesture, focus and layout requests.
sealed partial class Program
{
    static bool RestoreFocus(nint hwnd)
    {
        if (GetForegroundWindow() == hwnd)
        {
            return true;
        }
        uint self = GetCurrentThreadId();
        bool attached = false;
        nint current = GetForegroundWindow();
        uint attachTo = current != 0 ? GetWindowThreadProcessId(current, out _) : GetWindowThreadProcessId(hwnd, out _);
        if (attachTo != 0 && attachTo != self)
        {
            attached = AttachThreadInput(self, attachTo, true);
        }
        bool ok = SetForegroundWindow(hwnd);
        if (!ok)
        {
            // The shell may still be mid-transition (e.g. raising the Start menu);
            // retry once it settles.
            Thread.Sleep(100);
            ok = SetForegroundWindow(hwnd);
        }
        if (attached)
        {
            _ = AttachThreadInput(self, attachTo, false);
        }
        TraceAction(ok ? "restore-focus-ok" : "restore-focus-failed");
        return ok;
    }

    static bool RequestLayout(nint hwnd, nint hkl)
    {
        bool requested = SendMessageTimeout(hwnd, WmInputLangChangeRequest, 0, hkl, SmtoAbortIfHung, 1000, out _) != 0;
        TraceAction(requested ? $"layout-request-ok-0x{hwnd:X}-0x{hkl:X}" : $"layout-request-failed-0x{hwnd:X}-0x{hkl:X}");
        return requested;
    }

    // Hook-callback budget: worst case ~100 ms here plus ~100 ms WaitForLayout,
    // so the Idle race-start callback stays within its ~200 ms bound. The
    // 1000 ms seam above remains the only request path for Ctrl+Alt+H.
    static bool RequestLayoutHookSafe(nint hwnd, nint hkl)
    {
        bool requested = SendMessageTimeout(hwnd, WmInputLangChangeRequest, 0, hkl, SmtoAbortIfHung, 100, out _) != 0;
        TraceAction(requested ? $"layout-request-hook-safe-ok-0x{hwnd:X}-0x{hkl:X}" : $"layout-request-hook-safe-failed-0x{hwnd:X}-0x{hkl:X}");
        return requested;
    }

    static void SendWinH()
    {
        TraceAction("winh-begin");
        // Async gesture: post step 1 instead of blocking the loop. The
        // verified recipe (right-Win down, hold, H as scancode, right-Win up)
        // is completed by the hold timer on the free message loop, so the
        // low-level hook chains every injected event to the shell immediately
        // and no nested message pump or reentrancy guard is needed. The shell
        // sees the same timing as the original synchronous recipe. If the
        // post fails, no gesture is coming: roll the armed session back now.
        if (!PostMessageW(AppWindow, WmWinHDown, 0, 0))
        {
            TraceAction("winh-queue-failed");
            Core.RestoreIfDictating();
        }
    }

    // Async Win+H gesture, step 2 (hold timer): complete the recipe, or abort
    // if the session ended during the hold. Completing with H before
    // releasing Win is mandatory: a bare Win-up opens the Start menu. On
    // abort the H opens the bar (or toggles a natively opened one) and the
    // Escape closes it again; a stray Escape reaching the app matches the
    // existing stop-before-launch semantics. Failure handling is structured
    // around the activation point: once H-down succeeded, Windows may already
    // act on Win+H, so rollback must close the bar with Escape; before that
    // point, rollback is layout-only.
    static void CompleteWinHInjection(bool forceClose = false)
    {
        // No-op when nothing is armed: every valid caller requires an armed
        // hold, and Win32 does not remove already-queued WM_TIMER messages on
        // KillTimer, so a stale hold-timer message must never re-inject input
        // after the gesture was completed or listening was disabled.
        if (!WinHHoldArmed)
        {
            return;
        }
        WinHHoldArmed = false;
        _ = KillTimer(AppWindow, WinHHoldTimerId);
        bool hDown = SendKey(0, 0x23, up: false, useScanCode: true);
        bool hUp = hDown && SendKey(0, 0x23, up: true, useScanCode: true);
        bool winUp = SendKey(VK_RWIN, 0x5B, up: true, useScanCode: false, extended: true);
        if (!hDown)
        {
            // Voice Typing was never triggered: layout-only rollback. Win may
            // be logically held; release best-effort (a held Win is worse than
            // a stray Start here).
            TraceAction("winh-inject-failed");
            if (!winUp)
            {
                _ = SendKey(VK_RWIN, 0x5B, up: true, useScanCode: false, extended: true);
            }
            Core.RestoreIfDictating();
            return;
        }
        if (!hUp || !winUp)
        {
            // The gesture reached the activation point: the bar may be open
            // and listening. Release best-effort, close the bar with Escape,
            // then roll back core state.
            TraceAction("winh-inject-failed");
            if (!hUp)
            {
                _ = SendKey(0, 0x23, up: true, useScanCode: true); // best-effort H release
            }
            if (!winUp)
            {
                _ = SendKey(VK_RWIN, 0x5B, up: true, useScanCode: false, extended: true); // best-effort Win release
            }
            SendEscape();
            Core.RestoreIfDictating();
            return;
        }
        if (forceClose || !Core.IsDictating)
        {
            SendEscape();
            TraceAction("winh-aborted");
        }
        else
        {
            TraceAction("winh-sent");
        }
    }

    static void SendEscape()
    {
        if (SendKey(0, 0x01, up: false, useScanCode: true) &&
            SendKey(0, 0x01, up: true, useScanCode: true))
        {
            TraceAction("escape-sent");
        }
        else
        {
            TraceAction("escape-inject-failed");
        }
    }

    // Returns true when SendInput accepted the event. No core-state side
    // effects here: the Win+H gesture owns its rollback, and the stop path
    // restores unconditionally after its settle regardless of this result.
    static bool SendKey(ushort vk, ushort scan, bool up, bool useScanCode, bool extended = false)
    {
        uint flags = (up ? KeyeventfKeyUp : 0) | (extended ? KeyeventfExtendedKey : 0) | (useScanCode ? KeyeventfScanCode : 0);
        var input = new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = useScanCode ? (ushort)0 : vk,
                    wScan = scan,
                    dwFlags = flags,
                },
            },
        };
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 0)
        {
            TraceAction("send-input-failed");
            return false;
        }
        return true;
    }

    // Physical Win+H observation (WH_KEYBOARD_LL). The hook runs BEFORE the
    // event is delivered onward, so the callback must never swallow, block, or
    // inject input; it only observes and traces here (T3 wires the dispatch).
}
