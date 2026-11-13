; Instalador do DeckLauncher (Inno Setup 6). Compilado pelo GitHub Actions (.github/workflows/release.yml):
;   iscc /DAppVersion=1.0.0 installer\DeckLauncher.iss
; Espera o programa já publicado em ..\out\app (dotnet publish autônomo, arquivo único).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{BDA7A1E0-9871-4623-BC15-EE5ECE3F53A4}
AppName=DeckLauncher
AppVersion={#AppVersion}
AppVerName=DeckLauncher {#AppVersion}
AppPublisher=DeckLauncher
DefaultDirName={autopf}\DeckLauncher
DefaultGroupName=DeckLauncher
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=DeckLauncher-Setup-{#AppVersion}
SetupIconFile=..\src\DeckLauncher.App\Assets\app.ico
UninstallDisplayIcon={app}\DeckLauncher.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

; O Modo Xbox só entra no instalador se o pacote foi gerado antes (fse\Build-Fse.ps1 cria out\fse). Sem ele, o instalador sai normal.
#if FileExists(AddBackslash(SourcePath) + "..\out\fse\DeckLauncherFSE.msix")
  #define HasFse
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
#ifdef HasFse
Name: "xboxmode"; Description: "Modo Xbox: usar o DeckLauncher como tela inicial da Experiência de Tela Cheia do Xbox (pede permissão de administrador)"; GroupDescription: "Modo Xbox:"
#endif

[Files]
Source: "..\out\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef HasFse
Source: "..\out\fse\*"; DestDir: "{app}\fse"; Flags: ignoreversion; Tasks: xboxmode
#endif

[Registry]
; A ponte do Modo Xbox usa isto para achar o DeckLauncher.
Root: HKCU; Subkey: "Software\DeckLauncher"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey

[Icons]
Name: "{autoprograms}\DeckLauncher"; Filename: "{app}\DeckLauncher.exe"
Name: "{autodesktop}\DeckLauncher"; Filename: "{app}\DeckLauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DeckLauncher.exe"; Description: "{cm:LaunchProgram,DeckLauncher}"; Flags: nowait postinstall skipifsilent
; Atualização feita de dentro do launcher (instalação silenciosa): abre o launcher de novo.
Filename: "{app}\DeckLauncher.exe"; Flags: nowait skipifnotsilent

#ifdef HasFse
[Code]
// Instala o pacote do Modo Xbox (precisa de administrador: ele confia no certificado do pacote).
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('xboxmode') then
  begin
    WizardForm.StatusLabel.Caption := 'Instalando o Modo Xbox...';
    if not ShellExec('runas', 'powershell.exe',
        '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\fse\Install-FSE.ps1') + '"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      ResultCode := -1;
    if ResultCode <> 0 then
      MsgBox('O DeckLauncher foi instalado, mas o Modo Xbox não pôde ser ativado (a permissão foi negada ou o Windows recusou o pacote).' + #13#10 +
             'Você pode tentar de novo executando "Install-FSE.ps1" como administrador, na pasta "fse" dentro da pasta do programa.' + #13#10 +
             'Detalhes: ' + ExpandConstant('{%TEMP}\DeckLauncher-ModoXbox.log'), mbInformation, MB_OK);
  end;
end;

// Ao desinstalar, tira também o pacote do Modo Xbox.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\fse\Remove-FSE.ps1') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
#endif
