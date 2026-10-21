<#
    诊断：把光标移到指定屏幕坐标，抓一张屏幕截图，并真实点击一次。
    坐标是「虚拟化」的（与本脚本进程的 DPI 感知级别一致），SetCursorPos 会自动换算。
#>
param(
    [int]$X = 0,
    [int]$Y = 0,
    [string]$Out = 'D:\minimax\coder\appusage\docs\_probe.png',
    [switch]$NoClick
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Probe {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out System.Drawing.Point p);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
}
'@ -ReferencedAssemblies System.Drawing

[void][Probe]::SetCursorPos($X, $Y)
Start-Sleep -Milliseconds 400

$bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bmp.Size)
$g.Dispose()

# 在光标附近画个圈，方便肉眼定位
$cx = $X - $bounds.Left
$cy = $Y - $bounds.Top
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Lime), 3
$g2 = [System.Drawing.Graphics]::FromImage($bmp)
$g2.DrawEllipse($pen, ($cx - 22), ($cy - 22), 44, 44)
$g2.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$pt = New-Object System.Drawing.Point
[void][Probe]::GetCursorPos([ref]$pt)
Write-Host "[i] 光标实际位置: $($pt.X),$($pt.Y)  截图: $Out"

if (-not $NoClick) {
    [Probe]::mouse_event([Probe]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Probe]::mouse_event([Probe]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Write-Host '[i] 已点击'
}
