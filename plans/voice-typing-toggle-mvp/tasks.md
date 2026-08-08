# Tasks: Voice Typing Toggle MVP (v0.1)

- [x] T1 Confirm that an unsigned Native AOT hello-world executable runs on the machine where the utility will be used, and record the result
- [ ] T2 Confirm which English keyboard layouts are installed there via `GetKeyboardLayoutList` or the language bar, and record whether `en-US` is among them
- [ ] T3 Create the solution and a `net10.0-windows` console project at `src/VoiceTypingToggle` that prints the foreground window's thread id and current `HKL`, verified against a browser, a terminal, and an Office/Teams-class application
- [ ] T4 Extend the console app to select an English layout from the installed list, request the switch via `WM_INPUTLANGCHANGEREQUEST`, poll until the foreground thread reports it, and print elapsed milliseconds and outcome per application
- [ ] T5 Set the poll interval and timeout constants from the measured data, and record which applications honored the switch and how long it took
- [ ] T6 Extend the console app to send `Win+H` via `SendInput` after a confirmed switch, verifying Windows Voice Typing opens
- [ ] T7 Add `RegisterHotKey` on a hidden message-only window with a message loop, wire `Ctrl+Alt+H` to an Idle/Dictating toggle that switches then sends `Win+H` on start and sends `Win+H` then restores on stop, and switch the project to `WinExe`
- [ ] T8 Implement failure handling: unavailable English layout and unconfirmed switch both abort before `Win+H`, `SendInput` failure restores immediately, unavailable hotkey reports clearly, and shutdown while Dictating performs best-effort restore and hotkey cleanup
- [ ] T9 Add `tests/VoiceTypingToggle.Tests` with unit tests for English-layout selection and both state-machine transitions including failed-start, extracting only the seams those tests require
- [ ] T10 Configure Native AOT publishing for `win-x64` and confirm `dotnet publish -c Release -r win-x64` yields a single self-contained executable starting with no console or visible window
- [ ] T11 Execute the manual acceptance walkthrough from the concept document across a browser, a terminal, and an Office/Teams-class application, including an alternate starting layout and repeated toggles, recording any behavior that requires a plan revision

## Verification Evidence

- Gate T1: unsigned Native AOT `VoiceTypingToggle.exe` built from the planned project, ran on the machine where the utility will be used, printed its gate message, and exited 0 (2026-08-08).
