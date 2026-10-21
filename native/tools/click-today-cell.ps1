<#
    找出窗口截图里「今天」格子（橙色描边 #d97757）的位置并点击它。
    用于自动化验证热力图下钻交互。
#>
param(
    [string]$Process = 'AppUsage'
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Loc {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
[void][Loc]::SetProcessDpiAwarenessContext([IntPtr](-4))

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$shot = Join-Path $root 'docs\shot-native.png'
if (-not (Test-Path $shot)) { Write-Host '[x] 没有截图，先跑 capture-window.ps1' -ForegroundColor Red; exit 1 }

$bmp = [System.Drawing.Bitmap]::FromFile($shot)
$target = [System.Drawing.ColorTranslator]::FromHtml('#d97757')
# 只在热力图网格区域内找（避开统计卡上的同色强调条）
$rx0 = 80; $rx1 = [Math]::Min(1490, $bmp.Width - 1)
$ry0 = 300; $ry1 = [Math]::Min(600, $bmp.Height - 1)
$minX = 99999; $minY = 99999; $maxX = -1; $maxY = -1
for ($y = $ry0; $y -le $ry1; $y += 1) {
    for ($x = $rx0; $x -le $rx1; $x += 1) {
        $c = $bmp.GetPixel($x, $y)
        if ([Math]::Abs($c.R - $target.R) -lt 26 -and [Math]::Abs($c.G - $target.G) -lt 26 -and [Math]::Abs($c.B - $target.B) -lt 26) {
            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$bmp.Dispose()

if ($maxX -lt 0) { Write-Host '[x] 截图里没找到橙色高亮格' -ForegroundColor Red; exit 1 }

$cx = [int](($minX + $maxX) / 2)
$cy = [int](($minY + $maxY) / 2)
Write-Host "[i] 橙色格子位于图片 ($cx, $cy)，尺寸 $($maxX - $minX) x $($maxY - $minY)"

$proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Select-Object -First 1
$proc.Refresh()
$rect = New-Object Loc+RECT
[Loc]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
$sx = $rect.Left + $cx
$sy = $rect.Top + $cy
Write-Host "[i] 屏幕坐标 ($sx, $sy)"

& (Join-Path $PSScriptRoot 'click-at.ps1') -Process $Process -X $sx -Y $sy
