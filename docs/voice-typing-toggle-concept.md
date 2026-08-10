# Voice Typing Toggle - Concept

## Summary

A very small Windows utility that makes voice typing easier on machines where
Finnish is the normal typing language but Windows Voice Typing is only usable
in English.

The utility temporarily switches the active application to an English
keyboard layout, opens Windows Voice Typing, and restores the layout that was
active before dictation. Current behavior, startup defaults, and build
instructions are documented in `README.md`; product guardrails in
`AGENTS.md`; the implementation and tests in `src/` and `tests/`. This
document records product intent only. Pre-implementation design proposals
were removed; Git history preserves them.

The goal is to replace one narrow AutoHotkey workflow with a small standalone
executable.

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
