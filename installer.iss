; VolumeOSD installer — build with Inno Setup 6:
;   iscc "installer.iss"
; Output: dist\VolumeOSD-Setup-1.0.0.exe
; Per-user install (no admin): Start Menu shortcut, optional desktop icon,
; optional launch at finish. The app registers its own login autostart on
; first run; uninstall removes that registry value.

#define MyAppName "VolumeOSD"
#define MyAppVersion "1.1.0"
#define MyAppExeName "VolumeOSD.exe"

[Setup]
AppId={{c3cd5038-dff6-4812-8c68-b1e7bbe2faa9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
ArchitecturesAllowed=x64
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
DisableProgramGroupPage=yes

[Files]
Source: "dist\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "startup"; Description: "Start {#MyAppName} with Windows"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Registry]
; Login autostart (default on). The app keeps this entry in sync on every
; launch; uninstall removes it either way.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
