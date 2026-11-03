// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.Converters;
using TwinPanel.App.Services;
using TwinPanel.Core;
using TwinPanel.Core.Customization;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Tools;
// Alias : les propriétés TerminalKind et SizeDisplay de cette fenêtre cachent les types du même nom.
using TermKind = TwinPanel.Core.Settings.TerminalKind;
using SizeMode = TwinPanel.Core.Settings.SizeDisplay;

namespace TwinPanel.App.ViewModels;

/// <summary>Ce que la fenêtre Options fournit (choix de fichiers et de dossiers).</summary>
public interface IOptionsHost
{
    Task<string?> PickFileAsync(string title, bool programs);
    Task<string?> PickFolderAsync(string title);
    Task<string?> SaveFileAsync(string title, string suggestedName);
}

public partial class CategoryColorRow(FileCategory category, string label, string hex) : ObservableObject
{
    public FileCategory Category { get; } = category;
    public string Label { get; } = label;
    public IBrush DefaultBrush => FileIcons.DefaultBrushFor(Category);

    private string _hex = hex;

    /// <summary>Couleur choisie (#RRGGBB, vide = défaut). Une valeur nulle (liste de la palette vidée) est ignorée.</summary>
    public string Hex
    {
        get => _hex;
        set
        {
            if (value is null) return;
            if (SetProperty(ref _hex, value)) OnPropertyChanged(nameof(Brush));
        }
    }

    public IBrush Brush => FileColorRules.IsColor(Hex) ? new SolidColorBrush(Color.Parse(Hex)) : DefaultBrush;

    [RelayCommand] private void Reset() => Hex = string.Empty;
}

public partial class ColorRuleRow(string mask, string hex) : ObservableObject
{
    [ObservableProperty] private string _mask = mask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Brush))]
    private string _hex = hex;

    public IBrush Brush => FileColorRules.IsColor(Hex) ? new SolidColorBrush(Color.Parse(Hex)) : Brushes.Transparent;
}

/// <summary>Couleur d'icône de la barre des lecteurs : par type (Disque, USB…) ou pour un disque précis (« C: »).</summary>
public partial class DriveColorRow(string key, string label, string hex) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    [ObservableProperty] private string _hex = hex;

    [RelayCommand] private void Reset() => Hex = string.Empty;
}

public partial class ShortcutRow(CommandInfo info, string gestures) : ObservableObject
{
    public CommandInfo Info { get; } = info;
    public string Category => Info.Category;
    public string Label => Info.Label;
    public string DefaultText => Info.DefaultGestures.Length == 0 ? L.T("(aucun)") : Info.DefaultGestures.Replace(";", L.T(" ou "));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(DisplayGestures))]
    private string _gestures = gestures;

    [ObservableProperty] private string _conflict = string.Empty;

    public string DisplayGestures => Gestures.Length == 0 ? L.T("(aucun)") : Gestures.Replace(";", L.T(" ou "));
    public bool IsChanged => !string.Equals(Gestures, Info.DefaultGestures, StringComparison.OrdinalIgnoreCase);

    [RelayCommand] private void ResetDefault() => Gestures = Info.DefaultGestures;
    [RelayCommand] private void Clear() => Gestures = string.Empty;
}

public partial class EditorRuleRow(EditorRule rule) : ObservableObject
{
    [ObservableProperty] private string _extensions = rule.Extensions;
    [ObservableProperty] private string _name = rule.Name;
    [ObservableProperty] private string _path = rule.Path;
    [ObservableProperty] private string _arguments = rule.Arguments;

    public EditorRule ToRule() => new() { Extensions = Extensions.Trim(), Name = Name.Trim(), Path = Path.Trim().Trim('"'), Arguments = string.IsNullOrWhiteSpace(Arguments) ? "\"%1\"" : Arguments.Trim() };
}

/// <summary>Une ligne de la barre de boutons ou du menu Démarrer, modifiable.</summary>
public partial class CustomCommandRow : ObservableObject
{
    public CustomCommandRow(CustomCommand c)
    {
        _label = c.Label;
        _kind = Kinds.First(k => k.Value == c.Kind);
        _target = c.Target;
        _arguments = c.Arguments;
        _startIn = c.StartIn;
        _icon = c.Icon;
        _group = c.Group;
        _tooltip = c.Tooltip;
        _command = CommandChoices.FirstOrDefault(x => x.Value == c.Target);
    }

    public static IReadOnlyList<Choice<CustomCommandKind>> Kinds { get; } =
    [
        new(CustomCommandKind.Program, L.T("Programme ou document")),
        new(CustomCommandKind.Folder, L.T("Aller à un dossier")),
        new(CustomCommandKind.Command, L.T("Commande de TwinPanel")),
        new(CustomCommandKind.Separator, L.T("Séparateur")),
    ];

    public static IReadOnlyList<Choice<string>> CommandChoices { get; } =
        CommandCatalog.All.Select(c => new Choice<string>(c.Id, $"{c.Category} — {c.Label}")).ToList();

    public static IReadOnlyList<string> Icons => CustomIcons.Names;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgram), nameof(IsFolder), nameof(IsCommand), nameof(IsSeparator), nameof(HasTarget), nameof(Display))]
    private Choice<CustomCommandKind> _kind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _target;

    [ObservableProperty] private string _arguments;
    [ObservableProperty] private string _startIn;
    [ObservableProperty] private string _icon;
    [ObservableProperty] private string _group;
    [ObservableProperty] private string _tooltip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private Choice<string>? _command;

    public bool IsProgram => Kind.Value == CustomCommandKind.Program;
    public bool IsFolder => Kind.Value == CustomCommandKind.Folder;
    public bool IsCommand => Kind.Value == CustomCommandKind.Command;
    public bool IsSeparator => Kind.Value == CustomCommandKind.Separator;
    public bool HasTarget => IsProgram || IsFolder;

    public string Display => IsSeparator ? L.T("──────── séparateur ────────")
        : $"{(Label.Length > 0 ? Label : "(sans nom)")}   ·   {(IsCommand ? Command?.Label ?? "?" : Target)}";

    partial void OnCommandChanged(Choice<string>? value)
    {
        if (value is null) return;
        if (Label.Length == 0 && CommandCatalog.Find(value.Value) is { } info) Label = info.Label;
    }

    public CustomCommand ToCommand() => new()
    {
        Label = Label.Trim(),
        Kind = Kind.Value,
        Target = IsCommand ? Command?.Value ?? string.Empty : IsSeparator ? string.Empty : Target.Trim(),
        Arguments = IsProgram ? Arguments : string.Empty,
        StartIn = IsProgram ? StartIn.Trim() : string.Empty,
        Icon = Icon,
        Group = Group.Trim(),
        Tooltip = Tooltip.Trim(),
    };
}

/// <summary>Liste modifiable : barre de boutons ou menu Démarrer.</summary>
public partial class CustomCommandListViewModel : ObservableObject
{
    private readonly Func<IOptionsHost?> _host;

    public CustomCommandListViewModel(IEnumerable<CustomCommand> items, bool hasGroups, Func<IOptionsHost?> host)
    {
        HasGroups = hasGroups;
        _host = host;
        Load(items);
    }

    public bool HasGroups { get; }
    public ObservableCollection<CustomCommandRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CustomCommandRow? _selected;

    public bool HasSelection => Selected is not null;

    public void Load(IEnumerable<CustomCommand> items)
    {
        Items.Clear();
        foreach (var c in items) Items.Add(new CustomCommandRow(c));
        Selected = Items.FirstOrDefault();
    }

    public List<CustomCommand> ToList() => Items.Select(i => i.ToCommand())
        .Where(c => c.Kind == CustomCommandKind.Separator || c.Target.Length > 0).ToList();

    [RelayCommand]
    private void Add()
    {
        var row = new CustomCommandRow(new CustomCommand { Label = L.T("Nouveau"), Kind = CustomCommandKind.Program });
        Items.Insert(Selected is null ? Items.Count : Items.IndexOf(Selected) + 1, row);
        Selected = row;
    }

    [RelayCommand]
    private void AddSeparator()
    {
        var row = new CustomCommandRow(new CustomCommand { Kind = CustomCommandKind.Separator });
        Items.Insert(Selected is null ? Items.Count : Items.IndexOf(Selected) + 1, row);
        Selected = row;
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { } s) return;
        int i = Items.IndexOf(s);
        Items.Remove(s);
        Selected = Items.Count == 0 ? null : Items[Math.Min(i, Items.Count - 1)];
    }

    [RelayCommand] private void MoveUp() => Shift(-1);
    [RelayCommand] private void MoveDown() => Shift(1);

    private void Shift(int delta)
    {
        if (Selected is not { } s) return;
        int i = Items.IndexOf(s), j = i + delta;
        if (j < 0 || j >= Items.Count) return;
        Items.Move(i, j);
        Selected = s;
    }

    [RelayCommand]
    private async Task BrowseTarget()
    {
        if (Selected is not { } s || _host() is not { } host) return;
        var path = s.IsFolder ? await host.PickFolderAsync(L.T("Dossier")) : await host.PickFileAsync(L.T("Programme ou document"), programs: false);
        if (path is null) return;
        s.Target = path;
        if (s.Label.Length == 0 || s.Label == L.T("Nouveau")) s.Label = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;
    }

    [RelayCommand]
    private async Task BrowseStartIn()
    {
        if (Selected is not { } s || _host() is not { } host) return;
        if (await host.PickFolderAsync(L.T("Dossier de travail")) is { } path) s.StartIn = path;
    }
}

/// <summary>Fenêtre Options : toutes les rubriques travaillent sur une copie des réglages, appliquée par « OK ».</summary>
public partial class OptionsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ConnectionStore? _connections;
    private readonly IDialogService _dialogs;
    private IOptionsHost? _host;

    public static IReadOnlyList<string> Pages { get; } =
        ["Display", "Colors", "Behavior", "Startup", "Shortcuts", "ButtonBar", "StartMenu", "Editors", "Terminal", "Backup", "Sync"];

    public OptionsViewModel(AppSettings settings, ConnectionStore? connections, IDialogService dialogs, string? page = null)
    {
        _settings = settings;
        _connections = connections;
        _dialogs = dialogs;
        ButtonBar = new CustomCommandListViewModel([], hasGroups: false, () => _host);
        StartMenu = new CustomCommandListViewModel([], hasGroups: true, () => _host);
        HangWatchdog.Step("Options : liste des polices");
        Fonts = BuildFontList();
        HangWatchdog.Step($"Options : {Fonts.Count} polices ; chargement des réglages");
        LoadFrom(settings);
        if (connections is not null) SyncFolder = SyncState.Load(connections.Directory).Folder;
        HangWatchdog.Step("Options : réglages chargés");
        SelectedPage = Math.Max(0, page is null ? 0 : Pages.ToList().IndexOf(page));
    }

    public void AttachHost(IOptionsHost host) => _host = host;

    [ObservableProperty] private int _selectedPage;
    [ObservableProperty] private string _message = string.Empty;

    // ---- Affichage
    /// <summary>Les noms des langues restent écrits dans leur propre langue, pour qu'on retrouve toujours la sienne.</summary>
    public static IReadOnlyList<Choice<string>> Languages { get; } =
        [new(string.Empty, L.T("Automatique (langue du système)")), new(L.French, "Français"), new(L.English, "English")];

    public static IReadOnlyList<Choice<string>> Themes { get; } = [new("System", L.T("Comme le système")), new("Light", L.T("Clair")), new("Dark", L.T("Sombre"))];
    public static IReadOnlyList<Choice<double>> RowHeights { get; } = [new(22, L.T("Compacte")), new(28, L.T("Normale")), new(34, L.T("Aérée"))];
    public static IReadOnlyList<Choice<SizeMode>> SizeDisplays { get; } =
        [new(SizeMode.Auto, L.T("Automatique (o, Ko, Mo, Go)")), new(SizeMode.Bytes, L.T("En octets (1 234 567 o)")), new(SizeMode.Kilobytes, L.T("En Ko (1 206 Ko)"))];
    public static IReadOnlyList<string> DateFormats { get; } =
        ["dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yy HH:mm", "yyyy-MM-dd HH:mm", "d MMM yyyy HH:mm", "dddd d MMMM yyyy"];
    public IReadOnlyList<string> Fonts { get; }

    [ObservableProperty] private Choice<string> _language = Languages[0];
    [ObservableProperty] private Choice<string> _theme = Themes[0];
    [ObservableProperty] private string _fontFamily = string.Empty;
    [ObservableProperty] private decimal? _fontSize = 14;
    [ObservableProperty] private Choice<double> _rowHeight = RowHeights[1];
    [ObservableProperty] private bool _showExtensionColumn;
    [ObservableProperty] private bool _showSizeColumn;
    [ObservableProperty] private bool _showDateColumn;
    [ObservableProperty] private bool _showModeColumn;
    [ObservableProperty] private bool _showOwnerColumn;
    [ObservableProperty] private bool _showGroupColumn;
    [ObservableProperty] private bool _showDurationColumn;
    [ObservableProperty] private bool _showFolderSizes;
    [ObservableProperty] private bool _useShellIcons;
    public static IReadOnlyList<Choice<TabRowMode>> TabRowModes { get; } =
        [new(TabRowMode.Arrows, L.T("Une ligne, avec les flèches ‹ › pour faire défiler")), new(TabRowMode.Wrap, L.T("Plusieurs lignes (tous les onglets visibles)"))];
    [ObservableProperty] private Choice<TabRowMode> _tabRows = TabRowModes[0];

    public static IReadOnlyList<Choice<double>> ThumbnailSizes { get; } =
        [new(96, L.T("Petites")), new(160, L.T("Moyennes")), new(256, L.T("Grandes"))];
    [ObservableProperty] private Choice<double> _thumbnailSize = ThumbnailSizes[1];
    [ObservableProperty] private bool _thumbnailsOnServers = true;
    [ObservableProperty] private decimal? _thumbnailMaxServerMb = 20;
    [ObservableProperty] private decimal? _recentFoldersCount = 30;
    [ObservableProperty] private decimal? _flatViewLimit = 200_000;
    [ObservableProperty] private decimal? _transferRetries = 3;
    [ObservableProperty] private decimal? _maxTransfers;
    [ObservableProperty] private bool _clickToRename = true;
    [ObservableProperty] private bool _showTips = true;
    private int _nextTip;
    [ObservableProperty] private decimal? _remoteRefreshSeconds = 15;
    [ObservableProperty] private bool _showResolutionColumn;
    [ObservableProperty] private bool _showVideoCodecColumn;
    [ObservableProperty] private bool _showAudioLanguagesColumn;
    [ObservableProperty] private string _mkvPropEditPath = string.Empty;
    [ObservableProperty] private string _exifToolPath = string.Empty;
    public string MkvToolNixUrl => TwinPanel.Core.Media.MediaTools.MkvToolNixUrl;
    public string ExifToolUrl => TwinPanel.Core.Media.MediaTools.ExifToolUrl;
    private string? MkvPropEditLocated => Located(MkvPropEditPath, TwinPanel.Core.Media.MediaTools.FindMkvPropEdit);
    private string? ExifToolLocated => Located(ExifToolPath, TwinPanel.Core.Media.MediaTools.FindExifTool);
    public string MkvPropEditFound => FoundText(MkvPropEditPath, MkvPropEditLocated);
    public string ExifToolFound => FoundText(ExifToolPath, ExifToolLocated);
    public bool MkvPropEditMissing => MkvPropEditLocated is null;
    public bool ExifToolMissing => ExifToolLocated is null;

    /// <summary>Un chemin tapé ou choisi passe avant la recherche automatique : le résultat se voit tout de suite, sans attendre OK.</summary>
    private static string? Located(string typed, Func<string?> search)
    {
        var path = (typed ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0) return search();
        return File.Exists(path) ? path : null;
    }

    private static string FoundText(string typed, string? located) =>
        located is not null ? L.T("Trouvé : ") + located
        : string.IsNullOrWhiteSpace(typed) ? L.T("Introuvable : pas installé, ou installé ailleurs (bouton Parcourir…).")
        : L.T("Ce fichier n'existe pas.");

    partial void OnMkvPropEditPathChanged(string value)
    {
        OnPropertyChanged(nameof(MkvPropEditFound));
        OnPropertyChanged(nameof(MkvPropEditMissing));
    }

    partial void OnExifToolPathChanged(string value)
    {
        OnPropertyChanged(nameof(ExifToolFound));
        OnPropertyChanged(nameof(ExifToolMissing));
    }

    [RelayCommand]
    private async Task BrowseMkvPropEdit()
    {
        if (_host is not null && await _host.PickFileAsync(L.T("Où se trouve mkvpropedit ?"), programs: false) is { } path) MkvPropEditPath = path;
    }

    [RelayCommand]
    private async Task BrowseExifTool()
    {
        if (_host is not null && await _host.PickFileAsync(L.T("Où se trouve exiftool ?"), programs: false) is { } path) ExifToolPath = path;
    }

    [RelayCommand]
    private static void OpenLink(string url)
    {
        try { Services.ShellService.Open(url); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* pas de navigateur : l'adresse reste affichée */ }
    }

    public static IReadOnlyList<Choice<bool>> OwnerDisplays { get; } =
        [new(false, L.T("Le nom (utilisateur, www-data)")), new(true, L.T("Le numéro (uid / gid : 1000, 33)"))];
    [ObservableProperty] private Choice<bool> _ownerDisplay = OwnerDisplays[0];
    [ObservableProperty] private bool _showDriveBar;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatePreview))]
    private string _dateFormat = "dd/MM/yyyy HH:mm";
    [ObservableProperty] private Choice<SizeMode> _sizeDisplay = SizeDisplays[0];

    public string DatePreview
    {
        get
        {
            try { return L.T("Exemple : ") + DateTime.Now.ToString(DateFormat); }
            catch (FormatException) { return L.T("Format invalide"); }
        }
    }

    // ---- Couleurs
    public static IReadOnlyList<string> Palette { get; } =
        ["#E5534B", "#F778BA", "#F2B84B", "#D29922", "#3FB68B", "#57AB5A", "#39C5CF", "#4F8EF7", "#A371F7", "#8B949E", "#FFFFFF", "#000000"];
    [ObservableProperty] private bool _colorNames;
    public ObservableCollection<CategoryColorRow> CategoryColors { get; } = [];
    public ObservableCollection<ColorRuleRow> ColorRules { get; } = [];
    [ObservableProperty] private ColorRuleRow? _selectedColorRule;
    /// <summary>Couleurs des icônes de la barre des lecteurs (décoché : couleurs d'origine).</summary>
    [ObservableProperty] private bool _customDriveColors;
    public ObservableCollection<DriveColorRow> DriveTypeColors { get; } = [];
    public ObservableCollection<DriveColorRow> DriveColors { get; } = [];

    // ---- Comportement et démarrage
    [ObservableProperty] private bool _confirmDelete;
    [ObservableProperty] private bool _useRecycleBin;
    [ObservableProperty] private bool _openArchivesWithEnter;
    [ObservableProperty] private bool _confirmExitWithJobs;
    [ObservableProperty] private bool _autoReUpload;
    [ObservableProperty] private bool _restoreTabs;
    public static IReadOnlyList<Choice<RemoteTabsRestore>> RemoteTabChoices { get; } =
        [new(RemoteTabsRestore.Ask, L.T("Demander à chaque démarrage")), new(RemoteTabsRestore.Always, L.T("Toujours les rouvrir (reconnexion automatique)")), new(RemoteTabsRestore.Never, L.T("Ne pas les rouvrir"))];
    [ObservableProperty] private Choice<RemoteTabsRestore> _remoteTabs = RemoteTabChoices[0];
    [ObservableProperty] private string _leftPath = string.Empty;
    [ObservableProperty] private string _rightPath = string.Empty;

    // ---- Raccourcis
    public ObservableCollection<ShortcutRow> AllShortcuts { get; } = [];
    public ObservableCollection<ShortcutRow> VisibleShortcuts { get; } = [];
    [ObservableProperty] private string _shortcutFilter = string.Empty;
    [ObservableProperty] private string _conflictSummary = string.Empty;
    public IReadOnlyList<string> FixedKeys { get; } = CommandCatalog.FixedKeys.Select(k => $"{k.Keys} : {k.Action}").ToList();

    // ---- Barre de boutons, menu Démarrer
    public CustomCommandListViewModel ButtonBar { get; }
    public CustomCommandListViewModel StartMenu { get; }

    // ---- Éditeurs
    public ObservableCollection<EditorRuleRow> EditorRules { get; } = [];
    [ObservableProperty] private EditorRuleRow? _selectedEditorRule;
    public string GeneralEditor =>
        string.IsNullOrWhiteSpace(_settings.EditorPath) ? ToolLauncher.DefaultEditor.Name
        : string.IsNullOrWhiteSpace(_settings.EditorName) ? _settings.EditorPath : _settings.EditorName;

    // ---- Terminal
    public static IReadOnlyList<Choice<TermKind>> TerminalKinds { get; } = !OperatingSystem.IsWindows()
    ? [new(TermKind.Auto, L.T("Automatique (terminal par défaut du système)")), new(TermKind.Custom, L.T("Autre programme…"))]
    :
    [
        new(TermKind.Auto, L.T("Automatique (Windows Terminal s'il est installé, sinon PowerShell)")),
        new(TermKind.WindowsTerminal, "Windows Terminal"),
        new(TermKind.PowerShell, "PowerShell"),
        new(TermKind.Cmd, L.T("Invite de commandes (cmd)")),
        new(TermKind.Custom, L.T("Autre programme…")),
    ];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomTerminal))]
    private Choice<TermKind> _terminalKind = TerminalKinds[0];
    [ObservableProperty] private string _terminalPath = string.Empty;
    [ObservableProperty] private string _terminalArguments = string.Empty;
    public bool IsCustomTerminal => TerminalKind.Value == TermKind.Custom;
    public string ResolvedTerminal => TerminalCommand.Resolve(new TerminalSettings { Kind = TermKind.Auto }) switch
    {
        TermKind.WindowsTerminal => L.T("Sur ce PC, « Automatique » ouvre : Windows Terminal."),
        TermKind.Custom => L.T("Sur ce PC, « Automatique » ouvre : le terminal par défaut du système."),
        _ => L.T("Sur ce PC, « Automatique » ouvre : PowerShell (Windows Terminal n'est pas installé)."),
    };

    /// <summary>Sous Linux, l'option n'est proposée que si le bureau a un thème d'icônes.</summary>
    public bool ShellIconsAvailable => TwinPanel.App.Services.ShellIcons.IsSupported;
    public string ShellIconsLabel => OperatingSystem.IsWindows()
        ? L.T("Vraies icônes de Windows (celles de l'Explorateur) au lieu des icônes colorées")
        : L.T("Vraies icônes du système (celles du thème du bureau) au lieu des icônes colorées");

    // ---- Terminal intégré
    /// <summary>Seulement les programmes installés sur ce PC, plus « Autre programme ».</summary>
    public static IReadOnlyList<Choice<TwinPanel.Core.Settings.TerminalShell>> IntegratedShells { get; } =
        [.. TwinPanel.Core.Terminal.TerminalShells.Installed().Select(s => new Choice<TwinPanel.Core.Settings.TerminalShell>(s, s switch
        {
            TwinPanel.Core.Settings.TerminalShell.PowerShell => "PowerShell",
            TwinPanel.Core.Settings.TerminalShell.PowerShell7 => "PowerShell 7",
            TwinPanel.Core.Settings.TerminalShell.Cmd => L.T("Invite de commandes (cmd)"),
            TwinPanel.Core.Settings.TerminalShell.Wsl => L.T("Linux (WSL)"),
            TwinPanel.Core.Settings.TerminalShell.UserShell => L.F("Mon shell ({0})", Path.GetFileName(TwinPanel.Core.Terminal.TerminalShells.LoginShell())),
            _ => L.T("Autre programme…"),
        }))];
    [ObservableProperty] private bool _integratedEnabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomIntegratedShell))]
    private Choice<TwinPanel.Core.Settings.TerminalShell> _integratedShell = IntegratedShells[0];
    [ObservableProperty] private string _integratedShellPath = string.Empty;
    [ObservableProperty] private decimal? _integratedFontSize = 13;
    public bool IsCustomIntegratedShell => IntegratedShell.Value == TwinPanel.Core.Settings.TerminalShell.Custom;

    [RelayCommand]
    private async Task BrowseIntegratedShell()
    {
        if (_host is not null && await _host.PickFileAsync(L.T("Programme du terminal"), programs: true) is { } path) IntegratedShellPath = path;
    }

    // ---- Sauvegarde
    [ObservableProperty] private bool _backupIncludesConnections = true;

    // ---- Chargement / enregistrement -----------------------------------------------------------------------

    private static IReadOnlyList<string> BuildFontList()
    {
        var list = new List<string> { string.Empty };
        try { list.AddRange(FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)); }
        catch (InvalidOperationException) { }
        return list;
    }

    private void LoadFrom(AppSettings s)
    {
        var a = s.Appearance;
        Language = Languages.FirstOrDefault(l => l.Value == s.Language) ?? Languages[0];
        Theme = Themes.FirstOrDefault(t => t.Value == s.Theme) ?? Themes[0];
        FontFamily = a.FontFamily;
        FontSize = (decimal)a.FontSize;
        RowHeight = RowHeights.OrderBy(r => Math.Abs(r.Value - a.RowHeight)).First();
        ShowExtensionColumn = a.ShowExtensionColumn;
        ShowSizeColumn = a.ShowSizeColumn;
        ShowDateColumn = a.ShowDateColumn;
        ShowModeColumn = a.ShowModeColumn;
        ShowOwnerColumn = a.ShowOwnerColumn;
        ShowGroupColumn = a.ShowGroupColumn;
        ShowDurationColumn = a.ShowDurationColumn;
        ShowResolutionColumn = a.ShowResolutionColumn;
        ShowVideoCodecColumn = a.ShowVideoCodecColumn;
        ShowAudioLanguagesColumn = a.ShowAudioLanguagesColumn;
        ShowFolderSizes = a.ShowFolderSizes;
        UseShellIcons = a.UseShellIcons;
        TabRows = TabRowModes.FirstOrDefault(t => t.Value == a.TabRows) ?? TabRowModes[0];
        ThumbnailSize = ThumbnailSizes.FirstOrDefault(s => s.Value == a.ThumbnailSize) ?? ThumbnailSizes[1];
        ThumbnailsOnServers = a.ThumbnailsOnServers;
        ThumbnailMaxServerMb = a.ThumbnailMaxServerMb;
        RecentFoldersCount = a.RecentFoldersCount;
        FlatViewLimit = a.FlatViewLimit;
        MkvPropEditPath = a.MkvPropEditPath;
        ExifToolPath = a.ExifToolPath;
        OwnerDisplay = OwnerDisplays[a.OwnerAsNumber ? 1 : 0];
        ShowDriveBar = a.ShowDriveBar;
        ShowHidden = s.ShowHidden;
        DateFormat = a.DateFormat;
        SizeDisplay = SizeDisplays.First(x => x.Value == a.SizeDisplay);

        ColorNames = a.ColorNames;
        CategoryColors.Clear();
        foreach (var (cat, label) in new[]
                 {
                     (FileCategory.Folder, L.T("Dossiers")), (FileCategory.Image, "Images"), (FileCategory.Video, L.T("Vidéos")), (FileCategory.Audio, L.T("Musique")),
                     (FileCategory.Archive, "Archives"), (FileCategory.Document, "Documents"), (FileCategory.Code, "Code"),
                     (FileCategory.Executable, L.T("Programmes")), (FileCategory.File, L.T("Autres fichiers")),
                 })
            CategoryColors.Add(new CategoryColorRow(cat, label, a.CategoryColors.GetValueOrDefault(cat, string.Empty)));
        ColorRules.Clear();
        foreach (var r in a.ColorRules) ColorRules.Add(new ColorRuleRow(r.Mask, r.Color));
        TwinPanel.App.Views.ColorPalette.SetCustom(a.CustomColors);
        CustomDriveColors = a.CustomDriveColors;
        DriveTypeColors.Clear();
        foreach (var (key, label) in new[] { ("Drive", L.T("Disques")), ("Usb", L.T("Clés USB / amovibles")), ("Network", L.T("Lecteurs réseau")), ("Cd", "CD / DVD"), ("Server", L.T("Connexions (serveurs)")) })
            DriveTypeColors.Add(new DriveColorRow(key, label, a.DriveTypeColors.GetValueOrDefault(key, string.Empty)));
        DriveColors.Clear();
        // Disques présents (lus hors de l'interface : un lecteur réseau peut être lent) + ceux déjà réglés mais absents.
        var savedDrives = new Dictionary<string, string>(a.DriveColors, StringComparer.OrdinalIgnoreCase);
        foreach (var (letter, hex) in savedDrives.OrderBy(d => d.Key)) DriveColors.Add(new DriveColorRow(letter, letter + "  (absent)", hex));
        _ = LoadDriveRowsAsync(savedDrives);

        var b = s.Behavior;
        ConfirmDelete = b.ConfirmDelete;
        UseRecycleBin = b.UseRecycleBin;
        OpenArchivesWithEnter = b.OpenArchivesWithEnter;
        ConfirmExitWithJobs = b.ConfirmExitWithJobs;
        AutoReUpload = b.AutoReUpload;
        TransferRetries = b.TransferRetries;
        MaxTransfers = b.MaxTransfers;
        ClickToRename = b.ClickToRename;
        ShowTips = b.ShowTips;
        _nextTip = b.NextTip;
        RemoteRefreshSeconds = b.RemoteRefreshSeconds;
        RestoreTabs = s.Startup.RestoreTabs;
        RemoteTabs = RemoteTabChoices.First(c => c.Value == s.Startup.RemoteTabs);
        LeftPath = s.Startup.LeftPath;
        RightPath = s.Startup.RightPath;

        AllShortcuts.Clear();
        foreach (var info in CommandCatalog.All)
        {
            var row = new ShortcutRow(info, CommandCatalog.GesturesFor(info, s.Shortcuts));
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ShortcutRow.Gestures)) UpdateConflicts(); };
            AllShortcuts.Add(row);
        }
        FilterShortcuts();
        UpdateConflicts();

        ButtonBar.Load(s.ButtonBar);
        StartMenu.Load(s.StartMenu);

        EditorRules.Clear();
        foreach (var r in s.EditorRules) EditorRules.Add(new EditorRuleRow(r));
        SelectedEditorRule = EditorRules.FirstOrDefault();

        TerminalKind = TerminalKinds.First(k => k.Value == s.Terminal.Kind);
        TerminalPath = s.Terminal.CustomPath;
        TerminalArguments = s.Terminal.CustomArguments;
        IntegratedEnabled = s.Terminal.IntegratedEnabled;
        IntegratedShell = IntegratedShells.FirstOrDefault(k => k.Value == s.Terminal.IntegratedShell) ?? IntegratedShells[0];
        IntegratedShellPath = s.Terminal.IntegratedShellPath;
        IntegratedFontSize = (decimal)Math.Clamp(s.Terminal.IntegratedFontSize, 8, 32);
    }

    private async Task LoadDriveRowsAsync(IReadOnlyDictionary<string, string> saved)
    {
        IReadOnlyList<DriveScanner.DriveSnapshot> drives;
        try { drives = await Task.Run(DriveScanner.Scan); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var d in drives)
        {
            var letter = d.Label; // « C: »
            var label = d.Tooltip.Split('\n')[0].Trim();
            var row = DriveColors.FirstOrDefault(r => string.Equals(r.Key, letter, StringComparison.OrdinalIgnoreCase));
            var fresh = new DriveColorRow(letter, label, row?.Hex ?? saved.GetValueOrDefault(letter, string.Empty));
            if (row is not null) DriveColors[DriveColors.IndexOf(row)] = fresh;
            else DriveColors.Add(fresh);
        }
        // Ordre alphabétique des lettres, les disques absents gardés à la fin de leur lettre.
        var sorted = DriveColors.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();
        DriveColors.Clear();
        foreach (var r in sorted) DriveColors.Add(r);
    }

    private void SaveTo(AppSettings s)
    {
        s.Language = Language.Value;
        s.Theme = Theme.Value;
        s.ShowHidden = ShowHidden;
        var a = s.Appearance;
        a.FontFamily = FontFamily ?? string.Empty;
        a.FontSize = (double)Math.Clamp(FontSize ?? 14, 9, 28);
        a.RowHeight = RowHeight.Value;
        a.ShowExtensionColumn = ShowExtensionColumn;
        a.ShowSizeColumn = ShowSizeColumn;
        a.ShowDateColumn = ShowDateColumn;
        a.ShowModeColumn = ShowModeColumn;
        a.ShowOwnerColumn = ShowOwnerColumn;
        a.ShowGroupColumn = ShowGroupColumn;
        a.ShowDurationColumn = ShowDurationColumn;
        a.ShowResolutionColumn = ShowResolutionColumn;
        a.ShowVideoCodecColumn = ShowVideoCodecColumn;
        a.ShowAudioLanguagesColumn = ShowAudioLanguagesColumn;
        a.ShowFolderSizes = ShowFolderSizes;
        a.UseShellIcons = UseShellIcons;
        a.TabRows = TabRows.Value;
        a.ThumbnailSize = ThumbnailSize.Value;
        a.ThumbnailsOnServers = ThumbnailsOnServers;
        a.ThumbnailMaxServerMb = (int)Math.Clamp(ThumbnailMaxServerMb ?? 20, 1, 200);
        a.RecentFoldersCount = (int)Math.Clamp(RecentFoldersCount ?? 30, 5, 200);
        a.FlatViewLimit = (int)Math.Clamp(FlatViewLimit ?? 200_000, 1_000, 2_000_000);
        a.CustomColors = [.. TwinPanel.App.Views.ColorPalette.Custom];
        a.CustomDriveColors = CustomDriveColors;
        a.DriveTypeColors = DriveTypeColors.Where(d => FileColorRules.IsColor(d.Hex)).ToDictionary(d => d.Key, d => d.Hex.ToUpperInvariant());
        a.DriveColors = DriveColors.Where(d => FileColorRules.IsColor(d.Hex)).ToDictionary(d => d.Key, d => d.Hex.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
        a.MkvPropEditPath = (MkvPropEditPath ?? string.Empty).Trim().Trim('"');
        a.ExifToolPath = (ExifToolPath ?? string.Empty).Trim().Trim('"');
        a.OwnerAsNumber = OwnerDisplay.Value;
        a.ShowDriveBar = ShowDriveBar;
        a.DateFormat = DateFormat;
        a.SizeDisplay = SizeDisplay.Value;
        a.ColorNames = ColorNames;
        a.CategoryColors = CategoryColors.Where(c => FileColorRules.IsColor(c.Hex)).ToDictionary(c => c.Category, c => c.Hex.ToUpperInvariant());
        a.ColorRules = ColorRules.Where(r => r.Mask.Trim().Length > 0 && FileColorRules.IsColor(r.Hex))
            .Select(r => new ColorRule { Mask = r.Mask.Trim(), Color = r.Hex.ToUpperInvariant() }).ToList();

        s.Behavior = new BehaviorSettings
        {
            ConfirmDelete = ConfirmDelete,
            UseRecycleBin = UseRecycleBin,
            OpenArchivesWithEnter = OpenArchivesWithEnter,
            ConfirmExitWithJobs = ConfirmExitWithJobs,
            AutoReUpload = AutoReUpload,
            TransferRetries = (int)Math.Clamp(TransferRetries ?? 3, 0, 10),
            MaxTransfers = (int)Math.Clamp(MaxTransfers ?? TransferGate<object>.MinLimit, TransferGate<object>.MinLimit, TransferGate<object>.MaxLimit),
            ClickToRename = ClickToRename,
            ShowTips = ShowTips,
            NextTip = _nextTip,
            RemoteRefreshSeconds = (int)Math.Clamp(RemoteRefreshSeconds ?? 15, 0, 3600),
        };
        s.Startup = new StartupSettings { RestoreTabs = RestoreTabs, RemoteTabs = RemoteTabs.Value, LeftPath = LeftPath.Trim().Trim('"'), RightPath = RightPath.Trim().Trim('"') };
        s.Shortcuts = AllShortcuts.Where(r => r.IsChanged).ToDictionary(r => r.Info.Id, r => r.Gestures);
        s.ButtonBar = ButtonBar.ToList();
        s.StartMenu = StartMenu.ToList();
        s.EditorRules = EditorRules.Select(r => r.ToRule()).Where(r => r.Extensions.Length > 0 && r.Path.Length > 0).ToList();
        s.Terminal = new TerminalSettings
        {
            Kind = TerminalKind.Value,
            CustomPath = TerminalPath.Trim().Trim('"'),
            CustomArguments = TerminalArguments.Trim(),
            IntegratedEnabled = IntegratedEnabled,
            IntegratedShell = IntegratedShell.Value,
            IntegratedShellPath = IntegratedShellPath.Trim().Trim('"'),
            IntegratedFontSize = (double)Math.Clamp(IntegratedFontSize ?? 13, 8, 32),
            IntegratedHeight = s.Terminal.IntegratedHeight,
            IntegratedSplitRatio = s.Terminal.IntegratedSplitRatio,
            CommandsPanelOpen = s.Terminal.CommandsPanelOpen,
            SavedCommands = s.Terminal.SavedCommands,
        };
    }

    /// <summary>« OK » : vérifie, puis écrit dans la copie des réglages. False s'il faut rester dans la fenêtre.</summary>
    public async Task<bool> ApplyAsync()
    {
        try { _ = DateTime.Now.ToString(DateFormat); }
        catch (FormatException) { Message = L.T("Le format de date n'est pas valide."); SelectedPage = 0; return false; }

        var badColors = CategoryColors.Where(c => c.Hex.Length > 0 && !FileColorRules.IsColor(c.Hex)).Select(c => c.Label)
            .Concat(ColorRules.Where(r => r.Hex.Length > 0 && !FileColorRules.IsColor(r.Hex)).Select(r => r.Mask)).ToList();
        if (badColors.Count > 0)
        {
            Message = L.F("Couleur invalide (format #RRGGBB attendu) : {0}.", string.Join(", ", badColors));
            SelectedPage = 1;
            return false;
        }

        if (ConflictSummary.Length > 0 &&
            !await _dialogs.ConfirmAsync(L.T("Raccourcis en double"), ConflictSummary + L.T("\n\nUne seule des commandes réagira. Enregistrer quand même ?")))
        {
            SelectedPage = 4;
            return false;
        }

        if (TerminalKind.Value == TermKind.Custom && string.IsNullOrWhiteSpace(TerminalPath))
        {
            Message = L.T("Indique le programme du terminal (ou choisis un autre terminal).");
            SelectedPage = 8;
            return false;
        }

        if (IntegratedEnabled && IsCustomIntegratedShell && string.IsNullOrWhiteSpace(IntegratedShellPath))
        {
            Message = L.T("Indique le programme du terminal intégré (ou choisis-en un autre).");
            SelectedPage = 8;
            return false;
        }

        SaveTo(_settings);
        return true;
    }

    // ---- Couleurs ---------------------------------------------------------------------------------------------

    [RelayCommand]
    private void AddColorRule()
    {
        var row = new ColorRuleRow("*.log;*.tmp", "#8B949E");
        ColorRules.Add(row);
        SelectedColorRule = row;
    }

    [RelayCommand]
    private void RemoveColorRule()
    {
        if (SelectedColorRule is { } r) ColorRules.Remove(r);
        SelectedColorRule = ColorRules.FirstOrDefault();
    }

    // ---- Raccourcis ----------------------------------------------------------------------------------------------

    partial void OnShortcutFilterChanged(string value) => FilterShortcuts();

    private void FilterShortcuts()
    {
        VisibleShortcuts.Clear();
        var words = ShortcutFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var r in AllShortcuts.Where(r => words.All(w => r.Label.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                                                  || r.Category.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                                                  || r.Gestures.Contains(w, StringComparison.OrdinalIgnoreCase))))
            VisibleShortcuts.Add(r);
    }

    private void UpdateConflicts()
    {
        var byGesture = new Dictionary<string, List<ShortcutRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in AllShortcuts)
            foreach (var g in r.Gestures.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Shortcuts.Normalize).Where(g => g.Length > 0))
            {
                if (!byGesture.TryGetValue(g, out var list)) byGesture[g] = list = [];
                list.Add(r);
            }
        foreach (var r in AllShortcuts) r.Conflict = string.Empty;
        var lines = new List<string>();
        foreach (var (g, rows) in byGesture.Where(kv => kv.Value.Count > 1))
        {
            foreach (var r in rows) r.Conflict = L.F("{0} aussi utilisé par : {1}", g, string.Join(", ", rows.Where(x => x != r).Select(x => x.Label)));
            lines.Add($"{g} : {string.Join(" / ", rows.Select(x => x.Label))}");
        }
        ConflictSummary = lines.Count == 0 ? string.Empty : L.T("Raccourcis en double :\n") + string.Join("\n", lines);
    }

    [RelayCommand]
    private void ResetAllShortcuts()
    {
        foreach (var r in AllShortcuts) r.Gestures = r.Info.DefaultGestures;
    }

    // ---- Démarrage ---------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseStartup(string side)
    {
        if (_host is null || await _host.PickFolderAsync(side == "Left" ? L.T("Dossier du panneau gauche") : L.T("Dossier du panneau droit")) is not { } path) return;
        if (side == "Left") LeftPath = path; else RightPath = path;
    }

    // ---- Éditeurs ---------------------------------------------------------------------------------------------

    [RelayCommand]
    private void AddEditorRule()
    {
        var row = new EditorRuleRow(new EditorRule { Extensions = "py;js", Name = L.T("Mon éditeur"), Path = string.Empty });
        EditorRules.Add(row);
        SelectedEditorRule = row;
    }

    [RelayCommand]
    private void RemoveEditorRule()
    {
        if (SelectedEditorRule is { } r) EditorRules.Remove(r);
        SelectedEditorRule = EditorRules.FirstOrDefault();
    }

    [RelayCommand]
    private async Task BrowseEditorRule()
    {
        if (SelectedEditorRule is not { } r || _host is null) return;
        if (await _host.PickFileAsync(L.T("Programme d'édition"), programs: true) is not { } path) return;
        r.Path = path;
        if (r.Name.Length == 0 || r.Name == L.T("Mon éditeur")) r.Name = Path.GetFileNameWithoutExtension(path);
    }

    [RelayCommand]
    private void UseInternalEditorForRule()
    {
        if (SelectedEditorRule is not { } r) return;
        r.Path = ExternalTools.InternalPath;
        r.Name = ExternalTools.InternalEditor.Name;
        r.Arguments = string.Empty;
    }

    // ---- Terminal ---------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseTerminal()
    {
        if (_host is not null && await _host.PickFileAsync(L.T("Programme du terminal"), programs: true) is { } path) TerminalPath = path;
    }

    // ---- Sauvegarde --------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ExportBackup()
    {
        if (_host is null) return;
        var file = await _host.SaveFileAsync(L.T("Sauvegarder les réglages"), $"twinpanel-reglages-{DateTime.Now:yyyy-MM-dd}.json");
        if (file is null) return;
        try
        {
            var snapshot = SettingsStore.Clone(_settings);
            SaveTo(snapshot); // y compris ce qui vient d'être modifié dans cette fenêtre
            string? connections = null;
            if (BackupIncludesConnections && _connections is not null && _connections.Profiles.Count > 0)
            {
                var tmp = Path.GetTempFileName();
                try { _connections.Export(_connections.Profiles, tmp); connections = await File.ReadAllTextAsync(tmp); }
                finally { File.Delete(tmp); }
            }
            SettingsStore.ExportBundle(snapshot, connections, file);
            Message = L.F("Réglages sauvegardés dans {0}", Path.GetFileName(file)) + (connections is null ? "." : L.T(" (avec la liste des connexions, sans aucun mot de passe)."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await _dialogs.ShowErrorAsync(L.T("Sauvegarde"), ex.Message); }
    }

    [RelayCommand]
    private async Task ImportBackup()
    {
        if (_host is null) return;
        var file = await _host.PickFileAsync(L.T("Sauvegarde de réglages TwinPanel (.json)"), programs: false);
        if (file is null) return;
        SettingsBundle bundle;
        try { bundle = SettingsStore.ReadBundle(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await _dialogs.ShowErrorAsync(L.T("Restaurer"), ex.Message);
            return;
        }
        if (!await _dialogs.ConfirmAsync(L.T("Restaurer les réglages"),
                L.F("Remplacer les réglages affichés par ceux de la sauvegarde du {0:dd/MM/yyyy HH:mm} ?\n", bundle.CreatedUtc.ToLocalTime()) +
                L.T("Ils ne seront appliqués qu'avec « OK »."))) return;

        var restored = bundle.Settings!;
        // Les favoris, les jeux d'onglets et l'éditeur F4 font aussi partie de la sauvegarde.
        _settings.Favorites = restored.Favorites;
        _settings.TabSets = restored.TabSets;
        _settings.EditorName = restored.EditorName;
        _settings.EditorPath = restored.EditorPath;
        _settings.EditorArguments = restored.EditorArguments;
        _settings.ViewerPath = restored.ViewerPath;
        _settings.ViewerArguments = restored.ViewerArguments;
        LoadFrom(restored);
        OnPropertyChanged(nameof(GeneralEditor));
        var msg = L.T("Réglages de la sauvegarde chargés : « OK » pour les appliquer.");

        if (bundle.Connections is { } json && _connections is not null)
        {
            try
            {
                var doc = System.Text.Json.JsonSerializer.Deserialize<ConnectionDocument>(json.GetRawText(), new System.Text.Json.JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                });
                var fresh = doc?.Profiles.Where(p => !_connections.Profiles.Any(x =>
                    string.Equals(x.Name, p.Name, StringComparison.CurrentCultureIgnoreCase) && string.Equals(x.Host, p.Host, StringComparison.OrdinalIgnoreCase))).ToList() ?? [];
                if (fresh.Count > 0 && await _dialogs.ConfirmAsync(L.T("Connexions"),
                        L.F("La sauvegarde contient {0} connexion(s) absente(s) d'ici. Les ajouter au gestionnaire ?\n\nUne sauvegarde ne contient ni mots de passe ni clés : une connexion par clé revient réglée par mot de passe. Pour tout transporter, utilisez Options → Synchronisation.", fresh.Count)))
                    msg += L.F(" {0} connexion(s) ajoutée(s).", _connections.AddImported(fresh));
            }
            catch (System.Text.Json.JsonException) { msg += L.T(" (liste des connexions illisible, ignorée)"); }
        }
        Message = msg;
    }

    // ---- Synchronisation entre machines ---------------------------------------------------------------------

    [ObservableProperty] private string _syncFolder = string.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendSyncCommand), nameof(FetchSyncCommand))] private bool _syncIdle = true;

    [RelayCommand]
    private async Task BrowseSyncFolder()
    {
        if (_host is not null && await _host.PickFolderAsync(L.T("Dossier de synchronisation")) is { } path) SyncFolder = path;
    }

    /// <summary>L'état est écrit après chaque envoi ou récupération, sans attendre « OK » : annuler les Options ne les défait pas.</summary>
    private SyncState? CurrentSyncState()
    {
        var folder = SyncFolder.Trim();
        if (_connections is null) return null;
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            Message = L.T("Choisis d'abord un dossier qui existe.");
            return null;
        }
        var state = SyncState.Load(_connections.Directory);
        state.Folder = folder;
        return state;
    }

    private async Task<string?> AskSyncPassphraseAsync(bool isNew)
    {
        var prompts = new ConnectionPrompts();
        var first = await prompts.AskSecretAsync(new SecretRequest(L.T("Phrase secrète"),
            isNew ? L.F("Choisis une phrase secrète d'au moins {0} caractères :", ConfigSync.MinPassphraseLength) : L.T("Phrase secrète de la synchronisation :"), false));
        if (first is null || first.Value.Length == 0) return null;
        if (!isNew) return first.Value;
        if (first.Value.Length < ConfigSync.MinPassphraseLength)
        {
            Message = L.F("Il faut au moins {0} caractères : ce fichier peut se retrouver en ligne.", ConfigSync.MinPassphraseLength);
            return null;
        }
        var second = await prompts.AskSecretAsync(new SecretRequest(L.T("Phrase secrète"), L.T("Retape-la pour confirmer :"), false));
        if (second is null) return null;
        if (second.Value == first.Value) return first.Value;
        Message = L.T("Les deux phrases ne sont pas identiques. Rien n'a été envoyé.");
        return null;
    }

    /// <summary>Le coffre doit être ouvert : les mots de passe et les clés voyagent aussi.</summary>
    private async Task<bool> SyncVaultReadyAsync() =>
        _connections!.Vault.IsUnlocked || await new ConnectionPrompts().UnlockVaultAsync(_connections.Vault);

    private async Task RunSyncAsync(Func<Task> work)
    {
        SyncIdle = false;
        try { await work(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SyncPassphraseException
                                       or VaultLockedException or VaultKeyringException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            await _dialogs.ShowErrorAsync(L.T("Synchronisation"), ex.Message);
        }
        finally { SyncIdle = true; }
    }

    [RelayCommand(CanExecute = nameof(SyncIdle))]
    private Task SendSync() => RunSyncAsync(async () =>
    {
        if (CurrentSyncState() is not { } state || !await SyncVaultReadyAsync()) return;
        bool isNew = !ConfigSync.Exists(state.Folder);
        if (isNew && !await _dialogs.ConfirmAsync(L.T("Synchronisation"), L.T("Tes connexions, tes clés SSH et tes mots de passe enregistrés vont être chiffrés dans un fichier, avec une phrase secrète que tu vas choisir.\n\nElle te sera demandée à chaque envoi et sur chaque machine. TwinPanel ne la garde pas et ne l'envoie nulle part.\n\nSi tu l'oublies, personne ne pourra plus ouvrir ce fichier. Note-la en lieu sûr.\n\nContinuer ?"))) return;
        if (await AskSyncPassphraseAsync(isNew) is not { } phrase) return;

        var snapshot = SettingsStore.Clone(_settings);
        SaveTo(snapshot); // y compris ce qui vient d'être modifié dans cette fenêtre
        var connections = _connections!;
        if (!isNew)
        {
            // Une faute de frappe ne doit pas rechiffrer le fichier avec une autre phrase.
            var previous = await Task.Run(() => ConfigSync.Read(state.Folder, phrase));
            if (previous.Revision != state.LastRevision && !await _dialogs.ConfirmAsync(L.T("Synchronisation"),
                    L.F("Le fichier a été envoyé par {0} le {1:dd/MM/yyyy HH:mm}, et cette machine ne l'a pas récupéré.\nL'envoi va le remplacer : ce qui n'existe que là-bas sera perdu. Continuer ?", previous.Machine, previous.SavedUtc.ToLocalTime()))) return;
        }
        var package = ConfigSync.Build(snapshot, connections);
        await Task.Run(() => ConfigSync.Write(state.Folder, phrase, package));
        state.LastRevision = package.Revision;
        state.Save(connections.Directory);
        Message = L.F("Configuration envoyée : {0} connexion(s), {1} secret(s).", package.Connections.Profiles.Count, package.Secrets.Count);
    });

    [RelayCommand(CanExecute = nameof(SyncIdle))]
    private Task FetchSync() => RunSyncAsync(async () =>
    {
        if (CurrentSyncState() is not { } state) return;
        if (!ConfigSync.Exists(state.Folder))
        {
            Message = L.F("Aucun fichier {0} dans ce dossier.", ConfigSync.FileName);
            return;
        }
        if (!await SyncVaultReadyAsync() || await AskSyncPassphraseAsync(isNew: false) is not { } phrase) return;
        var package = await Task.Run(() => ConfigSync.Read(state.Folder, phrase));
        if (!await _dialogs.ConfirmAsync(L.T("Synchronisation"),
                L.F("Récupérer la configuration envoyée par {0} le {1:dd/MM/yyyy HH:mm} ?\nLes réglages communs seront remplacés. Les connexions d'ici absentes du fichier sont gardées.", package.Machine, package.SavedUtc.ToLocalTime()))) return;

        var connections = _connections!;
        // Sur le fil de l'interface : la barre des lecteurs suit les changements de la liste.
        int count = ConfigSync.ApplyConnections(package, connections);
        state.LastRevision = package.Revision;
        state.Save(connections.Directory);

        var restored = SettingsStore.Clone(_settings);
        SaveTo(restored); // ce qui vient d'être modifié ici et ne voyage pas reste affiché
        package.Settings.ApplyTo(restored);
        _settings.Terminal.SavedCommands = restored.Terminal.SavedCommands;
        LoadFrom(restored);
        Message = L.F("{0} connexion(s) reçue(s). Réglages chargés : « OK » pour les appliquer.", count);
    });
}

/// <summary>Nom lisible des icônes intégrées.</summary>
public sealed class IconNameConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IconNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value switch
    {
        "" or null => L.T("Automatique (selon le type)"),
        "Program" => L.T("Programme"),
        "Terminal" => "Terminal",
        "Folder" => L.T("Dossier"),
        "Star" => L.T("Étoile"),
        "Server" => L.T("Serveur"),
        "Search" => L.T("Loupe"),
        "Edit" => L.T("Crayon"),
        "Refresh" => L.T("Flèches"),
        "Home" => L.T("Maison"),
        "Swap" => L.T("Échange"),
        "Eye" => "Œil",
        "Menu" => "Menu",
        "Up" => L.T("Flèche haut"),
        string s => s,
        _ => value.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
