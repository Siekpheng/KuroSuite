[CmdletBinding()]
param(
    [switch]$SkipAutostart,
    [switch]$OnlyTask,
    [switch]$CleanupLegacy
)

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $dir 'trofeo_lcd.exe'

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Register-TrofeoTask {
    param([string]$TargetExe, [string]$WorkDir)
    $taskName = 'TrofeoLCD'
    $action = New-ScheduledTaskAction -Execute $TargetExe -Argument '--hide-console' -WorkingDirectory $WorkDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $trigger.Delay = 'PT8S'
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries -DontStopIfOnBatteries -StartWhenAvailable `
        -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero)
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest

    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
        -Settings $settings -Principal $principal `
        -Description 'Starts KuroHUD (trofeo_lcd.exe) elevated at logon.' | Out-Null
    Write-Host "  - Autostart task '$taskName' -> $TargetExe --hide-console (Elevated, at logon)" -ForegroundColor Green

    if ($CleanupLegacy) {
        Get-Process -Name 'trofeo_lcd', 'trofeo_lcd_core', 'lovingcool_lcd' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        $roots = @((Join-Path $env:USERPROFILE 'Documents'), ([Environment]::GetFolderPath('MyDocuments'))) | Select-Object -Unique
        foreach ($r in $roots) {
            foreach ($old in @('ThermalRight Screen', 'Lovingcool Screen', 'KuroBeat', 'ytm-remote')) {
                $p = Join-Path $r $old
                if (Test-Path -LiteralPath $p) {
                    Remove-Item -Recurse -Force -LiteralPath $p
                    Write-Host "  - Removed legacy folder $p" -ForegroundColor Yellow
                }
            }
        }
        Start-ScheduledTask -TaskName $taskName
        Write-Host "  - Started KuroHUD from $TargetExe" -ForegroundColor Green
    }
}

if ($OnlyTask) {
    if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe" }
    Register-TrofeoTask -TargetExe $exe -WorkDir $dir
    exit 0
}

Write-Host "Installing KuroHUD from $dir ..." -ForegroundColor Cyan
& (Join-Path $dir 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$docRoots = @(
    (Join-Path $env:USERPROFILE 'Documents'),
    ([Environment]::GetFolderPath('MyDocuments'))
) | Select-Object -Unique

foreach ($docs in $docRoots) {
    $legacyTrofeo = Join-Path $docs 'ThermalRight Screen'
    $legacyLoving = Join-Path $docs 'Lovingcool Screen'
    if (Test-Path -LiteralPath $legacyTrofeo) {
        foreach ($f in @('trofeo_lcd_core.exe', 'trofeo_config.exe', 'trofeo.conf')) {
            $src = Join-Path $legacyTrofeo $f
            $dst = Join-Path $dir $f
            if ((Test-Path -LiteralPath $src) -and -not (Test-Path -LiteralPath $dst)) {
                Copy-Item -Force $src $dst
                Write-Host "  - Imported $f from $legacyTrofeo"
            }
        }
        $srcWp = Join-Path $legacyTrofeo 'profiles\wallpaper.profile'
        $dstWp = Join-Path $dir 'profiles\wallpaper.profile'
        if ((Test-Path -LiteralPath $srcWp) -and -not (Test-Path -LiteralPath $dstWp)) {
            Copy-Item -Force $srcWp $dstWp
        }
    }
    if (Test-Path -LiteralPath $legacyLoving) {
        foreach ($f in @('AMDFamily17.bin', 'lovingcool.conf')) {
            $src = Join-Path $legacyLoving $f
            $dst = Join-Path $dir $f
            if ((Test-Path -LiteralPath $src) -and -not (Test-Path -LiteralPath $dst)) {
                Copy-Item -Force $src $dst
                Write-Host "  - Imported $f from $legacyLoving"
            }
        }
    }
}

$startDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Kuro'
New-Item -ItemType Directory -Path $startDir -Force | Out-Null
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut((Join-Path $startDir 'KuroHUD.lnk'))
$sc.TargetPath = $exe
$sc.Arguments = '--ui'
$sc.WorkingDirectory = $dir
$sc.IconLocation = "$exe,0"
$sc.Description = 'KuroHUD - Dual Case LCD & GPU RGB Control Center'
$sc.Save()

Write-Host ""
Write-Host "KuroHUD installed!" -ForegroundColor Green
Write-Host "  - Exe:            $exe"
Write-Host "  - Shortcut:       $(Join-Path $startDir 'KuroHUD.lnk')"

if (-not $SkipAutostart) {
    if (Test-IsAdmin) {
        Register-TrofeoTask -TargetExe $exe -WorkDir $dir
    } else {
        Write-Host "  - Requesting UAC elevation to register elevated startup task 'TrofeoLCD'..." -ForegroundColor Yellow
        $extra = if ($CleanupLegacy) { ' -CleanupLegacy' } else { '' }
        $psArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`" -OnlyTask$extra"
        try {
            $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $psArgs -Verb RunAs -Wait -PassThru
            if ($p.ExitCode -eq 0) {
                Write-Host "  - Autostart task 'TrofeoLCD' registered!" -ForegroundColor Green
            } else {
                Write-Warning "Autostart task registration returned exit code $($p.ExitCode)."
            }
        } catch {
            Write-Warning "Skipped elevated startup task registration (UAC declined or unavailable). Run install.ps1 as Administrator later to set TrofeoLCD autostart."
        }
    }
}
