// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace TwinPanel.App;

/// <summary>
/// Version publiée (Windows) : TwinPanel.exe en un seul fichier, et ses bibliothèques natives (dessin, polices,
/// médias, chiffrement) rangées dans le sous-dossier « lib » au lieu d'être en vrac à côté du programme.
/// .NET ne les cherche pas là de lui-même : on les lui donne quand il ne les trouve pas.
/// Version de développement (pas de dossier « lib ») : rien ne change.
/// </summary>
internal static class NativeLibraries
{
    public static void UseLibFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var lib = Path.Combine(AppContext.BaseDirectory, "lib");
        if (!Directory.Exists(lib)) return;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            var file = Path.Combine(lib, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll");
            // Chemin complet : Windows cherche aussi ses dépendances (libcurl, OpenSSL…) dans « lib ».
            return File.Exists(file) && NativeLibrary.TryLoad(file, out var handle) ? handle : IntPtr.Zero;
        };
    }
}
