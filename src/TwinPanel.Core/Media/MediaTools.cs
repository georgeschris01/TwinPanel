// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TwinPanel.Core.Media;

/// <summary>
/// Programmes externes (non livrés avec TwinPanel) : mkvpropedit (MKVToolNix, GPL) pour les pistes des MKV,
/// exiftool (Phil Harvey) pour écrire l'EXIF des photos. TwinPanel les cherche : chemin des Options, puis PATH,
/// puis les dossiers d'installation habituels.
/// </summary>
public static class MediaTools
{
    public const string MkvToolNixUrl = "https://mkvtoolnix.download/downloads.html";
    public const string ExifToolUrl = "https://exiftool.org/";

    /// <summary>Chemins choisis dans les Options (vides : recherche automatique).</summary>
    public static string MkvPropEditPath { get; set; } = string.Empty;
    public static string ExifToolPath { get; set; } = string.Empty;

    public static string? FindMkvPropEdit() => Find(MkvPropEditPath, "mkvpropedit",
        OperatingSystem.IsWindows()
            ? new[]
              {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MKVToolNix", "mkvpropedit.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MKVToolNix", "mkvpropedit.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MKVToolNix", "mkvpropedit.exe"),
              }
            : new[] { "/usr/bin/mkvpropedit", "/usr/local/bin/mkvpropedit", "/opt/homebrew/bin/mkvpropedit" });

    public static string? FindExifTool() => Find(ExifToolPath, "exiftool",
        OperatingSystem.IsWindows()
            ? new[]
              {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ExifTool", "exiftool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ExifTool", "exiftool.exe"),
                @"C:\Windows\exiftool.exe",
              }
            : new[] { "/usr/bin/exiftool", "/usr/local/bin/exiftool", "/opt/homebrew/bin/exiftool" });

    private static string? Find(string configured, string name, IEnumerable<string> usual)
    {
        if (configured.Length > 0) return File.Exists(configured) ? configured : null;
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return usual.FirstOrDefault(File.Exists);
    }

    /// <summary>Lance un programme et attend la fin (5 min maximum) ; retourne le code et tout le texte affiché.</summary>
    public static async Task<(int Exit, string Output)> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException(L.F("Impossible de lancer {0}.", Path.GetFileName(exe)));
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return (p.ExitCode, ((await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false))).Trim());
    }
}

/// <summary>Changement d'une piste d'un MKV (null : ne pas toucher).</summary>
public sealed record MkvTrackEdit(MediaInfoStream Kind, int Number)
{
    public string? Name { get; init; }
    /// <summary>Code de langue : « fr », « fre », « en-US »… (mkvpropedit l'écrit aussi en forme IETF).</summary>
    public string? Language { get; init; }
    public bool? IsDefault { get; init; }
    public bool? IsForced { get; init; }
    public bool IsEmpty => Name is null && Language is null && IsDefault is null && IsForced is null;
}

/// <summary>Modification des pistes et du titre d'un MKV sans le réécrire (mkvpropedit : instantané, sans perte).</summary>
public static class MkvEdit
{
    public static bool IsMkv(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mkv" or ".mka" or ".mks" or ".webm";

    /// <summary>Arguments pour mkvpropedit (le titre est null s'il ne change pas).</summary>
    public static List<string> BuildArguments(string file, string? title, IEnumerable<MkvTrackEdit> tracks)
    {
        var args = new List<string> { file };
        if (title is not null)
        {
            args.Add("--edit"); args.Add("info");
            if (title.Length == 0) { args.Add("--delete"); args.Add("title"); }
            else { args.Add("--set"); args.Add("title=" + title); }
        }
        foreach (var t in tracks.Where(t => !t.IsEmpty))
        {
            char type = t.Kind switch { MediaInfoStream.Video => 'v', MediaInfoStream.Audio => 'a', MediaInfoStream.Text => 's', _ => '?' };
            if (type == '?') continue;
            args.Add("--edit"); args.Add($"track:{type}{t.Number.ToString(CultureInfo.InvariantCulture)}");
            if (t.Name is { } name)
            {
                if (name.Length == 0) { args.Add("--delete"); args.Add("name"); }
                else { args.Add("--set"); args.Add("name=" + name); }
            }
            if (t.Language is { Length: > 0 } lang) { args.Add("--set"); args.Add("language=" + lang.Trim()); }
            if (t.IsDefault is { } d) { args.Add("--set"); args.Add("flag-default=" + (d ? "1" : "0")); }
            if (t.IsForced is { } f) { args.Add("--set"); args.Add("flag-forced=" + (f ? "1" : "0")); }
        }
        return args;
    }

    public static async Task ApplyAsync(string file, string? title, IReadOnlyList<MkvTrackEdit> tracks, CancellationToken ct)
    {
        var exe = MediaTools.FindMkvPropEdit() ?? throw new FileNotFoundException(
            L.F("mkvpropedit est introuvable. Installe MKVToolNix ({0}) ou indique son chemin dans Options → Outils médias.", MediaTools.MkvToolNixUrl));
        var args = BuildArguments(file, title, tracks);
        if (args.Count == 1) return;
        var (exit, output) = await MediaTools.RunAsync(exe, args, ct).ConfigureAwait(false);
        // 0 : réussi ; 1 : réussi avec avertissements ; 2 : erreur (rien n'est garanti).
        if (exit >= 2) throw new IOException(L.T("mkvpropedit a refusé : ") + LastLines(output));
    }

    internal static string LastLines(string output) =>
        string.Join(Environment.NewLine, output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(4));
}

/// <summary>Écriture de l'EXIF d'une photo avec exiftool (null : ne pas toucher ; "" : effacer).</summary>
public sealed record ExifChanges
{
    public DateTime? Taken { get; init; }
    public string? Artist { get; init; }
    public string? Copyright { get; init; }
    public string? Description { get; init; }
    public bool RemoveGps { get; init; }
    public bool IsEmpty => Taken is null && Artist is null && Copyright is null && Description is null && !RemoveGps;
}

public static class ExifEdit
{
    public static List<string> BuildArguments(string file, ExifChanges c)
    {
        // -overwrite_original : pas de copie « _original » laissée à côté ; -P : garde la date du fichier.
        var args = new List<string> { "-overwrite_original", "-P", "-charset", "filename=utf8" };
        if (c.Taken is { } t)
        {
            var v = t.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
            args.Add($"-DateTimeOriginal={v}");
            args.Add($"-CreateDate={v}");
        }
        if (c.Artist is { } a) { args.Add($"-Artist={a}"); args.Add($"-XMP-dc:Creator={a}"); }
        if (c.Copyright is { } cp) { args.Add($"-Copyright={cp}"); args.Add($"-XMP-dc:Rights={cp}"); }
        if (c.Description is { } d) { args.Add($"-ImageDescription={d}"); args.Add($"-XMP-dc:Description={d}"); }
        if (c.RemoveGps) args.Add("-gps:all=");
        args.Add(file);
        return args;
    }

    public static async Task ApplyAsync(string file, ExifChanges c, CancellationToken ct)
    {
        if (c.IsEmpty) return;
        var exe = MediaTools.FindExifTool() ?? throw new FileNotFoundException(
            L.F("exiftool est introuvable. Installe ExifTool ({0}) ou indique son chemin dans Options → Outils médias.", MediaTools.ExifToolUrl));
        var (exit, output) = await MediaTools.RunAsync(exe, BuildArguments(file, c), ct).ConfigureAwait(false);
        if (exit != 0) throw new IOException(L.T("exiftool a refusé : ") + MkvEdit.LastLines(output));
    }
}
