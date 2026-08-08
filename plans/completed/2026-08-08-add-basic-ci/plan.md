# Add basic GitHub Actions CI for pull request validation

## Summary

Add GitHub Actions CI so every pull request (and push to main) builds the solution,
runs the unit tests, and verifies the Native AOT publish on Windows.

## Scope

- Add `.github/workflows/ci.yml` with a single job on `windows-latest`.
- Add `global.json` pinning the .NET SDK to `10.0.100` with `rollForward: latestMinor`.
- No changes to product code, tests, or README.

## Approach

- Triggers: `pull_request` and `push` to `main`.
- Job steps:
  1. `actions/checkout`
  2. `actions/setup-dotnet` (version resolved from `global.json`, NuGet cache enabled)
  3. `dotnet build VoiceTypingToggle.slnx` (Debug; warnings-as-errors already enforced
     by `Directory.Build.props`)
  4. `dotnet test VoiceTypingToggle.slnx`
  5. `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`
     (catches AOT/trimming errors that a plain build does not)
- Fail-fast on any step. `windows-latest` is required: the project targets
  `net10.0-windows` and Native AOT needs the Windows C++ toolchain present on the
  runner image.
- `global.json` uses `10.0.100` as the minimum with `latestMinor` roll-forward, so
  fresh images without the exact SDK still resolve a compatible one while CI stays
  deterministic within a feature band.

## Acceptance Criteria

- Opening a PR against the repository triggers the workflow automatically; the
  status check gates merge when branch protection is configured.
- CI passes on a clean checkout with warnings treated as errors (`TreatWarningsAsErrors`,
  `AnalysisMode All`).
- The publish step produces a single-file `win-x64` Native AOT executable without
  warnings.
- `dotnet build`, `dotnet test`, and `dotnet publish` still pass locally under the
  pinned SDK.

## Verification

- Locally with `global.json` present, run and confirm success of:
  - `dotnet build VoiceTypingToggle.slnx`
  - `dotnet test VoiceTypingToggle.slnx`
  - `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`
- `dotnet --version` inside the repository resolves to 10.0.2xx on the local machine
  (roll-forward) and to 10.0.100 on a runner image with no newer SDK installed.
- Workflow execution itself cannot run locally. The first live run happens on the
  first PR push; the workflow is kept minimal and hand-checked, and a failed first
  run is corrected in a follow-up push.

## Risks and Considerations

- `windows-latest` image content drifts; the pinned SDK plus `setup-dotnet` keeps
  builds reproducible.
- The AOT publish step adds roughly two minutes per run — accepted trade-off for
  catching AOT-only failures in PR validation.
