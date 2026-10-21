<#
    点击顶栏的「浅色 / 深色」切换按钮（自动化验证用）。
    直接向按钮句柄投递鼠标消息：不受窗口被遮挡 / 前台锁影响。
#>
param(
    [string]$Process = 'AppUsage'
)

$ErrorActionPreference = 'Stop'

Add-Type -Path (Join-Path $PSScriptRoot 'WinEnum.cs') -ReferencedAssemblies System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Post {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    public const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
}
'@

$proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host "[x] 找不到进程 $Process" -ForegroundColor Red; exit 1 }
$proc.Refresh()
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq 0) { Write-Host '[x] 没有主窗口' -ForegroundColor Red; exit 1 }

$found = [WinEnum]::FindChild($hwnd, '色')
if (-not $found -or $found.Count -eq 0) {
    Write-Host '[x] 没找到主题切换按钮' -ForegroundColor Red
    exit 1
}
$info = $found[0]
Write-Host "[i] $info"
if ($info -notmatch 'hwnd=(\d+)') { Write-Host '[x] 解析失败' -ForegroundColor Red; exit 1 }
$btn = [IntPtr][int64]$Matches[1]

$lp = [IntPtr]((19 -shl 16) -bor (52 -band 0xFFFF))
[void][Post]::PostMessage($btn, [Post]::WM_LBUTTONDOWN, [IntPtr]1, $lp)
Start-Sleep -Milliseconds 80
[void][Post]::PostMessage($btn, [Post]::WM_LBUTTONUP, [IntPtr]0, $lp)
Write-Host '[i] 已向主题按钮投递点击消息'
