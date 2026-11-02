# Composants de TwinPanel

Tout ce que TwinPanel utilise et qui ne vient pas de nous, ainsi que le plan de nos propres modules.
**À mettre à jour à chaque ajout, retrait ou changement de version** (le journal note aussi chaque changement).

Dernière mise à jour : 30/10/2026 (lot 5, archives).

---

## 1. Plateforme

| Composant | Version | Licence | Rôle |
|---|---|---|---|
| .NET (SDK et runtime) | 10.0 (SDK 10.0.403) | MIT | Langage C#, bibliothèques de base, compilation. Version à support long (jusqu'à fin 2028). |

---

## 2. Bibliothèques livrées avec l'application

### Choisies directement (dans les fichiers `.csproj`)

| Composant | Version | Licence | Rôle dans TwinPanel | Pourquoi ce choix |
|---|---|---|---|---|
| **Avalonia** | 12.1.4 | MIT | Toute l'interface graphique (fenêtres, listes, boutons, thèmes). | Multiplateforme (Windows maintenant, Linux plus tard), proche de WPF, communautaire. La version 12 existe : migration prévue plus tard. |
| Avalonia.Desktop | 12.1.4 | MIT | Lancement de l'application sur ordinateur (Windows, Linux, macOS). | Fait partie d'Avalonia. |
| Avalonia.Themes.Fluent | 12.1.4 | MIT | Style moderne, thème clair / sombre / système. | Fait partie d'Avalonia. |
| Avalonia.Controls.DataGrid | 12.1.2 | MIT | Les listes de fichiers à colonnes (panneaux, recherche, synchronisation). | Composant officiel d'Avalonia, numéroté à part depuis la version 12. |
| Avalonia.Controls.ColorPicker | 12.1.4 | MIT | Roue chromatique des sélecteurs de couleur (couleurs des fichiers, icônes des disques). | Composant officiel d'Avalonia, même version ; aucune autre dépendance. |
| Avalonia.Fonts.Inter | 12.1.4 | MIT (paquet) ; police Inter : SIL Open Font License 1.1 | Police de l'interface. | Lisible, libre, identique sur tous les systèmes. |
| **CommunityToolkit.Mvvm** | 8.4.0 | MIT | Génère automatiquement le code répétitif des écrans (propriétés, commandes). | Standard de la .NET Foundation, sans dépendance. |
| **SharpCompress** | 0.48.1 | MIT | **Lecture** des archives zip, 7z, rar, gz seul, bz2, xz (afficher, extraire, tester). Les .tar / .tar.gz sont lus par .NET (plus fiable). | Seule bibliothèque libre et mûre qui lit 7z et rar en .NET. Version ≥ 0.48.0 obligatoire : corrige la faille « zip slip » CVE-2026-44788. |
| **SharpZipLib** | 1.4.2 | MIT | **Écriture** des zip chiffrés en AES-256 (création, modification d'un zip chiffré). |
| **SSH.NET** | 2026.0.0 | MIT | Connexions **SFTP** et **SCP** (lot 6) : authentification par mot de passe, clé ou « clavier interactif », proxy, serveur de rebond, vérification de la clé du serveur. | Bibliothèque SSH libre la plus utilisée en .NET. Version 2026.0.0 obligatoire : toutes les précédentes ont des failles (dont CVE-2026-48798 et CVE-2026-85756 sur SCP). |
| **FluentFTP** | 55.1.0 | MIT | Connexions **FTP** et **FTPS** (explicite / implicite), mode passif / actif, proxy. | Référence du FTP en .NET, sans aucune dépendance. 55.1.0 : le certificat est aussi vérifié sur les connexions de données (faille corrigée). |
| System.Security.Cryptography.ProtectedData | 10.0.0 | MIT (Microsoft) | Chiffrement des mots de passe enregistrés avec le compte Windows (**DPAPI**). | Fourni par Microsoft, c'est la méthode standard de Windows. |
| **MediaInfo.Core.Native** | 26.10.0 | BSD-2 (MediaArea) | Bibliothèque native MediaInfo (libmediainfo, avec libcurl, libssh2, OpenSSL et Brotli) : codecs, pistes, langues, HDR / Dolby Vision. Appelée directement par `Core/Media/MediaInfoLib.cs`. | Référence de l'analyse des médias, utilisée par la plupart des lecteurs. |
| **z440.atl.core** (ATL .NET) | 7.19.0 | MIT | Lecture et écriture des tags des musiques (MP3, FLAC, M4A, OGG, Opus, WMA…) et des MP4 ; débit, fréquence, durée. | Pur .NET, très complet, mis à jour régulièrement (MIT plutôt que la LGPL de TagLib#). |
| **MetadataExtractor** | 2.9.3 | Apache-2.0 | Lecture de l'EXIF / XMP / GPS des photos (avec XmpCore, licence BSD d'Adobe). | Référence en .NET pour lire les métadonnées d'images. |

### Installées automatiquement avec Avalonia (dépendances indirectes)

| Composant | Version | Licence | Rôle |
|---|---|---|---|
| Avalonia.Win32 / Avalonia.X11 / Avalonia.Native / Avalonia.FreeDesktop / Avalonia.FreeDesktop.AtSpi | 12.1.4 | MIT | Liaison avec Windows / Linux (X11) / macOS / bureaux Linux. |
| Avalonia.Skia | 12.1.4 | MIT | Dessin de l'interface via Skia. |
| Avalonia.HarfBuzz | 12.1.4 | MIT | Mise en forme du texte pour Avalonia. |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Communication D-Bus (utilisée par Avalonia **sous Linux** seulement). Amenée par Avalonia ; plus besoin d'imposer une version. |
| Avalonia.Themes.Simple, Avalonia.Controls.ColorPicker, Avalonia.Remote.Protocol | 12.1.4 | MIT | Éléments internes d'Avalonia (non utilisés directement). |
| Avalonia.Angle.Windows.Natives | 2.1.27548 | BSD-3 (ANGLE, Google) | Rendu graphique accéléré sous Windows. |
| SkiaSharp (+ NativeAssets) | 3.119.4 | MIT ; moteur Skia : BSD-3 | Moteur de dessin 2D. |
| HarfBuzzSharp (+ NativeAssets) | 8.3.1.3 | MIT ; HarfBuzz : licence « Old MIT » | Mise en forme du texte (accents, écritures complexes). |
| BouncyCastle.Cryptography | ≥ 2.7.0 (amenée par SSH.NET) | MIT | Algorithmes cryptographiques de SSH ; sert aussi à **créer** les clés Ed25519. |
| Microsoft.Extensions.Logging.Abstractions | ≥ 8.0.3 (amenée par SSH.NET) | MIT | Interface de journalisation (inutilisée par TwinPanel). |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 (amenée par Logging.Abstractions) | MIT | Élément interne (inutilisé par TwinPanel). |
| Ude.NetStandard | 1.2.0 (amenée par ATL .NET) | MPL-1.1 (ou GPL / LGPL au choix) | Détection de l'encodage des textes pour ATL. Livrée sans modification ; code source : https://github.com/yinyue200/ude. |
| MicroCom.Runtime | 0.11.6 | MIT | Appels système natifs (interne à Avalonia). |
| **Avalonia.BuildServices** | 11.3.2 | MIT | ⚠️ Envoie des **statistiques anonymes de compilation** à l'équipe Avalonia (pas pendant l'utilisation de TwinPanel). Désactivable : variable d'environnement `AVALONIA_TELEMETRY_OPTOUT=1`. |

---

## 3. Parties de .NET utilisées (rien à installer en plus)

| Partie de .NET | Utilisée pour |
|---|---|
| `System.IO.Compression` (ZipArchive, GZipStream) | **Créer** les archives .zip et .tar.gz (Alt+F5). |
| `System.Formats.Tar` | **Créer et lire** les archives .tar et .tar.gz / .tgz. |
| `System.Text.Json` | Réglages (`%APPDATA%\TwinPanel\settings.json`). |
| `System.Security.Cryptography` (MD5, SHA-1, SHA-256) | Sommes de contrôle, recherche de doublons. |
| `System.Threading.Channels` | Résultats de recherche qui arrivent au fur et à mesure. |
| `Microsoft.VisualBasic.FileIO` | Envoi à la **corbeille** de Windows (F8). |
| `FileSystemWatcher` | Mise à jour automatique des panneaux quand un fichier change. |
| `System.Net.Http` (HttpClient) | Client **WebDAV** fait maison (Nextcloud, NAS…) : PROPFIND, GET, PUT, MKCOL, MOVE, DELETE. |
| `System.Security.Cryptography` (AesGcm, PBKDF2, SHA-256, HMAC-SHA1) | Coffre des secrets (mot de passe maître), empreintes des serveurs, lecture des noms hachés de `known_hosts`. |
| `System.Xml.Linq` | Réponses WebDAV, import FileZilla. |
| `Microsoft.Win32.Registry` | Import des sessions WinSCP et PuTTY (lecture seule). |
| `System.Security.AccessControl`, `System.Security.Principal` (FileSystemAclExtensions, NTAccount, SecurityIdentifier) | Fenêtre Propriétés sous Windows : propriétaire, héritage, autorisations NTFS (lire, ajouter, modifier, retirer). |
| `File.GetUnixFileMode` / `SetUnixFileMode` | Droits des fichiers du disque local sous Linux / macOS. |
| `advapi32.dll` (OpenProcessToken, AdjustTokenPrivileges — fonctions de Windows) | Activer les privilèges « prendre possession » / « restaurer » d'un administrateur pour changer le propriétaire d'un fichier. |
| `shell32.dll` (SHGetFileInfo), `user32.dll` / `gdi32.dll` (GetIconInfo, GetDIBits — fonctions de Windows) | Vraies icônes de Windows dans les panneaux (option) et icône des programmes dans « Parcourir… ». Lecture seule, sur un fil à part. |
| `System.Diagnostics.Process` | Lancer l'éditeur, la visionneuse externe, cmd.exe, l'application associée. |

---

## 4. Outils de test (servent à vérifier le code, **jamais livrés** avec TwinPanel)

| Composant | Version | Licence | Rôle |
|---|---|---|---|
| xUnit (xunit, xunit.assert, xunit.core…) | 2.9.2 | Apache 2.0 | Cadre des tests automatiques (`build.bat` les lance). |
| xunit.runner.visualstudio | 2.8.2 | Apache 2.0 | Lancement des tests par `dotnet test`. |
| Microsoft.NET.Test.Sdk (+ TestPlatform, CodeCoverage) | 17.12.0 | MIT | Infrastructure de test de .NET. |
| Newtonsoft.Json (indirect) | 13.0.1 | MIT | Utilisé par l'outil de test (13.0.1 = version sans la faille connue des versions plus anciennes). |

---


### Fabrication de l'installateur (sur le PC de développement seulement, jamais livré)

| Outil | Version | Licence | Rôle |
|---|---|---|---|
| Inno Setup | 7.1.1 | Gratuit pour un usage non commercial (licence payante demandée aux utilisateurs commerciaux) | Assemble `TwinPanel-Setup-x.y.z.exe` à partir de `installer/TwinPanel.iss` (lancé par `build-installer.bat`). L'installateur produit ne dépend pas d'Inno Setup. |

## 5. Programmes externes appelés (non inclus, présents ou non sur le PC)

| Programme | Quand | Si absent |
|---|---|---|
| Bloc-notes (`notepad.exe`) | F4 par défaut. | Toujours présent sous Windows. |
| Éditeur choisi (VSCodium, Notepad++, …) | F4 si choisi dans Outils → Éditeur et visionneuse. Seuls les éditeurs **réellement installés** sont proposés. | Message clair, ouverture dans le Bloc-notes, proposition de revenir au Bloc-notes. |
| Visionneuse externe | F3 si choisie. | Retour à la visionneuse intégrée. |
| `cmd.exe` | Ligne de commande en bas de la fenêtre. | — |
| PowerShell, PowerShell 7, `cmd.exe`, WSL | Terminal intégré (F12), au choix dans Options → Terminal. Seuls les programmes **réellement installés** sont proposés. | Repli sur PowerShell, puis sur `cmd.exe`. |
| Application associée par Windows | Entrée sur un fichier, Alt+F3. | Message d'erreur de Windows affiché proprement. |

---

### Outils médias facultatifs (installés par l'utilisateur, jamais fournis)

| Programme | Licence | Utilisé pour |
|---|---|---|
| mkvpropedit (MKVToolNix) | GPL-2.0 | Titre, langue, nom et drapeaux des pistes d'un MKV, sans réécrire le fichier. |
| exiftool (Phil Harvey) | Artistic / GPL | Écriture de l'EXIF des photos (date, auteur, copyright, description, GPS). |

## 6. Éléments faits maison (aucune ressource externe)

- **Icônes** : toutes dessinées pour le projet en tracés vectoriels (fichier `App.axaml` et `Converters/IconConverters.cs`). Aucun pack d'icônes externe. Option « Vraies icônes de Windows » : celles du système de l'utilisateur, lues à l'affichage (rien n'est copié dans TwinPanel).
- **Code** : écrit entièrement pour TwinPanel. On s'inspire du **comportement** de Total Commander (raccourcis, ergonomie), **jamais de son code**.
- **Couleurs et thème** : définis dans `App.axaml`.
- **Traduction et guide** : moteur de traduction (`Core/Localization`), textes anglais, lecteur du guide, écrits pour TwinPanel, sans bibliothèque ajoutée.
- **Terminal intégré** : analyse du flux, écran virtuel, clavier, souris et affichage écrits pour TwinPanel, sans bibliothèque de terminal. Il repose sur ConPTY, la console virtuelle fournie par Windows (Windows 10 version 1809 ou plus récent), et sur SSH.NET pour les serveurs. Le texte défilant reste en mémoire : il n'est jamais écrit sur le disque ni dans un journal.
- **Client WebDAV, coffre des secrets, gestion des serveurs connus, imports, écriture des clés Ed25519 au format OpenSSH** : écrits pour TwinPanel.

---

## 7. Télémétrie et vie privée

- **TwinPanel lui-même n'envoie rien** sur Internet, sauf vers les serveurs auxquels **tu** te connectes (lot 6).
- Fichiers des connexions dans `%APPDATA%\TwinPanel` : `connections.json` (liste, **sans aucun secret**), `secrets.dat` (mots de passe, phrases de passe et clés privées, **chiffrés**), `known_hosts.json` (empreintes des serveurs acceptés). Le `known_hosts` d'OpenSSH est lu, jamais modifié.
- À la **compilation** seulement :
  - Avalonia.BuildServices (voir plus haut) : désactiver avec `AVALONIA_TELEMETRY_OPTOUT=1` ;
  - le SDK .NET : désactiver avec `DOTNET_CLI_TELEMETRY_OPTOUT=1`.
  Pour couper les deux définitivement sous Windows : Paramètres → Système → Informations système → Paramètres système avancés → Variables d'environnement → Nouvelle variable utilisateur, puis relancer le terminal.

---

## 8. Licence de TwinPanel

Les licences des bibliothèques livrées sont **permissives** (MIT, BSD, Apache 2.0, SIL OFL, licence curl), sauf **Ude.NetStandard**, sous **MPL-1.1** : cette licence s'applique à ce fichier, livré sans modification, avec sa mention et le lien vers son code source. Il faut garder les mentions de copyright de toutes ces bibliothèques — elles sont reproduites dans `THIRD-PARTY-NOTICES.md` et listées dans **À propos → Bibliothèques utilisées et leurs licences** (données : `Core/About/ThirdPartyComponents.cs`).

**TwinPanel lui-même : Licence TwinPanel 1.0** (fichier `LICENSE`, contrat écrit pour le projet ; © 2026 Georges Christophe, chrypox ; contact admin@chrypox.be) — gratuit, non commercial (usage en entreprise permis), partage de la version d'origine seulement, modification pour soi permise mais pas de diffusion, plugins gratuits permis, corrections par « Issue », droit belge. Symbole original : `logo/licence/`. Ce n'est pas une licence Creative Commons (Creative Commons déconseille ses licences pour le logiciel et interdit son nom / logo pour une licence modifiée). Rappel en tête de chaque fichier de code ; page à accepter dans l'installateur ; texte complet dans « À propos ».

---

## 9. Plan des modules internes

### `src/TwinPanel.Core` — la logique, sans interface (testable)

| Dossier / fichier | Rôle |
|---|---|
| `FileEntry.cs`, `FileCategory.cs`, `DirectoryReader.cs` | Description d'un fichier, type (image, vidéo…), lecture d'un dossier. |
| `FileOperations.cs` | Copier, déplacer (progression, pause, conflits), renommer, créer, supprimer (corbeille). |
| `Sources/` | `IFileSource` : ce qu'un panneau peut afficher. `LocalFileSource` (disques), `FileSourceRegistry` (choix de la source). Les plugins s'y brancheront (lot 8). |
| `Archives/` | `ArchiveEditor` (modifier une archive : réécrire, vérifier, remplacer), `ArchivePasswords` (mots de passe en mémoire, copies déchiffrées), `EncryptedZip` (écriture AES-256), `ArchivePath` (chemins « dans » une archive), `ArchiveIndex` (contenu mis en cache), `ArchiveFileSource` (archive vue comme un dossier), `ArchiveService` (extraire avec protection zip slip, tester, créer zip / tar / tar.gz). |
| `DiskSpace.cs` | Place libre, reconnaître un disque plein, messages (avant et pendant une copie). |
| `TransferGate.cs` | Nombre de transferts en même temps, ordre d'arrivée, une seule opération par archive ; choix des lignes de progression. |
| `Settings/TabPositions.cs` | Remettre les onglets de serveur à leur rang à la réouverture. |
| `Navigation/` | Tri, historique précédent / suivant, dossiers récents (`RecentFolders`, fichier `history.json`), fil d'Ariane. |
| `Selection/MaskMatcher.cs` | Masques `*.jpg;*.png \| *.tmp`. |
| `Operations/` | Taille des dossiers, renommage multiple (masques, compteur, regex, annulation). |
| `Search/` | Recherche (nom, texte, taille, date), doublons, vue à plat (`FlatListing`). |
| `Compare/SyncProfiles.cs` | Profils de synchronisation (`SyncProfile`, `sync-profiles.json`, sans secret). |
| `Operations/RenameMedia.cs` | Valeurs des balises médias de Ctrl+M ([Titre], [Résolution]…), nettoyées pour un nom de fichier valable. |
| `Remote/RemoteDuplicates.cs` | Doublons sur un serveur : tailles, puis `sha256sum` du serveur ou lecture par le réseau après accord (`ServerHashResult` dans `RemoteFileSystem.cs`). |
| `Remote/TransferQueueStore.cs` | Transferts gardés d'une session à l'autre (`SavedTransfer`, `transfers.json`, sans secret), enregistrés régulièrement ; réservés à la première fenêtre ouverte. |
| `Remote/TransferResume.cs` | Reprise des transferts : plan fichier par fichier (`TransferPlan`), tentatives après une coupure (`TransferResume`), coupure ou refus (`ConnectionFailure`), comparaison de la fin déjà transférée (`ResumeTailStream`). |
| `Media/ThumbnailRules.cs` | Miniatures : quels fichiers y ont droit, clé de vignette, mémoire limitée en octets (`ThumbnailCache`). |
| `Compare/` | Comparaison et synchronisation de dossiers (côtés PC ou serveur : `SyncSides.cs`, sens réglable), différences de texte, sommes de contrôle (`HashingStream` : empreinte pendant un téléchargement). |
| `Viewer/` | Détection d'encodage, affichage hexadécimal, `TextDocument` (éditeur : encodage et fins de ligne conservés). |
| `Tools/ExternalTools.cs` | Éditeurs et visionneuses externes : détection, arguments `%1 %L %N %D`, éditeur intégré. |
| `Terminal/` | Terminal intégré. `VtParser` (découpe du flux), `TerminalEmulator` (écran, historique, couleurs, second écran), `TerminalKeys` (clavier, souris, collage), `TerminalSession` (écran + connexion), `ConPtyConnection` (programme du PC), `TerminalShells` (programmes installés). Serveurs : `Remote/SshTerminalConnection.cs`. |
| `Settings/` | Réglages enregistrés en JSON. |
| `Media/` | Infos médias (`MediaInfoLib`, `MediaReport`, `MediaCache`), photos (`PhotoInfo`), tags (`AudioTags`), outils externes (`MediaTools`, `MkvEdit`, `ExifEdit`). |
| `Rights/` | Fenêtre Propriétés. `UnixRights.cs` : droits Unix (octal ↔ « rwxr-xr-x », setuid / setgid / sticky), changements partiels (cases changées seulement), utilisateurs et groupes (passwd / group), `RightsApplier` (application récursive, fichiers / dossiers séparés, erreurs listées, taille des dossiers), commandes shell sûres (chmod, chown par numéros). `LocalUnixRights.cs` : disque local sous Linux. `WindowsSecurity.cs` : NTFS (propriétaire, héritage, autorisations), attributs, parcours d'un dossier sans suivre les jonctions. |
| `Remote/` | Lot 6. `ConnectionProfile` (fiche), `ConnectionStore` (liste, import, export), `SecretVault` (coffre chiffré), `KnownHosts` (serveurs connus), `SshKeys` (clés), `ConnectionImport` (OpenSSH, FileZilla, Total Commander, WinSCP, PuTTY), `Credentials`, `SshFileSystems` (SFTP, SCP), `FtpFileSystem`, `WebDavFileSystem`, `RemoteSessions` (connexions ouvertes, source des panneaux), `RemoteTransfer` (copies, déplacements, suppressions), `RemoteArchives` (archives créées / extraites / testées depuis ou vers un serveur), `RemoteChecksums` (sommes sur un serveur), `RemoteSyncSide` (serveur comme côté de synchronisation). |

### `src/TwinPanel.App` — l'interface (Avalonia)

| Dossier | Rôle |
|---|---|
| `Views/` | Fenêtres : principale, panneau, visionneuse, éditeur, recherche, synchronisation, comparaison, renommage multiple, gestionnaire de connexions, guide (`HelpWindow`), terminal (`TerminalView`, dessin et saisie). `TExtension.cs` : `{l:T '…'}`, texte traduit dans les fichiers axaml. |
| `ViewModels/` | État et commandes de chaque écran (onglets, marquage, opérations en cours…). |
| `Services/` | Boîtes de dialogue, file des opérations, lancement des programmes externes, questions de connexion (clé du serveur, certificat, mots de passe), renvoi des fichiers distants édités. |
| `Converters/` | Icônes et couleurs selon le type de fichier. |

### `tests/TwinPanel.Core.Tests` — tests automatiques de la logique (xUnit).
