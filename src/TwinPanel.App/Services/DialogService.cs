// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TwinPanel.App.ViewModels;
using TwinPanel.App.Views;
using TwinPanel.Core;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Tools;
using Avalonia.Platform.Storage;

namespace TwinPanel.App.Services;

/// <summary>Dialogues simples construits en code (pas de dépendance externe).</summary>
public sealed class DialogService(Window owner) : IDialogService
{
    private sealed record PolicyChoice(ConflictPolicy? Policy, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly PolicyChoice[] PolicyChoices =
    [
        new(null, L.T("Demander en cas de conflit")),
        new(ConflictPolicy.Overwrite, L.T("Tout écraser")),
        new(ConflictPolicy.OverwriteOlder, L.T("Écraser seulement les plus anciens")),
        new(ConflictPolicy.Skip, L.T("Ignorer les éléments existants")),
        new(ConflictPolicy.RenameNew, L.T("Garder les deux (renommer la copie)")),
    ];

    public async Task<bool> ConfirmAsync(string title, string message) =>
        await ShowButtonsAsync(title, message, (L.T("Oui"), true), (L.T("Non"), false)) is true;

    public async Task ShowErrorAsync(string title, string message) =>
        await ShowButtonsAsync(title, message, ("OK", true));

    public async Task<ConflictPolicy?> AskConflictAsync(IReadOnlyList<string> conflicts)
    {
        var list = string.Join(Environment.NewLine, conflicts.Take(10).Select(Path.GetFileName));
        if (conflicts.Count > 10) list += L.F("{0}… et {1} autre(s)", Environment.NewLine, conflicts.Count - 10);
        var result = await ShowButtonsAsync(L.T("Conflit"),
            L.F("Ces éléments existent déjà dans la destination :{0}{1}", Environment.NewLine, list),
            (L.T("Tout écraser"), ConflictPolicy.Overwrite),
            (L.T("Écraser les plus anciens"), ConflictPolicy.OverwriteOlder),
            (L.T("Ignorer"), ConflictPolicy.Skip),
            (L.T("Garder les deux"), ConflictPolicy.RenameNew),
            (L.T("Annuler"), null));
        return result as ConflictPolicy?;
    }

    public async Task<string?> PromptAsync(string title, string message, string defaultValue = "", int? selectLength = null)
    {
        var box = new TextBox { Text = defaultValue, MinWidth = 420 };
        var dialog = CreateWindow(title);
        string? result = null;

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = Layout(new TextBlock { Text = message }, box, Buttons(ok, cancel));
        dialog.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectionStart = 0;
            box.SelectionEnd = Math.Min(selectLength ?? defaultValue.Length, defaultValue.Length);
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task<string?> AskArchivePasswordAsync(string archiveName, bool wrong)
    {
        var box = Dialogs.Password();
        var dialog = CreateWindow(L.T("Mot de passe de l'archive"));
        string? result = null;
        var ok = new Button { Content = L.T("Ouvrir"), IsDefault = true, MinWidth = 90, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { result = box.Text ?? string.Empty; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        var error = Dialogs.Text(L.T("Mot de passe incorrect."), color: Brushes.IndianRed);
        error.IsVisible = wrong;
        dialog.Content = Layout(Dialogs.Text(L.F("« {0} » est protégée par un mot de passe.", archiveName)), box, Dialogs.ShowToggle(box), error,
            Dialogs.Text(L.T("Il reste en mémoire jusqu'à la fermeture de TwinPanel, et n'est enregistré nulle part."), subtle: true), Buttons(ok, cancel));
        dialog.Opened += (_, _) => box.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task<PackRequest?> AskPackAsync(string defaultPath, int selectLength)
    {
        var dialog = CreateWindow(L.T("Créer une archive"));
        var path = new TextBox { Text = defaultPath, MinWidth = 420 };
        var protect = new CheckBox { Content = L.T("Protéger par un mot de passe (zip, AES-256)") };
        var first = Dialogs.Password();
        var second = Dialogs.Password();
        first.PlaceholderText = L.T("Mot de passe");
        second.PlaceholderText = L.T("Le même, une deuxième fois");
        var show = new CheckBox { Content = L.T("Afficher") };
        show.IsCheckedChanged += (_, _) => first.RevealPassword = second.RevealPassword = show.IsChecked == true;
        var fields = new StackPanel { Spacing = 8, IsVisible = false };
        fields.Children.AddRange([first, second, show, Dialogs.Text(
            L.T("Le contenu est chiffré, mais les noms des fichiers restent lisibles : c'est ainsi dans un zip.\n") +
            L.T("Sans ce mot de passe, personne ne pourra ouvrir l'archive, pas même TwinPanel : il n'est enregistré nulle part."), subtle: true)]);
        protect.IsCheckedChanged += (_, _) => { fields.IsVisible = protect.IsChecked == true; if (fields.IsVisible) first.Focus(); };
        var error = Dialogs.Text(string.Empty, color: Brushes.IndianRed);
        error.IsVisible = false;

        PackRequest? result = null;
        var ok = new Button { Content = L.T("Créer"), IsDefault = true, MinWidth = 90, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            var target = path.Text?.Trim().Trim('"') ?? string.Empty;
            if (target.Length == 0) return;
            bool locked = protect.IsChecked == true;
            var problem = !locked ? null
                : TwinPanel.Core.Archives.ArchiveService.KindFromName(target) != TwinPanel.Core.Archives.ArchiveKind.Zip ? L.T("Le mot de passe demande le format .zip.")
                : string.IsNullOrEmpty(first.Text) ? L.T("Écris un mot de passe.")
                : first.Text != second.Text ? L.T("Les deux mots de passe sont différents.")
                : null;
            error.Text = problem ?? string.Empty;
            error.IsVisible = problem is not null;
            if (problem is not null) return;
            result = new PackRequest(target, locked ? first.Text : null);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = Layout(new TextBlock { Text = L.T("Nom de l'archive — l'extension choisit le format : .zip, .tar ou .tar.gz.\nUn chemin « sftp://… » crée l'archive sur un serveur ouvert :") },
            path, protect, fields, error, Buttons(ok, cancel));
        dialog.Opened += (_, _) =>
        {
            path.Focus();
            path.SelectionStart = 0;
            path.SelectionEnd = Math.Min(selectLength, defaultPath.Length);
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task<TransferRequest?> AskTransferAsync(string verb, string what, string defaultDestination)
    {
        var dialog = CreateWindow(verb);
        var dest = new TextBox { Text = defaultDestination, MinWidth = 520 };
        var policy = new ComboBox { ItemsSource = PolicyChoices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        TransferRequest? result = null;

        var ok = new Button { Content = verb, IsDefault = true, MinWidth = 100, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            result = new TransferRequest(dest.Text?.Trim().Trim('"') ?? string.Empty, (policy.SelectedItem as PolicyChoice)?.Policy);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = Layout(
            new TextBlock { Text = L.F("{0} {1} vers :", verb, what) },
            dest,
            new TextBlock { Text = L.T("Si un élément existe déjà :"), Classes = { "subtle" } },
            policy,
            Buttons(ok, cancel));
        dialog.Opened += (_, _) => { dest.Focus(); dest.SelectAll(); };
        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task<RenameLog?> ShowMultiRenameAsync(IReadOnlyList<FileEntry> entries, Func<string, string, CancellationToken, Task>? move = null)
    {
        var window = new MultiRenameWindow { DataContext = new MultiRenameViewModel(entries) { Move = move } };
        return await window.ShowDialog<RenameLog?>(owner);
    }

    public async Task<string?> ShowSearchAsync(string startPath, TwinPanel.Core.Remote.RemoteSessionManager? sessions = null)
    {
        var window = new SearchWindow();
        window.DataContext = new SearchViewModel(startPath, sessions) { Confirm = new DialogService(window).ConfirmAsync };
        return await window.ShowDialog<string?>(owner);
    }

    public async Task<SyncRequest?> ShowSyncAsync(string left, string right, TwinPanel.Core.Remote.RemoteSessionManager? sessions = null,
        SyncProfileAccess? profiles = null, TwinPanel.Core.Compare.SyncProfile? start = null)
    {
        var window = new SyncWindow();
        var dialogs = new DialogService(window);
        var vm = new SyncViewModel(left, right, sessions, profiles)
        {
            Prompt = (title, message, value) => dialogs.PromptAsync(title, message, value),
            Confirm = dialogs.ConfirmAsync,
            NeedsConnection = profile => window.Close(new SyncRequest(left, right, [], profile)),
        };
        window.DataContext = vm;
        vm.SelectedProfile = start;
        return await window.ShowDialog<SyncRequest?>(owner);
    }

    public Task ShowCompareFilesAsync(string left, string right)
    {
        // Fenêtre non modale : on peut garder la comparaison ouverte en continuant à travailler.
        new CompareFilesWindow { DataContext = new CompareFilesViewModel(left, right) }.Show(owner);
        return Task.CompletedTask;
    }

    public async Task<T?> ChooseAsync<T>(string title, string message, params (string Label, T? Value)[] choices) where T : struct
    {
        var result = await ShowButtonsAsync(title, message, choices.Select(c => (c.Label, (object?)c.Value)).ToArray());
        return result is T t ? t : null;
    }

    public async Task<string?> ChoosePrinterAsync(IReadOnlyList<string> printers, string? selected, int fileCount)
    {
        var dialog = CreateWindow(L.T("Imprimer"));
        string? result = null;
        var combo = new ComboBox { ItemsSource = printers, SelectedItem = selected ?? printers.FirstOrDefault(), MinWidth = 380, HorizontalAlignment = HorizontalAlignment.Stretch };
        var ok = new Button { Content = L.T("Imprimer"), IsDefault = true, MinWidth = 100, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { result = combo.SelectedItem as string; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = Layout(
            new TextBlock { Text = fileCount == 1 ? L.T("Imprimer le fichier sur :") : L.F("Imprimer {0} fichiers sur :", fileCount) },
            combo,
            new TextBlock { Text = OperatingSystem.IsWindows() ? L.T("Textes : Bloc-notes ; images : Paint ; autres fichiers : leur application.") : L.T("Textes, PDF et images : imprimés par le système."), Classes = { "subtle" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
            Buttons(ok, cancel));
        dialog.Opened += (_, _) => combo.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    private static HelpWindow? _help;

    public void ShowHelp(IReadOnlyList<HelpShortcut> shortcuts, bool atShortcuts)
    {
        if (_help is null)
        {
            _help = new HelpWindow();
            _help.Closed += (_, _) => _help = null;
            _help.Open(shortcuts, atShortcuts);
            _help.Show(owner);
        }
        else
        {
            _help.Open(shortcuts, atShortcuts);
            _help.Activate();
        }
    }

    public async Task ShowAboutAsync(string title, string text, IReadOnlyList<string> details)
    {
        var dialog = CreateWindow(L.T("À propos de TwinPanel"));
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 90, Classes = { "accent" } };
        ok.Click += (_, _) => dialog.Close();
        var logo = new Image { Width = 128, Height = 128, HorizontalAlignment = HorizontalAlignment.Center };
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://TwinPanel/Assets/twinpanel-256.png"));
            logo.Source = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException) { }
        var detailsText = new TextBlock { Text = string.Join(Environment.NewLine, details), Classes = { "subtle" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        var components = LinkButton(L.T("Bibliothèques utilisées et leurs licences"));
        components.HorizontalAlignment = HorizontalAlignment.Center;
        components.Click += async (_, _) => await ShowComponentsAsync(dialog);
        // Symbole de la licence (clic : texte complet du contrat).
        var badge = new Image { Width = 305, Height = 80, HorizontalAlignment = HorizontalAlignment.Center };
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://TwinPanel/Assets/licence-twinpanel.png"));
            badge.Source = new Avalonia.Media.Imaging.Bitmap(s);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException) { }
        var badgeButton = new Button { Content = badge, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(badgeButton, L.T("Licence TwinPanel 1.0 : lire le contrat complet"));
        badgeButton.Click += async (_, _) => await ShowLicenseAsync(dialog);
        var license = LinkButton(L.T("Licence TwinPanel 1.0 — © 2026 Georges Christophe (chrypox)"));
        license.HorizontalAlignment = HorizontalAlignment.Center;
        license.Click += async (_, _) => await ShowLicenseAsync(dialog);
        dialog.Content = Layout(
            logo,
            new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center },
            badgeButton,
            license,
            components,
            detailsText,
            Buttons(ok));
        dialog.Opened += (_, _) => ok.Focus();
        await dialog.ShowDialog(owner);
    }

    /// <summary>Texte cliquable, souligné, couleur d'accent (lien).</summary>
    private static Button LinkButton(string text)
    {
        var label = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("SystemControlHighlightAccentBrush"));
        var b = new Button { Content = label, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2), Cursor = new Cursor(StandardCursorType.Hand) };
        return b;
    }

    /// <summary>Texte complet du contrat de licence (fichier LICENSE intégré au programme).</summary>
    private static async Task ShowLicenseAsync(Window parent)
    {
        string text;
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri(L.Language == L.English ? "avares://TwinPanel/Assets/LICENSE.en.txt" : "avares://TwinPanel/Assets/LICENSE.txt"));
            using var reader = new StreamReader(s, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException)
        {
            text = L.T("Le texte de la licence est introuvable. Voir le fichier LICENSE.txt dans le dossier de TwinPanel.");
        }
        var dialog = CreateWindow(L.T("Licence TwinPanel 1.0"));
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 90, Classes = { "accent" } };
        ok.Click += (_, _) => dialog.Close();
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Width = 640,
            Height = 480,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
        };
        dialog.Content = Layout(box, Buttons(ok));
        dialog.Opened += (_, _) => ok.Focus();
        await dialog.ShowDialog(parent);
    }

    /// <summary>Tableau « Bibliothèque — Rôle — Licence » des composants livrés (et de ceux qui ne le sont pas).</summary>
    private static async Task ShowComponentsAsync(Window parent)
    {
        var dialog = CreateWindow(L.T("Bibliothèques utilisées"));
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 90, Classes = { "accent" } };
        ok.Click += (_, _) => dialog.Close();

        Control Table(IReadOnlyList<TwinPanel.Core.About.ThirdPartyComponent> items)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*,190") };
            int row = 0;
            void Cell(Control c, int col, bool header, bool shade)
            {
                var border = new Border { Padding = new Thickness(8, 5), Child = c };
                if (header) border.Bind(Border.BackgroundProperty, border.GetResourceObservable("ChipBackground"));
                else if (shade) border.Bind(Border.BackgroundProperty, border.GetResourceObservable("HoverFill"));
                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                grid.Children.Add(border);
            }
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            foreach (var (h, col) in new[] { (L.T("Bibliothèque"), 0), (L.T("Rôle"), 1), (L.T("Licence"), 2) })
                Cell(new TextBlock { Text = h, FontWeight = FontWeight.SemiBold }, col, header: true, shade: false);
            foreach (var c in items)
            {
                row++;
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                bool shade = row % 2 == 0;
                // Nom cliquable : site du projet (où se trouve aussi le texte complet de sa licence).
                var name = LinkButton(c.Name);
                ((TextBlock)name.Content!).TextWrapping = TextWrapping.Wrap;
                ToolTip.SetTip(name, c.Url);
                var url = c.Url;
                name.Click += (_, _) =>
                {
                    try { ShellService.Open(url); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                };
                Cell(name, 0, false, shade);
                Cell(new TextBlock { Text = c.Role, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, 1, false, shade);
                Cell(new TextBlock { Text = c.License, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, 2, false, shade);
            }
            return grid;
        }

        var content = new StackPanel { Spacing = 10, Width = 860 };
        content.Children.Add(new TextBlock
        {
            Text = L.T("Bibliothèques d'autres développeurs livrées avec TwinPanel. Elles restent sous leur propre licence : la licence de TwinPanel ne s'applique pas à leur code. Clic sur un nom : site du projet et texte complet de sa licence."),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(Table(TwinPanel.Core.About.ThirdPartyComponents.Shipped));
        content.Children.Add(new TextBlock { Text = L.T("Non livrés avec TwinPanel (fabrication du programme, ou outils facultatifs installés par l'utilisateur) :"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(Table(TwinPanel.Core.About.ThirdPartyComponents.NotShipped));
        var scroll = new ScrollViewer { Content = content, MaxHeight = 560, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        dialog.Content = Layout(scroll, Buttons(ok));
        dialog.Opened += (_, _) => ok.Focus();
        await dialog.ShowDialog(parent);
    }

    public async Task ShowListAsync(string title, string message, IReadOnlyList<string> lines)
    {
        var dialog = CreateWindow(title);
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 90, Classes = { "accent" } };
        ok.Click += (_, _) => dialog.Close();
        var list = new TextBox
        {
            Text = string.Join(Environment.NewLine, lines),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Width = 640,
            MaxHeight = 360,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
        };
        dialog.Content = lines.Count > 0
            ? Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 }, list, Buttons(ok))
            : Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 }, Buttons(ok));
        dialog.Opened += (_, _) => ok.Focus();
        await dialog.ShowDialog(owner);
    }

    public void ShowViewer(IReadOnlyList<string> files, int index) =>
        new ViewerWindow { DataContext = new ViewerViewModel(files, index) }.Show(owner);

    public Task ShowEditorAsync(string filePath, int line = 1)
    {
        var window = new EditorWindow();
        window.DataContext = new EditorViewModel(filePath, line, new DialogService(window));
        window.Show(owner);
        return Task.CompletedTask;
    }

    private sealed record EditorChoice(ExternalTool? Tool, string Label)
    {
        public override string ToString() => Label;
    }

    public async Task<bool> ShowToolSettingsAsync(AppSettings settings)
    {
        var dialog = CreateWindow(L.T("Éditeur et visionneuse"));
        bool saved = false;

        // --- Éditeur (F4) : seuls les éditeurs réellement installés sont proposés.
        var choices = ExternalTools.DetectEditors().Select(t => new EditorChoice(t, t.Name)).ToList();
        if (!OperatingSystem.IsWindows()) choices.Insert(0, new EditorChoice(ToolLauncher.DefaultEditor, ToolLauncher.DefaultEditor.Name));
        var other = new EditorChoice(null, L.T("Autre programme…"));
        choices.Add(other);

        var editorPath = new TextBox { Text = settings.EditorPath, PlaceholderText = OperatingSystem.IsWindows() ? L.T("Chemin du programme (.exe)") : L.T("Chemin du programme"), MinWidth = 440 };
        var editorArgs = new TextBox { Text = settings.EditorArguments, MinWidth = 440 };
        var combo = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectedItem = string.IsNullOrWhiteSpace(settings.EditorPath)
            ? choices[0]
            : choices.FirstOrDefault(c => c.Tool is not null && string.Equals(c.Tool.Path, settings.EditorPath, StringComparison.OrdinalIgnoreCase)) ?? other;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is EditorChoice { Tool: { } t })
            {
                editorPath.Text = t == ToolLauncher.DefaultEditor ? string.Empty : t.Path;
                editorArgs.Text = t.Arguments;
            }
        };
        var browseEditor = new Button { Content = L.T("Parcourir…") };
        browseEditor.Click += async (_, _) =>
        {
            if (await PickProgramAsync(dialog) is { } p) { editorPath.Text = p; combo.SelectedItem = other; }
        };

        // --- Visionneuse (F3)
        var useExternalViewer = new CheckBox { Content = L.T("Utiliser une visionneuse externe pour F3"), IsChecked = !string.IsNullOrWhiteSpace(settings.ViewerPath) };
        var viewerPath = new TextBox { Text = settings.ViewerPath, PlaceholderText = OperatingSystem.IsWindows() ? L.T("Chemin du programme (.exe)") : L.T("Chemin du programme"), MinWidth = 440 };
        var viewerArgs = new TextBox { Text = settings.ViewerArguments, MinWidth = 440 };
        void SyncViewerEnabled() => viewerPath.IsEnabled = viewerArgs.IsEnabled = useExternalViewer.IsChecked == true;
        useExternalViewer.IsCheckedChanged += (_, _) => SyncViewerEnabled();
        SyncViewerEnabled();
        var browseViewer = new Button { Content = L.T("Parcourir…") };
        browseViewer.Click += async (_, _) => { if (await PickProgramAsync(dialog) is { } p) { viewerPath.Text = p; useExternalViewer.IsChecked = true; } };

        var ok = new Button { Content = L.T("Enregistrer"), IsDefault = true, MinWidth = 100, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        cancel.Click += (_, _) => dialog.Close();
        ok.Click += (_, _) =>
        {
            var ePath = editorPath.Text?.Trim().Trim('"') ?? string.Empty;
            var vPath = useExternalViewer.IsChecked == true ? viewerPath.Text?.Trim().Trim('"') ?? string.Empty : string.Empty;
            if (ePath.Length > 0 && !ExternalTools.IsAvailable(ePath)) { error.Text = L.F("Éditeur introuvable : {0}", ePath); return; }
            if (vPath.Length > 0 && !ExternalTools.IsAvailable(vPath)) { error.Text = L.F("Visionneuse introuvable : {0}", vPath); return; }

            settings.EditorPath = ePath;
            settings.EditorName = ePath.Length == 0 ? string.Empty
                : combo.SelectedItem is EditorChoice { Tool: { } t } && t.Path == ePath ? t.Name : Path.GetFileNameWithoutExtension(ePath);
            settings.EditorArguments = string.IsNullOrWhiteSpace(editorArgs.Text) ? "\"%1\"" : editorArgs.Text.Trim();
            settings.ViewerPath = vPath;
            settings.ViewerArguments = string.IsNullOrWhiteSpace(viewerArgs.Text) ? "\"%1\"" : viewerArgs.Text.Trim();
            saved = true;
            dialog.Close();
        };

        Control Row(Control main, Control side)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            g.Children.Add(main);
            Grid.SetColumn(side, 1);
            side.Margin = new Thickness(8, 0, 0, 0);
            g.Children.Add(side);
            return g;
        }

        dialog.Content = Layout(
            new TextBlock { Text = L.T("Éditeur (F4)"), FontWeight = FontWeight.SemiBold },
            combo,
            Row(editorPath, browseEditor),
            new TextBlock { Text = L.T("Arguments : %1 fichier, %L ligne, %N nom, %D dossier"), Classes = { "subtle" } },
            editorArgs,
            new TextBlock { Text = L.T("Visionneuse (F3)"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) },
            useExternalViewer,
            Row(viewerPath, browseViewer),
            viewerArgs,
            new TextBlock { Text = L.T("Sans visionneuse externe, F3 ouvre la visionneuse intégrée. Alt+F3 ouvre toujours avec l'application associée par le système."), Classes = { "subtle" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
            error,
            Buttons(ok, cancel));
        await dialog.ShowDialog(owner);
        return saved;
    }

    public async Task<TwinPanel.Core.Remote.ConnectionProfile?> ShowConnectionManagerAsync(TwinPanel.Core.Remote.ConnectionStore store,
        TwinPanel.Core.Remote.RemoteSessionManager sessions, TwinPanel.Core.Remote.IConnectionPrompts prompts)
    {
        var window = new ConnectionManagerWindow();
        window.DataContext = new ConnectionManagerViewModel(store, sessions, prompts, new DialogService(window));
        return await window.ShowDialog<TwinPanel.Core.Remote.ConnectionProfile?>(owner);
    }

    public async Task<bool> ShowOptionsAsync(AppSettings settings, TwinPanel.Core.Remote.ConnectionStore? connections, string? page = null)
    {
        HangWatchdog.Step("Options : création de la fenêtre");
        var window = new OptionsWindow();
        HangWatchdog.Step("Options : préparation des réglages affichés");
        var vm = new OptionsViewModel(settings, connections, new DialogService(window), page);
        HangWatchdog.Step("Options : affichage");
        window.DataContext = vm;
        window.Opened += (_, _) => HangWatchdog.Step("Options : fenêtre affichée");
        return await window.ShowDialog<bool>(owner);
    }

    public async Task<bool> ShowPropertiesAsync(IReadOnlyList<FileEntry> entries, TwinPanel.Core.Remote.RemoteSession? session)
    {
        var window = new PropertiesWindow();
        var vm = new PropertiesViewModel(entries, session, new DialogService(window));
        window.DataContext = vm;
        await window.ShowDialog<bool>(owner);
        return vm.Changed;
    }

    public async Task<QuickConnectRequest?> AskQuickConnectAsync()
    {
        var dialog = CreateWindow(L.T("Connexion rapide"));
        QuickConnectRequest? result = null;
        var address = new TextBox { MinWidth = 460, PlaceholderText = L.T("sftp://utilisateur@serveur:22/dossier") };
        var password = new TextBox { PasswordChar = '•', MinWidth = 460, PlaceholderText = L.T("Mot de passe (vide : demandé si nécessaire)") };
        var save = new CheckBox { Content = L.T("Ajouter au gestionnaire de connexions") };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        var ok = new Button { Content = L.T("Connecter"), IsDefault = true, MinWidth = 100, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            if (!TwinPanel.Core.Remote.QuickConnect.TryParse(address.Text ?? string.Empty, out var profile, out var message))
            {
                error.Text = message;
                return;
            }
            result = new QuickConnectRequest(profile, password.Text ?? string.Empty, save.IsChecked == true);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = Layout(
            new TextBlock { Text = L.T("Adresse du serveur :") },
            address,
            new TextBlock
            {
                Text = L.T("Exemples : utilisateur@192.168.0.10 (SFTP)  —  scp://…  —  ftp://…  —  ftps://…  —  https://serveur/remote.php/dav/files/moi (WebDAV)"),
                Classes = { "subtle" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 460,
            },
            password,
            save,
            error,
            Buttons(ok, cancel));
        dialog.Opened += (_, _) => address.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    private static Task<string?> PickProgramAsync(Window parent) => ChooseProgramAsync(parent, L.T("Choisir un programme"));

    /// <summary>
    /// Liste des programmes installés (éditeurs connus en tête), avec recherche ; « Parcourir… » si le programme n'y est pas.
    /// Retourne le chemin choisi, ou null.
    /// </summary>
    public static async Task<string?> ChooseProgramAsync(Window parent, string title)
    {
        var dialog = CreateWindow(title);
        string? result = null;
        var search = new TextBox { PlaceholderText = L.T("Rechercher (nom ou dossier)…"), MinWidth = 560 };
        var list = new ListBox
        {
            Height = 380,
            Width = 560,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TwinPanel.Core.Tools.InstalledProgram>((p, _) =>
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), Margin = new Thickness(2) };
                if (p is null) return row;
                // Icône du programme (lue dans le .exe par Windows) ; en attendant ou à défaut : icône « programme ».
                var fallback = Application.Current?.TryGetResource("IconProgram", null, out var g) == true && g is Geometry geo
                    ? new PathIcon { Data = geo, Width = 20, Height = 20, Opacity = 0.6 } : null;
                var image = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
                if (fallback is not null) { fallback.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(fallback); }
                row.Children.Add(image);
                ShellIcons.RequestProgram(p.Path, bmp =>
                {
                    image.Source = bmp;
                    if (fallback is not null) fallback.IsVisible = bmp is null;
                });
                var text = new StackPanel();
                Grid.SetColumn(text, 1);
                text.Children.Add(new TextBlock { Text = (p.IsKnownEditor ? "★ " : string.Empty) + p.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = p.Path, Classes = { "subtle" }, TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(text);
                return row;
            }),
        };
        var info = new TextBlock { Text = L.T("Recherche des programmes installés…"), Classes = { "subtle" } };
        var choose = new Button { Content = L.T("Choisir"), IsDefault = true, MinWidth = 100, Classes = { "accent" } };
        var browse = new Button { Content = L.T("Parcourir…"), MinWidth = 100 };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        IReadOnlyList<TwinPanel.Core.Tools.InstalledProgram> all = [];
        void Refill()
        {
            var items = TwinPanel.Core.Tools.InstalledPrograms.Filter(all, search.Text).ToList();
            list.ItemsSource = items;
            if (items.Count > 0) list.SelectedIndex = 0;
            info.Text = all.Count == 0 ? L.T("Aucun programme trouvé : utilise « Parcourir… ».") : L.F("{0} programme(s) — ★ : éditeurs reconnus par TwinPanel", items.Count);
        }
        void Pick()
        {
            if (list.SelectedItem is TwinPanel.Core.Tools.InstalledProgram p) { result = p.Path; dialog.Close(); }
        }
        search.TextChanged += (_, _) => Refill();
        search.KeyDown += (_, e) =>
        {
            // Flèches depuis la recherche : on parcourt la liste sans quitter le champ.
            if (e.Key is Key.Down or Key.Up && list.ItemCount > 0)
            {
                list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, list.ItemCount - 1);
                list.ScrollIntoView(list.SelectedIndex);
                e.Handled = true;
            }
        };
        list.DoubleTapped += (_, _) => Pick();
        choose.Click += (_, _) => Pick();
        cancel.Click += (_, _) => dialog.Close();
        browse.Click += async (_, _) =>
        {
            if (await BrowseProgramAsync(dialog) is { } path) { result = path; dialog.Close(); }
        };
        dialog.Content = Layout(search, list, info, Buttons(choose, browse, cancel));
        dialog.Opened += async (_, _) =>
        {
            search.Focus();
            all = await Task.Run(() => TwinPanel.Core.Tools.InstalledPrograms.Get());
            Refill();
        };
        await dialog.ShowDialog(parent);
        return result;
    }

    /// <summary>Choix d'un programme dans l'explorateur de fichiers de Windows.</summary>
    public static async Task<string?> BrowseProgramAsync(Window parent)
    {
        var files = await parent.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Choisir un programme"),
            AllowMultiple = false,
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType(L.T("Programmes")) { Patterns = ["*.exe", "*.cmd", "*.bat"] }]
                : null,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<object?> ShowButtonsAsync(string title, string message, params (string Label, object? Value)[] choices)
    {
        var dialog = CreateWindow(title);
        object? result = null;
        var buttons = choices.Select((c, i) =>
        {
            var b = new Button { Content = c.Label, MinWidth = 80, IsDefault = i == 0, IsCancel = i == choices.Length - 1 };
            if (i == 0) b.Classes.Add("accent");
            b.Click += (_, _) => { result = c.Value; dialog.Close(); };
            return b;
        }).ToArray();

        dialog.Content = Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
                                Buttons(buttons));
        dialog.Opened += (_, _) => buttons[0].Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    private static Window CreateWindow(string title)
    {
        var w = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        w.Bind(Window.BackgroundProperty, w.GetResourceObservable("AppBackground"));
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        return w;
    }

    private static Control Layout(params Control[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }

    private static Control Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) panel.Children.Add(b);
        return panel;
    }
}
