// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

/// <summary>Éditeur intégré. À la fermeture, propose d'enregistrer si le texte a été modifié.</summary>
public partial class EditorWindow : Window
{
    private bool _closeConfirmed;

    public EditorWindow()
    {
        InitializeComponent();
        SaveButton.Click += async (_, _) => await (Vm?.SaveAsync() ?? Task.FromResult(false));
        FindNextButton.Click += (_, _) => FindNext();
        ReplaceAllButton.Click += (_, _) => Vm?.ReplaceAll();
        FindBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; FindNext(); } };
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty) Vm?.UpdatePosition(Editor.CaretIndex);
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Closing += OnClosing;

        Opened += async (_, _) =>
        {
            if (Vm is not { } vm) return;
            if (!await vm.LoadAsync()) { _closeConfirmed = true; Close(); return; }
            ApplyWrap(vm.Wrap);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(EditorViewModel.Wrap)) ApplyWrap(vm.Wrap); };
            Editor.Focus();
            GoToOffset(vm.OffsetOfLine(vm.StartLine));
        };
    }

    private EditorViewModel? Vm => DataContext as EditorViewModel;

    private void ApplyWrap(bool wrap) => Editor.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    private async void OnKey(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;

        if (ctrl && e.Key == Key.S) { e.Handled = true; await vm.SaveAsync(); }
        else if (ctrl && e.Key == Key.F) { e.Handled = true; FindBox.Focus(); FindBox.SelectAll(); }
        else if (ctrl && e.Key == Key.H) { e.Handled = true; vm.ShowReplace = true; Dispatcher.UIThread.Post(() => { ReplaceBox.Focus(); ReplaceBox.SelectAll(); }); }
        else if (ctrl && e.Key == Key.G) { e.Handled = true; await GoToLineAsync(vm); }
        else if (e.Key == Key.F3) { e.Handled = true; FindNext(); }
        else if (e.Key == Key.Escape && e.Source is not TextBox { Name: "Editor" }) { e.Handled = true; Editor.Focus(); }
        else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }

    private void FindNext()
    {
        if (Vm is not { } vm) return;
        int i = vm.FindFrom(Math.Max(Editor.SelectionEnd, Editor.CaretIndex));
        if (i < 0) return;
        Editor.Focus();
        Editor.SelectionStart = i;
        Editor.SelectionEnd = i + vm.FindText.Length;
        Editor.CaretIndex = i + vm.FindText.Length;
    }

    private async Task GoToLineAsync(EditorViewModel vm)
    {
        var answer = await vm.Dialogs.PromptAsync(L.T("Aller à la ligne"), L.T("Numéro de ligne :"));
        if (int.TryParse(answer, out var line) && line > 0) GoToOffset(vm.OffsetOfLine(line));
    }

    private void GoToOffset(int offset)
    {
        Editor.Focus();
        Editor.CaretIndex = offset;
        Editor.SelectionStart = Editor.SelectionEnd = offset;
    }

    /// <summary>Fermeture avec modifications : Enregistrer / Ne pas enregistrer / Annuler.</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || Vm is not { IsModified: true } vm) return;
        e.Cancel = true;
        var choice = await vm.Dialogs.ChooseAsync<int>(L.T("Fichier modifié"),
            L.F("Enregistrer les modifications de « {0} » ?", Path.GetFileName(vm.FilePath)),
            (L.T("Enregistrer"), 1), (L.T("Ne pas enregistrer"), 2), (L.T("Annuler"), null));
        if (choice is null) return;
        if (choice == 1 && !await vm.SaveAsync()) return;
        _closeConfirmed = true;
        Close();
    }
}
