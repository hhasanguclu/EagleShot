# Downloads the optional tools bundled with the Windows build:
#   tools\ffmpeg\ffmpeg.exe      - screen / GIF recording   (static GPL build)
#   tools\tesseract\...          - text recognition (UB-Mannheim build, English + Turkish)
# Usage: ./scripts/fetch-windows-tools.ps1 -Destination publish/win-x64/tools
param(
    [Parameter(Mandatory = $true)][string]$Destination
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$work = Join-Path ([IO.Path]::GetTempPath()) ("eagleshot-tools-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$ffmpegDir = Join-Path $Destination "ffmpeg"
$tessDir = Join-Path $Destination "tesseract"
New-Item -ItemType Directory -Force $ffmpegDir, $tessDir | Out-Null

$headers = @{ "User-Agent" = "EagleShot-build" }
if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }

# ── ffmpeg ──────────────────────────────────────────────────────────────
$ffmpegSources = @(
    "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
    "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
)
$zip = Join-Path $work "ffmpeg.zip"
$ok = $false
foreach ($url in $ffmpegSources) {
    try {
        Write-Host "Downloading ffmpeg from $url"
        Invoke-WebRequest $url -OutFile $zip -Headers @{ "User-Agent" = "EagleShot-build" }
        $ok = $true
        break
    } catch {
        Write-Warning "ffmpeg download failed: $_"
    }
}
if (-not $ok) { throw "Could not download ffmpeg." }

$ffx = Join-Path $work "ffmpeg"
Expand-Archive $zip -DestinationPath $ffx
$exe = Get-ChildItem $ffx -Recurse -Filter ffmpeg.exe | Select-Object -First 1
if (-not $exe) { throw "ffmpeg.exe not found in the archive." }
Copy-Item $exe.FullName $ffmpegDir
$license = Get-ChildItem $ffx -Recurse -Include LICENSE, LICENSE.txt, COPYING* | Select-Object -First 1
if ($license) { Copy-Item $license.FullName (Join-Path $ffmpegDir "LICENSE.txt") }
Set-Content (Join-Path $ffmpegDir "README.txt") "FFmpeg (https://ffmpeg.org) is licensed under the GPL. Windows build from $url - source code is available from https://ffmpeg.org/download.html."

# ── Tesseract OCR ───────────────────────────────────────────────────────
$releases = Invoke-RestMethod "https://api.github.com/repos/UB-Mannheim/tesseract/releases?per_page=10" -Headers $headers
$asset = $null
foreach ($r in $releases) {
    $asset = $r.assets | Where-Object { $_.name -like "tesseract-ocr-w64-setup-*.exe" } | Select-Object -First 1
    if ($asset) { break }
}
if (-not $asset) { throw "No Tesseract Windows installer found in UB-Mannheim/tesseract releases." }

$setup = Join-Path $work "tesseract-setup.exe"
Write-Host "Downloading $($asset.browser_download_url)"
Invoke-WebRequest $asset.browser_download_url -OutFile $setup -Headers @{ "User-Agent" = "EagleShot-build" }

# The installer is an NSIS archive: unpack it instead of running it (no admin rights needed)
$tx = Join-Path $work "tesseract"
& 7z x $setup "-o$tx" -y | Out-Null
if ($LASTEXITCODE -ne 0) { throw "7-Zip could not unpack the Tesseract installer." }
Get-ChildItem $tx -Force | Where-Object { $_.Name -like '$*' -or $_.Name -like 'uninstall*' } | Remove-Item -Recurse -Force
if (-not (Test-Path (Join-Path $tx "tesseract.exe"))) {
    # Some versions nest the payload one level deeper
    $nested = Get-ChildItem $tx -Recurse -Filter tesseract.exe | Select-Object -First 1
    if (-not $nested) { throw "tesseract.exe not found in the installer." }
    $tx = $nested.DirectoryName
}
Copy-Item (Join-Path $tx "*") $tessDir -Recurse -Force

$tessdata = Join-Path $tessDir "tessdata"
New-Item -ItemType Directory -Force $tessdata | Out-Null
foreach ($lang in @("eng", "tur", "osd")) {
    $file = Join-Path $tessdata "$lang.traineddata"
    if (-not (Test-Path $file)) {
        Write-Host "Downloading $lang.traineddata"
        Invoke-WebRequest "https://github.com/tesseract-ocr/tessdata_fast/raw/main/$lang.traineddata" -OutFile $file -Headers @{ "User-Agent" = "EagleShot-build" }
    }
}

# ── Smoke tests ─────────────────────────────────────────────────────────
& (Join-Path $ffmpegDir "ffmpeg.exe") -hide_banner -version | Select-Object -First 1
if ($LASTEXITCODE -ne 0) { throw "Bundled ffmpeg does not run." }
$langs = & (Join-Path $tessDir "tesseract.exe") --tessdata-dir $tessdata --list-langs 2>&1
Write-Host ($langs -join " ")
if (-not ($langs -match "^eng$") -or -not ($langs -match "^tur$")) { throw "Bundled Tesseract is missing languages." }

Remove-Item $work -Recurse -Force
Write-Host "Tools ready in $Destination"
