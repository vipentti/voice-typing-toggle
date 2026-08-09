# Physical Win+H race interception

## Summary

Observe the physical `Win+H` shortcut with a `WH_KEYBOARD_LL` hook and race the
English layout switch ahead of Windows' own Voice Typing launch, so the native
bar opens with English already active. The hook never swallows or replays keys;
Windows opens the bar natively. Ctrl+Alt+H stays as the second, always-active
entry point. Interception is toggled from the tray menu and is enabled by
default (opt-out, fail-open: without interception or on layout failure, native
Win+H behavior is preserved).

## Scope

- Install a low-level keyboard hook (`WH_KEYBOARD_LL`) while interception is
  enabled. The callback matches physical Win+H only (vk `0x48` with a Win
  modifier held), ignores `LLKHF_INJECTED` events (our own injected `Win+H`
  must not re-trigger), and always returns 0 (never swallows, never replays).
  Plain Win, Win+E, Win+D and all other shortcuts pass through untouched.
- Start path (Idle): on observed physical Win+H, immediately send
  `WM_INPUTLANGCHANGEREQUEST` for English to the foreground thread and wait for
  layout confirmation (`ToggleCore.WaitForLayout`, ~100 ms timeout). Do not
  re-inject Win+H; the native handler opens the bar. If the layout request or
  confirmation fails, do nothing further: the bar opens in the current layout
  (fail-open).
- Stop path (Dictating): on observed physical Win+H, route through the existing
  `ToggleCore.Toggle()` stop sequence (Escape, settle, restore saved layout and
  window). The native handler also closes the bar; Escape behavior is verified
  manually and tuned if a double-close side effect appears.
- Ctrl+Alt+H keeps its current behavior exactly: injected `SendWinH` start,
  same stop path, always active regardless of the interception toggle.
- Focus-loss recovery (`CheckDictationFocus`) is unchanged and applies to
  Win+H-started sessions automatically because both entry points share the
  `ToggleCore` state machine.
- Tray menu gains a checked "Intercept Win+H" item, checked by default.
  Toggling installs or uninstalls the hook live. State is session-only (no
  persistence); the menu status line reflects it.
- Hook is uninstalled during orderly shutdown (`CompleteShutdown`) and never
  installed while interception is off, so no system-wide per-keystroke cost
  applies when disabled.
- Docs: revise the concept-doc exclusion of physical Win+H interception (the
  guardrail "Do not intercept the physical Win+H shortcut" becomes an explicit
  opt-out feature), update the repo AGENTS.md guardrail, and copy
  `winh-interception-research.md` into `tmp/` so the evidence travels with the
  repo.

## Approach

The spike in `.tmp/winh-interception-research.md` proved: the hook sees
physical Win+H, injected events are distinguishable via `LLKHF_INJECTED`, and
the app's existing `SendWinH` recipe opens the bar. The race variant avoids the
hard parts of full interception (Win-key leak, pending-Win timer, replay
policy) by never swallowing. Measured timing favors it: layout switches
complete in <1 ms while the bar takes ~1.5 s to appear.

Hook code lives in `Program.cs` next to the existing Win32 interop (P/Invoke:
`SetWindowsHookExW`, `CallNextHookEx`, `UnhookWindowsHookEx`, `KBDLLHOOKSTRUCT`,
`GetKeyState`). The callback runs on the message-loop thread; it must be
exception-safe (an exception escaping a native callback terminates a Native AOT
process), match only vk/modifier state, and never inspect or store typed
content (guardrail: never log keystrokes).

`ToggleCore` gains a race-start entry that performs layout switch and wait but
skips `SendWinH`; the existing start keeps the injection. Unit tests cover both
entries and unchanged Idle/Dictating transitions.

Interception default: enabled (opt-out), per user decision for phase 1. If the
measured race success rate is unacceptable, full interception (swallow and
replay) becomes a separate planlet and this one archives its findings.

## Acceptance Criteria

- Interception on: a physical Win+H press starts English dictation; plain Win,
  Win+E, Win+D and other Win combos behave exactly as without the hook.
- Interception on, dictating: a physical Win+H press stops dictation and
  restores the saved layout and window (existing stop semantics).
- Interception off (tray checkbox): native Win+H behavior is byte-identical to
  running without the app.
- Ctrl+Alt+H toggling works identically with interception on and off, and an
  injected Win+H never triggers the hook path (no double-toggle).
- Focus change while dictating (Win+H- or Ctrl+Alt+H-started) triggers the
  existing self-heal.
- Race success: English is confirmed active for at least 9 of 10 physical
  presses in each tested starting layout (fi-FI and one non-Finnish), measured
  with the existing trace timestamps.
- The hook is uninstalled when interception is off and at shutdown; no key
  content is ever logged.

## Verification

- `dotnet build VoiceTypingToggle.slnx` and `dotnet test VoiceTypingToggle.slnx`
  must pass (warnings and analyzer findings are errors).
- `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`
  must succeed (Native AOT, single-file win-x64).
- Manual Windows verification per AGENTS.md (this feature touches hotkeys,
  focus, layouts, timers, `SendInput`, and the Voice Typing UI): Finnish and
  non-Finnish starting layouts, failed English-layout activation, repeated
  toggles, focus changes, shutdown restoration, elevated-app limitation (a
  non-elevated hook cannot see input destined for elevated windows), and
  interception off/on at runtime.
- The race success measurement is durable verification evidence (external,
  non-reproducible in CI, and it decides whether phase 2 is needed): record the
  per-press outcome and timestamps (press -> layout confirmed -> bar show) in a
  committed file under `tmp/`, and reference it from the final task. This is
  the one place committed evidence is expected.
- Trace output (existing `DiagnosticTrace`) is the timing source; no new
  instrumentation is required beyond the hook's trace events.

## Risks and Considerations

- Race timing: if the shell captures the layout before our request lands, the
  bar opens in the wrong layout with no recovery. Mitigation: measure; fall
  back to full interception in a separate planlet if the rate is poor.
- Escape on a natively closed bar: on stop, Windows itself closes the bar from
  the physical press; our injected Escape may land in the foreground app.
  Existing Ctrl+Alt+H stop has the same shape; verify manually and tune the
  stop sequence only if a real side effect is observed.
- Elevation: same UIPI limitation as today; the hook and `SendInput` cannot
  reach elevated windows.
- Hook safety: exception-free native callback, no key logging, uninstalled
  when disabled; a bug here affects every keystroke system-wide.
- Windows 10 and older Windows 11 builds are untested; hook mechanics are the
  same, shell hotkey ownership differs.
