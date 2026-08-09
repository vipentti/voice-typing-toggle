using System.Runtime.InteropServices;
using static NativeMethods;

// VoiceTypingToggle: background utility for Windows Voice Typing with a saved
// English layout. Tray-gated features (session-only): a master "Enable
// listening" toggle gates the Ctrl+Alt+H hotkey (off by default), the
// physical Win+H race interception (on by default, opt-out), and the
// Enter/Space close keys while dictating. Each toggle is tray-only; the
// keyboard hook observes but never swallows Win+H. Program.cs owns process
// startup, the hidden message window, and dispatch; NativeMethods holds the
// Win32 interop; ToggleCore is the Idle/Dictating state machine.
sealed partial class Program
{
    // App-defined window messages (WM_APP based) and menu/timer identifiers.
    const uint WmTrayIcon = WmApp + 1;
    const uint WmCloseKeyStop = WmApp + 2;
    const uint WmWinHDown = WmApp + 3;
    const int HotkeyId = 1;
    const int TimerId = 2;
    const int WinHHoldTimerId = 3;
    const int WinHHoldMs = 500;
    const uint TrayIconId = 1;
    const int FocusWatchIntervalMs = 250; // bar auto-closes on focus change; heal within a quarter second
    const uint MenuExitId = 1;
    const uint MenuInterceptWinHId = 2;
    const uint MenuEnterCloseId = 3;
    const uint MenuSpaceCloseId = 4;
    const uint MenuListeningId = 5;
    const uint MenuHotkeyId = 6;

    private static readonly WndProc WndProcDelegate = WindowProc; // keep GC root for the lifetime of the class
    private static readonly WinEventProc WinEventCallback = OnVoiceUiEvent; // rooted: out-of-context hook calls back through the message loop
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
    private static bool ListeningEnabled = true;     // tray-gated master: hotkey, hook, and timers run only while true
    private static bool HotkeyEnabled;               // tray-gated: Ctrl+Alt+H toggle hotkey (off by default, session-only)
    private static bool InterceptWinHEnabled = true; // tray-gated: physical Win+H race interception (intent; the hook may fail to install)
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

        // The Ctrl+Alt+H hotkey is tray-gated and off by default (session-only):
        // it is registered only when "Enable Ctrl+Alt+H" is checked, so an
        // in-use combination is never a fatal startup condition.
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

}
