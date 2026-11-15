// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TwinPanel.App.Views;

/// <summary>
/// Premier démarrage de la version portable sur un PC où TwinPanel est aussi installé :
/// « Reprendre mes réglages ? » (sans les mots de passe). Montrée avant la fenêtre principale.
/// </summary>
public sealed class PortableImportWindow : Window
{
    private readonly Action<bool> _done;
    private bool _answered;

    /// <param name="done">Appelé une seule fois avec la réponse, AVANT la fermeture (la fenêtre principale s'ouvre d'abord).</param>
    public PortableImportWindow(Action<bool> done)
    {
        _done = done;
        Title = "TwinPanel — " + L.T("Version portable");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var yes = new Button { Content = L.T("Reprendre mes réglages"), IsDefault = true, Classes = { "accent" } };
        var no = new Button { Content = L.T("Commencer à vide"), IsCancel = true };
        yes.Click += (_, _) => Answer(true);
        no.Click += (_, _) => Answer(false);
        Closing += (_, _) => { if (!_answered) { _answered = true; _done(false); } };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = L.T("TwinPanel est aussi installé sur ce PC. Reprendre ses réglages dans cette version portable ?") + "\n\n" +
                           L.T("Sont copiés : réglages, onglets, historique des dossiers, profils de synchronisation, connexions et serveurs connus.") + "\n" +
                           L.T("Les mots de passe ne sont pas copiés : ils seront redemandés, et gardés dans le coffre protégé par un mot de passe maître."),
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { yes, no } },
            },
        };
    }

    private void Answer(bool accepted)
    {
        if (_answered) return;
        _answered = true;
        _done(accepted);
        Close();
    }
}
