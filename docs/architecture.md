# Architecture

CodexTray is a dependency-free .NET WinForms application split into four small
areas:

1. `CodexRateLimitClient` locates Codex, performs the app-server handshake, and
   parses the rate-limit response.
2. `UsageSnapshot` models quota windows and presentation-friendly reset text.
3. `TrayIconRenderer` and `TaskbarWidgetForm` render the notification icon and
   taskbar widgets.
4. `TrayApplicationContext`, `WidgetSettings`, and `StartupManager` coordinate
   refreshes, menus, monitors, persisted preferences, and application lifetime.

`AgentActivityMonitor` independently observes local session lifecycle events and
writer locks once per second. Codex runtime status is scoped to the app-server
owning each thread, so the short-lived quota server cannot supply desktop-wide
activity. The monitor caches rollout paths and committed byte offsets, ignores
incomplete JSONL writes, and classifies sessions using their `session_meta` records.
Only metadata whose session ID matches the writer-lock owner is applied;
ancestor metadata copied into forked logs cannot change the task's identity.
Subagent sources and nonempty `parent_thread_id` links are excluded, including
background guardian sessions. A `forked_from_id` alone does not exclude a main
chat fork. A main task counts only while its writer lock is held and its latest
turn has started without completing or aborting. Sessions without metadata are
not counted. Released locks clear cached activity after process exit.
Structured user messages beginning with `Pipeline dispatch id: <UUID>` and
containing role/round and `Factory instructions: <path>/AGENTS.md` headers
identify factory jobs. A job is hidden only while an active non-worker main task
matches its factory project through desktop project assignments or its working
directory. Without a matching active controller, each worker stays visible.
Project matching runs independently of color/badge settings and includes desktop
project-ID aliases. Hidden worker completions are silent; user-created main-chat
forks are preserved even when their copied history contains a dispatch.
Only classification fields, lifecycle fields, and factory dispatch headers are
interpreted; conversation content is neither retained nor logged. This local file format is
an implementation dependency and does not cover remote-only or ephemeral work.

The monitor also reports stopped main-turn lifetimes from lifecycle timestamps,
including interruption and released writer locks. Initial reads and truncated
history establish a silent baseline. Incremental reads preserve completions
even when the next turn starts between polls. `AgentCompletionSound` plays an
embedded PCM WAV asynchronously when any stop meets the saved minimum runtime;
simultaneous stops share one cue, independent of widgets and display settings.
Structured `request_user_input` and `request_user_input_async` function calls
are validated for a nonempty question and deduplicated by call ID. The initial
read and a truncated log establish a silent baseline. A session first discovered
later can still alert for a question timestamped after monitoring began, so a
quick question in a new chat is not missed. Main tasks and standalone factory workers qualify even
if the turn finishes in the same read. Subagents and hidden factory workers do
not. A separately persisted question-sound selection has six embedded Hmm/Huh PCM WAV
choices and its own managed custom WAV. An enabled question cue takes priority
over a completion found in the same poll, with no minimum-runtime requirement.
Sound IDs are validated against the bundled catalog. Audio is embedded for
single-file publishing, with attribution and license notices copied alongside
the executable. Defaults are None and 30 seconds.
Custom PCM WAV selections are validated and staged before atomically replacing
the managed copy in `%LOCALAPPDATA%\CodexTray\sounds`. The registry stores the
Custom selection and original filename. Playback uses the managed copy rather
than relying on the source file remaining available; invalid imports preserve
the existing copy and preferences.

`AgentIndicatorRenderer` draws eleven native loading styles, including a robot
head with blinking eyes, a glowing antenna, and a gentle bob over a slower
3.6-second cycle. Widgets reserve a separate strip at the selected outer edge
and collapse it completely when there are no displayed slots. Aggregate mode
counts only running tasks; repeated mode also reserves retained finished slots.
The animation timer presents a complete per-pixel alpha
frame while visible and active; the reduced-motion setting draws a
static frame. Activity layout changes synchronously present a complete
frame after resizing so newly exposed space is transparent immediately, even
with animation disabled. Counter visibility, style, motion, and side use the
same per-user registry settings as the existing display options.
The native indicator drawing uses a fixed 1.3 scale, including the count
and spacing. The old size registry preference is ignored and the size submenu
has been removed. Repeated-icon mode reserves one slot per working agent and reverses
slot order on the left, keeping the first icon nearest the battery controls.
The battery group stays anchored during activity resizing where taskbar bounds
permit it. Left-side icon positions account for rounded physical strip widths,
so new icons do not shift existing ones by a fraction of a pixel. Display mode
is persisted and defaults to one icon per task.
Repeated icons can share one animation phase or use an index-based phase offset.
The synchronization preference applies immediately and is persisted, defaulting
to the existing staggered timing.

`ReadTasks` retains stable thread IDs and working directories alongside each
main task's project name, recorded turn start, and optional project color.
The monitor preserves the order of tasks
already working and appends newly active tasks. Their icons grow outward without
reordering existing icons or changing their animation offsets. Completed tasks
become static shortcuts at 40% opacity, with a frozen runtime and a Finished
tooltip; a resumed task replaces its old shortcut at the outer edge.
`FinishedTaskTracker` maintains the shared in-memory slots on the UI thread.
The monitor supplies incremental stopped-task snapshots independently of sound
qualification; initial/truncated history is excluded. Temporary read failures
and factory visibility changes do not manufacture completions. Successful
navigation dismisses only finished slots across all monitors. Hidden counters
and mode switches retain the slots; exiting clears them.
`ProjectColorReader` also parses version-1 desktop unread state for local hosts.
The tracker dismisses a finished slot only after observing its unread flag clear
within a still-present identity/host scope. Missing or unsupported data and
removed scopes do not acknowledge tasks; transitions between polls may be missed.
No read-state writes or credential reads are involved. Finished slots render a
fixed frame and badge into a transparent layer at the target scale before
applying 40% opacity to the whole layer, including white details. RGB colors
are unchanged. The widget renders into a transparent premultiplied-BGRA surface;
`LayeredWidgetSurface` copies it to a top-down 32-bit DIB and presents it through
[`UpdateLayeredWindow` with `ULW_ALPHA`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow).
No `TransparencyKey` or window-wide opacity is configured, so finished pixels
blend into the actual taskbar instead of a nearly black key color. Each native
presentation releases its DIB and memory DC, including failure paths. State,
layout, animation, and visibility changes present complete frames directly;
painting can also refresh them. Alpha-zero pixels remain click-through. Static
widget previews composite the same transparent surface over synthetic backgrounds.
Finished-only widgets stop the animation timer. `ProjectColorReader`
reads `.codex-global-state.json` with shared read access and caches project
names, colors, markers, assignments, roots, hints, and local unread-ID sets.
File timestamp and length changes trigger reloads; partial writes preserve the
last valid mapping. Explicit project
assignments win over workspace hints and the longest unambiguous root match.
Projectless or unmatched tasks and unsupported/default colors use the normal
icon accent. Palette tokens use the desktop dark-sidebar RGB values. Project
names are resolved independently of colors, badges, and display mode. Names
follow canonical/legacy project IDs and explicit assignments, including tasks in
worktrees. Missing names fall back to project root or workspace folder names;
explicitly projectless tasks show No project. No Codex settings or state files
are written.

Repeated-icon tooltips share the session-navigation hit test, including project
badges and left-side slot reversal. Each tooltip shows the project name and
current turn runtime: seconds below one minute, minutes and seconds through five
minutes, and whole minutes above five minutes. A separate one-second UI timer
refreshes hover text even with animation disabled. Same-count activity updates
refresh names and starts without forcing a repaint. Battery and aggregate-mode
hovering retain the usage tooltip. Elapsed time uses the recorded UTC turn start,
includes tool/input waits, survives app restarts, and resets for a new turn.

Project markers travel alongside the color in `WorkingTask`. The independent
`Show project icon` preference draws a stationary badge at the upper-right of
each repeated icon. `DesktopProjectIcons` locates the current desktop installation
through running processes, the current user's package registry, or installation
directories. It reads the ASAR index and the icon data in the app's assets without
executing JavaScript. The sidebar's folder fallback and the desktop's authored
16px named presets are cached as vector geometry. `ProjectVectorIcon` converts
SVG paths to GDI+ curves, preserving separate filled paths and even-odd holes.
`ProjectMarkerRenderer` draws the vectors directly at the requested scale with
transparent surroundings. Built-in drawings cover unavailable/unsupported
assets; emoji use monochrome font glyphs. Badges always take the
project accent, even when loading-icon coloring is off. Unknown markers fall
back to folders; unmatched/projectless tasks have no badge. Badge geometry fits
inside the existing indicator width and scales with the loading icons.

## Token reports

`TokenReportForm` is a separate native WinForms window opened by **Show usage
report** in the shared tray/widget menu. Each period change, date change, project
selection, explicit refresh, or reopening regenerates the report asynchronously.
Window presets request fresh app-server quota metadata; failures fall back to
available reset history with a visible notice. Cancellation suppresses outdated
results; a semaphore serializes the reader's cache access. Closing cancels reads.
Quota lookup is wrapped in a worker task as well, because executable discovery
can block synchronously before the app-server client's first await. This lets
the first loading frame paint and keeps controls responsive throughout lookup.
`ReportReadProgress` reports discovery, file reads (including byte progress in a
large file), and grouping. Updates are throttled to 100 ms and marshalled through
`Progress<T>` to the UI; cancelled, superseded, or completed requests ignore late
notifications. A themed progress bar animates at unknown-length stages and shows
file-reading progress when the total is known. A UI timer updates animation and
elapsed time; Cancel preserves the completed view. Disposal is idempotent.

`ReportTheme` follows the Windows `AppsUseLightTheme` preference and high-contrast
system colors. .NET's native system color mode themes native controls and menus;
the report listens for user-preference changes to recolor its custom cards and
charts on the UI thread. Preview exports force light and dark only within their
own process. No Windows appearance preferences are written. `ReportDrawing`
uses Segoe UI in logical pixel units, scaling once per graphics surface.

`ReportHeading`, `ReportSummary`, `ProjectTokenChart`, and `TokenDetailChart`
render the dashboard hierarchy. The first project is selected automatically;
selection updates immediately and then regenerates from disk. Refreshes preserve
the last completed view, with a loading panel and error/retry states. Keyboard
selection scrolls its project into view. The initial window fits the display's
working area, with wrapping filters and scrollable chart/detail viewports at
smaller window sizes. Raw project IDs and coverage diagnostics are available
in tooltips and About this data, rather than taking space in the main dashboard.
`TokenBreakdown` partitions total tokens into other input, cached input, other
output, and reasoning. Cache writes are displayed separately as an input subset,
avoiding overlap. Inconsistent cached/reasoning subsets are capped for geometry;
the raw recorded counters remain accessible in About this data.

Calendar controls own the custom range independently of quota presets. A guarded
date-change transaction moves the opposite endpoint only when the edited pair
would invert, then starts one regeneration. Preset results update their summary
without overwriting hidden custom dates. Custom summaries display inclusive
calendar days, while filtering retains the exclusive next-day midnight boundary.

`TokenUsageReportReader` scans `sessions` and `archived_sessions` under `CODEX_HOME`
(default `%USERPROFILE%\.codex`). Unchanged file results are cached by path,
length, and last-write time; changed files are reparsed. Only matching rollout
metadata, token counts, and Codex reset observations are retained in memory.
Cumulative token deltas suppress duplicate notifications. Inherited fork records
establish baselines without contributing usage before the owning session's
creation timestamp. Archived duplicate session IDs contribute once. Partial
final lines are retried after changes; malformed usage records and read failures
are counted. Conversation records are neither retained nor logged.

Project identities come from `ProjectColorReader` independently of display names,
with canonical/legacy IDs, explicit assignments, workspace hints, roots, and full
directory fallbacks. Explicitly projectless tasks share a No project group.
Unassigned subagent worktrees can follow recorded ancestor project assignments.
The existing color and marker renderers supply bars and labels; current metadata
is used rather than a historical project-settings ledger. All projects remain
available through horizontal scrolling. Project clicks and arrow keys regenerate
the selected detail view. Input plus output defines total tokens; cached input,
cache writes, and reasoning are labelled as subsets rather than added again.

Period resolution orders observed reset identities separately for 300- and
10,080-minute windows. Reset estimates within two minutes of a stable identity
are coalesced into the same window, keeping its start and using the latest end;
inferred starts can extend slightly earlier when an estimate is corrected.
A larger reset change before its former expiry closes the old
window at the first changed observation, with an approximate-boundary notice.
Other starts use reset minus nominal duration. The previous window is the last
observed completed window; no arbitrary preceding seven-day interval is invented
when history is absent. Custom dates use local midnight boundaries and an
exclusive next-day end (including daylight-saving transitions). Reports cover
available local logs, not remote or deleted usage, quota attribution, or billing.
`--export-report-preview` renders the real form with synthetic data in an
offscreen window in light and dark modes without session/project metadata reads or quota requests. Icons
may use installed desktop vectors. This is a static preview, not interactive
desktop verification.
Loading previews use fixed synthetic file counts and a stopped timer.
`--print-report-windows` provides ranges and project counts without exposing
project identities. WinForms configuration precedes native color-mode setup,
including the self-test and export entry points, so theme initialization cannot
create a window before text-rendering configuration is established.

## Refresh lifecycle

```text
2-minute timer / Refresh command
              │
              ▼
      start codex app-server
              │
              ▼
 initialize → account/rateLimits/read
              │
              ▼
       parse UsageSnapshot
              │
       changed since last read?
          ┌───┴───┐
         no      yes
          │        │
       no paint   redraw icon/widgets
```

The app server is terminated after each bounded request. Refreshes are serialized
with a semaphore, have a 20-second timeout, and are cancelled during shutdown.

## Taskbar presentation

Windows 11 has no supported API for inserting an arbitrary custom control into
its XAML taskbar. Each widget is therefore a borderless, non-activating Win32
window owned by the matching Explorer taskbar and kept in the documented topmost
z-order band. Monitor-specific offsets are stored in the current user's registry.
Labels, percentages, and activity counts use pixel font sizes matching the drawn
geometry, so a high-DPI graphics surface cannot enlarge text independently of
the batteries and icons. Preview rendering accepts an explicit surface DPI for
regression checks from 96 through 288 DPI.
Battery percentages render Segoe UI Semibold at an 11-pixel size into a 4×
transparent premultiplied-alpha layer, then downsample with high-quality bicubic
interpolation. Grayscale antialiasing avoids ClearType color fringes on the
transparent taskbar; a half-pixel contrast shadow avoids the heavy offset edge
of the former bold text. The same renderer handles centered and external
percentages. `--export-screenshots` includes `bar-text-preview.png` with synthetic
43%/45% Neon bars at 1×, 1.25×, 1.5×, and 2× render scales and all three positions.
These static examples do not establish manual display or Windows scaling tests.

This design intentionally avoids DLL injection and Explorer patching. Out-of-context
foreground and window-location accessibility event hooks restore z-order after
transient overlays. Passive visibility polling never raises already-visible
widgets. It checks each taskbar's visible geometry and fullscreen windows above
that taskbar on its own monitor, ignoring the widget process and DWM-cloaked or
minimized windows. It does not hit-test animated transparent pixels or use a
different monitor's foreground application to decide visibility. Windows shell
surfaces such as Start and Search are recognized separately because they leave
the taskbar visible. Animated frames present the entire alpha surface to keep
the batteries and icons together under fullscreen-video composition.

## Automatic startup

`StartupManager` registers a per-user Task Scheduler logon task with an
interactive token and normal privileges. It is independent of any packaged
development app's redirected Startup-folder and registry view. The task starts
`StartupSupervisor`, which launches the widget and waits for its exit without
polling. Nonzero exits and launch exceptions receive up to three retries, one
minute apart. A successful exit ends supervision. The scheduler has no runtime
limit or battery restrictions, and duplicate supervisor and widget instances
are prevented by separate named mutexes.

`Program` logs before the widget mutex check and settings reads, and records
initialization and fatal UI errors. Launch entries include the package context
because Windows can redirect AppData logs and settings for packaged parents.
