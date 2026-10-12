# Startup investigation: 11 October 2026

## Findings

The normal Windows user profile's startup log records successful entry into
CodexTray's managed startup path on 10 October at 10:51:18. There is no matching
entry for the sign-in on 11 October before troubleshooting began. No retained
CodexTray crash report or Code Integrity block was found for that sign-in.
The original shortcut targeted the installed executable with `--startup`, and
the installed executable could still run when launched manually.

The exact original failure remains unproven. The old application checked its
single-instance mutex before logging, read settings outside its exception
handler, and used a Startup-folder shortcut with no recovery process. A skipped
shortcut, a native failure before managed startup, or a silent early exit cannot
be distinguished retrospectively with the available evidence. Windows Run-key
events are not a complete record of Startup-folder execution.

There were two separate AppData/registry views: diagnostics launched by the
packaged development application initially saw its redirected profile, while
Windows startup used the ordinary user profile. Reading the normal profile
through a temporary interactive scheduled task recovered the actual daily
history. This explains misleading diagnostic results; it is not proof of the
original startup failure.

## Repository changes and validation

- Replace the shortcut registration with a per-user interactive logon task,
  with a short delay, normal permissions, battery operation, and no time limit.
- Add a supervisor that retries failed child launches/exits up to three times
  per supervisor session, one minute apart. Successful Exit stops supervision.
- Log before mutex/settings access, record process/package identity and UI
  initialization, and log fatal errors with a nonzero exit status.
- Preserve the legacy launcher until replacement registration succeeds.

Release build, self-tests, formatting, and task XML validation passed. A live
test of the supervisor build terminated its widget child at 14:28:56; the same
supervisor started a new child at 14:29:57, and the new child initialized its tray
at 14:30:09. Task Scheduler's restart setting alone did not recover the direct
widget process during testing, so recovery is implemented explicitly.

## Local installation and remaining work

At 14:31:45, Smart App Control blocked the final rebuilt executable. Code
Integrity events 3033, 3077, and 3118 identify the new executable and policy
`VerifiedAndReputableDesktop`. Its SHA-256 is
`89D418248D8D9F91C5DCC25E807797051B67943015D52A3A1DAE4FF3A5133B67`.
There is no corresponding CodexTray security block recorded earlier that day.
Both the original and new executables are unsigned; Windows accepted the old
file and rejected this new file. No usable user code-signing certificate was
found. Security settings were not changed.

The original executable was temporarily restored from
`dist/startup-backup-20261011/` and started through the new logon task at 14:35:28.
A retry of the final build was blocked again at 14:39:30.

### Successful installation of the previously tested revision

The earlier supervisor build was reproduced byte for byte. Its SHA-256 is
`8284177EECF6232D8462391D120420E5C234D0772FF5FFB15CBFC5C21530A706`, matching
the build used in the successful live recovery test. Windows accepted it, and
both its pre-install and installed self-tests passed. It was installed to
`dist/CodexTray.exe` and registered with `--enable-startup`.

At 14:44:10, the normal Windows task launched the supervisor, which started its
widget child. The task now uses `--startup-supervisor`; it has one logon trigger,
allows battery operation, and has no execution time limit. The installed
revision contains all startup recovery and diagnostic behavior described above.
The tray startup checkbox now manages the same scheduled task.

The installed revision differs from the final repository source only in
self-test logging: the source injects a no-op logger into recovery tests to
avoid putting simulated failures in the application log. The accepted revision
uses the normal logger in those tests. Normal application behavior is the same.
Both artifacts remain in `publish/startup-last-working/` and
`publish/startup-recovery/` respectively. No security policy, exclusion, or trust
certificate was changed to make installation work.

The exact reason Microsoft's reputation service accepted one unsigned hash and
rejected the other is not exposed by the retained events. Reproducing the known
working revision resolved local installation; reliable future releases still
need trusted code signing. Check the next real Windows sign-in: the current
session's on-demand tests do not prove the next logon trigger will fire.

Detailed local test captures are in the ignored `obj/` directory, including
`unpackaged-startup.log`, `supervisor-recovery-test.json`, and
`code-integrity-codextray.json`. The original executable and log backups remain
in the ignored `dist/startup-backup-20261011/` directory.
