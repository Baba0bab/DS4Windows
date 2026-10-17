; Installeur DS4Windows (Inno Setup 6).
; Ne pas compiler à la main : lancer installer\build.ps1, qui publie l'appli
; dans installer\publish puis compile ce script.

#define AppName "DS4Windows"
#define AppExe "DS4Windows.exe"
#define AppUrl "https://github.com/Baba0bab/DS4Windows"

#ifndef PublishDir
  #define PublishDir "publish"
#endif
#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(PublishDir) + AppExe)
#endif
#ifndef SelfContained
  #define SelfContained "0"
#endif
#if SelfContained == "1"
  #define SetupSuffix "_selfcontained"
#else
  #define SetupSuffix ""
#endif

[Setup]
; Ne jamais changer l'AppId : c'est lui qui permet les mises à jour par-dessus
AppId={{952B15E0-0EF5-471D-BDB4-EFC77AE15602}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Baba0bab
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Par défaut pour l'utilisateur courant (sans droits admin), au choix pour tous
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=DS4Windows_{#AppVersion}_x64{#SetupSuffix}_Setup
SetupIconFile=..\DS4Windows\DS4W.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.InstallDrivers=Installer les pilotes (ViGEmBus, HidHide)
english.InstallDrivers=Install the drivers (ViGEmBus, HidHide)
french.DotNetMissing=DS4Windows a besoin du « .NET 10 Desktop Runtime » (x64), qui n'est pas installé sur ce PC.%n%nOuvrir la page de téléchargement ?%n%nL'installation continue quand même : il suffira d'installer le runtime avant de lancer DS4Windows.
english.DotNetMissing=DS4Windows needs the ".NET 10 Desktop Runtime" (x64), which is not installed on this PC.%n%nOpen the download page?%n%nSetup will continue anyway: just install the runtime before starting DS4Windows.
french.RemoveSettings=Supprimer aussi vos profils et réglages DS4Windows ?%n%n%1%n%nRépondez Non pour les garder (conseillé en cas de réinstallation).
english.RemoveSettings=Also delete your DS4Windows profiles and settings?%n%n%1%n%nAnswer No to keep them (recommended if you plan to reinstall).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "DS4Windows.installed"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "-driverinstall"; WorkingDir: "{app}"; Description: "{cm:InstallDrivers}"; Flags: postinstall skipifsilent runasoriginaluser; Check: IsDotNetReady and not IsViGEmBusInstalled
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: IsDotNetReady

[UninstallDelete]
; Fichiers créés par l'appli elle-même (démarrage via tâche planifiée, updater)
Type: files; Name: "{app}\task.bat"
Type: files; Name: "{app}\DS4Updater.exe"

[Code]
function IsDotNetDesktop10Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function IsDotNetReady: Boolean;
begin
#if SelfContained == "1"
  Result := True;
#else
  Result := IsDotNetDesktop10Installed;
#endif
end;

function IsViGEmBusInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

function GetShortcutTarget(const LnkPath: String): String;
var
  Shell, Shortcut: Variant;
begin
  Result := '';
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(LnkPath);
    Result := Shortcut.TargetPath;
  except
  end;
end;

function StartupShortcutPath: String;
begin
  Result := ExpandConstant('{userstartup}\DS4Windows.lnk');
end;

{ Le lancement au démarrage est géré par l'appli (onglet Paramètres). Si un
  raccourci existe déjà, on le fait pointer vers la copie installée. }
procedure RedirectStartupShortcut;
var
  Shell, Shortcut: Variant;
  NewExe: String;
begin
  if not FileExists(StartupShortcutPath) then
    Exit;

  NewExe := ExpandConstant('{app}\{#AppExe}');
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(StartupShortcutPath);
    if CompareText(ExtractFileName(Shortcut.TargetPath), '{#AppExe}') <> 0 then
      Exit;

    Shortcut.TargetPath := NewExe;
    Shortcut.Arguments := '-m';
    Shortcut.WorkingDirectory := ExpandConstant('{app}');
    Shortcut.IconLocation := NewExe;
    Shortcut.Save;
  except
  end;
end;

{ Ferme proprement les DS4Windows en cours (manettes déconnectées, pilotes
  rendus) via la commande IPC de l'appli. Si OnlyInDir n'est pas vide, seules
  les copies lancées depuis ce dossier sont fermées. }
function CloseRunningDS4Windows(const OnlyInDir: String): Boolean;
var
  Locator, Wmi, Procs, Proc: Variant;
  ExePath, Query: String;
  I, Attempt, ResultCode: Integer;
  Found: Boolean;
begin
  Result := True;
  Query := 'SELECT ExecutablePath FROM Win32_Process WHERE Name = ''{#AppExe}''';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');

    for Attempt := 0 to 20 do
    begin
      Procs := Wmi.ExecQuery(Query);
      Found := False;
      for I := 0 to Procs.Count - 1 do
      begin
        Proc := Procs.ItemIndex(I);
        ExePath := '';
        if not VarIsNull(Proc.ExecutablePath) then
          ExePath := Proc.ExecutablePath;

        if (OnlyInDir <> '') and
           (CompareText(RemoveBackslash(ExtractFileDir(ExePath)), RemoveBackslash(OnlyInDir)) <> 0) then
          Continue;

        Found := True;
        { La commande passe par IPC : n'importe quel DS4Windows.exe peut l'envoyer
          (chemin inconnu si le processus tourne en admin) }
        if not FileExists(ExePath) then
          ExePath := ExpandConstant('{app}\{#AppExe}');
        if (Attempt = 0) and FileExists(ExePath) then
          Exec(ExePath, '-command shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;

      if not Found then
        Exit;
      Sleep(500);
    end;

    Result := False;
  except
    { WMI indisponible : le Restart Manager d'Inno Setup prend le relais }
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNetReady and not WizardSilent then
    if MsgBox(CustomMessage('DotNetMissing'), mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '',
        SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningDS4Windows('');
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RedirectStartupShortcut;
end;

function InitializeUninstall: Boolean;
begin
  CloseRunningDS4Windows(ExpandConstant('{app}'));
  Result := True;
end;

procedure DeletePortableSettings(const Dir: String);
begin
  DeleteFile(Dir + '\Profiles.xml');
  DeleteFile(Dir + '\Auto Profiles.xml');
  DeleteFile(Dir + '\Actions.xml');
  DeleteFile(Dir + '\LinkedProfiles.xml');
  DeleteFile(Dir + '\ControllerConfigs.xml');
  DeleteFile(Dir + '\OutputSlots.xml');
  DelTree(Dir + '\Profiles', True, True, True);
  DelTree(Dir + '\Logs', True, True, True);
  RemoveDir(Dir);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir, AppDir: String;
  ResultCode: Integer;
begin
  AppDir := ExpandConstant('{app}');
  if CurUninstallStep = usUninstall then
  begin
    if FileExists(StartupShortcutPath) and
       (CompareText(GetShortcutTarget(StartupShortcutPath), AppDir + '\{#AppExe}') = 0) then
      DeleteFile(StartupShortcutPath);

    { task.bat n'existe que si l'appli a créé la tâche planifiée "RunDS4Windows" }
    if FileExists(AppDir + '\task.bat') then
      Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN RunDS4Windows /F', '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\DS4Windows');
    if UninstallSilent or
       not (DirExists(SettingsDir) or FileExists(AppDir + '\Profiles.xml')) then
      Exit;

    if MsgBox(FmtMessage(CustomMessage('RemoveSettings'), [SettingsDir]),
       mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(SettingsDir, True, True, True);
      DeletePortableSettings(AppDir);
    end;
  end;
end;
