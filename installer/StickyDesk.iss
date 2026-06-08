; StickyDesk installer (Inno Setup 6). Wraps the self-contained publish output into a single
; per-user setup.exe — no admin required (installs under %LOCALAPPDATA%\Programs).
; Build with scripts\build-installer.ps1 (publishes first, then compiles this script).

#define MyAppName "StickyDesk"
#define MyAppExe "StickyDesk.App.exe"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAumid "StickyDesk.App"

[Setup]
AppId={{9F3C7E21-7B4A-4E2C-9B2A-7C1D5E8A4F10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=StickyDesk
DefaultDirName={localappdata}\Programs\StickyDesk
DefaultGroupName=StickyDesk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=StickyDeskSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExe}
; Self-contained build runs everywhere; min OS Win10 2004.
MinVersion=10.0.19041

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startup"; Description: "Start StickyDesk when I sign in"; GroupDescription: "Startup:"

[Files]
; The entire self-contained publish folder.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; Ship the secrets template, and seed a writable .env in the app folder if one isn't there yet.
Source: "..\.env.example"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\.env.example"; DestDir: "{app}"; DestName: ".env"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\StickyDesk"; Filename: "{app}\{#MyAppExe}"; AppUserModelID: "{#MyAumid}"
Name: "{autodesktop}\StickyDesk"; Filename: "{app}\{#MyAppExe}"; AppUserModelID: "{#MyAumid}"; Tasks: desktopicon

[Registry]
; Optional launch-at-login (per-user), removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StickyDesk"; ValueData: """{app}\{#MyAppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch StickyDesk"; Flags: nowait postinstall skipifsilent
