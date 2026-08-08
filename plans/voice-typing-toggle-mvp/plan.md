# Voice Typing Toggle MVP (v0.1)

## Summary

Build the first working version of VoiceTypingToggle.exe: a background Windows
utility that, on a global `Ctrl+Alt+H` hotkey, saves the foreground application's
current keyboard layout, switches it to `en-US`, and invokes Windows Voice Typing
via a synthesized `Win+H`; a second press sends `Win+H` again and restores the
saved layout. Delivered as a self-contained .NET 10 Native AOT executable with no
third-party runtime dependencies.

The design is specified in `docs/voice-typing-toggle-concept.md`; this planlet
implements the "MVP Scope / Version 0.1" section of that document.

## Scope

- Two feasibility gates executed before implementation, either of which can
  invalidate the design.
- One project `src/VoiceTypingToggle`, started as a console executable for the
  spike increments and switched to a hidden `WinExe` once the toggle is wired.
- P/Invoke layer using `[LibraryImport]` source generation, grown per increment
  rather than declared upfront.
- Foreground-thread layout reading, `WM_INPUTLANGCHANGEREQUEST` switching with
  confirmation polling and measured latency, `SendInput` for `Win+H`,
  `RegisterHotKey` with a hidden message-only window, and the Idle/Dictating
  toggle.
- A test project added once the pure logic exists, covering English-layout
  selection and state-machine transitions.
- Native AOT publish configuration for `win-x64`.

Boundaries: hotkey, target language, and timing values are compile-time constants
(`Ctrl+Alt+H`, `en-US`, poll interval and timeout set from measured data). No
config file, no tray icon, no logging, no class-per-responsibility split — the
concept document already defers all four, and they are added only if the
background process becomes inconvenient in practice.

## Out of Scope

Deliberately deferred; each is a candidate follow-on planlet once the MVP has been
used in practice. Planlet has no parent/child structure, so this list is the
roadmap record and later planlets are created separately:

- **Configuration** — JSON config for hotkey and voice language, with defaults.
- **Tray icon** — Win32 `Shell_NotifyIcon` status/exit UI, plus start-with-Windows.
- **Packaging and signing** — release workflow, `win-arm64`, code signing.
- **Physical `Win+H` interception** — `WH_KEYBOARD_LL` hook with suppression and
  reinjection.
- **Robust dictation-state detection** — only if the optimistic toggle state model
  proves unreliable in daily use.

## Approach

Two gates run first, on the machine where the utility will actually be used:

1. **An unsigned Native AOT executable runs there.** Build a hello-world AOT
   executable and run it on that machine. If it is blocked from running, the
   entire deployment model is dead and no other work is worth doing.
2. **An English layout is installed there.** Confirmed via
   `GetKeyboardLayoutList` output or the language bar.

Only then does implementation start, in increments that are each independently
runnable and observable without a state machine:

1. Console app printing the foreground window's thread and `HKL`. Proves
   thread-layout reading works at all.
2. Add `WM_INPUTLANGCHANGEREQUEST` to the English layout, poll
   `GetKeyboardLayout` until it takes, print elapsed milliseconds. This produces
   real numbers for the concept document's open questions 1-3 — which applications
   honor the message, how fast the change becomes observable, and whether polling
   is needed at all — replacing the 50-150 ms guess with measurement. Poll interval
   and timeout constants are then chosen from the observed data.
3. Add `SendInput` for `Win+H`. Confirms Voice Typing actually opens after a
   confirmed switch.
4. Wrap the working pieces in `RegisterHotKey`, a hidden message-only window, and
   the two-state Idle/Dictating toggle; switch the project to `WinExe`.

English-layout selection reads the `HKL` list and matches on the low-word language
identifier: exact `en-US` (0x0409) first, then any other `en-*` primary-language
match, otherwise none — treated as a hard stop by callers. This and the toggle
state machine are the only pure logic, and they sit behind narrow seams so they can
be unit-tested; everything else is thin Win32 wrapping verified by running it.

On stop, `Win+H` is sent first and the saved layout is restored last. Closing Voice
Typing can shift focus, so restoring last means the restore lands on whatever
thread ends up foreground — which is also the concept document's "restore to
foreground at toggle time" rule, and resolves its open question on ordering.

Start-side failures leave the utility Idle and never send `Win+H`. A `SendInput`
failure after a confirmed switch restores the saved layout immediately. Shutdown
while Dictating performs a best-effort restore and unregisters the hotkey. Blocking
errors surface a `MessageBoxW`, since a hidden background process has no other way
to tell the user it failed. No file logging; no dictated text or keystrokes are
recorded anywhere.

## Acceptance Criteria

- Both feasibility gates are recorded as passed before implementation proceeds.
- Each spike increment is runnable on its own and produces observable output
  proving its capability.
- Measured layout-switch latency is recorded, and the poll interval and timeout
  constants are justified by it rather than guessed.
- `dotnet build` and `dotnet test` succeed from a clean clone with the .NET 10 SDK.
- `dotnet publish -c Release -r win-x64` produces a single self-contained native
  executable requiring no installed .NET runtime, launching with no console window
  and no visible window.
- With a non-English layout active in a normal application, `Ctrl+Alt+H` switches
  that application to English and opens Voice Typing; a second press closes Voice
  Typing and restores the exact layout that was active before.
- Restoration returns the layout actually saved, not a hard-coded default,
  verified from at least one non-Finnish starting layout.
- Repeated toggles never restore a stale layout.
- With no English layout available, the utility reports the error, sends no
  `Win+H`, changes no layout, and stays Idle.
- The executable runs without administrator privileges.
- Unit tests cover layout selection (exact match, `en-GB` fallback, none) and both
  state transitions including failed-start-stays-Idle.

## Verification

- Gates: manual, one-time. Recorded because they are environment-specific and not
  reproducible from repository history.
- Spike increments: run the console executable against a browser, a terminal, and
  an Office/Teams-class application; correctness is the printed layout, the
  confirmed switch, and the measured elapsed time.
- Automated: `dotnet build` on every code change; `dotnet test` once the test
  project exists.
- Publish gate: `dotnet publish -c Release -r win-x64` succeeds and yields a
  runnable single executable; run when AOT configuration lands and again before
  completion.
- Manual acceptance: the ten-step workflow in
  `docs/voice-typing-toggle-concept.md`, plus alternate-starting-layout and
  repeated-toggle cases.
- Known limitations: `WM_INPUTLANGCHANGEREQUEST`, `SendInput`, `Win+H`, and layout
  timing cannot be automated and are only provable by manual runs on a real
  session. Synthetic input cannot reach elevated applications, so targets are
  non-elevated.
- This plan expects a `## Verification Evidence` note in `tasks.md` for the two
  gate results and the measured switch latency. These are environment-specific,
  one-time observations that later decisions depend on and that no test, build, or
  CI record can reconstruct. Nothing else is copied there.

## Risks and Considerations

- `WM_INPUTLANGCHANGEREQUEST` may not be honored by every application (notably
  UWP/WinUI-hosted or custom-input surfaces). If a target application ignores it,
  the polling gate fails closed and voice typing is not started — correct but
  potentially surprising; alternatives (`ActivateKeyboardLayout` on an attached
  thread) are a revision, not silent implementation drift.
- Opening Voice Typing may move focus, which could affect which thread's layout is
  read on the stop press. The chosen rule is deliberately "restore to whatever is
  foreground at stop time"; if manual testing shows this misbehaves, it is a plan
  revision.
- The toggle state is optimistic: if the user closes Voice Typing by other means,
  the utility's state and reality diverge until the next toggle.
- Native AOT publishing requires the MSVC build prerequisites on the development
  machine; `dotnet build` alone will not surface a missing toolchain.
