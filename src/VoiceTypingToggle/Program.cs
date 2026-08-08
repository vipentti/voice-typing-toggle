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
    const uint ModAlt = 0x0001;
    const uint ModControl = 0x0002;
    const nint HwndMessage = -3; // HWND_MESSAGE: message-only window
    const int HotkeyId = 1;
    const int TimerId = 2;
    const int FocusWatchIntervalMs = 250; // bar auto-closes on focus change; heal within a quarter second
    const uint KeyeventfExtendedKey = 0x0001;
    const uint KeyeventfKeyUp = 0x0002;
    const uint KeyeventfScanCode = 0x0008;
    const ushort VK_RWIN = 0x5C;

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
    private static partial bool SetForegroundWindow(nint hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);

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

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);
    private static readonly WndProc WndProcDelegate = WindowProc; // keep GC root for the lifetime of the class

    private delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    private static readonly WinEventProc WinEventCallback = OnVoiceUiEvent; // rooted: out-of-context hook calls back through the message loop
    private const uint InputKeyboard = 1;

    private static ToggleCore Core = null!;

    static int Main()
    {
        nint[] layouts = new nint[32];
        int count = GetKeyboardLayoutList(layouts.Length, layouts);
        nint englishLayout = count > 0 ? ToggleCore.SelectEnglishLayout(layouts, count) : 0;
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
            SendWinH = SendWinH,
            SendEscape = SendEscape,
            RestoreFocus = RestoreFocus,
            Sleep = Thread.Sleep,
            IsVoiceUiVisible = IsVoiceUiVisible,
        };

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

        nint hwnd = CreateWindowExW(0, wndClass.lpszClassName, null, 0, 0, 0, 0, 0, HwndMessage, 0, hInstance, 0);
        if (hwnd == 0)
        {
            MessageBoxW(0, $"CreateWindow failed (Win32 {Marshal.GetLastPInvokeError()}).", "Voice Typing Toggle", 0x10);
            return 1;
        }

        if (!RegisterHotKey(hwnd, HotkeyId, ModControl | ModAlt, 'H'))
        {
            MessageBoxW(0, "Could not register the Ctrl+Alt+H hotkey (it may be in use by another program).",
                "Voice Typing Toggle", 0x10);
            return 1;
        }
        _ = SetWinEventHook(EventObjectShow, EventObjectShow, 0, WinEventCallback, 0, 0, 0 /* WINEVENT_OUTOFCONTEXT */); // stop-flash watchdog
        _ = SetTimer(hwnd, TimerId, FocusWatchIntervalMs, 0);

        // Best-effort restore if the process exits while dictating.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Core.RestoreIfDictating();

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
            case WmHotkey when wParam == HotkeyId:
                Core.Toggle();
                return 0;
            case WmTimer when wParam == TimerId:
                Core.CheckDictationFocus();
                return 0;
            case WmQueryEndSession:
                Core.RestoreIfDictating();
                return 1; // allow shutdown
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
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
        return ok;
    }

    static bool RequestLayout(nint hwnd, nint hkl) =>
        SendMessageTimeout(hwnd, WmInputLangChangeRequest, 0, hkl, SmtoAbortIfHung, 1000, out _) != 0;

    static void SendWinH()
    {
        // Empirically verified recipe: left-Win injection is ignored by the shell;
        // right-Win as extended scancode fires Win-key hotkeys. H must be a scancode.
        SendKey(VK_RWIN, 0x5B, up: false, useScanCode: false, extended: true);
        Thread.Sleep(500);
        SendKey(0, 0x23, up: false, useScanCode: true);
        SendKey(0, 0x23, up: true, useScanCode: true);
        SendKey(VK_RWIN, 0x5B, up: true, useScanCode: false, extended: true);
    }

    static void SendEscape()
    {
        SendKey(0, 0x01, up: false, useScanCode: true);
        SendKey(0, 0x01, up: true, useScanCode: true);
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
            Core.RestoreIfDictating(); // T8: SendInput failure restores immediately
        }
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
        Core.OnVoiceUiShown();
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
