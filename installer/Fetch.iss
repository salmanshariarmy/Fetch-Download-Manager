#define AppName "Fetch"
#define AppVersion "0.6.0"
#define AppPublisher "Fetch contributors"
#define AppExe "Fetch.Desktop.exe"

[Setup]
AppId={{70193B0E-D1CE-44C3-94B7-11268D93FB40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName=Fetch {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Fetch
DefaultGroupName=Fetch
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\artifacts\installer
OutputBaseFilename=FetchSetup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Fetch.Desktop\Assets\fetch.ico
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\native\*"; DestDir: "{app}\native"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\browser-extension\*"; DestDir: "{app}\browser-extension"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\Register-FetchBrowser.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\Install-MediaTools.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Fetch"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Fetch"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start Fetch"; Flags: nowait postinstall skipifsilent
