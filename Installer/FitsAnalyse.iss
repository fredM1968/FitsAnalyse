; Installateur de FitsAnalyse (plugin NINA) – Inno Setup 6
; Compiler ce script avec Inno Setup Compiler après avoir compilé le projet en Release.
; Résultat : Installer\Sortie\FitsAnalyse-<version>-setup.exe

#define AppName "FitsAnalyse"
#define DllPath "..\bin\Release\net8.0-windows\FitsAnalyse.dll"
#define AppVersion GetVersionNumbersString(DllPath)

[Setup]
; Identifiant de l'installateur : ne plus le changer après la première diffusion
AppId={{0CFBF02B-ED05-4832-A3E0-DD9418A95C6A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Fred
AppPublisherURL=https://github.com/fredM1968/FitsAnalyse
AppSupportURL=https://github.com/fredM1968/FitsAnalyse/issues
; Dossier des plugins de NINA 3.x, propre à l'utilisateur : pas besoin des droits administrateur
DefaultDirName={localappdata}\NINA\Plugins\3.0.0\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Sortie
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName} (plugin NINA)
UninstallDisplayIcon={app}\{#AppName}.dll
LicenseFile=..\LICENSE.txt
InfoBeforeFile=AvantInstallation.txt

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#DllPath}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LISEZMOI.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Code]
// NINA garde la DLL ouverte : on demande de le fermer avant d'installer ou de désinstaller.
function NinaRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  // tasklist renvoie 0 dans tous les cas : on passe par find pour tester la présence du processus
  if Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq NINA.exe" | find /I "NINA.exe" >nul',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

function WaitForNinaClosed(): Boolean;
begin
  Result := True;
  while NinaRunning() do
  begin
    if MsgBox('NINA est ouvert. Ferme NINA puis clique sur OK pour continuer (Annuler pour abandonner).',
              mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := WaitForNinaClosed();
end;

function InitializeUninstall(): Boolean;
begin
  Result := WaitForNinaClosed();
end;
