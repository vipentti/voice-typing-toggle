# Voice Typing Toggle

## Project orientation

Voice Typing Toggle is a small Windows-only background utility. It temporarily
switches the target application's active keyboard layout to English, opens
Windows Voice Typing, and restores the actual layout that was active before
dictation.

Read `docs/voice-typing-toggle-concept.md` before product-affecting work. Its
goals, non-goals, security constraints, and acceptance criteria are product
guardrails. Sections describing proposed designs, future enhancements, and open
questions are context rather than automatically current requirements.

The concept document records product intent; the implementation and tests record
current behavior; an active planlet records the scope of work in progress. If
these disagree, identify and reconcile the discrepancy instead of silently
choosing one source.

## Product guardrails

- Keep this a single-purpose voice-typing helper, not a general keyboard
  automation, remapping, or speech-recognition framework.
- Preserve ordinary user-level operation. Do not introduce administrator
  requirements, services, drivers, DLL injection, background networking, UI
  automation, or low-level keyboard hooks unless the task explicitly requires
  them.
- Do not intercept the physical `Win+H` shortcut unless that future feature is
  explicitly requested. The normal toggle hotkey is `Ctrl+Alt+H`.
- Never log dictated text, typed content, or captured keystrokes.
- Save and restore the actual active `HKL`; never assume the original layout is
  Finnish. Keyboard layouts belong to input threads, not globally to a process
  or the operating system.
- Fail closed: do not start Voice Typing unless the requested English layout is
  confirmed active. On failure, stop, focus loss, or normal shutdown, make a
  best-effort attempt to restore the saved state.
- Prefer direct Win32 interop and avoid new production dependencies unless they
  provide a clear benefit without compromising Native AOT or the small
  standalone executable.

## Current architecture

- `src/VoiceTypingToggle/Program.cs` owns process startup, the hidden Win32
  message window, P/Invoke declarations, global hotkey and timer dispatch,
  synthetic input, focus operations, and user-visible startup errors.
- `src/VoiceTypingToggle/ToggleCore.cs` owns English-layout selection and the
  Idle/Dictating state machine. Keep it free of direct Win32 calls; operating
  system interactions should remain injected seams that unit tests can fake.
- `tests/VoiceTypingToggle.Tests/ToggleCoreTests.cs` covers layout selection,
  transitions, failure behavior, restoration, and focus-loss recovery.

The required stop outcome is to close Voice Typing and safely restore state. Do
not infer the exact stop keystroke or restoration target from the concept alone:
the current implementation uses Escape, restores the saved starting window, and
self-heals when focus leaves that window. Changes to those semantics require
corresponding tests and documentation updates.

## Windows interop and deployment

- The application targets C# on `net10.0-windows` and publishes as a `WinExe`,
  Native AOT, single-file `win-x64` executable.
- Preserve Native AOT compatibility. Avoid reflection-heavy or dynamically
  generated behavior that depends on an unavailable runtime.
- Prefer source-generated `[LibraryImport]` declarations. Keep
  `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` on Windows system
  imports.
- `AllowUnsafeBlocks` is required by source-generated P/Invoke code; it does not
  mean handwritten unsafe blocks are expected.
- Do not switch layouts by simulating `Win+Space` or relying on the user's layout
  ordering. Address the relevant foreground or saved window input thread.
- Synthetic input from this unelevated process is not expected to control
  elevated applications.

## Verification

Warnings and analyzer findings are treated as errors. For code changes, run:

```powershell
dotnet build VoiceTypingToggle.slnx
dotnet test VoiceTypingToggle.slnx
```

For publishing or deployment changes, also run:

```powershell
dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release
```

Changes involving hotkeys, focus, layouts, timers, `SendInput`, or the Voice
Typing UI also require proportionate manual testing on Windows. Relevant cases
include Finnish and non-Finnish starting layouts, failed English-layout
activation, repeated toggles, focus changes, shutdown restoration, and normal
non-elevated desktop applications. Do not treat unit tests alone as proof that
Win32 interaction behavior works.

<!-- BEGIN PLANLET AGENTS v:1 hash:924c8d1f -->
## Planning with Planlet

This repository uses Planlet for focused implementation plans. A planlet is
`plans/<slug>/plan.md` + `tasks.md`; Markdown is the source of truth.

- Propose a planlet before multi-step work; skip it for one-file changes.
- Drive it with the `planlet` CLI, never by hand-editing plan files:
  `planlet create|show|tasks|status|validate <slug>`,
  `planlet task check <slug> <task-id>`, `planlet complete <slug>`.
- Check each task off only after its verification passes. When the last task is
  checked, run `planlet complete <slug>` to archive it.
- Run `planlet help [command]` before using a command you have not used here.
- If no `planlet` executable is available, stop and say so. Do not hand-create
  or hand-edit planlet files.
<!-- END PLANLET AGENTS -->
