; Inno Setup script of the Golether rendezvous server: installs it for the whole computer (administrator rights),
; registers the Windows service GoletherServer that starts with the computer and restarts after a failure, and allows
; the program in the Windows firewall. config\appsettings.json survives updates: the installer writes it only once.
; Layout: golether-server.exe (the launcher) in the root, bin, config, docs, and uninstall for the uninstaller.
; Build it with Scripts/build-server-installer.ps1, which passes the version and the source folder.

#ifndef ServerVersion
  #define ServerVersion "0.1.0"
#endif
#ifndef ServerSource
  #define ServerSource "..\artifacts\publish\win-x64\GoletherServer"
#endif
#ifndef ServerOutputDir
  #define ServerOutputDir "..\artifacts\publish"
#endif

#define ServiceName "GoletherServer"
#define FirewallRule "Golether rendezvous server"

[Setup]
AppId={{E3A9D4F1-6B2C-4F7E-8C41-2D5B9A7C3E62}
AppName=Golether Server
AppVersion={#ServerVersion}
AppPublisher=Golether
AppCopyright=GPL-3.0-or-later
VersionInfoVersion={#ServerVersion}
DefaultDirName={autopf}\Golether Server
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SourcePath}\..\Sources\Apps\Golether.Web.Server\Assets\golether-server.ico
UninstallDisplayIcon={app}\golether-server.exe
; The root keeps only the launcher and the folders.
UninstallFilesDir={app}\uninstall
OutputDir={#ServerOutputDir}
OutputBaseFilename=GoletherServer-{#ServerVersion}-setup
InfoAfterFile={#ServerSource}\docs\README.txt

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "service"; Description: "Запускать как службу Windows вместе с компьютером"; GroupDescription: "Служба:"
Name: "firewall"; Description: "Разрешить входящие подключения в брандмауэре Windows"; GroupDescription: "Сеть:"

[Files]
Source: "{#ServerSource}\*"; DestDir: "{app}"; Excludes: "\config\appsettings.json"; Flags: recursesubdirs createallsubdirs ignoreversion
; The settings of the administrator are kept on update.
Source: "{#ServerSource}\config\appsettings.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist

[Icons]
Name: "{autoprograms}\Golether Server\Настройки сервера (appsettings.json)"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\config\appsettings.json"""
Name: "{autoprograms}\Golether Server\Руководство администратора"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\docs\ADMIN-GUIDE.md"""
Name: "{autoprograms}\Golether Server\Состояние сервера"; Filename: "http://127.0.0.1:47980/health"

[Run]
Filename: "{sys}\notepad.exe"; Parameters: """{app}\docs\ADMIN-GUIDE.md"""; Description: "Открыть руководство администратора"; Flags: nowait postinstall skipifsilent
Filename: "http://127.0.0.1:47980/health"; Description: "Открыть страницу состояния сервера"; Flags: shellexec postinstall skipifsilent unchecked; Tasks: service

[Code]
const
  ServiceName = '{#ServiceName}';
  FirewallRule = '{#FirewallRule}';

{ Runs a system tool hidden and returns its exit code. }
function RunTool(const Tool, Parameters: string): Integer;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\') + Tool, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

{ Stops the service and makes sure the program has let go of its files. }
procedure StopServer();
begin
  RunTool('sc.exe', 'stop ' + ServiceName);
  Sleep(3000);
  RunTool('taskkill.exe', '/f /im golether-server.exe');
end;

{ The path of the service with quotes: an unquoted path with spaces could start another program. }
function ServiceBinPath(): string;
begin
  Result := 'binPath= "\"' + ExpandConstant('{app}') + '\golether-server.exe\""';
end;

procedure RegisterService();
begin
  if RunTool('sc.exe', 'create ' + ServiceName + ' ' + ServiceBinPath() + ' start= auto DisplayName= "Golether rendezvous server"') <> 0 then
    { The service exists after an earlier installation: point it at this folder. }
    RunTool('sc.exe', 'config ' + ServiceName + ' ' + ServiceBinPath() + ' start= auto');
  RunTool('sc.exe', 'description ' + ServiceName + ' "Introduces Golether hosts and participants behind routers; the sessions go directly between them."');
  RunTool('sc.exe', 'failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/5000/restart/60000');
  RunTool('sc.exe', 'start ' + ServiceName);
end;

procedure RemoveService();
begin
  RunTool('sc.exe', 'stop ' + ServiceName);
  Sleep(2000);
  RunTool('sc.exe', 'delete ' + ServiceName);
end;

procedure AllowFirewall();
begin
  RunTool('netsh.exe', 'advfirewall firewall delete rule name="' + FirewallRule + '"');
  RunTool('netsh.exe', 'advfirewall firewall add rule name="' + FirewallRule + '" dir=in action=allow profile=any enable=yes program="'
    + ExpandConstant('{app}') + '\golether-server.exe"');
end;

procedure RemoveFirewall();
begin
  RunTool('netsh.exe', 'advfirewall firewall delete rule name="' + FirewallRule + '"');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServer();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('firewall') then
      AllowFirewall()
    else
      RemoveFirewall();

    if WizardIsTaskSelected('service') then
      RegisterService()
    else
      RemoveService();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveService();
    RunTool('taskkill.exe', '/f /im golether-server.exe');
    RemoveFirewall();
  end;
end;
