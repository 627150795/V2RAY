#define AppName "多客户端节点监控"
#define AppVersion "0.4.3"
#define AppExe "ProxyMonitor.exe"

[Setup]
AppId={{A1AE9C45-CCDB-43FB-A233-EAB66679B626}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\ProxyMonitor
DefaultGroupName={#AppName}
OutputDir=artifacts
OutputBaseFilename=ProxyMonitor-Setup-0.4.3
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExe}

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ProxyMonitor"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent
