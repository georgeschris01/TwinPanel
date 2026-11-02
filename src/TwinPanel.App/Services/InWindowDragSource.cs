// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace TwinPanel.App.Services;

/// <summary>
/// Glissement limité aux fenêtres de TwinPanel, pour Linux en session Wayland.
/// TwinPanel y tourne par XWayland : tout glissement du système passe alors par le compositeur (KWin), qui affiche le curseur
/// « interdit » tant qu'aucune application Wayland n'accepte, même quand le glissement reste dans TwinPanel et réussit.
/// Le glissement interne d'Avalonia ne passe pas par le système : il garde les curseurs de copie et de déplacement,
/// mais on ne peut plus lâcher dans une autre application. Glisser depuis une autre application vers TwinPanel reste possible.
/// </summary>
internal static class InWindowDragSource
{
    private const string ImplementationName = "Avalonia.Platform.InProcessDragSource";

    /// <summary>
    /// Avalonia réserve à son propre usage la classe et le registre utilisés ici : tout passe par la réflexion, à l'exécution.
    /// Si l'un d'eux change avec une nouvelle version, le glissement du système reste en place.
    /// </summary>
    public static void UseUnderWayland()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return;
        try
        {
            var implementation = typeof(Window).Assembly.GetType(ImplementationName);
            var locator = typeof(AvaloniaLocator).GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (implementation is null || locator is null) return;
            var binding = locator.GetType().GetMethod("Bind")?.MakeGenericMethod(typeof(IPlatformDragSource)).Invoke(locator, null);
            // ToTransient : un objet par glissement, car celui d'Avalonia ne sert qu'une fois.
            binding?.GetType().GetMethod("ToTransient")?.MakeGenericMethod(implementation).Invoke(binding, null);
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidOperationException or AmbiguousMatchException)
        {
            CrashLog.Write(ex, fatal: false);
        }
    }
}
