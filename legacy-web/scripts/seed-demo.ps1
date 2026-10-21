<#
    生成演示数据（data\demo.db），用于预览热力图效果
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-AppUsageRoot
$py = Get-AppUsagePython
if (-not $py) {
    Write-Host '[x] 找不到 Python 3.10+' -ForegroundColor Red
    exit 1
}
& $py.Path @($py.Prefix + @((Join-Path $root 'seed_demo.py')))
