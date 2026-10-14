# MonitorCenter agent notes

MonitorCenter is a Windows 11 WPF tray app (.NET 10, self-contained single-file exe) that sets real monitor brightness over DDC/CI and WMI. User-facing docs are in `README.md`.

## Build and test

```powershell
dotnet build MonitorCenter.sln -c Release
dotnet test MonitorCenter.sln -c Release --no-build
```

Keep the build at zero warnings and all tests passing. Hardware tests are skipped unless `MONITORCENTER_HARDWARE_TESTS` is set (see README); never enable the write tests without being asked, because they change the user's monitor brightness.

## Updates and releases (Velopack)

The app installs and updates itself with [Velopack](https://velopack.io) from this repo's GitHub releases. Users install `MonitorCenter-Setup.exe` once; after that every release reaches them through the app. A broken release or a broken updater reaches every installed copy, so treat this area with care.

How it fits together:

- `src/MonitorCenter/Program.cs` is the entry point. `VelopackApp.Build()...Run()` must stay the first thing in `Main`, before WPF starts; Velopack runs install, update and uninstall hooks there and may exit the process. `App.xaml` is compiled as a `Page` and `StartupObject` is set in the csproj for this reason; don't turn it back into an `ApplicationDefinition`.
- `Services/AppUpdater.cs` (`IAppUpdater`, `VelopackAppUpdater`) is the only code that talks to Velopack. Updates are applied with `WaitExitThenApplyUpdates` followed by the app's normal graceful shutdown (`App.OnExitRequested`), so the tray icon and single-instance mutex are released before the updater restarts the app with `--startup`. Don't switch to `ApplyUpdatesAndRestart`, which exits the process abruptly.
- `Services/UpdateCoordinator.cs` drives the tray menu item text ("Check for updates" → "Checking for updates…" → "No new updates" / "Updating… n%" → "Updated!") and the background check (2 minutes after launch, then every 24 hours). It is unit-tested through a fake `IAppUpdater` in `tests/MonitorCenter.Tests/UpdateCoordinatorTests.cs`; extend those tests when changing its behaviour.
- `UI/TrayIconController.cs` hosts the menu item (`StaysOpenOnClick`, header bound to `MenuText`) and clickable notifications.
- `StartupRegistration.Unregister()` runs at uninstall and only removes the startup entry if it points at the copy being uninstalled.

Rules:

- **Never change the pack id `0xMalaz.MonitorCenter`** in `tools/Build-Release.ps1`. Installed copies find their updates by it, and it decides the install folder (`%LocalAppData%\0xMalaz.MonitorCenter`). It is deliberately different from the settings folder `%LocalAppData%\MonitorCenter`, so uninstalling never deletes user settings.
- Keep `--mainExe MonitorCenter.exe` matching the assembly name, and keep `RepositoryUrl` in `AppUpdater.cs` pointing at this repo.
- Don't upgrade the `Velopack` NuGet package without upgrading `vpk` in `dotnet-tools.json` to the same version.
- Builds run with `dotnet run` or from `dist\` are not installed, so updating is unavailable there and the menu says "Updates need the installed app". That is expected.

### Releasing

Releases are published by `.github/workflows/release.yml` when a `vX.Y.Z` tag is pushed. It checks the tag matches `<Version>` in `src/MonitorCenter/MonitorCenter.csproj`, runs the tests, builds delta and full packages with `tools/Build-Release.ps1`, uploads them to a draft release, adds `MonitorCenter-Setup.exe` and generated notes, then publishes.

1. Raise `<Version>` in the csproj and merge that to `main`.
2. `git tag vX.Y.Z` on that commit on `main`, then `git push origin vX.Y.Z`.

Only tag or publish a release when the user asks: a published release is installed automatically on every user's machine. Never hand-edit or delete assets of a published release (`releases.win.json`, `*.nupkg`); installed copies depend on them.

### Testing an update end to end

Unit tests don't cover the real install/update/restart path. To test it locally without publishing anything:

```powershell
./tools/Build-Release.ps1 -Version 1.2.0 -OutputDir C:\Temp\feed
C:\Temp\feed\0xMalaz.MonitorCenter-win-Setup.exe --silent --installto C:\Temp\mc-install
./tools/Build-Release.ps1 -Version 1.2.1 -OutputDir C:\Temp\feed
$env:MONITORCENTER_UPDATE_SOURCE = 'C:\Temp\feed'
Start-Process C:\Temp\mc-install\MonitorCenter.exe -ArgumentList '--startup'
```

Then use **Check for updates** in the tray menu. `MONITORCENTER_UPDATE_SOURCE` makes the app read updates from that folder instead of GitHub. Only one MonitorCenter runs per user (single-instance mutex), so exit any other running copy first, and restore it afterwards. Clean up with `C:\Temp\mc-install\Update.exe uninstall --silent`. The test install rewrites the "Start with Windows" registry entry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `MonitorCenter`) when launched without `--startup`; note its value before testing and put it back after.
