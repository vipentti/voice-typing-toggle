# Eliminate the stop screen-flash

## Summary

Find and implement a reliable way to close Windows Voice Typing and restore the
saved window without the visible background-window flash on stop. The current
stop path (`ToggleCore.StopDictation`) sends Escape, sleeps a blind 100 ms, and
only then restores focus; during the bar's asynchronous close the shell raises
an MRU candidate window, flashing windows behind the saved app. Primary
hypothesis (from `.tmp/research.md`): detect the Voice Typing UI's hide/destroy
via `SetWinEventHook` and restore focus at that boundary instead of a fixed
sleep. Instrument first, decide from the timeline data, implement only what the
data supports.

The instrumentation data (`.tmp/stop-flash-findings.md`) disproved the event
hypothesis: the only Voice Typing-related window (TextInputHost
`Xaml_WindowedPopupClass` "PopupHost") hides ~5 s after dictation start, not at
stop, so no bar-close window event exists to wait for. The data instead showed
the flash is exactly the blind 100 ms Escape settle: the MRU candidate is
raised at escape+0 ms and becomes visible at ~+31 ms, while the restore lands
at ~+110 ms. The chosen approach is therefore **D: shorten the blind Escape
settle** (tuned empirically), keeping the conditional
Escape retry and the bounded fallback.

Tuning found the reopen boundary: restores landing at ~+26 ms or earlier make
Windows Voice Typing reopen and listen again (the close teardown is
variable), so settle 30 ms (restore ~+31–47 ms) is the fastest reliable
fixed value — but it reopens occasionally under load and then leaves the
layout unrestored. The final design therefore adds a **watchdog**: a
WinEventHook (show events only, out-of-context) detects the TextInputHost
"Listening…" pointer reappearing while a stop is pending and runs a bounded
corrective close/restore pass, so a reopen degrades to a ~200 ms blip with a
correct end state instead of a stuck listening bar and wrong layout.

Baseline: published executable from commit `cf4656c` is accepted and usable;
the flash is a known residual limitation, not a defect. Preserve the known-good
stop path as fallback throughout. The archived MVP planlet
(`voice-typing-toggle-mvp`) is not reopened.

## Scope

- Add temporary, environment-gated WinEvent instrumentation to
  `src/VoiceTypingToggle/Program.cs` (env var `VTT_EVENT_TRACE=<path>`): a
  timestamped timeline of foreground changes, Voice Typing UI window
  show/hide/destroy events, window identity (HWND, PID, TID, class, title,
  visibility), and utility action markers (Escape sent, retry, RestoreFocus
  entry/exit, layout confirmed). Window class/title/HWND metadata only — never
  keystrokes, typed content, or dictated text.
- User-driven measurement cycles on the interactive desktop (the agent must not
  spawn GUI apps; see Verification).
- A decision gate: analyze the traces, then choose one of:
  - A) Event-driven restore: wait for the confirmed Voice Typing UI hide/destroy
    (bounded timeout, existing blind-sleep path as fallback), restore focus at
    that boundary; absence of the event within a bound drives the second-Escape
    retry. Keep focus-before-layout ordering.
  - B) Focus-stability confirmation only: after `SetForegroundWindow`, confirm
    the saved HWND stays foreground briefly and reattach/retry once if the shell
    wins. Does not by itself prevent the initial flash; only adopted if data
    rules out A.
  - C) Report-only: no product change if the data shows no reliable boundary;
    document why and stop.
  - D) Shorten the blind Escape settle: the data shows the flash is the 100 ms
    settle itself (MRU candidate raised at escape+0 ms, restore at ~+110 ms,
    candidate visible from ~+31 ms). Tune the settle to the smallest value that
    never reopens the bar (candidate range 30–60 ms), keep the conditional
    second-Escape retry heuristic and the bounded fallback. Only adopted when
    the data rules out A (no bar-close event exists).
- The decision gate resolved to **D** (recorded in `.tmp/stop-flash-findings.md`);
  A is dead (no stop-time bar event), B adds nothing (restore is already stable,
  no shell re-win observed), C is not wanted by the user.
- Implement D in `ToggleCore` (env-var-tunable settle, temporary for tuning;
  winner hardcoded before final commit) and `Program.cs`; add unit tests for the
  changed stop-path logic; remove the temporary instrumentation from the final
  product.
- **Watchdog (final design, user-approved)**: the settled value alone still
  reopens the bar occasionally (teardown time varies); a permanent
  `SetWinEventHook` (out-of-context, `EVENT_OBJECT_SHOW` only) matches the
  TextInputHost popup (class `Xaml_WindowedPopupClass`, title `PopupHost`,
  process `TextInputHost`) and reports shows to `ToggleCore.OnVoiceUiShown`;
  while `StopConfirmPending` (armed by stop, cleared by the next start/heal,
  expiring after ~1.5 s at the existing 250 ms timer cadence) it re-runs the
  bounded stop sequence (Escape, settle, focus, layout; max 2 corrections).
  This is product code, not instrumentation: it keeps the flash at 31–47 ms
  and makes the rare reopen self-correcting.
- **Bar-launch confirmation (user-approved, T7)**: the bar can take ~0.5–1.5 s
  to appear after Win+H while the state machine already entered Dictating, so
  an early stop or focus change raced a bar that did not exist yet and the
  late bar then listened with the utility Idle. `StartDictation` now enters a
  `WaitingForBar` state confirmed by the existing 250 ms timer against an
  `EnumWindows` visibility poll (never blocks the message loop — the first
  blocking variant queued hotkey presses and was reverted after field
  failure); a stop or focus change during the launch runs the normal stop/heal
  path and arms the watchdog, so a late bar is still closed. If no bar appears
  within ~2 s, fail closed (restore layout, Idle) and arm the watchdog. The
  watchdog pending window is ~2.5 s. `EnumWindows` matches the reused popup
  window even while hidden, so the poll ANDs with `IsWindowVisible`.
- Update `docs/voice-typing-toggle-concept.md` only if stop semantics change.

## Approach

Phase 1 — Instrumentation:
- `SetWinEventHook` with `WINEVENT_OUTOFCONTEXT` (no DLL injection, user-level,
  Native AOT-compatible; keep the callback delegate rooted) for
  `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_HIDE`,
  `EVENT_OBJECT_DESTROY`.
- At each event, record foreground HWND plus identity and visibility of the
  event window; identify Voice Typing UI candidates by class
  (`Windows.UI.Core.CoreWindow`, `ApplicationFrameWindow`) and title
  (`Windows Input Experience`) but correlate PID/lifetime/events before
  assuming any window is the bar.
- Mark utility actions in the same log (high-resolution timestamps) so the
  stop-path sequence can be reconstructed exactly.
- Gate everything behind `VTT_EVENT_TRACE`; zero behavior change when unset.
- Trace files go to `.tmp/` (gitignored).

Phase 2 — Decision gate (after user-driven cycles):
- Answer from traces: (a) which HWND is the bar; (b) does Escape-close produce a
  deterministic hide/destroy for it; (c) event ordering and latencies
  (Escape → hide → destroy → foreground drop → MRU candidate → current
  restore); (d) is the boundary stable across apps and earlier than 100 ms;
  (e) does the shell re-win after `SetForegroundWindow`.
- Record the analysis and the chosen approach in `.tmp/` research notes. The
  decision gate resolved to **D** (see `.tmp/stop-flash-findings.md`): no
  bar-close window event exists (the TextInputHost popup hides ~5 s after
  start), the MRU candidate is raised at escape+0 ms and visible at ~+31 ms,
  and the restore lands at ~+110 ms in every cycle. Never restore at the
  escape+0 ms raise itself — prior experiments show that race can reopen the
  bar.

Phase 3 — Implementation (approach D + watchdog):
- `ToggleCore`: keep `StopDictation`'s shape (Escape, settle, conditional
  retry, focus restore, layout restore), but make the settle tunable:
  read `VTT_ESCAPE_SETTLE_MS` (temporary, for tuning) with a default of the
  accepted 100 ms so a plain build stays the known-good baseline.
- Tuning loop: user runs ≥5 stop cycles per candidate settle (30/40/50/60 ms)
  across VS Code (stacked windows), Notepad, and Windows Terminal; the winning
  settle is the smallest value with no bar reopen, no listening residue, and
  correct focus/HKL in every cycle. Hardcode the winner, remove the env var.
  Actual values tested: 30 (restore +31–47 ms; 14 clean tuning cycles,
  occasional reopen under load), 20 (+31 ms, safe 3/3), 15/10 (reopens).
  Winner hardcoded: `EscapeRetryMs = 30`.
- Watchdog (see Scope): `ToggleCore` gains `StopConfirmPending`,
  `OnVoiceUiShown`, expiry ticking in `CheckDictationFocus`, and a shared
  bounded stop-sequence helper; `Program.cs` installs the show-only
  `SetWinEventHook` permanently and forwards matched shows. The hook callback
  runs on the utility's message loop (same thread as hotkey/timer dispatch), so
  no locking; the delegate stays statically rooted for Native AOT.
- Bar-launch confirmation (see Scope): `ToggleCore` gains `WaitingForBar`,
  `IsVoiceUiVisible`, timer-driven confirmation/abort in `CheckDictationFocus`;
  `Program.cs` implements the poll as `EnumWindows` + `IsWindowVisible` on the
  shared `IsVoiceUiWindow` matcher. `StopConfirmTimeoutTicks` is 10 (~2.5 s).
- `Program.cs`: delete the temporary `VTT_EVENT_TRACE` instrumentation before
  the final commit.
- Unit tests: settle value drives the sleep; conditional retry heuristic
  preserved; fallback path unchanged.
- Manual acceptance per the research doc's verification list.

## Acceptance Criteria

- Stopping with `Ctrl+Alt+H` produces no visible background-window flash across
  the required app set (VS Code with several windows stacked behind it, Notepad,
  Windows Terminal, Firefox, Word).
- Voice Typing bar closes and stays closed; no reopen, no delayed reopen, no
  "listening" state after stop.
- Focus returns to the exact saved window and remains there (stable for at
  least two seconds).
- The exact saved HKL returns, is confirmed, and remains stable for at least two
  seconds.
- Terminals and Notepad still work when they consume the first Escape
  (conditional retry preserved or replaced by an equivalent reliable signal).
- Focus-loss heal and shutdown restoration still work after the stop-path
  change; no stale toggle state on the next start.
- Repeated toggles and long dictation cycles behave as before.
- If the outcome is report-only (C), all criteria above still hold for the
  unmodified baseline and the report documents the evidence.
- The winning Escape settle value is recorded in code and in `.tmp/` notes with
  the cycles that validated it.
- Final product contains no trace instrumentation; nothing logs keystrokes,
  typed content, or dictated text.
- `dotnet build VoiceTypingToggle.slnx` and `dotnet test VoiceTypingToggle.slnx`
  pass with zero warnings; `dotnet publish` (Release, Native AOT) succeeds.

## Verification

Strategy, not a run log. Commands per `AGENTS.md`:

- `dotnet build VoiceTypingToggle.slnx` — 0 warnings/errors after each code
  change.
- `dotnet test VoiceTypingToggle.slnx` — new seam tests green alongside existing
  suite.
- `dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release` —
  after any stop-path change; relaunch only from
  `src\VoiceTypingToggle\bin\Release\net10.0-windows\win-x64\publish\VoiceTypingToggle.exe`
  (kill the running `VoiceTypingToggle.exe` first).
- Manual, physical, user-driven cycles on the interactive desktop. The agent
  does not launch Notepad, VS Code, Firefox, Word, Windows Terminal tabs, or
  mosh windows, and does not trust GUI processes spawned from its hidden
  desktop. Required cases: (1) VS Code with fi-FI and several windows visibly
  stacked behind it; (2) three short cycles plus at least one long dictation
  cycle; (3) Notepad and Windows Terminal (first-Escape consumption); (4)
  Firefox and Word as previously working controls; (5) after every stop: bar
  closed and stayed closed, no delayed reopen, saved window foreground, exact
  saved HKL stable ≥2 s, next start clean; (6) focus-loss heal and shutdown
  restoration.
- Trace evidence: `.tmp/` CSV timelines from `VTT_EVENT_TRACE` runs reviewed at
  the decision gate (event ordering and latencies, per-app consistency). Do not
  over-interpret automated GUI automation results; the old `.tmp/timing.ps1`
  path is diagnostic history only.
- Known limitation: timing and event behavior is Windows-build-specific
  (measured on Win11 23H2 build 22631); conclusions apply to that environment.

## Risks and Considerations

- No deterministic bar window event (UWP window identity varies, or close
  produces no observable hide/destroy): fall back to report-only (C) or the
  focus-stability variant (B); never reintroduce a large arbitrary sleep.
  Resolved by the data: no bar-close event exists, so D (shorter settle) is the
  fix.
- Restoring focus too early can reopen the bar and start listening again
  (observed in prior experiments). The settle must stay outside that race
  (known unsafe: escape+0–10 ms; candidate range 30–60 ms); each candidate
  settle is validated by user cycles before acceptance. Residual reopen risk at
  the winning settle is handled by the watchdog (bounded corrective passes;
  worst case ~200 ms bar blip, then closed with focus/layout restored).
- The shell may raise the MRU candidate before the bar's destroy event, leaving
  a shorter but nonzero flash; the trace data must quantify this before
  accepting approach A. Resolved: the candidate is raised at escape+0 ms, so D
  still shows it briefly; the goal is a flash short enough to be invisible.
- `SetWinEventHook` callbacks must stay rooted (static delegate) for Native AOT;
  use `WINEVENT_OUTOFCONTEXT` to avoid injection and keep the utility
  user-level and single-purpose.
- Removing the 100 ms sleep changes first-Escape-retry semantics; the event
  signal must cover that path or the conditional retry stays. The conditional
  retry heuristic stays unchanged under D.
- The watchdog hook is permanent product code; it observes window show events
  only (no keystrokes, no injection, out-of-context) and must stay statically
  rooted for Native AOT. Its matches are narrow (class + title + process) to
  avoid corrective Escapes on unrelated XAML popups.
- No keystroke or dictated-text logging at any point; window metadata only,
  gated behind an env var, and removed from the final product.
