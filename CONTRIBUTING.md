# Contributing

Thanks for helping improve CodexTray.

## Before opening a change

- Search existing issues and pull requests.
- Keep changes focused and explain user-visible behavior.
- Do not add telemetry, credential access, or model requests.
- Preserve support for both missing and partial rate-limit windows.

## Local workflow

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet restore
dotnet format
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --self-test
```

For taskbar changes, test the primary and at least one secondary display when
possible. Verify all styles, label and percentage positions, Windows scaling
above 100%, and left/right/custom placement. Regenerate the style gallery with
the screenshot-export command documented in the README when rendering changes.

For working-agent changes, verify idle hiding, task completion and interruption,
all icon styles at the fixed large size, both display modes, left/right expansion, and
synchronized/staggered timing. Check fullscreen video on one display while the
other display's taskbar remains visible. Keep the
[working-agent guide](docs/working-agents.md), README menu labels/defaults, and
generated previews consistent with the implementation.
For project colors, verify explicit assignments, worktree/root matching,
projectless and missing-color fallbacks, live recoloring, and color association
on both sides. Use synthetic project metadata and previews in public artifacts.
Check that project badges can be switched independently, keep their project
color with blue loading icons, and repaint after marker-only changes.
Use synthetic SVG/ASAR fixtures to test vector decoding; do not commit extracted
desktop source or assets. Check the optional live `--print-project-icons`
diagnostic and transparent rendering at different display scales.

For token-report changes, run `--self-test` and regenerate the synthetic report
preview using the command in [the report guide](docs/usage-reports.md). Verify
period switching, date boundaries, early resets, bar/label and keyboard selection,
new usage after a click, many-project scrolling, and closing during a read.
Check project colors/icons, equal-name identities, and high-DPI layouts without
including real project/session metadata in public previews. State which manual
scenarios were actually tested.
Regenerate both light/dark previews after report-rendering changes. Check system
theme changes while open, native filter flyouts, minimum-size scrolling, and
donut proportions without counting cached or reasoning subsets twice. The
self-test includes synthetic presentation and geometry checks; these are not
manual multi-monitor or screen-reader verification.

## Pull requests

Describe the motivation, implementation, and manual verification. Include a
tightly cropped screenshot for visual changes and avoid exposing usernames,
notification icons, clocks, or other personal desktop information.

## Before every push

Every push must include documentation appropriate to its changes. For a feature,
fix, or default change, add or update its **Unreleased** changelog entry and keep
the affected README sections, feature guides, and architecture notes consistent
with the implementation. Documentation-only corrections should explain what was
corrected in the commit; do not invent a product change. Keep unreleased behavior
distinct from versioned releases.

Review the complete staged diff and file list for private data, local build
outputs, logs, and extracted Codex assets. Use synthetic project/session fixtures
and cropped previews; retain source attribution and required license notices
for redistributed assets. Keep `bin/`, `obj/`, `dist/`, and `publish/` untracked.

Run the applicable local workflow above and `git diff --check` before pushing.
Document the reason for the change, resulting behavior, validation actually run,
and any untested scenarios in the commit body or pull request. Do not claim
manual display testing or CI success without evidence. Check the pushed revision's
CI result and report failures or pending checks. See [AGENTS.md](AGENTS.md) for
the corresponding instructions for coding agents.

By contributing, you agree that your contribution is licensed under the MIT
License included in this repository.
