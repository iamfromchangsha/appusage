<#
    停止后台追踪器
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-AppUsageRoot
$pidFile = Join-Path $root 'data\tracker.pid'

$pidValue = Get-AppUsageTrackerPid
if (-not $pidValue) {
    Write-Host '[=] 追踪器没有在运行' -ForegroundColor Yellow
    if (Test-Path $pidFile) { Remove-Item $pidFile -Force -ErrorAction SilentlyContinue }
    exit 0
}

try {
    Stop-Process -Id $pidValue -Force
    Start-Sleep -Milliseconds 400
    Write-Host "[+] 追踪器已停止 (PID $pidValue)" -ForegroundColor Green
} catch {
    Write-Host "[!] 停止失败: $_" -ForegroundColor Red
    exit 1
}
if (Test-Path $pidFile) { Remove-Item $pidFile -Force -ErrorAction SilentlyContinue }
