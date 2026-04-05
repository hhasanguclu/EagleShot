#!/usr/bin/env pwsh
# EagleShot v2.0.0 - Cross-platform build script
# Can be run from anywhere: ./scripts/build-all.ps1

$ErrorActionPreference = "Stop"
$Version = "2.0.0"

# Always resolve paths relative to project root (one level up from this script)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$CsprojPath = Join-Path $ProjectRoot "EagleShot.csproj"
$OutBase = Join-Path $ProjectRoot "publish"

Write-Host "=== EagleShot v$Version - Building all platforms ===" -ForegroundColor Cyan
Write-Host "Project root: $ProjectRoot"

if (Test-Path $OutBase) { Remove-Item -Recurse -Force $OutBase }

$rids = @("win-x64", "win-arm64", "linux-x64", "osx-x64", "osx-arm64")
$i = 1

foreach ($rid in $rids) {
    $outPath = Join-Path $OutBase $rid
    Write-Host "`n[$i/$($rids.Count)] Publishing $rid..." -ForegroundColor Yellow
    dotnet publish $CsprojPath -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -o $outPath
    if ($LASTEXITCODE -ne 0) { throw "Failed to publish $rid" }
    $i++
}

Write-Host "`n=== All platforms published to $OutBase ===" -ForegroundColor Green

# Create release zips
$distDir = Join-Path $ProjectRoot "dist"
if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Path $distDir | Out-Null

Write-Host "`nCreating release archives..." -ForegroundColor Yellow
foreach ($rid in $rids) {
    $srcPath = Join-Path $OutBase $rid
    $zipName = "EagleShot_v${Version}_${rid}.zip"
    $zipPath = Join-Path $distDir $zipName
    Compress-Archive -Path "$srcPath\*" -DestinationPath $zipPath -Force
    $size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  $zipName ($size MB)"
}

Write-Host "`n=== Release files ready in $distDir ===" -ForegroundColor Green
Write-Host ""
Write-Host "Upload these to GitHub Releases:"
Get-ChildItem $distDir -Filter "*.zip" | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "Windows installer: Inno Setup ile installers/windows/setup.iss derle"
