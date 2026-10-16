# MonitorCenter

A lightweight Windows 11 tray app for changing the real hardware brightness of your monitors, all from one place.

MonitorCenter lives in the notification area. It has no main window and stays out of the taskbar and Alt+Tab.

## Features

- Adjust the brightness of every connected display from a single tray flyout.
- Uses the monitor's own brightness control (DDC/CI), not a software dimming overlay.
- Supports built-in laptop screens through Windows' brightness control.
- Save up to three brightness profiles and switch between them with one click.
- Rename displays so they're easy to tell apart.
- Follows the Windows light and dark theme.
- Picks up monitors as they're connected or disconnected.
- Keeps itself up to date from GitHub releases.

## Install

1. Download [`MonitorCenter-Setup.exe`](https://github.com/0xMalaz/MonitorManager/releases/latest/download/MonitorCenter-Setup.exe) from the [latest release](https://github.com/0xMalaz/MonitorManager/releases/latest).
2. Run it. MonitorCenter installs for your user account only, so it doesn't need administrator rights, and adds a Start menu shortcut.

The app is self-contained, so you don't need to install .NET. Windows SmartScreen may warn you the first time, because the installer isn't code-signed; choose **More info** and then **Run anyway**.

When you start it normally for the first time, MonitorCenter adds itself to Windows startup for your user account. You can turn this off with **Start with Windows** in the tray menu.

To uninstall, use **Settings > Apps > Installed apps**. Your profiles and display names are kept.

### Updating from 1.0.0

Version 1.0.0 was a plain zip without an updater. Exit it from the tray menu, then install with `MonitorCenter-Setup.exe` as above. You can delete the old folder afterwards. Your settings carry over.

## Updates

MonitorCenter only checks for, downloads and installs updates when you ask it to. Right-click the tray icon and choose **Check for updates**. The item shows the progress, then either **No new updates** or **Updated!**, after which MonitorCenter restarts.

## Usage

- **Open the flyout:** click the MonitorCenter icon in the notification area.
- **Set brightness:** click inside a display's square. The bottom is 0%, the middle 50%, and the top 100%. Hold and drag up or down to adjust continuously. You can also use the mouse wheel (2% per notch, including high-resolution wheels) or the arrow keys.
- **Rename a display:** click the **⋮** button on its square and choose **Rename**, type the new name in place, then click the check button (or press Enter). Esc cancels.
- **Profiles:** choose **New Profile**, enter a name, and choose **Save** to store the current brightness of every display. Click a profile at the top of the flyout to apply it. Its **⋮** button renames it the same way as a display, or deletes it.
- **Close the flyout:** press **Escape**, click elsewhere, or click the tray icon again.
- **Tray menu:** right-click the icon to refresh displays, toggle **Start with Windows**, check for updates, or exit.

Profiles and display names are saved to `%LOCALAPPDATA%\MonitorCenter\settings.json`.

## Display support

| Display | How brightness is controlled |
| --- | --- |
| External monitors | DDC/CI, using the Windows monitor configuration API, with MCCS VCP code `0x10` as a fallback |
| Built-in laptop screens | Windows WMI brightness control |

Displays that can't be controlled still appear, greyed out, and their tooltip explains why. MonitorCenter never saves settings to a monitor's internal memory.

Some monitors drop part of their DDC/CI replies. MonitorCenter retries each display a few times when it detects it, then asks a display that didn't answer again after 1, 3 and 10 seconds. It also refreshes a few seconds after the PC wakes up, the screens turn back on, or you unlock it. A display that worked earlier and stops answering shows **Reconnecting…** while these retries run.

### A display shows as unavailable

1. Turn on **DDC/CI** in the monitor's on-screen menu, if it has that option.
2. Try a different cable (DisplayPort or HDMI) or a different port on your graphics card.
3. Right-click the tray icon and choose **Refresh displays**.

Some monitors, docks, and adapters don't pass DDC/CI through at all.

## Building from source

Requirements: Windows 11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/0xMalaz/MonitorManager.git
cd MonitorManager
dotnet build MonitorCenter.sln -c Release
dotnet test MonitorCenter.sln -c Release --no-build
dotnet publish src\MonitorCenter\MonitorCenter.csproj -c Release -r win-x64 --self-contained true -o dist
```

The published app is `dist\MonitorCenter.exe`. Builds run this way can't update themselves; **Check for updates** says **Updates need the installed app**.

### Hardware tests

Tests that talk to real monitors are skipped by default, so a normal test run never changes your monitor settings. To run them:

```powershell
# Read-only display discovery
$env:MONITORCENTER_HARDWARE_TESTS = '1'

# Also change brightness slightly on each display, then restore it
$env:MONITORCENTER_HARDWARE_WRITE_TESTS = '1'

dotnet test tests\MonitorCenter.Tests\MonitorCenter.Tests.csproj -c Release --filter "FullyQualifiedName~HardwareIntegrationTests"
```

## Releasing

Releases are built by the [Release workflow](.github/workflows/release.yml) with [Velopack](https://velopack.io), which produces the installer and the update packages that installed copies download.

1. Raise `<Version>` in `src/MonitorCenter/MonitorCenter.csproj`, for example to `1.2.0`, and commit it to `main`.
2. Tag that commit and push the tag:

   ```powershell
   git tag v1.2.0
   git push origin v1.2.0
   ```

The workflow checks that the tag matches the project version, runs the tests, and publishes the GitHub release. Installed copies get it when their user chooses **Check for updates**.

To try a release locally before publishing it, build it into a folder and point an installed copy at that folder:

```powershell
./tools/Build-Release.ps1 -Version 1.2.0 -OutputDir C:\Temp\feed
$env:MONITORCENTER_UPDATE_SOURCE = 'C:\Temp\feed'
& "$env:LOCALAPPDATA\0xMalaz.MonitorCenter\MonitorCenter.exe"
```

Don't change the Velopack pack id (`0xMalaz.MonitorCenter`) in `tools/Build-Release.ps1`; installed copies use it to find their updates.

## Contributing

Issues and pull requests are welcome. Please run the tests before opening a pull request.

## License

MonitorCenter is released under the [MIT License](LICENSE).
