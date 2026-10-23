<#
    查看应用使用时长（原生 EXE 版）的运行状态与统计
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'dist\AppUsage.exe'
$logDir = Join-Path $root 'dist\data\logs'

Write-Host '========== 应用使用时长 · 状态 ==========' -ForegroundColor Cyan

$proc = Get-Process -Name 'AppUsage' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($proc) {
    $ws = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    $cpu = [math]::Round($proc.TotalProcessorTime.TotalSeconds, 1)
    Write-Host "[+] 应用运行中 (PID $($proc.Id))  内存 ${ws} MB  累计 CPU ${cpu} 秒" -ForegroundColor Green
} else {
    Write-Host '[-] 应用未运行  (双击 dist\AppUsage.exe 启动)' -ForegroundColor Yellow
}

$startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$lnk = Join-Path $startup '应用使用时长.lnk'
$cmd = Join-Path $startup 'AppUsageTracker.cmd'
if (Test-Path $lnk) {
    # 别只看「文件在不在」——快捷方式指向的程序被挪走的话，开机根本拉不起来
    $target = $null
    try {
        $sh = New-Object -ComObject WScript.Shell
        $target = $sh.CreateShortcut($lnk).TargetPath
    } catch { }
    if ($target -and -not (Test-Path $target)) {
        Write-Host "[!] 开机自启：快捷方式指向的程序不存在 ($target)" -ForegroundColor Red
        Write-Host '    程序被挪过位置的话，重新开一次自启即可（托盘菜单 → 开机自启）' -ForegroundColor DarkGray
    } else {
        Write-Host '[+] 开机自启：已开启' -ForegroundColor Green
    }
} elseif (Test-Path $cmd) {
    Write-Host '[+] 开机自启：已开启（用的是 .cmd 兜底方式）' -ForegroundColor Green
} else {
    Write-Host '[-] 开机自启：未开启  (运行 scripts\toggle-autostart.ps1 可切换)' -ForegroundColor Yellow
}

if (-not (Test-Path $logDir)) {
    Write-Host "[-] 还没有数据目录：$logDir" -ForegroundColor Yellow
    exit 0
}

$logFiles = Get-ChildItem $logDir -Filter *.log -ErrorAction SilentlyContinue
$sizeKb = [math]::Round((($logFiles | Measure-Object -Property Length -Sum).Sum) / 1KB, 1)
Write-Host "[i] 数据目录：$logDir（$($logFiles.Count) 个日志文件，共 $sizeKb KB）"

# 用 Python 读一遍并输出纯 ASCII JSON，中文标签交给 PowerShell 格式化
$py = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python313\python.exe'
if (-not (Test-Path $py)) { $py = 'python' }

$code = @'
import sys, os, glob, json, datetime
log_dir = sys.argv[1]
today = datetime.date.today().isoformat()
daily = {}
apps = {}
total = 0.0
for fp in sorted(glob.glob(os.path.join(log_dir, "*.log"))):
    with open(fp, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            p = line.rstrip("\n").split("\t")
            if len(p) < 6:
                continue
            try:
                start = float(p[3]); dur = float(p[5])
            except ValueError:
                continue
            d = datetime.datetime.fromtimestamp(start).strftime("%Y-%m-%d")
            daily[d] = daily.get(d, 0.0) + dur
            apps[p[0]] = apps.get(p[0], 0.0) + dur
            total += dur
out = {
    "total": total,
    "today": daily.get(today, 0.0),
    "days": len(daily),
    "apps": len(apps),
    "top": sorted(apps.items(), key=lambda kv: -kv[1])[:5],
}
print(json.dumps(out, ensure_ascii=True))
'@

$tmp = Join-Path $env:TEMP ("appusage_status_" + [guid]::NewGuid().ToString('N') + ".py")
Set-Content -Path $tmp -Value $code -Encoding UTF8
try {
    $raw = & $py $tmp $logDir 2>$null | Select-Object -First 1
} finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
if (-not $raw) { exit 1 }
$data = $raw | ConvertFrom-Json

function Format-Duration {
    param([double]$Seconds)
    $s = [int]$Seconds
    $h = [math]::Floor($s / 3600)
    $m = [math]::Floor(($s % 3600) / 60)
    if ($h -gt 0) { return "{0} 小时 {1} 分" -f $h, $m }
    if ($m -gt 0) { return "{0} 分" -f $m }
    return "{0} 秒" -f $s
}

Write-Host ("  今日使用 : {0}" -f (Format-Duration $data.today))
Write-Host ("  累计使用 : {0}" -f (Format-Duration $data.total))
Write-Host ("  活跃天数 : {0}" -f $data.days)
Write-Host ("  应用数量 : {0}" -f $data.apps)
if ($data.top.Count -gt 0) {
    Write-Host '  --- 累计 TOP5 ---' -ForegroundColor DarkGray
    foreach ($row in $data.top) {
        Write-Host ("    {0,-16} {1}" -f $row[0], (Format-Duration $row[1]))
    }
}
