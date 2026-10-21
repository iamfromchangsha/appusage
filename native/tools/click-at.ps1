<#
    在窗口内指定偏移处发一次真实鼠标点击（自动化验证用）。
    会把目标窗口临时置顶，保证点击真的落在它上面（前台锁会让 SetForegroundWindow 失败）。

    用法：powershell -File click-at.ps1 -Process AppUsage -ScreenX 100 -ScreenY 200
#>
param(
    [string]$Process = 'AppUsage',
    [int]$ScreenX = 0,
    [int]$ScreenY = 0,
    [int]$OffsetX = -1,
    [int]$OffsetY = -1
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class RealClick {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
}
'@
[void][RealClick]::SetProcessDpiAwarenessContext([IntPtr](-4))

$proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host "[x] 找不到进程 $Process" -ForegroundColor Red; exit 1 }
$proc.Refresh()
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq 0) { Write-Host '[x] 没有主窗口' -ForegroundColor Red; exit 1 }

if ($OffsetX -ge 0) {
    $rect = New-Object RealClick+RECT
    [RealClick]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $ScreenX = $rect.Left + $OffsetX
    $ScreenY = $rect.Top + $OffsetY
}

# 临时置顶 → 真实点击 → 取消置顶
$topmost = [IntPtr](-1)
$notop = [IntPtr](-2)
[RealClick]::SetWindowPos($hwnd, $topmost, 0, 0, 0, 0, [RealClick]::SWP_NOMOVE -bor [RealClick]::SWP_NOSIZE -bor [RealClick]::SWP_NOACTIVATE) | Out-Null
[RealClick]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400

[void][RealClick]::SetCursorPos($ScreenX, $ScreenY)
Start-Sleep -Milliseconds 250
[RealClick]::mouse_event([RealClick]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[RealClick]::mouse_event([RealClick]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 250

[RealClick]::SetWindowPos($hwnd, $notop, 0, 0, 0, 0, [RealClick]::SWP_NOMOVE -bor [RealClick]::SWP_NOSIZE -bor [RealClick]::SWP_NOACTIVATE) | Out-Null
Write-Host "[i] 已在 ($ScreenX, $ScreenY) 真实点击"
