// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TwinPanel.App.Services;
using TwinPanel.App.ViewModels;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.Views;

/// <summary>Gestionnaire de connexions. Résultat : la connexion à ouvrir (« Connecter »), ou null.</summary>
public partial class ConnectionManagerWindow : Window, IConnectionManagerHost
{
    private bool _closingConfirmed;

    public ConnectionManagerWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close(null);
        ProfileList.DoubleTapped += async (_, e) =>
        {
            if (Vm is { } vm && (e.Source as Control)?.DataContext is ProfileRow row) await vm.ConnectRowAsync(row);
        };
        ProfileList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Vm is { } vm && vm.SelectedRow is ProfileRow row) { e.Handled = true; await vm.ConnectRowAsync(row); }
        };
        BrowseCertButton.Click += async (_, _) =>
        {
            if (Vm is { } vm && await PickFileAsync(L.T("Certificat client (.pfx, .p12)")) is { } f) vm.Editor.ClientCertificatePath = f;
        };
        ((MenuFlyout)ImportButton.Flyout!).Opening += (_, _) => BuildImportMenu();
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.AttachHost(this);
            vm.CloseRequested += (_, _) => { _closingConfirmed = true; Close(vm.ConnectRequest); };
        };
        Closing += async (_, e) =>
        {
            if (_closingConfirmed || Vm is not { } vm) return;
            e.Cancel = true;
            if (await vm.CanCloseAsync()) { _closingConfirmed = true; Close(vm.ConnectRequest); }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(null); } };
    }

    private ConnectionManagerViewModel? Vm => DataContext as ConnectionManagerViewModel;

    private void BuildImportMenu()
    {
        if (Vm is not { } vm) return;
        var items = ((MenuFlyout)ImportButton.Flyout!).Items;
        items.Clear();
        foreach (var s in ConnectionManagerViewModel.ImportSources)
            items.Add(new MenuItem { Header = s.Label, Command = vm.ImportCommand, CommandParameter = s });
        items.Add(new Separator());
        items.Add(new MenuItem { Header = L.T("Fichier exporté par TwinPanel (.json)…"), Command = vm.ImportCommand, CommandParameter = null });
    }

    // ---- IConnectionManagerHost -------------------------------------------------------------------------

    public async Task<string?> PickFileAsync(string title, string? startFile = null)
    {
        IStorageFolder? start = null;
        var dir = startFile is null ? null : Path.GetDirectoryName(startFile);
        if (dir is not null && Directory.Exists(dir)) start = await StorageProvider.TryGetFolderFromPathAsync(dir);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName });
        return file?.TryGetLocalPath();
    }

    public async Task SetClipboardAsync(string text)
    {
        if (Clipboard is { } c) await c.SetTextAsync(text);
    }

    public async Task<(IReadOnlyList<ImportedConnection> Selected, bool WithPasswords)?> ChooseImportsAsync(IReadOnlyList<ImportedConnection> found)
    {
        var w = Dialogs.Create(L.T("Importer des connexions"));
        (IReadOnlyList<ImportedConnection>, bool)? result = null;
        var boxes = found.Select(c => new CheckBox
        {
            IsChecked = true,
            Content = $"{c.Profile.Name}  —  {RemoteProtocols.Label(c.Profile.Protocol)}  {c.Profile.Summary}" +
                      (c.KeyFile is not null ? L.T("  (clé)") : string.Empty) + (c.Password is not null ? L.T("  (mot de passe)") : string.Empty),
            Tag = c,
        }).ToList();
        var list = new StackPanel { Spacing = 2 };
        foreach (var b in boxes) list.Children.Add(b);
        int withPwd = found.Count(c => c.Password is not null);
        var passwords = new CheckBox
        {
            Content = L.F("Importer aussi les {0} mot(s) de passe trouvé(s) (rangés chiffrés dans le coffre)", withPwd),
            IsVisible = withPwd > 0,
        };
        var all = new Button { Content = L.T("Tout cocher") };
        var none = new Button { Content = L.T("Tout décocher") };
        all.Click += (_, _) => boxes.ForEach(b => b.IsChecked = true);
        none.Click += (_, _) => boxes.ForEach(b => b.IsChecked = false);
        var ok = new Button { Content = L.T("Importer"), IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = (boxes.Where(b => b.IsChecked == true).Select(b => (ImportedConnection)b.Tag!).ToList(), passwords.IsChecked == true);
            w.Close();
        };
        cancel.Click += (_, _) => w.Close();
        var sources = string.Join(", ", found.Select(f => f.Source).Distinct());
        w.Content = Dialogs.Stack(
            Dialogs.Text(L.F("{0} connexion(s) trouvée(s) ({1}). Rien n'est modifié dans l'autre programme.", found.Count, sources), bold: true),
            new ScrollViewer { Content = list, MaxHeight = 380, MinWidth = 560 },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { all, none } },
            passwords,
            Dialogs.Text(L.T("Les mots de passe chiffrés par Total Commander ou par le mot de passe maître de FileZilla ne sont jamais repris : ils seront demandés à la première connexion."), subtle: true),
            Dialogs.Buttons(ok, cancel));
        await w.ShowDialog(this);
        return result;
    }

    public async Task<(ConnectionProfile Profile, bool UseKey)?> ChooseServerForKeyAsync(IReadOnlyList<ConnectionProfile> profiles, string keyName)
    {
        var w = Dialogs.Create(L.T("Installer la clé sur un serveur"));
        (ConnectionProfile, bool)? result = null;
        var combo = new ComboBox
        {
            ItemsSource = profiles.Select(p => new Choice<ConnectionProfile>(p, $"{p.Name}  —  {p.Summary}")).ToList(),
            SelectedIndex = 0,
            MinWidth = 460,
        };
        var useKey = new CheckBox { Content = L.T("Ensuite, utiliser cette clé pour cette connexion"), IsChecked = true };
        var ok = new Button { Content = L.T("Installer"), IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (combo.SelectedItem is Choice<ConnectionProfile> c) { result = (c.Value, useKey.IsChecked == true); w.Close(); }
        };
        cancel.Click += (_, _) => w.Close();
        w.Content = Dialogs.Stack(
            Dialogs.Text(L.F("Ajouter la partie publique de « {0} » dans ~/.ssh/authorized_keys sur :", keyName), bold: true),
            combo,
            Dialogs.Text(L.T("TwinPanel se connecte comme d'habitude (le mot de passe est demandé s'il n'est pas enregistré), ") +
                         L.T("crée ~/.ssh si besoin, ajoute la clé si elle n'y est pas déjà, et règle les droits (700 et 600). ") +
                         L.T("La partie privée ne quitte jamais ce PC."), subtle: true),
            useKey,
            Dialogs.Buttons(ok, cancel));
        await w.ShowDialog(this);
        return result;
    }
}
