# Prepare Repository for Public Visibility

## Summary

Prepare `vipentti/voice-typing-toggle` so changing its GitHub visibility from
private to public is a deliberate, reviewable action. The repository should
have an explicit open-source license, accurate public documentation, a small
security-reporting policy, least-privilege CI permissions, and a completed
history-aware privacy and provenance audit.

This plan covers public source repository readiness only. It does not publish
the repository, distribute binaries, or redesign the application.

## Scope

- Add a root MIT `LICENSE` for copyright year 2026 and copyright holder Ville
  Penttinen. Confirm that the repository owner has the right to publish the
  application icon and other non-code assets under the chosen terms. Review
  copied configuration and bundled Planlet files for any license or attribution
  obligations, and add only the notices that are actually required.
- Rewrite `README.md` around current implementation behavior. Cover the
  Windows-only and current maturity status, .NET 10 and Native AOT `win-x64`
  deployment model, prerequisites, source build and run workflow, first-run
  behavior, tray controls and session-only defaults, diagnostic tracing,
  privacy guarantees, and important limitations. State plainly that this work
  does not provide an installer, packaged GitHub release, or speech-recognition
  implementation.
- Reconcile clearly stale current-state material in `AGENTS.md` and
  `docs/voice-typing-toggle-concept.md`. Preserve useful historical design
  context, but label it so a public reader cannot mistake old MVP proposals or
  non-goals for current behavior. Keep the implementation and tests as the
  source of truth.
- Add a concise root or `.github/SECURITY.md`. Establish an honest private
  reporting route by enabling GitHub private vulnerability reporting when
  available, then direct suspected vulnerabilities there instead of to public
  issues. Describe the narrow user-level keyboard-hook and synthetic-input
  behavior, absence of background networking, and prohibition on dictated-text
  or typed-content logging without claiming an unconfigured contact channel.
- Add explicit `permissions: contents: read` to `.github/workflows/ci.yml`.
  Keep the workflow structure and required checks intact. Pin GitHub-maintained
  actions to immutable commits only if the exact revisions can be verified
  confidently and the change remains small.
- Perform a history-aware audit across every GitHub-visible branch and relevant
  pull-request ref, not only the current tree. Use an established scanner such
  as Gitleaks when available, plus targeted inspection for credentials, private
  keys, connection strings, `.env` content, local user or machine identifiers,
  absolute user-profile paths, screenshots, trace output, debug artifacts, and
  deleted temporary research files. Review pull-request bodies, comments, and
  other GitHub surfaces that become visible with the repository. The known
  commit author name and personal email are approved and remain unchanged.
- Validate non-code asset provenance before declaring the MIT-licensed
  repository ready. If provenance or redistribution rights cannot be
  established, report that as a blocker rather than inventing attribution.
- Finish with a public-readiness report covering license, documentation, CI
  permissions, security reporting, history scan, asset provenance, and any
  remaining blockers. Its verdict must be exactly `READY TO MAKE PUBLIC` or
  `NOT READY TO MAKE PUBLIC`.

## Out of Scope

- Changing repository visibility or rewriting Git history.
- NuGet publishing, GitHub Releases, release automation, installers, MSIX or
  MSI packaging, auto-update infrastructure, or code signing.
- Removing or redesigning Native AOT, adding runtime dependencies, or changing
  product behavior unless documentation review exposes a concrete bug that is
  separately approved.
- Broad refactoring, broad contributor bureaucracy, a code of conduct, complex
  issue templates, or unrelated GitHub administration.
- Treating optional repository metadata, dependency automation, action SHA
  pinning, branch cleanup, or branch protection as blockers for visibility.

## Approach

Start with the legal and provenance gate because the approved MIT license must
not be applied to material the owner cannot redistribute. Use the smallest
necessary documentation edits after tracing defaults and limitations directly
to source and tests. Historical concept material should be retained where it
explains design decisions, with explicit status language instead of a wholesale
rewrite.

Keep CI changes limited to token permissions unless immutable action revisions
are independently verified. Do not redesign jobs or add publishing behavior.
The security policy must refer only to a reporting mechanism that has actually
been configured.

Treat the history audit as an evidence-gathering gate. Scan all remote-visible
refs and inspect suspicious results manually to distinguish real exposure from
benign engineering notes. Report an exact file and commit, classification, and
rationale for any concern. Never rewrite history as part of this plan.

## Acceptance Criteria

- A root MIT license identifies the approved copyright holder and year, and all
  tracked non-code material has either confirmed compatible provenance or an
  explicitly reported blocker.
- The README accurately describes current defaults, including listening enabled,
  physical `Win+H` interception enabled, `Ctrl+Alt+H` disabled, and the actual
  Enter and Space close-key defaults at startup.
- Public documentation states supported and verified environments,
  prerequisites, build and publish commands, current distribution status,
  diagnostic behavior, privacy guarantees, and elevated-application limits
  without overstating compatibility.
- `AGENTS.md` and the concept document no longer present obsolete architecture,
  tray behavior, or hook assumptions as current requirements.
- A concise security policy offers a working private reporting path and
  accurately describes the application's security and privacy posture.
- CI declares `contents: read` and retains formatting, build, test, and Native
  AOT publish validation with no unnecessary token permissions.
- The current tree, all GitHub-visible refs, deleted historical files, pull
  requests, comments, and relevant non-code assets have been reviewed for
  secrets, privacy exposure, and licensing concerns. Findings are resolved or
  reported precisely without an automatic history rewrite.
- The implementation handoff ends with the required exact readiness verdict.
  Optional polish is not presented as a blocker.

## Verification

Run the repository's stable automated checks:

```powershell
dotnet tool restore
dotnet csharpier check .
dotnet build VoiceTypingToggle.slnx
dotnet test VoiceTypingToggle.slnx
dotnet publish src\VoiceTypingToggle\VoiceTypingToggle.csproj -c Release
```

Run the publish check with no process locking its normal output, or use a
separate ignored output directory when preserving a running instance. Inspect
`git status` and the full diff afterward so build products, traces, scanner
output, and other generated files are not accidentally tracked.

Validate documentation claims against the tray defaults, keyboard-hook gates,
diagnostic trace fields, target framework, runtime identifier, and current
tests. Validate the Planlet and confirm CI succeeds on the resulting branch.
Use a history-aware secret scanner plus targeted Git and GitHub inspection over
all public-visible refs and collaboration surfaces. Routine command results
remain in CI, review, and the final handoff rather than being copied into the
Planlet files.

## Risks and Considerations

- Applying MIT to an asset with unclear provenance would create a licensing
  problem. Unknown provenance blocks readiness until resolved.
- A private reporting mechanism mentioned in `SECURITY.md` must be enabled
  before the policy relies on it.
- Secret scanners produce false positives and can miss contextual privacy
  issues. Targeted review remains necessary, especially for deleted temporary
  files and GitHub collaboration history.
- The application relies on empirically verified Windows Voice Typing behavior.
  Public documentation must distinguish tested configurations from unsupported
  or unverified ones.
