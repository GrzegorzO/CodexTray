# Changelog

All notable changes are documented here. This project follows
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Purple battery color for the final 5% of remaining usage

### Fixed

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
