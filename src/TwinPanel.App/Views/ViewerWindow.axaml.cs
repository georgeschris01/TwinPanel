// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

/// <summary>Fenêtre de la visionneuse : Échap ferme, N/P fichier suivant/précédent, 1/2/3 texte/hexa/image, Ctrl+F rechercher.</summary>
public partial class ViewerWindow : Window
{
    public ViewerWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is ViewerViewModel vm) await vm.LoadAsync();
            Viewer.FocusContent();
        };
        Closed += (_, _) => (DataContext as ViewerViewModel)?.Dispose();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        // Les lettres et chiffres passent par le caractère tapé, pas par la touche physique :
        // sur un clavier AZERTY, « 1 » s'obtient avec Maj, et doit marcher quand même.
        AddHandler(TextInputEvent, OnText, RoutingStrategies.Tunnel);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewerViewModel vm) return;

        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) { e.Handled = true; Viewer.FocusFind(); return; }
        if (e.Key == Key.F3) { e.Handled = true; vm.FindNext(); return; }
        if (e.Source is TextBox || e.KeyModifiers != KeyModifiers.None) return;

        // Pavé numérique : pas de caractère tapé si Verr. Num est désactivé, on garde donc ces touches ici.
        switch (e.Key)
        {
            case Key.NumPad1: e.Handled = true; vm.ShowText(); break;
            case Key.NumPad2: e.Handled = true; vm.ShowHex(); break;
            case Key.NumPad3: e.Handled = true; _ = vm.ShowImage(); break;
            case Key.NumPad4: e.Handled = true; _ = vm.ShowMedia(); break;
        }
    }

    private async void OnText(object? sender, TextInputEventArgs e)
    {
        if (DataContext is not ViewerViewModel vm || e.Source is TextBox || string.IsNullOrEmpty(e.Text)) return;
        switch (char.ToLowerInvariant(e.Text[0]))
        {
            case 'n': e.Handled = true; await vm.Next(); break;
            case 'p': e.Handled = true; await vm.Previous(); break;
            case '1': e.Handled = true; vm.ShowText(); break;
            case '2': e.Handled = true; vm.ShowHex(); break;
            case '3': e.Handled = true; await vm.ShowImage(); break;
            case '4': e.Handled = true; await vm.ShowMedia(); break;
            case 'w': e.Handled = true; vm.Wrap = !vm.Wrap; break;
        }
    }
}
