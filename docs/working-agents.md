# Working-agent indicators

CodexTray can show how many local main Codex tasks have an in-progress turn.
Subagents and background helpers are excluded. The indicator sits beside the
usage batteries and disappears completely when the count reaches zero,
including the space it occupied.

This guide describes the current `main` branch. Check
[Unreleased in the changelog](../CHANGELOG.md#unreleased) before expecting these
options in an older release archive.

## Getting started

1. Run a build that includes working-agent indicators.
2. Start a task in a compatible local Codex Desktop or CLI session.
3. Right-click CodexTray's notification-area icon or its taskbar widget, then
   open **Display → Working agents**.
4. Leave **Show counter when working** enabled and choose the appearance you want.

The setting is enabled by default. When idle, use the notification-area icon to
access Settings even though the activity indicator itself is absent.

## Settings and defaults

All options below are under **Display → Working agents**. Changes apply
immediately to widgets on all monitors and are saved under
`HKCU\Software\CodexTray` for the next launch.

| Setting | Default | Behavior |
| --- | --- | --- |
| Show counter when working | On | Show activity when the count is nonzero. Turning it off hides the activity indicator. |
| Loading icon style | Rotating arc | Choose from eleven styles, listed below. |
| Counter position | Right of all controls | Place the indicator outside the full battery, label, and percentage group. Left is also available. |
| Indicator size | Normal (100%) | Small is 70% and Large is 130% of Normal. Icons, numbers, and spacing scale together; the batteries keep their existing size. |
| Display mode | Icon and number | Show one icon plus the count, or choose **One icon per task** to replace the number with repeated icons. |
| Animate loading icon | On | Animate the selected style. Turn off for a static frame. |
| Synchronize icon animations | Off | Repeated icons use staggered timing by default. Turn on to put them in the same animation frame. |
| Use project colors | Off | Available in **One icon per task** mode. Color each icon using its task's Codex project color. |
| Show project icon | Off | Available in **One icon per task** mode. Show the project's marker as a small badge at the upper-right of each loading icon. |

The indicator remains vertically centered at every size. Idle hiding is always
applied; there is no visible zero-state icon or reserved gap.

### Display modes

With two in-progress main tasks, **Icon and number** shows one icon followed by `2`.
**One icon per task** shows two icons side by side, separated by a gap, with no
number. Each icon represents one main task, rather than its progress or
remaining work.

Additional icons extend outward from the battery group: rightward when the
indicator is on the right and leftward when it is on the left. Existing battery
controls stay anchored where space permits; the widget shifts when needed to
fit within the available taskbar area.

Synchronization changes animation timing only. With synchronization on, robot
heads blink and bob together, and other styles share the same animation phase.
With it off, each repeated icon has an offset. In icon-and-number mode there is
only one activity icon, so the synchronization option has no visible effect.

![Static previews of the three sizes and both display modes](images/agent-layout-gallery.png)

### Project colors

Choose **Display mode → One icon per task**, then enable **Use project colors**.
Each icon uses its main task's project color from Codex Desktop's dark-sidebar
palette. Tasks in the same project use the same color; changing a project's
color updates its running icons on the next activity check. Synchronization
still controls animation timing independently of color.

Icons keep their task/color association on either side of the battery group.
The option is saved, starts off, and is unavailable in icon-and-number mode.
Switching back to icon-and-number mode leaves the single icon its usual blue.

A task's explicit project assignment takes priority, including when it runs in
a worktree. When no assignment is available, workspace hints and matching local
project roots can supply the mapping. The longest unambiguous root match wins.
Projectless tasks, default/unsupported project colors, missing metadata, or
unresolved project mappings fall back to the normal blue icon. These fallbacks
do not remove the task from the count.

![Static previews using sample green, yellow, and purple projects](images/project-colors-gallery.png)

### Project icon badges

Enable **Show project icon** to add a small, stationary project marker at the
upper-right of each repeated loading icon. It has its own saved switch and works
independently of **Use project colors**. The badge always uses its project's color,
even when the loading icon keeps its normal blue. Projects with default or
unsupported colors use blue for the badge too.

CodexTray draws simplified versions of the desktop's named project markers,
including folders, planes, terminals, and wrenches. Emoji markers use a
monochrome Windows font glyph tinted with the project color. Projects without a
supported marker use a folder; unmatched and explicitly projectless tasks have
no badge. Badges scale with the loading icon and retain the same task association
on either side of the batteries. Switching them off restores the unbadged icon.

![Project badges with blue and project-colored loading icons](images/project-icons-gallery.png)

### Icon styles

Rotating arc, Orbiting dots, Pulsing dot, Animated bars, Spinning spokes,
Bouncing dots, Ripple rings, Rotating diamond, Radar sweep, Hourglass, and
Robot head are available. Robot head has blinking eyes, a glowing antenna,
and a gentle bob on a slower animation cycle.

These are separate from the eleven **Display → Style** options, which control
the appearance of the usage batteries.

![Static previews of all eleven activity-icon styles](images/working-agents-gallery.png)

## What the count includes

- Each main local task with a recorded turn start and no matching completion or
  interruption counts once while Codex still holds its thread writer lock.
- Subagents and background helpers, including Codex's guardian sessions, do not
  count. A main task and two working subagents produce a count of one.
- Session metadata identifies workers by a subagent source or a parent-task link.
  A manually forked main chat remains a separate main task and can count
  independently.
- Turns waiting on tools, approval, or user input remain included until their
  completion or interruption is recorded. The count measures in-progress turns,
  not CPU activity.
- Open idle threads do not count. When the writer lock is released, for example
  after a Codex process exits, that thread is excluded.
- Every monitor shows the same count; having multiple widgets does not multiply it.

Activity is checked about once per second on a background thread. Discovery of
a new session can take a few seconds. The usage batteries have their own
two-minute refresh cycle; **Refresh** requests a quota update.

## Compatibility and local data

The activity monitor needs Codex's local `sessions` logs and `thread-writer-locks`
files, including session metadata identifying main tasks and workers. Sessions
without metadata are not counted. It looks under the `CODEX_HOME` environment
variable, or `%USERPROFILE%\.codex` when that variable is unset. CodexTray and Codex
must use the same local home directory. Restart CodexTray after changing `CODEX_HOME`.

These files are an implementation detail rather than a stable public status
API. No minimum compatible Codex version has been established. Changes to their
names, locking, or event format can affect detection independently of quota
display. Remote/cloud-only work, ephemeral sessions, and other sessions without
the required local files are not covered.

When project coloring or project badges are enabled in repeated-icon mode, CodexTray also reads
`.codex-global-state.json` from the same Codex home directory. It interprets
project colors and markers, task-project assignments, project-ID aliases, workspace
hints, and local project roots. The desktop state file is not modified. Its
format is also an implementation detail; incompatible or unavailable color
metadata falls back to blue. Codex CLI-only installations can still show the
main-task count without desktop project colors.

Session logs can contain prompts, responses, and tool output. CodexTray reads
the files to interpret session type, parent links, lifecycle event types, and
turn IDs. It keeps paths, read offsets, and current turn state in memory; it does
not save, upload, or log conversation content, change session contents, or read
credential files.
Activity checks do not start a model request or consume Codex tokens.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| No indicator while Codex is idle | Expected: it appears only for a nonzero detected count. |
| No indicator while a local task is running | Confirm the build includes this feature, **Show counter when working** is on, and CodexTray and Codex use the same `CODEX_HOME`. The session must have compatible local logs and writer locks. Missing indicators can mean unsupported activity, rather than zero activity. |
| A task waiting for approval still appears | Expected: its turn is still in progress. The count clears after completion, interruption, or release of its writer lock. |
| The count is higher than the number of chats being watched | Check for other local main tasks, including separate CLI sessions or manually forked main chats. Subagents and background helpers are excluded. |
| Icons do not animate together | Enable **Synchronize icon animations**. It is off by default and only has a visible effect with repeated icons. |
| Icons are static | Enable **Animate loading icon**. Animation stops while the widget is hidden or idle. |
| Use project colors is unavailable | Select **Display mode → One icon per task** first. The option does not apply to the single icon-and-number display. |
| Show project icon is unavailable | Select **Display mode → One icon per task** first. The switch is independent of Use project colors. |
| No badge on an otherwise working task icon | Confirm Show project icon is enabled and the task can be matched to a local project. Unmatched and explicitly projectless tasks have no badge. |
| A task icon remains blue with project colors enabled | Confirm the project has a supported custom color in Codex Desktop and the app state is available under the same `CODEX_HOME`. Projectless, default-colored, or unmatched tasks intentionally use blue. |
| A widget disappears during fullscreen video | Expected on the monitor whose taskbar is covered. A visible taskbar on another monitor should retain its widget. Auto-hidden taskbars also hide their widgets. |
| The widget flickers on another monitor during fullscreen video | Use a build containing the fullscreen flicker fix. If it persists, report monitor layout, display scaling, video/browser details, and widget settings. |

From a source checkout, inspect the detected count without starting the UI:

```powershell
dotnet run --configuration Release -- --print-activity
```

To inspect resolved colors as well, add `--project-colors`:

```powershell
dotnet run --configuration Release -- --print-activity --project-colors
```

The diagnostic prints the count and an RGB color or `default` for each task in
icon order. It does not print task titles, project names, or working directories.

For a [public bug report](https://github.com/GrzegorzO/CodexTray/issues/new?template=bug_report.yml),
include the CodexTray build or commit, Codex version, display configuration,
selected activity settings, reproduction steps, and the diagnostic count.
Do not attach Codex session logs, credential files, or an entire `.codex` folder;
these can contain private conversation or account data. Screenshots should be
cropped to the widget and exclude private desktop information.
