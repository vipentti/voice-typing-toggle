# Tasks: Voice Typing Toggle MVP (v0.1)

- [x] T1 Confirm that an unsigned Native AOT hello-world executable runs on the machine where the utility will be used, and record the result
- [x] T2 Confirm which English keyboard layouts are installed there via `GetKeyboardLayoutList` or the language bar, and record whether `en-US` is among them
- [x] T3 Create the solution and a `net10.0-windows` console project at `src/VoiceTypingToggle` that prints the foreground window's thread id and current `HKL`, verified against a browser, a terminal, and an Office/Teams-class application
- [x] T4 Extend the console app to select an English layout from the installed list, request the switch via `WM_INPUTLANGCHANGEREQUEST`, poll until the foreground thread reports it, and print elapsed milliseconds and outcome per application
- [x] T5 Set the poll interval and timeout constants from the measured data, and record which applications honored the switch and how long it took
- [x] T6 Extend the console app to send `Win+H` via `SendInput` after a confirmed switch, verifying Windows Voice Typing opens
- [x] T7 Add `RegisterHotKey` on a hidden message-only window with a message loop, wire `Ctrl+Alt+H` to an Idle/Dictating toggle that switches then sends `Win+H` on start and sends `Win+H` then restores on stop, and switch the project to `WinExe`
- [x] T8 Implement failure handling: unavailable English layout and unconfirmed switch both abort before `Win+H`, `SendInput` failure restores immediately, unavailable hotkey reports clearly, and shutdown while Dictating performs best-effort restore and hotkey cleanup
- [ ] T9 Add `tests/VoiceTypingToggle.Tests` with unit tests for English-layout selection and both state-machine transitions including failed-start, extracting only the seams those tests require
- [ ] T10 Configure Native AOT publishing for `win-x64` and confirm `dotnet publish -c Release -r win-x64` yields a single self-contained executable starting with no console or visible window
- [ ] T11 Execute the manual acceptance walkthrough from the concept document across a browser, a terminal, and an Office/Teams-class application, including an alternate starting layout and repeated toggles, recording any behavior that requires a plan revision
- [x] T12 Add a `WM_TIMER` foreground watcher (250 ms) that restores the saved layout and resets to Idle when focus leaves the dictation target, because the Voice Typing bar auto-closes on focus change on this machine; verify by starting dictation, switching apps, and confirming the next hotkey press starts fresh

## Verification Evidence

- Gate T1: unsigned Native AOT `VoiceTypingToggle.exe` built from the planned project, ran on the machine where the utility will be used, printed its gate message, and exited 0 (2026-08-08).
- Gate T2: language bar shows installed English layouts en-US (0409:0000040B) and en-GB (0809:0000040B); en-US present (2026-08-08).
- Spike T4/T5: WM_INPUTLANGCHANGEREQUEST honored by Firefox, Word, Windows Terminal, VS Code, and the Alt+Tab overlay; each switch and restore observed in <1 ms. Windows Settings (UWP) timed out at 250 ms, ignoring the message (2026-08-08).
- Constants T5: poll interval 10 ms, switch timeout 100 ms — 100x margin over the observed <1 ms switches (2026-08-08).
- Spike T6: this machine's shell ignores injected left-Win keypresses (all vk/scan variants, SendInput and keybd_event); Start menu opened from injected right-Win extended scancode. Verified recipe: right-Win (VK_RWIN, extended scancode 0x5B) held 500 ms, H as scancode down/up, release — opens and closes Voice Typing; Win+E probe also fires (2026-08-08).
- Spike T7: on this build, Win+H while Voice Typing is listening only pauses it — the bar stays open and re-engages on focus change. Stop uses Escape (closes the bar), then restores the saved layout; verified bar closes and layout returns to en-GB (2026-08-08).
- Spike T7: closing the bar drops foreground to the taskbar, so stop restores layout and focus to the saved window (not foreground-at-stop); Escape needs ~100 ms settle before focus moves — 0 ms loses the close, 150+ ms adds a visible blip (2026-08-08).
- Spike T8/T12: on this machine the Voice Typing bar closes automatically on any focus change, making an optimistic toggle state unreliable. A WM_TIMER watcher (250 ms) restores the saved layout and resets to Idle when focus leaves the dictation target; verified: focus change healed Notepad++ back to en-GB within a quarter second, and the next hotkey press starts fresh (2026-08-08).
- Spike T8/T12: Escape closes the bar app-dependently (terminals and Notepad eat the first Escape as a control char); stop retries Escape only when the bar is still open, claims foreground the moment the bar close drops it to zero (before the shell raises Start/MRU), attaches to the current foreground thread's input queue, and retries SetForegroundWindow once — verified over repeated Notepad cycles: bar closes, focus and layout return to the dictation window, no window shuffling (2026-08-08).
