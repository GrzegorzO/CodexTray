# Changelog

All notable changes are documented here. This project follows
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Purple battery color for the final 5% of remaining usage
- Working local-agent counter with eleven loading styles: rotating arc, orbiting
  dots, pulsing dot, animated bars, spinning spokes, bouncing dots, ripple rings,
  rotating diamond, radar sweep, hourglass, and an animated robot head; hidden
  without spacing when idle
- Saved counter visibility, animation, and far-left/far-right placement settings
  with right-side placement by default
- Local activity diagnostics via `--print-activity` and a loading-style gallery
- Small (70%), Normal (100%), and Large (130%) agent-indicator sizes
- One-icon-per-agent mode with spaced icons growing outward on either side
- Saved synchronization toggle for repeated icons, with staggered timing retained
  by default

### Fixed

- Repaint animation as a complete buffered frame and keep visibility polling
  passive to avoid flickering when video is fullscreen on another monitor
- Detect fullscreen coverage per monitor without hit-testing the widget's own
  animated transparent surface; ignore minimized and cloaked windows
- Keep the tray icon available when no startup task exists and the COM binder
  reports the missing task as a FileNotFoundException
- Replace one-shot Startup-folder registration with a per-user logon task and
  startup supervisor that retries failures, runs on battery power, and has no
  execution time limit; a normal Exit stops both processes
- Log launch attempts before the single-instance check and settings reads, log
  successful tray initialization, and return failure for unhandled UI errors
- Persist display styles by stable names while continuing to load legacy numeric
  settings, preventing styles such as Neon from reopening as Carbon
- Tie each widget's visibility to its taskbar, including auto-hide and taskbars
  covered by browser, game, or other fullscreen windows
- Keep the widget visible while Start, Search, and other taskbar shell flyouts are open

## [0.1.0] - 2026-09-06

### Added

- Two-battery taskbar and notification-area usage display
- Eleven visual styles, including a color-independent Pencil-sketch meter
- Remaining-percent colors and proportional fill
- Static and reset-countdown labels with left/right placement
- Left, centered, or right percentage placement with independent label spacing
- Left, right, and per-monitor custom widget placement
- Multi-monitor support and display-change handling
- Two-minute refresh interval with change-aware, double-buffered rendering
- Persistent settings and optional startup through a standard per-user shortcut
- Codex app-server rate-limit integration and built-in self-test
