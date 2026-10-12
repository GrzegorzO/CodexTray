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
- One widget on every connected display
- Settings and per-monitor positions remembered in the current user profile
- Optional start with Windows
- Refreshes every two minutes without repainting unchanged values
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
| **Start with Windows** | Toggle per-user automatic startup |

Dragging a widget horizontally switches that display to custom placement.
Display and placement settings are stored under `HKCU\Software\CodexTray`.
All display settings and per-monitor offsets are read again when the app starts.

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
