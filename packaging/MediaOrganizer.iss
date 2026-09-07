; MediaOrganizer 桌面版 — Inno Setup 安装包脚本
;
; 前置：winget install -e --id JRSoftware.InnoSetup   （6.3+）
; 编译：ISCC.exe packaging\MediaOrganizer.iss
; 先跑：packaging\publish.ps1
;
; 设计要点：发布为自包含（publish.ps1 内嵌 .NET Runtime + ReadyToRun），
;           安装器不做任何运行时检测/下载，离线可装。
;           默认安装到当前用户目录（{localappdata}\Programs），以 lowest 权限安装，
;           全程无需管理员提权。

#define MyAppName      "MediaOrganizer"
; 版本号可由 CI 用 /DMyAppVersion=1.2.3 覆盖（tag v1.2.3 → 传 1.2.3，不带前导 v）
#ifndef MyAppVersion
#define MyAppVersion   "1.1.0"
#endif
#define MyAppPublisher "dioha"
#define MyAppExeName   "MediaOrganizer.Desktop.exe"
#define MyAppId        "{7E4C1B52-3A9D-4E58-9C1F-6D2A8B70F431}"

[Setup]
AppId={{7E4C1B52-3A9D-4E58-9C1F-6D2A8B70F431}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 默认装到用户目录下（%LocalAppData%\Programs\MediaOrganizer），
; 用户仍可在向导里自行更换
DefaultDirName={localappdata}\Programs\{#MyAppName}
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
; 自包含 + 用户目录安装，全程不需要提权
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

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
