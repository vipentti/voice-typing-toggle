# Split Program.cs into focused modules

## Summary

Program.cs is a 1430-line monolith mixing Win32 interop, the message loop,
tray UI, input synthesis, keyboard hook, and shutdown orchestration. Extract
the interop into its own class, split the remaining message-loop side into a
partial class across concern files, and ignore the repository's temp
directories so they stop polluting working-tree status.

## Scope

- Extract all constants, `[LibraryImport]`/`[DllImport]` declarations,
  structs, and delegates from Program.cs into an `internal static class
  NativeMethods` in a new file `NativeMethods.cs`. Signatures, CharSet,
  `DllImportSearchPaths`, and struct layouts stay byte-identical.
- Split Program into a partial class across concern files: `Program.cs`
  (Main, WindowProc dispatch skeleton, static fields), `Program.Tray.cs`
  (tray icon plumbing, tray menu, toggles), `Program.Input.cs` (SendKey,
  SendEscape, SendWinH, CompleteWinHInjection, RestoreFocus, RequestLayout*),
  `Program.KeyboardHook.cs` (hook lifecycle, LowLevelKeyboardProc, observation
  dispatch), `Program.Shutdown.cs` (RequestOrderlyShutdown,
  ContinueShutdownIfNeeded, CompleteShutdown), `Program.VoiceUi.cs`
  (OnVoiceUiEvent, window matching, TraceAction), `Program.Messages.cs`
  (tray/taskbar message handlers, point helpers).
- Update the stale top-of-file comment to describe current behavior
  (Ctrl+Alt+H hotkey, physical Win+H race interception, close keys, tray
  toggles).
- .gitignore: add `tmp/` and `:TEMP/`; move the two tracked research docs
  out of `tmp/` into `docs/` so ignoring the directory loses nothing.
- Update references to the moved docs (`tmp/winh-interception-research.md`,
  `tmp/race-evidence.md`) in the completed winh-race-intercept planlet and in
  `docs/voice-typing-toggle-concept.md`. Historical `.tmp/` scratch references
  in the completed stop-flash plan stay as-is.
- No behavior change. File organization only.

## Approach

1. Housekeeping first: move `tmp/race-evidence.md` and
   `tmp/winh-interception-research.md` to `docs/`, remove the tracked
   originals, ignore `tmp/` and `:TEMP/`.
2. Mechanical interop extraction (~350 lines) into one file-scoped static
   class. No renames, no modernization of the remaining `[DllImport]`
   declarations.
3. Partial split by cohesion; only the `partial` keyword and file moves
   change. No field or method signature changes. The three GC-rooted native
   callback delegates stay static readonly fields in Program.cs.
4. Header comment rewrite, then publish and manual verification.

## Acceptance Criteria

- `dotnet build VoiceTypingToggle.slnx` and
  `dotnet test VoiceTypingToggle.slnx` pass with zero warnings and analyzer
  findings (treated as errors).
- Every P/Invoke, struct, delegate, and interop constant that was in
  Program.cs now lives in NativeMethods.cs; Program.cs keeps none.
- The diff is moves only: no logic line changed in the split steps.
- `git status` shows no `tmp/` or `:TEMP/` paths; both patterns are ignored.
- `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`
  produces the Native AOT single-file executable.
- Manual Windows pass confirms hotkey, physical Win+H race start/stop,
  Enter/Space close keys, tray toggles, focus-loss heal, and shutdown
  restoration still behave as before.

## Verification

Strategy: run the AGENTS.md build and test commands after each split step
and after the final edit; treat any warning or analyzer finding as failure.
Review the split diffs for move-only content (Program.cs deletions must match
new-file additions). Run the Release publish as the AOT gate. Manual Windows
verification is required because the changes touch hotkeys, focus, layouts,
timers, SendInput, and the Voice Typing UI; the pass records per-case
outcomes in the PR description, and a `VTT_TRACE=1` run supports the
hotkey/layout/focus cases where useful. No `## Verification Evidence` section
is planned: routine results stay in build, test, and review history.

## Risks and Considerations

- Native callback delegates (WndProcDelegate, KeyboardProcDelegate,
  WinEventCallback) must remain static readonly fields rooted for the class
  lifetime; the split must not move or change them.
- All partial files must use the identical class declaration
  (`sealed partial class Program`), default internal visibility.
- Interop extraction is mechanical; any signature drift would silently change
  behavior, so the diff must be reviewed move-only.
