using System.Runtime.InteropServices;
using System.Text;

// Spike increment: prove foreground-thread layout reading works.
// Prints, once per second: pid, thread id, HKL, and window title of the
// foreground window. Ctrl+C to exit.
partial class Program
{
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(nint hWnd, char[] text, int maxCount);

    static void Main()
    {
        Console.WriteLine("VoiceTypingToggle spike: foreground thread id + HKL, sampled every 1 s. Ctrl+C to exit.");
        while (true)
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == 0)
            {
                Console.WriteLine("no foreground window");
            }
            else
            {
                uint tid = GetWindowThreadProcessId(hwnd, out uint pid);
                nint hkl = GetKeyboardLayout(tid);
                var title = new char[256];
                int len = GetWindowText(hwnd, title, title.Length);
                Console.WriteLine($"pid={pid} tid={tid} hkl=0x{hkl:X8} title={new string(title, 0, Math.Max(0, len))}");
            }
            Thread.Sleep(1000);
        }
    }
}
