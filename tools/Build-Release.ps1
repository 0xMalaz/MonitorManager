<#
.SYNOPSIS
Publishes MonitorCenter and packages it with Velopack into an installer and update packages.

.EXAMPLE
./tools/Build-Release.ps1 -Version 1.1.0
#>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $OutputDir = '.artifacts/releases'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root '.artifacts/publish'

Push-Location $root
try {
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    dotnet publish src/MonitorCenter/MonitorCenter.csproj -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

    # The pack id decides the install folder (%LocalAppData%\0xMalaz.MonitorCenter) and must never change,
    # or installed copies stop receiving updates. It is kept apart from the settings folder on purpose.
    dotnet vpk pack `
        --packId 0xMalaz.MonitorCenter `
        --packVersion $Version `
        --packTitle MonitorCenter `
        --packAuthors 0xMalaz `
        --packDir $publishDir `
        --mainExe MonitorCenter.exe `
        --icon src/MonitorCenter/Assets/MonitorCenter.ico `
        --shortcuts StartMenuRoot `
        --outputDir $OutputDir
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }
}
finally {
    Pop-Location
}
