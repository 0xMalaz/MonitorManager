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

## Install

1. Download `MonitorCenter-Windows-x64.zip` from the [latest release](https://github.com/0xMalaz/MonitorManager/releases/latest).
2. Extract it to a permanent folder, for example `%LOCALAPPDATA%\Programs\MonitorCenter`.
3. Run `MonitorCenter.exe`.

The executable is self-contained, so you don't need to install .NET.

When you start it normally for the first time, MonitorCenter adds itself to Windows startup for your user account. You can turn this off with **Start with Windows** in the tray menu.

## Usage

- **Open the flyout:** click the MonitorCenter icon in the notification area.
- **Set brightness:** click inside a display's square. The bottom is 0%, the middle 50%, and the top 100%. Hold and drag up or down to adjust continuously. You can also use the mouse wheel or the arrow keys.
- **Rename a display:** click the **...** button on its square and choose **Rename Display**.
- **Profiles:** choose **New Profile**, enter a name, and choose **Save** to store the current brightness of every display. Click a profile at the top of the flyout to apply it.
- **Close the flyout:** press **Escape**, click elsewhere, or click the tray icon again.
- **Tray menu:** right-click the icon to refresh displays, toggle **Start with Windows**, or exit.

Profiles and display names are saved to `%LOCALAPPDATA%\MonitorCenter\settings.json`.

## Display support

| Display | How brightness is controlled |
| --- | --- |
| External monitors | DDC/CI, using the Windows monitor configuration API, with MCCS VCP code `0x10` as a fallback |
| Built-in laptop screens | Windows WMI brightness control |

Displays that can't be controlled still appear, greyed out, and their tooltip explains why. MonitorCenter never saves settings to a monitor's internal memory.

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

The published app is `dist\MonitorCenter.exe`.

### Hardware tests

Tests that talk to real monitors are skipped by default, so a normal test run never changes your monitor settings. To run them:

```powershell
# Read-only display discovery
$env:MONITORCENTER_HARDWARE_TESTS = '1'

# Also change brightness slightly on each display, then restore it
$env:MONITORCENTER_HARDWARE_WRITE_TESTS = '1'

dotnet test tests\MonitorCenter.Tests\MonitorCenter.Tests.csproj -c Release --filter "FullyQualifiedName~HardwareIntegrationTests"
```

## Contributing

Issues and pull requests are welcome. Please run the tests before opening a pull request.

## License

MonitorCenter is released under the [MIT License](LICENSE).
