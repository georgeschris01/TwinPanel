// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinPanel.Core.Navigation;

namespace TwinPanel.Core.Settings;

public sealed class FavoriteFolder
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class PanelState
{
    public List<string> Tabs { get; set; } = [];
    public int ActiveTab { get; set; }
    public SortColumn SortColumn { get; set; } = SortColumn.Name;
    public bool SortDescending { get; set; }
}

/// <summary>
/// Jeu d'onglets enregistré : les onglets des deux panneaux, à rappeler d'un clic.
/// Un onglet sur un serveur désigne sa fiche de connexion (<see cref="SavedRemoteTab"/>), sans aucun secret.
/// </summary>
public sealed class TabSet
{
    public string Name { get; set; } = string.Empty;
    public PanelState Left { get; set; } = new();
    public PanelState Right { get; set; } = new();

    /// <summary>« 3 onglets à gauche, 2 à droite ».</summary>
    public string Summary => L.F("{0} onglet(s) à gauche, {1} à droite", Left.Tabs.Count, Right.Tabs.Count);
}

public sealed class AppSettings
{
    public PanelState Left { get; set; } = new();
    public PanelState Right { get; set; } = new();
    public List<FavoriteFolder> Favorites { get; set; } = [];
    public List<TabSet> TabSets { get; set; } = [];
    public bool ShowHidden { get; set; }
    /// <summary>Langue : « fr », « en », ou vide pour suivre celle de Windows.</summary>
    public string Language { get; set; } = string.Empty;
    /// <summary>"System", "Light" ou "Dark".</summary>
    public string Theme { get; set; } = "System";
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    /// <summary>Éditeur pour F4. Chemin vide = Bloc-notes de Windows (ou éditeur du système ailleurs).</summary>
    public string EditorName { get; set; } = string.Empty;
    public string EditorPath { get; set; } = string.Empty;
    public string EditorArguments { get; set; } = "\"%1\"";

    /// <summary>Visionneuse pour F3. Chemin vide = visionneuse intégrée.</summary>
    public string ViewerPath { get; set; } = string.Empty;
    public string ViewerArguments { get; set; } = "\"%1\"";

    /// <summary>Arborescence affichée dans chaque panneau (Ctrl+F8).</summary>
    public bool LeftTreeVisible { get; set; }
    public bool RightTreeVisible { get; set; }

    // ---- Lot 7 : personnalisation ------------------------------------------------------------

    public AppearanceSettings Appearance { get; set; } = new();
    public BehaviorSettings Behavior { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
    /// <summary>Raccourcis changés par l'utilisateur : identifiant de commande → touche (« Ctrl+Shift+O »), vide = aucun raccourci.</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = [];
    /// <summary>Boutons personnels (sous la barre d'outils).</summary>
    public List<CustomCommand> ButtonBar { get; set; } = [];
    /// <summary>Menu « Démarrer » personnel.</summary>
    public List<CustomCommand> StartMenu { get; set; } = [];
    /// <summary>Éditeur selon l'extension (la première règle qui correspond gagne ; sinon l'éditeur F4 général).</summary>
    public List<EditorRule> EditorRules { get; set; } = [];
    public TerminalSettings Terminal { get; set; } = new();
}

public enum SizeDisplay { Auto, Bytes, Kilobytes }

/// <summary>Onglets trop nombreux pour la largeur : une ligne qu'on fait défiler (flèches ‹ ›) ou plusieurs lignes.</summary>
public enum TabRowMode { Arrows, Wrap }

public sealed class AppearanceSettings
{
    /// <summary>Police de l'interface (vide : Inter, celle de TwinPanel).</summary>
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; } = 14;
    /// <summary>Hauteur des lignes des panneaux (22 compact, 28 normal, 34 aéré).</summary>
    public double RowHeight { get; set; } = 28;
    public bool ShowExtensionColumn { get; set; } = true;
    public bool ShowSizeColumn { get; set; } = true;
    public bool ShowDateColumn { get; set; } = true;
    /// <summary>Colonnes Droits (755), Propriétaire et Groupe (serveurs, Linux ; propriétaire seul sous Windows).</summary>
    public bool ShowModeColumn { get; set; }
    public bool ShowOwnerColumn { get; set; }
    public bool ShowGroupColumn { get; set; }
    /// <summary>Propriétaire / groupe en numéros (uid / gid) au lieu des noms.</summary>
    public bool OwnerAsNumber { get; set; }
    /// <summary>Colonnes médias (vidéos et musiques du PC) : durée, résolution, codec vidéo, langues audio.</summary>
    public bool ShowDurationColumn { get; set; }
    public bool ShowResolutionColumn { get; set; }
    public bool ShowVideoCodecColumn { get; set; }
    public bool ShowAudioLanguagesColumn { get; set; }
    /// <summary>Taille des dossiers calculée automatiquement en arrière-plan (PC et serveurs) au lieu de « &lt;DIR&gt; ».</summary>
    public bool ShowFolderSizes { get; set; }
    /// <summary>Vraies icônes de Windows (celles de l'Explorateur) au lieu des icônes colorées de TwinPanel.</summary>
    public bool UseShellIcons { get; set; }
    /// <summary>Ordre des colonnes choisi en glissant les titres (clés : Name, Extension, Size, Date, Mode…) ; vide : ordre par défaut.</summary>
    public List<string> ColumnOrder { get; set; } = [];
    /// <summary>Ctrl + molette : points ajoutés (ou retirés) au texte des panneaux, en plus de la taille de police.</summary>
    public int PanelZoom { get; set; }
    /// <summary>Côté des miniatures, en points (Ctrl + molette dans la vue Miniatures).</summary>
    public double ThumbnailSize { get; set; } = 160;
    /// <summary>Miniatures d'images sur les serveurs (il faut les télécharger).</summary>
    public bool ThumbnailsOnServers { get; set; } = true;
    /// <summary>Au-delà de ce poids (en Mo), une image d'un serveur reste une icône.</summary>
    public int ThumbnailMaxServerMb { get; set; } = 20;
    /// <summary>Nombre de dossiers retenus dans l'historique.</summary>
    public int RecentFoldersCount { get; set; } = TwinPanel.Core.Navigation.RecentFolders.DefaultCapacity;
    /// <summary>Nombre de fichiers au-delà duquel la vue à plat s'arrête.</summary>
    public int FlatViewLimit { get; set; } = TwinPanel.Core.Search.FlatListing.DefaultLimit;
    /// <summary>Beaucoup d'onglets : une ligne avec flèches ‹ › ou plusieurs lignes.</summary>
    public TabRowMode TabRows { get; set; } = TabRowMode.Arrows;
    /// <summary>Couleurs des icônes de la barre des lecteurs (faux : couleurs d'origine).</summary>
    public bool CustomDriveColors { get; set; }
    /// <summary>Couleur par type : Drive (disque), Usb, Network, Cd, Server (#RRGGBB ; absent : couleur d'origine).</summary>
    public Dictionary<string, string> DriveTypeColors { get; set; } = [];
    /// <summary>Couleur d'un disque précis (clé : lettre « C: »), prioritaire sur celle de son type.</summary>
    public Dictionary<string, string> DriveColors { get; set; } = [];
    /// <summary>Couleurs ajoutées par l'utilisateur à la palette (roue chromatique).</summary>
    public List<string> CustomColors { get; set; } = [];
    /// <summary>Chemins de mkvpropedit et d'exiftool (vides : recherche automatique).</summary>
    public string MkvPropEditPath { get; set; } = string.Empty;
    public string ExifToolPath { get; set; } = string.Empty;
    /// <summary>Barre des lecteurs (disques et connexions) au-dessus de chaque panneau.</summary>
    public bool ShowDriveBar { get; set; } = true;
    public string DateFormat { get; set; } = "dd/MM/yyyy HH:mm";
    public SizeDisplay SizeDisplay { get; set; } = SizeDisplay.Auto;
    /// <summary>Colorer aussi le NOM des fichiers selon leur type (sinon seulement l'icône).</summary>
    public bool ColorNames { get; set; }
    /// <summary>Couleur de chaque type (Image, Video…) : #RRGGBB ; absent = couleur par défaut.</summary>
    public Dictionary<FileCategory, string> CategoryColors { get; set; } = [];
    /// <summary>Règles par masque (« *.log;*.tmp » → gris), prioritaires sur les types.</summary>
    public List<ColorRule> ColorRules { get; set; } = [];
}

public sealed class ColorRule
{
    public string Mask { get; set; } = string.Empty;
    public string Color { get; set; } = "#8B949E";
}

public sealed class BehaviorSettings
{
    public bool ConfirmDelete { get; set; } = true;
    /// <summary>F8 envoie à la corbeille (Windows) ; sinon suppression définitive.</summary>
    public bool UseRecycleBin { get; set; } = true;
    /// <summary>Entrée sur une archive l'ouvre comme un dossier (sinon : application associée).</summary>
    public bool OpenArchivesWithEnter { get; set; } = true;
    /// <summary>Demander confirmation avant de quitter si des copies sont en cours.</summary>
    public bool ConfirmExitWithJobs { get; set; } = true;
    /// <summary>Fichier d'un serveur ouvert puis enregistré : le renvoyer sans demander (sinon : question à chaque enregistrement).</summary>
    public bool AutoReUpload { get; set; }
    /// <summary>Tentatives automatiques après une coupure pendant un transfert (0 : aucune).</summary>
    public int TransferRetries { get; set; } = 3;
    /// <summary>Cliquer sur le nom du fichier déjà sous le curseur ouvre le renommage.</summary>
    public bool ClickToRename { get; set; } = true;
    /// <summary>Relecture du dossier de serveur affiché (0 : jamais).</summary>
    public int RemoteRefreshSeconds { get; set; } = 15;
    /// <summary>Copies, déplacements, envois et extractions qui tournent en même temps ; les suivants attendent.</summary>
    public int MaxTransfers { get; set; } = 1;
    /// <summary>Bandeau d'astuce au démarrage.</summary>
    public bool ShowTips { get; set; } = true;
    /// <summary>Rang de la prochaine astuce.</summary>
    public int NextTip { get; set; }
}

public enum RemoteTabsRestore { Ask, Always, Never }

public sealed class StartupSettings
{
    /// <summary>Rouvrir les onglets de la dernière fois (sinon les dossiers ci-dessous).</summary>
    public bool RestoreTabs { get; set; } = true;
    /// <summary>Onglets ouverts sur un serveur : les rouvrir (reconnexion) au démarrage ?</summary>
    public RemoteTabsRestore RemoteTabs { get; set; } = RemoteTabsRestore.Ask;
    public string LeftPath { get; set; } = string.Empty;
    public string RightPath { get; set; } = string.Empty;
}

public enum CustomCommandKind { Command, Program, Folder, Separator }

/// <summary>
/// Un bouton de la barre personnelle ou une ligne du menu Démarrer.
/// Paramètres possibles dans Arguments et StartIn : %P dossier actif, %N nom de l'élément, %F son chemin complet,
/// %T dossier de l'autre panneau, %S éléments marqués (chemins complets entre guillemets), %% pour « % ».
/// </summary>
public sealed class CustomCommand
{
    public string Label { get; set; } = string.Empty;
    public CustomCommandKind Kind { get; set; } = CustomCommandKind.Program;
    /// <summary>Identifiant de commande TwinPanel (Kind = Command), programme ou document (Program), dossier (Folder).</summary>
    public string Target { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    /// <summary>Dossier de travail (vide : dossier actif).</summary>
    public string StartIn { get; set; } = string.Empty;
    /// <summary>Icône intégrée (« Folder », « Terminal », « Star »… ; vide : selon le type).</summary>
    public string Icon { get; set; } = string.Empty;
    /// <summary>Sous-menu (menu Démarrer seulement ; vide : premier niveau).</summary>
    public string Group { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;

    public CustomCommand Clone() => (CustomCommand)MemberwiseClone();
}

public sealed class EditorRule
{
    /// <summary>Extensions sans point, séparées par « ; » (« py;js;ts »).</summary>
    public string Extensions { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Programme, ou « internal » pour l'éditeur intégré.</summary>
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = "\"%1\"";

    public bool Matches(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).TrimStart('.');
        return ext.Length > 0 && Extensions.Split(';', ',', ' ').Any(e => e.Trim().TrimStart('*', '.').Equals(ext, StringComparison.OrdinalIgnoreCase));
    }
}

public enum TerminalKind { Auto, WindowsTerminal, PowerShell, Cmd, Custom }

public sealed class TerminalSettings
{
    public TerminalKind Kind { get; set; } = TerminalKind.Auto;
    public string CustomPath { get; set; } = string.Empty;
    /// <summary>Arguments du terminal personnalisé ; %P = dossier.</summary>
    public string CustomArguments { get; set; } = string.Empty;
}

/// <summary>Lecture / écriture des réglages en JSON (%APPDATA%\TwinPanel\settings.json).</summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SettingsStore Default { get; } = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TwinPanel", "settings.json"));

    public string FilePath { get; } = filePath;

    /// <summary>Charge les réglages ; en cas de fichier absent ou abîmé, repart des valeurs par défaut.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fichier corrompu : on le garde de côté pour ne pas perdre l'information.
            TryBackupCorrupted();
            return new AppSettings();
        }
    }

    /// <summary>Écriture atomique : fichier temporaire puis remplacement, pour ne jamais laisser un JSON à moitié écrit.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Copie complète et indépendante (la fenêtre Options travaille sur une copie jusqu'à « OK »).</summary>
    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Options), Options) ?? new AppSettings();

    /// <summary>
    /// Sauvegarde de tous les réglages dans un seul fichier (pour un autre PC ou une réinstallation).
    /// connectionsJson : la liste des connexions déjà exportée SANS secrets (ou null pour ne pas l'inclure).
    /// </summary>
    public static void ExportBundle(AppSettings settings, string? connectionsJson, string file)
    {
        var bundle = new SettingsBundle
        {
            CreatedUtc = DateTime.UtcNow,
            Settings = settings,
            Connections = connectionsJson is null ? null : JsonDocument.Parse(connectionsJson).RootElement.Clone(),
        };
        File.WriteAllText(file, JsonSerializer.Serialize(bundle, Options));
    }

    /// <summary>Lit une sauvegarde. Lève InvalidDataException si ce n'en est pas une.</summary>
    public static SettingsBundle ReadBundle(string file)
    {
        SettingsBundle? bundle;
        try { bundle = JsonSerializer.Deserialize<SettingsBundle>(File.ReadAllText(file), Options); }
        catch (JsonException ex) { throw new InvalidDataException(L.T("Ce fichier n'est pas une sauvegarde de réglages TwinPanel."), ex); }
        if (bundle?.Settings is null || bundle.Format != SettingsBundle.FormatName)
            throw new InvalidDataException(L.T("Ce fichier n'est pas une sauvegarde de réglages TwinPanel."));
        return bundle;
    }

    private void TryBackupCorrupted()
    {
        try { File.Copy(FilePath, FilePath + ".corrompu", overwrite: true); }
        catch (IOException) { /* sauvegarde de confort : sans importance si elle échoue */ }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Fichier de sauvegarde complet des réglages.</summary>
public sealed class SettingsBundle
{
    public const string FormatName = "TwinPanel-reglages";
    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public AppSettings? Settings { get; set; }
    /// <summary>Liste des connexions (sans aucun secret), au format de l'export du gestionnaire.</summary>
    public JsonElement? Connections { get; set; }
}

/// <summary>
/// Onglet sur un serveur enregistré dans les réglages : « remote:{connexion}|{chemin} ».
/// La connexion est désignée par l'identifiant de sa fiche (le nom court sftp://… change d'une session à l'autre).
/// </summary>
/// <summary>Verrou d'un onglet : aucun, verrouillé (aller ailleurs ouvre un nouvel onglet), ou verrouillé avec
/// changement de dossier permis (l'onglet revient à son dossier quand on le reprend).</summary>
public enum TabLock { None, Locked, LockedAllowChange }

/// <summary>
/// Un onglet enregistré : « chemin » seul (anciens réglages), ou « chemin␟verrou␟nom » quand il est verrouillé ou renommé,
/// suivi de « ␟plat␟miniatures » (0 ou 1) pour le mode d'affichage (jeux d'onglets seulement).
/// Le séparateur (caractère 0x1F) ne peut pas apparaître dans un chemin.
/// </summary>
public readonly record struct SavedTab(string Path, TabLock Lock = TabLock.None, string Name = "", bool Flat = false, bool Thumbnails = false)
{
    private const char Sep = '\u001F';

    public string Encode()
    {
        if (Lock == TabLock.None && Name.Length == 0 && !Flat && !Thumbnails) return Path;
        var text = $"{Path}{Sep}{(int)Lock}{Sep}{Name}";
        return Flat || Thumbnails ? $"{text}{Sep}{(Flat ? 1 : 0)}{Sep}{(Thumbnails ? 1 : 0)}" : text;
    }

    public static SavedTab Decode(string text)
    {
        var parts = (text ?? string.Empty).Split(Sep);
        if (parts.Length < 3) return new SavedTab(text ?? string.Empty);
        var lockMode = int.TryParse(parts[1], out var l) && Enum.IsDefined(typeof(TabLock), l) ? (TabLock)l : TabLock.None;
        return new SavedTab(parts[0], lockMode, parts[2], parts.Length > 3 && parts[3] == "1", parts.Length > 4 && parts[4] == "1");
    }
}

public static class SavedRemoteTab
{
    private const string Prefix = "remote:";

    public static string Encode(Guid profileId, string remotePath) => $"{Prefix}{profileId:N}|{remotePath}";

    public static bool TryDecode(string text, out Guid profileId, out string remotePath)
    {
        profileId = Guid.Empty;
        remotePath = string.Empty;
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int bar = text.IndexOf('|', Prefix.Length);
        if (bar < 0 || !Guid.TryParseExact(text[Prefix.Length..bar], "N", out profileId)) return false;
        remotePath = text[(bar + 1)..];
        if (remotePath.Length == 0 || remotePath[0] != '/') remotePath = "/" + remotePath;
        return true;
    }
}
