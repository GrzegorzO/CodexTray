# Codex Usage Taskbar Widget (CodexTray)

Glanceable Codex usage batteries for the Windows taskbar.

CodexTray shows the remaining capacity of your 5-hour and weekly Codex usage
windows without opening Codex. It is a small native WinForms application: no
browser, no Electron runtime, no telemetry, and no model request on refresh.

![All eleven CodexTray visual styles](docs/images/styles-gallery.png)

![All eleven CodexTray visual styles with time-left labels](docs/images/time-left-gallery.png)

## Highlights

- Two filled battery gauges: 5-hour on top, weekly on the bottom
- Blue at 90% or above, green above 50%, orange above 25%, red above 5%,
  and purple for the last 5%
- Eleven styles: Classic, Futuristic, Fluent, Minimal, High contrast, Neon,
  Glass, Pixel, Carbon, Dots, and a color-independent Pencil-sketch meter
- Static `5h` / `7d` labels or live time-to-reset labels
- Optional percentages and left- or right-side labels
- Percentages centered in, or placed to the left or right of, the batteries
- Left, right, or per-monitor custom taskbar placement
- Live working-agent count with eleven loading icons, including an animated robot head; hidden entirely when idle
- Counter at the far left or right (default), centered vertically, with optional animation
- Small (70%), Normal (100%), and Large (130%) agent indicators
- Icon and number, or one spaced icon per working agent, expanding outward
- One widget on every connected display
- Settings and per-monitor positions remembered in the current user profile
- Optional start with Windows
- Usage refreshes every two minutes without repainting unchanged values; agent activity updates every second
- Transparent taskbar surface with event-driven z-order recovery after overlays
- Uses the existing local Codex sign-in; it never reads or stores credentials

The second gallery shows the live time-to-reset label mode: the top and bottom
labels show the remaining time in the current 5-hour and weekly windows.

## Install

### From a release

1. Download `CodexTray-win-x64.zip` from the repository's **Releases** page.
2. Extract it to a permanent folder.
3. Run `CodexTray.exe`.
4. Right-click the tray icon and enable **Start with Windows** if desired.

The release build is self-contained, so a separate .NET installation is not
required. Windows may show a SmartScreen warning for unsigned community builds.
Review the source and release workflow before choosing **Run anyway**.

### Build from source

Requirements: Windows 10 or 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
and Codex Desktop or Codex CLI signed in locally.

```powershell
git clone <your-fork-or-repository-url>
cd CodexTray
dotnet restore
dotnet build --configuration Release
dotnet run --configuration Release
```

Create a single-file, self-contained executable:

```powershell
dotnet publish --configuration Release --runtime win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true --output .\publish
```

## Use

The top battery is the 5-hour window; the bottom battery is the weekly window.
Battery width and color both represent the percentage remaining.

Right-click either the taskbar widget or notification-area icon:

| Menu | Options |
| --- | --- |
| **Show details** | Show percentages and reset times |
| **Refresh** | Request a usage update immediately |
| **Show this widget in taskbar** | Show or hide widgets on every monitor |
| **Display → Style** | Eleven classic, modern, accessible, and decorative styles |
| **Display → Position** | Left side, Right side, or Custom by dragging |
| **Display → Show labels** | Show or hide the label beside each battery |
| **Display → Label content** | Static window names or time remaining |
| **Display → Label position** | Labels on the left or right |
| **Display → Show percentages** | Show or hide percentage values |
| **Display → Percentage position** | Left, centered, or right with collision-free label spacing |
| **Display → Working agents → Show counter when working** | Enable or disable the activity indicator (enabled by default) |
| **Display → Working agents → Loading icon style** | Eleven styles: Rotating arc (default), Orbiting dots, Pulsing dot, Animated bars, Spinning spokes, Bouncing dots, Ripple rings, Rotating diamond, Radar sweep, Hourglass, and Robot head |
| **Display → Working agents → Counter position** | Right of all controls (default) or left of all controls |
| **Display → Working agents → Indicator size** | Small (70%), Normal (100%, default), or Large (130%) |
| **Display → Working agents → Display mode** | Icon and number (default), or one icon per working agent |
| **Display → Working agents → Animate loading icon** | Toggle animation, including a static option for reduced motion |
| **Display → Working agents → Synchronize icon animations** | Animate repeated icons in step, or keep staggered timing (default) |
| **Start with Windows** | Toggle per-user automatic startup |

Dragging a widget horizontally switches that display to custom placement.
Display and placement settings are stored under `HKCU\Software\CodexTray`.
All display settings and per-monitor offsets are read again when the app starts.

![Working-agent loading icon choices](docs/images/working-agents-gallery.png)

The activity indicator shows an icon and the number of active local Codex threads,
including working subagents. It is vertically centered beside the full battery,
label, and percentage group. At zero, both the icon and count disappear and the
widget releases that space. Turning off animation keeps the selected icon static.
Size scales the indicator's icons, count, and spacing; the usage batteries keep
their existing size. In **One icon per agent** mode, two agents show two icons
with no number. Additional icons extend rightward on the right side and leftward
on the left side. The battery group stays anchored while space is available;
at a taskbar boundary the widget moves enough to fit.
Enable **Synchronize icon animations** to keep all repeated icons in the same
animation frame, including robot blinks and movement. Turn it off for staggered
timing. The preference is saved and takes effect immediately.

![Agent indicator sizes and display modes](docs/images/agent-layout-gallery.png)

The additional styles use familiar visual conventions: **Fluent** follows
[Windows' 4-pixel rounded bar geometry](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry),
**Minimal** uses a quiet determinate progress track, and **High contrast** combines
a strong outline with hatching so fill state is not communicated by color alone,
in line with [WCAG non-text contrast guidance](https://www.w3.org/WAI/WCAG21/Understanding/non-text-contrast.html).

## How it works

Every two minutes, CodexTray starts the locally installed Codex app server,
performs its initialization handshake, and requests `account/rateLimits/read`.
This reads account quota state; it does not send a model prompt or consume Codex
tokens. The process is closed immediately after the response.

Agent activity is read separately from local Codex session lifecycle events and
thread writer locks under `CODEX_HOME` (or `%USERPROFILE%\.codex`). A running turn
counts once; completion or interruption removes it. A released writer lock also
removes it, so a crashed Codex process cannot leave a phantom count. Loaded idle
threads do not count. This requires Codex's local writer-lock/session format;
remote/cloud-only tasks and sessions without those files are not included.
The monitor reads locally on a background thread and sends no model requests.
Animation runs only while a visible widget has a nonzero count.

CodexTray checks `CODEXTRAY_CODEX_PATH`, then `PATH`, then the Codex Desktop
installation below `%LOCALAPPDATA%\OpenAI\Codex\bin`.

Windows 11 does not provide a supported extension slot for an arbitrary custom
taskbar control. CodexTray therefore uses a compact, non-activating, topmost
window positioned over the selected taskbar area. Right-side placement detects
the primary notification-area boundary when Windows exposes it; custom dragging
is available when another taskbar layout needs adjustment.

When **Start with Windows** is enabled, a per-user Task Scheduler logon task
launches a small supervisor in your desktop session after a short delay. The
supervisor starts the widget and retries failed runs up to three times, one
minute apart. Choosing **Exit** ends both processes. The task runs on battery power and has no
runtime limit. It uses your normal Windows permissions and stores no password.
The task is named `CodexTray-<your Windows user SID>`.

Older Startup-folder shortcuts remain supported. To replace one with the new
task, turn **Start with Windows** off and on, or run the installed executable
with `--enable-startup`. The old shortcut is removed only after registration
succeeds. A task disabled in Task Scheduler appears unchecked in the app.

Startup diagnostics, including duplicate-instance exits and UI initialization,
are written to `%LOCALAPPDATA%\CodexTray\startup.log`. Task Scheduler also shows
the last run time and result, including failures before .NET reaches the app's
log. See [startup troubleshooting](docs/startup-troubleshooting.md).

## Privacy and security

- No analytics, telemetry, advertisements, or update service
- No direct reading of `auth.json`, API keys, passwords, or browser data
- No model prompts or generation requests
- Network/authentication behavior is delegated to the installed Codex app server
- Display preferences remain in the current user's Windows registry hive

See [SECURITY.md](SECURITY.md) for vulnerability reporting and supported builds.

## Development

```powershell
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --self-test
dotnet run --configuration Release --no-build -- --print
dotnet run --configuration Release --no-build -- --print-activity
dotnet run --configuration Release --no-build -- --export-screenshots .\docs\images
```

The project has no third-party runtime packages. CI builds and runs the self-test
on Windows for every push and pull request. Tags matching `v*` produce a
self-contained `win-x64` release archive.

See [CONTRIBUTING.md](CONTRIBUTING.md), [CHANGELOG.md](CHANGELOG.md), and the
[architecture notes](docs/architecture.md) for more detail.

## License

MIT. See [LICENSE](LICENSE).

CodexTray is an independent community project and is not affiliated with or
endorsed by OpenAI or Microsoft. Codex is a trademark of OpenAI.
