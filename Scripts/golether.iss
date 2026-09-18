; Inno Setup script of Golether: a per-user installation without administrator rights.
; All application data (keys, database, components) stays in the "data" folder inside the installation,
; so the application remains self-contained and an update never touches personal data.
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
DefaultDirName={localappdata}\Programs\Golether
DefaultGroupName=Golether
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
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

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Значок на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
; Everything from the published package except the data folder of a tried package.
Source: "{#GoletherSource}\*"; DestDir: "{app}"; Excludes: "data\*,data"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Golether"; Filename: "{app}\Golether.exe"
Name: "{userdesktop}\Golether"; Filename: "{app}\Golether.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Golether.exe"; Description: "Запустить Golether"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The runtime files of the components are downloaded after the installation.
Type: filesandordirs; Name: "{app}\app\native"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{app}\data');
    if DirExists(DataDir) then
    begin
      if MsgBox('Удалить и личные данные Golether (ключи, настройки, список контактов) из папки data?'
        + #13#10 + DataDir + #13#10 + #13#10
        + 'Нажмите «Нет», если собираетесь поставить Golether снова.', mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
