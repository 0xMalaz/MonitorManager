# MonitorCenter

MonitorCenter is a tray-only Windows 11 app for controlling the real hardware brightness of connected displays. It has no main window and does not appear in the taskbar or Alt+Tab.

## Use

1. Run `dist\MonitorCenter.exe`.
2. Click the blue monitor icon in the taskbar notification area.
3. Displays appear as square brightness controls, with up to three controls per row.
4. Click anywhere inside a display's square. The bottom is 0%, the middle is 50%, and the top is 100%.
5. Hold the mouse button and drag vertically to adjust continuously. The shown percentage is read back from the monitor after release.

Use the **...** button at the top right of a display and choose **Rename Display** to give it a custom name. The name is retained across display refreshes and app restarts.

Press **Escape**, click elsewhere, or click the tray icon again to hide the flyout. Right-click the tray icon to refresh displays, control Windows startup, or exit MonitorCenter.

## Profiles

You can keep up to three brightness profiles. Each profile records the current brightness of every controllable display.

- Click a profile at the top of the tray flyout to apply it.
- Choose **New Profile** in the Profiles section, enter a name, and choose **Save** to capture the current brightness setup.
- The **New Profile** button becomes unavailable after three profiles have been saved.

Profiles and custom display names are stored as versioned JSON under `%LOCALAPPDATA%\MonitorCenter\settings.json`. When upgrading from an older MonitorCenter build, the first three existing profiles and their brightness values are retained; settings for the removed main-window features are discarded.

The first normal Release launch enables startup for the current Windows user. Startup launches use the internal `--startup` argument and do not open the flyout.

## Display support

- External monitors use Windows' DDC/CI monitor APIs. The app first tries the high-level brightness API and then MCCS VCP code `0x10`.
- Built-in laptop panels use Windows WMI brightness control.
- Unsupported displays remain visible as disabled squares with a DDC/CI diagnostic in their tooltip. No software dimming overlay is used.
- Monitor settings are never saved to display NVRAM.

If a display is unavailable, enable DDC/CI in its on-screen menu when the option exists, then try another DisplayPort/HDMI cable or GPU port and choose **Refresh displays**.

## Restore after reinstalling Windows

The project is backed up in the private GitHub repository [0xMalaz/MonitorManager](https://github.com/0xMalaz/MonitorManager). Sign in with the GitHub account that owns the repository to access it.

For a ready-to-run copy, download `MonitorCenter-Windows-x64.zip` from the [PC backup release](https://github.com/0xMalaz/MonitorManager/releases/tag/pc-backup-2026-10-01) and extract it into a permanent folder, for example `%LOCALAPPDATA%\Programs\MonitorCenter`. The executable is self-contained; running it does not require Git or the .NET SDK.

Personal settings are not uploaded to GitHub or included in the release archive. Before formatting your PC, copy `%LOCALAPPDATA%\MonitorCenter\settings.json` to a USB drive or another backup location if you want to keep your profiles and custom display names. After reinstalling Windows, restore that file to the same path before starting the app, creating the destination directory if needed. Monitor identities may change after reinstalling Windows or changing display connections; check any restored profiles against the connected displays.

Run `MonitorCenter.exe` from the permanent folder. Its first normal Release launch registers that location for Windows startup; you can change this through the tray menu.

To restore the source for development, install Git and the .NET 10 SDK, then run:

```powershell
git clone https://github.com/0xMalaz/MonitorManager.git
cd MonitorManager
dotnet publish src\MonitorCenter\MonitorCenter.csproj -c Release -r win-x64 --self-contained true -o dist
.\dist\MonitorCenter.exe
```

Generated files (`dist`, `bin`, `obj`, and `.artifacts`) are intentionally excluded from Git. The release archive preserves the executable separately from the source; personal settings require your own backup.

## Build and test

Requirements: Windows 11 x64 and the .NET 10 SDK.

```powershell
dotnet build MonitorCenter.sln -c Release
dotnet test MonitorCenter.sln -c Release --no-build
dotnet publish src\MonitorCenter\MonitorCenter.csproj -c Release -r win-x64 --self-contained true -o dist
```

Hardware discovery and brightness write/restore tests are opt-in so ordinary test runs never change monitor settings:

```powershell
$env:MONITORCENTER_HARDWARE_TESTS = '1'
dotnet test tests\MonitorCenter.Tests\MonitorCenter.Tests.csproj -c Release --filter "FullyQualifiedName~HardwareMonitorServiceTests"

$env:MONITORCENTER_HARDWARE_WRITE_TESTS = '1'
dotnet test tests\MonitorCenter.Tests\MonitorCenter.Tests.csproj -c Release --filter "FullyQualifiedName~HardwareMonitorServiceTests"
```
