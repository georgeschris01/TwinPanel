// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.Converters;
using TwinPanel.App.Services;
using TwinPanel.Core;
using TwinPanel.Core.Customization;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;

namespace TwinPanel.App.ViewModels;

/// <summary>Un bouton de la barre personnelle.</summary>
public sealed class CustomButtonViewModel(CustomCommand command)
{
    public CustomCommand Command { get; } = command;
    public bool IsSeparator => Command.Kind == CustomCommandKind.Separator;
    public bool IsButton => !IsSeparator;
    public string Label => Command.Label;
    public bool HasLabel => Command.Label.Length > 0;
    public string Tooltip => Command.Tooltip.Length > 0 ? Command.Tooltip
        : Command.Kind == CustomCommandKind.Command ? CommandCatalog.Find(Command.Target)?.Label ?? Command.Target
        : Command.Target;
    public Geometry? Icon => CustomIcons.For(Command);
}

/// <summary>Icônes intégrées proposées pour les boutons et le menu.</summary>
public static class CustomIcons
{
    public static IReadOnlyList<string> Names { get; } =
        ["", "Program", "Terminal", "Folder", "Star", "Server", "Search", "Edit", "Refresh", "Home", "Swap", "Eye", "Menu", "Up"];

    public static Geometry? For(CustomCommand c)
    {
        var name = c.Icon.Length > 0 ? c.Icon : c.Kind switch
        {
            CustomCommandKind.Folder => "Folder",
            CustomCommandKind.Command => "Star",
            _ => "Program",
        };
        return Application.Current?.TryGetResource("Icon" + name, null, out var g) == true ? g as Geometry : null;
    }
}

/// <summary>Lot 7 : réglages appliqués en direct, barre de boutons, menu Démarrer, terminal.</summary>
public partial class MainWindowViewModel
{
    /// <summary>Police, taille, hauteur des lignes ou colonnes changées : la fenêtre les applique.</summary>
    public event EventHandler? AppearanceChanged;
    /// <summary>Raccourcis changés : la fenêtre refait ses raccourcis clavier.</summary>
    public event EventHandler? ShortcutsChanged;

    public ObservableCollection<CustomButtonViewModel> ButtonBarItems { get; } = [];
    public bool HasButtonBar => ButtonBarItems.Count > 0;
    public IReadOnlyList<CustomCommand> StartMenu => _settings.StartMenu;
    public bool HasActiveJobs => Operations.Jobs.Any(j => j.IsActive);

    /// <summary>Réglages d'apparence actuels (menu des colonnes des panneaux).</summary>
    public AppearanceSettings Appearance => _settings.Appearance;

    /// <summary>Clic droit sur les titres : afficher / masquer une colonne (clé de tri : Extension, Size, Date, Mode, Owner, Group).</summary>
    public void SetColumnVisible(string key, bool visible)
    {
        var a = _settings.Appearance;
        switch (key)
        {
            case "Extension": a.ShowExtensionColumn = visible; break;
            case "Size": a.ShowSizeColumn = visible; break;
            case "Date": a.ShowDateColumn = visible; break;
            case "Mode": a.ShowModeColumn = visible; break;
            case "Owner": a.ShowOwnerColumn = visible; break;
            case "Group": a.ShowGroupColumn = visible; break;
            case "MediaDuration": a.ShowDurationColumn = visible; break;
            case "MediaResolution": a.ShowResolutionColumn = visible; break;
            case "MediaVideoCodec": a.ShowVideoCodecColumn = visible; break;
            case "MediaAudioLanguages": a.ShowAudioLanguagesColumn = visible; break;
            case "FolderSizes": a.ShowFolderSizes = visible; break;
            case "ShellIcons": a.UseShellIcons = visible; break;
            default: return;
        }
        ApplyCustomization(refreshPanels: false);
        if (key is "FolderSizes" or "ShellIcons" || (visible && key.StartsWith("Media", StringComparison.Ordinal)))
            foreach (var tab in Left.Tabs.Concat(Right.Tabs)) tab.Rebuild(); // lance la lecture des vidéos / musiques
    }

    /// <summary>Titres des colonnes glissés à la souris : nouvel ordre (vide : ordre par défaut), pour les deux panneaux.</summary>
    public void SetColumnOrder(IReadOnlyList<string> order)
    {
        _settings.Appearance.ColumnOrder = [.. order];
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ctrl + molette sur une liste : texte des panneaux plus grand / plus petit (0 : taille des Options).</summary>
    public void ChangePanelZoom(int delta)
    {
        var a = _settings.Appearance;
        a.PanelZoom = delta == 0 ? 0 : Math.Clamp(a.PanelZoom + delta, -6, 14);
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage = L.F("Texte des panneaux : {0:0} pt (Ctrl + molette ; Ctrl + clic milieu : taille normale).", PanelFontSize(a));
    }

    /// <summary>Taille du texte des listes : celle des Options + le zoom Ctrl + molette.</summary>
    public static double PanelFontSize(AppearanceSettings a) => Math.Clamp(Math.Clamp(a.FontSize, 9, 28) + a.PanelZoom, 8, 40);

    public void SetOwnerAsNumber(bool asNumber)
    {
        _settings.Appearance.OwnerAsNumber = asNumber;
        ApplyCustomization(refreshPanels: true);
    }

    /// <summary>Applique les réglages de personnalisation (au démarrage et après « OK » dans les Options).</summary>
    private void ApplyCustomization(bool refreshPanels)
    {
        Operations.Limit = _settings.Behavior.MaxTransfers;
        HangWatchdog.Step("ApplyCustomization : début");
        var a = _settings.Appearance;
        FileEntry.DateFormat = string.IsNullOrWhiteSpace(a.DateFormat) ? "dd/MM/yyyy HH:mm" : a.DateFormat;
        FileEntry.SizeDisplay = a.SizeDisplay;
        FileEntry.OwnerAsNumber = a.OwnerAsNumber;
        FolderTabViewModel.LoadMediaColumns = a.ShowDurationColumn || a.ShowResolutionColumn || a.ShowVideoCodecColumn || a.ShowAudioLanguagesColumn;
        FolderTabViewModel.ComputeFolderSizes = a.ShowFolderSizes;
        FolderTabViewModel.FlatLimit = Math.Clamp(a.FlatViewLimit, 1_000, 2_000_000);
        _recent.Capacity = Math.Clamp(a.RecentFoldersCount, 5, 200);
        FolderTabViewModel.UseShellIcons = a.UseShellIcons;
        DriveButtonViewModel.Configure(a);
        TwinPanel.Core.Media.MediaTools.MkvPropEditPath = a.MkvPropEditPath;
        TwinPanel.Core.Media.MediaTools.ExifToolPath = a.ExifToolPath;
        // Propriétaire des fichiers Windows : lu seulement si sa colonne est visible ; on relit les dossiers quand elle apparaît.
        bool reloadOwners = a.ShowOwnerColumn && !DirectoryReader.ReadOwners;
        DirectoryReader.ReadOwners = a.ShowOwnerColumn;
        FileIcons.Configure(a);
        FolderTabViewModel.OpenArchivesWithEnter = _settings.Behavior.OpenArchivesWithEnter;
        if (_remoteEdits is not null) _remoteEdits.AutoUpload = _settings.Behavior.AutoReUpload;
        Left.ShowDriveBar = Right.ShowDriveBar = a.ShowDriveBar;
        Left.ApplyDriveColors();
        Right.ApplyDriveColors();
        TwinPanel.App.Views.ColorPalette.SetCustom(a.CustomColors);

        ButtonBarItems.Clear();
        foreach (var c in _settings.ButtonBar) ButtonBarItems.Add(new CustomButtonViewModel(c));
        OnPropertyChanged(nameof(HasButtonBar));
        OnPropertyChanged(nameof(StartMenu));

        HangWatchdog.Step("ApplyCustomization : apparence");
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        HangWatchdog.Step("ApplyCustomization : raccourcis");
        ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        if (refreshPanels)
            foreach (var tab in Left.Tabs.Concat(Right.Tabs))
            {
                HangWatchdog.Step($"ApplyCustomization : réaffichage de {tab.Title}");
                tab.Rebuild();
            }
        if (reloadOwners)
            foreach (var tab in Left.Tabs.Concat(Right.Tabs).Where(t => !t.IsRemote && t.CurrentPath.Length > 0))
                _ = tab.RefreshAsync();
        HangWatchdog.Step("ApplyCustomization : fin");
    }

    /// <summary>Options (Outils → Options).</summary>
    [RelayCommand]
    private async Task OpenOptions(string? page = null)
    {
        HangWatchdog.Step("Options : ouverture demandée");
        CaptureOpenTabs(); // la sauvegarde des réglages doit contenir les onglets ouverts maintenant
        var copy = SettingsStore.Clone(_settings);
        HangWatchdog.Step("Options : copie des réglages faite");
        // L'état des panneaux et de la fenêtre continue d'évoluer pendant que les Options sont ouvertes : on le garde.
        var ok = await _dialogs.ShowOptionsAsync(copy, _connections, page);
        HangWatchdog.Step($"Options : fermée ({(ok ? "OK" : "Annuler")})");
        if (!ok) return;
        CaptureOpenTabs(); // ouverts ou fermés pendant les Options
        copy.Left = _settings.Left;
        copy.Right = _settings.Right;
        copy.OpenTerminals = _settings.OpenTerminals;
        copy.WindowWidth = _settings.WindowWidth;
        copy.WindowHeight = _settings.WindowHeight;
        copy.WindowMaximized = _settings.WindowMaximized;
        _settings = copy;
        ApplyTerminalSettings();
        OnPropertyChanged(nameof(IsTerminalEnabled));
        _terminalCommands?.Refresh();
        ApplyTheme(_settings.Theme);
        if (ShowHidden != _settings.ShowHidden) ShowHidden = _settings.ShowHidden;
        OnPropertyChanged(nameof(Favorites));
        if (!_settings.Behavior.ShowTips) IsTipVisible = false;
        ApplyCustomization(refreshPanels: true);
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
        StatusMessage = L.T("Options enregistrées.");
        if (copy.PendingTabs is { } received)
        {
            copy.PendingTabs = null;
            await OpenReceivedTabsAsync(received);
        }
        if (L.Resolve(_settings.Language, System.Globalization.CultureInfo.CurrentUICulture) != L.Language
            && await _dialogs.ConfirmAsync(L.T("Langue"), L.T("TwinPanel doit redémarrer pour changer de langue. Redémarrer maintenant ? (Sinon : au prochain démarrage.)")))
            RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Demande de fermer TwinPanel puis de le relancer.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Contexte des paramètres %P %N %F %T %S.</summary>
    private CommandContext CurrentContext()
    {
        var tab = ActivePanel.Current;
        var cur = tab.CurrentItem is { IsParentLink: false } c ? c : null;
        return new CommandContext(tab.CurrentPath, cur?.Name, cur?.FullPath, OtherPanel.Current.CurrentPath,
            tab.MarkedItems.Select(i => i.FullPath).ToList());
    }

    /// <summary>Clic sur un bouton personnel ou une ligne du menu Démarrer.</summary>
    [RelayCommand]
    private async Task RunCustomCommand(CustomCommand? command)
    {
        if (command is null) return;
        switch (command.Kind)
        {
            case CustomCommandKind.Separator:
                return;
            case CustomCommandKind.Command:
                if (CommandCatalog.Find(command.Target) is { } info && CommandCatalog.Resolve(this, info) is { } cmd)
                {
                    if (cmd.CanExecute(info.Parameter)) cmd.Execute(info.Parameter);
                }
                else await _dialogs.ShowErrorAsync(L.T("Commande"), L.F("Commande inconnue : {0}", command.Target));
                return;
            case CustomCommandKind.Folder:
                var folder = Environment.ExpandEnvironmentVariables(CommandArguments.Expand(command.Target, CurrentContext())).Trim().Trim('"');
                await ActivePanel.Current.NavigateAsync(folder);
                return;
            default:
                try
                {
                    Process.Start(CommandArguments.BuildStartInfo(command, CurrentContext()))?.Dispose();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
                {
                    await _dialogs.ShowErrorAsync(L.T("Lancer"), $"« {command.Label} » : {ex.Message}");
                }
                return;
        }
    }

    /// <summary>Ouvre un terminal dans le dossier actif ; sur un serveur SSH : une session ssh vers lui.</summary>
    [RelayCommand]
    private async Task OpenTerminal()
    {
        TerminalCommand.Target target;
        if (ActivePanel.Current.IsRemote)
        {
            if (SessionOf(ActivePanel.Current) is not { } session || !RemoteProtocols.IsSsh(session.Profile.Protocol))
            {
                await _dialogs.ShowErrorAsync("Terminal", L.T("Un terminal ne peut s'ouvrir que sur ton PC ou sur un serveur SSH (SFTP / SCP)."));
                return;
            }
            var p = session.Profile;
            var user = _connections?.FindIdentity(p.IdentityId)?.UserName is { Length: > 0 } u ? u : p.UserName;
            target = new TerminalCommand.Target(null, user, p.Host, p.Port);
            StatusMessage = L.T("Terminal ssh : l'authentification est faite par OpenSSH (ses propres clés), pas par le coffre de TwinPanel.");
        }
        else
        {
            var dir = ActivePanel.Current.IsInsideArchive ? null : ActivePanel.Current.CurrentPath;
            target = new TerminalCommand.Target(Directory.Exists(dir) ? dir : null, null, null, 0);
        }
        var (exe, args) = TerminalCommand.Build(_settings.Terminal, target);
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (target.Folder is { } f) psi.WorkingDirectory = f;
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync("Terminal", L.F("{0} : {1}\nChoisis un autre terminal dans Outils → Options → Terminal.", exe, ex.Message));
        }
    }
}
