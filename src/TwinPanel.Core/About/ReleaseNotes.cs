// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.About;

/// <summary>
/// Nouveautés de la version installée (fenêtre « À propos » → « Nouveautés de cette version »).
/// À réécrire à chaque nouvelle version : seulement ce qui a changé depuis la précédente.
/// Version actuelle : Windows 0.8.11, Linux 0.1.15-beta.
/// </summary>
public static class ReleaseNotes
{
    /// <summary>Corrections de bug. windows : faux sous Linux (les lignes propres à Windows sont retirées).</summary>
    public static IReadOnlyList<string> Fixes(bool windows) =>
    [
        L.T("Recherche de texte : les PDF, epub et documents Word, Excel, PowerPoint, OpenDocument ne donnent plus de résultats sans rapport (leur contenu compressé était lu comme du texte)."),
        L.T("Archives .tar : après une modification (renommer, supprimer…), la liste affichée n'est plus parfois l'ancienne."),
        L.T("Infobulle des textes coupés : elle suit le texte quand il change sous la souris (barre d'état, listes qui se remplissent)."),
    ];

    /// <summary>Nouvelles fonctionnalités. windows : faux sous Linux (les lignes propres à Windows sont retirées).</summary>
    public static IReadOnlyList<string> Features(bool windows) =>
    [
        L.T("Recherche (Alt+F7) : un mot dans « Nom » sans * ni ? cherche les noms qui le contiennent (destin = *destin*), et le texte est cherché dans le vrai texte des documents (PDF, epub, Word, Excel, PowerPoint, OpenDocument)."),
        L.T("Recherche (Alt+F7) : colonnes des résultats élargies en tirant leur bord ; un nom ou un dossier trop long s'affiche en entier au survol de la souris, même pendant la recherche."),
        L.T("Recherche (Alt+F7) : les résultats ont les mêmes icônes et couleurs que les panneaux (couleurs des Options, vraies icônes du système si elles sont choisies)."),
        L.T("Options → Comportement → Glisser-déposer de fichiers : demander (petit menu), copier ou déplacer sans demander ; Maj déplace et Ctrl copie toujours."),
        L.T("Fichiers lâchés sur un onglet : l'onglet s'ouvre pour montrer les éléments arrivés (case dans Options → Comportement)."),
        L.T("Fichiers glissés au-dessus d'un onglet : il s'ouvre après un court instant, sans lâcher la souris (case dans Options → Comportement)."),
        L.T("Corbeille dans un panneau, sous Windows et Linux (bouton « Corbeille » de la barre des disques, menu ≡ → Fichiers) : restaurer, copier ailleurs (F5), voir (F3), supprimer définitivement (F8), vider."),
        L.T("Taille de l'interface (60 à 200 %, Options → Affichage), appliquée tout de suite à toutes les fenêtres, en plus de la mise à l'échelle du système ; taille des icônes des fichiers réglable à part."),
        L.T("Fichiers et dossiers cachés estompés (icône et nom à moitié transparents), comme dans l'Explorateur ; case dans Options → Affichage."),
        .. (windows ? [L.T("Version portable pour Windows (fichier .zip) : réglages dans le dossier Data à côté du programme, rien n'est écrit sur le PC ; reprise des réglages de la version installée ; mots de passe protégés par le mot de passe maître.")] : Array.Empty<string>()),
    ];
}
