<#
    AppUsage 控制台菜单
#>
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-AppUsageRoot

function Show-Menu {
    Clear-Host
    Write-Host ''
    Write-Host '  ====================================================' -ForegroundColor DarkCyan
    Write-Host '     应用使用时长  ·  本地追踪与 GitHub 风格热力图' -ForegroundColor Cyan
    Write-Host '  ====================================================' -ForegroundColor DarkCyan
    Write-Host ''
    Write-Host '   [1]  启动追踪器（后台常驻，开始累计数据）' -ForegroundColor Gray
    Write-Host '   [2]  打开仪表盘（浏览器）' -ForegroundColor Gray
    Write-Host '   [3]  停止追踪器' -ForegroundColor Gray
    Write-Host '   [4]  查看当前状态' -ForegroundColor Gray
    Write-Host '   [5]  开启开机自启' -ForegroundColor Gray
    Write-Host '   [6]  关闭开机自启' -ForegroundColor Gray
    Write-Host '   [7]  生成演示数据（预览热力图效果，不影响真实数据）' -ForegroundColor Gray
    Write-Host '   [0]  退出' -ForegroundColor Gray
    Write-Host ''
}

while ($true) {
    Show-Menu
    $choice = Read-Host '   请选择'
    switch ($choice) {
        '1' { & (Join-Path $PSScriptRoot 'start-tracker.ps1') }
        '2' { & (Join-Path $PSScriptRoot 'start-dashboard.ps1') -Background }
        '3' { & (Join-Path $PSScriptRoot 'stop-tracker.ps1') }
        '4' { & (Join-Path $PSScriptRoot 'status.ps1') }
        '5' { & (Join-Path $PSScriptRoot 'install-startup.ps1') }
        '6' { & (Join-Path $PSScriptRoot 'uninstall-startup.ps1') }
        '7' { & (Join-Path $PSScriptRoot 'seed-demo.ps1') }
        '0' { Write-Host '   再见。' -ForegroundColor Cyan; return }
        default {
            if ([string]::IsNullOrWhiteSpace($choice)) { continue }
            Write-Host '   无效的选项。' -ForegroundColor Yellow
        }
    }
    Write-Host ''
    [void](Read-Host '   按回车继续')
}
