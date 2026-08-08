# Voice Typing Toggle

Press `Ctrl+Alt+H` to switch the active application to English and open Windows
Voice Typing. Press it again to close Voice Typing and restore the layout that
was active when dictation started.

## Optional focus-loss behavior

By default, Alt+Tabbing during dictation restores the saved layout only to the
application where dictation started. Set this environment variable before
launching the utility to also apply that saved layout to the newly focused
window when it still uses the utility's temporary English layout:

```powershell
$env:VTT_RESTORE_FOCUSED_LAYOUT = '1'
Start-Process .\VoiceTypingToggle.exe
```

This option does not steal focus. It can change a destination window that was
intentionally using the same English layout, because Windows does not expose
whether that layout was inherited during the focus change or selected earlier.

## Diagnostic tracing

Tracing is built in but disabled by default. Enable it for a reproduction run
with either the default trace location:

```powershell
$env:VTT_TRACE = '1'
```

or an explicit file path:

```powershell
$env:VTT_TRACE = 'C:\temp\voice-typing-toggle-trace.csv'
```

`VTT_TRACE=1` writes `%LOCALAPPDATA%\VoiceTypingToggle\trace.csv`. Each launch
replaces the previous file. The CSV remains readable while the utility runs and
records timestamps, state/action markers, window/thread handles, keyboard layout
handles, and state flags. It never records dictated text, typed content, key
contents, window titles, or document names. Tracing adds diagnostic overhead and
should be disabled for normal use by unsetting `VTT_TRACE`.
