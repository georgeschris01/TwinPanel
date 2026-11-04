// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TwinPanel.Core;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.ViewModels;

public enum DriveButtonKind { Drive, Separator, Connection }

/// <summary>Un bouton de la barre des lecteurs : un disque (C:, clé USB, réseau…) ou une connexion enregistrée.</summary>
public partial class DriveButtonViewModel : ObservableObject
{
    public DriveButtonKind Kind { get; init; }
    /// <summary>Racine du lecteur (« C:\ ») ou identifiant de la connexion.</summary>
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Tooltip { get; init; } = string.Empty;
    public string IconKey { get; init; } = "IconDrive";
    public ConnectionProfile? Profile { get; init; }
    /// <summary>Nom court de la connexion ouverte (sftp://alias/…), null si fermée.</summary>
    public string? OpenAlias { get; init; }

    [ObservableProperty] private bool _isCurrent;

    public bool IsSeparator => Kind == DriveButtonKind.Separator;
    public bool IsButton => Kind != DriveButtonKind.Separator;
    public bool IsConnected => OpenAlias is not null;

    /// <summary>Groupe de boutons qu'on peut ranger en les glissant : « network » (lecteurs réseau), « connection » ; null : bouton fixe.</summary>
    public string? MoveGroup => Kind switch
    {
        DriveButtonKind.Connection => "connection",
        DriveButtonKind.Drive when IconKey == "IconNetwork" => "network",
        _ => null,
    };
    public Geometry? Icon => Application.Current?.TryGetResource(IconKey, null, out var g) == true ? g as Geometry : null;

    // ---- Couleur de l'icône (Options → Couleurs ; décoché : couleur d'origine du thème) ----

    private static bool s_custom;
    private static Dictionary<string, string> s_typeColors = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> s_driveColors = new(StringComparer.OrdinalIgnoreCase);

    public static void Configure(TwinPanel.Core.Settings.AppearanceSettings a)
    {
        s_custom = a.CustomDriveColors;
        s_typeColors = new Dictionary<string, string>(a.DriveTypeColors, StringComparer.OrdinalIgnoreCase);
        s_driveColors = new Dictionary<string, string>(a.DriveColors, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Type pour les couleurs : Drive, Usb, Network, Cd, Server.</summary>
    public static string TypeOf(string iconKey) => iconKey switch
    {
        "IconUsb" => "Usb",
        "IconNetwork" => "Network",
        "IconCd" => "Cd",
        "IconServer" => "Server",
        _ => "Drive",
    };

    /// <summary>Couleur choisie (#RRGGBB) : celle du disque, sinon celle de son type ; null : couleur d'origine.</summary>
    public string? IconColor
    {
        get
        {
            if (!s_custom || Kind == DriveButtonKind.Separator) return null;
            var letter = Kind == DriveButtonKind.Drive ? Label : null;
            if (letter is not null && s_driveColors.TryGetValue(letter, out var own) && TwinPanel.Core.Customization.FileColorRules.IsColor(own)) return own;
            return s_typeColors.TryGetValue(TypeOf(IconKey), out var t) && TwinPanel.Core.Customization.FileColorRules.IsColor(t) ? t : null;
        }
    }

    public bool HasIconColor => IconColor is not null;
    public IBrush? IconBrush => IconColor is { } c ? new SolidColorBrush(Color.Parse(c)) : null;

    /// <summary>Signature pour ne reconstruire la barre que si quelque chose a vraiment changé.</summary>
    public string Signature => $"{Kind}|{Key}|{Label}|{Tooltip}|{OpenAlias}|{IconColor}";
}

/// <summary>Lecture des lecteurs (hors du fil de l'interface : un lecteur réseau déconnecté peut mettre du temps à répondre).</summary>
public static class DriveScanner
{
    public sealed record DriveSnapshot(string Root, string Label, string Tooltip, string IconKey);

    public static IReadOnlyList<DriveSnapshot> Scan()
    {
        var list = new List<DriveSnapshot>();
        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add(new DriveSnapshot(home, "~", home, "IconHome"));
        }
        foreach (var d in LocalDisks.Usable())
        {
            try
            {
                if (!d.IsReady) continue;
                var free = L.F("{0} libres sur {1}", FileEntry.FormatSize(d.AvailableFreeSpace), FileEntry.FormatSize(d.TotalSize));
                if (OperatingSystem.IsLinux())
                {
                    list.Add(new DriveSnapshot(d.Name, LocalDisks.ShortName(d.Name), $"{d.Name}\n{free}", d.DriveType switch
                    {
                        DriveType.Network => "IconNetwork",
                        DriveType.CDRom => "IconCd",
                        _ => LocalDisks.IsRemovable(d.Name) ? "IconUsb" : "IconDrive",
                    }));
                    continue;
                }
                var letter = d.Name.TrimEnd('\\', '/');
                var volume = string.IsNullOrWhiteSpace(d.VolumeLabel) ? TypeName(d.DriveType) : d.VolumeLabel;
                list.Add(new DriveSnapshot(d.Name, letter, $"{letter}  {volume}\n{free}", d.DriveType switch
                {
                    DriveType.Removable => "IconUsb",
                    DriveType.Network => "IconNetwork",
                    DriveType.CDRom => "IconCd",
                    _ => "IconDrive",
                }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* lecteur qui ne répond pas : ignoré */ }
        }
        return list;
    }

    private static string TypeName(DriveType t) => t switch
    {
        DriveType.Removable => L.T("Disque amovible"),
        DriveType.Network => L.T("Lecteur réseau"),
        DriveType.CDRom => L.T("Lecteur CD / DVD"),
        DriveType.Ram => L.T("Disque en mémoire"),
        _ => L.T("Disque local"),
    };
}
