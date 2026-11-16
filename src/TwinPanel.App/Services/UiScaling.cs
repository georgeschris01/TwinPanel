// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace TwinPanel.App.Services;

/// <summary>
/// Taille de l'interface (Options → Affichage, 75 à 200 %) : le contenu de chaque fenêtre et de chaque menu, infobulle
/// ou liste déroulante est agrandi ou réduit d'un même facteur, en plus de la mise à l'échelle du système.
/// Tout est dessiné en vectoriel : le texte et les icônes restent nets. Un changement s'applique tout de suite.
/// </summary>
internal static class UiScaling
{
    /// <summary>Enveloppe posée autour du contenu d'une fenêtre.</summary>
    private sealed class ScaledRoot : LayoutTransformControl;

    private static readonly List<WeakReference<ScaledRoot>> s_roots = [];
    private static double s_scale = 1;
    private static bool s_installed;

    public static double Current => s_scale;

    /// <summary>À appeler une fois, avant la première fenêtre.</summary>
    public static void Install()
    {
        if (s_installed) return;
        s_installed = true;
        ContentControl.ContentProperty.Changed.AddClassHandler<Window>((w, e) => Wrap(w, e.NewValue));
        // Menus, infobulles, listes déroulantes : ils reprennent l'agrandissement de l'endroit d'où ils s'ouvrent
        // (fenêtre agrandie). Ni enveloppe autour de leur contenu ni échelle posée à la main : le menu ne s'ouvrirait plus.
        Popup.ChildProperty.Changed.AddClassHandler<Popup>((p, _) => p.InheritsTransform = true);
    }

    /// <summary>Change la taille de l'interface (1 = 100 %) : fenêtres ouvertes comprises.</summary>
    public static void Set(double scale)
    {
        scale = Math.Clamp(scale, 0.5, 3);
        if (Math.Abs(scale - s_scale) < 0.001) return;
        double ratio = scale / s_scale;
        s_scale = scale;
        lock (s_roots)
        {
            s_roots.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (var weak in s_roots)
            {
                if (!weak.TryGetTarget(out var root)) continue;
                root.LayoutTransform = new ScaleTransform(scale, scale);
                // Fenêtre de taille fixe (pas la fenêtre principale, dont la taille est celle choisie par l'utilisateur) :
                // elle suit, pour que son contenu y tienne toujours.
                if (TopLevel.GetTopLevel(root) is Window { SizeToContent: SizeToContent.Manual } w && w != MainWindowOf(w))
                {
                    w.Width = w.Bounds.Width * ratio;
                    w.Height = w.Bounds.Height * ratio;
                }
            }
        }
    }

    private static Window? MainWindowOf(Window _) =>
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static void Wrap(ContentControl host, object? content)
    {
        if (content is not Control child || child is ScaledRoot) return;
        var root = new ScaledRoot { LayoutTransform = new ScaleTransform(s_scale, s_scale) };
        // D'abord retirer le contenu de la fenêtre (il ne peut avoir qu'un parent), puis le mettre dans l'enveloppe.
        host.Content = root;
        root.Child = child;
        lock (s_roots) s_roots.Add(new WeakReference<ScaledRoot>(root));

        // Fenêtre de taille fixe pas encore ouverte : ses dimensions suivent la taille de l'interface.
        if (host is Window { IsVisible: false } w && Math.Abs(s_scale - 1) > 0.001)
        {
            if (!double.IsNaN(w.Width)) w.Width *= s_scale;
            if (!double.IsNaN(w.Height)) w.Height *= s_scale;
            if (w.MinWidth > 0) w.MinWidth *= s_scale;
            if (w.MinHeight > 0) w.MinHeight *= s_scale;
            if (!double.IsInfinity(w.MaxWidth)) w.MaxWidth *= s_scale;
            if (!double.IsInfinity(w.MaxHeight)) w.MaxHeight *= s_scale;
        }
    }
}
