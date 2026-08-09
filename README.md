# Voice Typing Toggle

Press `Ctrl+Alt+H` to switch the active application to English and open Windows
Voice Typing. Press it again to close Voice Typing and restore the layout that
was active when dictation started.

## Notification area

The utility runs without a visible window and shows one icon in the Windows
notification area, including its overflow area when Windows chooses to place it
there. Hovering reports whether the utility is Idle or Dictating.

Right-click the icon, or use the keyboard context-menu command when the icon has
keyboard focus, to see the current status, the `Ctrl+Alt+H` hotkey, and Exit.
Left-click and double-click do nothing. Opening the menu during dictation ends
that session through the normal focus-loss recovery path and restores the saved
keyboard layout before the menu reports Idle.

Choose Exit to close the utility. If it is stopping a Voice Typing session, it
keeps monitoring active until the session has closed and the saved layout has
been restored. If closure cannot be confirmed, Exit is cancelled and the icon
remains available with an error message.

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
