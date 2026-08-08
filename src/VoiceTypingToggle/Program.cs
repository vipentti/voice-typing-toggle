using System.Diagnostics;
using System.Runtime.InteropServices;

// Spike increment: select an English layout from the installed list, switch the
// foreground application to it via WM_INPUTLANGCHANGEREQUEST, poll until the
// foreground thread reports it, and print elapsed milliseconds and outcome per
// application. Each measured switch is immediately restored so testing leaves
// the desktop in its original language. Ctrl+C to exit.
partial class Program
{
    const int PollIntervalMs = 10;   // placeholder until T5 sets from measured data
    const int SwitchTimeoutMs = 250; // placeholder until T5 sets from measured data
    const uint WmInputLangChangeRequest = 0x0050;
    const uint SmtoAbortIfHung = 0x0002;
    const uint LangEnUs = 0x0409;

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

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(nint hWnd, char[] text, int maxCount);

    static void Main()
    {
        nint[] layouts = new nint[32];
        int count = GetKeyboardLayoutList(layouts.Length, layouts);
        if (count <= 0)
        {
            Console.WriteLine("error: no installed keyboard layouts");
            return;
        }
        nint english = SelectEnglishLayout(layouts, count);
        if (english == 0)
        {
            Console.WriteLine("error: no English keyboard layout installed");
            return;
        }
        Console.WriteLine("installed: " + string.Join(", ", layouts[..count].Select(h => $"0x{h:X8}")));
        Console.WriteLine($"english target: 0x{english:X8} (en-US preferred, any en-* fallback). Ctrl+C to exit.");

        uint lastTid = 0;
        while (true)
        {
            nint hwnd = GetForegroundWindow();
            uint tid = GetWindowThreadProcessId(hwnd, out uint pid);
            if (tid != 0 && tid != lastTid)
            {
                lastTid = tid;
                nint hkl = GetKeyboardLayout(tid);
                var title = new char[256];
                int len = GetWindowText(hwnd, title, title.Length);
                Console.WriteLine($"pid={pid} tid={tid} hkl=0x{hkl:X8} title={new string(title, 0, Math.Max(0, len))}");

                if (hkl == english)
                {
                    Console.WriteLine("  already english");
                    continue;
                }
                if (RequestLayout(hwnd, english))
                {
                    int ms = WaitForLayout(tid, english, SwitchTimeoutMs);
                    if (ms >= 0)
                    {
                        Console.WriteLine($"  -> english 0x{english:X8} in {ms} ms");
                        if (RequestLayout(hwnd, hkl))
                        {
                            int rms = WaitForLayout(tid, hkl, SwitchTimeoutMs);
                            Console.WriteLine(rms >= 0
                                ? $"  -> restored 0x{hkl:X8} in {rms} ms"
                                : "  -> restore timed out");
                        }
                        else
                        {
                            Console.WriteLine("  restore request failed");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  -> english switch timed out after {SwitchTimeoutMs} ms");
                    }
                }
                else
                {
                    Console.WriteLine("  switch request failed");
                }
            }
            Thread.Sleep(1000);
        }
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
        var sw = Stopwatch.StartNew();
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
}
