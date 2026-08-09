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
  enabled. The callback matches physical Win+H and never intentionally
  swallows input: it chains through `CallNextHookEx` and returns its result on
  every path. For `nCode < 0` it must chain immediately and return that result
  without any other processing. Plain Win, Win+E, Win+D and all other shortcuts
  pass through untouched. While the core is dictating, the callback also
  matches physical Escape (vk `0x1B`), Enter (vk `0x0D`), and Space
  (vk `0x20`) keydowns; all are control/commit keys matched by vk only, never
  typed content. Escape: the user's own key closes the bar natively, so the
  callback only restores the saved state (native-stop path, no injection).
  Enter and Space: the bar does NOT close on either natively, so the callback
  swallows that one key (a deliberate, scope-limited exception to the
  no-swallow rule, Enter/Space-while-dictating only) and defers the standard
  Escape-first stop to the message loop, which closes the bar and restores the
  saved state; the swallowed key never reaches the bar or the app, so no stray
  newline or space is inserted. Enter and Space while Idle pass through
  untouched, and each close-on-key behavior is gated by its own tray toggle:
  while a toggle is off, that key passes through untouched even while
  dictating.
- Win modifier detection: the callback tracks `VK_LWIN`/`VK_RWIN` down/up state
  from the hook events themselves (non-injected keydown sets the flag, keyup
  clears it). `GetKeyState` is not used: it reflects the hook thread's own
  consumed keyboard messages, not global physical-key state, and cannot be
  trusted while input targets another application. A Win keydown that happened
  before the hook was installed is unknown to the tracker; the chord then
  dispatches nothing and behaves natively (fail-open).
- Dispatch contract: exactly one `ToggleCore` observation per physical Win+H
  chord. The callback triggers on the first non-injected H keydown (vk `0x48`)
  while a tracked Win flag is set. `LLKHF_INJECTED` events are ignored (our own
  injected `Win+H` must not re-trigger). After dispatch, further H keydown
  callbacks for the same chord, including auto-repeat, are suppressed until the
  chord is re-armed. Re-arm happens when the H keyup or either Win keyup is
  observed. Key-up and auto-repeat callbacks therefore never produce additional
  observations, and a fast Idle -> Dictating -> Idle double-toggle from one
  chord is impossible. While dictating, the first non-injected Escape keydown
  dispatches the native stop (auto-repeat and further presses are no-ops
  because the core is already Idle after the first dispatch). The first
  non-injected Enter or Space keydown while dictating is swallowed and defers
  the standard stop; auto-repeat keydowns of either key are also swallowed and
  their deferred stops no-op once the core is Idle.
- Start path (Idle): on the single observation per chord, the callback
  performs the layout request and confirmation synchronously BEFORE chaining
  the H event, through a dedicated hook-safe bounded request path. Chaining
  first would let the native Win+H proceed before English is confirmed and
  defeat the race; deferring to the message loop loses the same race. The
  hook-safe path uses `SendMessageTimeout` with `SMTO_ABORTIFHUNG` and a
  100 ms timeout for the request, then `ToggleCore.WaitForLayout` with its
  existing ~100 ms cap; worst-case callback duration is therefore bounded at
  roughly 200 ms (typical <5 ms), after which the H event is chained and the
  native handler opens the bar with English already confirmed. `ToggleCore`
  receives this bounded request through a distinct injected seam used only by
  race-start; the existing `RequestLayout` seam stays bound to the 1000 ms
  `SendMessageTimeout` and remains the only request path for Ctrl+Alt+H. Do
  not re-inject Win+H. If the layout request or confirmation fails, the
  race-start leaves `ToggleCore` Idle with no saved session and performs no
  injected Win+H; because the hook does not swallow, the physical Win+H still
  proceeds natively and the bar opens in the current layout (fail-open,
  native behavior preserved).
- Stop path (Dictating): on the single observation, the callback chains the
  physical Win+H first and returns immediately. `WH_KEYBOARD_LL` runs before
  the event is delivered onward, so sending Escape before chaining would close
  the bar and then let the chained physical Win+H reopen it. The native close
  is performed by Windows itself. `ToggleCore` gets a distinct native-stop
  entry that arms the existing stop-confirmation watchdog, marks Idle, and
  defers restoration (saved window and layout) to the message loop after a
  short settle. The Ctrl+Alt+H stop path (Escape-first) is unchanged. An
  external close via physical Escape uses the same native-stop entry: the
  user's own key closes the bar, the callback only restores saved layout and
  focus afterwards, and the positive-only watchdog covers a close that fails.
  Enter or Space while dictating runs the standard Escape-first stop from the
  message loop (never from the callback), because the bar does not close on
  either natively.
- Native-close confirmation: closure cannot be proven by observation, so the
  confirmation is positive-only, consistent with the repository's treatment of
  the TextInputHost popup (absence is explicitly inconclusive: the popup is
  transient and does not appear for every launch or remain for the whole
  session). While a native-stop confirmation is pending, the message-loop
  timer runs a bounded corrective stop (Escape, same corrective machinery as
  the shutdown path) only on positive evidence that Voice Typing remains or
  reappears: `IsVoiceUiVisible() == true`, or an `OnVoiceUiShown` SHOW event.
  Absence is never treated as closure: no correction runs, and the pending
  confirmation expires through the existing watchdog expiry semantics exactly
  as the Ctrl+Alt+H stop does today. A native close that fails with no
  observable popup signal is therefore an accepted residual limitation,
  identical to the existing stop path's exposure.
- Ctrl+Alt+H keeps its current behavior exactly: injected `SendWinH` start,
  Escape-first stop, always active regardless of the interception toggle.
- Focus-loss recovery (`CheckDictationFocus`) is unchanged and applies to
  Win+H-started sessions automatically because both entry points share the
  `ToggleCore` state machine.
- Tray menu gains a checked "Intercept Win+H" item, checked by default.
  Toggling installs or uninstalls the hook live. The tray menu also gains
  checked "Close dictation on Enter" and "Close dictation on Space" items,
  both checked by default (the user prefers the close-on-key behavior), each
  gating its key's close behavior live, session-only (no persistence). The
  menu status line reflects state. Escape close-on-dictation is always active:
  it is native bar behavior, the hook only restores state after it.
- Hook is uninstalled during orderly shutdown (`CompleteShutdown`) and never
  installed while interception is off, so no system-wide per-keystroke cost
  applies when disabled.
- Docs: revise the concept-doc exclusion of physical Win+H interception (the
  guardrail "Do not intercept the physical Win+H shortcut" becomes an explicit
  opt-out feature), update the repo AGENTS.md guardrail, and create tracked
  `docs/winh-interception-research.md` from the Research Evidence section below
  so the evidence travels with the repo.

## Research Evidence

Embedded from the external spike document (originally
`.tmp/winh-interception-research.md` outside this repository, Windows 11 23H2
build 22631, throwaway spike in `%TEMP%\winh-spike`). This section is the
source for the tracked `docs/winh-interception-research.md`; T5 reproduces that
file from this content.

- `RegisterHotKey(MOD_WIN, 'H')` fails with error 1409
  (`ERROR_HOTKEY_ALREADY_REGISTERED`): the Windows shell owns Win+H (Voice
  Typing). No clean registration path exists; observing Win+H requires a
  low-level keyboard hook.
- A `WH_KEYBOARD_LL` hook installed from an ordinary unelevated process sees
  physical Win+H keydowns (vk `0x48`, scan `0x23` while Win held): 3 of 3
  physical presses logged.
- Returning 1 from the hook callback for the H keydown suppresses Windows' own
  voice typing: 3 of 3 presses swallowed, the native bar never opened.
- Re-injecting Win+H after a delay (right-Win down, hold, H scan code
  down/up, right-Win up, the app's verified `SendWinH` recipe) opens the bar
  and starts listening: confirmed on 2 of 3 presses.
- Injected events carry `LLKHF_INJECTED`; without a guard checking that flag,
  the re-injection is swallowed recursively by the same hook. With the guard,
  re-injections pass through.
- Swallowing only the H key leaks the Win key to the shell: pressing left-Win+H
  opened the Start menu and closed the bar, because the shell saw a Win keyup
  with no combo. Full interception would therefore require swallowing Win
  keydown + H + Win keyup with a pending-Win timer and replay policy, including
  handling plain Win and other Win combos (PowerToys-style). That is out of
  scope for this planlet; the race variant never swallows and avoids all of it.
- Spike latency: a fixed 400 ms sleep standing in for the layout switch gave
  ~0.9 s press-to-injection; the product should poll the foreground thread's
  HKL until English is confirmed (existing `ToggleCore.WaitForLayout`,
  ~100 ms timeout) instead of a fixed sleep.
- Race rationale: the app's earlier research observed visible bar windows
  ~1.5 s after the start hotkey, while layout switches complete in <1 ms, so
  English is likely active before the bar initializes. Risk: if the shell
  captures the layout before our request lands, the bar opens in the wrong
  layout with no recovery. The spike's open question about the simpler
  observe-and-race variant is exactly what this planlet implements.

## Approach

The race variant avoids the hard parts of full interception (Win-key leak,
pending-Win timer, replay policy) by never swallowing; the Research Evidence
section records the verified facts it rests on. Measured timing favors it:
layout switches complete in <1 ms while the bar takes ~1.5 s to appear.

Hook code lives in `Program.cs` next to the existing Win32 interop (P/Invoke:
`SetWindowsHookExW`, `CallNextHookEx`, `UnhookWindowsHookEx`, `KBDLLHOOKSTRUCT`).
Win-modifier and chord state come from the hook events themselves, never from
`GetKeyState`. The callback runs on the message-loop thread; it must be
exception-safe (an exception escaping a native callback terminates a Native AOT
process), match only vk/modifier state, and never inspect or store typed
content (guardrail: never log keystrokes). It chains through `CallNextHookEx`
on all pass-through paths and never returns a nonzero value to swallow input.
The callback never injects input. Blocking policy differs by state: on an
Idle observation the callback performs the bounded synchronous layout
request and confirmation (worst case ~200 ms, typical <5 ms, see Scope) so
English is confirmed before the H event is chained; on a Dictating
observation the callback chains first and defers all stop work to the
message loop, so the Dictating callback path never blocks.

`ToggleCore` gains a race-start entry (bounded hook-safe layout request and
wait through a distinct injected seam, no `SendWinH`) and a native-stop entry
(watchdog armed, Idle marked, restoration deferred, Escape reserved for
corrective passes, positive-only native-close confirmation on the
message-loop timer: corrective Escape only when the popup is observed visible
or a SHOW event arrives, absence inconclusive and left to the existing
watchdog expiry). The existing injected-start and Escape-first stop paths,
including their 1000 ms `RequestLayout` seam, are unchanged. Unit tests cover
all four entries and
unchanged Idle/Dictating transitions, including the exact layout-failure
semantics: failed English confirmation leaves the core Idle, saves no session,
and injects nothing, while the physical Win+H still proceeds natively because
the hook never swallows.

Interception default: enabled (opt-out), per user decision for phase 1. If the
measured race success rate is unacceptable, full interception (swallow and
replay) becomes a separate planlet and this one archives its findings.

## Acceptance Criteria

- Interception on: a physical Win+H press starts English dictation; plain Win,
  Win+E, Win+D and other Win combos behave exactly as without the hook.
- Interception on, dictating: a physical Win+H press stops dictation via the
  native close (no Escape injected before the physical event is chained) and
  restores the saved layout and window.
- Interception on, failed English activation: the core stays Idle with no saved
  session and no injected Win+H; the native bar still opens in the current
  layout (hook chained the press through).
- Interception on, native close failure: a physical Win+H stop whose native
  close fails with positively observed evidence (popup visible or a SHOW
  event) triggers the bounded corrective Escape pass from the message-loop
  timer and ends with the bar closed and the saved layout and window restored.
  A failure with no observable popup signal is an accepted residual limitation
  identical to the existing Ctrl+Alt+H stop exposure: the pending confirmation
  expires without correction.
- Interception off (tray checkbox): native Win+H behavior is byte-identical to
  running without the app.
- Ctrl+Alt+H toggling works identically with interception on and off, and an
  injected Win+H never triggers the hook path (no double-toggle).
- Focus change while dictating (Win+H- or Ctrl+Alt+H-started) triggers the
  existing self-heal.
- Closing dictation with physical Escape restores the saved layout and focus
  and returns the core to Idle; the next start then begins from the correct
  starting layout.
- Pressing Enter or Space while dictating closes the bar (injected Escape
  from the message loop) and restores the saved layout and focus, with no
  stray newline or space inserted in the target app; Enter and Space while
  Idle pass through untouched.
- Unchecking "Close dictation on Enter" or "Close dictation on Space" restores
  native key behavior while dictating (the key passes through, the bar stays
  open); rechecking restores the close behavior.
- Race success: for at least 9 of 10 physical presses in each tested starting
  layout (fi-FI and one non-Finnish), Voice Typing itself is observably using
  English: the bar's language indicator shows English, or a controlled English
  recognition check transcribes correctly. Only pass/fail metadata is recorded,
  never dictated content. English HKL confirmation, bar visibility, and
  timestamps are supporting evidence; `IsVoiceUiVisible` is not required where
  its known false negatives apply (a missing popup observation is not evidence
  of launch failure).
- Exactly one observation per physical Win+H chord: holding and releasing the
  chord toggles the core once, never twice.
- The hook is uninstalled when interception is off and at shutdown; no key
  content is ever logged.

## Verification

- `dotnet build VoiceTypingToggle.slnx` and `dotnet test VoiceTypingToggle.slnx`
  must pass (warnings and analyzer findings are errors).
- `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`
  must succeed (Native AOT, single-file win-x64).
- Manual Windows verification per AGENTS.md (this feature touches hotkeys,
  focus, layouts, timers, `SendInput`, and the Voice Typing UI): Finnish and
  non-Finnish starting layouts, failed English-layout activation (observable
  result: native bar opens in current layout, core stays Idle), repeated
  toggles, hold-and-release chord (single toggle), focus changes, shutdown
  restoration, external close via physical Escape while dictating (layout and
  focus restored, core Idle, next start correct), Enter and Space while
  dictating (bar closes, layout and focus restored, no stray newline or space
  in the target app), Enter and Space while Idle (native behavior, nothing
  swallowed), runtime toggling of the Enter/Space close checkboxes and the
  Intercept Win+H checkbox off and on, elevated-app
  limitation (a non-elevated hook cannot see input destined for elevated
  windows), and interception off/on at runtime.
- Both stop paths are verified manually: physical Win+H stop closes via the
  native handler with no injected Escape before the chained event, and
  Ctrl+Alt+H stop keeps the existing Escape-first behavior. A physical stop
  must never reopen the bar. Native-close failure is verified on positive
  evidence only: with the bar present and observable (popup visible or a SHOW
  event after the stop), the pending native stop runs a bounded corrective
  Escape pass and ends with the bar closed and layout and window restored
  within the watchdog bounds; a silently surviving bar with no observable
  popup signal is documented as the accepted residual limitation.
- The race success measurement is durable verification evidence (external,
  non-reproducible in CI, and it decides whether phase 2 is needed): record the
  per-press outcome in a committed file under `docs/` and reference it from the
  final task. Per-press pass = Voice Typing observably using English via the
  manual signal above (required). English HKL confirmation and bar visibility
  (`IsVoiceUiVisible`, non-required due to its false negatives) are supporting
  evidence. The press -> layout-confirmed -> bar-show timestamps come from the
  existing trace; the bar-show timestamp is best-effort/nullable.
- Trace output (existing `DiagnosticTrace`) is the timing source; no new
  instrumentation is required beyond the hook's trace events.

## Risks and Considerations

- Race timing: if the shell captures the layout before our request lands, the
  bar opens in the wrong layout with no recovery. Mitigation: measure with the
  observable-English signal; fall back to full interception in a separate
  planlet if the rate is poor.
- Native-stop ordering: injecting input inside the hook callback before the
  physical event is chained would let the chained Win+H reopen the bar. The
  callback therefore chains first, never blocks, and defers all stop work to
  the message loop; the watchdog's corrective Escape passes cover a native
  close that fails.
- Dispatch edge cases: the chord contract (first non-injected H keydown with
  tracked Win held, re-arm on H or Win keyup) must hold under auto-repeat and
  fast re-presses; unit-level reasoning plus manual hold/release testing cover
  it. A chord started before hook installation dispatches nothing and behaves
  natively.
- Elevation: same UIPI limitation as today; the hook and `SendInput` cannot
  reach elevated windows.
- Hook safety: exception-free native callback, no key logging, uninstalled
  when disabled; a bug here affects every keystroke system-wide.
- Swallowing Enter or Space while dictating is a deliberate, scope-limited
  exception to the no-swallow contract; it exists only so the close-on-key
  feature produces no stray newline or space. Any future general key
  interception must re-justify the exception.
- Windows 10 and older Windows 11 builds are untested; hook mechanics are the
  same, shell hotkey ownership differs.
