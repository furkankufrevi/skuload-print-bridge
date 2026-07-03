# Build the win-x64 self-contained app FOLDER on Windows, and optionally pack a
# Velopack release (Setup.exe + nupkg) locally.
#
#   .\build.ps1               # publish only  -> publish\
#   .\build.ps1 -Version 1.2.3  # publish + vpk pack -> releases\
#
# NOTE: releases are normally cut by CI (see .github\workflows\release.yml).
param(
  [string]$Version = ""
)
$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

Write-Host "==> publish (self-contained win-x64 folder)"
if (Test-Path publish) { Remove-Item -Recurse -Force publish }
$verArg = @()
if ($Version) { $verArg = @("-p:Version=$Version") }
dotnet publish .\PrintBridge.csproj -c Release -r win-x64 --self-contained @verArg -o publish
Write-Host "Published: $PSScriptRoot\publish\skuload-print-bridge.exe"

if ($Version) {
  Write-Host "==> vpk pack $Version"
  dotnet tool install -g vpk --version 1.2.0 2>$null | Out-Null
  vpk pack `
    --packId skuload-print-bridge `
    --packVersion $Version `
    --packDir publish `
    --mainExe skuload-print-bridge.exe `
    --packTitle "skuload print bridge" `
    --packAuthors "skuload" `
    --icon assets\app.ico `
    --runtime win-x64 `
    --outputDir releases
  Write-Host "Packed into: $PSScriptRoot\releases"
}
