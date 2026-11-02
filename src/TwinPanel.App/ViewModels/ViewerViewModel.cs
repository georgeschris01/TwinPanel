// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Viewer;

namespace TwinPanel.App.ViewModels;

public enum ViewerMode { Text, Hex, Image, Info }

public sealed record EncodingChoiceItem(TextEncodingChoice Choice, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Visionneuse (F3) et vue rapide (Ctrl+Q) : texte, hexadécimal, image ou informations.
/// N / P passent au fichier suivant / précédent du dossier.
/// </summary>
public partial class ViewerViewModel : ObservableObject, IDisposable
{
    private const int MaxTextBytes = 32 * 1024 * 1024;
    private const int MaxHexBytes = 8 * 1024 * 1024;
    private const long MaxImageBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "png", "jpg", "jpeg", "bmp", "gif", "webp", "ico" };

    private readonly IReadOnlyList<string> _files;
    private CancellationTokenSource? _cts;
    private byte[] _bytes = [];

    public ViewerViewModel(IReadOnlyList<string> files, int index, bool compact = false)
    {
        _files = files;
        _index = Math.Clamp(index, 0, Math.Max(0, files.Count - 1));
        IsCompact = compact;
        _selectedEncoding = Encodings[0];
    }

    public static IReadOnlyList<EncodingChoiceItem> Encodings { get; } =
    [
        new(TextEncodingChoice.Auto, L.T("Encodage automatique")),
        new(TextEncodingChoice.Utf8, "UTF-8"),
        new(TextEncodingChoice.Latin1, "Windows / Latin-1"),
        new(TextEncodingChoice.Utf16LE, "UTF-16 LE"),
        new(TextEncodingChoice.Utf16BE, "UTF-16 BE"),
    ];

    public bool IsCompact { get; }
    public string CurrentPath => _files.Count == 0 ? string.Empty : _files[Index];
    public string Title => _files.Count == 0 ? L.T("Visionneuse") : $"{Path.GetFileName(CurrentPath)} — {Index + 1}/{_files.Count}";

    [ObservableProperty] private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLines), nameof(IsImage), nameof(IsInfo))]
    private ViewerMode _mode = ViewerMode.Text;

    [ObservableProperty] private IReadOnlyList<string> _lines = [];
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _info = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _wrap;
    [ObservableProperty] private EncodingChoiceItem _selectedEncoding;
    [ObservableProperty] private string _findText = string.Empty;
    [ObservableProperty] private int _selectedLine = -1;
    [ObservableProperty] private bool _canShowImage;
    /// <summary>Vidéo, musique ou photo : le mode « Média » (4) montre codecs, pistes, langues ou EXIF.</summary>
    [ObservableProperty] private bool _canShowMedia;

    public bool IsLines => Mode is ViewerMode.Text or ViewerMode.Hex;
    public bool IsImage => Mode == ViewerMode.Image;
    public bool IsInfo => Mode == ViewerMode.Info;

    partial void OnSelectedEncodingChanged(EncodingChoiceItem value)
    {
        if (Mode == ViewerMode.Text) ShowText();
    }

    // ---- Chargement -------------------------------------------------------------------

    public async Task LoadAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(Title));
        ReleaseImage();
        _bytes = [];
        Lines = [];
        SelectedLine = -1;
        var path = CurrentPath;
        if (path.Length == 0) { ShowInfo(L.T("Aucun fichier.")); return; }

        try
        {
            if (Directory.Exists(path)) { await ShowFolderAsync(path, cts.Token); return; }
            if (!File.Exists(path)) { ShowInfo(L.T("Fichier introuvable.")); return; }

            var info = new FileInfo(path);
            CanShowImage = ImageExtensions.Contains(info.Extension.TrimStart('.')) && info.Length <= MaxImageBytes;
            var category = FileCategories.FromExtension(info.Extension.TrimStart('.'));
            CanShowMedia = category is FileCategory.Video or FileCategory.Audio || TwinPanel.Core.Media.PhotoInfo.IsPhoto(path);
            if (CanShowImage && await TryShowImageAsync(path, cts.Token)) return;
            // Vidéos et musiques : leurs caractéristiques plutôt que de l'hexadécimal.
            if (category is FileCategory.Video or FileCategory.Audio && await TryShowMediaAsync(path, cts.Token)) return;

            _bytes = await ReadHeadAsync(path, MaxTextBytes, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (TextDecoding.LooksBinary(_bytes)) ShowHex();
            else ShowText();
            Status = info.Length > _bytes.Length
                ? L.F("{0} — aperçu limité aux {1} premiers", FileEntry.FormatSize(info.Length), FileEntry.FormatSize(_bytes.Length))
                : FileEntry.FormatSize(info.Length);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(L.T("Lecture impossible : ") + ex.Message);
        }
    }

    [RelayCommand]
    public void ShowText()
    {
        if (!EnsureBytes()) return;
        var encoding = TextDecoding.Resolve(SelectedEncoding.Choice, _bytes);
        Lines = TextDecoding.DecodeLines(_bytes, encoding);
        Mode = ViewerMode.Text;
    }

    [RelayCommand]
    public void ShowHex()
    {
        if (!EnsureBytes()) return;
        var data = _bytes.Length > MaxHexBytes ? _bytes[..MaxHexBytes] : _bytes;
        Lines = HexFormatter.FormatAll(data);
        Mode = ViewerMode.Hex;
    }

    [RelayCommand]
    public async Task ShowImage()
    {
        if (CanShowImage) await TryShowImageAsync(CurrentPath, _cts?.Token ?? default);
    }

    [RelayCommand]
    public async Task ShowMedia()
    {
        if (CanShowMedia) await TryShowMediaAsync(CurrentPath, _cts?.Token ?? default);
    }

    private async Task<bool> TryShowMediaAsync(string path, CancellationToken ct)
    {
        try
        {
            var text = await Task.Run<string?>(() =>
            {
                if (TwinPanel.Core.Media.PhotoInfo.IsPhoto(path))
                {
                    if (TwinPanel.Core.Media.PhotoInfo.Read(path) is not { } photo) return null;
                    var sb = new System.Text.StringBuilder();
                    foreach (var f in photo.Main) sb.Append(f.Label).Append(" : ").AppendLine(f.Value);
                    sb.AppendLine();
                    foreach (var g in photo.All.GroupBy(x => x.Group))
                    {
                        sb.AppendLine("[" + g.Key + "]");
                        foreach (var (_, f) in g) sb.Append("  ").Append(f.Label).Append(" : ").AppendLine(f.Value);
                    }
                    return sb.ToString();
                }
                return TwinPanel.Core.Media.MediaAnalyzer.Analyze(path)?.ToText();
            }, ct);
            if (ct.IsCancellationRequested) return false;
            if (text is null)
            {
                if (!TwinPanel.Core.Media.MediaInfoLib.IsAvailable) Status = L.T("MediaInfo est introuvable (libmediainfo) : réinstalle TwinPanel.");
                return false;
            }
            Lines = text.Replace("\r", string.Empty).Split('\n');
            Mode = ViewerMode.Text;
            Status = L.T("Infos média (4) — Texte (1), Hexa (2)");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException) { Status = ex.Message; return false; }
    }

    [RelayCommand] public Task Next() => Move(1);
    [RelayCommand] public Task Previous() => Move(-1);

    private async Task Move(int delta)
    {
        if (_files.Count < 2) return;
        Index = (Index + delta + _files.Count) % _files.Count;
        await LoadAsync();
    }

    /// <summary>Cherche le texte à partir de la ligne suivante (et reprend au début).</summary>
    [RelayCommand]
    public void FindNext()
    {
        if (FindText.Length == 0 || Lines.Count == 0) return;
        for (int k = 1; k <= Lines.Count; k++)
        {
            int i = (Math.Max(SelectedLine, -1) + k) % Lines.Count;
            if (Lines[i].Contains(FindText, StringComparison.CurrentCultureIgnoreCase))
            {
                SelectedLine = i;
                Status = L.F("Trouvé ligne {0}", i + 1);
                return;
            }
        }
        Status = L.F("« {0} » introuvable", FindText);
    }

    private async Task<bool> TryShowImageAsync(string path, CancellationToken ct)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var fs = File.OpenRead(path);
                // Les très grandes photos sont réduites au décodage : moins de mémoire, affichage aussi net.
                return new FileInfo(path).Length > 8 * 1024 * 1024 ? Bitmap.DecodeToWidth(fs, 2560) : new Bitmap(fs);
            }, ct);
            if (ct.IsCancellationRequested) { bitmap.Dispose(); return false; }
            ReleaseImage();
            Image = bitmap;
            Mode = ViewerMode.Image;
            Status = L.F("{0} × {1} px — {2}", bitmap.PixelSize.Width, bitmap.PixelSize.Height, FileEntry.FormatSize(new FileInfo(path).Length));
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Format non reconnu par le décodeur : on retombe sur l'hexadécimal.
            return false;
        }
    }

    private async Task ShowFolderAsync(string path, CancellationToken ct)
    {
        ShowInfo(L.F("{0}{1}{2}Calcul de la taille…", Path.GetFileName(path.TrimEnd('\\', '/')), Environment.NewLine, Environment.NewLine));
        var r = await FolderSize.CalculateAsync(path, ct);
        ShowInfo($"{Path.GetFileName(path.TrimEnd('\\', '/'))}{Environment.NewLine}{Environment.NewLine}" +
                 L.F("{0} fichier(s), {1} sous-dossier(s){2}{3}", r.Files, r.Folders, Environment.NewLine, FileEntry.FormatSize(r.Bytes)));
    }

    /// <summary>Après l'affichage d'une image, les octets ne sont pas encore lus : on les lit à la demande.</summary>
    private bool EnsureBytes()
    {
        if (_bytes.Length > 0 || !File.Exists(CurrentPath)) return File.Exists(CurrentPath);
        try
        {
            using var fs = new FileStream(CurrentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[(int)Math.Min(fs.Length, MaxTextBytes)];
            int read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            _bytes = read == buffer.Length ? buffer : buffer[..read];
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(L.T("Lecture impossible : ") + ex.Message);
            return false;
        }
    }

    private void ShowInfo(string text)
    {
        Info = text;
        Mode = ViewerMode.Info;
        Status = string.Empty;
    }

    private static async Task<byte[]> ReadHeadAsync(string path, int max, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, true);
        var buffer = new byte[(int)Math.Min(fs.Length, max)];
        int read = await fs.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private void ReleaseImage()
    {
        var old = Image;
        Image = null;
        old?.Dispose();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        ReleaseImage();
        GC.SuppressFinalize(this);
    }
}

/// <summary>bool « retour à la ligne » → TextWrapping.</summary>
public sealed class WrapConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly WrapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
