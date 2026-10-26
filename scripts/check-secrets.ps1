<#
    提交前扫密钥：被跟踪的文件里不能出现「真密钥」。

    起因：写「凭据清洗」自测时，把真实 DeepSeek key 当填充字符串贴进了源码，
    一路推到公开仓库里，三天才发现。密钥一旦进过公开仓库就必须轮换，
    补救成本远高于提交前扫一遍。

    扫两类：
      1) 本机 config.json 里 ai.api_key / shots.github_token 的**原值**
      2) 形状像密钥的字符串（sk- + 32 位 hex、github_pat_、ghp_ 等）

    用法：powershell -ExecutionPolicy Bypass -File scripts\check-secrets.ps1
    退出码 0 = 干净，1 = 发现疑似泄漏。
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$tracked = @(git ls-files)
if ($tracked.Count -eq 0) { Write-Host '[!] 不是 git 仓库或没有跟踪文件' -ForegroundColor Red; exit 1 }

$hits = New-Object System.Collections.Generic.List[string]

function Test-Text([string]$path, [string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return }
    $lines = $text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($pat in @('sk-[0-9a-fA-F]{32}', 'github_pat_[0-9A-Za-z]{20,}', 'ghp_[0-9A-Za-z]{30,}', 'gho_[0-9A-Za-z]{30,}')) {
            if ($line -match $pat) {
                # 掩码后再报出来，别把密钥原文再打一遍到日志里
                $masked = [regex]::Replace($line, $pat, { param($m) $m.Value.Substring(0, [Math]::Min(8, $m.Value.Length)) + '...' })
                $hits.Add("$path`:$($i + 1)  $masked")
            }
        }
    }
}

# 1) 真值：从本机 config.json 里取（仓库里的 config.json 是被 .gitignore 排除的）
$cfgPath = Join-Path $root 'config.json'
if (Test-Path $cfgPath) {
    try {
        $cfg = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($secret in @($cfg.ai.api_key, $cfg.shots.github_token)) {
            if ([string]::IsNullOrWhiteSpace($secret)) { continue }
            if ($secret.Length -lt 12) { continue }
            foreach ($f in $tracked) {
                $full = Join-Path $root $f
                if (-not (Test-Path $full)) { continue }
                try { $text = [System.IO.File]::ReadAllText($full) } catch { continue }
                if ($text.Contains($secret)) {
                    $hits.Add("$f  ← 含 config.json 里的真实密钥（已掩码）")
                }
            }
        }
    }
    catch {
        Write-Host "[!] 读 config.json 失败：$($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# 2) 形状扫描
foreach ($f in $tracked) {
    $full = Join-Path $root $f
    if (-not (Test-Path $full)) { continue }
    $ext = [System.IO.Path]::GetExtension($full).ToLowerInvariant()
    if ($ext -in @('.png', '.jpg', '.jpeg', '.gif', '.ico', '.exe', '.zip', '.dll')) { continue }
    try { $text = [System.IO.File]::ReadAllText($full) } catch { continue }
    Test-Text $f $text
}

if ($hits.Count -gt 0) {
    Write-Host ''
    Write-Host "[x] 发现 $($hits.Count) 处疑似密钥（**不要提交**）：" -ForegroundColor Red
    $hits | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '    这些是密钥的真实形状或本机 config.json 里的原值。' -ForegroundColor Yellow
    Write-Host '    已经在公开仓库里待过的密钥，**必须去服务商那边轮换**，光删代码没用。' -ForegroundColor Yellow
    exit 1
}

Write-Host "[✓] 干净：$($tracked.Count) 个跟踪文件里没有密钥" -ForegroundColor Green
exit 0
