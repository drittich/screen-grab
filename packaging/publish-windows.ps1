# Publish a self-contained Windows build of ScreenGrab.
#
# Usage:   packaging\publish-windows.ps1 [-Version 1.0.0]
# Output:  packaging\dist\win-x64\screengrab.exe (self-contained, single folder)

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProj  = Join-Path $repoRoot "ScreenGrab.App\ScreenGrab.App.csproj"
$outDir   = Join-Path $PSScriptRoot "dist\win-x64"

Write-Host ">> Publishing self-contained win-x64 build (v$Version)"
dotnet publish $appProj `
    -c Release `
    -r win-x64 `
    -f net10.0-windows `
    --self-contained true `
    -p:Version=$Version `
    -o $outDir

Write-Host ""
Write-Host ">> Done. Output in $outDir"
Write-Host ">> Run screengrab.exe to start the tray app (Ctrl-Alt-F12 to capture)."
