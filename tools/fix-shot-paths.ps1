# 一次性数据修复：把私有仓里误传到 shots/YYYY/MM/DD/ 的文件移到正确的 shots/YYYY/MM/dd/ 目录。
# 用 Git Data API（同一个 blob 换路径），不重新上传图片数据。
$ErrorActionPreference = 'Stop'

$owner = 'iamfromchangsha'
$repo = 'appusage-shots'
$token = (gh auth token 2>$null)
if (-not $token) { Write-Output '拿不到 gh token'; exit 1 }

$h = @{ 'Authorization' = "Bearer $token"; 'Accept'='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28'; 'User-Agent'='AppUsage' }
$api = "https://api.github.com/repos/$owner/$repo"

Write-Output '=== 当前文件树 ==='
$tree = Invoke-RestMethod -Uri "$api/git/trees/main?recursive=1" -Headers $h -Method Get -TimeoutSec 30
$files = $tree.tree | Where-Object { $_.type -eq 'blob' -and $_.path -like 'shots/*' }
$files | ForEach-Object { Write-Output ("  " + $_.path) }
if (-not $files) { Write-Output '  （没有 shots 下的文件）'; exit 0 }

$entries = @()
foreach ($f in $files) {
    if ($f.path -notmatch '^shots/(\d{4})/(\d{2})/DD/(.+)$') { continue }
    $new = "shots/$($Matches[1])/$($Matches[2])/23/$($Matches[3])"
    Write-Output ("  移动: " + $f.path + "  ->  " + $new)
    $entries += @{ path = $new; mode = '100644'; type = 'blob'; sha = $f.sha }
    $entries += @{ path = $f.path; mode = '100644'; type = 'blob'; sha = $null }   # null = 删除旧路径
}
if (-not $entries) { Write-Output '  （没有需要移动的文件）'; exit 0 }

Write-Output '=== 创建新 tree ==='
$body = @{ base_tree = $tree.sha; tree = $entries } | ConvertTo-Json -Depth 5
$newTree = Invoke-RestMethod -Uri "$api/git/trees" -Headers $h -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 30
Write-Output ("  新 tree: " + $newTree.sha)

Write-Output '=== 创建 commit ==='
$ref = Invoke-RestMethod -Uri "$api/git/refs/heads/main" -Headers $h -Method Get -TimeoutSec 30
$headCommit = $ref.object.sha
$commitBody = @{
    message = 'fix: 把截图移到正确的日期目录（DD -> 23）'
    tree    = $newTree.sha
    parents = @($headCommit)
} | ConvertTo-Json -Depth 5
$newCommit = Invoke-RestMethod -Uri "$api/git/commits" -Headers $h -Method Post -Body $commitBody -ContentType 'application/json' -TimeoutSec 30
Write-Output ("  新 commit: " + $newCommit.sha)

Write-Output '=== 更新 main 分支 ==='
$refBody = @{ sha = $newCommit.sha; force = $false } | ConvertTo-Json
$null = Invoke-RestMethod -Uri "$api/git/refs/heads/main" -Headers $h -Method Patch -Body $refBody -ContentType 'application/json' -TimeoutSec 30
Write-Output '  已更新'

Write-Output '=== 结果 ==='
$after = Invoke-RestMethod -Uri "$api/git/trees/main?recursive=1" -Headers $h -Method Get -TimeoutSec 30
$after.tree | Where-Object { $_.type -eq 'blob' -and $_.path -like 'shots/*' } | ForEach-Object { Write-Output ("  " + $_.path) }
