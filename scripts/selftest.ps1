<#
    跑 AppUsage 的内置自测（--selftest）。
    自测是纯逻辑的：不联网、不碰真实数据，随时可以跑。
    完整结果也会写到 dist\data\selftest.txt。
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'dist\AppUsage.exe'

if (-not (Test-Path $exe)) {
    Write-Host "[-] 找不到 $exe" -ForegroundColor Red
    Write-Host '    先发布：dotnet publish native\AppUsage.csproj -c Release -r win-x64 --self-contained true -o dist' -ForegroundColor DarkGray
    exit 1
}

# 本程序是 WinExe，要用 Start-Process -Wait 才等得到退出码
$p = Start-Process -FilePath $exe -ArgumentList '--selftest' -NoNewWindow -Wait -PassThru

$color = if ($p.ExitCode -eq 0) { 'Green' } else { 'Red' }
Write-Host ''
Write-Host ("自测退出码：{0}（0 = 全过）" -f $p.ExitCode) -ForegroundColor $color
Write-Host ("报告：{0}" -f (Join-Path $root 'dist\data\selftest.txt')) -ForegroundColor DarkGray

exit $p.ExitCode
