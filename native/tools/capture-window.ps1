<#
    抓取原生窗口截图（开发自检用）。
    用法：powershell -File capture-window.ps1 [-Process AppUsage] [-Out path.png]
    优先用 PrintWindow 直接抓窗口内容，不受窗口是否被遮挡 / 在后台影响。
#>
param(
    [string]$Process = 'AppUsage',
    [string]$Out = '',
    [int]$DelaySeconds = 0,
    [long]$TargetHwnd = 0
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class WinCap {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

# 必须让自己也变成 Per-Monitor V2 感知，否则 GetWindowRect 返回的是被虚拟化的逻辑坐标
[void][WinCap]::SetProcessDpiAwarenessContext([IntPtr](-4))

if ($DelaySeconds -gt 0) { Start-Sleep -Seconds $DelaySeconds }

if (-not $Out) {
    # native\tools\ -> native\ -> 项目根 -> docs\
    $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $docs = Join-Path $root 'docs'
    if (-not (Test-Path $docs)) { New-Item -ItemType Directory -Force -Path $docs | Out-Null }
    $Out = Join-Path $docs 'shot-native.png'
}

$proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    Write-Host "[x] 找不到进程 $Process" -ForegroundColor Red
    exit 1
}
$proc.Refresh()
$hwnd = $proc.MainWindowHandle
if ($TargetHwnd -gt 0) { $hwnd = [IntPtr]$TargetHwnd }
if ($hwnd -eq 0) {
    Add-Type -Path (Join-Path $PSScriptRoot 'WinEnum.cs') -ReferencedAssemblies System.Drawing
    $wins = [WinEnum]::ForPid($proc.Id)
    Write-Host "[!] 主窗口句柄为 0，该进程的窗口："
    $wins | ForEach-Object { Write-Host "    $_" }
    exit 1
}

[WinCap]::ShowWindow($hwnd, 9) | Out-Null    # SW_RESTORE
[WinCap]::BringWindowToTop($hwnd) | Out-Null
[WinCap]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 600

$rect = New-Object WinCap+RECT
[WinCap]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Host "[i] 窗口尺寸 ${w}x${h}"

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinCap]::PrintWindow($hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()

if (-not $ok) {
    Write-Host '[!] PrintWindow 失败，回退到屏幕拷贝'
    $g2 = [System.Drawing.Graphics]::FromImage($bmp)
    $g2.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g2.Dispose()
}

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "[+] 已保存 $Out"
