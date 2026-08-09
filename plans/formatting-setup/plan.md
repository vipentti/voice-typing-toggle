# Husky.Net + CSharpier formatting pipeline

## Summary

Add a repo-wide formatting pipeline: `.editorconfig` taken from
`https://github.com/vipentti/dotnet-check-updates/blob/main/.editorconfig`
(verbatim except the file-header rule), CSharpier 1.3.0 and Husky.Net 0.9.1 as
dotnet local tools, and a pre-commit hook that formats only staged C# files.
During this feature, one full-repo format pass runs so that staged-only
formatting is sufficient afterwards.

## Scope

- Add `.editorconfig` from the source URL, minus the
  `file_header_template` and `dotnet_diagnostic.IDE0073.severity` lines (no
  mandatory file headers; the template points at another repo's license and
  this repo has no LICENSE file).
- Add `.config/dotnet-tools.json` with `csharpier` 1.3.0 and `husky` 0.9.1 as
  local tools.
- Add `.husky/pre-commit` hook and `.husky/task-runner.json` running CSharpier
  and `dotnet format` on staged `.cs` files only, then re-staging them.
- One-time format pass over all existing `.cs` files, including the
  `charset = utf-8-bom` conversion the editorconfig mandates.
- README note on restoring tooling in a fresh clone.
- CI step in `.github/workflows/ci.yml` that fails when formatting drifts:
  `dotnet csharpier . --check` and `dotnet format VoiceTypingToggle.slnx
  --verify-no-changes`; tool restore and cache-dependency-path extended to
  `.config/dotnet-tools.json`.
- No node/npm, no changes to product logic.

## Approach

- Local tools over global: `dotnet tool install csharpier` and
  `dotnet tool install Husky`; every developer gets pinned versions via
  `dotnet tool restore`.
- The hook runs `dotnet husky run --group pre-commit`; tasks in
  `task-runner.json` use the `${staged}` placeholder with `include: ["**/*.cs"]`
  so only staged C# files are touched:
  1. `dotnet csharpier ${staged}` formats code.
  2. `dotnet format VoiceTypingToggle.slnx --include ${staged}` applies
     style/analyzer fixes from the editorconfig.
  3. `git add ${staged}` re-stages the formatted files.
- Fail closed: a nonzero exit from any task blocks the commit.
- Husky.Net manages the git hooks path; `git config core.hooksPath` is local
  config, so a fresh clone must run `dotnet tool restore` and
  `dotnet husky init` (README note).

## Acceptance Criteria

- `.editorconfig` matches the source URL except for the removed file-header
  rule lines.
- A commit containing unformatted staged `.cs` files lands with those files
  formatted (CSharpier first, then `dotnet format`), and the formatted content
  is part of the commit.
- A commit whose staged changes contain no `.cs` files skips formatting tasks
  and succeeds.
- A failing formatting task blocks the commit (fail closed).
- After the one-time pass, `dotnet build VoiceTypingToggle.slnx` and
  `dotnet test VoiceTypingToggle.slnx` pass with warnings treated as errors.
- Formatting is idempotent: running CSharpier twice produces no diff on the
  second run.
- CI fails when any committed `.cs` file drifts from CSharpier or `dotnet
  format` (checked on every PR and push to main).
- No node/npm dependency; Husky.Net and CSharpier are dotnet local tools.

## Verification

- Stable commands: `dotnet build VoiceTypingToggle.slnx`,
  `dotnet test VoiceTypingToggle.slnx`, `dotnet csharpier .` run twice
  (second run must be a no-op), `dotnet husky run --group pre-commit`.
- End-to-end hook checks in the worktree: stage deliberately unformatted code
  and commit to confirm auto-formatting; commit non-C# changes only to confirm
  the hook no-ops; temporarily break a task to confirm the commit is blocked.
- CI formatting check validated locally with the same commands the workflow
  runs; the PR run of the workflow is the external gate.
- Windows-only repo; hooks run through Husky.Net, so behavior is validated on
  this machine. Editorconfig/formatting changes are code-adjacent, so unit
  tests plus the build are the regression gate; no Win32 behavior changes here.
