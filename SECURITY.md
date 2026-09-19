# Security policy

## Supported versions

Security fixes are applied to the latest release only.

## Reporting a vulnerability

Please use GitHub's **Report a vulnerability** private security-advisory flow
instead of opening a public issue. Include reproduction steps, affected Windows
and Codex versions, and the expected impact. Do not include credentials, tokens,
or private account data.

CodexTray intentionally delegates authentication to the locally installed Codex
app server and should never read credential files directly. Reports involving
unexpected credential access, command execution, startup persistence, or data
transmission are especially important.
