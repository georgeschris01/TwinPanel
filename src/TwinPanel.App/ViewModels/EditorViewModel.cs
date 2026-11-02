// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.ComponentModel;
using TwinPanel.App.Services;
using TwinPanel.Core;
using TwinPanel.Core.Viewer;

namespace TwinPanel.App.ViewModels;

/// <summary>Éditeur de texte intégré : Ctrl+S enregistrer, Ctrl+F rechercher, Ctrl+H remplacer, Ctrl+G aller à la ligne.</summary>
public partial class EditorViewModel(string filePath, int startLine, IDialogService dialogs) : ObservableObject
{
    private TextDocument? _doc;
    private string _savedText = string.Empty;

    public string FilePath { get; } = filePath;
    public int StartLine { get; } = Math.Max(1, startLine);
    public IDialogService Dialogs { get; } = dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified), nameof(Title))]
    private string _text = string.Empty;

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _wrap;
    [ObservableProperty] private string _findText = string.Empty;
    [ObservableProperty] private string _replaceText = string.Empty;
    [ObservableProperty] private bool _showReplace;
    [ObservableProperty] private string _position = L.T("Ligne 1, colonne 1");
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _formatInfo = string.Empty;

    public bool IsModified => IsLoaded && Text != _savedText;
    public string Title => L.F("{0}{1} — Éditeur TwinPanel", (IsModified ? "● " : string.Empty), Path.GetFileName(FilePath));

    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(Title));

    /// <summary>Charge le fichier. Retourne false (après un message) si c'est impossible : la fenêtre se ferme alors.</summary>
    public async Task<bool> LoadAsync()
    {
        try
        {
            _doc = await Task.Run(() => TextDocument.Load(FilePath));
            _savedText = _doc.Text;
            Text = _doc.Text;
            FormatInfo = $"{_doc.EncodingLabel}  ·  {_doc.NewLineLabel}";
            IsLoaded = true;
            return true;
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.ShowErrorAsync(L.T("Éditeur"), ex.Message);
            return false;
        }
    }

    public async Task<bool> SaveAsync()
    {
        if (_doc is null) return false;
        try
        {
            var updated = _doc with { Text = Text };
            await Task.Run(() => updated.Save(FilePath));
            _doc = updated;
            _savedText = Text;
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(Title));
            Status = L.F("Enregistré à {0:HH:mm:ss}", DateTime.Now);
            return true;
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.ShowErrorAsync(L.T("Enregistrer"), ex.Message);
            return false;
        }
    }

    /// <summary>Position du texte recherché après « from » (insensible à la casse, reprend au début) ; -1 si absent.</summary>
    public int FindFrom(int from)
    {
        if (FindText.Length == 0 || Text.Length == 0) return -1;
        from = Math.Clamp(from, 0, Text.Length);
        int i = Text.IndexOf(FindText, from, StringComparison.CurrentCultureIgnoreCase);
        if (i < 0 && from > 0) i = Text.IndexOf(FindText, 0, StringComparison.CurrentCultureIgnoreCase);
        Status = i < 0 ? L.F("« {0} » introuvable", FindText) : string.Empty;
        return i;
    }

    /// <summary>Remplace toutes les occurrences ; retourne leur nombre.</summary>
    public int ReplaceAll()
    {
        if (FindText.Length == 0) return 0;
        int count = 0, i = 0;
        var sb = new System.Text.StringBuilder(Text.Length);
        while (true)
        {
            int j = Text.IndexOf(FindText, i, StringComparison.CurrentCultureIgnoreCase);
            if (j < 0) break;
            sb.Append(Text, i, j - i).Append(ReplaceText);
            i = j + FindText.Length;
            count++;
        }
        if (count == 0) { Status = L.F("« {0} » introuvable", FindText); return 0; }
        sb.Append(Text, i, Text.Length - i);
        Text = sb.ToString();
        Status = L.F("{0} remplacement(s)", count);
        return count;
    }

    /// <summary>Position (index dans le texte) du début d'une ligne (numérotée à partir de 1).</summary>
    public int OffsetOfLine(int line)
    {
        int offset = 0;
        for (int l = 1; l < line; l++)
        {
            int next = Text.IndexOf('\n', offset);
            if (next < 0) return offset;
            offset = next + 1;
        }
        return offset;
    }

    public void UpdatePosition(int caret)
    {
        caret = Math.Clamp(caret, 0, Text.Length);
        int line = 1, lineStart = 0;
        for (int i = 0; i < caret; i++)
            if (Text[i] == '\n') { line++; lineStart = i + 1; }
        Position = L.F("Ligne {0}, colonne {1}", line, caret - lineStart + 1);
    }
}
