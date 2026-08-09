# Tasks: PR review: CSharpier sole formatting owner

- [x] T1 Trim `.editorconfig` to formatting-related settings: keep indentation, spacing, newline, charset, and `csharp_*` formatting rules plus the IDE0055 suppression; remove all `dotnet_style_*`, `csharp_style_*`, `dotnet_naming_*`, and analyzer-suppression rules
- [x] T2 Rewrite `.husky/task-runner.json` as a single check-only task: `dotnet csharpier check ${staged}` with `include: ["**/*.cs"]`; remove the `dotnet format` tasks and the `git add ${staged}` re-staging task
- [x] T3 Remove the `dotnet format style` and `dotnet format analyzers` verify steps from `.github/workflows/ci.yml`; keep `dotnet csharpier check .` as the only formatting check
- [ ] T4 Update AGENTS.md and README to CSharpier-only instructions (`dotnet csharpier format .`, `dotnet csharpier check .`); verify `dotnet build`, `dotnet test`, `dotnet csharpier check .`, and hook behavior (unformatted staged `.cs` blocks with index unchanged; formatted staged set passes without re-staging)
