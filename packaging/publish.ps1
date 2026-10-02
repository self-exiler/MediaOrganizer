# MediaOrganizer 桌面版发布脚本
# 产出：Native AOT 发布目录（原生单 exe + 原生库），目标机无需安装 .NET Runtime
#
# 用法：
#   .\publish.ps1                              # Release / win-x64 / Native AOT
#   .\publish.ps1 -RuntimeIdentifier win-arm64
#   .\publish.ps1 -OutputDir packaging/publish/win-x64   # CI 与安装包脚本约定此目录
#
# 成立前提：闭包内不得有反射式序列化与动态视图解析——已由 AppJson（System.Text.Json
#           源生成）与桌面 ViewLocator（静态登记表）满足，并以 <IsAotCompatible> 让
#           普通构建常驻 IL2026/IL3050 分析器防回归。
# 残留风险：TagLibSharp / SMBLibrary 是无标注的 netstandard2.0 库，AOT 下仍报 IL2104
#           （分析器看不见其内部，其中 TagLibSharp 用 Assembly.GetTypes() 建 mimetype
#           表，最可能在裁剪后静默失效）。发版前应跑一轮真实照片库的功能回归。

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'src/MediaOrganizer.Desktop/MediaOrganizer.Desktop.csproj'

if (-not $OutputDir) {
    $OutputDir = Join-Path $PSScriptRoot "publish/$RuntimeIdentifier"
}

Write-Host "==> 发布 $Configuration / $RuntimeIdentifier (Native AOT)"

# CI 发版时经 GITHUB_ENV 注入 APP_VERSION(tag 剥掉前导 v),让 exe/dll 的
# FileVersion/InformationalVersion 跟上 tag;本地未设置时跳过,走 csproj 默认值。
$versionArgs = @()
if ($env:APP_VERSION) {
    $versionArgs += @("-p:Version=$($env:APP_VERSION)", "-p:InformationalVersion=$($env:APP_VERSION)")
    Write-Host "==> 程序集版本 = $($env:APP_VERSION)"
}

dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishAot=true `
    -p:PublishSingleFile=false `
    @versionArgs `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit=$LASTEXITCODE）" }

# AOT 的原生 pdb（实测 108MB）由 ILCompiler 在 Publish 目标之后产出，csproj 的
# PrunePublishOutput 删不到它。不放进安装包但需保留供崩溃符号解析，故移出而非删除。
$symbolDir = Join-Path $PSScriptRoot "symbols/$RuntimeIdentifier"
New-Item -ItemType Directory -Force -Path $symbolDir | Out-Null
foreach ($pdb in Get-ChildItem -Path $OutputDir -Filter '*.pdb' -File) {
    Move-Item -Force $pdb.FullName (Join-Path $symbolDir $pdb.Name)
    Write-Host "==> 符号已移出：$($pdb.Name) → $symbolDir"
}

# 原生调试符号（libSkiaSharp.pdb / libHarfBuzzSharp.pdb 等）由 Desktop.csproj 的
# PrunePublishOutput 目标在 Publish 后删除，这里做一次兜底校验。
$leftover = Get-ChildItem -Path $OutputDir -Filter '*.pdb' -Recurse -File
if ($leftover) { throw "发布目录仍有 $($leftover.Count) 个 .pdb 未清理" }

# Native AOT 无 coreclr.dll：托管代码全部编进单个 exe，产物里不该再有本项目程序集。
# 若还能看到 MediaOrganizer.Core.dll，说明 AOT 没生效（发出的会是混合布局）。
if (-not (Test-Path (Join-Path $OutputDir 'MediaOrganizer.Desktop.exe'))) {
    throw "发布目录未找到 MediaOrganizer.Desktop.exe，疑似未按 Native AOT 发布"
}
if (Test-Path (Join-Path $OutputDir 'MediaOrganizer.Core.dll')) {
    throw "发布目录仍存在 MediaOrganizer.Core.dll，AOT 编译未生效"
}

$size = (Get-ChildItem -Path $OutputDir -Recurse -File | Measure-Object Length -Sum).Sum
$count = (Get-ChildItem -Path $OutputDir -Recurse -File).Count
Write-Host "==> 完成：$OutputDir"
Write-Host ("     {0} 个文件，共 {1:N1} MB" -f $count, ($size / 1MB))
