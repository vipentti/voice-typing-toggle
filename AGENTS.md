<!-- BEGIN PLANLET AGENTS v:1 hash:924c8d1f -->
## Planning with Planlet

This repository uses Planlet for focused implementation plans. A planlet is
`plans/<slug>/plan.md` + `tasks.md`; Markdown is the source of truth.

- Propose a planlet before multi-step work; skip it for one-file changes.
- Drive it with the `planlet` CLI, never by hand-editing plan files:
  `planlet create|show|tasks|status|validate <slug>`,
  `planlet task check <slug> <task-id>`, `planlet complete <slug>`.
- Check each task off only after its verification passes. When the last task is
  checked, run `planlet complete <slug>` to archive it.
- Run `planlet help [command]` before using a command you have not used here.
- If no `planlet` executable is available, stop and say so. Do not hand-create
  or hand-edit planlet files.
<!-- END PLANLET AGENTS -->
