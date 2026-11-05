// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core;

/// <summary>Le disque de destination manque de place : le voir avant de commencer, et le reconnaître si ça arrive en cours de route.</summary>
public static class DiskSpace
{
    private const int WindowsErrorFamily = unchecked((int)0x80070000);
    private const int WindowsErrorMask = unchecked((int)0xFFFF0000);
    private const int LowWordMask = 0xFFFF;
    private const int ErrorDiskFull = 0x70;
    private const int ErrorHandleDiskFull = 0x27;
    private const int UnixNoSpace = 28;

    /// <summary>Ce que disent les serveurs : ils n'ont pas de code commun.</summary>
    private static readonly string[] FullWords = ["no space left", "disk full", "disk quota", "quota exceeded", "insufficient storage", "not enough space", "over quota"];

    /// <summary>Place libre ; null quand on ne peut pas la connaître (partage réseau) : l'écriture dira si ça manque.</summary>
    public static long? FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Cette erreur, ou celle qui l'a causée, veut-elle dire « plus de place » ?</summary>
    public static bool IsFull(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            int code = ex.HResult;
            if ((code & WindowsErrorMask) == WindowsErrorFamily && (code & LowWordMask) is ErrorDiskFull or ErrorHandleDiskFull) return true;
            if (ex is IOException && code == UnixNoSpace) return true;
            if (FullWords.Any(word => ex.Message.Contains(word, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    /// <summary>Message quand un fichier ne rentre plus, en cours de copie.</summary>
    public static string FullMessage(string target, long size, bool canResume)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(target));
        var free = FreeBytes(target) is { } bytes ? L.F(", il reste {0}", FileEntry.FormatSize(bytes)) : string.Empty;
        var message = L.F("Plus de place sur {0} : « {1} » ({2}) ne rentre pas{3}.", root, Path.GetFileName(target), FileEntry.FormatSize(size), free);
        return canResume
            ? message + L.T("\nLibère de la place puis clique sur Reprendre : la copie continue à ce fichier. Si tu annules, ce qui est déjà copié reste en place.")
            : message;
    }

    /// <summary>Message quand tout ne pourra pas rentrer, avant de commencer.</summary>
    public static string ShortfallMessage(string folder, long needed, long free, bool canResume)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(folder));
        var message = L.F("Pas assez de place sur {0} : il faut {1}, il reste {2} (il manque {3}).", root, FileEntry.FormatSize(needed), FileEntry.FormatSize(free), FileEntry.FormatSize(needed - free));
        return canResume ? message + L.T("\nLibère de la place puis clique sur Reprendre, ou annule.") : message;
    }

    /// <summary>
    /// Avant de commencer : s'il manque de la place, l'opération attend en pause (sans contrôleur de pause : elle s'arrête).
    /// Reprendre continue quoi qu'il arrive : c'est une estimation, et si ça ne rentre vraiment pas, l'écriture le dira.
    /// </summary>
    public static void CheckBeforeStart(string folder, long needed, PauseController? pause, CancellationToken ct)
    {
        if (FreeBytes(folder) is not { } free || free >= needed) return;
        if (pause is null) throw new FileOperationException(ShortfallMessage(folder, needed, free, canResume: false));
        pause.PauseFor(ShortfallMessage(folder, needed, free, canResume: true));
        pause.WaitIfPaused(ct);
    }
}
