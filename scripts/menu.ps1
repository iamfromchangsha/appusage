<#
    AppUsage 控制台菜单
#>
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-AppUsageRoot
$exe = Join-Path $root 'dist\AppUsage.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path $root 'AppUsage.exe' }

function Show-Menu {
    Clear-Host
    Write-Host ''
    Write-Host '  ====================================================' -ForegroundColor DarkCyan
    Write-Host '     应用使用时长  ·  本机使用追踪与 GitHub 风格热力图' -ForegroundColor Cyan
    Write-Host '  ====================================================' -ForegroundColor DarkCyan
    Write-Host ''
    Write-Host '   [1]  打开主界面（原生窗口，不用浏览器）' -ForegroundColor Gray
    Write-Host '   [2]  后台常驻（只在托盘，不占屏幕）' -ForegroundColor Gray
    Write-Host '   [3]  退出应用' -ForegroundColor Gray
    Write-Host '   [4]  开机自启：开启 / 关闭' -ForegroundColor Gray
    Write-Host '   [5]  查看当前状态' -ForegroundColor Gray
    Write-Host '   [6]  打开数据目录' -ForegroundColor Gray
    Write-Host '   [0]  退出控制台' -ForegroundColor Gray
    Write-Host ''
}

while ($true) {
    Show-Menu
    $choice = Read-Host '   请选择'
    switch ($choice) {
        '1' {
            if (Test-Path $exe) { Start-Process $exe }
            else { Write-Host "   找不到 $exe，请先运行 build.cmd 编译。" -ForegroundColor Red }
        }
        '2' {
            if (Test-Path $exe) {
                Start-Process $exe -ArgumentList '--tray'
                Write-Host '   已在后台运行：托盘图标双击可打开主界面。' -ForegroundColor Green
            }
            else { Write-Host "   找不到 $exe" -ForegroundColor Red }
        }
        '3' {
            Get-Process -Name 'AppUsage' -ErrorAction SilentlyContinue | ForEach-Object {
                Stop-Process -Id $_.Id -Force
            }
            Write-Host '   已退出应用。' -ForegroundColor Green
        }
        '4' { & (Join-Path $PSScriptRoot 'toggle-autostart.ps1') }
        '5' { & (Join-Path $PSScriptRoot 'status.ps1') }
        '6' {
            $dataDir = Join-Path $root 'dist\data'
            if (Test-Path $dataDir) { Invoke-Item $dataDir }
        }
        '0' { Write-Host '   再见。' -ForegroundColor Cyan; return }
        default {
            if ([string]::IsNullOrWhiteSpace($choice)) { continue }
            Write-Host '   无效的选项。' -ForegroundColor Yellow
        }
    }
    Write-Host ''
    [void](Read-Host '   按回车继续')
}
