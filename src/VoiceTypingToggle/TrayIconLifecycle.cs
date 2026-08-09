// Keeps the decision after a native notification-icon add attempt separate
// from the Shell_NotifyIconW call. Tests can inject an add failure without
// depending on Explorer, while Program remains the owner of Win32 interop.
internal sealed class TrayIconLifecycle
{
    readonly Func<bool> tryAdd;
    readonly Action<bool> reportFailure;
    readonly Action requestOrderlyShutdown;

    public TrayIconLifecycle(Func<bool> tryAdd, Action<bool> reportFailure, Action requestOrderlyShutdown)
    {
        this.tryAdd = tryAdd;
        this.reportFailure = reportFailure;
        this.requestOrderlyShutdown = requestOrderlyShutdown;
    }

    public bool Install()
    {
        return TryAdd(isRecreation: false);
    }

    public void RecreateAfterTaskbarRestart()
    {
        _ = TryAdd(isRecreation: true);
    }

    bool TryAdd(bool isRecreation)
    {
        if (tryAdd())
        {
            return true;
        }

        reportFailure(isRecreation);
        requestOrderlyShutdown();
        return false;
    }
}
