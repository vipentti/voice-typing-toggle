# Physical Win+H Interception — Research Findings

Status: research complete, feature implemented (race variant) in the
winh-race-intercept planlet. Original spike: Windows 11 23H2 (build 22631),
throwaway spike in `%TEMP%\winh-spike` (not part of the repo). This tracked
copy is reproduced from the planlet's Research Evidence section so the
evidence travels with the repository.

## Question

Can the utility react to the physical `Win+H` shortcut instead of (or in
addition to) its own `Ctrl+Alt+H` hotkey, so the flow starts even when the user
presses the native shortcut?

## Why RegisterHotKey cannot do this

`RegisterHotKey(MOD_WIN, 'H')` fails with error 1409
(`ERROR_HOTKEY_ALREADY_REGISTERED`) on this machine. The Windows shell owns
`Win+H` (Voice Typing). Conclusion: there is no clean registration path.
Observing Win+H requires a low-level keyboard hook.

## What was verified with a WH_KEYBOARD_LL hook spike

The spike installs `SetWindowsHookEx(WH_KEYBOARD_LL)` from an ordinary,
unelevated console process and runs a message loop.

| # | Claim | Evidence |
|---|-------|----------|
| 1 | The hook sees physical Win+H keydowns. | 3 physical presses logged with `vk=0x48 scan=0x23` while Win held. |
| 2 | Swallowing the H keydown suppresses Windows' own voice typing. | 3 presses swallowed; the native bar never opened. |
| 3 | Re-injecting Win+H after a delay opens Voice Typing, which starts listening. | Re-injected Win+H (right-Win + H scan code, the app's verified recipe) opened the bar; user confirmed listening on 2 of 3 presses. |
| 4 | Injected events pass through the hook when the guard checks `LLKHF_INJECTED`; otherwise the re-injection is swallowed recursively. | First forward-mode run swallowed its own re-injection (no guard); after the guard, re-injections passed. |
| 5 | Swallowing only the H key leaks the Win key to the shell. | User pressed left-Win+H: Start menu opened, the bar closed. The shell saw a Win keyup with no combo and opened Start, stealing focus; the bar auto-closes on focus change. |

## Why the race variant was chosen over full interception

The spike swallowed only the H keydown and let the Win key events pass; the
shell then treated the physical Win press as a plain Win press (Start menu). A
full intercept must swallow Win keydown, H keydown, and Win keyup for a Win+H
candidate, replay the full gesture after the layout switch, ignore injected
events, and handle the pending-Win policy (plain Win and other Win combos
pass through the hook while it cannot yet know whether H follows). Fully
correct handling of arbitrary Win combos requires PowerToys-style
swallow-and-replay of every Win gesture, which conflicts with the
single-purpose guardrail. The race variant avoids all of it by never
swallowing: the hook observes, switches the layout, and chains the physical
press so Windows opens the bar natively.

## Latency and the race rationale

Spike delay between physical press and re-injection: 400 ms (fixed sleep,
standing in for the layout switch). Measured press-to-injection ~0.9 s
including tick granularity and injection processing. Bar launch latency is
additional (earlier research observed visible bar windows ~1.5 s after the
start hotkey). The product polls the foreground thread's HKL until English is
confirmed (`ToggleCore.WaitForLayout`, ~100 ms timeout) instead of a fixed
sleep, so the re-injection happens as soon as the layout is ready, typically
well under 300 ms after the press. For the race variant the layout switch
lands in <1 ms while the bar takes ~1.5 s to appear, so English is likely
active before the bar initializes. Risk: if the shell captures the layout
before the request lands, the bar opens in the wrong layout with no recovery.
The race success rate is measured during acceptance (T6 evidence).

## Design implications implemented in the product

- Hook callback: exception-safe (an exception escaping a native callback
  terminates a Native AOT process), matches only vk/modifier state, never
  inspects or stores typed content, chains through `CallNextHookEx` on every
  pass-through path, never swallows except the scoped Enter/Space close keys
  while dictating.
- Win-modifier and chord state are tracked from the hook events themselves
  (`VK_LWIN`/`VK_RWIN` down/up); `GetKeyState` is not used because it reflects
  the hook thread's own consumed messages, not global physical-key state.
- The Idle race-start performs a bounded synchronous layout request and
  confirmation inside the callback (100 ms `SendMessageTimeout` with
  `SMTO_ABORTIFHUNG` + ~100 ms `WaitForLayout`, worst case ~200 ms, typical
  <5 ms) before chaining the H event; the Dictating callback path never blocks
  and never injects.
- Elevation: same UIPI limitation as the existing `SendInput` path; a
  non-elevated hook cannot see or control input destined for elevated windows.
- The existing `Ctrl+Alt+H` path remains as-is; the hook adds a second entry
  point into the same `ToggleCore` state machine.

## Open items not covered by the spike

- Behavior on Windows 10 (dictation) and older Windows 11 builds; the hook
  mechanics are the same, the shell hotkey ownership is not.
- Whether the bar opened by the physical Win+H ever conflicts with the stop
  watchdog and focus-loss heuristics (measured during acceptance).
- Full interception (suppress and replay) remains a possible follow-up if the
  measured race success rate is unacceptable; the race evidence in this
  planlet decides that.
