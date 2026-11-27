$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Installing KuroBeat from $dir ..." -ForegroundColor Cyan

& (Join-Path $dir 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $dir 'KuroBeat.exe'
$startDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Kuro'
New-Item -ItemType Directory -Path $startDir -Force | Out-Null
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut((Join-Path $startDir 'KuroBeat.lnk'))
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dir
$sc.IconLocation = "$exe,0"
$sc.Description = 'KuroBeat - 60 FPS Monitor Music Visualizer & YouTube Music Phone Remote'
$sc.Save()

Write-Host ""
Write-Host "KuroBeat installed!" -ForegroundColor Green
Write-Host "  - Exe:            $exe"
Write-Host "  - Extension path: $(Join-Path $dir 'extension')  (load unpacked once in Chrome/Edge -> Developer mode)"
Write-Host "  - Phone URL:      http://kurobeat.local:8765  (or press R in KuroBeat for QR code)"
Write-Host ""
Write-Host "Note: If your phone cannot connect over Wi-Fi, allow KuroBeat through Windows Firewall on Private networks." -ForegroundColor Yellow