// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.About;

/// <summary>
/// Nouveautés de la version installée (fenêtre « À propos » → « Nouveautés de cette version »).
/// À réécrire à chaque nouvelle version : seulement ce qui a changé depuis la précédente.
/// Version actuelle : Windows 0.8.10, Linux 0.1.14-beta.
/// </summary>
public static class ReleaseNotes
{
    /// <summary>Corrections de bug. windows : faux sous Linux (les lignes propres à Windows sont retirées).</summary>
    public static IReadOnlyList<string> Fixes(bool windows)
    {
        var list = new List<string>
        {
            L.T("Les onglets de serveur et les terminaux ne sont plus perdus si TwinPanel se ferme pendant le démarrage."),
        };
        if (windows) list.Add(L.T("« Lancer TwinPanel » à la fin de l'installation l'ouvre avec les droits de l'utilisateur, plus en administrateur."));
        list.AddRange(
        [
            L.T("Les onglets apparaissent tout de suite au démarrage ; les dossiers sont lus en même temps, l'onglet actif d'abord."),
            L.T("Les onglets de serveur apparaissent dès le démarrage (« connexion… ») ; les serveurs se connectent en même temps, leurs questions venant une à la fois. Sans réponse : clic droit sur l'onglet → Reconnecter."),
            L.T("Un onglet verrouillé garde le nom de son dossier verrouillé, même quand il affiche un autre dossier."),
            L.T("Après une copie ou un déplacement (F5, F6, glisser-déposer), la barre de couleur reste sur l'élément transféré et, dans l'autre panneau, va sur l'élément arrivé ; tous les éléments arrivés sont mis en valeur quelques secondes."),
            L.T("Taille des dossiers sur un serveur : le calcul va jusqu'au bout (mesure par le serveur en SSH, 4 dossiers en même temps, tailles gardées ; Ctrl+R recalcule)."),
            L.T("Le total en bas du panneau compte aussi la taille des dossiers déjà calculée."),
            L.T("Barre d'adresse : le clic droit (Copier, Coller…) ne referme plus la case."),
            L.T("Colonnes : les colonnes s'élargissent même quand la fenêtre est étroite."),
            L.T("Glisser-déposer : on n'entre dans un dossier que sur son nom, et le dossier visé est entouré."),
            L.T("Licence TwinPanel 1.1 : précisions ajoutées : usage comme outil de travail en entreprise permis (article 1), responsabilité gardée en cas de dol ou de faute lourde (article 9)."),
        ]);
        return list;
    }

    /// <summary>Nouvelles fonctionnalités.</summary>
    public static IReadOnlyList<string> Features() =>
    [
        L.T("Couper / Copier / Coller des fichiers (Ctrl+X / Ctrl+C / Ctrl+V), sur le PC, dans les archives et sur les serveurs."),
        L.T("Infobulle avec le texte entier sur tout texte coupé par « … »."),
        L.T("Lecteurs réseau et connexions de la barre des lecteurs : rangés en les glissant."),
        L.T("Tailles en To à partir de 1 024 Go."),
        L.T("Précédent / Suivant (bouton, souris, Alt+← / Alt+→) : le curseur revient sur l'élément où on était, souvent le dossier dans lequel on était entré, montré au milieu de la liste."),
        L.T("« Nouveautés de cette version » dans « À propos »."),
        L.T("« À propos » : total des téléchargements de TwinPanel pour le système utilisé (toutes versions), lu sur GitHub (rien n'est envoyé)."),
    ];
}
