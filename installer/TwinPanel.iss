; TwinPanel — © 2026 Georges Christophe (chrypox). Licence TwinPanel 1.0 : voir le fichier LICENSE.
; Installateur de TwinPanel (Inno Setup 6 ou 7, gratuit : https://jrsoftware.org/isdl.php).
; Compilé par build-installer.bat, qui publie d'abord TwinPanel dans publish\win-x64 et donne la version.
; Une nouvelle version = un nouveau TwinPanel-Setup-x.y.z.exe : lancé sur un PC où TwinPanel est déjà installé,
; il le met à jour (même AppId ci-dessous, à NE JAMAIS changer). Les réglages (%APPDATA%\TwinPanel) ne sont pas touchés.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{148AA9F1-8428-46AD-9082-858121CBFB5E}
AppName=TwinPanel
AppVersion={#AppVersion}
AppVerName=TwinPanel {#AppVersion}
AppPublisher=chrypox
AppCopyright=© 2026 Georges Christophe (chrypox)
AppContact=admin@chrypox.be
; Le choix de la langue est proposé au début ; la licence à accepter est donnée pour chaque langue dans [Languages].
ShowLanguageDialog=yes
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Installation de TwinPanel
DefaultDirName={autopf}\TwinPanel
DefaultGroupName=TwinPanel
DisableProgramGroupPage=yes
; Mise à jour : même dossier que l'installation précédente, sans redemander.
UsePreviousAppDir=yes
DisableDirPage=auto
OutputDir=..\installer-output\windows
OutputBaseFilename=TwinPanel-Setup-{#AppVersion}
SetupIconFile=..\src\TwinPanel.App\Assets\twinpanel.ico
UninstallDisplayIcon={app}\TwinPanel.exe
UninstallDisplayName=TwinPanel
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Pour tous les utilisateurs (Program Files) ; l'installateur propose aussi « pour moi seulement » sans droits administrateur.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; TwinPanel ouvert pendant une mise à jour : l'installateur propose de le fermer.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "..\LICENSE"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "..\LICENSE.en.txt"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Mise à jour : bibliothèques de l'ancienne version retirées (une bibliothèque abandonnée ne doit pas rester).
Type: files; Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\runtimes"

[Files]
; Seulement le programme : aucun réglage, connexion, mot de passe ni journal (double sécurité, ils ne sont de toute façon pas dans publish).
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.log,settings*.json,connections*.json,secrets.dat,known_hosts*.json,history.json,transfers.json*,sync-profiles.json,*.twinpanel-part,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\COMPOSANTS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.en.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\LICENSE.en.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\TwinPanel"; Filename: "{app}\TwinPanel.exe"
Name: "{autodesktop}\TwinPanel"; Filename: "{app}\TwinPanel.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TwinPanel.exe"; Description: "{cm:LaunchProgram,TwinPanel}"; Flags: nowait postinstall skipifsilent
