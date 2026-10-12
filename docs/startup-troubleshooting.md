# Startup troubleshooting

CodexTray writes startup diagnostics to
`%LOCALAPPDATA%\CodexTray\startup.log`. Each entry includes a process ID.

The main milestones are:

- `Launch requested`: managed application code started; includes the executable
  path, version, package context, and whether `--startup` was supplied.
- `Launch skipped`: another instance already holds the single-instance mutex.
- `Loading settings`: the instance is loading persisted display preferences.
- `Tray initialized`: notification icon, widgets, and timers were created.
- `Fatal application error`: startup or the UI message loop failed; the process
  exits with code 1 so the startup supervisor can retry it.
- `Application exited normally`: the message loop ended without an exception.

## Inspect the registration

In Task Scheduler, find `CodexTray-<your Windows user SID>`. The task runs only
when that user signs in, in their interactive desktop, with normal permissions.
It launches the installed executable with `--startup-supervisor`, which starts
the widget with `--startup` and observes its exit code.

The task delays ten seconds after sign-in. The application also gives the shell
ten seconds to initialize. The supervisor retries any nonzero child exit or
launch exception up to three times at one-minute intervals. This includes native
startup failures and forced process termination. It records each child PID,
exit code, and retry in the log. Task Scheduler's own restart setting also covers
failure to start the supervisor; the app does not rely on that setting alone to
handle child exit codes. Battery and idle conditions do not gate execution, and there is no
time limit that could stop the widget after several days. A normal **Exit** is
successful and does not trigger recovery. There are two processes during
automatic startup: the supervisor and the widget. No polling or usage requests
are performed by the supervisor while it waits for the widget to exit.

For the current user, PowerShell can show the registration and last result:

```powershell
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$task = Get-ScheduledTask -TaskName "CodexTray-$sid"
$task | Select-Object TaskName, State, Actions, Triggers, Settings
$task | Get-ScheduledTaskInfo
Get-Content "$env:LOCALAPPDATA\CodexTray\startup.log" -Tail 50
```

`0` is a successful exit. `267009` (`0x41301`) means the task is still running,
which is expected for the widget. Other results should be checked alongside the
application log and the Task Scheduler Operational log, if that log is enabled.
The scheduler retains the last result even when an executable fails before its
managed entry point and cannot write an application log.

## Repair or upgrade startup

Run the executable from its permanent installation folder:

```powershell
.\CodexTray.exe --enable-startup
```

This registers or repairs the task first, then removes the old CodexTray Startup
shortcut and legacy Run entry. Existing shortcuts continue to work until this
explicit upgrade. Registration errors are logged and leave the old shortcut in
place. Moving the executable later requires repeating this command from its new
location.

To turn automatic startup off, use the tray menu or `--disable-startup`.
This removes the task and legacy launchers without closing the current widget.

## Limits of historical diagnosis

### Packaged launch contexts

Windows can redirect AppData files and HKCU settings when an executable is
launched by a virtualized MSIX desktop app. The same apparent path can then
refer to different files for the packaged launcher and ordinary Windows
Explorer. For example, a packaged launcher may see its copy under
`%LOCALAPPDATA%\Packages\<package family>\LocalCache\Local\CodexTray\startup.log`,
while the widget started at sign-in uses the normal AppData log. Display settings
can also differ because registry writes are virtualized.

Check the `package=` field in new launch entries. Task Scheduler launches the
widget in the normal user desktop context. To inspect that context's log and
registry, open PowerShell from Windows Explorer or the Start menu. A terminal
launched by a packaged development app can inherit the app's redirected view.
See Microsoft's [MSIX virtualization documentation](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization).

### Missing evidence

A missing entry in an old application log does not prove that Windows never
launched the executable. Older builds checked the mutex before logging and read
settings outside their exception handler. They could exit without recording a
reason. Windows' Shell-Core Run-key events also do not provide a complete
history of Startup-folder shortcut execution. If no process audit or error
report was retained, the exact cause of a past missed launch cannot be recovered
from those logs alone.

Testing **Run** in Task Scheduler verifies the registered executable and desktop
session, but the logon trigger must still be checked at the next real sign-in.

### Windows blocks the executable

If Windows reports that an Application Control policy blocked the file, inspect
`Microsoft-Windows-CodeIntegrity/Operational` in Event Viewer. Event 3077 records
an enforced block; associated events can identify Smart App Control and the
exact executable hash. This can happen before CodexTray writes a log entry.

```powershell
Get-WinEvent -FilterHashtable @{
    LogName = 'Microsoft-Windows-CodeIntegrity/Operational'
    StartTime = (Get-Date).AddDays(-1)
} | Where-Object { $_.ToXml() -match 'CodexTray' } |
    Select-Object TimeCreated, Id, Message
Get-AuthenticodeSignature .\CodexTray.exe
```

Search the event XML as well as the rendered message: not every event includes
the executable path in its message. A block recorded during an upgrade does not
prove that an earlier startup attempt was blocked.

Smart App Control can reject an unknown unsigned build even when an older build
runs. Rebuilding changes the file's identity; the previous build's reputation
does not establish trust for the new file. Publish releases with a trusted code
signing certificate, or use a previously working release while the new build's
trust issue is resolved. Restart attempts cannot override a security block.
See Microsoft's [Smart App Control overview](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview)
and [guidance for app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).
