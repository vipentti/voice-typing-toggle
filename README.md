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
