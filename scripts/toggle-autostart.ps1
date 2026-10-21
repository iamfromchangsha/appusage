<#
    切换 EXE 的开机自启（启动文件夹快捷方式）
#>
$ErrorActionPreference = 'Stop'

$exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\AppUsage.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'AppUsage.exe' }
if (-not (Test-Path $exe)) {
    Write-Host '[x] 找不到 AppUsage.exe' -ForegroundColor Red
    exit 1
}

$startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$lnk = Join-Path $startup '应用使用时长.lnk'

if (Test-Path $lnk) {
    Remove-Item $lnk -Force
    Write-Host '[+] 已关闭开机自启' -ForegroundColor Green
    exit 0
}

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($lnk)
$link.TargetPath = $exe
$link.Arguments = '--tray'
$link.WorkingDirectory = Split-Path -Parent $exe
$link.WindowStyle = 7
$link.Description = '应用使用时长 · 本机使用追踪'
$link.IconLocation = "$exe,0"
$link.Save()

Write-Host '[+] 已开启开机自启（登录后静默进入托盘）' -ForegroundColor Green
Write-Host "[i] $lnk"
