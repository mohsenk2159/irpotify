; Script generated for irpotify Player Windows Setup
#define MyAppName "irpotify Player"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "irpotify"
#define MyAppURL "https://github.com/mohsenk2159/irpotify"
#define MyAppExeName "MusicHub.Player.exe"
#define SourceDir "..\..\artifacts\publish\player-win-x64"

[Setup]
AppId={{9B78D826-64A1-4ED3-9C14-2C62C04D81B2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\irpotify Player
DisableProgramGroupPage=yes
OutputBaseFilename=irpotify-player-win-x64-1.0.0-setup
OutputDir=..\..\artifacts\installers
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Native Windows Player for irpotify

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
