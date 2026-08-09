# Tasks: Address PR #4 review

- [x] T1 Make `ShutdownDecision` the single escalation-capable shutdown state: nullable `Kind`, escalation in `Begin`, `Cancel()`, drop `Requested`
- [x] T2 Add `ToggleCore.CorrectPendingStop()` running the canonical saved-stop correction when a stop snapshot exists
- [x] T3 Update `Program`: replace `ShutdownRequested`/`ShutdownReason` with `ShutdownPolicy.Kind`, always handle `TaskbarCreated`, gate cancellation on installed tray, use `Core.CorrectPendingStop()` for `Correct` and fatal branches
- [x] T4 Delete `TrayIconLifecycle.cs` and inline tray install/recreate in `Program`
- [x] T5 Update tests: escalation regression, cancellation gating, `CorrectPendingStop`, remove `TrayIconLifecycle` tests
- [x] T6 Run `dotnet build` and `dotnet test` on the solution and confirm all pass

## Completion

- Completed at: 2026-08-09T12:03:31.214Z
- Mode: normal
