// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Navigation;

/// <summary>Nom affiché sur la pastille d'un onglet.</summary>
public static class TabTitle
{
    /// <summary>Nom du dossier ; disque du PC : lettre devant (« C:\Users\utilisateur\Documents » → « C: Documents », « C:\ » → « C: »).</summary>
    public static string FromPath(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name)) return trimmed;
        bool driveLetter = trimmed.Length >= 2 && trimmed[1] == ':' && char.IsAsciiLetter(trimmed[0])
                           && (trimmed.Length == 2 || trimmed[2] is '\\' or '/');
        return driveLetter ? $"{char.ToUpperInvariant(trimmed[0])}: {name}" : name;
    }

    /// <summary>
    /// Nom choisi par l'utilisateur s'il y en a un ; sinon, onglet verrouillé : nom de son dossier verrouillé,
    /// même s'il affiche un autre dossier (verrou avec changement de dossier) ; sinon, nom du dossier affiché.
    /// </summary>
    public static string Display(string customName, TabLock lockMode, string lockedPath, string currentTitle)
    {
        if (customName.Length > 0) return customName;
        if (lockMode != TabLock.None && lockedPath.Length > 0) return FromPath(lockedPath);
        return currentTitle;
    }
}
