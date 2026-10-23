<#
    给 trace.log 里记录的某个句柄投递一次点击（自动化验证用，不依赖焦点 / z-order）。
    用法：powershell -File click-traced.ps1 -Marker "report button" [-LocalX 30 -LocalY 13]
#>
param(
    [string]$Marker = '',
    [int]$LocalX = 30,
    [int]$LocalY = 13,
    [string]$Trace = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class TracedClick {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    public const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
}
'@

if (-not $Trace) {
    $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $Trace = Join-Path $root 'dist\data\trace.log'
}

$line = Get-Content $Trace | Where-Object { $_ -like "*$Marker*" } | Select-Object -Last 1
if (-not $line) { Write-Host "[x] trace 里找不到 '$Marker'" -ForegroundColor Red; exit 1 }
if ($line -notmatch 'hwnd = (\d+)') { Write-Host '[x] 解析失败' -ForegroundColor Red; exit 1 }
$hwnd = [IntPtr][int64]$Matches[1]

$lp = [IntPtr](($LocalY -shl 16) -bor ($LocalX -band 0xFFFF))
[void][TracedClick]::PostMessage($hwnd, [TracedClick]::WM_LBUTTONDOWN, [IntPtr]1, $lp)
Start-Sleep -Milliseconds 80
[void][TracedClick]::PostMessage($hwnd, [TracedClick]::WM_LBUTTONUP, [IntPtr]0, $lp)
Write-Host "[i] 已向 '$Marker' 投递点击 (hwnd=$hwnd)"
