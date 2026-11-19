# Windows smoke test: start EagleShot in the tray, take a full-screen capture through a second
# instance, check the PNG and the history entry, then quit it with --quit.
param([string]$Exe = "target/release/eagleshot.exe")
$ErrorActionPreference = "Stop"
$work = Join-Path $env:RUNNER_TEMP "eagleshot-smoke"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$work/config", "$work/shots" | Out-Null
$env:EAGLESHOT_CONFIG_DIR = "$work/config"
$env:EAGLESHOT_PORT = "47556"
$shots = (Resolve-Path "$work/shots").Path -replace '\\', '\\'
Set-Content "$work/config/settings.json" "{ `"SaveFolder`": `"$shots`", `"ShowSplash`": false, `"TakeOverPrintScreen`": false, `"AfterCapture`": { `"CopyToClipboard`": true, `"SaveToFile`": true, `"ShowNotification`": true } }"

& $Exe --version | Out-Host
$p = Start-Process $Exe -ArgumentList "--minimized" -PassThru -RedirectStandardError "$work/stderr.txt" -RedirectStandardOutput "$work/stdout.txt"
Start-Sleep -Seconds 6
if ($p.HasExited) {
  Get-Content "$work/stderr.txt", "$work/stdout.txt" -ErrorAction SilentlyContinue | Out-Host
  throw "EagleShot exited early (code $($p.ExitCode))"
}

Start-Process $Exe -ArgumentList "--fullscreen" -Wait
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
  $png = Get-ChildItem "$work/shots" -Filter *.png -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($png -and (Test-Path "$work/config/history.json")) { $ok = $true; break }
  Start-Sleep -Milliseconds 500
}
if (-not $ok) {
  Get-Content "$work/stderr.txt" -ErrorAction SilentlyContinue | Out-Host
  Stop-Process -Id $p.Id -Force; throw "no screenshot was saved"
}
Write-Host "saved $($png.FullName) ($($png.Length) bytes)"
if (-not (Select-String -Path "$work/config/history.json" -Pattern '"Kind":"Full screen"' -Quiet)) { throw "history entry missing" }

Start-Process $Exe -ArgumentList "--quit" -Wait
if (-not $p.WaitForExit(15000)) { Stop-Process -Id $p.Id -Force; throw "EagleShot did not quit" }
Write-Host "smoke test passed"
