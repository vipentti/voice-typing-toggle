# Tasks: Native Win32 Tray Icon

- [ ] T1 Convert the approved original art concept into a committed multi-resolution `.ico`, configure it as the executable icon, and verify the published Native AOT executable contains a usable icon without a sidecar asset
- [ ] T2 Add the direct Win32 notification-icon lifecycle, replace the message-only window with an invisible top-level owner, fail visibly when icon installation fails, and recreate the icon after `TaskbarCreated`
- [ ] T3 Add the dynamic tooltip and right-click or keyboard context menu showing the application name, current Idle or Dictating status, `Ctrl+Alt+H`, and Exit, while leaving left-click and double-click inert
- [ ] T4 Implement one idempotent graceful-shutdown path that stops active dictation, restores saved state, removes the icon, releases registered Win32 resources, destroys the hidden window, and exits the message loop
- [ ] T5 Update the README and tray-icon section of the concept document, and add focused automated coverage for any non-Win32 state synchronization or shutdown logic extracted during implementation
- [ ] T6 Run build, test, and Native AOT publish verification, then manually verify tray rendering, menu behavior, state synchronization, Explorer restart recovery, graceful Exit, layout restoration, and absence of visible window surfaces
