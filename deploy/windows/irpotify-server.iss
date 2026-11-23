; Script for irpotify Server for Windows Setup
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
VersionInfoDescription=Complete Self-Hosted Server for Windows (irpotify)

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut for irpotify Server"; GroupDescription: "Additional icons:"
Name: "runsetup"; Description: "Automatically configure and start the server at the end of installation"; GroupDescription: "Configuration:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}\irpotify Server"; Filename: "{app}\dashboard.bat"
Name: "{autoprograms}\{#MyAppName}\Start Server"; Filename: "{app}\start.bat"
Name: "{autoprograms}\{#MyAppName}\Stop Server"; Filename: "{app}\stop.bat"
Name: "{autoprograms}\{#MyAppName}\Server Status"; Filename: "{app}\status.bat"
Name: "{autoprograms}\{#MyAppName}\Server Credentials"; Filename: "{app}\credentials.bat"
Name: "{autoprograms}\{#MyAppName}\Setup & Reconfigure"; Filename: "{app}\install.bat"
Name: "{autodesktop}\irpotify Server"; Filename: "{app}\dashboard.bat"; Tasks: desktopicon

[Run]
Filename: "{app}\install.bat"; Parameters: "-ApiPort {code:GetPort}"; Description: "Initialize and start the server services now"; Flags: nowait postinstall skipifsilent; Tasks: runsetup

[Code]
var
  PortPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  PortPage := CreateInputQueryPage(wpSelectDir,
    'Server Port Configuration',
    'Choose the listening port for irpotify Server',
    'Specify the port number on which the Web Dashboard and API will listen (default is 5080):');
  PortPage.Add('Listening Port (1024 - 65535):', False);
  PortPage.Values[0] := '5080';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  PortNum: Integer;
begin
  Result := True;
  if CurPageID = PortPage.ID then
  begin
    PortNum := StrToIntDef(Trim(PortPage.Values[0]), 0);
    if (PortNum < 1024) or (PortNum > 65535) then
    begin
      MsgBox('Please enter a valid TCP port number between 1024 and 65535.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function GetPort(Param: String): String;
var
  P: String;
begin
  P := Trim(PortPage.Values[0]);
  if P = '' then P := '5080';
  Result := P;
end;
