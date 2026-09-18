# Installs the OmniClass Room Data panel onto Autodesk Revit's Arch Tools tab.
# Run from a Developer PowerShell after building, or pass -Configuration Debug.
#
#   .\install\Install-OmniClassRooms.ps1
#   .\install\Install-OmniClassRooms.ps1 -RevitYear 2024
#   .\install\Install-OmniClassRooms.ps1 -Uninstall

[CmdletBinding()]
param(
    [ValidateSet('2023', '2024', '2025', '2026')]
    [string[]]$RevitYear = @('2023', '2024', '2025', '2026'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-TargetFramework([string]$year) {
    if ($year -in @('2023', '2024')) { return 'net48' }
    return 'net8.0-windows'
}

foreach ($year in $RevitYear) {
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    $addinFile = Join-Path $addins 'OmniClass.Rooms.addin'
    $bundle = Join-Path $addins 'OmniClassRooms'

    if ($Uninstall) {
        if (Test-Path $addinFile) { Remove-Item $addinFile -Force }
        if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
        Write-Host "Removed OmniClass Rooms from Revit $year."
        continue
    }

    $tfm = Get-TargetFramework $year
    $output = Join-Path $repoRoot "src\OmniClass.Revit\bin\$Configuration\$tfm"
    $dll = Join-Path $output 'OmniClass.Revit.dll'
    if (-not (Test-Path $dll)) {
        throw "Build output not found: $dll`nRun: dotnet build -c $Configuration"
    }

    New-Item -ItemType Directory -Force -Path $addins | Out-Null
    New-Item -ItemType Directory -Force -Path $bundle | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundle 'data') | Out-Null

    Copy-Item (Join-Path $output 'OmniClass.Revit.dll') $bundle -Force
    Copy-Item (Join-Path $output 'OmniClass.Core.dll') $bundle -Force
    Copy-Item (Join-Path $output 'OmniClass.Rooms.config') $bundle -Force
    Copy-Item (Join-Path $output 'data\room_aliases.csv') (Join-Path $bundle 'data') -Force
    Copy-Item (Join-Path $PSScriptRoot 'OmniClass.Rooms.addin') $addinFile -Force

    Write-Host "Installed OmniClass Rooms for Revit $year ($tfm)."
    Write-Host "  Tab: Arch Tools    Panel: Room Data"
}

if (-not $Uninstall) {
    Write-Host ""
    Write-Host "Restart Revit. Look for Arch Tools > Room Data."
}
