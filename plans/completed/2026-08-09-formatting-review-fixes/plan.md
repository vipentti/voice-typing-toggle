# PR review: CSharpier sole formatting owner

## Summary

Address the two major requested changes on PR #7: make CSharpier the sole
automatic formatter and stop the pre-commit hook from staging whole files.
The hook becomes check-only, CI drops `dotnet format` verification, and
`.editorconfig` is trimmed to formatting-related settings.

## Scope

- `.husky/task-runner.json`: single task `dotnet csharpier check ${staged}`
  on staged `.cs` files. No `dotnet format` tasks, no `git add ${staged}`
  re-staging, so a hook can never change index or commit contents.
- `.github/workflows/ci.yml`: remove the `dotnet format style` and
  `dotnet format analyzers` verify steps; keep `dotnet csharpier check .`
  as the only formatting gate.
- `.editorconfig`: keep indentation, spacing, newline, charset, and
  `csharp_*` formatting rules plus the IDE0055 suppression; drop all
  `dotnet_style_*`, `csharp_style_*`, naming, and analyzer-suppression
  rules (style and analyzer policy is out of scope for this feature).
- AGENTS.md and README: manual format instructions become CSharpier-only.
- No product code changes; existing committed formatting stays as is.

## Approach

- Check-only hook: CSharpier verifies staged `.cs` paths and fails the
  commit when they need formatting; the developer runs
  `dotnet csharpier format .` (documented in AGENTS.md and README) and
  re-stages. This removes the atomicity hazard: no tool mutates the index.
- CI mirrors the hook at repo scope with `dotnet csharpier check .`.
- `.editorconfig` keeps only settings that describe formatting (spacing,
  indentation, wrapping, encoding, final newline); style rules that would
  turn `dotnet format` into a code-fixing policy are removed with the
  `dotnet format` invocations.

## Acceptance Criteria

- The pre-commit hook never stages or modifies working-tree files; it only
  checks staged `.cs` files and blocks the commit when CSharpier would
  change them.
- A commit with unformatted staged `.cs` files is blocked; after
  `dotnet csharpier format .` and re-staging, the same commit succeeds.
- CI runs exactly one formatting check: `dotnet csharpier check .`.
- `.editorconfig` contains no `dotnet_style_*`, `csharp_style_*`,
  `dotnet_naming_*`, or analyzer-suppression rules.
- `dotnet build` and `dotnet test` stay green.

## Verification

- Stable commands: `dotnet build VoiceTypingToggle.slnx`,
  `dotnet test VoiceTypingToggle.slnx`, `dotnet csharpier check .`,
  `dotnet husky run --group pre-commit`.
- Hook behavior: stage an unformatted `.cs` file, confirm `husky run`
  fails and the index is unchanged; format it, confirm the same staged
  set passes and no file is re-staged by the hook.
- PR run of the workflow is the external gate for CI.
