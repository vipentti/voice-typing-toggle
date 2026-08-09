# Native Win32 Tray Icon

## Summary

Add a native Windows notification-area icon that makes Voice Typing Toggle
visible while it is running, exposes its current Idle or Dictating state and
configured hotkey, and provides a safe Exit command.

Use direct Win32 interop and an original static icon asset. Preserve Native AOT,
single-file publishing, ordinary user-level operation, and the existing
voice-typing state-machine behavior.

## Scope

- Add the project-original icon defined by this handoff as an editable vector
  master and a multi-resolution `.ico` used by both the executable and
  notification area. The fixed concept is a transparent square canvas with a
  centered cobalt-blue rounded-square keycap, a thin near-white outer keyline,
  and a near-white microphone glyph whose base ends in two small opposing
  arrowheads to communicate toggling. Use flat geometry with no text, gradients,
  shadows, or fine interior detail. Preserve a clear silhouette and at least one
  pixel of separation between major shapes at 16 pixels. Include hand-tuned 16,
  20, 24, and 32 pixel images plus scalable 48, 64, and 256 pixel images. The
  saturated blue field and light glyph or keyline must remain distinguishable on
  both light and dark taskbars.
- Create the artwork from project-defined primitive geometry. Do not derive it
  from a third-party logo, icon set, font glyph, or stock asset. Commit the
  editable source beside the `.ico` so its originality and later changes are
  reviewable. This plan is the approval record for the concept; implementation
  does not depend on an external visual-design decision.
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

Keep the lifecycle decision that consumes the Boolean `Shell_NotifyIconW`
result behind a narrow non-Win32 seam. Its production collaborators perform the
real add or re-add, report a visible error through `MessageBoxW`, and request the
shared orderly-shutdown coordinator. Route both the initial `NIM_ADD` and the
`TaskbarCreated` re-add through this same decision point. Tests can then inject a
false add result without trying to destabilize Explorer or the real notification
area, while the production P/Invoke remains direct and Native AOT compatible.

Load the committed multi-resolution icon from the executable rather than from a
sidecar file. Configure it as the executable application icon and verify that
Native AOT publishing preserves a loadable icon resource in the single-file
output.

Build the context menu on demand so its status row always reflects
`ToggleCore.IsDictating`. Open it only for right-click or keyboard context-menu
activation. Follow the Win32 foreground-window and menu-dismissal requirements,
then restore focus to the notification area after menu handling. Left-click and
double-click notifications perform no action.

Opening the context menu while Dictating deliberately preserves the current
focus-loss rule rather than suspending it. Make the invisible owner window the
foreground menu owner, then run the normal focus-loss check and synchronize tray
status before constructing or displaying the menu. Leaving the saved dictation
window therefore ends dictation, restores its saved layout, and makes the menu
and tooltip report Idle. Cancelling the menu leaves the utility Idle and does
not restore focus, restart dictation, or return to a captured Dictating status.
The next hotkey press starts a fresh session. The same outcome applies to mouse
and keyboard context-menu activation.

Synchronize the tooltip after every existing entry point that can change the
core state, including hotkey toggles, timer-driven focus recovery, Voice Typing
UI watchdog callbacks, and shutdown. Avoid adding tray concerns to `ToggleCore`.

Route Exit through one idempotent shutdown coordinator. On the first request,
block new toggle and menu commands. If `ToggleCore.IsDictating` is true, invoke
the normal stop behavior so the saved window and layout are restored and the
existing stop-confirmation watchdog is armed. If the core is already Idle with
`StopConfirmPending` true, adopt that pending work instead of treating Idle as
safe to terminate.

When Exit begins from active dictation or pending stop confirmation, retain the
hidden window, message loop, timer, WinEvent hook, icon, and core callbacks.
Continue dispatching `CheckDictationFocus` and `OnVoiceUiShown` until the core is
Idle, `StopConfirmPending` is false, the stop sequence has restored the saved
layout, and the Voice Typing UI is no longer visible. A late popup during this
window must run the existing corrective stop and layout-restore pass before
shutdown may complete. Exit requested during bar launch follows the same path.
An Exit request made while Idle with no pending stop work may skip this wait and
tear down immediately; it must not inspect or close an unrelated Voice Typing UI
that the utility did not start.

If the existing watchdog reaches its bound but the Voice Typing UI associated
with this stop is still visible, run an equivalent bounded shutdown-specific
Escape, retry, layout-restore, and visibility-confirmation pass while the message
infrastructure remains alive. Successful Exit is permitted only after the UI is
confirmed absent and layout restoration has completed. If the bounded pass still
cannot establish that outcome, cancel shutdown, keep the tray and monitoring
resources running, restore command handling, and show a visible error. This is a
failed Exit rather than terminating and allowing Voice Typing to reopen later.

Only after the required stop work is complete may shutdown remove the tray icon,
stop the timer, unregister the hotkey, release the WinEvent hook and icon
resources, destroy the hidden window, and end the message loop. Process-exit
restoration remains a final best-effort fallback for external termination.

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
- Opening the context menu while Dictating applies the existing focus-loss
  recovery before the menu is displayed: the exact saved layout is restored,
  the core becomes Idle, and both menu and tooltip report Idle.
- Cancelling a context menu opened during Dictating leaves the utility Idle and
  does not restart dictation or restore a stale Dictating status; the next
  hotkey press starts a fresh session.
- Starting, stopping, failed starting, focus-loss recovery, and watchdog
  correction leave the displayed status consistent with `ToggleCore`.
- Selecting Exit while Idle removes the icon and terminates the process.
- Selecting Exit while Dictating, including while the bar is still launching,
  keeps stop-confirmation monitoring active until any late popup is corrected,
  Voice Typing is confirmed absent, and the exact saved layout is restored;
  only then are the icon and process removed.
- Selecting Exit while the core is Idle but `StopConfirmPending` is true waits
  for that pending stop work under the same completion rule instead of tearing
  down its timer or WinEvent hook.
- After a successful Exit, Voice Typing does not reopen late. If bounded shutdown
  correction cannot confirm closure, the utility reports a visible error and
  remains running rather than claiming a successful Exit.
- Restarting Explorer while the utility runs restores exactly one working tray
  icon without restarting the utility.
- A test-injected initial `NIM_ADD` failure invokes the production-wired visible
  error path and the shared orderly-shutdown coordinator, completing partial
  startup cleanup and message-loop termination so no invisible background
  process remains.
- After a successful initial add, a test-injected re-add failure on
  `TaskbarCreated` invokes the same visible error and orderly-shutdown path. Any
  pending stop confirmation still completes before teardown, and the process
  does not continue invisibly without its icon.
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

Add controlled automated fault-injection tests at the notification-icon
lifecycle seam. For initial installation, make the first add return false and
assert that the visible-error collaborator is invoked exactly once,
initialization does not continue as a successful tray process, and the shared
shutdown coordinator reaches its terminal window-destroy and message-loop-exit
actions. For recreation, make initial add succeed and the next add triggered by
`TaskbarCreated` fail; assert the same error route is invoked, shutdown is
requested, existing pending-stop monitoring is retained when applicable, and
terminal teardown occurs only through the already-covered shutdown completion
rule. These tests must prove that neither failure path can leave the application
running invisibly. They do not need to force the real Windows shell API to fail.

Manually verify on Windows:

- Icon appearance at common taskbar scale factors and in light and dark modes.
- Tooltip and menu status while Idle and Dictating.
- Right-click and keyboard menu access.
- Right-click and keyboard menu activation while Dictating, including menu
  cancellation, confirming focus-loss recovery, exact layout restoration, and
  an Idle menu and tooltip afterward.
- No action from left-click or double-click.
- Normal hotkey start, stop, repeated toggles, failed English activation,
  focus-loss recovery, and watchdog correction.
- Exit while Idle, while established Dictating, during bar launch, and while an
  earlier stop still has `StopConfirmPending` active.
- A late Voice Typing popup during Exit, confirming corrective handling runs
  before teardown and the UI does not reopen after successful process exit.
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
- Opening the tray menu intentionally ends active dictation through the existing
  focus-loss recovery path. Menu creation must occur after that recovery and
  status synchronization so it cannot display a stale Dictating state.
- `NOTIFYICONDATAW` structure layout and resource loading must be correct for
  `win-x64` and compatible with Native AOT marshalling.
- Explorer restart and DPI changes can remove and recreate notification icons.
  The registered taskbar message must re-add the icon and reapply its
  notification version without creating duplicates.
- Windows may place the icon in the notification-area overflow according to the
  user's taskbar preferences. The application cannot require it to remain
  permanently visible beside the clock.
- The icon must be redrawn and pixel-tuned from the primitive-geometry concept
  recorded in Scope. Importing a similar third-party glyph or omitting the
  editable vector master would break the provenance handoff even if the rendered
  result looks similar.
- Graceful Exit can take as long as the bounded stop-confirmation window. The
  timer, WinEvent hook, hidden window, and message loop must outlive that window;
  releasing them when `StopDictation` returns would reintroduce the late-popup
  failure the watchdog exists to correct.
