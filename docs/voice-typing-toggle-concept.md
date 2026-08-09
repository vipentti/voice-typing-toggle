# Voice Typing Toggle — Concept

## Summary

A very small Windows utility that makes voice typing easier on machines where Finnish is the normal typing language but Windows voice typing is only usable in English.

The utility provides a single toggle hotkey:

- First press:
  1. Remember the input language/layout currently active in the foreground application.
  2. Switch that application to an English input language.
  3. Trigger Windows Voice Typing with `Win+H`.

- Second press:
  1. Trigger `Win+H` again to stop/close voice typing.
  2. Restore the original input language/layout that was active before voice typing started.

The goal is to replace one narrow AutoHotkey workflow with a small standalone executable.

---

## Goals

- Make starting English voice typing a single action.
- Restore the user's original typing language automatically afterward.
- Avoid requiring AutoHotkey, Python, PowerShell scripts, Electron, or other runtimes.
- Produce a small standalone Windows executable.
- Avoid administrator privileges.
- Minimize dependencies and installation requirements.
- Work with normal desktop applications such as browsers, editors, Outlook, Teams, Word, terminals, and similar applications.
- Keep the implementation simple enough to audit and maintain.

## Non-goals

The utility is not intended to:

- Implement speech recognition itself.
- Replace Windows Voice Typing.
- Translate dictated text.
- Change the Windows display language.
- Permanently change the user's default keyboard layout.
- Manage arbitrary keyboard remappings.
- Become a general-purpose automation tool.
- Initially intercept or replace the operating system's own `Win+H` shortcut.

---

## Proposed Technology

### Language

**C#**

### Runtime / deployment model

**.NET 10 Native AOT**

The application should be published as a self-contained native Windows executable.

This gives most of the deployment advantages of C++ while retaining the simpler implementation and Win32 interoperability of C#.

### External dependencies

Preferably none.

Use direct P/Invoke calls into Windows APIs rather than third-party hotkey, keyboard-hook, or UI automation libraries.

---

## High-Level Architecture

```text
┌─────────────────────────────────────────┐
│             VoiceTypingToggle.exe             │
│                                         │
│  ┌───────────────────────────────────┐  │
│  │ Hidden Win32 message window       │  │
│  │ / message loop                    │  │
│  └─────────────────┬─────────────────┘  │
│                    │                    │
│  ┌─────────────────▼─────────────────┐  │
│  │ Global hotkey handler             │  │
│  └─────────────────┬─────────────────┘  │
│                    │                    │
│  ┌─────────────────▼─────────────────┐  │
│  │ Voice typing state machine        │  │
│  │                                   │  │
│  │ Idle <──────────────> Dictating   │  │
│  └───────┬─────────────────┬─────────┘  │
│          │                 │            │
│          ▼                 ▼            │
│  Input-language APIs    SendInput       │
│                                         │
└─────────────────────────────────────────┘
```

A GUI is not required for the MVP.

The process can run in the background with a hidden window solely to receive Windows messages and hotkey events.

## Notification area behavior

The utility has one static notification-area icon and no visible application
window, taskbar button, or Alt+Tab entry. Its tooltip identifies the utility and
reports Idle or Dictating. The icon can appear in the notification-area overflow
depending on the user's Windows taskbar preferences.

Right-click and keyboard context-menu activation show informational application,
status, and hotkey rows plus Exit. Left-click and double-click are inert.
Opening the menu during dictation follows the existing focus-loss recovery path,
so the saved layout is restored and the menu reports Idle. Exit waits for any
stop confirmation and late-popup correction before teardown; an unconfirmed
user Exit leaves the utility running with its icon and reports an error.

---

## User Interaction

### Default toggle hotkey

Recommended initial default:

```text
Ctrl+Alt+H
```

A custom hotkey should be used instead of intercepting the physical `Win+H` shortcut.

The utility internally generates `Win+H` when it needs Windows Voice Typing to start or stop.

### Start voice typing

Given:

```text
Current input language: Finnish
Utility state: Idle
```

The user presses:

```text
Ctrl+Alt+H
```

The utility:

```text
1. Determine foreground window
2. Determine foreground window thread
3. Read current keyboard layout
4. Save that layout
5. Find configured English keyboard layout
6. Request foreground application to switch to English
7. Wait briefly if necessary for the layout change
8. Send Win+H
9. State -> Dictating
```

### Stop voice typing

The user presses:

```text
Ctrl+Alt+H
```

again.

The utility:

```text
1. Send Win+H
2. Restore the saved original keyboard layout
3. Clear saved state
4. State -> Idle
```

---

## State Model

The MVP can use a very small state machine.

### Idle

No voice typing session is considered active.

Stored state:

```text
originalKeyboardLayout = null
```

On toggle:

```text
remember layout
switch to English
send Win+H
-> Dictating
```

### Dictating

The utility believes it started a voice typing session.

Stored state:

```text
originalKeyboardLayout = <saved HKL>
```

On toggle:

```text
send Win+H
restore original layout
clear saved layout
-> Idle
```

The utility does not need to inspect or automate the Windows Voice Typing UI for the MVP.

Its state reflects the actions initiated by the utility rather than trying to determine the exact internal state of Windows Voice Typing.

---

## Relevant Win32 APIs

### `RegisterHotKey`

Purpose:

Register the custom global toggle shortcut.

Example:

```text
Ctrl+Alt+H
```

Why:

- Simple.
- Native Windows API.
- Does not require a global keyboard hook.
- Less likely to trigger endpoint-security concerns than low-level keyboard interception.

---

### `GetForegroundWindow`

Purpose:

Identify the application currently receiving keyboard input.

---

### `GetWindowThreadProcessId`

Purpose:

Determine the thread associated with the foreground window.

The active keyboard layout is associated with an input thread rather than globally with the entire system.

---

### `GetKeyboardLayout`

Purpose:

Read the current keyboard layout / input locale for the foreground application's thread.

The returned `HKL` should be stored before switching to English.

This is preferable to assuming that the user always starts in Finnish.

---

### `GetKeyboardLayoutList`

Purpose:

Enumerate keyboard layouts currently installed and available in the Windows session.

The program can use this to locate an English layout.

This avoids requiring a hard-coded keyboard-layout handle.

---

### `WM_INPUTLANGCHANGEREQUEST`

Purpose:

Request that the foreground application's input thread switch to another keyboard layout.

The message is sent to the foreground window.

This is preferable to simulating repeated `Win+Space` keystrokes because it does not depend on keyboard-layout ordering.

---

### `SendInput`

Purpose:

Generate:

```text
Win key down
H down
H up
Win key up
```

to invoke Windows Voice Typing.

The same mechanism is used to toggle Voice Typing off.

---

## English Layout Selection

The program should not blindly assume one specific English layout exists.

Recommended behavior:

1. Enumerate installed layouts.
2. Prefer a configured language, for example:
   - English (United States), `en-US`
3. If unavailable, optionally fall back to another installed English layout.
4. If no English layout is available, show/log a clear error and do not start voice typing.

### Initial configuration

For the first MVP, using `en-US` as the preferred English language is reasonable.

Later this can become configurable.

Example configuration:

```json
{
  "hotkey": "Ctrl+Alt+H",
  "voiceLanguage": "en-US"
}
```

Configuration should remain optional; sensible defaults are preferable.

---

## Restoring the Original Layout

The program should save the actual `HKL` active when dictation starts.

Example:

```text
Before:
Finnish

Start:
save Finnish HKL
switch to English
start voice typing

Stop:
stop voice typing
restore saved Finnish HKL
```

This also works correctly if the user happens to start from another language:

```text
Swedish -> English -> Swedish
German  -> English -> German
```

The utility should restore what was active, not what it assumes the user's default language to be.

---

## Foreground-Window Considerations

There is a potential difference between:

- the application focused when dictation starts, and
- the application focused when dictation stops.

For the MVP, the simplest rule is:

> Restore the saved input layout to whichever application is foreground when the user toggles voice typing off.

An alternative future behavior would be to remember the original foreground window and attempt to restore its layout specifically.

The simple foreground-at-toggle-time behavior is probably more intuitive if the user changes applications during dictation.

---

## Timing

Changing an application's input language may not be instantaneous.

The flow may require a very small delay between:

```text
switch language
```

and:

```text
send Win+H
```

Avoid an arbitrary large delay.

Potential strategies:

### MVP

Use a short delay, for example roughly 50-150 ms, and validate experimentally.

### Better implementation

After requesting the language change, poll the foreground thread's keyboard layout briefly until it matches the requested English layout, with a small timeout.

For example:

```text
request English
poll every 10 ms
stop when English detected
timeout after ~250 ms
send Win+H
```

This is more deterministic than a fixed sleep.

---

## Failure Handling

### No English layout installed

Do not invoke voice typing.

Possible behavior:

- Write an error to a log.
- Optionally show a small Windows notification.

### Language change fails

Do not invoke voice typing unless the target English layout becomes active.

### `SendInput` fails

Attempt to restore the original layout immediately.

### Program exits while in Dictating state

Best-effort restore the saved original layout before exiting.

This can be handled during normal process shutdown, although crashes or forced termination cannot be guaranteed.

---

## Security Considerations

The design intentionally avoids:

- administrator privileges;
- services;
- drivers;
- DLL injection;
- low-level keyboard hooks for the MVP;
- accessibility/UI automation frameworks;
- third-party keyboard interception libraries;
- scripting engines;
- background network access.

The executable only needs ordinary user-level Windows APIs.

### `SendInput` limitation

Windows integrity levels affect synthetic input.

A normally running utility generally cannot inject keystrokes into an elevated application running as Administrator.

This should not matter for typical non-elevated applications.

### Endpoint protection

Native AOT produces a custom executable.

The application should:

- have no unnecessary obfuscation;
- have no self-modifying behavior;
- perform no networking;
- avoid keyboard logging;
- avoid global low-level hooks unless later explicitly needed;
- ideally be code-signed if possible

---

## Process Lifetime

Several approaches are possible.

### Recommended MVP

A background process with:

- hidden message-only/window;
- global hotkey;
- no visible console window.

Possible launch methods:

- manually start executable;
- shortcut in Windows Startup folder;
- user-level startup entry.

Do not require a Windows service.

---

## Optional Tray Icon

A system-tray icon is useful but not required for the first implementation.

Possible tray menu:

```text
Voice Typing Toggle
-------------------
Status: Idle
English language: English (US)
Hotkey: Ctrl+Alt+H
-------------------
Exit
```

Benefits:

- obvious indication that the utility is running;
- clean way to exit;
- possible configuration access later.

Cost:

- additional UI code;
- potentially introduces Windows Forms or another UI dependency unless implemented directly using Win32.

Recommendation:

**Skip the tray icon for the first prototype.**

Add it only if the background-process UX becomes inconvenient.

---

## Logging

Logging should be very lightweight.

Possible log events:

```text
Application started
Hotkey registered
Foreground layout detected: fi-FI
Switching layout -> en-US
Voice typing toggled on
Voice typing toggled off
Restored layout -> fi-FI
Error: English layout unavailable
```

For the MVP, logging can be disabled by default or written to a small file only when a debug option is enabled.

No dictated text or keyboard contents should ever be logged.

---

## Suggested Project Structure

A minimal maintainable project might look like:

```text
VoiceTypingToggle/
├── VoiceTypingToggle.csproj
├── Program.cs
├── NativeMethods.cs
├── HotkeyManager.cs
├── InputLanguageManager.cs
└── VoiceTypingController.cs
```

### Responsibilities

#### `Program.cs`

- startup;
- hidden message loop;
- process lifetime;
- shutdown handling.

#### `NativeMethods.cs`

P/Invoke declarations and Win32 constants.

#### `HotkeyManager.cs`

- register hotkey;
- unregister hotkey;
- surface toggle event.

#### `InputLanguageManager.cs`

- foreground thread detection;
- current `HKL`;
- installed-layout enumeration;
- English-layout selection;
- switch layout;
- restore layout.

#### `VoiceTypingController.cs`

Owns the state machine:

```text
Idle
Dictating
```

and coordinates language switching with `SendInput`.

For an extremely small first prototype these classes can initially live in one file and be split later.

---

## Native AOT Publishing

Target:

```text
win-x64
```

or, if needed:

```text
win-arm64
```

Typical desired publish characteristics:

```text
Self-contained: yes
Native AOT: yes
Console window: no
Single executable: yes
External runtime required: no
```

The development machine needs the .NET SDK and Native AOT build prerequisites.

The published application should only need the resulting executable.

---

## MVP Scope

### Version 0.1

Implement only:

- background Windows executable;
- custom global hotkey;
- detect foreground keyboard layout;
- find `en-US`;
- save original layout;
- switch to English;
- invoke `Win+H`;
- second hotkey press invokes `Win+H`;
- restore original layout;
- graceful exit cleanup;
- Native AOT publish.

No UI.

No configuration file unless needed.

Default:

```text
Toggle: Ctrl+Alt+H
Voice input layout: en-US
```

---

## MVP Acceptance Criteria

The prototype is successful if the following workflow works reliably:

```text
1. Finnish keyboard layout is active.
2. Focus a normal application.
3. Press Ctrl+Alt+H.
4. Input language changes to English.
5. Windows Voice Typing opens.
6. Dictate English technical text.
7. Press Ctrl+Alt+H again.
8. Windows Voice Typing stops/closes.
9. Input language returns to Finnish.
10. Normal Finnish typing continues.
```

Also verify:

```text
- Starting from another layout restores that layout.
- Repeated toggles do not lose the saved original layout.
- No administrator privileges are needed.
```

---

## Future Enhancements

### Configurable hotkey

Examples:

```text
Ctrl+Alt+H
Ctrl+Shift+Space
F8
```

### Configurable voice language

Examples:

```text
en-US
en-GB
```

### Tray icon

Show current state and allow clean exit/configuration.

### Start with Windows

Optional user-level startup integration.

### More robust voice-typing state detection

Potentially detect whether Windows Voice Typing actually opened instead of relying purely on the utility's own toggle state.

This should only be added if the simple state model proves unreliable.

### Physical `Win+H` interception

A later version could let the user press the normal:

```text
Win+H
```

and transparently perform:

```text
switch to English
forward/synthesize Win+H
restore language on second press
```

This would require a low-level keyboard hook such as:

```text
SetWindowsHookEx(WH_KEYBOARD_LL)
```

and suppression/reinjection logic.

This is deliberately excluded from the MVP because it:

- adds complexity;
- introduces recursion/injection edge cases;
- provides relatively little benefit compared with a dedicated global hotkey.

---

## Open Questions to Validate During Prototype

1. Does `WM_INPUTLANGCHANGEREQUEST` reliably switch the target applications used most often?
2. How quickly does the layout change become observable?
3. Is polling required before `Win+H`, or is immediate invocation reliable?
4. Does opening Voice Typing change focus in a way that affects restoration?
5. Is `en-US` installed on the machine, or should another English variant be preferred?
6. Does the user prefer the second hotkey press to restore the language immediately before or immediately after sending the stop `Win+H`?
7. Should exiting the process while Dictating perform best-effort restoration?

---

## Recommended First Implementation

Use:

```text
Language:       C#
Framework:      .NET 10
Deployment:     Native AOT
UI:             None
Hotkey:         Ctrl+Alt+H
Voice language: en-US
Dependencies:   None
```

Core Win32 APIs:

```text
RegisterHotKey
GetForegroundWindow
GetWindowThreadProcessId
GetKeyboardLayout
GetKeyboardLayoutList
WM_INPUTLANGCHANGEREQUEST
SendInput
```

The design should stay intentionally narrow: it is not a keyboard automation framework. It is a single-purpose helper that temporarily changes the foreground application's input language for Windows Voice Typing and then restores the user's original typing environment.
