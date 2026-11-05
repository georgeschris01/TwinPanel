// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.ComponentModel;
using TwinPanel.Core;

namespace TwinPanel.App.ViewModels;

/// <summary>Une ligne de la liste : le fichier + son état d'affichage (marqué, taille calculée).</summary>
public partial class FileItemViewModel(FileEntry entry) : ObservableObject
{
    public FileEntry Entry { get; } = entry;

    [ObservableProperty] private bool _isMarked;

    /// <summary>Dossier visé pendant un glisser de fichiers (bordure sur le nom).</summary>
    [ObservableProperty] private bool _isDropTarget;

    /// <summary>Vient d'arriver par une copie / un déplacement : mis en valeur quelques secondes (rien d'autre ne change).</summary>
    [ObservableProperty] private bool _isJustArrived;

    /// <summary>Couleur de l'icône (règles des Options comprises).</summary>
    public Avalonia.Media.IBrush IconBrush => Converters.FileIcons.BrushFor(Entry);

    /// <summary>
    /// Le nom a une couleur à lui : règle par masque, ou couleur du type si « colorer les noms » est choisi.
    /// Sinon, aucune couleur n'est imposée : le nom garde celle du thème (et un élément marqué reste rouge).
    /// </summary>
    public bool HasNameColor => !IsParentLink && (Converters.FileIcons.ColorNames || Converters.FileIcons.Rules.RuleColor(Entry) is not null);

    /// <summary>Couleur du nom quand <see cref="HasNameColor"/> est vrai (utilisée par le style « colored »).</summary>
    public Avalonia.Media.IBrush NameColor => Converters.FileIcons.BrushFor(Entry);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long? _computedSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _isComputingSize;

    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsParentLink => Entry.IsParentLink;
    public FileCategory Category => Entry.Category;
    public string DisplayName => Entry.DisplayName;
    public string Extension => Entry.Extension;
    public string DisplayDate => Entry.DisplayDate;
    public string DisplayMode => Entry.DisplayMode;
    /// <summary>Vue à plat : dossier du fichier, depuis le dossier de l'onglet.</summary>
    public string FolderText { get; init; } = string.Empty;

    // ---- Vraie icône de Windows (Options → Affichage ; null : icône dessinée de TwinPanel) ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShellIcon))]
    private Avalonia.Media.Imaging.Bitmap? _shellIcon;
    public bool HasShellIcon => ShellIcon is not null;

    // ---- Colonnes médias (remplies en arrière-plan) ----
    [ObservableProperty] private string _mediaDuration = string.Empty;
    [ObservableProperty] private string _mediaResolution = string.Empty;
    [ObservableProperty] private string _mediaVideoCodec = string.Empty;
    [ObservableProperty] private string _mediaAudioLanguages = string.Empty;
    public bool HasMedia { get; private set; }

    public void SetMedia(TwinPanel.Core.Media.MediaReport? r)
    {
        HasMedia = true;
        if (r is null) return;
        MediaDuration = r.DurationText;
        MediaResolution = r.ResolutionText;
        MediaVideoCodec = r.VideoCodecText;
        MediaAudioLanguages = r.AudioLanguagesText;
    }
    public string DisplayOwner => Entry.DisplayOwner;
    public string DisplayGroup => Entry.DisplayGroup;

    /// <summary>Taille réelle : celle du fichier, ou celle calculée pour un dossier (0 sinon).</summary>
    public long EffectiveSize => IsDirectory ? ComputedSize ?? 0 : Entry.Size;

    public string SizeText => IsComputingSize ? "…"
        : IsDirectory && ComputedSize is { } s ? FileEntry.FormatListSize(s)
        : Entry.DisplaySize;
}
