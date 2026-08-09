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
  enabled. The callback matches physical Win+H only and never intentionally
  swallows input: it chains through `CallNextHookEx` and returns its result on
  every path. For `nCode < 0` it must chain immediately and return that result
  without any other processing. Plain Win, Win+E, Win+D and all other shortcuts
  pass through untouched.
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
  chord is impossible.
- Start path (Idle): on the single observation per chord, immediately send
  `WM_INPUTLANGCHANGEREQUEST` for English to the foreground thread and wait for
  layout confirmation (`ToggleCore.WaitForLayout`, ~100 ms timeout). Do not
  re-inject Win+H; the native handler opens the bar. If the layout request or
  confirmation fails, the race-start leaves `ToggleCore` Idle with no saved
  session and performs no injected Win+H; because the hook does not swallow,
  the physical Win+H still proceeds natively and the bar opens in the current
  layout (fail-open, native behavior preserved).
- Stop path (Dictating): on the single observation, the callback chains the
  physical Win+H first and returns immediately. `WH_KEYBOARD_LL` runs before
  the event is delivered onward, so sending Escape before chaining would close
  the bar and then let the chained physical Win+H reopen it. The native close
  is performed by Windows itself. `ToggleCore` gets a distinct native-stop
  entry that arms the existing stop-confirmation watchdog, marks Idle, and
  defers restoration (saved window and layout) to the message loop after a
  short settle. Escape is reserved for corrective passes only, if the bar
  fails to close natively. The Ctrl+Alt+H stop path (Escape-first) is
  unchanged.
- Ctrl+Alt+H keeps its current behavior exactly: injected `SendWinH` start,
  Escape-first stop, always active regardless of the interception toggle.
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
  opt-out feature), update the repo AGENTS.md guardrail, and create tracked
  `tmp/winh-interception-research.md` from the Research Evidence section below
  so the evidence travels with the repo.

## Research Evidence

Embedded from the external spike document (originally
`.tmp/winh-interception-research.md` outside this repository, Windows 11 23H2
build 22631, throwaway spike in `%TEMP%\winh-spike`). This section is the
source for the tracked `tmp/winh-interception-research.md`; T5 reproduces that
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
On a Dictating observation it chains first and defers all stop work to the
message loop; the callback body itself never blocks and never injects input.

`ToggleCore` gains a race-start entry (layout switch and wait, no `SendWinH`)
and a native-stop entry (watchdog armed, Idle marked, restoration deferred,
Escape reserved for corrective passes). The existing injected-start and
Escape-first stop paths are unchanged. Unit tests cover all four entries and
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
- Interception off (tray checkbox): native Win+H behavior is byte-identical to
  running without the app.
- Ctrl+Alt+H toggling works identically with interception on and off, and an
  injected Win+H never triggers the hook path (no double-toggle).
- Focus change while dictating (Win+H- or Ctrl+Alt+H-started) triggers the
  existing self-heal.
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
  restoration, elevated-app limitation (a non-elevated hook cannot see input
  destined for elevated windows), and interception off/on at runtime.
- Both stop paths are verified manually: physical Win+H stop closes via the
  native handler with no injected Escape before the chained event, and
  Ctrl+Alt+H stop keeps the existing Escape-first behavior. A physical stop
  must never reopen the bar.
- The race success measurement is durable verification evidence (external,
  non-reproducible in CI, and it decides whether phase 2 is needed): record the
  per-press outcome in a committed file under `tmp/` and reference it from the
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
- Windows 10 and older Windows 11 builds are untested; hook mechanics are the
  same, shell hotkey ownership differs.
