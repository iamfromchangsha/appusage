<#
    取消开机自启（计划任务 + 启动文件夹两种都清）
#>
$ErrorActionPreference = 'Stop'
$taskName = 'AppUsageTracker'

& schtasks /Delete /TN $taskName /F 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "[+] 已删除计划任务 $taskName" -ForegroundColor Green
} else {
    Write-Host '[=] 没有找到计划任务（可能本来就没用这种方式）' -ForegroundColor Yellow
}

$startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$launcher = Join-Path $startup 'AppUsageTracker.cmd'
if (Test-Path $launcher) {
    try {
        Remove-Item $launcher -Force -ErrorAction Stop
        Write-Host '[+] 已移除启动文件夹里的启动项' -ForegroundColor Green
    } catch {
        Write-Host "[!] 启动项删除失败，请手动删除：$launcher" -ForegroundColor Yellow
    }
} else {
    Write-Host '[=] 启动文件夹里没有启动项' -ForegroundColor Yellow
}

Write-Host '[i] 开机自启已关闭。' -ForegroundColor Cyan
