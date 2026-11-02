// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TwinPanel.Core;

/// <summary>
/// Corbeille des bureaux Linux (spécification freedesktop.org) : l'élément rejoint « files », et une fiche dans « info »
/// garde son chemin d'origine. Les gestionnaires de fichiers du système la voient et savent restaurer.
/// Un élément qui ne peut pas y aller reste en place : une erreur vaut mieux qu'un effacement que personne n'a demandé.
/// </summary>
public static class DesktopTrash
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Met à la corbeille du disque où se trouve l'élément.</summary>
    [SupportedOSPlatform("linux")]
    public static void Delete(string path)
    {
        var home = HomeTrash();
        var mount = MountOf(Path.GetDirectoryName(path) ?? path);
        if (mount == MountOf(ExistingParent(home)))
        {
            MoveTo(path, home, topDirectory: null, DateTime.Now);
            return;
        }
        MoveTo(path, VolumeTrash(mount), mount, DateTime.Now);
    }

    /// <summary>Déplace l'élément dans cette corbeille. Avec topDirectory, le chemin gardé est relatif à ce disque : il reste juste s'il est monté ailleurs.</summary>
    internal static void MoveTo(string path, string trash, string? topDirectory, DateTime now)
    {
        var name = Path.GetFileName(path);
        try
        {
            var files = Path.Combine(trash, "files");
            var infos = Path.Combine(trash, "info");
            CreateFolder(trash);
            CreateFolder(files);
            CreateFolder(infos);
            var kept = topDirectory is null ? path : Path.GetRelativePath(topDirectory, path);
            var (target, info) = Reserve(name, files, infos, InfoText(kept, now));
            try
            {
                if (Directory.Exists(path) && File.ResolveLinkTarget(path, returnFinalTarget: false) is null) Directory.Move(path, target);
                else File.Move(path, target);
            }
            catch
            {
                File.Delete(info);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileOperationException(L.F("« {0} » n'a pas pu aller à la corbeille, rien n'a été supprimé : {1}", name, ex.Message), ex);
        }
    }

    /// <summary>Crée la fiche d'abord : sa création exclusive réserve le nom, même si un autre programme vide ou remplit la corbeille en même temps.</summary>
    private static (string Target, string Info) Reserve(string name, string files, string infos, string text)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        for (int i = 1; ; i++)
        {
            var candidate = i == 1 ? name : $"{Path.GetFileNameWithoutExtension(name)}.{i}{Path.GetExtension(name)}";
            var target = Path.Combine(files, candidate);
            var info = Path.Combine(infos, candidate + ".trashinfo");
            if (File.Exists(target) || Directory.Exists(target)) continue;
            try
            {
                using var stream = new FileStream(info, FileMode.CreateNew, FileAccess.Write);
                stream.Write(bytes);
                return (target, info);
            }
            catch (IOException) when (File.Exists(info))
            {
                // Nom pris entre-temps : on passe au suivant.
            }
        }
    }

    /// <summary>Contenu de la fiche : le chemin est encodé comme une adresse, la date est celle de l'heure locale.</summary>
    internal static string InfoText(string path, DateTime date)
    {
        var encoded = string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        return $"[Trash Info]\nPath={encoded}\nDeletionDate={date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}\n";
    }

    private static void CreateFolder(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, OwnerOnly);
    }

    private static string HomeTrash()
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(data)) data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(data, "Trash");
    }

    private static string ExistingParent(string path)
    {
        var current = path;
        while (!Directory.Exists(current) && Path.GetDirectoryName(current) is { } parent) current = parent;
        return current;
    }

    /// <summary>Le point de montage le plus long qui contient ce chemin.</summary>
    internal static string MountOf(string path, IEnumerable<string>? mounts = null)
    {
        mounts ??= DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName);
        var full = EndingWithSeparator(Path.GetFullPath(path));
        return mounts.Select(EndingWithSeparator)
            .Where(m => full.StartsWith(m, StringComparison.Ordinal))
            .OrderByDescending(m => m.Length)
            .FirstOrDefault() ?? Path.DirectorySeparatorChar.ToString();
    }

    private static string EndingWithSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    /// <summary>
    /// Corbeille d'un autre disque : « .Trash/uid » si l'administrateur l'a prévue, sinon « .Trash-uid ».
    /// Un dossier en lien, ou ouvert aux autres, est refusé : on n'y déposerait pas ses fichiers à la vue de tous.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static string VolumeTrash(string mount)
    {
        var user = GetUserId().ToString(CultureInfo.InvariantCulture);
        var shared = Path.Combine(mount, ".Trash");
        if (Directory.Exists(shared) && File.ResolveLinkTarget(shared, false) is null
            && File.GetUnixFileMode(shared).HasFlag(UnixFileMode.StickyBit))
        {
            var own = Path.Combine(shared, user);
            if (IsPrivateOrAbsent(own)) return own;
        }
        var personal = Path.Combine(mount, ".Trash-" + user);
        if (IsPrivateOrAbsent(personal)) return personal;
        throw new FileOperationException(L.F("La corbeille de ce disque n'est pas sûre : {0}. Rien n'a été supprimé.", personal));
    }

    [SupportedOSPlatform("linux")]
    private static bool IsPrivateOrAbsent(string folder)
    {
        if (!Directory.Exists(folder) && !File.Exists(folder)) return true;
        if (File.ResolveLinkTarget(folder, false) is not null || !Directory.Exists(folder)) return false;
        return (File.GetUnixFileMode(folder) & ~OwnerOnly & ~UnixFileMode.StickyBit) == UnixFileMode.None;
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUserId();
}
