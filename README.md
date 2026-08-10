# Voice Typing Toggle

A small Windows background utility that opens Windows Voice Typing with the
active application switched to an English keyboard layout, and restores the
layout that was active before dictation when the session ends.

This repository is the source for the utility. It does not provide an
installer or prebuilt binaries; build it from source with the commands below.

## Current status

Single-purpose utility, actively developed, no stable release. Verified on
Windows 11 23H2 (build 22631). Windows 10 and other Windows 11 builds are not
verified; see `docs/winh-interception-research.md` for open questions.

## How it works

Press `Win+H` (intercepted, on by default) or `Ctrl+Alt+H` (optional) in the
application you want to dictate into. The utility:

1. Switches that application's keyboard layout to the installed English
   layout, waiting until the switch is confirmed.
2. Opens Windows Voice Typing.
3. On stop (Escape, optional Enter/Space, tray Exit, or focus loss), closes
   Voice Typing and restores the layout and window that were active when
   dictation started.

Physical `Win+H` interception is a race variant: the utility observes the
keystroke and switches the layout first; it never swallows or replays the
shortcut. Keyboard layouts belong to input threads, so the utility addresses
the saved or foreground window's input thread, not a global setting.

`Ctrl+Alt+H` is not registered at startup. Enable it from the tray menu; it
stays enabled only for the current session. The utility requires an installed
English keyboard layout at startup and refuses to start without one.

## Startup defaults

All toggles are session-only: they are never persisted and reset on every
launch.

| Setting | Default |
| --- | --- |
| Enable listening (master gate) | On |
| Intercept Win+H | On (opt-out) |
| Enable Ctrl+Alt+H | Off |
| Close dictation on Enter | On |
| Close dictation on Space | On |

While "Enable listening" is off, the hotkey, the keyboard hook, and both
timers are off, the other toggles are grayed out, and the tray status reads
"Disabled".

## Tray controls

The utility runs without a visible window and shows one icon in the Windows
notification area. Right-click (or use the keyboard context-menu command)
for:

- Status: Idle, Dictating, or Disabled.
- The session-only toggles listed above. Checkmarks reflect live state;
  enabling "Intercept Win+H" can fail if the hook cannot be installed, in
  which case the checkbox clears and native behavior stays untouched.
- Exit. If a dictation session is active, the utility keeps monitoring until
  the bar has closed and the saved layout is restored; if closure cannot be
  confirmed, Exit is cancelled with an error message.

Left-click and double-click do nothing. Opening the menu during dictation
ends the session through the normal focus-loss recovery path and restores the
saved layout before the menu is shown.

## Optional focus-loss behavior

By default, Alt+Tabbing during dictation restores the saved layout only to
the application where dictation started. To also apply the saved layout to
the newly focused window when it still uses the utility's temporary English
layout, set this environment variable before launching:

```powershell
$env:VTT_RESTORE_FOCUSED_LAYOUT = '1'
Start-Process .\VoiceTypingToggle.exe
```

This option does not steal focus. It can change a destination window that was
intentionally using the same English layout, because Windows does not expose
whether that layout was inherited during the focus change or selected
earlier.

## Requirements

- Windows 11 (verified on 23H2), with Windows Voice Typing available.
- An installed English keyboard layout.
- .NET 10 SDK for building and publishing.

## Build and run from source

```powershell
dotnet tool restore
dotnet build VoiceTypingToggle.slnx
dotnet test VoiceTypingToggle.slnx
dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release
```

The publish output is a Native AOT, single-file `win-x64` executable at
`src\VoiceTypingToggle\bin\Release\net10.0-windows\win-x64\publish\VoiceTypingToggle.exe`.
Start it directly, or run `./update.ps1` to stop a running instance, rebuild,
and restart it in one step.

Formatting is enforced by CSharpier, driven by `.editorconfig`. A Husky.Net
pre-commit hook checks staged C# files; CI runs the same check at repo scope.
Install the git hooks with `dotnet husky install`.

## Diagnostic tracing

Tracing is built in but disabled by default. Enable it for a reproduction run
with the default trace location:

```powershell
$env:VTT_TRACE = '1'
```

or an explicit file path:

```powershell
$env:VTT_TRACE = 'C:\temp\voice-typing-toggle-trace.csv'
```

`VTT_TRACE=1` writes `%LOCALAPPDATA%\VoiceTypingToggle\trace.csv`. Each launch
replaces the previous file. The CSV records timestamps, state and action
markers, window and thread handles, keyboard layout handles, and state flags,
and remains readable while the utility runs. It never records window titles,
document names, dictated text, typed content, or keys. Tracing adds overhead;
unset `VTT_TRACE` for normal use.

## Privacy

- No background networking of any kind.
- No logging of dictated text, typed content, or captured keystrokes. The
  keyboard hook observes only the `Win+H` chord and, while dictating, the
  Escape, Enter, and Space keys.
- Synthetic input is limited to `Win+H` and `Escape` key events; no code
  injection, DLL loading, or process memory access. The utility does not
  control elevated applications (running as administrator is not supported).

## Limitations

- No installer, MSIX/MSI packaging, auto-update, or code signing.
- No speech-recognition implementation; Windows Voice Typing does the actual
  dictation and must be available.
- No support for elevated (administrator) target applications.
- Behavior on Windows 10 and older Windows 11 builds is unverified.

## License

MIT, see [LICENSE](LICENSE).
