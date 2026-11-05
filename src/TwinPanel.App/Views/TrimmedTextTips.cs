// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TwinPanel.App.Views;

/// <summary>
/// Infobulle avec le texte entier sur tout texte coupé par « … » (noms de fichiers, chemins, listes…),
/// dans toutes les fenêtres. Rien quand le texte est lisible en entier, et une infobulle déjà prévue
/// par une fenêtre n'est jamais remplacée.
/// </summary>
public static class TrimmedTextTips
{
    /// <summary>Vrai : l'infobulle de ce texte a été mise ici (elle peut donc être retirée ou changée).</summary>
    private static readonly AttachedProperty<bool> OursProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("TrimmedTipOurs", typeof(TrimmedTextTips));

    private static bool s_installed;

    public static void Install()
    {
        if (s_installed) return;
        s_installed = true;
        InputElement.PointerEnteredEvent.AddClassHandler<TextBlock>((text, _) => Update(text));
    }

    private static void Update(TextBlock text)
    {
        bool ours = text.GetValue(OursProperty);
        if (!ours && ToolTip.GetTip(text) is not null) return;
        // Texte des onglets : il défile déjà sous la souris.
        if (text.Parent is ScrollingText) return;
        bool trimmed = text.TextTrimming != TextTrimming.None && !string.IsNullOrEmpty(text.Text) && IsTrimmed(text);
        if (trimmed)
        {
            ToolTip.SetTip(text, text.Text);
            text.SetValue(OursProperty, true);
        }
        else if (ours)
        {
            text.ClearValue(ToolTip.TipProperty);
            text.SetValue(OursProperty, false);
        }
    }

    private static bool IsTrimmed(TextBlock text)
    {
        foreach (var line in text.TextLayout.TextLines)
            if (line.HasCollapsed) return true;
        return false;
    }
}
