<#
    启动后台追踪器（默认隐藏窗口运行，可加 -Foreground 前台运行）
#>
[CmdletBinding()]
param(
    [switch]$Foreground
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-AppUsageRoot
$py = Get-AppUsagePython
if (-not $py) {
    Write-Host '[x] 找不到 Python 3.10+，请先安装：https://www.python.org/downloads/' -ForegroundColor Red
    exit 1
}

$tracker = Join-Path $root 'tracker.py'
$cfg = Join-Path $root 'config.json'
$db = Join-Path $root 'data\usage.db'

$existing = Get-AppUsageTrackerPid
if ($existing -and -not $Foreground) {
    Write-Host "[=] 追踪器已在运行 (PID $existing)，数据库 $db" -ForegroundColor Yellow
    exit 0
}

Write-Host "[i] Python : $($py.Path) ($($py.Version))"
Write-Host "[i] 数据库 : $db"

if ($Foreground) {
    Write-Host '[i] 前台运行，Ctrl+C 退出' -ForegroundColor Cyan
    & $py.Path @($py.Prefix + @($tracker, '--config', $cfg))
    exit $LASTEXITCODE
}

$argList = @()
$argList += $py.Prefix
$argList += (Quote-Arg $tracker)
$argList += '--config'
$argList += (Quote-Arg $cfg)

Start-Process -FilePath $py.Path -ArgumentList $argList -WorkingDirectory $root -WindowStyle Hidden | Out-Null
Start-Sleep -Milliseconds 900
$pid2 = Get-AppUsageTrackerPid
if ($pid2) {
    Write-Host "[+] 追踪器已启动 (PID $pid2)" -ForegroundColor Green
} else {
    Write-Host '[!] 启动后未检测到进程，请检查 data\tracker.pid 或手动前台运行排查' -ForegroundColor Yellow
}
