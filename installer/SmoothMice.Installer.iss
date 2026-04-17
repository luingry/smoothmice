; Inno Setup 6 — compile com ISCC.exe (instala Inno Setup 6 se ainda não tiveres).
; O script assume que fizeste publish antes (ver build-installer.ps1).

#define MyAppName "SmoothMice"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "SmoothMice"
#define MyAppExeName "SmoothMice.exe"
; Pasta relativa a este ficheiro (installer\)
#define PublishDir "..\src\SmoothMice.App\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{B5F3C2A1-4D6E-4F90-9A1B-2C3D4E5F6078}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=SmoothMice_Setup_{#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start SmoothMice when Windows starts"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
; Single-file publish: only the main exe (avoids shipping *.pdb from referenced projects)
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmoothMice"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup
