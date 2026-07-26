; Inno Setup script for Umnatha Network Monitor
; Packages the self-contained publish output (which already bundles the .NET 10
; runtime AND the Windows App SDK runtime) into a single setup.exe.
;
; Build with:  Installer\build-installer.ps1
; or manually: ISCC.exe /DMyPublishDir="<path-to-publish>" /DMyAppVersion=1.0.0 NetworkMonitor.iss
;
; Requires Inno Setup 6 (https://jrsoftware.org/isdl.php).

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef MyPublishDir
  #define MyPublishDir "..\NetworkMonitor\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

#define MyAppName "Umnatha Network Monitor"
#define MyAppPublisher "Jazzz Software"
#define MyAppCopyright "© Jazzz Software 2026"
#define MyAppExeName "NetworkMonitor.exe"

[Setup]
AppId={{7074c3a8-a61b-4e4a-9e6c-dedc9a62ae94}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\NetworkMonitor\Assets\app.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
OutputDir=Output
OutputBaseFilename=Umnatha Network Monitor v{#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "runstartup"; Description: "Run automatically at Windows startup (no UAC prompt)"; GroupDescription: "Startup:"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{sys}\schtasks.exe"; Parameters: "/create /tn ""{#MyAppName}"" /tr ""\""{app}\{#MyAppExeName}\"" --minimized"" /sc onlogon /rl highest /f"; Flags: runhidden; Tasks: runstartup
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; Silent runs come from the in-app updater, which exited the app to unlock its files — relaunch it.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--minimized"; Flags: nowait skipifnotsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""{#MyAppName}"" /f"; Flags: runhidden; RunOnceId: "DelUmnathaStartupTask"
