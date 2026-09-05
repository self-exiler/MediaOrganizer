; MediaOrganizer 桌面版 — Inno Setup 安装包脚本（Velopack 之外的备选路线）
;
; 前置：winget install -e --id JRSoftware.InnoSetup   （需 6.3+，用到内置 DownloadTemporaryFile）
; 编译：ISCC.exe packaging\MediaOrganizer.iss
; 先跑：packaging\publish.ps1
;
; 设计要点：与 Velopack 一样保持“运行时外置”——不打包 .NET Runtime，
;           安装时检测 Microsoft.NETCore.App 10.x，缺失则在线下载静默安装。

#define MyAppName      "MediaOrganizer"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "dioha"
#define MyAppExeName   "MediaOrganizer.Desktop.exe"
#define MyAppId        "{7E4C1B52-3A9D-4E58-9C1F-6D2A8B70F431}"
; 只需 .NET Runtime（Microsoft.NETCore.App），Avalonia 不依赖 WindowsDesktop Runtime
#define DotNetRuntimeUrl "https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=releases
OutputBaseFilename={#MyAppName}_Setup_{#MyAppVersion}
SetupIconFile=..\src\MediaOrganizer.Desktop\Assets\app-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
; 降到 lowest 可以让用户装到非 Program Files 目录而无须提权
PrivilegesRequired=lowest

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加选项:"; Flags: unchecked

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; \
    Flags: runhidden; RunOnceId: "KillProcess"

[Code]
const
  DotNetRegKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App';

{ 检测是否装了任意 10.x 的 Microsoft.NETCore.App }
function IsDotNet10RuntimeInstalled(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, DotNetRegKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      { 值名形如 10.0.8；命中 10. 前缀即视为满足 net10.0 }
      if Pos('10.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

{ 下载进度回调，需定义在 PrepareToInstall 之前（Pascal Script 要求先声明后使用） }
procedure DownloadProgress(const Url, Filename: String; const Progress, ProgressMax: Int64);
begin
  if ProgressMax > 0 then
    WizardForm.PreparingLabel.Caption :=
      Format('正在下载 .NET 10 运行时…  %d%%', [Progress * 100 div ProgressMax]);
end;

{ 缺失运行时时，在文件复制前下载并静默安装 .NET 10 Runtime }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstallerPath: String;
  ResultCode: Integer;
begin
  Result := '';
  if IsDotNet10RuntimeInstalled() then
    Exit;

  if WizardSilent() then
  begin
    Result := FmtMessage('未检测到 .NET 10 运行时，请先安装：%1', ['{#DotNetRuntimeUrl}']);
    Exit;
  end;

  WizardForm.PreparingLabel.Caption := '正在下载 .NET 10 运行时…';
  WizardForm.PreparingLabel.Visible := True;

  try
    InstallerPath := DownloadTemporaryFile('{#DotNetRuntimeUrl}', 'dotnet-runtime-10-win-x64.exe', '', @DownloadProgress);
  except
    Result := '下载 .NET 10 运行时失败，请检查网络后重试，或手动安装：' + '{#DotNetRuntimeUrl}';
    Exit;
  end;

  WizardForm.PreparingLabel.Caption := '正在安装 .NET 10 运行时…';
  if not Exec(InstallerPath, '/install /quiet /norestart', '', SW_HIDE,
              ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '安装 .NET 10 运行时失败：' + SysErrorMessage(ResultCode);
    Exit;
  end;

  if not IsDotNet10RuntimeInstalled() then
    Result := '.NET 10 运行时安装完成后仍检测不到，请重启后重试。';
end;
