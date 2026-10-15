; Inno Setup script of the Golether rendezvous server: installs it for the whole computer (administrator rights),
; registers the Windows service GoletherServer that starts with the computer and restarts after a failure, and allows
; the program in the Windows firewall. config\appsettings.json survives updates: the installer writes it only once.
; Layout: golether-server.exe and golether-server-monitor.exe (the launchers) in the root, bin, config, docs, monitor
; (Golether Server Monitor: the state, the journal of events, start and stop, the settings), and uninstall for the
; uninstaller.
; Build it with Scripts/Server/build-server-installer.ps1, which passes the version and the source folder.

#ifndef ServerVersion
  #define ServerVersion "0.1.0"
#endif
; The page of the project (PackageProjectUrl of Directory.Build.props): the link of the program in the list of
; installed apps.
#ifndef ServerUrl
  #define ServerUrl "https://depthrel.github.io/Golether"
#endif
#ifndef ServerSource
  #define ServerSource "..\..\artifacts\publish\win-x64\GoletherServer"
#endif
#ifndef ServerOutputDir
  #define ServerOutputDir "..\..\artifacts\publish"
#endif

; The pictures of the wizard, drawn by Scripts/Assets/generate-installer-art.ps1 (build-server-installer.ps1 runs it).
#ifndef InstallerArt
  #define InstallerArt SourcePath + "..\..\artifacts\installer-art"
#endif

#define ServiceName "GoletherServer"
#define FirewallRule "Golether rendezvous server"

[Setup]
AppId={{E3A9D4F1-6B2C-4F7E-8C41-2D5B9A7C3E62}
AppName=Golether Server
AppVersion={#ServerVersion}
AppPublisher=Golether
AppPublisherURL={#ServerUrl}
AppSupportURL={#ServerUrl}
AppUpdatesURL={#ServerUrl}
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
SetupIconFile={#SourcePath}\..\..\Sources\Apps\Golether.Web.Server\Assets\golether-server.ico
UninstallDisplayIcon={app}\golether-server.exe
; The logo instead of the stock box; without the pictures (the script was not run) the stock ones are used.
#if FileExists(InstallerArt + "\server-wizard.bmp")
WizardImageFile={#InstallerArt}\server-wizard.bmp
WizardSmallImageFile={#InstallerArt}\server-wizard-small.bmp
#endif
; The wizard opens with a greeting that shows the logo, not with the choice of a folder.
DisableWelcomePage=no
; The language is asked first, with the language of Windows preselected; a Windows in a language the wizard does not
; speak gets English, the first in [Languages].
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
; The root keeps only the launcher and the folders.
UninstallFilesDir={app}\uninstall
OutputDir={#ServerOutputDir}
OutputBaseFilename=GoletherServer-{#ServerVersion}-setup

[Languages]
; The first language is the one offered by default. A new language is one more line here and one more block in
; [Messages] and [CustomMessages].
; The note shown after the installation comes in the language of the wizard (publish-server-win.ps1 writes them all
; from Scripts/Server/Notes).
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoAfterFile: "{#ServerSource}\docs\README.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; InfoAfterFile: "{#ServerSource}\docs\README.de.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoAfterFile: "{#ServerSource}\docs\README.es.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; InfoAfterFile: "{#ServerSource}\docs\README.fr.txt"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"; InfoAfterFile: "{#ServerSource}\docs\README.pt.txt"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; InfoAfterFile: "{#ServerSource}\docs\README.ru.txt"

[Messages]
; The greeting of the first page; [name] and [name/ver] are filled in by Inno Setup.
english.WelcomeLabel1=Welcome to the [name] setup
english.WelcomeLabel2=The rendezvous server introduces Golether hosts and their guests behind routers. The sessions themselves go directly between them and never pass through the server.%n%nThis wizard installs [name/ver] on your computer.
german.WelcomeLabel1=Willkommen bei der Installation von [name]
german.WelcomeLabel2=Der Vermittlungsserver stellt Golether-Gastgeber und ihre Gäste hinter Routern einander vor. Die Sitzungen selbst laufen direkt zwischen ihnen und nie über den Server.%n%nDieser Assistent installiert [name/ver] auf Ihrem Computer.
spanish.WelcomeLabel1=Te damos la bienvenida a la instalación de [name]
spanish.WelcomeLabel2=El servidor de encuentro presenta a los anfitriones de Golether y a sus invitados que están detrás de routers. Las sesiones van directamente entre ellos y nunca pasan por el servidor.%n%nEste asistente instalará [name/ver] en tu equipo.
french.WelcomeLabel1=Bienvenue dans l'installation de [name]
french.WelcomeLabel2=Le serveur de rendez-vous présente les hôtes Golether à leurs invités situés derrière des routeurs. Les séances elles-mêmes passent directement entre eux, jamais par le serveur.%n%nCet assistant va installer [name/ver] sur votre ordinateur.
portuguese.WelcomeLabel1=Bem-vindo à instalação do [name]
portuguese.WelcomeLabel2=O servidor de encontro apresenta os anfitriões do Golether aos convidados que estão atrás de routers. As sessões em si decorrem diretamente entre eles e nunca passam pelo servidor.%n%nEste assistente vai instalar o [name/ver] no seu computador.
russian.WelcomeLabel1=Добро пожаловать в установку [name]
russian.WelcomeLabel2=Сервер рукопожатий знакомит ведущих Golether с гостями, которые сидят за роутерами. Сами сеансы идут напрямую между ними и через сервер не проходят.%n%nМастер установит [name/ver] на ваш компьютер.

[CustomMessages]
; Everything the wizard says beyond the standard messages of Inno Setup.
english.TaskService=Run as a Windows service that starts with the computer
english.TaskServiceGroup=Service:
english.TaskFirewall=Allow incoming connections in the Windows firewall
english.TaskFirewallGroup=Network:
english.IconSettings=Server settings (appsettings.json)
english.IconGuide=Administrator guide
english.IconHealth=Server status
english.IconMonitor=Golether Server Monitor
english.RunMonitor=Open Golether Server Monitor
english.RunGuide=Open the administrator guide
english.RunHealth=Open the server status page
english.GuideFile=ADMIN-GUIDE.md
german.TaskService=Als Windows-Dienst mit dem Computer starten
german.TaskServiceGroup=Dienst:
german.TaskFirewall=Eingehende Verbindungen in der Windows-Firewall zulassen
german.TaskFirewallGroup=Netzwerk:
german.IconSettings=Servereinstellungen (appsettings.json)
german.IconGuide=Administratorhandbuch
german.IconHealth=Serverstatus
german.IconMonitor=Golether Server Monitor
german.RunMonitor=Golether Server Monitor öffnen
german.RunGuide=Administratorhandbuch öffnen
german.RunHealth=Statusseite des Servers öffnen
german.GuideFile=ADMIN-GUIDE.de.md
spanish.TaskService=Ejecutar como servicio de Windows al iniciar el equipo
spanish.TaskServiceGroup=Servicio:
spanish.TaskFirewall=Permitir las conexiones entrantes en el firewall de Windows
spanish.TaskFirewallGroup=Red:
spanish.IconSettings=Ajustes del servidor (appsettings.json)
spanish.IconGuide=Guía del administrador
spanish.IconHealth=Estado del servidor
spanish.IconMonitor=Golether Server Monitor
spanish.RunMonitor=Abrir Golether Server Monitor
spanish.RunGuide=Abrir la guía del administrador
spanish.RunHealth=Abrir la página de estado del servidor
spanish.GuideFile=ADMIN-GUIDE.es.md
french.TaskService=Lancer comme service Windows au démarrage de l'ordinateur
french.TaskServiceGroup=Service :
french.TaskFirewall=Autoriser les connexions entrantes dans le pare-feu Windows
french.TaskFirewallGroup=Réseau :
french.IconSettings=Réglages du serveur (appsettings.json)
french.IconGuide=Guide de l'administrateur
french.IconHealth=État du serveur
french.IconMonitor=Golether Server Monitor
french.RunMonitor=Ouvrir Golether Server Monitor
french.RunGuide=Ouvrir le guide de l'administrateur
french.RunHealth=Ouvrir la page d'état du serveur
french.GuideFile=ADMIN-GUIDE.fr.md
portuguese.TaskService=Executar como serviço do Windows ao iniciar o computador
portuguese.TaskServiceGroup=Serviço:
portuguese.TaskFirewall=Permitir ligações de entrada na firewall do Windows
portuguese.TaskFirewallGroup=Rede:
portuguese.IconSettings=Definições do servidor (appsettings.json)
portuguese.IconGuide=Guia do administrador
portuguese.IconHealth=Estado do servidor
portuguese.IconMonitor=Golether Server Monitor
portuguese.RunMonitor=Abrir o Golether Server Monitor
portuguese.RunGuide=Abrir o guia do administrador
portuguese.RunHealth=Abrir a página de estado do servidor
portuguese.GuideFile=ADMIN-GUIDE.pt.md
russian.TaskService=Запускать как службу Windows вместе с компьютером
russian.TaskServiceGroup=Служба:
russian.TaskFirewall=Разрешить входящие подключения в брандмауэре Windows
russian.TaskFirewallGroup=Сеть:
russian.IconSettings=Настройки сервера (appsettings.json)
russian.IconGuide=Руководство администратора
russian.IconHealth=Состояние сервера
russian.IconMonitor=Golether Server Monitor
russian.RunMonitor=Открыть Golether Server Monitor
russian.RunGuide=Открыть руководство администратора
russian.RunHealth=Открыть страницу состояния сервера
russian.GuideFile=ADMIN-GUIDE.ru.md

[Tasks]
Name: "service"; Description: "{cm:TaskService}"; GroupDescription: "{cm:TaskServiceGroup}"
Name: "firewall"; Description: "{cm:TaskFirewall}"; GroupDescription: "{cm:TaskFirewallGroup}"

[Files]
Source: "{#ServerSource}\*"; DestDir: "{app}"; Excludes: "\config\appsettings.json,\config\monitor.json"; Flags: recursesubdirs createallsubdirs ignoreversion
; The settings of the administrator are kept on update.
Source: "{#ServerSource}\config\appsettings.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist
; The settings of the monitor (its language) are kept on update too, and every user may change them: the monitor is
; opened without administrator rights. The settings of the server stay writable by administrators only.
Source: "{#ServerSource}\config\monitor.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist; Permissions: users-modify

[Icons]
Name: "{autoprograms}\Golether Server\{cm:IconMonitor}"; Filename: "{app}\golether-server-monitor.exe"
Name: "{autoprograms}\Golether Server\{cm:IconSettings}"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\config\appsettings.json"""
Name: "{autoprograms}\Golether Server\{cm:IconGuide}"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\docs\{cm:GuideFile}"""
Name: "{autoprograms}\Golether Server\{cm:IconHealth}"; Filename: "http://127.0.0.1:47980/health"

[Run]
Filename: "{app}\golether-server-monitor.exe"; Description: "{cm:RunMonitor}"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{sys}\notepad.exe"; Parameters: """{app}\docs\{cm:GuideFile}"""; Description: "{cm:RunGuide}"; Flags: nowait postinstall skipifsilent
Filename: "http://127.0.0.1:47980/health"; Description: "{cm:RunHealth}"; Flags: shellexec postinstall skipifsilent unchecked; Tasks: service

[Code]
const
  ServiceName = '{#ServiceName}';
  FirewallRule = '{#FirewallRule}';

procedure InitializeWizard();
begin
  { On screens scaled above 100% the boxes of the lists are drawn larger while their distance from the edge stays
    the same, so the left side of a box is cut off. The distance is scaled with the rest of the wizard. }
  WizardForm.TasksList.Offset := ScaleX(8);
  WizardForm.RunList.Offset := ScaleX(8);
end;

{ Runs a system tool hidden and returns its exit code. }
function RunTool(const Tool, Parameters: string): Integer;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\') + Tool, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

{ Stops the service and makes sure the program and its monitor have let go of their files. }
procedure StopServer();
begin
  RunTool('sc.exe', 'stop ' + ServiceName);
  Sleep(3000);
  RunTool('taskkill.exe', '/f /im golether-server.exe');
  RunTool('taskkill.exe', '/f /im golether-server-monitor.exe');
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
    RunTool('taskkill.exe', '/f /im golether-server-monitor.exe');
    RemoveFirewall();
  end;
end;
