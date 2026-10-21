<#
    用 PostMessage 直接给窗口投递鼠标消息（不依赖焦点 / z-order），
    坐标从应用的 trace.log 里读（"today cell client = x,y"）。

    用法：powershell -File click-cell.ps1
#>
param(
    [string]$Process = 'AppUsage',
    [string]$Trace = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Msg {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);
    public const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
}
'@
[void][Msg]::SetProcessDpiAwarenessContext([IntPtr](-4))

if (-not $Trace) {
    $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $Trace = Join-Path $root 'dist\data\trace.log'
}
if (-not (Test-Path $Trace)) { Write-Host "[x] 找不到 $Trace" -ForegroundColor Red; exit 1 }

$line = Get-Content $Trace | Where-Object { $_ -like '*today cell client*' } | Select-Object -Last 1
if (-not $line) { Write-Host '[x] trace 里没有格子坐标，先重启应用' -ForegroundColor Red; exit 1 }
if ($line -notmatch 'heat hwnd = (\d+) cell local = (-?\d+),(-?\d+)') { Write-Host '[x] 解析失败' -ForegroundColor Red; exit 1 }
$heatHwnd = [IntPtr][int64]$Matches[1]
$cx = [int]$Matches[2]; $cy = [int]$Matches[3]
Write-Host "[i] 热力图控件 hwnd=$heatHwnd 格子局部坐标 ($cx, $cy)"

$lp = [IntPtr](($cy -shl 16) -bor ($cx -band 0xFFFF))
[void][Msg]::PostMessage($heatHwnd, [Msg]::WM_LBUTTONDOWN, [IntPtr]1, $lp)
Start-Sleep -Milliseconds 80
[void][Msg]::PostMessage($heatHwnd, [Msg]::WM_LBUTTONUP, [IntPtr]0, $lp)
Write-Host '[i] 已投递鼠标消息'
