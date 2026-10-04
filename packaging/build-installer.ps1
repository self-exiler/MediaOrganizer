# MediaOrganizer 桌面版安装包构建脚本（WiX v5 → MSI）
#
# 用法：
#   .\build-installer.ps1                      # 用 packaging/publish/win-x64 出包
#   .\build-installer.ps1 -Version 1.4.0       # 覆盖版本号（CI 由 APP_VERSION 传入）
#   .\build-installer.ps1 -PublishDir <dir> -OutputDir <dir>
#
# 前置（一次性）：
#   dotnet tool install --global wix --version 5.0.2
#   wix extension add -g WixToolset.UI.wixext/5.0.2
#
# 为什么需要本脚本而不是直接 wix build：发布目录里的文件由 Native AOT 决定，会随依赖变化
# （今天 5 个，明天可能 6 个）。WiX v5 的 <Files> 自动 harvest 只能"一文件一组件"，而 per-user
# 安装下 ICE38 要求组件用 HKCU 注册表值做 keypath（自动 GUID 又不允许"注册表 keypath + 文件"
# 的组合）。所以这里按发布目录实际内容生成一个单组件清单 obj/Harvest.wxs，GUID 固定。

[CmdletBinding()]
param(
    [string]$Version,
    [string]$PublishDir,
    [string]$OutputDir,
    [string]$WixCulture = 'zh-CN',
    [switch]$SkipValidate
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$packagingDir = $PSScriptRoot
$repoRoot     = Split-Path -Parent $packagingDir
$objDir       = Join-Path $packagingDir 'obj'

# wix 把告警/错误写到 stderr。$ErrorActionPreference='Stop' 下 PowerShell 会把原生命令的
# stderr 包成 ErrorRecord 抛出，拿不到 WiX 自己的退出码与完整诊断，故调用期间降为 Continue。
function Invoke-Wix {
    param([string[]]$Arguments)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & wix @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prev }
    [pscustomobject]@{ ExitCode = $code; Output = $out }
}

# 应用文件组件的 GUID：由 Product.wxs 的 $(var.AppFilesGuid) 使用。**永不可改**——
# 改了以后升级不会清理旧版本的文件（MSI 视其为不同组件）。
$AppFilesGuid = '{3F6B9C2E-8D41-4A7B-B0C5-2E9F7D4A1C63}'

# 与 MediaOrganizer.iss（已删除）时代的默认值一致；CI 用 APP_VERSION 覆盖。
if (-not $Version) { $Version = if ($env:APP_VERSION) { $env:APP_VERSION } else { '1.3.0' } }
if ($Version -notmatch '^\d+(\.\d+){1,3}$') {
    throw "版本号 '$Version' 不符合 MSI 要求（形如 1.4.0 或 1.4.0.1）"
}

if (-not $PublishDir) { $PublishDir = Join-Path $packagingDir 'publish/win-x64' }
if (-not $OutputDir)  { $OutputDir  = Join-Path $packagingDir 'releases' }
$PublishDir = (Resolve-Path -LiteralPath $PublishDir).Path

# ---------- 前置检查 ----------
$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    throw "未找到 wix CLI。安装：dotnet tool install --global wix --version 5.0.2（并确保 ~/.dotnet/tools 在 PATH 上）"
}
$wixVersion = ((& wix --version) | Out-String).Trim()
Write-Host "==> $wixVersion"
if ($wixVersion -notmatch '\b([4-9])\.\d+') { throw "需要 WiX v5（当前 $wixVersion）" }

$mainExe = Join-Path $PublishDir 'MediaOrganizer.Desktop.exe'
if (-not (Test-Path -LiteralPath $mainExe)) {
    throw "发布目录缺少 MediaOrganizer.Desktop.exe，请先跑 .\publish.ps1（$PublishDir）"
}

$files = @(Get-ChildItem -LiteralPath $PublishDir -File -Recurse)
if ($files.Count -eq 0) { throw "发布目录为空：$PublishDir" }
# 单组件模型只覆盖根目录文件；出现子目录说明发布布局变了，宁可报错也不要静默漏装。
$dirs = @($files | Where-Object { $_.DirectoryName -ne $PublishDir })
if ($dirs) {
    throw "发布目录含子目录（$($dirs[0].FullName)），当前打包模型只支持扁平布局，请同步修改本脚本与 Product.wxs"
}

# wix build 按调用者 CWD 解析 .wxs 里的相对路径，从仓库根调用即失效，所以图标以绝对路径传入。
$AppIcon = Join-Path $repoRoot 'src\MediaOrganizer.Desktop\Assets\app-icon.ico'
if (-not (Test-Path -LiteralPath $AppIcon)) { throw "找不到应用图标：$AppIcon" }

# ---------- 生成 harvest 清单 ----------
New-Item -ItemType Directory -Force -Path $objDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$esc = { param($s) [System.Security.SecurityElement]::Escape($s) }
$fileEntries = $files | ForEach-Object {
    "        <File Source=`"$(& $esc $_.FullName)`" />"
}
$harvest = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <!-- 本文件由 build-installer.ps1 自动生成，不要手工编辑，也不要纳入版本控制。
       单个组件承载全部应用文件：exe 与原生 dll 必须同进同退。
       keypath 用 HKCU 注册表值（per-user 安装的 ICE38 要求），RemoveFolder 清目录（ICE64）。 -->
  <Fragment>
    <ComponentGroup Id="MediaOrganizerFiles" Directory="APPLICATIONFOLDER">
      <Component Id="MediaOrganizerFiles" Guid="`$(var.AppFilesGuid)">
        <RegistryValue Root="HKCU" Key="Software\MediaOrganizer" Name="Installed"
                       Type="integer" Value="1" KeyPath="yes" />
        <RemoveFolder Id="RemoveApplicationFolder" Directory="APPLICATIONFOLDER" On="uninstall" />
        <!-- %LocalAppData%\Programs 是中间层：只在它为空时才会被删（别的程序在用就留着），
             但没有这条 ICE64 会报错。 -->
        <RemoveFolder Id="RemoveLocalAppProgramsDir" Directory="LocalAppProgramsDir" On="uninstall" />
$fileEntries
      </Component>
    </ComponentGroup>
  </Fragment>
</Wix>
"@
$harvestPath = Join-Path $objDir 'Harvest.wxs'
[System.IO.File]::WriteAllText($harvestPath, $harvest, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "==> 已生成 $harvestPath（$($files.Count) 个文件）"

# ---------- 构建 ----------
$msiPath = Join-Path $OutputDir "MediaOrganizer-windows-x64-$Version.msi"
$buildArgs = @(
    'build'
    (Join-Path $packagingDir 'Product.wxs')
    $harvestPath
    '-ext'; 'WixToolset.UI.wixext'
    '-culture'; $WixCulture
    '-d'; "Version=$Version"
    '-d'; "AppFilesGuid=$AppFilesGuid"
    '-d'; "AppIcon=$AppIcon"
    '-intermediatefolder'; (Join-Path $objDir 'wix')
    '-o'; $msiPath
)
Write-Host "==> wix build -> $msiPath"
$build = Invoke-Wix $buildArgs
Write-Host $build.Output.TrimEnd()
if ($build.ExitCode -ne 0) { throw "wix build 失败（exit=$($build.ExitCode)）" }
if (-not (Test-Path -LiteralPath $msiPath)) { throw "wix build 返回成功但未产出 $msiPath" }

# ---------- ICE 校验 ----------
# wix build 不跑 ICE，必须单独 validate；否则 per-user 打包最常见的 ICE38/57/64 回归无人拦截。
if (-not $SkipValidate) {
    Write-Host "==> wix msi validate"
    $validation = Invoke-Wix @('msi'; 'validate'; $msiPath)
    $lines = @($validation.Output -split "`r?`n" | Where-Object { $_ -match 'WIX\d+' })
    foreach ($line in $lines) { Write-Host "    $line" }
    $errors = @($lines | Where-Object { $_ -match 'error WIX\d+' })
    if ($errors -or $validation.ExitCode -ne 0) {
        throw "MSI ICE 校验未通过（$($errors.Count) 条 error）"
    }
}

$size = (Get-Item -LiteralPath $msiPath).Length
Write-Host ("==> 完成：{0}（{1:N1} MB，内含 {2} 个文件、发布目录 {3:N1} MB）" -f `
    $msiPath, ($size / 1MB), $files.Count, (
        ($files | Measure-Object Length -Sum).Sum / 1MB))
