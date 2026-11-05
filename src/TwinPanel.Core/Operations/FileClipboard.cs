// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Buffers.Binary;

namespace TwinPanel.Core.Operations;

/// <summary>
/// Couper / Copier / Coller des fichiers (Ctrl+X / Ctrl+C / Ctrl+V) : la partie sans interface.
/// Avec l'Explorateur de Windows, « couper » se dit par le format « Preferred DropEffect » (un entier : 1 copier, 2 déplacer).
/// </summary>
public static class FileClipboard
{
    private const int EffectCopy = 1, EffectMove = 2;

    /// <summary>Contenu du format « Preferred DropEffect » : copier ou déplacer.</summary>
    public static byte[] DropEffectBytes(bool move)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, move ? EffectMove : EffectCopy);
        return bytes;
    }

    /// <summary>Vrai si l'autre programme a « coupé » les fichiers (déplacer, sans copier).</summary>
    public static bool IsMove(byte[]? dropEffect) =>
        dropEffect is { Length: >= 4 } && (BinaryPrimitives.ReadInt32LittleEndian(dropEffect) & (EffectCopy | EffectMove)) == EffectMove;

    /// <summary>Dossier qui contient un élément (PC, archive ou serveur), sans séparateur final.</summary>
    public static string ParentOf(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('/', '\\');
        int cut = trimmed.LastIndexOfAny(['/', '\\']);
        return cut <= 0 ? trimmed : trimmed[..cut].TrimEnd('/', '\\');
    }

    /// <summary>Vrai si l'un des éléments est déjà dans ce dossier : le coller là n'a pas de sens.</summary>
    public static bool AnyAlreadyIn(IEnumerable<string> fullPaths, string folder)
    {
        var target = folder.TrimEnd('/', '\\');
        return fullPaths.Any(p => string.Equals(ParentOf(p), target, PathText.Comparison));
    }

    /// <summary>Élément du PC venu d'un autre programme ; null s'il n'existe plus.</summary>
    public static FileEntry? LocalEntry(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var d = new DirectoryInfo(path);
                return new FileEntry(d.Name, d.FullName, true, 0, d.LastWriteTime, d.Attributes);
            }
            if (File.Exists(path))
            {
                var f = new FileInfo(path);
                return new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime, f.Attributes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }
}
