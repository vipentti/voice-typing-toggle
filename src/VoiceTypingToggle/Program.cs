using System.Runtime.InteropServices;

// VoiceTypingToggle: background utility. Ctrl+Alt+H toggles English voice typing:
//   Idle -> save foreground layout, switch to en-US, Win+H (Voice Typing opens)
//   Dictating -> Win+H (closes), restore saved layout to foreground at that time
sealed partial class Program
{
    const uint WmInputLangChangeRequest = 0x0050;
    const uint SmtoAbortIfHung = 0x0002;
    const uint WmHotkey = 0x0312;
    const uint WmTimer = 0x0113;
    const uint WmQueryEndSession = 0x0011;
    const uint WmNull = 0x0000;
    const uint WmContextMenu = 0x007B;
    const uint WmRButtonUp = 0x0205;
    const uint WmApp = 0x8000;
    const uint WmTrayIcon = WmApp + 1;
    const uint WmCloseKeyStop = WmApp + 2;
    const uint WmWinHDown = WmApp + 3;
    const uint ModAlt = 0x0001;
    const uint ModControl = 0x0002;
    const uint WsExToolWindow = 0x00000080;
    const uint WsExNoActivate = 0x08000000;
    const int HotkeyId = 1;
    const int TimerId = 2;
    const int WinHHoldTimerId = 3;
    const int WinHHoldMs = 500;
    const uint TrayIconId = 1;
    const uint NimAdd = 0x00000000;
    const uint NimDelete = 0x00000002;
    const uint NimModify = 0x00000001;
    const uint NimSetFocus = 0x00000003;
    const uint NimSetVersion = 0x00000004;
    const uint NifMessage = 0x00000001;
    const uint NifIcon = 0x00000002;
    const uint NifTip = 0x00000004;
    const uint NifShowTip = 0x00000080;
    const uint NotifyIconVersion4 = 4;
    const nint ApplicationIconResourceId = 32512;
    const uint MfString = 0x00000000;
    const uint MfDisabled = 0x00000002;
    const uint MfGrayed = 0x00000001;
    const uint MfChecked = 0x00000008;
    const uint MfUnchecked = 0x00000000;
    const uint MfSeparator = 0x00000800;
    const uint TpmRightButton = 0x0002;
    const uint TpmReturnCommand = 0x0100;
    const uint MenuExitId = 1;
    const uint MenuInterceptWinHId = 2;
    const uint MenuEnterCloseId = 3;
    const uint MenuSpaceCloseId = 4;
    const int FocusWatchIntervalMs = 250; // bar auto-closes on focus change; heal within a quarter second
    const uint KeyeventfExtendedKey = 0x0001;
    const uint KeyeventfKeyUp = 0x0002;
    const uint KeyeventfScanCode = 0x0008;
    const ushort VK_RWIN = 0x5C;
    const ushort VK_LWIN = 0x5B;
    const ushort VK_H = 0x48;
    const ushort VK_ESCAPE = 0x1B;
    const ushort VK_RETURN = 0x0D;
    const ushort VK_SPACE = 0x20;
    const int WhKeyboardLl = 13;
    const uint LlkhfInjected = 0x00000010;
    const uint WmKeyDown = 0x0100;
    const uint WmSysKeyDown = 0x0104;

    // stop-flash watchdog: watch for the TextInputHost "Listening..." popup
    // reappearing after a stop (the bar reopened; the core runs a corrective pass).
    const uint EventObjectShow = 0x8002;
    const int ObjidWindow = 0;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hWnd, char[] lpClassName, int nMaxCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, char[] lpString, int nMaxCount);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint idThread);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial int GetKeyboardLayoutList(int nBuff, nint[] list);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static partial nint SendMessageTimeout(
        nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, KeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW wndClass);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial nint LoadIconW(nint hInstance, nint lpIconName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG lpMsg);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in MSG lpMsg);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint SetTimer(nint hWnd, nint nIDEvent, uint uElapse, nint lpTimerFunc);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(nint hWnd, nint uIDEvent);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hWinEventHook);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial uint TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint hMenu);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial ulong GetTickCount64();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);
    private static readonly WndProc WndProcDelegate = WindowProc; // keep GC root for the lifetime of the class

    private delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    private static readonly WinEventProc WinEventCallback = OnVoiceUiEvent; // rooted: out-of-context hook calls back through the message loop
    private const uint InputKeyboard = 1;

    private delegate nint KeyboardProc(int nCode, nint wParam, nint lParam);
    private static readonly KeyboardProc KeyboardProcDelegate = LowLevelKeyboardProc; // rooted: the hook calls back through the message loop
    private static nint KeyboardHook;
    private static bool LeftWinDown;
    private static bool RightWinDown;
    private static bool WinHDispatched; // one observation per physical Win+H chord; re-armed on H or Win keyup

    private static ToggleCore Core = null!;
    private static DiagnosticTrace Trace = DiagnosticTrace.Disabled;
    private static nint AppWindow;
    private static nint AppIcon;
    private static nint VoiceUiHook;
    private static uint TaskbarCreatedMessage;
    private static bool HotkeyRegistered;
    private static bool FocusTimerRunning;
    private static bool TrayIconInstalled;
    private static bool WinHHoldArmed; // injected right-Win is down: shutdown must release it
    private static bool EnterCloseEnabled = true;  // tray-gated: close dictation on physical Enter while dictating
    private static bool SpaceCloseEnabled = true;  // tray-gated: close dictation on physical Space while dictating
    private static readonly ShutdownDecision ShutdownPolicy = new();

    static int Main()
    {
        Trace = DiagnosticTrace.CreateFromEnvironment();
        bool restoreFocusedLayout = Environment.GetEnvironmentVariable("VTT_RESTORE_FOCUSED_LAYOUT") == "1";

        nint[] layouts = new nint[32];
        int count = GetKeyboardLayoutList(layouts.Length, layouts);
        // Prefer the English layout on the user's current physical keyboard:
        // switching only the language (same klid) is stable mid-gesture, while
        // a klid switch during a held key broke the shell's Win-combo handling.
        nint currentHkl = 0;
        nint foreground = GetForegroundWindow();
        if (foreground != 0)
        {
            uint tid = GetWindowThreadProcessId(foreground, out _);
            currentHkl = tid != 0 ? GetKeyboardLayout(tid) : 0;
        }
        nint englishLayout = count > 0 ? ToggleCore.SelectEnglishLayout(layouts, count, currentHkl != 0 ? (uint)currentHkl & 0xFFFF0000 : 0) : 0;
        if (englishLayout == 0)
        {
            MessageBoxW(0, "No English keyboard layout is installed. Voice Typing Toggle cannot start.",
                "Voice Typing Toggle", 0x10 /* MB_ICONERROR */);
            return 1;
        }

        Core = new ToggleCore(englishLayout)
        {
            GetForeground = GetForegroundWindow,
            GetThreadId = h => GetWindowThreadProcessId(h, out _),
            GetLayout = GetKeyboardLayout,
            RequestLayout = RequestLayout,
            RequestLayoutBounded = RequestLayoutHookSafe,
            SendWinH = SendWinH,
            SendEscape = SendEscape,
            RestoreFocus = RestoreFocus,
            Sleep = Thread.Sleep,
            IsVoiceUiVisible = IsVoiceUiVisible,
            RestoreFocusedLayoutOnFocusLoss = restoreFocusedLayout,
            Trace = TraceAction,
        };
        TraceAction(restoreFocusedLayout ? "startup-focused-restore-on" : "startup-focused-restore-off");
        Trace.Flush();

        nint hInstance = GetModuleHandleW(null);
        var wndClass = new WNDCLASSW
        {
            lpfnWndProc = WndProcDelegate,
            hInstance = hInstance,
            lpszClassName = "VoiceTypingToggleWindow",
        };
        if (RegisterClassW(ref wndClass) == 0)
        {
            MessageBoxW(0, $"RegisterClass failed (Win32 {Marshal.GetLastPInvokeError()}).", "Voice Typing Toggle", 0x10);
            return 1;
        }

        AppWindow = CreateWindowExW(WsExToolWindow | WsExNoActivate, wndClass.lpszClassName, null, 0, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (AppWindow == 0)
        {
            MessageBoxW(0, $"CreateWindow failed (Win32 {Marshal.GetLastPInvokeError()}).", "Voice Typing Toggle", 0x10);
            return 1;
        }

        if (!RegisterHotKey(AppWindow, HotkeyId, ModControl | ModAlt, 'H'))
        {
            MessageBoxW(0, "Could not register the Ctrl+Alt+H hotkey (it may be in use by another program).",
                "Voice Typing Toggle", 0x10);
            return 1;
        }
        HotkeyRegistered = true;
        VoiceUiHook = SetWinEventHook(EventObjectShow, EventObjectShow, 0, WinEventCallback, 0, 0, 0 /* WINEVENT_OUTOFCONTEXT */); // stop-flash watchdog
        FocusTimerRunning = SetTimer(AppWindow, TimerId, FocusWatchIntervalMs, 0) != 0;
        // Physical Win+H observation (race interception): installed by default
        // (opt-out); the tray checkbox toggles it live (T4). Failure is
        // non-fatal: native Win+H behavior simply stays untouched.
        TryInstallKeyboardHook();
        TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        AppIcon = LoadIconW(hInstance, ApplicationIconResourceId);
        if (AppIcon == 0)
        {
            MessageBoxW(AppWindow, "Could not load the embedded application icon.", "Voice Typing Toggle", 0x10);
            RequestOrderlyShutdown(ShutdownKind.FatalTrayLoss);
            return 1;
        }

        if (!TryAddTrayIcon())
        {
            ReportTrayIconFailure(isRecreation: false);
            RequestOrderlyShutdown(ShutdownKind.FatalTrayLoss);
            return 1;
        }

        // Best-effort restore if the process exits while dictating.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            TraceAction("process-exit");
            Core.RestoreIfDictating();
            Trace.Dispose();
        };

        while (GetMessageW(out MSG msg, 0, 0, 0) > 0)
        {
            _ = TranslateMessage(in msg);
            _ = DispatchMessageW(in msg);
        }
        return 0;
    }

    static nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmHotkey when wParam == HotkeyId && ShutdownPolicy.Kind is null:
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
            case WmWinHDown when ShutdownPolicy.Kind is null:
                // Async Win+H gesture, step 1: the loop is free (no nested
                // message pump), the low-level hook stays responsive, and the
                // injected right-Win chains to the shell immediately.
                SendKey(VK_RWIN, 0x5B, up: false, useScanCode: false, extended: true);
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

    static void ShowTrayMenu(POINT point)
    {
        // A menu activation deliberately moves focus away from the dictation
        // target. Let the existing focus-loss path end and restore that session
        // before the dynamic status is rendered.
        if (Core.IsDictating)
        {
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
            _ = AppendMenuW(menu, informationalFlags, 0, "Voice Typing Toggle");
            _ = AppendMenuW(menu, informationalFlags, 0, $"Status: {CurrentStatus}");
            _ = AppendMenuW(menu, informationalFlags, 0, "Hotkey: Ctrl+Alt+H");
            _ = AppendMenuW(menu, MfSeparator, 0, null);
            // Session-only interception and close-key toggles; the checkmarks
            // reflect the live state.
            _ = AppendMenuW(menu, MfString | (KeyboardHook != 0 ? MfChecked : MfUnchecked), MenuInterceptWinHId, "Intercept Win+H");
            _ = AppendMenuW(menu, MfString | (EnterCloseEnabled ? MfChecked : MfUnchecked), MenuEnterCloseId, "Close dictation on Enter");
            _ = AppendMenuW(menu, MfString | (SpaceCloseEnabled ? MfChecked : MfUnchecked), MenuSpaceCloseId, "Close dictation on Space");
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

    static void ToggleInterception()
    {
        if (KeyboardHook != 0)
        {
            UninstallKeyboardHook(); // native Win+H behavior returns untouched
        }
        else
        {
            TryInstallKeyboardHook();
        }
        UpdateTrayTooltip();
        Trace.Flush();
    }

    static string CurrentStatus => Core.IsDictating ? "Dictating" : "Idle";

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

    static void RequestOrderlyShutdown(ShutdownKind reason)
    {
        ShutdownAction initialAction = ShutdownPolicy.Begin(reason, Core.IsDictating, Core.StopConfirmPending);
        TraceAction(reason == ShutdownKind.UserExit ? "user-exit-requested" : "fatal-tray-loss-requested");
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
        ShutdownAction action = ShutdownPolicy.Advance(Core.IsDictating, Core.StopConfirmPending, IsVoiceUiVisible());
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
            MessageBoxW(AppWindow, "Voice Typing could not be confirmed closed. Exit was cancelled so monitoring can continue.", "Voice Typing Toggle", 0x10);
            return;
        }
        MessageBoxW(AppWindow, "Voice Typing could not be confirmed closed after the notification icon was lost. It may require manual dismissal.", "Voice Typing Toggle", 0x10);
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
    // existing stop-before-launch semantics.
    static void CompleteWinHInjection(bool forceClose = false)
    {
        WinHHoldArmed = false;
        _ = KillTimer(AppWindow, WinHHoldTimerId);
        // Always finish the gesture with H before releasing Win: a bare Win-up
        // opens the Start menu. When the session ended during the hold, the H
        // opens the bar (or toggles a natively opened one) and the Escape
        // closes it again; a stray Escape reaching the app matches the
        // existing stop-before-launch semantics.
        SendKey(0, 0x23, up: false, useScanCode: true);
        SendKey(0, 0x23, up: true, useScanCode: true);
        SendKey(VK_RWIN, 0x5B, up: true, useScanCode: false, extended: true);
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
        SendKey(0, 0x01, up: false, useScanCode: true);
        SendKey(0, 0x01, up: true, useScanCode: true);
        TraceAction("escape-sent");
    }

    static void SendKey(ushort vk, ushort scan, bool up, bool useScanCode, bool extended = false)
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
            Core.RestoreIfDictating(); // T8: SendInput failure restores immediately
        }
    }

    // Physical Win+H observation (WH_KEYBOARD_LL). The hook runs BEFORE the
    // event is delivered onward, so the callback must never swallow, block, or
    // inject input; it only observes and traces here (T3 wires the dispatch).
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
    static void OnVoiceUiEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
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
        Trace.Write(GetTickCount64(), eventName, foreground, foregroundTid, foregroundHkl,
            Core.IsDictating, Core.WaitingForBar, Core.StopConfirmPending);
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
        _ = EnumWindows((h, _) =>
        {
            if (IsVoiceUiWindow(h) && IsWindowVisible(h))
            {
                found = true;
                return false; // stop enumerating
            }
            return true;
        }, 0);
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
