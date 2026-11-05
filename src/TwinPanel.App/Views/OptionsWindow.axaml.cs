// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

/// <summary>Fenêtre Options. Résultat : true si « OK » (les réglages copiés ont été mis à jour).</summary>
public partial class OptionsWindow : Window, IOptionsHost
{
    public OptionsWindow()
    {
        InitializeComponent();
        OkButton.Click += async (_, _) =>
        {
            if (DataContext is OptionsViewModel vm && await vm.ApplyAsync()) Close(true);
        };
        CancelButton.Click += (_, _) => Close(false);
        DataContextChanged += (_, _) => (DataContext as OptionsViewModel)?.AttachHost(this);
        // Saisie d'un raccourci : la touche pressée dans une case « raccourci » devient son raccourci.
        AddHandler(KeyDownEvent, OnShortcutKey, RoutingStrategies.Tunnel);
    }

    private static readonly HashSet<Key> ModifierKeys =
        [Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin, Key.System];

    private void OnShortcutKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { Tag: "shortcut", DataContext: ShortcutRow row }) return;
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None) return; // passer à la case suivante
        e.Handled = true;
        if (ModifierKeys.Contains(e.Key)) return;
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Delete or Key.Back) { row.Gestures = string.Empty; return; }
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape) return;
        var gesture = new KeyGesture(e.Key, e.KeyModifiers).ToString();
        row.Gestures = gesture;
    }

    public async Task<string?> PickFileAsync(string title, bool programs)
    {
        // Un programme : d'abord la liste des programmes installés (avec « Parcourir… » si besoin).
        if (programs && OperatingSystem.IsWindows()) return await TwinPanel.App.Services.DialogService.ChooseProgramAsync(this, title);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = programs && OperatingSystem.IsWindows()
                ? [new FilePickerFileType(L.T("Programmes")) { Patterns = ["*.exe", "*.cmd", "*.bat", "*.ps1"] }, FilePickerFileTypes.All]
                : null,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName });
        return file?.TryGetLocalPath();
    }
}
