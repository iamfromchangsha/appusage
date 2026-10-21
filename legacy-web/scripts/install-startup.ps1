<#
    注册开机自启（登录时自动启动追踪器）

    优先用计划任务；没有管理员权限时自动回退到「启动」文件夹方式，
    两种方式都不需要你改任何系统设置。
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-AppUsageRoot
$py = Get-AppUsagePython
$taskName = 'AppUsageTracker'
$runner = Join-Path $PSScriptRoot 'start-tracker.ps1'

if (-not $py) {
    Write-Host '[x] 找不到 Python 3.10+' -ForegroundColor Red
    exit 1
}

# ---------- 方式一：计划任务 ----------
Write-Host '[i] 尝试注册计划任务（需要管理员权限时可用）...'
$tr = '"' + (Join-Path $PSScriptRoot 'run-tracker.cmd') + '"'
$taskOk = $false
try {
    $ErrorActionPreference = 'Continue'
    $null = & schtasks /Create /TN $taskName /SC ONLOGON /TR $tr /F 2>&1
    $taskOk = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = 'Stop'
} catch {
    $taskOk = $false
}
if ($taskOk) {
    Write-Host "[+] 开机自启已开启：计划任务 $taskName" -ForegroundColor Green
    Write-Host '[i] 取消：运行 uninstall-startup.ps1' -ForegroundColor DarkGray
    exit 0
}

# ---------- 方式二：启动文件夹（无需管理员） ----------
$startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$launcher = Join-Path $startup 'AppUsageTracker.cmd'

try {
    if (-not (Test-Path $startup)) { New-Item -ItemType Directory -Force -Path $startup | Out-Null }
    $body = @(
        '@echo off',
        'rem Installed by AppUsage - auto start the local usage tracker at logon',
        ('powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f $runner),
        ''
    ) -join "`r`n"
    Set-Content -Path $launcher -Value $body -Encoding ASCII
    Write-Host "[+] 开机自启已开启（启动文件夹方式）" -ForegroundColor Green
    Write-Host "[i] 启动项：$launcher"
    Write-Host '[i] 取消：运行 uninstall-startup.ps1' -ForegroundColor DarkGray
    exit 0
} catch {
    Write-Host "[!] 自动设置失败：$_" -ForegroundColor Red
    Write-Host '[i] 你可以手动把下面这个文件放进「启动」文件夹：' -ForegroundColor Yellow
    Write-Host "    $runner"
    exit 1
}
