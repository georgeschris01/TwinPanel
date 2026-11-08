; TwinPanel — © 2026 Georges Christophe (chrypox). Licence TwinPanel 1.1 : voir le fichier LICENSE.
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
; Présentation : bande bleu foncé à gauche des pages d'accueil et de fin (logo, nom, petite capture), bandeau bleu foncé
; sur toute la largeur en haut des autres pages (voir [Code]). Images fabriquées par images\make-images.py, une par
; réglage d'affichage (100 % à 250 %) ; Setup prend la mieux adaptée. Capture anglaise chargée dans [Code].
DisableWelcomePage=no
WizardImageFile=images\accueil-fr-202x386.png,images\accueil-fr-269x515.png,images\accueil-fr-336x643.png,images\accueil-fr-403x772.png,images\accueil-fr-430x824.png,images\accueil-fr-534x1022.png
WizardSmallImageFile=images\logo-58.png,images\logo-77.png,images\logo-97.png,images\logo-116.png,images\logo-124.png,images\logo-143.png,images\logo-159.png
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
; Images de l'accueil en anglais : seulement pour l'installateur, jamais copiées sur le PC.
Source: "images\accueil-en-*.png"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\TwinPanel"; Filename: "{app}\TwinPanel.exe"
Name: "{autodesktop}\TwinPanel"; Filename: "{app}\TwinPanel.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TwinPanel.exe"; Description: "{cm:LaunchProgram,TwinPanel}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  { Couleurs au format Inno Setup ($BBGGRR). }
  BannerColor = $512F13;      { bleu foncé, RGB 19, 47, 81 }
  BannerLineColor = $00C4FF;  { jaune du logo, RGB 255, 196, 0 }
  BannerTextColor = $EBD7C8;  { bleu très clair, RGB 200, 215, 235 }

{ Bandeau sur toute la largeur en haut des pages : fond bleu foncé, logo à gauche, titre en blanc,
  « TwinPanel » en jaune à droite, trait jaune dessous. Même présentation pour l'installation et la désinstallation. }
procedure StyleBanner(Panel: TPanel; Bevel: TBevel; Logo: TBitmapImage; Title, Description: TNewStaticText);
var
  Name: TNewStaticText;
  Line: TPanel;
  Margin, TextLeft, LineHeight: Integer;
begin
  Margin := ScaleX(16);
  LineHeight := ScaleY(3);
  Panel.ParentBackground := False;
  Panel.Color := BannerColor;
  Bevel.Visible := False;

  Line := TPanel.Create(Panel);
  Line.Parent := Panel;
  Line.BevelOuter := bvNone;
  Line.ParentBackground := False;
  Line.Color := BannerLineColor;
  Line.Height := LineHeight;
  Line.Align := alBottom;

  Logo.BackColor := BannerColor;
  Logo.Left := Margin;
  Logo.Top := (Panel.Height - LineHeight - Logo.Height) div 2;

  Name := TNewStaticText.Create(Panel);
  Name.Parent := Panel;
  Name.Caption := 'TwinPanel';
  Name.AutoSize := True;
  Name.Font.Style := [fsBold];
  Name.Font.Size := 13;
  Name.Font.Color := BannerLineColor;
  Name.Color := BannerColor;
  Name.Left := Panel.Width - Name.Width - Margin - ScaleX(4);
  Name.Top := (Panel.Height - LineHeight - Name.Height) div 2;
  Name.Anchors := [akTop, akRight];

  TextLeft := Logo.Left + Logo.Width + ScaleX(14);
  Title.Left := TextLeft;
  Title.Width := Name.Left - TextLeft - Margin;
  Title.Color := BannerColor;
  Title.Font.Color := clWhite;
  Description.Left := TextLeft;
  Description.Width := Name.Left - TextLeft - Margin;
  Description.Color := BannerColor;
  Description.Font.Color := BannerTextColor;
end;

{ Accueil et fin en anglais : même bande, avec la capture anglaise, à la taille la mieux adaptée à l'écran. }
procedure LoadEnglishWelcomeImage;
var
  Sizes: TArrayOfString;
  I, Width: Integer;
  FileName: String;
begin
  Sizes := ['202x386', '269x515', '336x643', '403x772', '430x824', '534x1022'];
  FileName := Sizes[GetArrayLength(Sizes) - 1];
  for I := 0 to GetArrayLength(Sizes) - 1 do
  begin
    Width := StrToInt(Copy(Sizes[I], 1, Pos('x', Sizes[I]) - 1));
    if Width >= WizardForm.WizardBitmapImage.Width then
    begin
      FileName := Sizes[I];
      Break;
    end;
  end;
  FileName := 'accueil-en-' + FileName + '.png';
  ExtractTemporaryFile(FileName);
  WizardForm.WizardBitmapImage.PngImage.LoadFromFile(ExpandConstant('{tmp}\' + FileName));
  WizardForm.WizardBitmapImage2.PngImage.LoadFromFile(ExpandConstant('{tmp}\' + FileName));
end;

procedure InitializeWizard;
begin
  StyleBanner(WizardForm.MainPanel, WizardForm.Bevel1, WizardForm.WizardSmallBitmapImage,
    WizardForm.PageNameLabel, WizardForm.PageDescriptionLabel);
  if ActiveLanguage = 'english' then
    LoadEnglishWelcomeImage;
end;

procedure InitializeUninstallProgressForm;
begin
  StyleBanner(UninstallProgressForm.MainPanel, UninstallProgressForm.Bevel1, UninstallProgressForm.WizardSmallBitmapImage,
    UninstallProgressForm.PageNameLabel, UninstallProgressForm.PageDescriptionLabel);
end;
