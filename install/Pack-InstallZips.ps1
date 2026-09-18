# Builds the two install zips under install/packages/.
# Run from the repo after: dotnet build -c Release

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot 'packages'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Pack([string]$tfm, [string]$zipName) {
    $src = Join-Path $repo "src\OmniClass.Revit\bin\$Configuration\$tfm"
    $dll = Join-Path $src 'OmniClass.Revit.dll'
    if (-not (Test-Path $dll)) {
        throw "Build output not found: $dll`nRun: dotnet build -c $Configuration"
    }

    $stage = Join-Path $outDir ('_stage_' + $tfm)
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $bundle = Join-Path $stage 'OmniClassRooms'
    New-Item -ItemType Directory -Force -Path (Join-Path $bundle 'data') | Out-Null

    Copy-Item (Join-Path $PSScriptRoot 'OmniClass.Rooms.addin') $stage
    Copy-Item (Join-Path $src 'OmniClass.Revit.dll') $bundle
    Copy-Item (Join-Path $src 'OmniClass.Core.dll') $bundle
    Copy-Item (Join-Path $src 'OmniClass.Rooms.config') $bundle
    Copy-Item (Join-Path $src 'data\room_aliases.csv') (Join-Path $bundle 'data')
    Copy-Item (Join-Path $outDir 'HOW-TO-INSTALL.txt') $stage -ErrorAction SilentlyContinue

    $zip = Join-Path $outDir $zipName
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Remove-Item $stage -Recurse -Force
    Write-Host "Wrote $zip"
}

Pack 'net48' 'OmniClassRooms-Revit2023-2024.zip'
Pack 'net8.0-windows' 'OmniClassRooms-Revit2025-2026.zip'
