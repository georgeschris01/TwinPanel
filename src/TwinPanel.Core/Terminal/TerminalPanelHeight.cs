// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Terminal;

/// <summary>Hauteur du panneau des terminaux : celle que l'utilisateur a choisie, gardée d'une fois à l'autre.</summary>
public static class TerminalPanelHeight
{
    public const double MinTerminal = 110;
    public const double MinFiles = 120;

    /// <summary>
    /// Hauteur à donner au panneau qui s'ouvre. Tant que la fenêtre n'a pas encore sa taille (au démarrage),
    /// on garde la hauteur choisie : la limiter à ce moment-là la réduirait au minimum.
    /// </summary>
    public static double ToApply(double saved, double available)
    {
        if (double.IsNaN(saved) || saved <= 0) saved = 260;
        if (available <= 0) return Math.Max(MinTerminal, saved);
        return Math.Clamp(saved, MinTerminal, Math.Max(MinTerminal, available - MinFiles));
    }

    /// <summary>
    /// Hauteur à retenir. Un panneau pas encore mesuré (ouvert puis refermé dans la même seconde,
    /// comme à la réouverture des terminaux) a une hauteur sous le minimum : on garde alors l'ancienne.
    /// </summary>
    public static double ToRemember(double actual, double previous) =>
        actual >= MinTerminal ? actual : previous;
}
