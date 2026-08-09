# Native Win32 Tray Icon

## Summary

Add a native Windows notification-area icon that makes Voice Typing Toggle
visible while it is running, exposes its current Idle or Dictating state and
configured hotkey, and provides a safe Exit command.

Use direct Win32 interop and an original static icon asset. Preserve Native AOT,
single-file publishing, ordinary user-level operation, and the existing
voice-typing state-machine behavior.

## Scope

- Add one approved original icon design as a multi-resolution `.ico` asset used
  by both the executable and notification area.
- Replace the message-only window with an invisible top-level Win32 window that
  remains absent from the taskbar and Alt+Tab interface.
- Add and manage the notification icon through `Shell_NotifyIconW`.
- Show a dynamic tooltip containing the application name and current Idle or
  Dictating state.
- Provide a right-click and keyboard-accessible context menu containing the
  application name, current status, `Ctrl+Alt+H`, and Exit.
- Treat the title, status, and hotkey rows as informational rather than commands.
- Keep left-click and double-click activation inert.
- Recreate the notification icon after Explorer or the taskbar restarts.
- Add an orderly exit path that closes Voice Typing when necessary, restores the
  saved keyboard layout, removes the icon, releases registered Win32 resources,
  and terminates the message loop.
- Report a visible startup error and exit if the notification icon cannot be
  installed.
- Update user-facing documentation to describe the icon, status display, menu,
  and Exit behavior.

## Out of Scope

- Toggling dictation by clicking the notification icon.
- Settings, configuration windows, or an Options command.
- Changing the hotkey or English language from the tray.
- Start-with-Windows behavior.
- Balloon notifications, toast notifications, or startup announcements.
- Separate Idle and Dictating artwork.
- Windows Forms, WPF, UI Automation, networking, or new production dependencies.
- Changes to the physical `Win+H` behavior or existing voice-typing semantics.

## Approach

Create a focused tray-icon component around direct Win32 interop while retaining
`Program` as the owner of process startup, window messages, and application
shutdown. Keep `ToggleCore` free of direct Win32 calls.

Use the existing window as the notification icon owner, but create it as an
invisible top-level window instead of an `HWND_MESSAGE` window. Do not give it
visible window styles, a taskbar button, or an Alt+Tab presence. This allows it
to own context menus and receive the registered `TaskbarCreated` broadcast.

Add the icon with `NIM_ADD`, select version 4 notification behavior with
`NIM_SETVERSION`, update its tooltip with `NIM_MODIFY`, and remove it with
`NIM_DELETE`. Register and handle `TaskbarCreated`, re-adding the icon and
reapplying its version whenever Explorer recreates the taskbar.

Load the committed multi-resolution icon from the executable rather than from a
sidecar file. Configure it as the executable application icon and verify that
Native AOT publishing preserves a loadable icon resource in the single-file
output.

Build the context menu on demand so its status row always reflects
`ToggleCore.IsDictating`. Open it only for right-click or keyboard context-menu
activation. Follow the Win32 foreground-window and menu-dismissal requirements,
then restore focus to the notification area after menu handling. Left-click and
double-click notifications perform no action.

Synchronize the tooltip after every existing entry point that can change the
core state, including hotkey toggles, timer-driven focus recovery, Voice Typing
UI watchdog callbacks, and shutdown. Avoid adding tray concerns to `ToggleCore`.

Route Exit through one idempotent shutdown path. If dictation is active, use the
normal stop behavior so Voice Typing is closed and the saved window and layout
are restored. Then remove the tray icon, stop the timer, unregister the hotkey,
release the WinEvent hook and icon resources, destroy the hidden window, and end
the message loop. Process-exit restoration remains a final best-effort fallback.

If initial icon installation or later icon recreation fails, show a visible
error and take the same orderly exit path rather than continuing as an
unexpected invisible background process.

## Acceptance Criteria

- The published executable displays one original static notification icon and
  retains that icon as its executable file icon.
- The utility still has no console, visible application window, taskbar button,
  or Alt+Tab entry.
- Hovering over the icon reports Voice Typing Toggle and the current Idle or
  Dictating state.
- Right-click and keyboard context-menu activation show the application name,
  current state, `Ctrl+Alt+H`, and Exit.
- Left-clicking or double-clicking the icon does not start or stop dictation,
  open a window, or change configuration.
- Starting, stopping, failed starting, focus-loss recovery, and watchdog
  correction leave the displayed status consistent with `ToggleCore`.
- Selecting Exit while Idle removes the icon and terminates the process.
- Selecting Exit while Dictating closes Voice Typing, restores the exact saved
  layout to the saved starting window, removes the icon, and terminates the
  process.
- Restarting Explorer while the utility runs restores exactly one working tray
  icon without restarting the utility.
- Failure to install or recreate the tray icon produces a visible error and does
  not leave an invisible background process running.
- Existing hotkey, layout-switching, focus recovery, failure handling, tracing,
  and non-elevated operation continue to behave as before.
- Native AOT Release publishing still produces the expected standalone
  single-file executable without adding a production dependency.

## Verification

Run `dotnet build VoiceTypingToggle.slnx` and
`dotnet test VoiceTypingToggle.slnx` after code changes. Both must complete
without warnings, analyzer findings, or test failures.

Run
`dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release`.
Confirm that the result is a standalone Native AOT executable, that its file
icon is present, and that launching it does not require a sidecar icon file.

Manually verify on Windows:

- Icon appearance at common taskbar scale factors and in light and dark modes.
- Tooltip and menu status while Idle and Dictating.
- Right-click and keyboard menu access.
- No action from left-click or double-click.
- Normal hotkey start, stop, repeated toggles, failed English activation,
  focus-loss recovery, and watchdog correction.
- Exit while Idle and while Dictating.
- Exact layout restoration from Finnish and at least one non-Finnish starting
  layout.
- Explorer restart recovery without duplicate or missing icons.
- No visible window, taskbar button, Alt+Tab entry, or elevation requirement.

Direct notification-area behavior, Explorer restart recovery, icon rendering,
menu focus behavior, and integration with Windows Voice Typing require manual
desktop testing. Unit tests and Native AOT publishing alone cannot prove them.

## Risks and Considerations

- A hidden top-level window must remain genuinely invisible. Incorrect styles or
  activation handling could create a taskbar button, Alt+Tab entry, or unwanted
  focus transition.
- Opening the tray menu changes shell focus and can interact with the existing
  focus-loss recovery path during dictation. Exit and menu cancellation require
  manual testing while Voice Typing is active.
- `NOTIFYICONDATAW` structure layout and resource loading must be correct for
  `win-x64` and compatible with Native AOT marshalling.
- Explorer restart and DPI changes can remove and recreate notification icons.
  The registered taskbar message must re-add the icon and reapply its
  notification version without creating duplicates.
- Windows may place the icon in the notification-area overflow according to the
  user's taskbar preferences. The application cannot require it to remain
  permanently visible beside the clock.
- The final visual design depends on an approved original art concept. The
  production `.ico` should include suitable small-size images and remain legible
  against both light and dark taskbar backgrounds.
