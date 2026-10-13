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
Subagent sources and nonempty `parent_thread_id` links are excluded, including
background guardian sessions. A `forked_from_id` alone does not exclude a main
chat fork. A main task counts only while its writer lock is held and its latest
turn has started without completing or aborting. Sessions without metadata are
not counted. Released locks clear cached activity after process exit. Only
session classification and lifecycle fields are interpreted; conversation
content is neither retained nor logged. This local file format is
an implementation dependency and does not cover remote-only or ephemeral work.

`AgentIndicatorRenderer` draws eleven native loading styles, including a robot
head with blinking eyes, a glowing antenna, and a gentle bob over a slower
3.6-second cycle. Widgets reserve a separate strip at the selected outer edge
and collapse it completely at zero. The animation timer invalidates the complete
buffered window while visible and active; the reduced-motion setting draws a
static frame. Counter visibility, style, motion, and side use the same per-user
registry settings as the existing display options.
Indicator size scales the native drawing by 0.7, 1.0, or 1.3, including the count
and spacing. Repeated-icon mode reserves one slot per working agent and reverses
slot order on the left, keeping the first icon nearest the battery controls.
The battery group stays anchored during activity resizing where taskbar bounds
permit it. Size and mode are also persisted, defaulting to Normal and icon/count
for existing installations.
Repeated icons can share one animation phase or use an index-based phase offset.
The synchronization preference applies immediately and is persisted, defaulting
to the existing staggered timing.

`ReadTasks` retains stable thread IDs and working directories alongside each
main task's optional project color. Task IDs define a stable icon order across
polls. When coloring is enabled in repeated-icon mode, `ProjectColorReader`
reads `.codex-global-state.json` with shared read access and caches only the
project-color/assignment metadata. File timestamp and length changes trigger
reloads; partial writes preserve the last valid mapping. Explicit project
assignments win over workspace hints and the longest unambiguous root match.
Projectless or unmatched tasks and unsupported/default colors use the normal
icon accent. Palette tokens use the desktop dark-sidebar RGB values. No desktop
state is read for coloring while the option is off or in icon-and-number mode,
and no Codex settings or state files are written.

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
