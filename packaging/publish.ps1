# MediaOrganizer 桌面版发布脚本
# 产出：自包含（内嵌 .NET Runtime）+ ReadyToRun 预编译的发布目录
#
# 用法：
#   .\publish.ps1                          # Release / win-x64 / 自包含 + R2R
#   .\publish.ps1 -NoReadyToRun            # 关掉 R2R，省约 17MB（启动略慢）
#   .\publish.ps1 -RuntimeIdentifier win-arm64
#
# 说明：自包含后目标机无需安装 .NET Runtime，PublishTrimmed 理论上可用，
#       但 Avalonia 大量反射绑定，裁剪极易运行时崩溃，故不启用（ADR-0009 决策 1 的
#       "裁剪互斥"结论已因改为自包含失效，风险结论仍然成立）。

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$NoReadyToRun,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'src/MediaOrganizer.Desktop/MediaOrganizer.Desktop.csproj'

if (-not $OutputDir) {
    $OutputDir = Join-Path $PSScriptRoot "publish/$RuntimeIdentifier"
}

# R2R 预编译：体积 +约 17MB，换来明显更快的冷启动。关掉则传 -NoReadyToRun。
$r2r = if ($NoReadyToRun) { 'false' } else { 'true' }

Write-Host "==> 发布 $Configuration / $RuntimeIdentifier (self-contained=true, R2R=$r2r)"
dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishReadyToRun=$r2r `
    -p:PublishSingleFile=false `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit=$LASTEXITCODE）" }

# 原生调试符号（libSkiaSharp.pdb / libHarfBuzzSharp.pdb，合计约 102MB）由
# Desktop.csproj 的 PrunePublishOutput 目标在 Publish 后删除，这里做一次兜底校验。
$leftover = Get-ChildItem -Path $OutputDir -Filter '*.pdb' -Recurse -File
if ($leftover) { throw "发布目录仍有 $($leftover.Count) 个 .pdb 未清理" }

# 自包含应内嵌运行时，不该再依赖 runtimes\ 下的外部布局
if (-not (Test-Path (Join-Path $OutputDir 'coreclr.dll'))) {
    throw "发布目录未找到 coreclr.dll，疑似未按自包含发布"
}

$size = (Get-ChildItem -Path $OutputDir -Recurse -File | Measure-Object Length -Sum).Sum
$count = (Get-ChildItem -Path $OutputDir -Recurse -File).Count
Write-Host "==> 完成：$OutputDir"
Write-Host ("     {0} 个文件，共 {1:N1} MB" -f $count, ($size / 1MB))
