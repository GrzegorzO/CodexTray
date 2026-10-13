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
incomplete JSONL writes, and counts a thread only when its writer lock is held
and its latest turn has started without completing or aborting. Released locks
clear cached activity after process exit. Only lifecycle fields are interpreted;
conversation content is neither retained nor logged. This local file format is
an implementation dependency and does not cover remote-only or ephemeral work.

`AgentIndicatorRenderer` draws eleven native loading styles, including a robot
head with blinking eyes, a glowing antenna, and a gentle bob over a slower
3.6-second cycle. Widgets reserve a separate strip at the selected outer edge
and collapse it completely at zero. The animation timer invalidates only the
indicator strip while visible and active; the reduced-motion setting draws a
static frame. Counter visibility, style, motion,
and side use the same per-user registry settings as the existing display options.
Indicator size scales the native drawing by 0.7, 1.0, or 1.3, including the count
and spacing. Repeated-icon mode reserves one slot per working agent and reverses
slot order on the left, keeping the first icon nearest the battery controls.
The battery group stays anchored during activity resizing where taskbar bounds
permit it. Size and mode are also persisted, defaulting to Normal and icon/count
for existing installations.
Repeated icons can share one animation phase or use an index-based phase offset.
The synchronization preference applies immediately and is persisted, defaulting
to the existing staggered timing.

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
transient overlays. A lightweight visibility check hit-tests each taskbar's actual
z-order presence, keeping its widget hidden whenever that taskbar is auto-hidden or
covered by any fullscreen application. Windows shell surfaces such as Start and
Search are recognized separately because they leave the taskbar visible.

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
