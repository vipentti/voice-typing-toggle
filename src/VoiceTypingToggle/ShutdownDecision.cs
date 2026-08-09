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

// Pure policy for the coordinator. Program owns Win32 teardown and supplies the
// observed core and Voice Typing UI state on every timer or popup callback.
internal sealed class ShutdownDecision
{
    const int CorrectionLimit = 2;
    ShutdownKind kind;
    bool needsDrain;
    int corrections;

    public bool Requested { get; private set; }

    public ShutdownAction Begin(ShutdownKind requestedKind, bool isDictating, bool stopConfirmPending)
    {
        if (Requested)
        {
            return ShutdownAction.Wait;
        }
        Requested = true;
        kind = requestedKind;
        needsDrain = isDictating || stopConfirmPending;
        return needsDrain ? ShutdownAction.Wait : ShutdownAction.Complete;
    }

    public ShutdownAction Advance(bool isDictating, bool stopConfirmPending, bool voiceUiVisible)
    {
        if (!Requested || !needsDrain)
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
        if (kind == ShutdownKind.UserExit)
        {
            Requested = false;
            needsDrain = false;
            corrections = 0;
            return ShutdownAction.CancelUserExit;
        }
        return ShutdownAction.ForceFatalShutdown;
    }
}
