# Listening disable toggle and Ctrl+Alt+H checkbox

## Summary

Add two session-only tray toggles so the application can stay open in the tray
while doing nothing. A master "Enable listening" checkbox (checked by default)
turns off the whole listening part: hotkey, keyboard hook, and timers. A new
"Enable Ctrl+Alt+H" checkbox (unchecked by default) gates the Ctrl+Alt+H
toggle hotkey, which is no longer registered at startup. No persistence: every
start begins from the defaults. Disabling either control while dictating first
ends the active session (bar closed, saved layout restored), then applies the
change. In the normal tray-menu flow the existing focus-loss self-heal does
the ending; the defensive abort helper runs the Escape-first stop, which also
restores focus to the saved window.

## Scope

- Master toggle "Enable listening" (checked by default, session-only):
  unchecked while Idle unregisters the Ctrl+Alt+H hotkey (when enabled),
  uninstalls the keyboard hook, and kills the focus-watch timer and the
  Win+H-hold timer. Unchecked while Dictating runs the shared abort helper
  (canonical Escape-first stop `ToggleCore.StopDictation`: Escape, settle,
  restore saved window and layout; an in-flight Win+H hold is already
  resolved by the tray-menu ordering rule below before any toggle runs)
  before tearing the listening machinery down; see the
  tray-menu-during-dictation rule below for when that path actually runs.
  The application keeps running with the tray icon; the menu status line and
  tooltip show "Disabled".
- While listening is disabled, the "Enable Ctrl+Alt+H", "Intercept Win+H",
  "Close dictation on Enter", and "Close dictation on Space" items are grayed
  out and inert (menu `MF_GRAYED | MF_DISABLED`). Their checkbox intent is
  preserved unchanged across a disable/re-enable cycle; re-enabling listening
  restores the operating-system state that matches the intents (timer, hook
  when "Intercept Win+H" is checked, hotkey when "Enable Ctrl+Alt+H" is
  checked). A failed hook installation during re-enable clears the intercept
  intent (checkbox unchecked, physical Win+H native) without rolling back the
  rest of the re-enable; only hotkey registration failure rolls back. Hotkey
  registration failure at re-enable clears the hotkey intent
  instead of preserving it: the message box is shown, `ListeningEnabled` ends
  false, `HotkeyEnabled` ends false, the just-applied hook and timer are
  rolled back, and the application stays disabled (fail-closed,
  all-or-nothing). The checkbox then shows unchecked, matching the actual
  registration state.
- New checkbox "Enable Ctrl+Alt+H" (unchecked by default, session-only):
  gates `RegisterHotKey(AppWindow, HotkeyId, MOD_CONTROL | MOD_ALT, 'H')`.
  Startup no longer registers the hotkey and no longer fails fatally when the
  combination is in use; the fatal-startup hotkey path is removed. Checking
  the item registers the hotkey; failure shows a message box, the item stays
  unchecked, `HotkeyEnabled` ends false, and `ListeningEnabled` is unchanged
  (all other listening machinery stays as it is). Unchecking while Dictating
  runs the shared abort helper first (normally the session already ended via
  the tray-menu focus-loss rule below), then unregisters. An in-flight
  Win+H-hold gesture is already resolved at tray-menu open (see below), so
  an uncheck command never runs with a hold armed.
- Tray menu during dictation (existing behavior preserved, one added step):
  `ShowTrayMenu` deliberately ends an active session before rendering. While
  `Core.IsDictating` it first resolves an in-flight Win+H hold
  (`WinHHoldArmed`: run `CompleteWinHInjection()`, which completes the
  gesture while the core is still dictating and therefore sends no Escape
  and never leaves the injected Win held), then restores focus to the app
  window and runs `CheckDictationFocus`, whose focus-loss self-heal closes
  the bar (the bar auto-closes on focus change), restores the saved layout,
  and returns the core to Idle. With this ordering the hold is always
  resolved while the core is dictating: `WinHHoldArmed` is false by the time
  any menu command runs, so no Idle-hold path exists. A bar that opens late
  from the completed gesture is closed by the existing launch-race watchdog:
  the heal arms `StopConfirmPending` while the session is still in its
  launch window, and the `OnVoiceUiShown` SHOW-event corrective pass closes
  the bar with the usual bounds. Menu commands are therefore selected while
  the core is Idle in normal operation, and the Escape-first stop in the
  shared abort helper is the defensive path for the case where the heal did
  not run (for example the foreground stayed on the saved window). Toggle
  handlers always guard with the abort helper so a disable can never strand
  an active session, regardless of how the session state was reached. The
  menu status line is rendered after the heal and shows "Idle" when a
  dictation session was ended by opening the menu.
- In-flight Win+H hold completion: `CompleteWinHInjection()` as it exists
  today sends a closing Escape when `Core.IsDictating` is false, so a hold
  must never be resolved from a state where the core is already Idle. The
  hold is therefore resolved at the start of `ShowTrayMenu`'s dictating
  branch, while the core is still dictating: the gesture completes as today
  (H down/up, Win up; no Escape while dictating; the hold timer is killed
  and the injected Win is never left held), and the subsequent focus-loss
  self-heal ends the session. A bar that opens late from the completed
  gesture is closed by the existing launch-race watchdog: the heal arms
  `StopConfirmPending` while the session is still in its launch window, and
  the `OnVoiceUiShown` SHOW-event corrective pass closes it. Program never
  arms the stop-confirmation watchdog itself (`ArmStopConfirm` is private in
  `ToggleCore`, which stays untouched) and never corrects from
  timer-observed visibility (that correction runs only under the private
  native-stop restore path). No Idle-hold path exists, so no bar can remain
  open after a disable from a hold-armed state.
- Checkbox state becomes intent flags in `Program.cs`:
  `ListeningEnabled` (default true), `HotkeyEnabled` (default false),
  `InterceptWinHEnabled` (default true). The existing `KeyboardHook != 0`
  check stops doubling as the intercept checkbox state; the keyboard hook is
  installed only when `InterceptWinHEnabled && ListeningEnabled`. A failed
  hook installation keeps the existing nonfatal semantics (trace-only
  `hook-install-failed`, no message box): it clears `InterceptWinHEnabled` to
  false at direct checking and at master re-enable alike, so the checkbox
  shows unchecked and physical Win+H stays native. The existing
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
- Shutdown: `CompleteShutdown` keeps its existing guarded teardown. A user
  exit that begins while listening is disabled with a stop confirmation still
  pending must not stall: `ShutdownDecision.Begin` returns Wait for a pending
  confirmation, and that drain advances only from the focus-watch timer
  (watchdog expiry in `CheckDictationFocus`, `OnVoiceUiShown` corrective
  passes, `ContinueShutdownIfNeeded`). `RequestOrderlyShutdown` therefore
  re-arms the focus-watch timer when the initial action is Wait and the timer
  is not running (the disabled state), so the existing drain semantics run
  unchanged; `CompleteShutdown` kills the re-armed timer exactly as it does in
  the enabled state. If the re-arm fails, the drain falls back to the same
  timer-dependent exposure the enabled state already has.
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
current structure. `ShowTrayMenu` keeps its focus-loss recovery ordering and gains one step at
the start of its dictating branch (see the tray-menu-during-dictation rule
in Scope): while `Core.IsDictating`, resolve an in-flight Win+H hold
(`WinHHoldArmed`: `CompleteWinHInjection()` while still dictating, no
Escape) before restoring focus to the app window and running
`CheckDictationFocus`. An active
session therefore ends through the focus-loss self-heal before the menu
renders, commands are selected while the core is Idle in normal operation,
and no hold is ever armed when a command runs. A shared
abort helper guards every disable path for the case where a session is still
active when a toggle runs:

1. If `Core.IsDictating`, run `Core.StopDictation()` (the same Escape-first
   stop the close-key path uses), which restores the saved window and layout.
   No hold handling is needed here: an in-flight hold is resolved at
   tray-menu open while the core is still dictating, so `WinHHoldArmed` is
   false whenever a toggle runs, and an injected Win key is never left held.
2. Apply the disable (unregister hotkey and/or uninstall hook, kill timers as
   applicable) and update tooltip, menu status, and trace.

Re-enable applies the intents in dependency order: start the focus-watch
timer, install the hook when `InterceptWinHEnabled`, register the hotkey when
`HotkeyEnabled`. A failed hook installation at re-enable is nonfatal like
today: it clears `InterceptWinHEnabled` (checkbox unchecked, physical Win+H
native) and the re-enable continues with the timer and hotkey. A hotkey
registration failure on re-enable rolls the
just-applied hook and timer back, shows the startup-style message box, and
ends with `ListeningEnabled` false and `HotkeyEnabled` false (the checkbox
shows unchecked, matching the actual registration state). A direct
"Enable Ctrl+Alt+H" registration failure instead ends with `ListeningEnabled`
unchanged (true) and `HotkeyEnabled` false.

Shutdown drain while disabled: `RequestOrderlyShutdown` re-arms the
focus-watch timer when `ShutdownPolicy.Begin` returns Wait and the timer is
not running, so a pending stop confirmation drains through the existing
`CheckDictationFocus` watchdog expiry and `ContinueShutdownIfNeeded` even
though the master disable killed the timer. `ContinueShutdownIfNeeded`,
`ShutdownDecision`, and `CompleteShutdown` are unchanged.

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
  exactly as before; opening the tray menu while Dictating ends the session
  through the existing focus-loss self-heal (bar closes, saved layout
  restored, core Idle) before commands run, and unchecking the item then
  unregisters the hotkey (Ctrl+Alt+H passes through untouched). If a toggle
  ever runs while a session is still active (heal did not run), the abort
  helper performs the Escape-first stop first and the session ends exactly
  like a normal stop.
- Unchecking "Enable listening" while Dictating ends the session first (the
  tray-menu heal, or the abort helper's Escape-first stop when the heal did
  not run), restoring the saved layout and leaving the core Idle (the
  tray-menu heal does not steal focus back; only the abort helper's
  Escape-first stop restores focus to the saved window), then unregisters the
  hotkey and uninstalls the hook; the tray icon and menu stay available,
  status reads "Disabled", and no keyboard input is observed by the
  application (Ctrl+Alt+H and physical Win+H both behave natively).
- While listening is disabled, all four sub-toggle items are grayed and
  unselectable, "Exit" still works, and re-checking "Enable listening"
  restores exactly the per-checkbox state from before the disable (including
  an enabled Ctrl+Alt+H hotkey when that box was checked).
- Direct "Enable Ctrl+Alt+H" registration failure (combination already in
  use) shows a message box and ends with `HotkeyEnabled` false (item
  unchecked) and `ListeningEnabled` true; all other listening machinery stays
  as it was.
- Master re-enable with a failing hotkey registration shows a message box and
  ends with `ListeningEnabled` false and `HotkeyEnabled` false: the
  just-applied hook and timer are rolled back and the application stays
  disabled (fail-closed, all-or-nothing).
- Exit while listening is disabled completes normally: with no pending stop
  confirmation the shutdown completes immediately, and with a pending
  confirmation (stop, then disable before watchdog expiry, then Exit) the
  re-armed focus-watch timer drains the watchdog and shutdown completes or
  cancels under the existing semantics instead of stalling.
- A disable requested while a Win+H-hold gesture is armed never leaves an
  injected Win key held and never leaves Voice Typing open after the
  disable: opening the tray menu resolves the hold first, while the core is
  still dictating (the gesture completes without Escape, the hold timer is
  killed, the injected Win is released), then the focus-loss self-heal ends
  the session and arms the launch-race watchdog, so a bar that opens late
  from the completed gesture is closed by the existing `OnVoiceUiShown`
  SHOW-event corrective pass with the usual bounds. By the time a toggle
  runs, the hold is resolved and the core is Idle; the disable therefore
  applies to an inert application and no Escape is sent into the window
  that replaced the dictation target.
- Checking "Intercept Win+H" when hook installation fails (direct check or
  master re-enable) keeps listening enabled, clears `InterceptWinHEnabled`
  (checkbox unchecked), and leaves physical Win+H native; the failure is
  nonfatal and trace-only, matching today's behavior.
- All existing behavior with listening enabled and the hotkey checked is
  unchanged: Ctrl+Alt+H toggle, Win+H interception, Enter/Space close keys,
  focus-loss self-heal, stop-flash watchdog, shutdown restoration.
- Session-only: restarting the application returns every checkbox to its
  default state regardless of the previous session's toggle history (for
  example, a session that enabled "Enable Ctrl+Alt+H" and disabled listening
  starts fresh with the hotkey unchecked and listening enabled).

## Verification

- `dotnet build VoiceTypingToggle.slnx` and `dotnet test VoiceTypingToggle.slnx`
  must pass (warnings and analyzer findings are errors).
- `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`
  must succeed (Native AOT, single-file win-x64).
- Manual Windows verification per AGENTS.md (this feature touches hotkeys,
  focus, layouts, timers, `SendInput`, and the Voice Typing UI), covering:
  default start with Ctrl+Alt+H unregistered (combination reaches other apps
  untouched; physical Win+H still intercepts), checking "Enable Ctrl+Alt+H"
  while Idle, unchecking it while Idle, and the dictating cases from the
  state that actually exists when menu commands run (opening the tray menu
  while Dictating ends the session via the focus-loss self-heal: bar closes,
  saved layout restored with no focus steal-back, core Idle, status line
  shows Idle; the toggle is then selected while Idle), master disable while
  Idle and the same menu-open-during-dictation flow (session ends and
  restores first, everything inert afterwards, tray still usable), re-enable
  restoring per-checkbox state, grayed sub-toggles being unselectable,
  hotkey-in-use failure at direct enable and at master re-enable (message
  box; direct failure leaves listening enabled with the item unchecked,
  re-enable failure leaves the application disabled with both flags false),
  Exit while disabled with no pending confirmation (immediate completion)
  and with a pending stop confirmation (stop dictation, disable listening
  before the watchdog expires, select Exit: the re-armed focus-watch timer
  drains the watchdog and shutdown completes or cancels under the existing
  semantics, never stalls), Enter and Space close behavior and stop-flash
  watchdog behavior with listening active after a disable/re-enable cycle,
  during an in-flight Win+H hold (start dictation, then open the tray menu
  within the ~500 ms hold window: the hold is resolved at menu open while
  the core is still dictating, the gesture completes without an Escape and
  without a stuck Win, the focus-loss heal then ends the session and
  restores the saved layout, a bar that opens late from the completed
  gesture is closed by the existing launch-race watchdog via `OnVoiceUiShown`,
  and disabling afterwards leaves nothing open and everything inert, with no
  Escape in the window that replaced the dictation target), repeated toggle cycles, focus changes, and shutdown restoration. Session-only reset: change
  checkbox intents (for example enable "Enable Ctrl+Alt+H" and disable
  listening), restart the application, and verify every checkbox returns to
  its documented default (hotkey unchecked, listening and Intercept Win+H
  checked). The defensive abort path (session still active when a toggle
  runs because the heal did not fire) is not reliably reproducible manually;
  it is covered by code review and the trace events, and the manual matrix
  targets the normal menu flow.
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
- Disabled-state shutdown drain depends on the re-armed focus-watch timer; a
  `SetTimer` failure at that point leaves the same timer-dependent exposure
  the enabled state already has, no additional handling is added.
