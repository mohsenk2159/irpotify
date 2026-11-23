; Script for irpotify Windows Server Setup
#define MyAppName "irpotify Server"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "irpotify"
#define MyAppURL "https://github.com/mohsenk2159/irpotify"
#define SourceDir "C:\Users\MohsenKarami\AppData\Local\Temp\MusicHubServer-win-x64-1.0.0"

[Setup]
AppId={{E61A5C1B-61A2-4091-B8AE-9B28A2534571}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName=C:\irpotify-server
DisableProgramGroupPage=yes
OutputBaseFilename=irpotify-server-win-x64-1.0.0-setup
OutputDir=..\..\artifacts\installers
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Complete Self-Hosted Windows Server for irpotify

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut for Web Dashboard"; GroupDescription: "Additional icons:"
Name: "runsetup"; Description: "Automatically configure and start the server at the end of installation"; GroupDescription: "Configuration:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}\Web Dashboard"; Filename: "{app}\dashboard.bat"
Name: "{autoprograms}\{#MyAppName}\Start Server"; Filename: "{app}\start.bat"
Name: "{autoprograms}\{#MyAppName}\Stop Server"; Filename: "{app}\stop.bat"
Name: "{autoprograms}\{#MyAppName}\Server Status"; Filename: "{app}\status.bat"
Name: "{autoprograms}\{#MyAppName}\Server Credentials"; Filename: "{app}\credentials.bat"
Name: "{autoprograms}\{#MyAppName}\Setup & Reconfigure"; Filename: "{app}\install.bat"
Name: "{autodesktop}\irpotify Web Dashboard"; Filename: "{app}\dashboard.bat"; Tasks: desktopicon

[Run]
Filename: "{app}\install.bat"; Parameters: "-NonInteractive"; Description: "Initialize and start the server services now"; Flags: nowait postinstall skipifsilent; Tasks: runsetup
