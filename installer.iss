#define AppName "多客户端节点监控"
#define AppVersion "0.4.8"
#define AppExe "ProxyMonitor.exe"

[Setup]
AppId={{A1AE9C45-CCDB-43FB-A233-EAB66679B626}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\ProxyMonitor
DefaultGroupName={#AppName}
OutputDir=artifacts
OutputBaseFilename=ProxyMonitor-Setup-0.4.8
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=assets\ProxyMonitor.ico
UninstallDisplayIcon={app}\ProxyMonitor.ico

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\ProxyMonitor.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\ProxyMonitor.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ProxyMonitor"; ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent
