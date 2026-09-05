# MediaOrganizer 桌面版 Velopack 打包脚本
# 产出：Setup.exe（LZMA 压缩的单文件安装包）+ 便携版 zip + 增量更新包
#
# 特点：纯 dotnet 工具链，不需要 Inno Setup / NSIS / WiX 等外部安装器；
#       --framework 让安装包在缺失 .NET 10 Runtime 时自动下载安装（运行时外置）。
#
# 前置（一次性）：
#   dotnet tool install -g vpk
#
# 用法：
#   .\pack-velopack.ps1 -Version 1.0.0
#   .\pack-velopack.ps1 -Version 1.0.1            # 增量包会自动比对 releases/ 里的历史版本
#   .\pack-velopack.ps1 -Version 1.0.0 -SkipPublish   # 复用已发布的目录

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Channel = 'win-x64',
    [string]$PackId = 'MediaOrganizer',
    [string]$PackTitle = 'MediaOrganizer',
    [string]$MainExe = 'MediaOrganizer.Desktop.exe',
    [switch]$SkipPublish,
    [switch]$NoPortable
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot    = Split-Path -Parent $PSScriptRoot
$packDir     = Join-Path $PSScriptRoot "publish/$RuntimeIdentifier"
$outputDir   = Join-Path $PSScriptRoot 'releases'
$icon        = Join-Path $repoRoot 'src/MediaOrganizer.Desktop/Assets/app-icon.ico'

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -RuntimeIdentifier $RuntimeIdentifier
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "未找到 vpk。请先执行：dotnet tool install -g vpk"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host "==> Velopack 打包 v$Version"
$args = @(
    'pack',
    '--packId',      $PackId,
    '--packVersion', $Version,
    '--packTitle',   $PackTitle,
    '--channel',     $Channel,
    '--packDir',     $packDir,
    '--mainExe',     $MainExe,
    '--outputDir',   $outputDir,
    # 运行时外置：安装时若目标机没有 .NET 10 Runtime（注意是 Runtime，不是 Desktop Runtime），
    # 安装包会自动下载安装。Avalonia 只依赖 Microsoft.NETCore.App，不需要 WindowsDesktop。
    '--framework',   'net10.0-x64-runtime'
)
if (Test-Path $icon) { $args += @('--icon', $icon) }
if ($NoPortable)     { $args += '--noPortable' }

& vpk @args
if ($LASTEXITCODE -ne 0) { throw "vpk pack 失败（exit=$LASTEXITCODE）" }

Write-Host "==> 产物："
Get-ChildItem -Path $outputDir -File | Sort-Object LastWriteTime -Descending |
    Select-Object -First 6 |
    ForEach-Object { "     {0,-38} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
