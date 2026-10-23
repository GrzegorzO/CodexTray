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
and collapse it completely at zero. The animation timer invalidates the complete
buffered window while visible and active; the reduced-motion setting draws a
static frame. Activity layout changes synchronously paint a complete buffered
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
is persisted and defaults to icon/count.
Repeated icons can share one animation phase or use an index-based phase offset.
The synchronization preference applies immediately and is persisted, defaulting
to the existing staggered timing.

`ReadTasks` retains stable thread IDs and working directories alongside each
main task's optional project color. The monitor preserves the order of tasks
already working and appends newly active tasks. Their icons grow outward without
reordering existing icons or changing their animation offsets. Completed tasks
are removed; a resumed task rejoins at the outer edge. When coloring or badges
are enabled in repeated-icon mode, `ProjectColorReader`
reads `.codex-global-state.json` with shared read access and caches only the
project-color/marker/assignment metadata. File timestamp and length changes trigger
reloads; partial writes preserve the last valid mapping. Explicit project
assignments win over workspace hints and the longest unambiguous root match.
Projectless or unmatched tasks and unsupported/default colors use the normal
icon accent. Palette tokens use the desktop dark-sidebar RGB values. No desktop
state is needed for project appearance while both options are off or in
icon-and-number mode; factory-controller matching can still read it in those
modes. No Codex settings or state files are written.

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

This design intentionally avoids DLL injection and Explorer patching. Out-of-context
foreground and window-location accessibility event hooks restore z-order after
transient overlays. Passive visibility polling never raises already-visible
widgets. It checks each taskbar's visible geometry and fullscreen windows above
that taskbar on its own monitor, ignoring the widget process and DWM-cloaked or
minimized windows. It does not hit-test animated transparent pixels or use a
different monitor's foreground application to decide visibility. Windows shell
surfaces such as Start and Search are recognized separately because they leave
the taskbar visible. Animated frames repaint the entire double-buffered window
to keep the batteries and icons together under fullscreen-video composition.

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
