$ErrorActionPreference = 'Stop'
$fx = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = "$fx\csc.exe"
$refs = @(
  '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll',
  "/r:$fx\System.Runtime.dll", "/r:$fx\System.Runtime.WindowsRuntime.dll", "/r:$fx\System.Runtime.InteropServices.WindowsRuntime.dll",
  '/r:C:\Windows\System32\WinMetadata\Windows.Foundation.winmd',
  '/r:C:\Windows\System32\WinMetadata\Windows.Media.winmd',
  '/r:C:\Windows\System32\WinMetadata\Windows.Storage.winmd'
)
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
& $csc /nologo /platform:x64 /optimize+ /target:winexe /win32icon:"$dir\kurobeat.ico" /out:"$dir\KuroBeat.exe" @refs "$dir\KuroBeat.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built $dir\KuroBeat.exe (with built-in YTM Phone Remote + kurobeat.local mDNS)" -ForegroundColor Cyan