; Inno Setup script of Golether.
; The first page asks whom to install for:
;   "only for me" (the default) - %LOCALAPPDATA%\Programs\Golether, no administrator rights; all application data
;       (keys, database, components) stays in the "data" folder inside the installation;
;   "for all users" - C:\Program Files\Golether, asks for administrator rights once. Ordinary users cannot write
;       there, so the installer leaves the file golether.shared next to golether.install and every user keeps the
;       data in %LOCALAPPDATA%\Golether (see AppDataPaths). The application itself never needs administrator rights.
; Either way an update never touches personal data.
; The installer also teaches the system to open golether:// links (invitations clicked in a browser) with Golether.
; Build it with Scripts/build-installer.ps1, which passes the version and the source folder.

#ifndef GoletherVersion
  #define GoletherVersion "0.1.0"
#endif
#ifndef GoletherSource
  #define GoletherSource "..\artifacts\publish\win-x64\Golether"
#endif
#ifndef GoletherOutputDir
  #define GoletherOutputDir "..\artifacts\publish"
#endif

[Setup]
AppId={{7C2B1E22-2F1B-4C5E-9A0C-7C9C7E3A5B10}
AppName=Golether
AppVersion={#GoletherVersion}
AppPublisher=Golether
AppCopyright=GPL-3.0-or-later
VersionInfoVersion={#GoletherVersion}
; {autopf} is %LOCALAPPDATA%\Programs for one user and C:\Program Files for all users.
DefaultDirName={autopf}\Golether
DefaultGroupName=Golether
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
; Shows the choice "only for me / for all users"; an update keeps the choice of the first installation.
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#GoletherSource}\..\..\..\..\Sources\Apps\Golether.UI\Assets\golether.ico
UninstallDisplayIcon={app}\Golether.exe
OutputDir={#GoletherOutputDir}
OutputBaseFilename=Golether-{#GoletherVersion}-setup
CloseApplications=yes
CloseApplicationsFilter=Golether.exe
; The link handler is registered below: the shell must learn about it at once.
ChangesAssociations=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Значок на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

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
Filename: "{app}\Golether.exe"; Description: "Запустить Golether"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The runtime files of the components are downloaded after the installation.
Type: filesandordirs; Name: "{app}\app\native"
Type: files; Name: "{app}\golether.shared"

[Code]
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
      Question := 'Удалить и личные данные Golether (ключи, настройки, список контактов)?'
        + #13#10 + DataDir + #13#10 + #13#10;
      if IsAdminInstallMode then
        Question := Question + 'Удалятся данные только текущего пользователя. '
          + 'Данные других пользователей компьютера останутся в их профилях.' + #13#10 + #13#10;
      Question := Question + 'Нажмите «Нет», если собираетесь поставить Golether снова.';
      if MsgBox(Question, mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
