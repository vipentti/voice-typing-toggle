# Tasks: Husky.Net + CSharpier formatting pipeline

- [x] T1 Add `.editorconfig` (verbatim from the source URL, minus the `file_header_template` and `dotnet_diagnostic.IDE0073.severity` lines) on branch `feat/formatting-pipeline` in the treehouse worktree
- [x] T2 Add `.config/dotnet-tools.json` with local tools `csharpier` 1.3.0 and `husky` 0.9.1; `dotnet csharpier --version` and `dotnet husky --version` resolve through the manifest
- [x] T3 Initialize Husky.Net (`dotnet husky init`) and wire `.husky/pre-commit` to run `dotnet husky run --group pre-commit`; hook file committed
- [x] T4 Configure `.husky/task-runner.json`: staged-only tasks for CSharpier, `dotnet format VoiceTypingToggle.slnx --include ${staged}`, and re-staging via `git add ${staged}`, all with `include: ["**/*.cs"]`
- [x] T5 Run the one-time full-repo format pass (`dotnet csharpier .` then `dotnet format VoiceTypingToggle.slnx`); `dotnet build` and `dotnet test` pass; second `dotnet csharpier .` run produces no diff
- [ ] T6 Verify the hook end-to-end: commit with an unformatted staged `.cs` file auto-formats it, a non-C# commit is a no-op, a failing task blocks the commit; add README fresh-clone note (`dotnet tool restore`, `dotnet husky init`)
- [ ] T7 Extend `.github/workflows/ci.yml`: `dotnet tool restore`, then `dotnet csharpier . --check` and `dotnet format VoiceTypingToggle.slnx --verify-no-changes` steps; add `.config/dotnet-tools.json` to the setup-dotnet cache-dependency-path; run the check commands locally to confirm they pass on the formatted repo
