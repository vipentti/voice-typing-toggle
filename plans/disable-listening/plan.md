# Listening disable toggle and Ctrl+Alt+H checkbox

## Summary

Add two session-only tray toggles so the application can stay open in the tray
while doing nothing. A master "Enable listening" checkbox (checked by default)
turns off the whole listening part: hotkey, keyboard hook, and timers. A new
"Enable Ctrl+Alt+H" checkbox (unchecked by default) gates the Ctrl+Alt+H
toggle hotkey, which is no longer registered at startup. No persistence: every
start begins from the defaults. Disabling either control while dictating first
aborts the session exactly like Escape (close bar, restore saved layout and
focus), then applies the change.

## Scope

- Master toggle "Enable listening" (checked by default, session-only):
  unchecked while Idle unregisters the Ctrl+Alt+H hotkey (when enabled),
  uninstalls the keyboard hook, and kills the focus-watch timer and the
  Win+H-hold timer. Unchecked while Dictating first runs the canonical
  Escape-first stop (`ToggleCore.StopDictation`: Escape, settle, restore saved
  window and layout) and completes any in-flight injected Win+H gesture before
  tearing the listening machinery down. The application keeps running with the
  tray icon; the menu status line and tooltip show "Disabled".
- While listening is disabled, the "Enable Ctrl+Alt+H", "Intercept Win+H",
  "Close dictation on Enter", and "Close dictation on Space" items are grayed
  out and inert (menu `MF_GRAYED | MF_DISABLED`). Their checkbox intent is
  preserved unchanged; re-enabling listening restores the operating-system
  state that matches the intents (timer, hook when "Intercept Win+H" is
  checked, hotkey when "Enable Ctrl+Alt+H" is checked). If the hotkey
  registration fails during re-enable, show the existing-style message box and
  stay disabled (fail-closed, all-or-nothing).
- New checkbox "Enable Ctrl+Alt+H" (unchecked by default, session-only):
  gates `RegisterHotKey(AppWindow, HotkeyId, MOD_CONTROL | MOD_ALT, 'H')`.
  Startup no longer registers the hotkey and no longer fails fatally when the
  combination is in use; the fatal-startup hotkey path is removed. Checking
  the item registers the hotkey; failure shows a message box and the item
  stays unchecked. Unchecking while Dictating first runs the canonical
  Escape-first stop, then unregisters. Unchecking while a Win+H-hold gesture
  is armed completes (or aborts) that gesture first, exactly like the master
  toggle.
- Checkbox state becomes intent flags in `Program.cs`:
  `ListeningEnabled` (default true), `HotkeyEnabled` (default false),
  `InterceptWinHEnabled` (default true). The existing `KeyboardHook != 0`
  check stops doubling as the intercept checkbox state; the keyboard hook is
  installed only when `InterceptWinHEnabled && ListeningEnabled`. The existing
  `EnterCloseEnabled`/`SpaceCloseEnabled` bools are already intent flags and
  stay as they are; the hook callback's Enter/Space/close handling is
  additionally unreachable while the hook is uninstalled.
- The stop-flash `WinEvent` hook (`VoiceUiHook`) stays installed while
  listening is disabled: it is passive observation of TextInputHost popup
  shows, not keyboard listening, and its corrective passes are bounded and
  only fire on positive SHOW evidence. A pending stop-confirmation watchdog
  that cannot expire without the focus-watch timer remains safe (corrections
  bounded by `StopConfirmMaxCorrections`, triggered only by SHOW events) and
  expires once listening is re-enabled.
- Shutdown is unchanged in shape: `CompleteShutdown` already guards each
  teardown by its `!= 0` state, which the disabled paths leave consistent.
  Exit from the disabled state works normally.
- `ToggleCore` is untouched. No new unit tests: the tray menu, hotkey
  registration, and Program-level state are manual-verification territory per
  AGENTS.md. Existing `ToggleCoreTests` must keep passing.
- Docs: AGENTS.md guardrail "The normal toggle hotkey is Ctrl+Alt+H and stays
  always active" becomes tray-gated and off by default; the concept doc's
  matching hotkey and tray sections are revised to describe both toggles as
  session-only opt-ins.

## Approach

All new state lives in `Program.cs` next to the existing tray menu and
hotkey code; the message loop, `ToggleCore`, and the hook callback keep their
current structure. A shared abort helper runs before any disable:

1. If `WinHHoldArmed`, run `CompleteWinHInjection()` so an injected Win key is
   never left held and the gesture ends in a consistent state.
2. If `Core.IsDictating`, run `Core.StopDictation()` (the same Escape-first
   stop the close-key path uses), which restores the saved window and layout.
3. Apply the disable (unregister hotkey and/or uninstall hook, kill timers as
   applicable) and update tooltip, menu status, and trace.

Re-enable applies the intents in dependency order: start the focus-watch
timer, install the hook when `InterceptWinHEnabled`, register the hotkey when
`HotkeyEnabled`. A hotkey registration failure on re-enable shows the
startup-style message box, leaves `ListeningEnabled` false, and leaves all
listening machinery off.

Menu layout: title, status line ("Status: Idle", "Status: Dictating", or
"Status: Disabled"), the hotkey line, separator, "Enable listening", "Enable
Ctrl+Alt+H", "Intercept Win+H", "Close dictation on Enter", "Close dictation
on Space", separator, "Exit". When listening is disabled, the four sub-toggles
are appended with `MF_GRAYED | MF_DISABLED` so `TrackPopupMenu` cannot return
them; their stored intent values still drive the restore on re-enable.

## Acceptance Criteria

- Default start: the tray menu shows "Enable listening" and "Intercept Win+H"
  checked, "Enable Ctrl+Alt+H" unchecked; Ctrl+Alt+H is not registered (the
  combination reaches other applications untouched) and the application starts
  without any hotkey-in-use failure path; physical Win+H starts English
  dictation as today.
- Checking "Enable Ctrl+Alt+H" registers the hotkey and Ctrl+Alt+H toggles
  exactly as before; unchecking it while Dictating closes the bar, restores
  the saved layout and focus, leaves the core Idle, and unregisters the
  hotkey (Ctrl+Alt+H then passes through untouched).
- Unchecking "Enable listening" while Dictating closes the bar (Escape-first
  stop), restores the saved layout and focus, then unregisters the hotkey and
  uninstalls the hook; the tray icon and menu stay available, status reads
  "Disabled", and no keyboard input is observed by the application (Ctrl+Alt+H
  and physical Win+H both behave natively).
- While listening is disabled, all four sub-toggle items are grayed and
  unselectable, "Exit" still works, and re-checking "Enable listening"
  restores exactly the per-checkbox state from before the disable (including
  an enabled Ctrl+Alt+H hotkey when that box was checked).
- Hotkey registration failure (combination already in use) at enable or
  re-enable shows a message box and leaves the checkbox unchecked (master
  re-enable with a failing hotkey leaves listening disabled).
- A disable requested while a Win+H-hold gesture is armed never leaves an
  injected Win key held and never sends a stray Escape into an unrelated
  foreground window; the session ends like a normal stop.
- All existing behavior with listening enabled and the hotkey checked is
  unchanged: Ctrl+Alt+H toggle, Win+H interception, Enter/Space close keys,
  focus-loss self-heal, stop-flash watchdog, shutdown restoration.
- Session-only: restarting the application returns every checkbox to its
  default state.

## Verification

- `dotnet build VoiceTypingToggle.slnx` and `dotnet test VoiceTypingToggle.slnx`
  must pass (warnings and analyzer findings are errors).
- `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`
  must succeed (Native AOT, single-file win-x64).
- Manual Windows verification per AGENTS.md (this feature touches hotkeys,
  focus, layouts, timers, `SendInput`, and the Voice Typing UI), covering:
  default start with Ctrl+Alt+H unregistered (combination reaches other apps
  untouched; physical Win+H still intercepts), checking and unchecking
  "Enable Ctrl+Alt+H" while Idle and while Dictating (disable stops and
  restores first), master disable while Idle and while Dictating (Escape-like
  stop, layout and focus restored, everything inert afterwards, tray still
  usable), re-enable restoring per-checkbox state, grayed sub-toggles being
  unselectable, hotkey-in-use failure at enable and re-enable (message box,
  stays unchecked), disable during an in-flight Win+H hold (no stuck Win, no
  stray Escape), Exit while disabled, repeated toggle cycles, focus changes,
  and shutdown restoration.
- No new unit tests are expected; existing `ToggleCoreTests` must pass
  unchanged.

## Risks and Considerations

- The default changes from hotkey-always-on to hotkey-off. Users of the old
  default must re-check the box each session; that is the requested session-only
  trade-off.
- Killing the focus-watch timer while a stop confirmation is pending leaves
  the watchdog unable to expire until re-enable; corrections stay bounded by
  `StopConfirmMaxCorrections` and fire only on positive SHOW evidence, so no
  behavior regresses.
- All-or-nothing re-enable means one unavailable hotkey keeps listening
  disabled; the message box explains why and the user can leave the hotkey
  unchecked.
