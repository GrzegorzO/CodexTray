# Public repository documentation review — 2026-10-24

The review covered the eight most recent commits on `origin/main`, from
`10a93cf` through `9be37fe`, after fetching the remote. The local main branch
matched `origin/main`; the uncommitted work was the project/runtime tooltip
feature. No published history was rewritten.

## Recent changes

| Commit | Change | Documentation included |
| --- | --- | --- |
| `9be37fe` | Question sounds and factory-worker grouping | Changelog, README, working-agent guide, architecture, audio attribution and per-file provenance |
| `5d29420` | Dark flash when working-agent icons appear | Changelog and architecture |
| `9bde42d` | Completion sounds and custom WAV support | Changelog, README, working-agent guide, architecture, audio attribution and licenses |
| `ba48e2d` | Click task icons to open sessions | Changelog, README, working-agent guide |
| `f20686c` | Project-colored Robot defaults | Changelog, README, working-agent settings table |
| `f157bbc` | Neon/countdown defaults | Changelog and README |
| `c37324e` | Duplicate task icons and high-DPI text | Changelog, working-agent guide, architecture |
| `10a93cf` | Font-independent badge transparency self-test | Changelog |

Each reviewed commit documented its feature or fix. However, the project-color
guide still said the option started off, and the architecture still described
icon/count as the default. Both contradicted the updated defaults documented in
`f20686c` and are corrected in this update.

The recent commit messages had descriptive subjects but no verification bodies.
The latest seven recorded main-branch CI runs were successful, including
`9be37fe`. No separate run for `c37324e` appeared in the retrieved recent run
list; later successful builds include it. An older badge-rendering CI failure
was followed by the successful `10a93cf` fix. The six question-audio files were
also checked against the SHA-256 values in `assets/sounds/question-sources.json`;
all matched. Their attribution and required MIT notice are present.

## Corrections and workflow

The tooltip feature now has an Unreleased changelog entry, README usage and
local-data/privacy notes, a working-agent guide, architecture details, and a
reproducible static preview with a synthetic project. Documentation describes
all three elapsed-time formats, worktree/projectless/CLI behavior, and runtime
resetting for each turn. The preview uses production tooltip formatting; its
drawn tooltip box is an illustration, not a native hover screenshot.

New `AGENTS.md` instructions, a corresponding CONTRIBUTING section, and PR
template checkboxes make documentation, public-data review, verification
evidence, and post-push CI checks explicit for every push. Build outputs,
installation backups, session data, and extracted desktop assets remain outside
the committed change. The installed tooltip build passed its self-test and its
startup log confirmed tray initialization; this does not establish manual
multi-monitor, DPI, fullscreen, or native-tooltip visual verification.

The accompanying commit body records the checks run for this update. This review
is limited to the stated range and current documentation; it is not an audit of
all repository history or a new license/legal review.
