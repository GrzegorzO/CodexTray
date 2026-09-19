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
