# Tasks: Add basic GitHub Actions CI for pull request validation

- [x] T1 Add `global.json` pinning SDK `10.0.100` with `rollForward: latestMinor`; confirm `dotnet build VoiceTypingToggle.slnx`, `dotnet test VoiceTypingToggle.slnx`, and `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release` all pass locally under it.
- [x] T2 Add `.github/workflows/ci.yml`: `pull_request` + `push` to `main` triggers, `windows-latest` runner, `actions/setup-dotnet` with NuGet cache, then build, test, and Release Native AOT publish steps.
- [x] T3 Hand-check workflow YAML (indentation, step names, exact commands match the local verification commands) and report that the first live CI run happens on the first PR push.

## Completion

- Completed at: 2026-08-08T13:08:44.130Z
- Mode: normal
