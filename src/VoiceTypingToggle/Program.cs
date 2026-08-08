using System.Runtime.InteropServices;

// VoiceTypingToggle: background utility. Ctrl+Alt+H toggles English voice typing:
//   Idle -> save foreground layout, switch to en-US, Win+H (Voice Typing opens)
//   Dictating -> Win+H (closes), restore saved layout to foreground at that time
sealed partial class Program
{
    const int PollIntervalMs = 10;   // T5: measured switches complete in <1 ms; 10 ms keeps polling cheap
    const int SwitchTimeoutMs = 100;  // T5: 100x margin over observed <1 ms switches; unhonored apps never switch
    const uint WmInputLangChangeRequest = 0x0050;
    const uint SmtoAbortIfHung = 0x0002;
    const uint LangEnUs = 0x0409;
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

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

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
    private const uint InputKeyboard = 1;

    private static nint _englishLayout;
    private static nint _savedLayout;
    private static nint _savedWindow;
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
        _ = SetTimer(hwnd, TimerId, FocusWatchIntervalMs, 0);

        // Best-effort restore if the process exits while dictating.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreIfDictating();

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
                Toggle();
                return 0;
            case WmTimer when wParam == TimerId:
                CheckDictationFocus();
                return 0;
            case WmQueryEndSession:
                RestoreIfDictating();
                return 1; // allow shutdown
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // Self-healing state: the Voice Typing bar auto-closes on any focus change,
    // so once focus leaves the dictation target the state is stale — restore the
    // saved layout and go Idle. The next hotkey press then starts fresh.
    static void CheckDictationFocus()
    {
        if (_dictating && GetForegroundWindow() != _savedWindow)
        {
            RestoreIfDictating();
        }
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
            if (!RequestLayout(hwnd, _englishLayout) || !WaitForLayout(tid, _englishLayout, SwitchTimeoutMs))
            {
                return; // stay Idle
            }
        }
        _savedLayout = current;
        _savedWindow = hwnd;
        _dictating = true;
        SendWinH();
    }

    static void StopDictation()
    {
        SendEscape();
        Thread.Sleep(100);
        // Retry only if the bar is still there: when Escape #1 worked, the bar's close
        // moves foreground away from the saved window — a second Escape would then hit
        // whatever the shell raised (an unrelated app).
        if (GetForegroundWindow() == _savedWindow)
        {
            SendEscape(); // apps like terminals consume the first Escape as a control char
        }
        // The bar close drops foreground (often to 0 momentarily); claim the saved
        // window right then, before the shell raises its own candidate (Start, MRU).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 300 && GetForegroundWindow() != 0)
        {
            Thread.Sleep(10);
        }
        if (_savedWindow != 0)
        {
            _ = RestoreFocus(_savedWindow); // restore focus first, then layout: shortest visible blip
        }
        if (_savedLayout != 0)
        {
            uint tid = GetWindowThreadProcessId(_savedWindow, out _);
            if (GetKeyboardLayout(tid) != _savedLayout)
            {
                _ = RequestLayout(_savedWindow, _savedLayout);
            }
        }
        _savedLayout = 0;
        _savedWindow = 0;
        _dictating = false;
    }

    static void RestoreIfDictating()
    {
        if (!_dictating)
        {
            return;
        }
        nint target = _savedWindow != 0 ? _savedWindow : GetForegroundWindow();
        if (target != 0 && _savedLayout != 0)
        {
            _ = RequestLayout(target, _savedLayout);
        }
        _savedLayout = 0;
        _savedWindow = 0;
        _dictating = false;
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

    static bool WaitForLayout(uint tid, nint expected, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (GetKeyboardLayout(tid) == expected)
            {
                return true;
            }
            Thread.Sleep(PollIntervalMs);
        }
        return false;
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
