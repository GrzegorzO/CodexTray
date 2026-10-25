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

## Local installation and upgrades

When asked to install an update, use the existing deployment rather than
rediscovering it or creating a second startup registration. The established
local installation is `dist/CodexTray.exe` in this checkout. Confirm the actual
running executable and scheduled-task action before replacing it; another
checkout or a future installation can use a different location.

1. Read [startup troubleshooting](docs/startup-troubleshooting.md). Inspect the
   working tree for concurrent changes and preserve them. Publish the intended
   source to a separate ignored staging folder:

   ```powershell
   dotnet publish --configuration Release --runtime win-x64 --self-contained true `
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
     --output .\publish\local-install
   ```

2. Run that exact published executable with `--self-test` before stopping the
   working installation. Use `Start-Process` with `-WindowStyle Hidden`,
   `-WorkingDirectory` set to the publish folder, and `-Wait -PassThru`; check
   its exit code. Do not treat a successful build as a successful executable
   launch. Record its SHA-256 so the installed artifact can be compared later.
3. Locate the existing scheduled task by its action's executable path and
   `--startup-supervisor` argument, rather than hard-coding a user-specific task
   name. Preserve whether startup is enabled. Back up the installed executable
   and any files to be overwritten under `dist/backups/<timestamp>/`.
4. Stop the matching scheduled task first, then stop only CodexTray processes
   whose executable path matches this installation. Both supervisor and widget
   must exit; otherwise the supervisor can restart the old widget during copying.
5. Copy the tested publish output into the installation, retaining unrelated
   files and user preferences. Include current README, changelog, license, and
   published attribution/license files. If overwriting the stopped executable
   fails because Windows still holds it open, moving that old executable into
   the backup directory and copying the tested executable into its original path
   worked in the prior deployment. Verify resolved absolute installation and
   backup paths before moving anything. This addresses a file lock only, not a
   security-policy rejection. Do not delete the known-good backup.
6. Verify the installed executable's SHA-256 matches the tested publish. Restart
   the existing scheduled task if startup is enabled; otherwise launch the
   installed widget with `Start-Process -WindowStyle Hidden`. Do not change saved
   startup preferences or re-register the task merely to update its executable.
7. Allow for the widget's ten-second `--startup` delay. Confirm exactly one
   matching supervisor and one widget for scheduled startup, verify the widget
   reaches its UI message loop (`WaitForInputIdle`), and check it remains alive.
   If only the supervisor is present, inspect the startup result and allow its
   documented bounded recovery: failed child launches are retried at one-minute
   intervals. Do not repeatedly restart the task while a retry is pending or
   mistake the retry wait for a successful widget launch.
   Look for initialization in the correct startup-log context: a packaged Codex
   launcher can see redirected AppData, while Task Scheduler uses the normal
   desktop context. A missing entry in the launcher's log alone is not failure.
   On a deployment failure, restore the backup and restart the prior version.

Keep `ReportTheme.InitializeNativeMode()` after
`ApplicationConfiguration.Initialize()` in UI entry points. Calling native
theme setup first can create a Win32 window too early and cause
`SetCompatibleTextRenderingDefault` to fail. This is a managed initialization
error, distinct from an Application Control block.

### Distinguish file locks from Application Control blocks

Previous unsigned builds have installed successfully. That does not establish
whether a newly built artifact is allowed. If the exact published executable
reports an Application Control policy block, leave the working installation
running and inspect `Microsoft-Windows-CodeIntegrity/Operational` for matching
events. Event 3077 is an enforced block; inspect the event XML, where the path
field can be named **`File Name`** (with a space), not `FileName`. The policy
`VerifiedAndReputableDesktop` has rejected a new local report build even though
the previous installed build remained usable. Report the specific artifact's
observed result rather than saying unsigned builds categorically cannot run.

On the report update, a later normal retry of the **same SHA-256, unchanged
published executable** passed its full `--self-test`, after which that artifact
was installed through the procedure above. No code-signing certificate, security
setting, exclusion, filename change, or repackaging was needed. Therefore, after
recording the block and completing diagnostics, retry that same artifact once
before concluding installation requires signing or a policy change. The exact
reason Windows initially rejected and later accepted it was not established;
do not present trust-service timing as a confirmed cause. If it still fails,
retain the working version and report the specific blocker.

Do not rename, repackage, alter code, or change trust/security settings to evade
a confirmed block. Do not replace the working installation with an artifact that
cannot run. The prior file-lock replacement procedure is not a verified fix for
this block. Update these instructions with evidence if another resolution is
established.
