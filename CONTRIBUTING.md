# Contributing

Thanks for helping improve CodexTray.

## Before opening a change

- Search existing issues and pull requests.
- Keep changes focused and explain user-visible behavior.
- Do not add telemetry, credential access, or model requests.
- Preserve support for both missing and partial rate-limit windows.

## Local workflow

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet restore
dotnet format
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --self-test
```

For taskbar changes, test the primary and at least one secondary display when
possible. Verify all styles, label and percentage positions, Windows scaling
above 100%, and left/right/custom placement. Regenerate the style gallery with
the screenshot-export command documented in the README when rendering changes.

## Pull requests

Describe the motivation, implementation, and manual verification. Include a
tightly cropped screenshot for visual changes and avoid exposing usernames,
notification icons, clocks, or other personal desktop information.

By contributing, you agree that your contribution is licensed under the MIT
License included in this repository.
