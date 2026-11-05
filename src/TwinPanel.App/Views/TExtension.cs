// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.App.Views;

/// <summary>Texte traduit dans un fichier axaml : <c>Text="{l:T 'Annuler'}"</c>.</summary>
public sealed class TExtension(string text)
{
    public string Text { get; } = text;

    public string ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
