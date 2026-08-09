// Permanent opt-in diagnostics. The trace contains state/action metadata only:
// never window titles, document names, dictated text, typed content, or keys.
internal sealed class DiagnosticTrace : IDisposable
{
    const string DefaultDirectoryName = "VoiceTypingToggle";
    const string DefaultFileName = "trace.csv";

    readonly StreamWriter? writer;
    readonly object sync = new();
    bool dirty;

    DiagnosticTrace(StreamWriter? writer) => this.writer = writer;

    public static DiagnosticTrace Disabled { get; } = new(null);
    public bool Enabled => writer is not null;

    public static DiagnosticTrace CreateFromEnvironment()
    {
        string? setting = Environment.GetEnvironmentVariable("VTT_TRACE");
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        return Create(setting, localAppData);
    }

    internal static DiagnosticTrace Create(string? setting, string localAppData)
    {
        try
        {
            string? path = ResolvePath(setting, localAppData);
            if (path is null)
            {
                return Disabled;
            }
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite
            );
            var traceWriter = new StreamWriter(stream) { AutoFlush = false };
            traceWriter.WriteLine(
                "tick,event,foreground,foregroundTid,foregroundHkl,isDictating,waitingForBar,stopConfirmPending"
            );
            traceWriter.Flush();
            return new DiagnosticTrace(traceWriter);
        }
        catch (ArgumentException)
        {
            return Disabled;
        }
        catch (IOException)
        {
            return Disabled;
        }
        catch (UnauthorizedAccessException)
        {
            return Disabled;
        }
        catch (NotSupportedException)
        {
            return Disabled;
        }
    }

    internal static string? ResolvePath(string? setting, string localAppData)
    {
        if (string.IsNullOrWhiteSpace(setting) || setting == "0")
        {
            return null;
        }
        string path =
            setting == "1"
                ? Path.Combine(localAppData, DefaultDirectoryName, DefaultFileName)
                : setting;
        return Path.GetFullPath(path);
    }

    public void Write(
        ulong tick,
        string eventName,
        nint foreground,
        uint foregroundTid,
        nint foregroundHkl,
        bool isDictating,
        bool waitingForBar,
        bool stopConfirmPending
    )
    {
        if (writer is null)
        {
            return;
        }
        string line = FormattableString.Invariant(
            $"{tick},{eventName},0x{foreground:X},{foregroundTid},0x{foregroundHkl:X},{isDictating},{waitingForBar},{stopConfirmPending}"
        );
        lock (sync)
        {
            writer.WriteLine(line);
            dirty = true;
        }
    }

    public void Flush()
    {
        if (writer is null)
        {
            return;
        }
        lock (sync)
        {
            if (dirty)
            {
                writer.Flush();
                dirty = false;
            }
        }
    }

    public void Dispose()
    {
        if (writer is null)
        {
            return;
        }
        lock (sync)
        {
            writer.Dispose();
            dirty = false;
        }
    }
}
