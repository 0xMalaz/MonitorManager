<p align="center">
  <img src="docs/icon.png" width="128" height="128" alt="MonitorCenter icon">
</p>

<h1 align="center">MonitorCenter</h1>

<p align="center">Set the real brightness of all your monitors from the Windows 11 tray.</p>

<p align="center">
  <a href="https://github.com/0xMalaz/MonitorManager/releases/latest/download/MonitorCenter-Setup.exe"><b>Download MonitorCenter-Setup.exe</b></a>
</p>

## Features

- One slider per display, all in a single tray flyout.
- Changes the monitor's own backlight over DDC/CI instead of dimming the picture with an overlay.
- Works with laptop screens too.
- Up to three brightness profiles, one click to apply.
- Rename displays so you can tell them apart.
- Follows the Windows light and dark theme.
- Shown in Turkish when Windows is set to Turkish, otherwise in English.

## Install

Run `MonitorCenter-Setup.exe`. It installs for your user only, without admin rights, and starts with Windows. You can turn that off in the tray menu.

The installer isn't code-signed, so SmartScreen may warn you. Choose **More info**, then **Run anyway**.

MonitorCenter checks for a new version every 8 hours and when you open its tray menu (at most once every 30 minutes). When one is available, the tray menu shows **Install update** instead of **Check for updates**. MonitorCenter only downloads and installs an update when you choose it.

To uninstall, go to **Settings > Apps > Installed apps**. Your profiles and display names are kept in `%LOCALAPPDATA%\MonitorCenter\settings.json`.

If you still use 1.0.0 (the zip version), exit it and run the installer. Your settings carry over.

## Usage

- Click the tray icon to open the flyout.
- Click or drag on a display's screen to set brightness. Bottom is 0%, top is 100%. The mouse wheel and arrow keys work too.
- Use the **⋯** button next to a display's name to rename it.
- **New Profile** saves the current brightness of every display. Click a profile to apply it; its bars show the brightness it sets on each display.
- Right-click the tray icon to check for updates, toggle **Start with Windows**, or exit.

## A display shows as unavailable

External monitors are controlled over DDC/CI and laptop screens through Windows. If a display is greyed out:

1. Turn on **DDC/CI** in the monitor's on-screen menu.
2. Try another cable or port.
3. Click the refresh button at the top of the flyout.

Some docks and adapters don't pass DDC/CI through at all.

## Build from source

You need Windows 11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build MonitorCenter.sln -c Release
dotnet test MonitorCenter.sln -c Release --no-build
dotnet publish src\MonitorCenter\MonitorCenter.csproj -c Release -r win-x64 --self-contained true -o dist
```

Tests that touch real monitors are skipped unless you set `MONITORCENTER_HARDWARE_TESTS=1`. Setting `MONITORCENTER_HARDWARE_WRITE_TESTS=1` as well lets them change brightness slightly and then restore it.

## Releasing

Raise `<Version>` in `src/MonitorCenter/MonitorCenter.csproj`, merge it to `main`, then tag that commit:

```powershell
git tag v1.6.0
git push origin v1.6.0
```

The [release workflow](.github/workflows/release.yml) runs the tests and publishes the installer and update packages with [Velopack](https://velopack.io).

## License

[MIT](LICENSE)
