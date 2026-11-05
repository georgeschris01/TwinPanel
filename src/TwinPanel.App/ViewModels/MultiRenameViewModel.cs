// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TwinPanel.Core;
using TwinPanel.Core.Operations;

namespace TwinPanel.App.ViewModels;

public sealed record CaseChoice(CaseMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Fenêtre de renommage multiple (Ctrl+M) : l'aperçu se met à jour à chaque frappe.</summary>
public partial class MultiRenameViewModel : ObservableObject
{
    private readonly IReadOnlyList<FileEntry> _entries;
    /// <summary>Valeurs déjà lues : chaque fichier n'est ouvert qu'une fois, quel que soit le nombre de frappes.</summary>
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _media = new(PathText.Comparer);
    private readonly CancellationTokenSource _closing = new();
    private bool _loadingMedia;

    public MultiRenameViewModel(IReadOnlyList<FileEntry> entries)
    {
        _entries = entries;
        _selectedCase = Cases[0];
        UpdatePreview();
    }

    /// <summary>Renommage sur un serveur (null : disque du PC).</summary>
    public Func<string, string, CancellationToken, Task>? Move { get; init; }

    /// <summary>Lire les tags d'un serveur obligerait à télécharger chaque fichier.</summary>
    public bool CanUseMedia => Move is null;

    public static IReadOnlyList<CaseChoice> Cases { get; } =
    [
        new(CaseMode.Unchanged, L.T("Inchangée")),
        new(CaseMode.Lower, L.T("minuscules")),
        new(CaseMode.Upper, L.T("MAJUSCULES")),
        new(CaseMode.FirstLetterUpper, L.T("Première lettre en majuscule")),
        new(CaseMode.EachWordUpper, L.T("Chaque Mot En Majuscule")),
    ];

    public ObservableCollection<RenamePreview> Preview { get; } = [];

    [ObservableProperty] private string _nameMask = "[N]";
    [ObservableProperty] private string _extensionMask = "[E]";
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _replace = string.Empty;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private CaseChoice _selectedCase;
    [ObservableProperty] private decimal? _counterStart = 1;
    [ObservableProperty] private decimal? _counterStep = 1;
    [ObservableProperty] private decimal? _counterDigits = 1;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _canRename;

    public RenameOptions Options => new()
    {
        NameMask = NameMask,
        ExtensionMask = ExtensionMask,
        Search = Search,
        Replace = Replace,
        UseRegex = UseRegex,
        Case = SelectedCase.Mode,
        CounterStart = (int)(CounterStart ?? 1),
        CounterStep = (int)(CounterStep ?? 1),
        CounterDigits = (int)(CounterDigits ?? 1),
    };

    /// <summary>Ajoute un code ([N], [C], [YMD]…) au masque du nom.</summary>
    public void Insert(string placeholder) => NameMask += placeholder;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Status) or nameof(CanRename))) UpdatePreview();
    }

    /// <summary>À la fermeture : plus besoin de lire les fichiers restants.</summary>
    public void StopReadingMedia() => _closing.Cancel();

    private void UpdatePreview()
    {
        bool usesMedia = MultiRename.UsesMedia(NameMask) || MultiRename.UsesMedia(ExtensionMask);
        if (usesMedia && CanUseMedia && !_loadingMedia && _entries.Any(e => !_media.ContainsKey(e.FullPath))) _ = ReadMediaAsync();

        var items = MultiRename.Preview(_entries, Options, CanUseMedia ? _media : null);
        Preview.Clear();
        foreach (var p in items) Preview.Add(p);

        int errors = items.Count(p => p.Error is not null);
        int changes = items.Count(p => p.Changes);
        int skipped = items.Count(p => p.Skipped is not null);
        var ignored = skipped > 0 ? L.F(" {0} ignoré(s) : valeur absente.", skipped) : string.Empty;
        CanRename = !_loadingMedia && errors == 0 && changes > 0;
        if (_loadingMedia) return; // la lecture affiche son avancement
        Status = errors > 0 ? L.F("{0} erreur(s) : corrige-les pour pouvoir renommer.", errors)
            : changes == 0 ? L.T("Aucun nom ne change.") + ignored
            : L.F("{0} élément(s) seront renommés.", changes) + ignored;
    }

    /// <summary>Les tags se lisent hors de l'interface : un dossier de musique ne doit pas figer la frappe.</summary>
    private async Task ReadMediaAsync()
    {
        _loadingMedia = true;
        var todo = _entries.Where(e => !_media.ContainsKey(e.FullPath)).ToList();
        var ct = _closing.Token;
        for (int i = 0; i < todo.Count && !ct.IsCancellationRequested; i++)
        {
            var entry = todo[i];
            Status = L.F("Lecture des médias : {0} / {1}…", i + 1, todo.Count);
            _media[entry.FullPath] = entry.IsDirectory
                ? new Dictionary<string, string>()
                : await Task.Run(() => RenameMedia.Read(entry.FullPath, entry.Size, entry.LastWriteTime), CancellationToken.None);
        }
        _loadingMedia = false;
        if (!ct.IsCancellationRequested) UpdatePreview();
    }
}
