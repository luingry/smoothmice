; Inno Setup 6 — compile with ISCC.exe (install Inno Setup 6 if you do not have it yet).
; This script assumes you published first (see build-installer.ps1).

#define MyAppName "SmoothMice"
; MyAppVersion comes from ISCC /D (see build-installer.ps1; value = <Version> in Directory.Build.props).
#ifndef MyAppVersion
#error Define MyAppVersion: compile via installer\build-installer.ps1 or ISCC /DMyAppVersion=x.y.z
#endif
#define MyAppPublisher "SmoothMice"
; Stable name after install (shortcuts, Run, icons):
#define MyAppExeName "SmoothMice.exe"
; File produced by publish (AssemblyName = SmoothMice-{Version}); pass /DMyPublishedExe=...
#ifndef MyPublishedExe
#error Define MyPublishedExe (e.g. ISCC /DMyPublishedExe=SmoothMice-0.3.0.exe); see build-installer.ps1
#endif
; Folder relative to this file (installer\) — net48 publish without a RID
#define PublishDir "..\src\SmoothMice.App\bin\Release\net48\publish"
; Setup.exe / wizard icon (same .ico as the WPF app)
#define AppIcon "..\src\SmoothMice.App\SmoothMice.ico"

[Setup]
AppId={{B5F3C2A1-4D6E-4F90-9A1B-2C3D4E5F6078}
AppName={#MyAppName}
SetupIconFile={#AppIcon}
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
; Allow replacing SmoothMice.exe while a previous instance is exiting (OTA / silent upgrades).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start SmoothMice when Windows starts"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
; Main EXE (renamed to a fixed name for startup and OTA updates)
Source: "{#PublishDir}\{#MyPublishedExe}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion
; Dependent DLLs (app assemblies + NuGet: System.Text.Json, System.Memory, etc.)
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; .NET Framework configuration file (app.exe.config)
Source: "{#PublishDir}\*.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmoothMice"; ValueData: """{app}\{#MyAppExeName}"" /tray"; Flags: uninsdeletevalue; Tasks: startup
