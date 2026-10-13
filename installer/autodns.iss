#define MyAppName "AutoDNS"
#define MyAppVersion "2.1.7"
#define MyAppPublisher "AutoDNS"
#define MyAppExeName "AutoDNS.exe"

[Setup]
AppId={{A7C4E91B-2D18-4F6A-9C33-8B0E5D1A7F24}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
UsePreviousAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=AutoDNS-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=force
SetupIconFile=..\src\AutoDNS\Assets\app.ico
MinVersion=10.0
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install"; Flags: runhidden waituntilterminated
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""AutoDNS.Run"""; Flags: runhidden nowait

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall"; RunOnceId: "AutoDNSCleanup"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\AutoDNS"
Type: filesandordirs; Name: "{commonappdata}\AutoHosts"
