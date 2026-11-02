// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Interactivity;
using TwinPanel.App.ViewModels;
using TwinPanel.Core;
using TwinPanel.Core.Operations;

namespace TwinPanel.App.Views;

public partial class MultiRenameWindow : Window
{
    public MultiRenameWindow()
    {
        InitializeComponent();
        RenameButton.Click += OnRename;
        CancelButton.Click += (_, _) => Close(null);
        Closed += (_, _) => (DataContext as MultiRenameViewModel)?.StopReadingMedia();
        // Les boutons « [N] nom », « [C] compteur »… ajoutent leur code au masque.
        AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button { Tag: string code } && DataContext is MultiRenameViewModel vm) vm.Insert(code);
        }, RoutingStrategies.Bubble);
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MultiRenameViewModel vm || !vm.CanRename) return;
        try
        {
            RenameButton.IsEnabled = false;
            var log = vm.Move is { } move
                ? await MultiRename.ExecuteAsync(vm.Preview.ToList(), move)
                : MultiRename.Execute(vm.Preview.ToList());
            Close(log);
        }
        catch (FileOperationException ex)
        {
            vm.Status = ex.Message;
        }
        finally { RenameButton.IsEnabled = true; }
    }
}
