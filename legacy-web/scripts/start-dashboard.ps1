<#
    启动本地仪表盘（默认 http://127.0.0.1:8787/）
    -Demo  使用演示数据库 data\demo.db
#>
[CmdletBinding()]
param(
    [int]$Port = 8787,
    [switch]$Demo,
    [switch]$NoOpen,
    [switch]$Background
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-AppUsageRoot
$py = Get-AppUsagePython
if (-not $py) {
    Write-Host '[x] 找不到 Python 3.10+' -ForegroundColor Red
    exit 1
}

$server = Join-Path $root 'server.py'
$db = Join-Path $root 'data\usage.db'
if ($Demo) { $db = Join-Path $root 'data/demo.db' }

if (-not (Test-Path $db)) {
    Write-Host "[!] 数据库不存在：$db" -ForegroundColor Yellow
    if ($Demo) {
        Write-Host '[i] 正在生成演示数据…' -ForegroundColor Cyan
        & $py.Path @($py.Prefix + @((Join-Path $root 'seed_demo.py')))
    } else {
        Write-Host '[i] 请先运行 start-tracker.ps1 开始采集，或用 -Demo 预览演示数据' -ForegroundColor Yellow
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $db) | Out-Null
    }
}

$argList = @($server, '--db', $db, '--port', "$Port")
if (-not $NoOpen) { $argList += '--open' }
$url = "http://127.0.0.1:$Port/"

if ($Background) {
    $already = Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*server.py*$Port*" }
    if ($already) {
        Write-Host "[=] 端口 $Port 上已有服务在运行" -ForegroundColor Yellow
        if (-not $NoOpen) { Start-Process $url }
        exit 0
    }
    $full = @()
    $full += $py.Prefix
    foreach ($a in $argList) { $full += (Quote-Arg $a) }
    Start-Process -FilePath $py.Path -ArgumentList $full -WorkingDirectory $root -WindowStyle Hidden | Out-Null
    Start-Sleep -Milliseconds 900
    Write-Host "[+] 仪表盘已在后台启动 → $url" -ForegroundColor Green
    if (-not $NoOpen) { Start-Process $url }
    exit 0
}

Write-Host "[i] 仪表盘: http://127.0.0.1:$Port/" -ForegroundColor Cyan
Write-Host '[i] 按 Ctrl+C 停止服务' -ForegroundColor DarkGray
& $py.Path @($py.Prefix + $argList)
