# 公共函数：定位项目根目录与 Python 解释器

function Get-AppUsageRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Get-AppUsagePython {
    $cands = @()

    # 1) 官方 per-user 安装目录（最稳）
    foreach ($v in '313', '312', '311', '310') {
        $p = Join-Path $env:LOCALAPPDATA "Programs\Python\Python$v\python.exe"
        if (Test-Path $p) { $cands += ,@($p, @()) }
    }

    # 2) PATH 上的 python（可能是转发 shim，也可能是 WindowsApps 死存根）
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { $cands += ,@($cmd.Source, @()) }

    # 3) py 启动器
    $pycmd = Get-Command py -ErrorAction SilentlyContinue
    if ($pycmd -and $pycmd.Source) { $cands += ,@($pycmd.Source, @('-3')) }

    foreach ($c in $cands) {
        $exe = $c[0]
        $prefix = $c[1]
        if (-not (Test-Path $exe)) { continue }
        try {
            $probe = & $exe @($prefix + @('-c', "import sys;print('%d.%d' % sys.version_info[:2])")) 2>$null
            if ($LASTEXITCODE -eq 0 -and $probe) {
                $ver = ($probe | Select-Object -First 1).ToString().Trim()
                if ($ver -match '^\d+\.\d+') {
                    return [pscustomobject]@{ Path = $exe; Prefix = $prefix; Version = $ver }
                }
            }
        } catch { }
    }
    return $null
}

function Get-AppUsageTrackerPid {
    $pidFile = Join-Path (Get-AppUsageRoot) 'data\tracker.pid'
    if (-not (Test-Path $pidFile)) { return $null }
    try {
        $raw = (Get-Content $pidFile -Raw).Trim()
        if (-not $raw) { return $null }
        $p = [int]$raw
        if (Get-Process -Id $p -ErrorAction SilentlyContinue) { return $p }
    } catch { }
    return $null
}

function Quote-Arg {
    param([string]$Value)
    if ($null -eq $Value) { return '""' }
    if ($Value -match '\s') { return '"' + $Value + '"' }
    return $Value
}
