// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

/// <summary>Résultat : la demande de synchronisation (<see cref="SyncRequest"/>), ou null.</summary>
public partial class SyncWindow : Window
{
    public SyncWindow()
    {
        InitializeComponent();
        SyncButton.Click += (_, _) =>
        {
            if (DataContext is SyncViewModel vm) Close(vm.BuildRequest());
        };
        CloseButton.Click += (_, _) => Close(null);
        Closing += (_, _) => (DataContext as SyncViewModel)?.Cancel();
    }
}
