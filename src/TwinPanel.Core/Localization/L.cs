// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text.Json;

namespace TwinPanel.Core.Localization;

/// <summary>
/// Traduction de l'interface. Le texte français écrit dans le code sert de clé ; en.json donne son équivalent anglais.
/// Sans traduction, le français s'affiche. La langue est choisie une fois au démarrage (changement après redémarrage).
/// </summary>
public static class L
{
    public const string French = "fr";
    public const string English = "en";

    private static Dictionary<string, string>? _texts;

    /// <summary>Langue en service : « fr » ou « en ».</summary>
    public static string Language { get; private set; } = French;

    /// <summary>Langue à utiliser : celle du réglage, sinon le français si Windows est en français, sinon l'anglais.</summary>
    public static string Resolve(string? setting, CultureInfo systemCulture) => setting switch
    {
        French => French,
        English => English,
        _ => systemCulture.TwoLetterISOLanguageName == French ? French : English,
    };

    /// <summary>Met une langue en service. À appeler avant d'ouvrir la moindre fenêtre.</summary>
    public static void Use(string language)
    {
        _texts = language == English ? LoadEnglish() : null;
        Language = _texts is null ? French : English;
    }

    /// <summary>Texte traduit.</summary>
    public static string T(string french) =>
        _texts is not null && _texts.TryGetValue(french, out var text) ? text : french;

    /// <summary>Texte traduit dont les {0}, {1}… sont remplacés par les valeurs données.</summary>
    public static string F(string french, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(french), values);

    /// <summary>Toutes les traductions anglaises (clé : le texte français).</summary>
    public static Dictionary<string, string> LoadEnglish()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("TwinPanel.Core.Localization.en.json")
            ?? throw new InvalidOperationException("Traductions introuvables dans le programme.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
