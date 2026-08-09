# Tasks: Split Program.cs into focused modules

- [x] T1 Ignore temp directories: move tracked docs `tmp/race-evidence.md` and `tmp/winh-interception-research.md` to `docs/`, remove the tracked originals, and add `tmp/` and `:TEMP/` to `.gitignore`. Verify `git status` shows no temp paths and `git check-ignore` matches both patterns.
- [x] T2 Extract Win32 interop: move every constant, `[LibraryImport]`/`[DllImport]` declaration, struct, and delegate from Program.cs into `internal static class NativeMethods` in `NativeMethods.cs`, preserving signatures, CharSet, and `DllImportSearchPaths`. Verify `dotnet build VoiceTypingToggle.slnx` and `dotnet test VoiceTypingToggle.slnx` pass with zero warnings.
- [x] T3 Split Program into partial concern files: `Program.Tray.cs`, `Program.Input.cs`, `Program.KeyboardHook.cs`, `Program.Shutdown.cs`, `Program.VoiceUi.cs`, `Program.Messages.cs`; Program.cs keeps Main, WindowProc dispatch, fields, and the rooted native callback delegates. Verify build and test pass and the diff is moves only.
- [x] T4 Update the stale top-of-file comment in Program.cs to describe current behavior: tray-gated Ctrl+Alt+H hotkey (off by default), physical Win+H race interception, Enter/Space close keys, listening master toggle. Verify build and test pass.
- [x] T5 Publish check: `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release` succeeds and produces the single-file Native AOT executable.
- [x] T6 Manual Windows verification: Ctrl+Alt+H toggle with hotkey enabled, physical Win+H race start and native stop, Enter/Space close keys while dictating, tray menu toggles including listening master and sub-toggle gray-out, focus-loss heal, shutdown while dictating, and a non-Finnish starting layout. Record per-case outcomes in the PR description.
- [x] T7 Update references to the moved research docs: replace `tmp/winh-interception-research.md` and `tmp/race-evidence.md` paths with the `docs/` equivalents in `plans/completed/2026-08-09-winh-race-intercept/plan.md`, `plans/completed/2026-08-09-winh-race-intercept/tasks.md`, and `docs/voice-typing-toggle-concept.md`. Verify no remaining `tmp/` references to the moved docs (`.tmp/` gitignored scratch references in the stop-flash plan stay as historical record).

## Completion

- Completed at: 2026-08-09T18:57:19.902Z
- Mode: normal
