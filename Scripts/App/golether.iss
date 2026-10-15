; Inno Setup script of Golether.
; A page of the wizard asks whom to install for:
;   "only for me" (the default) - %LOCALAPPDATA%\Programs\Golether, no administrator rights; all application data
;       (keys, database, components) stays in the "data" folder inside the installation;
;   "for all users" - C:\Program Files\Golether, asks for administrator rights once. Ordinary users cannot write
;       there, so the installer leaves the file golether.shared next to golether.install and every user keeps the
;       data in %LOCALAPPDATA%\Golether (see AppDataPaths). The application itself never needs administrator rights.
; Either way an update never touches personal data.
; The installer also teaches the system to open golether:// links (invitations clicked in a browser) with Golether.
; Build it with Scripts/App/build-installer.ps1, which passes the version and the source folder.

#ifndef GoletherVersion
  #define GoletherVersion "0.1.0"
#endif
; The page of the project (PackageProjectUrl of Directory.Build.props): the link of the program in the list of
; installed apps.
#ifndef GoletherUrl
  #define GoletherUrl "https://depthrel.github.io/Golether"
#endif
#ifndef GoletherSource
  #define GoletherSource "..\..\artifacts\publish\win-x64\Golether"
#endif
#ifndef GoletherOutputDir
  #define GoletherOutputDir "..\..\artifacts\publish"
#endif
; The pictures of the wizard, drawn by Scripts/Assets/generate-installer-art.ps1 (build-installer.ps1 runs it).
#ifndef InstallerArt
  #define InstallerArt SourcePath + "..\..\artifacts\installer-art"
#endif

[Setup]
AppId={{7C2B1E22-2F1B-4C5E-9A0C-7C9C7E3A5B10}
AppName=Golether
AppVersion={#GoletherVersion}
AppPublisher=Golether
AppPublisherURL={#GoletherUrl}
AppSupportURL={#GoletherUrl}
AppUpdatesURL={#GoletherUrl}
AppCopyright=GPL-3.0-or-later
VersionInfoVersion={#GoletherVersion}
; {autopf} is %LOCALAPPDATA%\Programs for one user and C:\Program Files for all users.
DefaultDirName={autopf}\Golether
DefaultGroupName=Golether
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
; The choice "only for me / for all users" is a page of the wizard (see [Code]), not a dialog before it. Setup cannot
; change its rights half way, so choosing "for all users" starts it again with /ALLUSERS, which this line permits.
; An update keeps the choice of the first installation.
PrivilegesRequiredOverridesAllowed=commandline
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#GoletherSource}\..\..\..\..\Sources\Apps\Golether.UI\Assets\golether.ico
UninstallDisplayIcon={app}\Golether.exe
; The logo instead of the stock box: a dark panel in the look of the application on the welcome page, the logo alone
; in the header of the others. Without the pictures (the script was not run) the stock ones are used.
#if FileExists(InstallerArt + "\golether-wizard.bmp")
WizardImageFile={#InstallerArt}\golether-wizard.bmp
WizardSmallImageFile={#InstallerArt}\golether-wizard-small.bmp
#endif
OutputDir={#GoletherOutputDir}
OutputBaseFilename=Golether-{#GoletherVersion}-setup
CloseApplications=yes
CloseApplicationsFilter=Golether.exe
; The wizard opens with a greeting, not with a question.
DisableWelcomePage=no
; The language is asked first, with the language of Windows preselected; a Windows in a language the wizard does not
; speak gets English, the first in [Languages].
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
; The link handler is registered below: the shell must learn about it at once.
ChangesAssociations=yes

[Languages]
; The first language is the one offered by default. A new language is one more line here and one more block in
; [Messages] and [CustomMessages].
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
; The greeting of the first page; [name] and [name/ver] are filled in by Inno Setup.
english.WelcomeLabel1=Welcome to the [name] setup
english.WelcomeLabel2=Golether is for watching films together: everyone sees the same frame at the same moment, with the faces and voices of friends beside it.%n%nThis wizard installs [name/ver] on your computer.
german.WelcomeLabel1=Willkommen bei der Installation von [name]
german.WelcomeLabel2=Golether ist für gemeinsame Filmabende: Alle sehen im selben Moment dasselbe Bild, und die Gesichter und Stimmen der Freunde sind gleich daneben.%n%nDieser Assistent installiert [name/ver] auf Ihrem Computer.
spanish.WelcomeLabel1=Te damos la bienvenida a la instalación de [name]
spanish.WelcomeLabel2=Golether sirve para ver películas juntos: todos ven el mismo fotograma en el mismo momento, con las caras y las voces de los amigos al lado.%n%nEste asistente instalará [name/ver] en tu equipo.
french.WelcomeLabel1=Bienvenue dans l'installation de [name]
french.WelcomeLabel2=Golether sert à regarder des films ensemble : tout le monde voit la même image au même moment, avec les visages et les voix des amis à côté.%n%nCet assistant va installer [name/ver] sur votre ordinateur.
portuguese.WelcomeLabel1=Bem-vindo à instalação do [name]
portuguese.WelcomeLabel2=O Golether serve para ver filmes em conjunto: todos veem a mesma imagem no mesmo momento, com as caras e as vozes dos amigos ao lado.%n%nEste assistente vai instalar o [name/ver] no seu computador.
russian.WelcomeLabel1=Добро пожаловать в установку приложения [name]
russian.WelcomeLabel2=Golether — это кино вместе: все видят один и тот же кадр в один и тот же момент, а лица и голоса друзей рядом.%n%nМастер установит [name/ver] на ваш компьютер.

[CustomMessages]
; Everything the wizard says beyond the standard messages of Inno Setup. %n is a line break, %1 a value.
english.DesktopIcon=Desktop shortcut
english.Extras=Extras:
english.Launch=Launch Golether
english.ModeTitle=Who to install Golether for
english.ModeQuestion=Who will use the program on this computer?
english.ModeHelp=“Only for me” installs the program into your profile and needs no administrator rights.%n“For everyone” installs it into Program Files: Windows asks for an administrator's permission once.
english.ModeOnlyMe=Only for me (%1)
english.ModeEveryone=For everyone
english.ModeRefused=Windows did not grant administrator rights (code %1), and Golether cannot be installed for everyone without them.%n%nRun the setup as administrator to install for everyone: right-click the setup file and choose “Run as administrator”. Or choose “Only for me”.
english.RemoveData=Remove the personal data of Golether as well (keys, settings, the contact list)?
english.RemoveDataShared=Only the data of the current user is removed. The data of the other users of this computer stays in their profiles.
english.RemoveDataKeep=Press “No” if you are going to install Golether again.
german.DesktopIcon=Verknüpfung auf dem Desktop
german.Extras=Zusätzlich:
german.Launch=Golether starten
german.ModeTitle=Für wen Golether installiert wird
german.ModeQuestion=Wer wird das Programm auf diesem Computer verwenden?
german.ModeHelp=„Nur für mich“ installiert das Programm in Ihr Profil und benötigt keine Administratorrechte.%n„Für alle“ installiert es nach Program Files: Windows fragt einmal nach der Erlaubnis eines Administrators.
german.ModeOnlyMe=Nur für mich (%1)
german.ModeEveryone=Für alle
german.ModeRefused=Windows hat keine Administratorrechte erteilt (Code %1), und ohne sie kann Golether nicht für alle installiert werden.%n%nStarten Sie die Installation als Administrator, um für alle zu installieren: Rechtsklick auf die Installationsdatei → „Als Administrator ausführen“. Oder wählen Sie „Nur für mich“.
german.RemoveData=Auch die persönlichen Daten von Golether entfernen (Schlüssel, Einstellungen, Kontaktliste)?
german.RemoveDataShared=Es werden nur die Daten des aktuellen Benutzers entfernt. Die Daten der anderen Benutzer dieses Computers bleiben in deren Profilen.
german.RemoveDataKeep=Drücken Sie „Nein“, wenn Sie Golether erneut installieren wollen.
spanish.DesktopIcon=Acceso directo en el escritorio
spanish.Extras=Adicional:
spanish.Launch=Iniciar Golether
spanish.ModeTitle=Para quién instalar Golether
spanish.ModeQuestion=¿Quién usará el programa en este equipo?
spanish.ModeHelp=«Solo para mí» instala el programa en tu perfil y no necesita permisos de administrador.%n«Para todos» lo instala en Program Files: Windows pide una vez el permiso de un administrador.
spanish.ModeOnlyMe=Solo para mí (%1)
spanish.ModeEveryone=Para todos
spanish.ModeRefused=Windows no concedió permisos de administrador (código %1), y sin ellos no se puede instalar Golether para todos.%n%nEjecuta la instalación como administrador para instalar para todos: clic derecho en el archivo de instalación → «Ejecutar como administrador». O elige «Solo para mí».
spanish.RemoveData=¿Borrar también los datos personales de Golether (claves, ajustes, lista de contactos)?
spanish.RemoveDataShared=Solo se borran los datos del usuario actual. Los datos de los demás usuarios de este equipo se quedan en sus perfiles.
spanish.RemoveDataKeep=Pulsa «No» si vas a volver a instalar Golether.
french.DesktopIcon=Raccourci sur le bureau
french.Extras=Options supplémentaires :
french.Launch=Lancer Golether
french.ModeTitle=Pour qui installer Golether
french.ModeQuestion=Qui utilisera le programme sur cet ordinateur ?
french.ModeHelp=« Seulement pour moi » installe le programme dans votre profil et ne demande pas de droits d'administrateur.%n« Pour tout le monde » l'installe dans Program Files : Windows demande une fois l'autorisation d'un administrateur.
french.ModeOnlyMe=Seulement pour moi (%1)
french.ModeEveryone=Pour tout le monde
french.ModeRefused=Windows n'a pas accordé les droits d'administrateur (code %1), et sans eux Golether ne peut pas être installé pour tout le monde.%n%nLancez l'installation en tant qu'administrateur pour installer pour tout le monde : clic droit sur le fichier d'installation → « Exécuter en tant qu'administrateur ». Ou choisissez « Seulement pour moi ».
french.RemoveData=Supprimer aussi les données personnelles de Golether (clés, réglages, liste des contacts) ?
french.RemoveDataShared=Seules les données de l'utilisateur actuel sont supprimées. Les données des autres utilisateurs de cet ordinateur restent dans leurs profils.
french.RemoveDataKeep=Appuyez sur « Non » si vous comptez réinstaller Golether.
portuguese.DesktopIcon=Atalho no ambiente de trabalho
portuguese.Extras=Adicional:
portuguese.Launch=Iniciar o Golether
portuguese.ModeTitle=Para quem instalar o Golether
portuguese.ModeQuestion=Quem vai usar o programa neste computador?
portuguese.ModeHelp=“Só para mim” instala o programa no seu perfil e não precisa de direitos de administrador.%n“Para todos” instala-o em Program Files: o Windows pede uma vez a permissão de um administrador.
portuguese.ModeOnlyMe=Só para mim (%1)
portuguese.ModeEveryone=Para todos
portuguese.ModeRefused=O Windows não concedeu direitos de administrador (código %1), e sem eles não é possível instalar o Golether para todos.%n%nExecute a instalação como administrador para instalar para todos: clique com o botão direito no ficheiro de instalação → “Executar como administrador”. Ou escolha “Só para mim”.
portuguese.RemoveData=Remover também os dados pessoais do Golether (chaves, definições, lista de contactos)?
portuguese.RemoveDataShared=Só são removidos os dados do utilizador atual. Os dados dos outros utilizadores deste computador ficam nos respetivos perfis.
portuguese.RemoveDataKeep=Prima “Não” se vai voltar a instalar o Golether.
russian.DesktopIcon=Значок на рабочем столе
russian.Extras=Дополнительно:
russian.Launch=Запустить Golether
russian.ModeTitle=Для кого установить Golether
russian.ModeQuestion=Кто будет пользоваться программой на этом компьютере?
russian.ModeHelp=«Только для меня» ставит программу в ваш профиль и не требует прав администратора.%n«Для всех» ставит её в Program Files: Windows один раз попросит разрешение администратора.
russian.ModeOnlyMe=Только для меня (%1)
russian.ModeEveryone=Для всех
russian.ModeRefused=Windows не выдала права администратора (код %1), а без них установить Golether для всех нельзя.%n%nЗапустите установку от имени администратора, чтобы установить для всех: правый щелчок по файлу установки → «Запуск от имени администратора». Или выберите «Только для меня».
russian.RemoveData=Удалить и личные данные Golether (ключи, настройки, список контактов)?
russian.RemoveDataShared=Удалятся данные только текущего пользователя. Данные других пользователей компьютера останутся в их профилях.
russian.RemoveDataKeep=Нажмите «Нет», если собираетесь поставить Golether снова.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Extras}"; Flags: unchecked

[Files]
; Everything from the published package except the data folder of a tried package.
Source: "{#GoletherSource}\*"; DestDir: "{app}"; Excludes: "data\*,data,golether.shared"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Golether"; Filename: "{app}\Golether.exe"
Name: "{autodesktop}\Golether"; Filename: "{app}\Golether.exe"; Tasks: desktopicon

[Registry]
; golether://join/... opens Golether. HKA is HKCU for one user and HKLM for all users.
Root: HKA; Subkey: "Software\Classes\golether"; ValueType: string; ValueName: ""; ValueData: "URL:Golether"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\golether"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\golether\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Golether.exe"",0"
Root: HKA; Subkey: "Software\Classes\golether\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Golether.exe"" ""%1"""

[Run]
Filename: "{app}\Golether.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The runtime files of the components are downloaded after the installation.
Type: filesandordirs; Name: "{app}\app\native"
Type: files; Name: "{app}\golether.shared"

[Code]
{ Starts a program through the shell; with the verb "runas" Windows asks for administrator rights. Returns a number
  above 32 on success, otherwise an error code (5 - refused, 2 - file not found...). }
function ShellExecuteW(Wnd: HWND; Operation, FileName, Parameters, Directory: String; ShowCmd: Integer): THandle;
  external 'ShellExecuteW@shell32.dll stdcall';

var
  { The page "whom to install for". }
  ModePage: TInputOptionWizardPage;
  { Setup was started again with administrator rights; this copy closes without questions. }
  Relaunched: Boolean;

{ An installation that is already there keeps the way it was installed. }
function IsUpdate(): Boolean;
begin
  Result := WizardForm.PrevAppDir <> '';
end;

procedure InitializeWizard();
begin
  ModePage := CreateInputOptionPage(wpWelcome,
    CustomMessage('ModeTitle'), CustomMessage('ModeQuestion'), CustomMessage('ModeHelp'), True, False);
  ModePage.Add(FmtMessage(CustomMessage('ModeOnlyMe'), [ExpandConstant('{username}')]));
  ModePage.Add(CustomMessage('ModeEveryone'));
  if IsAdminInstallMode then
    ModePage.SelectedValueIndex := 1
  else
    ModePage.SelectedValueIndex := 0;

  { On screens scaled above 100% the boxes of the lists are drawn larger while their distance from the edge stays
    the same, so the left side of a box is cut off. The distance is scaled with the rest of the wizard. }
  ModePage.CheckListBox.Offset := ScaleX(8);
  WizardForm.TasksList.Offset := ScaleX(8);
  WizardForm.RunList.Offset := ScaleX(8);
end;

{ The copy started with /ALLUSERS has the answer already, and an update does not ask again. }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = ModePage.ID) and (IsAdminInstallMode or IsUpdate());
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Code: THandle;
begin
  Result := True;
  if (CurPageID = ModePage.ID) and (ModePage.SelectedValueIndex = 1) and not IsAdminInstallMode then
  begin
    { Installing for all users needs administrator rights from the very start: Setup is started again with them. }
    Result := False;
    Code := ShellExecuteW(WizardForm.Handle, 'runas', ExpandConstant('{srcexe}'), '/ALLUSERS /LANG=' + ActiveLanguage, '', SW_SHOWNORMAL);
    if Code > 32 then
    begin
      Relaunched := True;
      WizardForm.Close;
    end
    else
      MsgBox(FmtMessage(CustomMessage('ModeRefused'), [IntToStr(Code)]), mbInformation, MB_OK);
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if Relaunched then
    Confirm := False;
end;

{ The folder with the personal data: inside the installation for one user, in the profile for all users. }
function PersonalDataDir(): string;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{localappdata}\Golether')
  else
    Result := ExpandConstant('{app}\data');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Marker: string;
begin
  if CurStep = ssPostInstall then
  begin
    { The marker tells the application to keep its data in the profile of each user. }
    Marker := ExpandConstant('{app}\golether.shared');
    if IsAdminInstallMode then
      SaveStringToFile(Marker, 'Golether is installed for all users: each user keeps the data in the profile (%LOCALAPPDATA%\Golether).' + #13#10, False)
    else
      DeleteFile(Marker);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
  Question: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := PersonalDataDir();
    if DirExists(DataDir) then
    begin
      Question := CustomMessage('RemoveData') + #13#10 + DataDir + #13#10 + #13#10;
      if IsAdminInstallMode then
        Question := Question + CustomMessage('RemoveDataShared') + #13#10 + #13#10;
      Question := Question + CustomMessage('RemoveDataKeep');
      if MsgBox(Question, mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
