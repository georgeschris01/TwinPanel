// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Media;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.ViewModels;

/// <summary>Une rubrique de l'onglet Média : « Général », « Audio 1 — français — E-AC-3 5.1 »…</summary>
public sealed record MediaSection(string Header, IReadOnlyList<MediaField> Fields);

/// <summary>Une piste d'un MKV, modifiable (nom, langue, par défaut, forcée).</summary>
public partial class MkvTrackRow : ObservableObject
{
    public MkvTrackRow(MediaTrack t)
    {
        Track = t;
        _name = t.Title;
        _language = t.LanguageCode;
        _isDefault = t.IsDefault;
        _isForced = t.IsForced;
    }

    public MediaTrack Track { get; }
    public string Header => $"{Track.KindLabel} {Track.Number} — {Track.Codec}";
    public bool IsVideo => Track.Kind == MediaInfoStream.Video;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _language;
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private bool _isForced;

    public MkvTrackEdit ToEdit() => new(Track.Kind, Track.Number)
    {
        Name = Name != Track.Title ? Name.Trim() : null,
        Language = !string.Equals(Language.Trim(), Track.LanguageCode, StringComparison.OrdinalIgnoreCase) && Language.Trim().Length > 0 ? Language.Trim() : null,
        IsDefault = IsDefault != Track.IsDefault ? IsDefault : null,
        IsForced = IsForced != Track.IsForced ? IsForced : null,
    };
}

/// <summary>Onglet Média de la fenêtre Propriétés : caractéristiques (MediaInfo / EXIF) et modification des tags.</summary>
public sealed partial class PropertiesViewModel
{
    /// <summary>Fichier d'un serveur : seul le début est lu pour les caractéristiques (pas tout un film).</summary>
    private const long RemoteSampleBytes = 32L * 1024 * 1024;

    public ObservableCollection<MediaSection> MediaSections { get; } = [];
    public ObservableCollection<MkvTrackRow> MkvTracks { get; } = [];
    public static IReadOnlyList<string> CommonLanguages { get; } = ["fr", "en", "nl", "de", "es", "it", "pt", "ja", "ko", "zh", "ru", "ar", "und"];

    [ObservableProperty] private bool _hasMediaTab;
    [ObservableProperty] private string _mediaMessage = string.Empty;
    [ObservableProperty] private string _fullMediaReport = string.Empty;

    // Tags (musique, MP4) — un ou plusieurs fichiers
    [ObservableProperty] private bool _canEditTags;
    [ObservableProperty] private bool _tagsIsVideo;
    [ObservableProperty] private string _tagTitle = string.Empty;
    [ObservableProperty] private string _tagArtist = string.Empty;
    [ObservableProperty] private string _tagAlbumArtist = string.Empty;
    [ObservableProperty] private string _tagAlbum = string.Empty;
    [ObservableProperty] private string _tagYear = string.Empty;
    [ObservableProperty] private string _tagTrack = string.Empty;
    [ObservableProperty] private string _tagTrackTotal = string.Empty;
    [ObservableProperty] private string _tagDisc = string.Empty;
    [ObservableProperty] private string _tagGenre = string.Empty;
    [ObservableProperty] private string _tagComposer = string.Empty;
    [ObservableProperty] private string _tagComment = string.Empty;
    [ObservableProperty] private string _tagDescription = string.Empty;
    [ObservableProperty] private string _tagTechnical = string.Empty;
    [ObservableProperty] private Bitmap? _cover;
    [ObservableProperty] private string _coverInfo = string.Empty;
    private TagValues _tagsInitial = new();
    private readonly HashSet<string> _mixedTags = [];
    private byte[]? _newCover;
    private bool _removeCover;

    /// <summary>« (plusieurs) » pour un champ dont la valeur diffère entre les fichiers choisis.</summary>
    public string MixedHint => _mixedTags.Count > 0 ? L.T("Champs vides marqués « plusieurs » : valeurs différentes selon les fichiers ; laissés vides, ils ne changent pas.") : string.Empty;

    // MKV
    [ObservableProperty] private bool _canEditMkv;
    [ObservableProperty] private string _mkvTitle = string.Empty;
    [ObservableProperty] private string _mkvToolInfo = string.Empty;
    private string _mkvTitleInitial = string.Empty;

    // Photo
    [ObservableProperty] private bool _canEditExif;
    [ObservableProperty] private string _exifTaken = string.Empty;
    [ObservableProperty] private string _exifArtist = string.Empty;
    [ObservableProperty] private string _exifCopyright = string.Empty;
    [ObservableProperty] private string _exifDescription = string.Empty;
    [ObservableProperty] private bool _exifHasGps;
    [ObservableProperty] private bool _exifRemoveGps;
    [ObservableProperty] private string _exifToolInfo = string.Empty;
    [ObservableProperty] private bool _exifToolMissing;
    [ObservableProperty] private bool _mkvToolMissing;
    public string ExifToolUrl => MediaTools.ExifToolUrl;
    public string MkvToolNixUrl => MediaTools.MkvToolNixUrl;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private static void OpenLink(string url)
    {
        try { Services.ShellService.Open(url); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* pas de navigateur : l'adresse reste affichée */ }
    }
    private PhotoInfo? _photo;

    /// <summary>Choix d'une image pour la pochette (donné par la fenêtre).</summary>
    public Func<Task<string?>>? PickImageAsync { get; set; }

    private string? _mediaTemp;

    private static bool IsMediaFile(FileEntry e) =>
        !e.IsDirectory && (e.Category is FileCategory.Video or FileCategory.Audio || PhotoInfo.IsPhoto(e.Name) || AudioTags.CanEdit(e.Name));

    private async Task LoadMediaAsync()
    {
        var files = _entries.Where(IsMediaFile).ToList();
        HasMediaTab = files.Count > 0 && files.Count == _entries.Count;
        if (!HasMediaTab) return;

        // Tags de plusieurs morceaux : on lit chacun (PC seulement).
        if (files.Count > 1)
        {
            if (IsRemote) { MediaMessage = L.T("Sur un serveur, choisis un seul fichier pour voir ou modifier ses tags."); return; }
            if (files.All(f => AudioTags.CanEdit(f.Name))) await LoadTagsAsync(files.Select(f => f.FullPath).ToList());
            MediaMessage = L.F("{0} fichiers : les tags modifiés seront écrits dans chacun.", files.Count);
            return;
        }

        var entry = files[0];
        string? local = entry.FullPath;
        bool partial = false;
        if (IsRemote && _session is not null)
        {
            MediaMessage = L.T("Lecture du début du fichier sur le serveur…");
            (local, partial) = await SampleRemoteAsync(entry);
            if (local is null) { MediaMessage = L.T("Lecture impossible sur le serveur."); return; }
        }

        var path = local;
        if (PhotoInfo.IsPhoto(entry.Name))
        {
            _photo = await Task.Run(() => PhotoInfo.Read(path));
            if (_photo is not null)
            {
                MediaSections.Add(new MediaSection("Photo", _photo.Main));
                foreach (var g in _photo.All.GroupBy(x => x.Group)) MediaSections.Add(new MediaSection(g.Key, g.Select(x => x.Field).ToList()));
                ExifTaken = _photo.Taken?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
                ExifArtist = _photo.Artist;
                ExifCopyright = _photo.Copyright;
                ExifDescription = _photo.Description;
                ExifHasGps = _photo.HasGps;
            }
            else MediaMessage = L.T("Pas de métadonnées EXIF dans cette image.");
            CanEditExif = !IsRemote;
            ExifToolMissing = MediaTools.FindExifTool() is null;
            ExifToolInfo = MediaTools.FindExifTool() is { } et ? L.T("Écrit avec ") + et
                : L.T("Pour modifier l'EXIF, installe ExifTool ou indique son chemin dans Options → Affichage.");
        }
        else
        {
            var report = await Task.Run(() => MediaAnalyzer.Analyze(path, partial));
            if (report is null)
                MediaMessage = MediaInfoLib.IsAvailable ? L.T("MediaInfo ne reconnaît pas ce fichier.") : L.T("MediaInfo est introuvable (libmediainfo) : réinstalle TwinPanel.");
            else
            {
                MediaSections.Add(new MediaSection(L.T("Général"), report.General));
                foreach (var t in report.Tracks) MediaSections.Add(new MediaSection(t.Summary, t.Fields));
                FullMediaReport = report.FullText;
                MediaMessage = partial ? L.T("Fichier d'un serveur : analyse faite sur ses 32 premiers Mo (la durée ou certaines pistes peuvent manquer).") : string.Empty;
                if (MkvEdit.IsMkv(entry.Name) && !IsRemote)
                {
                    CanEditMkv = true;
                    _mkvTitleInitial = report.General.FirstOrDefault(f => f.Label == L.T("Titre"))?.Value ?? string.Empty;
                    MkvTitle = _mkvTitleInitial;
                    foreach (var t in report.Tracks.Where(t => t.Kind is MediaInfoStream.Video or MediaInfoStream.Audio or MediaInfoStream.Text))
                        MkvTracks.Add(new MkvTrackRow(t));
                    MkvToolMissing = MediaTools.FindMkvPropEdit() is null;
                    MkvToolInfo = MediaTools.FindMkvPropEdit() is { } mk ? L.T("Modifié avec ") + mk + L.T(" (instantané, le film n'est pas réécrit).")
                        : L.T("Pour modifier les pistes, installe MKVToolNix ou indique le chemin de mkvpropedit dans Options → Affichage.");
                }
            }
            // Sur un serveur, les tags sont lus sur l'échantillon et écrits sur le fichier complet (téléchargé puis renvoyé).
            if (AudioTags.CanEdit(entry.Name)) await LoadTagsAsync([path]);
        }
    }

    /// <summary>Copie temporaire d'un fichier d'un serveur (32 premiers Mo, ou tout s'il est plus petit).</summary>
    private async Task<(string? Path, bool Partial)> SampleRemoteAsync(FileEntry entry)
    {
        if (_session is null) return (null, false);
        var dir = Path.Combine(Path.GetTempPath(), "TwinPanel", "media-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _mediaTemp = dir;
        var target = Path.Combine(dir, RemotePath.IsValidWindowsName(entry.Name) ? entry.Name : "fichier" + Path.GetExtension(entry.Name));
        var remotePath = PanelRemotePath.TryParse(entry.FullPath, out var p) ? p.Path : entry.FullPath;
        var limited = new LimitedFileStream(target, RemoteSampleBytes);
        try
        {
            await _session.UseAsync(fs => fs.DownloadAsync(remotePath, limited, CancellationToken.None), CancellationToken.None);
        }
        catch (Exception) when (limited.LimitReached) { /* assez lu */ }
        catch (Exception ex) when (ex is RemoteException or IOException)
        {
            await limited.DisposeAsync();
            MediaMessage = ex.Message;
            return (null, false);
        }
        await limited.DisposeAsync();
        return (target, limited.LimitReached);
    }

    private async Task LoadTagsAsync(IReadOnlyList<string> paths)
    {
        List<TagValues> all;
        try { all = await Task.Run(() => paths.Select(AudioTags.Read).ToList()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            MediaMessage = L.T("Tags illisibles : ") + ex.Message;
            return;
        }
        string Common(Func<TagValues, string> get, string key)
        {
            var values = all.Select(get).Distinct(StringComparer.Ordinal).ToList();
            if (values.Count == 1) return values[0];
            _mixedTags.Add(key);
            return string.Empty;
        }
        _tagsInitial = new TagValues
        {
            Title = Common(t => t.Title, nameof(TagTitle)),
            Artist = Common(t => t.Artist, nameof(TagArtist)),
            AlbumArtist = Common(t => t.AlbumArtist, nameof(TagAlbumArtist)),
            Album = Common(t => t.Album, nameof(TagAlbum)),
            Year = Common(t => t.Year, nameof(TagYear)),
            TrackNumber = Common(t => t.TrackNumber, nameof(TagTrack)),
            TrackTotal = Common(t => t.TrackTotal, nameof(TagTrackTotal)),
            DiscNumber = Common(t => t.DiscNumber, nameof(TagDisc)),
            Genre = Common(t => t.Genre, nameof(TagGenre)),
            Composer = Common(t => t.Composer, nameof(TagComposer)),
            Comment = Common(t => t.Comment, nameof(TagComment)),
            Description = Common(t => t.Description, nameof(TagDescription)),
        };
        TagTitle = _tagsInitial.Title;
        TagArtist = _tagsInitial.Artist;
        TagAlbumArtist = _tagsInitial.AlbumArtist;
        TagAlbum = _tagsInitial.Album;
        TagYear = _tagsInitial.Year;
        TagTrack = _tagsInitial.TrackNumber;
        TagTrackTotal = _tagsInitial.TrackTotal;
        TagDisc = _tagsInitial.DiscNumber;
        TagGenre = _tagsInitial.Genre;
        TagComposer = _tagsInitial.Composer;
        TagComment = _tagsInitial.Comment;
        TagDescription = _tagsInitial.Description;
        TagTechnical = all.Count == 1 ? all[0].Technical : string.Empty;
        TagsIsVideo = paths.All(AudioTags.IsVideoContainer);
        if (all.Count == 1 && all[0].Cover is { Length: > 0 } bytes) ShowCover(bytes);
        else CoverInfo = all.Count > 1 ? L.T("Pochette : changer ici la remplace dans tous les fichiers.") : L.T("Pas de pochette.");
        CanEditTags = true;
        OnPropertyChanged(nameof(MixedHint));
    }

    private void ShowCover(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            Cover = Bitmap.DecodeToWidth(ms, 240);
            CoverInfo = L.F("Pochette : {0}", FileEntry.FormatSize(bytes.Length));
        }
        catch (Exception) { Cover = null; CoverInfo = L.T("Pochette illisible."); }
    }

    [RelayCommand]
    private async Task ChangeCover()
    {
        if (PickImageAsync is null || await PickImageAsync() is not { } file) return;
        try
        {
            _newCover = await File.ReadAllBytesAsync(file);
            _removeCover = false;
            ShowCover(_newCover);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await _dialogs.ShowErrorAsync(L.T("Pochette"), ex.Message); }
    }

    [RelayCommand]
    private void RemoveCover()
    {
        _newCover = null;
        _removeCover = true;
        Cover = null;
        CoverInfo = L.T("Pochette retirée (à l'enregistrement).");
    }

    /// <summary>null : inchangé (ou laissé vide sur un champ « plusieurs ») ; sinon la nouvelle valeur ("" efface).</summary>
    private string? TagDiff(string current, string initial, string key) =>
        current == initial ? null
        : current.Trim().Length == 0 && _mixedTags.Contains(key) ? null
        : current.Trim();

    private TagChanges BuildTagChanges() => new()
    {
        Title = TagDiff(TagTitle, _tagsInitial.Title, nameof(TagTitle)),
        Artist = TagDiff(TagArtist, _tagsInitial.Artist, nameof(TagArtist)),
        AlbumArtist = TagDiff(TagAlbumArtist, _tagsInitial.AlbumArtist, nameof(TagAlbumArtist)),
        Album = TagDiff(TagAlbum, _tagsInitial.Album, nameof(TagAlbum)),
        Year = TagDiff(TagYear, _tagsInitial.Year, nameof(TagYear)),
        TrackNumber = TagDiff(TagTrack, _tagsInitial.TrackNumber, nameof(TagTrack)),
        TrackTotal = TagDiff(TagTrackTotal, _tagsInitial.TrackTotal, nameof(TagTrackTotal)),
        DiscNumber = TagDiff(TagDisc, _tagsInitial.DiscNumber, nameof(TagDisc)),
        Genre = TagDiff(TagGenre, _tagsInitial.Genre, nameof(TagGenre)),
        Composer = TagDiff(TagComposer, _tagsInitial.Composer, nameof(TagComposer)),
        Comment = TagDiff(TagComment, _tagsInitial.Comment, nameof(TagComment)),
        Description = TagDiff(TagDescription, _tagsInitial.Description, nameof(TagDescription)),
        Cover = _newCover,
        RemoveCover = _removeCover,
    };

    private async Task<bool> ApplyMediaAsync()
    {
        if (!HasMediaTab) return true;
        var errors = new List<string>();
        IsBusy = true;
        try
        {
            if (CanEditTags && BuildTagChanges() is { IsEmpty: false } changes)
            {
                var files = _entries.Where(IsMediaFile).ToList();
                foreach (var f in files)
                {
                    try
                    {
                        Status = L.T("Tags : ") + f.Name;
                        if (IsRemote && _session is not null) await WriteRemoteTagsAsync(f, changes);
                        else await Task.Run(() => AudioTags.Write(f.FullPath, changes));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                    {
                        errors.Add($"{f.Name} : {ex.Message}");
                    }
                }
                Changed = true;
            }

            if (CanEditMkv)
            {
                var edits = MkvTracks.Select(r => r.ToEdit()).Where(e => !e.IsEmpty).ToList();
                string? title = MkvTitle != _mkvTitleInitial ? MkvTitle.Trim() : null;
                if (edits.Count > 0 || title is not null)
                {
                    try
                    {
                        Status = L.T("Modification des pistes…");
                        await MkvEdit.ApplyAsync(_entries[0].FullPath, title, edits, CancellationToken.None);
                        Changed = true;
                    }
                    catch (Exception ex) when (ex is IOException or FileNotFoundException or System.ComponentModel.Win32Exception)
                    {
                        errors.Add(ex.Message);
                    }
                }
            }

            if (CanEditExif && _photo is not null)
            {
                DateTime? taken = null;
                if (ExifTaken.Trim() != (_photo.Taken?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty))
                {
                    if (!DateTime.TryParse(ExifTaken.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var t)
                        && !DateTime.TryParse(ExifTaken.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    {
                        await _dialogs.ShowErrorAsync("Photo", L.T("Date de prise de vue : écris-la comme 2024-05-31 14:30:00."));
                        return false;
                    }
                    taken = t;
                }
                var c = new ExifChanges
                {
                    Taken = taken,
                    Artist = ExifArtist != _photo.Artist ? ExifArtist.Trim() : null,
                    Copyright = ExifCopyright != _photo.Copyright ? ExifCopyright.Trim() : null,
                    Description = ExifDescription != _photo.Description ? ExifDescription.Trim() : null,
                    RemoveGps = ExifRemoveGps,
                };
                if (!c.IsEmpty)
                {
                    try
                    {
                        Status = "EXIF…";
                        await ExifEdit.ApplyAsync(_entries[0].FullPath, c, CancellationToken.None);
                        Changed = true;
                    }
                    catch (Exception ex) when (ex is IOException or FileNotFoundException or System.ComponentModel.Win32Exception)
                    {
                        errors.Add(ex.Message);
                    }
                }
            }
        }
        finally { IsBusy = false; }

        if (errors.Count > 0)
        {
            await _dialogs.ShowListAsync(L.T("Média"), L.T("Certaines modifications n'ont pas pu être faites :"), errors);
            return false;
        }
        Status = L.T("Modifications enregistrées.");
        return true;
    }

    /// <summary>Morceau d'un serveur : téléchargé entier, modifié, puis renvoyé à sa place.</summary>
    private async Task WriteRemoteTagsAsync(FileEntry f, TagChanges changes)
    {
        var item = new RemoteItem(f.Name, PanelRemotePath.TryParse(f.FullPath, out var p) ? p.Path : f.FullPath, false, f.Size, f.LastWriteTime);
        var local = await Task.Run(() => RemoteTransfer.DownloadToTempAsync(_session!, item, CancellationToken.None));
        try
        {
            await Task.Run(() => AudioTags.Write(local, changes));
            await Task.Run(() => RemoteTransfer.UploadReplaceAsync(_session!, local, item.FullPath, CancellationToken.None));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(local)!, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Nettoyage du fichier temporaire d'un serveur.</summary>
    public void CleanupMedia()
    {
        Cover = null;
        if (_mediaTemp is null) return;
        try { Directory.Delete(_mediaTemp, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _mediaTemp = null;
    }
}

/// <summary>Fichier qui refuse d'aller au-delà d'une taille : sert à ne télécharger que le début d'un fichier.</summary>
internal sealed class LimitedFileStream(string path, long limit) : FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true)
{
    public bool LimitReached { get; private set; }

    private int Allowed(int count)
    {
        long room = limit - Length;
        if (room <= 0) { LimitReached = true; throw new IOException(L.T("limite atteinte")); }
        return (int)Math.Min(count, room);
    }

    public override void Write(byte[] buffer, int offset, int count) => base.Write(buffer, offset, Allowed(count));
    public override void Write(ReadOnlySpan<byte> buffer) => base.Write(buffer[..Allowed(buffer.Length)]);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => base.WriteAsync(buffer, offset, Allowed(count), ct);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => base.WriteAsync(buffer[..Allowed(buffer.Length)], ct);

    public override void SetLength(long value)
    {
        // Reconnexion automatique : on repart d'un fichier vide.
        if (value == 0) LimitReached = false;
        base.SetLength(value);
    }
}
