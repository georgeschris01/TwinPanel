// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

/// <summary>Fenêtre Propriétés. Résultat : vrai si quelque chose a été modifié (le panneau est alors rafraîchi).</summary>
public partial class PropertiesWindow : Window
{
    public PropertiesWindow()
    {
        InitializeComponent();
        OkButton.Click += async (_, _) =>
        {
            if (DataContext is not PropertiesViewModel vm) { Close(false); return; }
            if (await vm.ApplyAndCloseAsync()) Close(vm.Changed);
        };
        CloseButton.Click += (_, _) => Close((DataContext as PropertiesViewModel)?.Changed ?? false);
        Opened += async (_, _) =>
        {
            if (DataContext is not PropertiesViewModel vm) return;
            vm.PickImageAsync = PickImageAsync;
            await vm.LoadAsync();
        };
        CopyReportButton.Click += async (_, _) =>
        {
            if (DataContext is PropertiesViewModel { FullMediaReport.Length: > 0 } vm && Clipboard is { } clipboard)
                await clipboard.SetTextAsync(vm.FullMediaReport);
        };
        Closing += (_, _) =>
        {
            if (DataContext is not PropertiesViewModel vm) return;
            vm.Cancel();
            vm.CleanupMedia();
        };
    }

    private async Task<string?> PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Choisir la pochette"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
