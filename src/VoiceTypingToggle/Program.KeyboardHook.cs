using System.Runtime.InteropServices;
using static NativeMethods;

// Physical Win+H observation (WH_KEYBOARD_LL) and close-key handling.
sealed partial class Program
{
    static nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(KeyboardHook, nCode, wParam, lParam);
        }
        bool swallow = false;
        try
        {
            // Never inspect key content beyond vk/flags. Injected events (our
            // own SendWinH/SendEscape) must not arm or trigger the chord.
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((info.flags & LlkhfInjected) != 0)
            {
                return CallNextHookEx(KeyboardHook, nCode, wParam, lParam);
            }
            bool down = (uint)wParam == WmKeyDown || (uint)wParam == WmSysKeyDown;
            switch (info.vkCode)
            {
                case VK_LWIN:
                    LeftWinDown = down;
                    if (!down)
                    {
                        WinHDispatched = false; // chord ended: re-arm
                    }
                    break;
                case VK_RWIN:
                    RightWinDown = down;
                    if (!down)
                    {
                        WinHDispatched = false;
                    }
                    break;
                case VK_H when down && (LeftWinDown || RightWinDown) && !WinHDispatched:
                    WinHDispatched = true; // auto-repeat H keydowns stay suppressed until re-arm
                    TraceAction("winh-observed");
                    OnWinHObservation();
                    break;
                case VK_H when !down:
                    WinHDispatched = false;
                    break;
                case VK_ESCAPE when down && Core.IsDictating:
                    // External close: the user's own Escape closes the bar; the
                    // native-stop dispatch only restores the saved state. The
                    // injected guard above keeps our own stop Escape from
                    // re-triggering; auto-repeat is a no-op once the core is Idle.
                    TraceAction("escape-observed");
                    OnExternalCloseObservation();
                    break;
                case VK_RETURN when down && Core.IsDictating && EnterCloseEnabled:
                    // The bar does not close on Enter natively. Swallow the key
                    // (scoped exception to the no-swallow rule: a chained Enter
                    // would reach the app as a stray newline) and close the bar
                    // with the standard Escape-first stop from the message loop.
                    // Swallow only when the deferred stop is actually queued.
                    TraceAction("enter-observed");
                    swallow = PostMessageW(AppWindow, WmCloseKeyStop, 0, 0);
                    break;
                case VK_SPACE when down && Core.IsDictating && SpaceCloseEnabled:
                    // Same as Enter: the bar does not close on Space natively.
                    TraceAction("space-observed");
                    swallow = PostMessageW(AppWindow, WmCloseKeyStop, 0, 0);
                    break;
            }
        }
#pragma warning disable CA1031 // an exception escaping a native callback terminates a Native AOT process
        catch (Exception)
#pragma warning restore CA1031
        {
            swallow = false; // failures never swallow input, even if the key was already matched
            TraceAction("hook-error");
        }
        return swallow ? 1 : CallNextHookEx(KeyboardHook, nCode, wParam, lParam);
    }

    // Observation dispatch (called from the hook callback on the message-loop
    // thread, BEFORE the event is chained onward). Idle: bounded synchronous
    // race-start so English is confirmed before the native Win+H proceeds.
    // Dictating: native stop only, which injects nothing; the physical press
    // itself closes the bar, and restoration runs later on the focus-watch
    // timer. Never dispatch while a shutdown drains.
    static void OnWinHObservation()
    {
        // Never dispatch while a shutdown drains.
        if (ShutdownPolicy.Kind is not null)
        {
            return;
        }
        TraceAction("winh-observation");
        DispatchObservation();
    }

    // Physical Escape while dictating: the key itself closes the bar; only
    // the saved state needs restoring.
    static void OnExternalCloseObservation()
    {
        if (ShutdownPolicy.Kind is not null)
        {
            return;
        }
        TraceAction("external-close-observation");
        DispatchObservation();
    }

    // Shared observation dispatch (called from the hook callback on the
    // message-loop thread, BEFORE the event is chained onward). Idle: bounded
    // synchronous race-start so English is confirmed before the native Win+H
    // proceeds. Dictating: native stop only, which injects nothing; the
    // physical press itself closes the bar, and restoration runs later on the
    // focus-watch timer.
    static void DispatchObservation()
    {
        if (Core.IsDictating)
        {
            Core.StopDictationNative();
        }
        else
        {
            nint hwnd = Core.GetForeground();
            if (hwnd != 0)
            {
                Core.StartDictationRace(hwnd);
            }
        }
        UpdateTrayTooltip();
        Trace.Flush();
    }

    static bool TryInstallKeyboardHook()
    {
        if (KeyboardHook != 0)
        {
            return true;
        }
        KeyboardHook = SetWindowsHookExW(WhKeyboardLl, KeyboardProcDelegate, 0, 0);
        if (KeyboardHook == 0)
        {
            InterceptWinHEnabled = false; // intent cleared: checkbox unchecked, physical Win+H stays native
            TraceAction("hook-install-failed");
            return false;
        }
        TraceAction("hook-installed");
        return true;
    }

    static void UninstallKeyboardHook()
    {
        if (KeyboardHook == 0)
        {
            return;
        }
        _ = UnhookWindowsHookEx(KeyboardHook);
        KeyboardHook = 0;
        LeftWinDown = false;
        RightWinDown = false;
        WinHDispatched = false;
        TraceAction("hook-uninstalled");
    }

    // stop-flash watchdog: the Voice Typing "Listening..." pointer is a
    // TextInputHost popup (class Xaml_WindowedPopupClass, title PopupHost). Its
    // SHOW after a stop means the bar reopened; the core decides what to do.
}
