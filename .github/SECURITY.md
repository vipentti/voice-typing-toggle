# Security Policy

Voice Typing Toggle is a user-level Windows background utility. It performs
no background networking and accepts no network input. Its only external
input is physical keyboard events observed through a low-level keyboard hook:
the `Win+H` chord and, while dictating, the Escape, Enter, and Space keys.

## Reporting a vulnerability

Do not create GitHub issues for security problems. Issue history becomes
visible to everyone, so an issue would disclose the report before it is
handled.

Use the **Report a vulnerability** button on the repository's Security page.
GitHub private vulnerability reporting sends the report privately to the
repository owner for review and coordinated resolution.

## Security and privacy posture

- Runs at the normal user integrity level. No administrator requirements,
  services, drivers, DLL injection, process memory access, or UI automation
  frameworks.
- Uses only ordinary user-level Windows APIs (direct P/Invoke) for keyboard
  layout queries and changes, hotkey registration, a low-level keyboard hook,
  synthetic input, and tray integration.
- The keyboard hook observes only the physical `Win+H` chord and, while
  dictating, the Escape, Enter, and Space keys. It never swallows `Win+H`;
  Enter and Space are swallowed only while dictating and only when their tray
  toggles are enabled.
- Synthetic input is limited to `Win+H` and Escape key events.
- Never logs dictated text, typed content, or captured keystrokes. Opt-in
  tracing (`VTT_TRACE`) records state, handles, and layout values only.
- No background networking of any kind.
- Synthetic input cannot control elevated (administrator) applications.

## Supported versions

No releases exist. Security fixes land on the default branch; the repository
provides no binaries or packaged releases.
