# Race Interception Verification Evidence — Win+H Race Interception

Durable, non-reproducible verification for the winh-race-intercept planlet,
recorded during acceptance (T6). Per-press outcome: Voice Typing observably
using English (bar language indicator or controlled English recognition).
Only pass/fail metadata is recorded, never dictated content. Timestamps come
from `DiagnosticTrace` (`%LOCALAPPDATA%\VoiceTypingToggle\trace.csv`);
bar-show timestamps are best-effort/nullable because the TextInputHost popup
show event is not observed for every launch. English HKL confirmation and bar
visibility (`IsVoiceUiVisible`) are supporting evidence, never the pass
criterion by themselves.

## Measurement sessions

### Session 1: fi-FI starting layout

10 physical Win+H presses, English confirmation observed manually per press.
All presses started from fi-FI (0x40B040B); layout confirmed English
(0x40B0409) before the press was chained (race-start-armed). No popup-show
events observed this session (bar reused), so bar-show stays nullable.

| Press | English observed (pass/fail) | winh-observed tick | layout confirmed tick | bar-show tick (nullable) |
|-------|------------------------------|--------------------|-----------------------|--------------------------|
| 1     | pass                         | 596204328          | 596204359             | null                     |
| 2     | pass                         | 596211296          | 596211312             | null                     |
| 3     | pass                         | 596217859          | 596217875             | null                     |
| 4     | pass                         | 596224625          | 596224656             | null                     |
| 5     | pass                         | 596231531          | 596231562             | null                     |
| 6     | pass                         | 596237734          | 596237765             | null                     |
| 7     | pass                         | 596246328          | 596246359             | null                     |
| 8     | pass                         | 596252421          | 596252437             | null                     |
| 9     | pass                         | 596258890          | 596258921             | null                     |
| 10    | pass                         | 596285546          | 596285546             | null                     |
| 11    | pass                         | 596401109          | 596401140             | null                     |
| 12    | pass                         | 596407343          | 596407375             | null                     |
| 13    | pass                         | 596413937          | 596413968             | null                     |
| 14    | pass                         | 596421343          | 596421375             | null                     |
| 15    | pass                         | 596427343          | 596427375             | null                     |
| 16    | pass                         | 596432921          | 596432937             | null                     |
| 17    | pass                         | 596438750          | 596438781             | null                     |
| 18    | pass                         | 596444250          | 596444281             | null                     |
| 19    | pass                         | 596450421          | 596450437             | null                     |
| 20    | pass                         | 596456187          | 596456203             | null                     |
| 21    | pass                         | 596464781          | 596464781             | null                     |
| 22    | pass                         | 596487046          | 596487046             | null                     |

Result: 22/22 pass (the extra 12 presses were also started from fi-FI; the
en-GB session below remains the non-Finnish sample).

### Session 2: non-Finnish starting layout (en-GB)

10 physical Win+H presses from a fresh Notepad instance with en-GB
(0x40B0809, English UK language on the Finnish physical keyboard) selected;
all raced to en-US (0x40B0409) before the press was chained. English
dictation observed manually per press. No popup-show events observed (bar
reused), so bar-show stays nullable.

| Press | English observed (pass/fail) | winh-observed tick | layout confirmed tick | bar-show tick (nullable) |
|-------|------------------------------|--------------------|-----------------------|--------------------------|
| 1     | pass                         | 596759906          | 596759906             | null                     |
| 2     | pass                         | 596768765          | 596768765             | null                     |
| 3     | pass                         | 596773640          | 596773640             | null                     |
| 4     | pass                         | 596777500          | 596777500             | null                     |
| 5     | pass                         | 596783453          | 596783453             | null                     |
| 6     | pass                         | 596789234          | 596789250             | null                     |
| 7     | pass                         | 596795281          | 596795281             | null                     |
| 8     | pass                         | 596801109          | 596801109             | null                     |
| 9     | pass                         | 596806640          | 596806640             | null                     |
| 10    | pass                         | 596815546          | 596815562             | null                     |

Result: 10/10 pass. Note: from en-GB the native bar would open in English
anyway; the meaningful race evidence is the fi-FI session above, where the
layout switch matters.

## Manual matrix

Verified during T1/T3/T4/T7 development testing on Windows 11 23H2 (build
22631), trace-backed where noted:

- [x] Plain Win (Start menu) and Win+E unaffected with the hook installed (T1 smoke, trace: no interference events).
- [x] Physical Win+H opens the bar natively; holding the chord toggles exactly once (T1/T3).
- [x] Physical Win+H stop closes via the native handler, restores saved layout and window, never reopens the bar (T3, trace: native-stop-armed/restore).
- [x] Ctrl+Alt+H start/stop unchanged; injected Win+H never re-triggers the hook (T3, trace: no winh-observed from injected events).
- [x] Layout switch races ahead of the native bar: English confirmed before the press is chained (T3 trace: layout-request-hook-safe-ok precedes chaining).
- [x] External close via physical Escape restores layout and focus, core Idle (T7, trace: escape-observed -> native-stop).
- [x] Enter and Space while dictating close the bar, restore state, insert no stray newline/space (T7, trace: enter-observed/space-observed -> close-key-stop).
- [x] Enter and Space while Idle pass through untouched (T7).
- [x] Tray toggles: Intercept Win+H, Close dictation on Enter, Close dictation on Space; unchecking restores native behavior, rechecking restores it (T4/T7).
- [x] Focus-loss self-heal during dictation (pre-existing, re-verified T3).
- [x] Failed English-layout activation: native bar opens in current layout, core stays Idle (not forced manually; unit-covered by T2 `RaceStartLayoutFailureStaysIdleWithoutWinHOrSession`).
- [x] Native-close failure on positive evidence: corrective Escape within watchdog bounds (not forced manually; unit-covered by T2 `NativeStopCorrectsWhenPopupVisibleWhilePending` and `NativeStopShowEventRunsBoundedCorrection`).
- [x] Elevated-app limitation acknowledged (non-elevated hook cannot see elevated input; same UIPI limit as SendInput).
