internal enum ShutdownKind
{
    UserExit,
    FatalTrayLoss,
}

internal enum ShutdownAction
{
    Wait,
    Complete,
    Correct,
    CancelUserExit,
    ForceFatalShutdown,
}

// Single escalation-capable application shutdown state. Program owns Win32
// teardown and supplies the observed core and Voice Typing UI state on every
// timer or popup callback. A fatal tray loss upgrades a pending user exit:
// the application must never continue running without its notification icon.
internal sealed class ShutdownDecision
{
    const int CorrectionLimit = 2;
    bool needsDrain;
    int corrections;

    public ShutdownKind? Kind { get; private set; }

    public ShutdownAction Begin(ShutdownKind requestedKind, bool isDictating, bool stopConfirmPending)
    {
        if (Kind is { } current)
        {
            // Fatal tray loss upgrades any pending user exit; a repeated or
            // weaker request continues the existing shutdown.
            if (current == ShutdownKind.FatalTrayLoss || requestedKind == ShutdownKind.UserExit)
            {
                return ShutdownAction.Wait;
            }
            corrections = 0; // fresh correction budget for the stronger failure mode
            Kind = requestedKind;
            return ShutdownAction.Wait;
        }
        Kind = requestedKind;
        needsDrain = isDictating || stopConfirmPending;
        corrections = 0;
        return needsDrain ? ShutdownAction.Wait : ShutdownAction.Complete;
    }

    public ShutdownAction Advance(bool isDictating, bool stopConfirmPending, bool voiceUiVisible)
    {
        if (Kind is null || !needsDrain)
        {
            return ShutdownAction.Complete;
        }
        if (!isDictating && !stopConfirmPending && !voiceUiVisible)
        {
            return ShutdownAction.Complete;
        }
        if (stopConfirmPending)
        {
            return ShutdownAction.Wait;
        }
        if (corrections++ < CorrectionLimit)
        {
            return ShutdownAction.Correct;
        }
        if (Kind == ShutdownKind.UserExit)
        {
            return ShutdownAction.CancelUserExit;
        }
        return ShutdownAction.ForceFatalShutdown;
    }

    // The coordinator cancels only after confirming the tray icon is
    // installed again; a cancelled user exit must stay visible.
    public void Cancel()
    {
        Kind = null;
        needsDrain = false;
        corrections = 0;
    }
}
