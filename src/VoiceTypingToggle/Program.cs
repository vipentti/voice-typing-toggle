using System.Runtime.InteropServices;

// VoiceTypingToggle: background utility. Ctrl+Alt+H toggles English voice typing:
//   Idle -> save foreground layout, switch to en-US, Win+H (Voice Typing opens)
//   Dictating -> Win+H (closes), restore saved layout to foreground at that time
partial class Program
{
    const int PollIntervalMs = 10;   // T5: measured switches complete in <1 ms; 10 ms keeps polling cheap
    const int SwitchTimeoutMs = 100;  // T5: 100x margin over observed <1 ms switches; unhonored apps never switch
    const uint WmInputLangChangeRequest = 0x0050;
    const uint SmtoAbortIfHung = 0x0002;
    const uint LangEnUs = 0x0409;
    const uint WmHotkey = 0x0312;
    const uint WmQueryEndSession = 0x0011;
    const uint WmEndSession = 0x0016;
    const uint WmDestroy = 0x0002;
    const uint ModAlt = 0x0001;
    const uint ModControl = 0x0002;
    const nint HwndMessage = -3; // HWND_MESSAGE: message-only window
    const int HotkeyId = 1;
    const uint KeyeventfExtendedKey = 0x0001;
    const uint KeyeventfKeyUp = 0x0002;
    const uint KeyeventfScanCode = 0x0008;
    const ushort VK_RWIN = 0x5C;

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    private static partial int GetKeyboardLayoutList(int nBuff, nint[] list);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static partial nint SendMessageTimeout(
        nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW wndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

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
    private const uint InputKeyboard = 1;

    private static nint _englishLayout;
    private static nint _savedLayout;
    private static bool _dictating;

    static int Main()
    {
        nint[] layouts = new nint[32];
        int count = GetKeyboardLayoutList(layouts.Length, layouts);
        if (count <= 0 || (nint)(_englishLayout = SelectEnglishLayout(layouts, count)) == 0)
        {
            MessageBoxW(0, "No English keyboard layout is installed. Voice Typing Toggle cannot start.",
                "Voice Typing Toggle", 0x10 /* MB_ICONERROR */);
            return 1;
        }

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

        // Best-effort restore if the process exits while dictating.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreIfDictating();

        while (GetMessageW(out MSG msg, 0, 0, 0) > 0)
        {
            _ = TranslateMessage(in msg);
            _ = DispatchMessageW(in msg);
        }
        _ = UnregisterHotKey(hwnd, HotkeyId);
        _ = DestroyWindow(hwnd);
        return 0;
    }

    static nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmHotkey when wParam == HotkeyId:
                Toggle();
                return 0;
            case WmQueryEndSession:
                RestoreIfDictating();
                return 1; // allow shutdown
            case WmDestroy:
                _ = DefWindowProcW(hWnd, msg, wParam, lParam);
                return 0;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    static void Toggle()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            return;
        }
        if (!_dictating)
        {
            StartDictation(hwnd);
        }
        else
        {
            StopDictation();
        }
    }

    static void StartDictation(nint hwnd)
    {
        uint tid = GetWindowThreadProcessId(hwnd, out _);
        nint current = GetKeyboardLayout(tid);

        // Fail closed: only start voice typing after the English layout is confirmed active.
        if (current != _englishLayout)
        {
            if (!RequestLayout(hwnd, _englishLayout) || WaitForLayout(tid, _englishLayout, SwitchTimeoutMs) < 0)
            {
                return; // stay Idle
            }
        }
        _savedLayout = current;
        _dictating = true;
        SendWinH();
    }

    static void StopDictation()
    {
        SendEscape(); // closes the Voice Typing bar (Win+H alone only stops listening on this build)
        Thread.Sleep(300); // let the bar close so focus settles
        nint hwnd = GetForegroundWindow();
        if (hwnd != 0 && _savedLayout != 0)
        {
            uint tid = GetWindowThreadProcessId(hwnd, out _);
            if (GetKeyboardLayout(tid) != _savedLayout)
            {
                _ = RequestLayout(hwnd, _savedLayout);
            }
        }
        _savedLayout = 0;
        _dictating = false;
    }

    static void RestoreIfDictating()
    {
        if (!_dictating)
        {
            return;
        }
        nint hwnd = GetForegroundWindow();
        if (hwnd != 0 && _savedLayout != 0)
        {
            uint tid = GetWindowThreadProcessId(hwnd, out _);
            _ = RequestLayout(hwnd, _savedLayout);
        }
        _savedLayout = 0;
        _dictating = false;
    }

    // Pure selection logic: exact en-US first, then any English primary language.
    static nint SelectEnglishLayout(nint[] layouts, int count)
    {
        nint fallback = 0;
        for (int i = 0; i < count; i++)
        {
            uint lang = (uint)layouts[i] & 0xFFFF;
            if (lang == LangEnUs)
            {
                return layouts[i];
            }
            if ((lang & 0xFF) == 0x09 && fallback == 0)
            {
                fallback = layouts[i];
            }
        }
        return fallback;
    }

    static bool RequestLayout(nint hwnd, nint hkl) =>
        SendMessageTimeout(hwnd, WmInputLangChangeRequest, 0, hkl, SmtoAbortIfHung, 1000, out _) != 0;

    static int WaitForLayout(uint tid, nint expected, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (GetKeyboardLayout(tid) == expected)
            {
                return (int)sw.ElapsedMilliseconds;
            }
            Thread.Sleep(PollIntervalMs);
        }
        return -1;
    }

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
            RestoreIfDictating(); // T8: SendInput failure restores immediately
        }
    }
}
