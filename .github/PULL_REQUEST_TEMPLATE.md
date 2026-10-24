## Summary

Describe the user-visible change and why it is needed.

## Documentation

- [ ] Unreleased changelog and affected README, guides, and architecture notes agree with the implementation (or this is a documentation-only correction)
- [ ] New or redistributed assets retain source attribution and required licenses

## Verification

- [ ] `dotnet format --verify-no-changes --no-restore`
- [ ] Release build succeeds with no warnings
- [ ] Built-in self-test passes
- [ ] Taskbar behavior was checked on the affected display configuration
- [ ] Screenshots were updated for visual changes

Describe checks actually run and any manual scenarios that remain untested.

## Privacy and security

- [ ] No telemetry, credential access, or model request was added
- [ ] No private account or desktop information appears in screenshots or logs
