# Repository instructions

CodexTray is a public Windows/.NET 10 WinForms repository. Read
`CONTRIBUTING.md`, `SECURITY.md`, and the relevant feature guide before changing
behavior. Follow these instructions for every push, including direct pushes
authorized by the user.

## Documentation for every push

- Document the work in the same commit or pushed batch. Features, fixes, and
  default changes need an accurate `CHANGELOG.md` Unreleased entry and updates to
  affected README sections, feature guides, and architecture notes.
- For documentation-only corrections, explain the correction in the commit
  body; do not fabricate a product changelog entry.
- Check existing descriptions as well as newly added paragraphs. Menu labels,
  defaults, compatibility, local data reads, and privacy statements must match
  the code. Distinguish current main-branch behavior from versioned releases.
- Use a descriptive commit subject and a body stating the problem, resulting
  behavior, checks actually run, and material limitations. If creating or updating
  a pull request, follow `.github/PULL_REQUEST_TEMPLATE.md` as well.
- For UI changes, include a cropped, reproducible preview with synthetic data.
  Update relevant galleries when rendering changes. Clearly label static
  examples and do not represent them as manual desktop verification.

## Public repository hygiene

- Review the staged file list and complete diff before committing. Do not commit
  credentials, session logs, private conversation content, account identifiers,
  real project metadata, or screenshots exposing unrelated desktop content.
- Keep build outputs, installation copies, and backups in ignored `bin/`,
  `obj/`, `dist/`, or `publish/` directories. Do not commit extracted Codex
  Desktop source or assets; use synthetic fixtures for tests and public previews.
- Retain attribution, provenance, and required license notices when adding or
  redistributing third-party assets.
- Preserve the app's privacy boundaries: no telemetry, direct credential reads,
  model requests, or changes to Codex's own state files.

## Verification and pushing

For source changes, run the CI-equivalent checks:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --self-test
git diff --check
```

Use the targeted verification described in `CONTRIBUTING.md`. Documentation-only
changes need diff/link/content checks; rerun source checks if source changed.
State which display configurations or manual scenarios were not verified.

Before pushing, fetch the remote and inspect the current branch, tracking branch,
and any incoming/outgoing commits. Preserve unrelated user work. Do not rewrite
published history or force-push without explicit authorization. After a push,
verify the remote commit and inspect its CI result; report pending or failed
checks accurately. When asked to audit recent pushes, state the reviewed commit
range, compare documentation with behavior, and correct gaps without rewriting
history.
