; VectorPilot installer (Inno Setup 6)
; Build: ISCC.exe installer.iss
; Produces a signed-ready Windows installer with .shoppilot file association.

#define MyAppName "VectorPilot"
#define MyAppVersion "0.3.0"
#define MyAppPublisher "0xzgbot"
; The App project's assembly name is "VectorPilot" — the publish output is VectorPilot.exe.
#define MyAppExeName "VectorPilot.exe"

[Setup]
AppId={{9E4B2F1C-5D2A-4E7B-9C3F-4B2A7E5D1C09}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\VectorPilot
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\dist
OutputBaseFilename=VectorPilot-{#MyAppVersion}-setup
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Published WPF app (dotnet publish -c Release -r win-x64 --self-contained)
Source: "..\src\VectorPilot.App\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; .shoppilot package association
Root: HKA; Subkey: "Software\Classes\.shoppilot"; ValueType: string; ValueName: ""; ValueData: "VectorPilot.Document"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\VectorPilot.Document"; ValueType: string; ValueName: ""; ValueData: "VectorPilot Job"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\VectorPilot.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\VectorPilot.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
