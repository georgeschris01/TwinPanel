// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Help;

/// <summary>Astuces du bandeau de démarrage : d'abord les gestes souris, qu'on ne découvre pas tout seul.</summary>
public static class Tips
{
    public static IReadOnlyList<string> All { get; } =
    [
        L.T("Clic droit court sur un fichier : il est marqué. Maintiens le bouton droit en glissant pour en marquer plusieurs."),
        L.T("Maintiens le clic droit une demi-seconde sur un fichier pour ouvrir son menu."),
        L.T("Clic milieu sur un dossier : il s'ouvre dans un nouvel onglet, en arrière-plan."),
        L.T("Clic milieu sur un onglet : il se ferme."),
        L.T("Double-clic dans le vide de la liste : retour au dossier parent."),
        L.T("Les boutons latéraux de la souris font dossier précédent et dossier suivant."),
        L.T("Ctrl + molette agrandit le texte des panneaux. Ctrl + clic milieu le remet à sa taille normale."),
        L.T("Glisse un fichier vers l'autre panneau, un onglet, un lecteur ou l'Explorateur : au lâcher, TwinPanel demande s'il faut copier ou déplacer."),
        L.T("Glisse un onglet pour le ranger ailleurs, ou lâche-le sur l'autre panneau pour l'y envoyer."),
        L.T("Glisse le titre d'une colonne pour changer l'ordre des colonnes. Clic droit sur les titres : choisir celles à afficher."),
        L.T("Clique sur le nom du fichier déjà sélectionné pour le renommer directement dans la liste."),
        L.T("Clic droit sur le bouton Précédent : les derniers dossiers visités."),
        L.T("Clic droit sur un onglet : le verrouiller, le renommer, le dupliquer."),
        L.T("Tape simplement les premières lettres d'un nom pour le trouver dans la liste."),
        L.T("Espace sur un dossier calcule sa taille. L'option « Taille des dossiers » les calcule toutes."),
        L.T("Ctrl+B : vue à plat, tous les fichiers des sous-dossiers dans une seule liste."),
        L.T("Ctrl+Maj+J : enregistre tes onglets en jeu, et rappelle-les d'un clic."),
        L.T("Ctrl+M : renomme plusieurs fichiers, aussi selon les médias : titre, artiste, date de prise de vue."),
        L.T("Ctrl+Q : aperçu du fichier sous le curseur dans l'autre panneau."),
        L.T("Alt+F7 : cherche aussi les doublons, même sur un serveur."),
        L.T("Ctrl+F : gère tes connexions SFTP, FTP et WebDAV. Les serveurs s'utilisent comme des dossiers."),
        L.T("F12 : un terminal dans le bas de la fenêtre, ouvert dans le dossier du panneau actif, même sur un serveur SSH."),
        L.T("Entrée sur une archive l'ouvre comme un dossier. Zip, 7z et tar sont modifiables."),
        L.T("Ctrl+Maj+S : synchronise deux dossiers, et garde les réglages dans un profil."),
        L.T("Tous les raccourcis se changent dans Options → Raccourcis. F1 ouvre le guide complet."),
    ];

    /// <summary>L'astuce du rang demandé ; après la dernière, on recommence.</summary>
    public static string At(int index) => All[Math.Abs(index % All.Count)];
}
