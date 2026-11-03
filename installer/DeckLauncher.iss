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

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\out\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\DeckLauncher"; Filename: "{app}\DeckLauncher.exe"
Name: "{autodesktop}\DeckLauncher"; Filename: "{app}\DeckLauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DeckLauncher.exe"; Description: "{cm:LaunchProgram,DeckLauncher}"; Flags: nowait postinstall skipifsilent
