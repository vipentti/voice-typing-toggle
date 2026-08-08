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
- Implement the chosen approach (A or B) in `ToggleCore` (new injectable seam)
  and `Program.cs`; add unit tests for any new logic; remove the temporary
  instrumentation from the final product.
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
- Record the analysis and the chosen approach in `.tmp/` research notes. If A is
  chosen, restore strictly on the event that the data shows is safe (destroy
  preferred over hide — research shows early restore can reopen the bar); never
  restore while the bar can still reassert itself. Keep a bounded timeout so the
  accepted behavior is the fallback.

Phase 3 — Implementation:
- `ToggleCore`: replace `Sleep(EscapeRetryMs)` + the foreground-based retry
  heuristic with a new seam (e.g. `WaitForVoiceUiClosed(timeoutMs)` returning a
  signal state) only if A is chosen; keep fail-closed ordering (focus restore,
  then layout restore with confirmation and one retry). If B: add a
  focus-stability confirmation seam inside the restore path.
- `Program.cs`: wire the WinEvent hook and bridge events to the seam; delete the
  temporary trace instrumentation before the final commit.
- Unit tests: fake the new seam — event fires → immediate restore; no event →
  bounded fallback; no event → second-Escape retry; focus-stability retry
  (if B).
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
- Restoring focus too early can reopen the bar and start listening again
  (observed in prior experiments). Restore strictly on the data-supported
  event; the bounded timeout keeps the accepted behavior as fallback.
- The shell may raise the MRU candidate before the bar's destroy event, leaving
  a shorter but nonzero flash; the trace data must quantify this before
  accepting approach A.
- `SetWinEventHook` callbacks must stay rooted (static delegate) for Native AOT;
  use `WINEVENT_OUTOFCONTEXT` to avoid injection and keep the utility
  user-level and single-purpose.
- Removing the 100 ms sleep changes first-Escape-retry semantics; the event
  signal must cover that path or the conditional retry stays.
- No keystroke or dictated-text logging at any point; window metadata only,
  gated behind an env var, and removed from the final product.
