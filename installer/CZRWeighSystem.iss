; 精锐计量称重软件 安装包脚本（Inno Setup 6）
; 编译前请先执行发布（仓库根 publish.ps1，或 publish.ps1 -Installer 一步到位）。
; 说明：安装目录默认在系统盘根目录（如 C:\精锐计量称重软件），不装进 Program Files——
;       程序把 config.json / logs / data / captures / backups 都写在 exe 同目录下，
;       装在 Program Files 会导致现场非管理员账号无写权限，无法过磅。

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "精锐计量称重软件"
#define ExeName "CZRWeighSystem.exe"
#define PublishDir "..\src\CZRWeighSystem\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{B7B1A2C4-8E3D-4C9A-9E2F-2F5C6D7E8A91}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
DefaultDirName={sd}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename={#AppName}_安装包_V{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 需要写系统盘根目录并给安装目录授权，故要求管理员权限
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#ExeName}
; 安装包自身的图标（与软件 exe 同一份 app.ico）
SetupIconFile=..\src\CZRWeighSystem\UI\Resources\app.ico
; 安装/升级前提示关闭正在运行的软件，避免文件被占用
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; 整包发布目录（含 libvlc\win-x64 原生解码库，必须随行）
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; 运行数据写在 exe 目录，授予 Users 组修改权限，否则非管理员账号无法保存称重记录
Name: "{app}"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; 卸载时只删除安装包释放的文件；运行期生成的 config.json、logs、data（称重数据库）、
; captures（抓拍图片）、backups 默认保留，避免误删现场数据。