; Installateur Agenda Reminder (Inno Setup 6)
; Compilé par la CI : ISCC.exe /DMyAppVersion=1.2.3 installer\AgendaReminder.iss
; Attend la publication de l'app dans ..\publish

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "Agenda Reminder"
#define MyAppExe "AgendaReminder.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{3E5B9C2A-7F41-4C8E-9A6D-2B1F0E7C4D93}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppName}
VersionInfoVersion={#MyAppVersion}
; Installation pour l'utilisateur courant : aucun droit administrateur requis.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=AgendaReminder-Setup-{#MyAppVersion}
SetupIconFile=..\src\AgendaReminder.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Ferme l'app si elle tourne pendant une mise à jour / désinstallation.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startup"; Description: "Lancer Agenda Reminder au démarrage de Windows"; GroupDescription: "Options :"
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Options :"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Pas lors d'une mise à jour silencieuse : on respecte le réglage choisi depuis l'app.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "AgendaReminder"; \
  ValueData: """{app}\{#MyAppExe}"" --background"; Tasks: startup; Check: not WizardSilent

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent
; Mise à jour silencieuse lancée depuis l'app : on relance l'app une fois la mise à jour terminée.
Filename: "{app}\{#MyAppExe}"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im {#MyAppExe} /f"; Flags: runhidden; RunOnceId: "StopApp"

[Code]
// Désinstallation : retire le démarrage automatique (qu'il ait été activé ici ou dans l'app)
// et propose de supprimer les données (comptes, paramètres, cache).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, '{#RunKey}', 'AgendaReminder');
    DataDir := ExpandConstant('{userappdata}\AgendaReminder');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox('Supprimer aussi vos données Agenda Reminder (comptes connectés, paramètres, cache) ?',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
