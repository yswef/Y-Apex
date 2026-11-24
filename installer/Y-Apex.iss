; © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
#define AppName "Y-Apex"
#define AppVersion "0.1.0"
#define AppPublisher "ENG.YSWEF"
#define AppId "{f35b5e2b-c6b4-4ebc-a965-4d27a2402f53}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\YSWEF\Y-Apex
DefaultGroupName=Y-Apex
OutputDir=..\out\installer
OutputBaseFilename=Y-Apex-Setup
SetupIconFile=..\assets\icons\generated\app.ico
UninstallDisplayIcon={app}\ui\YApex.UI.exe
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
ChangesAssociations=no
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Dirs]
Name: "{app}\ui"
Name: "{app}\service"
Name: "{app}\cli"
Name: "{commonappdata}\YSWEF\Y-Apex"; Permissions: system-full admins-full

[Files]
Source: "..\out\portable\win-x64\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\out\portable\win-x64\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\out\portable\win-x64\cli\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\config\features.json"; DestDir: "{commonappdata}\YSWEF\Y-Apex"; DestName: "features.json"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{group}\Y-Apex"; Filename: "{app}\ui\YApex.UI.exe"; WorkingDir: "{app}\ui"
Name: "{autodesktop}\Y-Apex"; Filename: "{app}\ui\YApex.UI.exe"; WorkingDir: "{app}\ui"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\cli\YApex.Cli.exe"; Parameters: "register-pipe-user --interactive"; StatusMsg: "Registering the signed-in desktop account for the local service pipe..."; Flags: runhidden waituntilterminated runascurrentuser
Filename: "{app}\service\YApex.Service.exe"; Parameters: "--install-service"; StatusMsg: "Installing and starting the Y-Apex service..."; Flags: runhidden waituntilterminated
Filename: "{app}\ui\YApex.UI.exe"; Description: "Launch Y-Apex"; WorkingDir: "{app}\ui"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\YSWEF\Y-Apex"; Check: ShouldRemoveManagedRules
Type: filesandordirs; Name: "{userappdata}\YSWEF\Y-Apex"

[Code]
var
  RemoveManagedRules: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  NeedsRestart := False;
  if FileExists(ExpandConstant('{app}\service\YApex.Service.exe')) then
  begin
    if not Exec(
      ExpandConstant('{app}\service\YApex.Service.exe'),
      '--uninstall-service',
      ExpandConstant('{app}\service'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
    begin
      Result := 'The existing Y-Apex service could not be launched for shutdown. Setup will not replace its files.';
    end
    else if ResultCode <> 0 then
    begin
      Result := 'The existing Y-Apex service could not be stopped and removed before updating. Setup will not replace its files.';
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  if UninstallSilent then
  begin
    RemoveManagedRules := False;
  end
  else
  begin
    RemoveManagedRules := MsgBox(
      'Remove all firewall rules managed by Y-Apex? Choose No to keep the rules and service data for manual cleanup.',
      mbConfirmation,
      MB_YESNO) = IDYES;
  end;
  Result := True;
end;

procedure RunServiceCleanupCommand(const ServiceExe, Parameters, FailureMessage: String);
var
  ResultCode: Integer;
begin
  if not Exec(ServiceExe, Parameters, ExpandConstant('{app}\service'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox(FailureMessage, mbError, MB_OK);
    Abort;
  end;
  if ResultCode <> 0 then
  begin
    MsgBox(FailureMessage, mbError, MB_OK);
    Abort;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ServiceExe: String;
begin
  if CurUninstallStep <> usUninstall then
  begin
    Exit;
  end;

  ServiceExe := ExpandConstant('{app}\service\YApex.Service.exe');
  if not FileExists(ServiceExe) then
  begin
    MsgBox('The Y-Apex service helper is missing. Uninstallation is cancelled to avoid leaving service data or firewall rules behind.', mbError, MB_OK);
    Abort;
  end;

  RunServiceCleanupCommand(ServiceExe, '--uninstall-service', 'The Y-Apex service could not be stopped and removed. Uninstallation is cancelled.');
  if RemoveManagedRules then
  begin
    RunServiceCleanupCommand(ServiceExe, '--remove-owned-rules-confirmed', 'The Y-Apex firewall rules could not all be removed. Uninstallation is cancelled so service data is preserved.');
  end;
end;

function ShouldRemoveManagedRules(): Boolean;
begin
  Result := RemoveManagedRules;
end;
