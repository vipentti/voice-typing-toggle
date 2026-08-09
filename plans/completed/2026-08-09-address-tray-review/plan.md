# Address PR #4 review: shutdown state, stop correction, tray ownership

## Summary

Resolve the three requested changes from the PR #4 review: make application
shutdown state escalation-capable so a tray loss during a user Exit cannot be
lost, move Voice Typing stop correction into `ToggleCore`, and remove the
test-only `TrayIconLifecycle` wrapper.

## Scope

- `ShutdownDecision` becomes the single application shutdown state: it owns
  the reason (`Kind`), the drain flag, and the correction budget. `Program`
  stops carrying parallel `ShutdownRequested` / `ShutdownReason` state.
- `ShutdownDecision.Begin` escalates a pending `UserExit` to `FatalTrayLoss`
  instead of ignoring every repeated request.
- `TaskbarCreated` is always handled: the icon is recreated, or the shutdown
  is escalated to fatal tray loss. A `CancelUserExit` outcome only cancels
  when the tray icon is installed again.
- `ToggleCore` exposes `CorrectPendingStop()`, the canonical saved-stop
  correction (Escape, settle, conditional retry, focus and layout restore).
  `Program` invokes it from its `Correct` and fatal branches instead of
  sending raw Escape.
- `TrayIconLifecycle` is deleted; `Program` inlines the add/report/request
  sequence at its two call sites.
- Tests: regression test for Explorer restart during a user Exit that would
  otherwise cancel; updated `ShutdownDecision` tests; `ToggleCore`
  `CorrectPendingStop` test; `TrayIconLifecycle` tests removed.

Out of scope: tray component extraction into a new class (tray ownership
stays in `Program` per the reviewer's stated fallback), menu or tooltip
behavior changes, `ToggleCore` semantics changes beyond the new method.

## Approach

1. Rework `ShutdownDecision`: nullable `Kind` replaces `Requested`; `Begin`
   escalates `UserExit` to `FatalTrayLoss` with a fresh correction budget
   while preserving the ongoing drain; add `Cancel()`.
2. In `Program`, replace `ShutdownRequested` and the local `ShutdownReason`
   enum with `ShutdownPolicy.Kind is null` checks and `ShutdownKind`.
   Inline tray install at startup and after `TaskbarCreated`; on recreation
   failure call `RequestOrderlyShutdown(ShutdownKind.FatalTrayLoss)`.
   `CancelUserExit` requires `TrayIconInstalled`, else escalates to fatal.
3. Add `ToggleCore.CorrectPendingStop()` wrapping `RunStopSequence(corrective:
   true)` when a stop snapshot exists and no dictation is active; use it from
   the shutdown `Correct` and `ForceFatalShutdown` branches.

## Acceptance Criteria

- A tray loss during a pending user Exit upgrades the shutdown to fatal tray
  loss: corrections continue with a fresh budget and the process terminates
  instead of cancelling back to a running state.
- `TaskbarCreated` during shutdown is processed: successful recreation keeps
  a `UserExit` cancellable; failed recreation escalates to fatal.
- Shutdown `Correct` runs the canonical saved-stop correction through
  `ToggleCore`; `Program` sends no raw Escape during shutdown.
- `TrayIconLifecycle` and its tests no longer exist.
- All build and unit tests pass; no analyzer warnings.

## Verification

- `dotnet build VoiceTypingToggle.slnx` and `dotnet test VoiceTypingToggle.slnx`
  must pass with warnings treated as errors.
- Unit tests cover escalation (tray loss during user Exit), cancellation
  gating, and the canonical correction path.
- Manual Windows smoke test of tray loss during Exit remains out of scope for
  this change set unless explicitly requested; Win32 behavior of the changed
  paths is otherwise unchanged.

## Risks and Considerations

- `RunStopSequence(corrective: true)` sleeps 100 ms inside the message loop;
  this is already accepted for the watchdog corrective pass.
- Escalation keeps the drain state; the correction budget resets once so the
  fatal path gets its own bounded attempts.
