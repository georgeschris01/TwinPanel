# TwinPanel

**Français** · [English](README.en.md)

Gestionnaire de fichiers à deux panneaux, gratuit, moderne et complet. Pour Windows, et pour Linux en version bêta. Interface en français et en anglais.

- Deux panneaux, onglets verrouillables et renommables, jeux d'onglets, vue rapide, arborescence, miniatures, vue à plat
- Copie et déplacement avec file de transferts, plusieurs transferts en même temps, reprise après coupure, contrôle de la place libre
- Serveurs SFTP, SCP, FTP, FTPS et WebDAV, utilisés comme des dossiers : gestionnaire de connexions, coffre chiffré pour les mots de passe et les clés, import de connexions
- Fichiers d'un serveur ouverts ou édités directement, et renvoyés à chaque enregistrement
- Terminal intégré sur le PC et sur les serveurs SSH : onglets, côte à côte, commandes enregistrées, recherche
- Visionneuse (`F3`) et éditeur intégrés, impression
- Archives zip, 7z, tar, tar.gz modifiables, zip chiffré AES-256, aussi sur un serveur
- Recherche, doublons, comparaison de dossiers, synchronisation avec profils
- Renommage multiple, aussi selon les infos des médias ; infos et tags des fichiers audio, vidéo et photo
- Propriétés complètes : droits Unix, propriétaire et groupe, droits NTFS
- Glisser-déposer, menus souris, boutons latéraux, clic milieu, `Ctrl` + molette
- Sauvegarde des réglages, et synchronisation chiffrée de la configuration entre vos machines Windows et Linux, onglets ouverts compris
- Thème clair ou sombre, raccourcis, couleurs, icônes et barre de boutons personnalisables
- Guide complet intégré (`F1`), avec FAQ et astuces

![TwinPanel sous Windows et Linux](docs/screenshots/demo-2k-fr.gif)

## Télécharger

**[TwinPanel pour Windows](../../releases/latest)** (installateur).

**[TwinPanel pour Linux, version bêta](../../releases/tag/linux-v0.1.13-beta)** : paquet `.deb` pour Debian et Ubuntu, paquet `.rpm` pour Fedora, ou AppImage. Bêta testée sur Debian 13 avec KDE, Ubuntu 26.04 et Fedora 45 avec GNOME : des défauts peuvent rester.

```
sudo apt install ./twinpanel_*_amd64.deb     # Debian, Ubuntu
sudo dnf install ./twinpanel-*.x86_64.rpm    # Fedora
```

Avec l'AppImage : `chmod +x TwinPanel-*.AppImage`, puis lancez le fichier. Sur Fedora 45, installez d'abord OpenSSL 3 : `sudo dnf install openssl3-libs` (le paquet `.rpm` le fait de lui-même). Sur Ubuntu, le Centre d'applications ne montre pas TwinPanel, car il ne connaît que son propre catalogue ; on le désinstalle avec `sudo apt remove twinpanel`.

![TwinPanel sous Linux (Ubuntu)](docs/screenshots/linux-ubuntu-main-2k-fr.png)

![TwinPanel sous Linux (Debian), thème clair](docs/screenshots/linux-debian-clair-2k-fr.png)

![Plusieurs terminaux côte à côte (Debian)](docs/screenshots/linux-debian-terminaux-2k-fr.png)

> **Message « Windows a protégé votre ordinateur » :** TwinPanel est gratuit et n'est pas signé, car un certificat de signature est payant chaque année. Windows affiche donc ce message tant que le programme n'est pas assez connu. Cliquez sur **Informations complémentaires**, puis sur **Exécuter quand même**. Vérifiez que le fichier vient bien de cette page ; son empreinte SHA-256 est donnée avec chaque version.

Pour demander une fonction ou signaler un problème : ouvrez une [« Issue »](../../issues) sur la page GitHub de TwinPanel. Voir [`CONTRIBUTING.md`](CONTRIBUTING.md).

## À l'origine de ce projet (Pourquoi et comment ?)

Ce logiciel est né d'un besoin très personnel. Depuis 2003, j'utilise au quotidien l'excellent logiciel **Total Commander**, que je trouve absolument génial. Cependant, pour mon utilisation précise, certaines fonctionnalités me manquaient ou n'étaient pas totalement optimales.

J'ai donc décidé de créer **TwinPanel**, à la base uniquement pour mon usage personnel et celui de ma femme. Après l'avoir montré à des amis qui ont souhaité le tester, ces derniers m'ont vivement conseillé de le partager et de le mettre à disposition d'autres utilisateurs potentiels, car il pourrait rendre de grands services — et ce, malgré l'existence d'autres projets similaires sur le marché.

### Une totale transparence avec vous :

Je tiens à être 100 % honnête avec tout le monde : **je n'y connais absolument rien en programmation et je ne parle pas d'autre langue que le français**.

**TwinPanel** a pu voir le jour, être entièrement codé et traduit en anglais grâce à l'aide de l'intelligence artificielle **Claude Code**. L'IA m'a accompagné pas à pas pour traduire mes besoins en lignes de code et pour générer la version anglaise du logiciel afin qu'il soit accessible au plus grand nombre.

Le programme est partagé ici tel quel, de bon cœur, dans le seul but d'être utile. Il est entièrement gratuit, mais comme vous pourrez le lire dans la section Licence ci-dessous, il reste protégé pour garantir qu'il ne soit jamais commercialisé ou dénaturé.

Bienvenue à tous les futurs utilisateurs de TwinPanel !

## Guide

Ce qui suit est le guide complet. C'est le même texte que dans le programme (touche **F1**).

<!-- guide -->
## Prise en main

TwinPanel montre deux dossiers côte à côte : on travaille dans le **panneau actif**, celui qui a le contour bleu, et la plupart des opérations visent l'**autre panneau**.

### L'écran

- En haut : la barre d'outils. Le bouton **≡** donne accès à toutes les commandes.
- Dans chaque panneau : les lecteurs et connexions, les onglets, le chemin, puis la liste des fichiers.
- En bas : la ligne de commande et les touches F3 à F8, cliquables.
- Les transferts en cours s'affichent juste au-dessus de la ligne de commande.

### Les cinq gestes à connaître

| Geste | Résultat |
|---|---|
| `Tab` | Changer de panneau |
| `Entrée` ou double-clic | Ouvrir un dossier ou un fichier |
| `Retour arrière` | Revenir au dossier parent |
| `Inser` ou clic droit court | Marquer un fichier |
| `F5` / `F6` | Copier / déplacer les fichiers marqués vers l'autre panneau |

Si rien n'est marqué, les opérations portent sur le fichier sous le curseur.

Pour aller plus vite vers un fichier, tapez simplement les premières lettres de son nom (`Échap` pour annuler).

## La souris

TwinPanel se pilote entièrement à la souris. Plusieurs gestes ne se devinent pas : voici la liste complète.

### Dans la liste des fichiers

| Geste | Résultat |
|---|---|
| Clic gauche | Choisir la ligne |
| Double-clic | Ouvrir le dossier ou le fichier |
| Clic sur le nom de la ligne déjà choisie | Renommer dans la liste (`Entrée` : valider, `Échap` : annuler) |
| Ctrl + clic | Marquer un élément |
| Maj + clic | Marquer une plage d'éléments |
| Clic droit court | Marquer ou démarquer l'élément |
| Bouton droit maintenu en glissant | Marquer ou démarquer tout ce que la souris survole |
| Bouton droit maintenu ½ seconde | Ouvrir le menu de l'élément (Couper, Copier, Coller…) |
| Clic milieu sur un dossier | L'ouvrir dans un nouvel onglet, en arrière-plan |
| Double-clic dans le vide | Revenir au dossier parent |
| Boutons latéraux | Dossier précédent / suivant |
| Ctrl + molette | Texte plus grand ou plus petit (en miniatures : taille des images) |
| Ctrl + clic milieu | Revenir à la taille normale |

Le clic sur le nom se désactive dans Options → Comportement.

### Glisser-déposer

Maintenez le bouton gauche sur un élément et déplacez la souris. Si l'élément est marqué, tous les éléments marqués partent avec lui.

Vous pouvez le lâcher sur un dossier, sur l'autre panneau, sur un onglet, sur un bouton de lecteur ou de connexion, ou dans un autre programme (Explorateur, messagerie…). Depuis l'Explorateur, le chemin inverse marche aussi. Glissés depuis un serveur ou une archive vers un autre programme, les éléments sont copiés sur le PC au moment où vous les lâchez (dans un dossier temporaire), puis donnés à ce programme ; l'attente dépend de leur taille. Au-delà de 500 Mo, utilisez plutôt Copier / Coller. Pour entrer dans un dossier de la liste, lâchez sur son **nom** (ou son icône) : une bordure le montre. Sur ses autres colonnes (Ext, Taille, Date…), les fichiers vont dans le dossier affiché du panneau. Sous Linux, tout dépend du type de session choisi à la connexion :

- **session X11** : le glisser-déposer marche dans les deux sens, de TwinPanel vers une autre application et l'inverse ;
- **session Wayland** : on ne peut pas lâcher dans une autre application. Depuis une autre application vers TwinPanel, le glissement reste possible.

Pour connaître votre session, tapez `echo $XDG_SESSION_TYPE` dans un terminal du bureau. Sous KDE, on choisit la session sur l'écran de connexion, avant de taper le mot de passe. Sous GNOME (Ubuntu, Fedora), la session est toujours Wayland.

| Au lâcher | Résultat |
|---|---|
| Sans touche | Menu « Copier ici / Déplacer ici », ou copier, ou déplacer : au choix dans Options → Comportement → **Glisser-déposer de fichiers** |
| Maj maintenue | Déplacer |
| Ctrl maintenue | Copier sans demander |

Des fichiers lâchés sur un onglet y sont copiés ou déplacés, et cet onglet s'ouvre pour montrer les éléments arrivés (case **Fichiers lâchés sur un onglet : ouvrir cet onglet** dans Options → Comportement). Sans lâcher, gardez la souris un court instant sur un onglet : il s'ouvre, et vous pouvez lâcher sur un dossier de sa liste (case **Fichiers glissés au-dessus d'un onglet : l'ouvrir après un court instant, sans lâcher**).

### Onglets et colonnes

| Geste | Résultat |
|---|---|
| Clic milieu sur un onglet | Le fermer |
| Clic droit sur un onglet | Dupliquer, renommer, verrouiller, fermer, supprimer les doublons |
| Glisser un onglet | Le ranger ailleurs, ou l'envoyer dans l'autre panneau |
| Molette sur les onglets | Faire défiler les onglets |
| Clic sur un titre de colonne | Trier par cette colonne |
| Glisser un titre de colonne | Changer l'ordre des colonnes |
| Tirer le bord d'un titre de colonne | Élargir ou rétrécir la colonne (le Nom prend la place qui reste ; si tout ne tient plus, une barre de défilement apparaît) |
| Clic droit sur les titres | Choisir les colonnes affichées |
| Souris sur un texte coupé par « … » | Infobulle avec le texte entier (noms, colonnes, listes des fenêtres) |
| Clic droit sur le bouton Précédent | Dossiers récents |

## Le clavier

Tous les raccourcis se changent dans Options → Raccourcis. Dans le programme, la liste ci-dessous montre ceux réellement en vigueur.

<!-- raccourcis -->
### Navigation

| Touche | Action |
|---|---|
| `Tab` | Changer de panneau |
| `Entrée` | Ouvrir |
| `Retour arrière` | Dossier parent |
| `Ctrl+T` / `Ctrl+W` | Nouvel onglet / fermer l'onglet |
| `Ctrl+Tab` / `Ctrl+Maj+Tab` | Onglet suivant / précédent |
| `Alt+←` / `Alt+→` | Dossier précédent / suivant (le curseur revient sur l'élément où on était, au milieu de la liste) |
| `Alt+↓` | Dossiers récents |
| `Ctrl+Maj+J` | Jeux d'onglets |
| `Ctrl+PgUp` / `Ctrl+\` | Dossier parent / racine |
| `Ctrl+D` | Favoris |
| `Ctrl+L` | Modifier le chemin |
| `Ctrl+H` | Fichiers cachés |
| `Ctrl+F3` à `Ctrl+F6` | Trier par nom / extension / date / taille |
| `Ctrl+U` | Échanger les panneaux |
| `Ctrl+Maj+O` | Même dossier dans l'autre panneau |
| `Ctrl+Maj+←` / `→` | Ouvrir le dossier dans l'autre panneau |
| `Ctrl+R` | Rafraîchir |
| `Ctrl+B` | Vue à plat |
| `Ctrl+Q` | Vue rapide |
| `Ctrl+F8` | Arborescence |
| `Ctrl+Maj+F1` | Miniatures |
| `F12` / `Ctrl+F12` | Terminal intégré : afficher ou masquer / nouveau terminal |
| `Ctrl+Maj+F12` | Deux terminaux côte à côte |
| `Alt+F12` | Changer de côté dans le terminal |
| `Ctrl+Maj+F` | Dans le terminal : chercher dans le texte |

### Marquage

| Touche | Action |
|---|---|
| `Inser` | Marquer et descendre |
| `Espace` | Marquer (calcule la taille d'un dossier) |
| `Maj+↑` / `Maj+↓` | Marquer en se déplaçant |
| `Num+` / `Num-` | Marquer / démarquer selon un masque |
| `Num*` | Inverser le marquage |
| `Alt+Num+` | Marquer la même extension |
| `Ctrl+A` / `Ctrl+Maj+A` | Tout marquer / tout démarquer |

### Opérations

| Touche | Action |
|---|---|
| `F1` | Ce guide |
| `F3` | Voir |
| `Alt+F3` | Ouvrir avec l'application associée |
| `F4` | Éditer |
| `Maj+F4` | Nouveau fichier texte |
| `F5` / `F6` | Copier / déplacer |
| `Ctrl+X` / `Ctrl+C` / `Ctrl+V` | Couper / copier / coller (presse-papiers, aussi avec l'Explorateur) |
| `Maj+F6` | Renommer |
| `Ctrl+M` | Renommage multiple |
| `Ctrl+Z` | Annuler le dernier renommage multiple |
| `F7` | Nouveau dossier |
| `F8` / `Suppr` | Supprimer |
| `Alt+Entrée` | Propriétés |
| `Alt+F5` / `Alt+F9` | Créer une archive / extraire |
| `Ctrl+Entrée` / `Ctrl+Maj+Entrée` | Nom / chemin dans la ligne de commande |
| `Ctrl+Maj+C` / `Ctrl+Alt+C` | Copier les chemins / les noms |
| `Ctrl+Maj+T` | Ouvrir un terminal ici |

### Recherche et connexions

| Touche | Action |
|---|---|
| `Alt+F7` | Rechercher |
| `Maj+F2` | Comparer les dossiers |
| `Ctrl+Maj+F2` | Comparer le contenu de deux fichiers |
| `Ctrl+Maj+S` | Synchroniser les dossiers |
| `Ctrl+F` | Gestionnaire de connexions |
| `Ctrl+N` | Connexion rapide |
| `Ctrl+Maj+F` | Déconnecter le panneau actif |

### Visionneuse et éditeur

| Touche | Action |
|---|---|
| `N` / `P` (visionneuse) | Fichier suivant / précédent |
| `1` `2` `3` `4` (visionneuse) | Texte, hexa, image, infos média |
| `Ctrl+F` (visionneuse) | Chercher |
| `Ctrl+S` (éditeur) | Enregistrer |
| `Ctrl+F` / `F3` (éditeur) | Rechercher / suivant |
| `Ctrl+H` (éditeur) | Remplacer partout |
| `Ctrl+G` (éditeur) | Aller à la ligne |
<!-- /raccourcis -->

## Onglets et vues

Chaque panneau a ses onglets : `Ctrl+T` pour en ouvrir un, `Ctrl+W` pour le fermer. Ils sont rouverts au démarrage suivant, y compris ceux sur un serveur (Options → Démarrage). Les onglets d'un serveur apparaissent tout de suite avec « connexion… », puis s'ouvrent dès que leur serveur répond ; les serveurs se connectent en même temps, et leurs questions (mot de passe, phrase secrète) viennent une à la fois. Un serveur qui ne répond pas laisse ses onglets « non connecté » : clic droit sur l'onglet → **Reconnecter**.

### Verrouiller un onglet

Clic droit sur l'onglet :

- **Verrouiller cet onglet** : aller ailleurs ouvre un nouvel onglet.
- **Verrouiller avec changement de dossier** : vous pouvez naviguer, et l'onglet revient à son dossier quand vous le reprenez.

Un onglet verrouillé garde le nom de son dossier verrouillé, même quand il affiche un autre dossier.

### Jeux d'onglets

`Ctrl+Maj+J` ou ≡ → Onglets : enregistrez les onglets des deux panneaux sous un nom, puis rappelez-les d'un clic.

### Vues

- **Vue rapide** (`Ctrl+Q`) : l'autre panneau montre le fichier sous le curseur.
- **Arborescence** (`Ctrl+F8`) : l'arbre des dossiers à côté de la liste.
- **Miniatures** (`Ctrl+Maj+F1`) : images et vidéos sur le PC, images sur un serveur (chaque onglet a son mode).
- **Vue à plat** (`Ctrl+B`) : tous les fichiers des sous-dossiers, sur le PC et sur un serveur. `Échap` arrête la lecture.

Le tri par taille (`Ctrl+F6`) classe aussi les dossiers dont la taille est connue : calculée automatiquement (option **Taille des dossiers**) ou avec la barre d'espace. Les dossiers restent avant les fichiers.

Le total en bas de la liste (« 98 dossier(s), 12 fichier(s) — 1,5 Go ») compte les fichiers du dossier affiché et la taille des dossiers déjà calculée. Sans l'option **Taille des dossiers**, un dossier qui ne contient que des sous-dossiers montre donc 0 o. Au-delà de 1 024 Go, la taille s'affiche en To (format de taille « Automatique », Options → Affichage). « … » après le total : des calculs sont encore en cours. Sur un serveur SSH (SFTP, SCP), c'est le serveur qui mesure (`du`), beaucoup plus vite et quatre dossiers à la fois, avec ses sous-dossiers sur deux niveaux : en y entrant, les tailles sont déjà là. Les tailles connues sont gardées pendant toute la séance, tant que la date du dossier ne change pas ; le rafraîchissement automatique ne les recalcule pas. `Ctrl+R` les recalcule (utile quand un fichier a changé plus loin dans l'arborescence, ce qui ne change pas la date du dossier).

## Marquer et travailler sur les fichiers

### Marquer

Marquez avec `Inser`, `Espace`, `Maj+flèches`, le clic droit, ou selon un masque avec `Num+` : `*.jpg;*.png` choisit deux types, `*.* | *.tmp` choisit tout sauf les `.tmp`.

### Copier et déplacer

`F5` copie, `F6` déplace. La fenêtre permet de changer la destination et de choisir quoi faire si un élément existe déjà : demander, écraser, écraser les plus anciens, ignorer, ou garder les deux.

- **Couper / Copier / Coller** (`Ctrl+X` / `Ctrl+C` / `Ctrl+V`, ou le menu du bouton droit maintenu) : ouvrez le dossier d'arrivée, dans n'importe quel panneau, puis collez. La même fenêtre que `F5` / `F6` confirme. Cela marche aussi pour les serveurs et les archives, et avec l'Explorateur (ou un autre programme) dans les deux sens. Copiés depuis un serveur ou une archive, les éléments sont d'abord copiés sur le PC, dans un dossier temporaire (avec une question au-delà de 500 Mo) : dans TwinPanel, Coller marche tout de suite ; dans les autres programmes, dès que la copie est finie. Ils y reçoivent une copie : couper ne retire rien du serveur, et une modification faite dans l'autre programme n'y retourne pas. Ces copies sont effacées au Copier suivant, et au démarrage quand elles ont plus d'un jour. Des éléments coupés ne se collent qu'une fois. Quand on tape du texte (chemin, nom, ligne de commande), ces touches gardent leur rôle habituel.
- Le nombre de transferts en même temps se règle dans Options → Comportement (1 par défaut) : les suivants attendent leur tour.
- Un transfert peut être mis en pause, repris ou annulé.
- Après une copie ou un déplacement (`F5`, `F6`, glisser-déposer), la barre de couleur reste sur l'élément transféré (déplacé : sur la première ligne restante qui le suivait, même pour plusieurs éléments). Dans le panneau d'arrivée, elle va sur le premier élément arrivé, et tous les éléments arrivés sont mis en valeur (fond vert sur leur nom) pendant 5 secondes ou jusqu'au premier clic, sans être marqués. Le clavier reste dans le panneau de départ.
- S'il n'y a plus de place, TwinPanel s'arrête sur le fichier en cause. Libérez de la place puis cliquez sur **Reprendre**.

### Voir, éditer, supprimer

- `F3` ouvre la visionneuse intégrée : texte, hexa, image, infos média.
- `F4` ouvre l'éditeur choisi, par défaut le Bloc-notes sous Windows et l'éditeur du système sous Linux. Un éditeur intégré est aussi disponible, et chaque extension peut avoir son éditeur (Options → Éditeurs).
- `F8` envoie à la corbeille du système. Sur un serveur ou dans une archive, la suppression est définitive et toujours confirmée.
- Clic droit → **Partager…** envoie les fichiers marqués, ou celui sous le curseur. Sous Windows, c'est la fenêtre de partage de Windows : partage à proximité, courrier, applications. Sous Linux, TwinPanel propose **Par courriel** (la messagerie habituelle s'ouvre, fichiers joints) et chaque appareil relié par KDE Connect (téléphone, autre ordinateur). Un fichier d'un serveur ou d'une archive est d'abord copié dans un dossier temporaire. Les dossiers ne se partagent pas.

### Renommage multiple

`Ctrl+M` montre le résultat avant de renommer. Le masque du nom accepte `[N]` nom, `[N1-3]` trois premières lettres, `[C]` compteur, `[YMD]` date, `[h][m][s]` heure, `[P]` dossier. Rechercher / remplacer accepte les expressions régulières. `Ctrl+Z` annule le dernier renommage multiple.

### Propriétés

`Alt+Entrée` montre la taille réelle, les dates, les droits, le propriétaire et le groupe sur un serveur ou sous Linux, les attributs et la sécurité NTFS sous Windows, et les tags des médias.

## Rechercher, comparer, synchroniser

### Recherche

`Alt+F7` cherche par nom, texte contenu, taille et date, dans plusieurs dossiers et sur les serveurs ouverts. La case **Doublons** trouve les fichiers au contenu identique.

Dans **Nom**, un mot sans `*` ni `?` cherche les noms qui le contiennent : `destin` trouve « Le destin.epub » comme « Destinations.pdf » (comme `*destin*`). **Contenant le texte** cherche à l'intérieur des fichiers. Avec la case **Lire le texte des documents** (cochée au départ), le texte est cherché dans le vrai texte des PDF, epub, Word, Excel, PowerPoint et OpenDocument : ces fichiers sont compressés, et sans elle la recherche tomberait sur des morceaux sans rapport. Un PDF scanné (une image) n'a pas de texte à lire.

Les colonnes des résultats s'élargissent en tirant le bord de leur titre. Un nom ou un dossier trop long se termine par « … » et s'affiche en entier au survol de la souris, même pendant la recherche.

Sur un serveur, les tailles sont comparées d'abord. En SSH, le serveur calcule lui-même les empreintes, sans rien transférer. Sinon, TwinPanel annonce le volume à lire et demande votre accord.

### Comparaison

- `Maj+F2` compare les deux panneaux et marque les différences.
- `Ctrl+Maj+F2` compare le contenu de deux fichiers.
- ≡ → Outils : créer et vérifier des sommes de contrôle MD5, SHA-1 et SHA-256.

### Synchronisation

`Ctrl+Maj+S` ouvre la synchronisation. Choisissez les dossiers et le sens, cliquez sur **Comparer**, vérifiez la liste, puis lancez. Cliquez sur une flèche pour changer l'action d'une ligne. Rien n'est copié sans cette étape.

Un **profil** garde les dossiers, le sens et les options.

## Serveurs

TwinPanel se connecte en SFTP, SCP, FTP, FTPS et WebDAV. Un serveur s'utilise comme un dossier : `F5` envoie ou télécharge, y compris d'un serveur à l'autre.

### Se connecter

- `Ctrl+N` : connexion rapide, par exemple `utilisateur@192.168.0.10`, `ftps://…` ou `https://…/remote.php/dav/files/moi`.
- `Ctrl+F` : gestionnaire de connexions, avec fiches, groupes, clés SSH, identités, serveurs connus et coffre.
- Un bouton dans la barre des lecteurs ouvre chaque connexion enregistrée. Glissez les boutons des connexions, comme les onglets, pour les ranger dans l'ordre voulu. Les lecteurs réseau de Windows (Z:…) se rangent de la même façon, entre eux. Les disques du PC gardent leur place. L'ordre sert aux deux panneaux et il est retenu.
- Les connexions de FileZilla, WinSCP, PuTTY, OpenSSH et Total Commander s'importent.
- Un serveur inaccessible directement passe par un **serveur de rebond**, choisi dans sa fiche. Ce rebond peut lui-même passer par un autre, jusqu'à 8 à la suite. L'empreinte de chaque serveur est contrôlée.

### Sécurité

Les mots de passe enregistrés sont chiffrés dans un coffre lié à votre compte Windows, ou au trousseau du bureau sous Linux, avec un mot de passe maître facultatif. L'export des connexions ne contient aucun secret.

Le mot de passe maître se définit ou se change dans Options → Sécurité (ou Gestionnaire de connexions, partie Coffre). TwinPanel donne alors un **code de secours** de 24 caractères (lettres, chiffres et caractères spéciaux), montré une seule fois : notez-le, copiez-le ou **enregistrez-le** dans un fichier, de préférence hors du PC (clé USB). Si vous oubliez le mot de passe maître, cliquez sur **Mot de passe maître oublié ?** dans la fenêtre qui le demande : avec le code de secours, vous choisissez un nouveau mot de passe sans rien perdre. Un nouveau code remplace l'ancien à chaque changement du mot de passe maître.

À la première connexion SSH, comparez l'empreinte du serveur avant de l'accepter. Si elle change ensuite, TwinPanel bloque la connexion et vous prévient.

### Fichiers sur un serveur

`F4`, `Entrée` ou un double-clic ouvrent une copie sur le PC. À chaque enregistrement, TwinPanel propose de la renvoyer sur le serveur (ou sans demander : Options → Comportement).

### Transfert coupé

TwinPanel retente tout seul (3 fois par défaut), puis propose **Reprendre**. La reprise repart de l'endroit atteint en SFTP, et en FTP / FTPS ou en téléchargement WebDAV si le serveur l'accepte. Sinon, le fichier en cours repart de zéro. Les fichiers déjà terminés ne sont jamais renvoyés.

Si TwinPanel a été fermé pendant un transfert, il propose au démarrage suivant **Reprendre**, **Plus tard** ou **Oublier**. Pour y revenir : ≡ → Connexions → Transferts non terminés.

### Retrouver sa configuration sur une autre machine

Options → Synchronisation : choisis un dossier partagé entre tes machines (Nextcloud, OneDrive, Syncthing, clé USB…), puis **Envoyer**. Sur l'autre machine, Windows ou Linux, indique ce dossier et clique sur **Récupérer**, puis sur OK. Au tout premier lancement, TwinPanel le propose de lui-même.

- **Ce qui voyage**, au choix à chaque envoi : connexions avec clés SSH et mots de passe enregistrés, serveurs connus, réglages communs (commandes enregistrées, thème, couleurs, raccourcis, langue) et **onglets ouverts**. En récupérant, une fenêtre montre ce que contient le fichier : décochez ce que vous ne voulez pas reprendre.
- **Onglets reçus** : ils remplacent ceux ouverts quand vous cliquez sur OK, après une question. Un onglet de serveur s'ouvre si sa connexion existe sur cette machine. Un onglet d'un dossier du PC ne s'ouvre que sur le même système (de Windows à Windows, ou de Linux à Linux), et si le dossier existe.
- **Ce qui reste sur chaque machine** : favoris, jeux d'onglets, éditeurs, terminal, boutons et taille de la fenêtre.
- **Phrase secrète** : tout le fichier est chiffré avec une phrase d'au moins 12 caractères, demandée à chaque fois. TwinPanel ne la garde pas : **si tu l'oublies, personne ne pourra plus ouvrir le fichier**. Il te restera à ressaisir tes mots de passe et à envoyer de nouveau.
- **Rien n'est automatique** : Récupérer garde les connexions absentes du fichier, et Envoyer remplace le fichier, en prévenant si une autre machine y a écrit depuis. Une connexion supprimée sur une machine reste donc sur les autres.

## Archives

`Entrée` ouvre une archive comme un dossier. `Retour arrière` en sort.

| Format | Possible |
|---|---|
| zip, 7z, tar, tar.gz | Lire, extraire, modifier |
| rar, gz, bz2, xz | Lire et extraire |
| zip chiffré | Lire, extraire, modifier avec son mot de passe |
| 7z chiffré | Lire et extraire seulement |

| Touche | Action |
|---|---|
| `F5` (dans une archive) | Extraire vers l'autre panneau |
| `F6` (dans une archive) | Déplacer hors de l'archive |
| `F5` / `F6` vers une archive ouverte | Ajouter dans l'archive |
| `Maj+F6`, `F7`, `F8` | Renommer, créer un dossier, supprimer |
| `Alt+F9` | Extraire toute l'archive à côté d'elle |
| `Alt+F5` | Créer une archive (.zip, .tar, .tar.gz), avec mot de passe en option |

Une archive modifiée est réécrite à côté, relue, puis elle remplace l'original : il faut de la place libre, et c'est long sur les gros fichiers.

Tout cela fonctionne aussi entre une archive et un serveur, dans les deux sens, et pour une archive qui se trouve sur un serveur. `Entrée` la télécharge une fois dans un dossier temporaire, avec une barre de progression ; la copie disparaît quand plus aucun onglet ne la montre. Pour modifier une archive d'un serveur, TwinPanel réécrit la copie, puis remplace l'archive sur le serveur en gardant ses droits. Une grosse archive est donc téléchargée, puis renvoyée en entier. Si elle a changé sur le serveur depuis son ouverture, rien n'est remplacé : sortez-en, puis rouvrez-la.

Le mot de passe d'une archive reste en mémoire jusqu'à la fermeture de TwinPanel et n'est enregistré nulle part. Dans un zip chiffré, le contenu est protégé, mais les noms des fichiers restent visibles.

## Médias

### Infos et tags

- Colonnes facultatives : durée, résolution, codec vidéo, langues audio (clic droit sur les titres des colonnes).
- `F3` puis `4` : rapport complet d'une vidéo, d'une musique ou d'une photo.
- `Alt+Entrée` : modifier les tags des musiques et des MP4.

Deux outils facultatifs, à installer soi-même, ajoutent des possibilités : **mkvpropedit** (pistes des MKV) et **exiftool** (EXIF des photos). Leur chemin se règle dans Options → Affichage.

### Renommer selon les médias

Dans `Ctrl+M`, sur les fichiers du PC : `[Titre]`, `[Artiste]`, `[Album]`, `[Piste]`, `[Année]`, `[Genre]`, `[Résolution]`, `[Durée]`, `[Date de prise de vue]`. Un fichier sans cette valeur garde son nom. Les noms anglais marchent aussi : `[Title]`, `[Artist]`…

## Terminal

Un vrai terminal dans le bas de la fenêtre, écrit pour TwinPanel : les couleurs, `vim`, `nano` et `htop` y fonctionnent.

### L'ouvrir

- `F12` ouvre le panneau. Si le clavier est déjà dans le terminal, `F12` le masque : les terminaux continuent de tourner.
- Le bouton **Terminal** de la barre du haut déroule un menu : afficher ou masquer, nouveau terminal, dupliquer, un programme de ton PC ou un de tes serveurs SFTP / SCP.
- `Ctrl+F12` ouvre un autre terminal, dans un nouvel onglet. Le bouton **+** après les onglets propose tes programmes et tes serveurs SFTP / SCP.
- Le bouton **⧉** d'un onglet le duplique : même programme ou même serveur, dans son dossier de départ. La copie porte un numéro dans son onglet, par exemple `serveur (2)`. Le clic droit sur un onglet propose aussi **Dupliquer** ; en côte à côte, tu choisis le côté de la copie.
- Le clic droit sur un onglet propose **Reconnecter** pour un serveur, **Relancer** sur ce PC : le terminal repart dans le même onglet, dans son dossier de départ, par exemple après une coupure de connexion. L'onglet reste ouvert après une coupure et rappelle la marche à suivre. Le terminal repart à neuf : un programme lancé avant la coupure ne revient pas, sauf s'il tourne dans `tmux` ou `screen` sur le serveur. S'il semble encore en marche, TwinPanel demande confirmation, car ce qui s'y exécute est fermé.
- Le bouton **Côte à côte** ou `Ctrl+Maj+F12` montre deux terminaux ensemble, avec une séparation à tirer. Chaque côté a ses onglets et son bouton **+**. S'il n'y a qu'un terminal, le second est sa copie. Clique dans un côté pour le rendre actif : il reçoit le clavier et les commandes enregistrées. `Alt+F12` passe d'un côté à l'autre. La largeur de la séparation est mémorisée. Le clic droit sur un onglet propose aussi **Déplacer** vers l'autre côté. En revenant à un seul côté, tous les terminaux restent ouverts.
- Le terminal démarre dans le dossier du panneau actif.
- Sur un serveur SFTP ou SCP, il ouvre un shell sur ce serveur, avec les mêmes identifiants, le même rebond et le même contrôle du serveur que le panneau.
- Tire la séparation au-dessus du terminal pour changer sa hauteur : TwinPanel s'en souvient.
- Au démarrage suivant, TwinPanel propose de rouvrir les terminaux de la dernière fois : **Rouvrir**, **Toujours rouvrir**, **Ne jamais rouvrir** ou **Pas cette fois** (modifiable dans Options → Démarrage). Chaque terminal repart de son dossier de départ, de son côté ; ceux d'un serveur se reconnectent. Le texte affiché et les programmes en cours ne sont pas gardés.

Quand le clavier est dans le terminal, les touches vont au programme qui y tourne : `F5`, `F8` ou `Ctrl+W` n'agissent pas sur les fichiers. Seuls les raccourcis du terminal restent actifs.

### Copier et coller

- `Ctrl+C` copie le texte sélectionné. Sans sélection, il interrompt le programme, comme dans tout terminal.
- `Ctrl+V` colle, toujours. `Ctrl+Maj+C`, `Ctrl+Maj+V` et `Maj+Inser` marchent aussi.
- Clic droit : copie la sélection, ou colle s'il n'y a rien de sélectionné.
- Double-clic : un mot. Triple-clic : une ligne.
- Si un programme utilise la souris, garde `Maj` enfoncée pour sélectionner du texte.
- Avant de coller plusieurs lignes dans un programme qui les exécuterait aussitôt, TwinPanel demande confirmation. Le texte collé perd ses caractères de contrôle, et un dernier retour à la ligne ne lance rien.

Quand le clavier est dans le terminal, toutes les touches vont au programme qui y tourne : `F5` ne copie pas de fichier. Seuls `F12` et `Alt+F4` restent à TwinPanel. Clique sur une liste pour revenir aux fichiers.

### Commandes enregistrées

Le bouton **Commandes** affiche ou cache un panneau à droite du terminal.

- **Ajouter** : donne un nom et écris la commande, telle qu'elle doit être envoyée.
- **Exécuter** lance la commande dans le terminal affiché. **Copier** la met dans le presse-papiers.
- **Demander avant de lancer** : une confirmation, utile pour `rm` ou `reboot`.
- **Seulement sur ce serveur** : la commande n'apparaît que sur ce serveur. Sinon, elle apparaît partout.

- **Secret (mot de passe)** : le texte de la commande est rangé dans le coffre chiffré, jamais dans les réglages, et il est caché à l'écran (case **Afficher** pour le voir en le tapant). Donne-lui un nom : c'est lui qui s'affiche. Si le coffre est verrouillé, TwinPanel demande le mot de passe maître avant d'envoyer la commande.

Sans la case Secret, les commandes sont enregistrées en clair dans tes réglages : pour un mot de passe, coche toujours **Secret**. Une commande secrète voyage avec la synchronisation (chiffrée), mais pas avec la sauvegarde des réglages : sur un autre PC, modifie-la pour retaper son texte.

### Remonter dans le texte

La molette ou `Maj+PgPréc` / `Maj+PgSuiv` remontent dans les 10 000 dernières lignes de chaque terminal. Ce texte reste en mémoire et n'est jamais écrit sur le disque. Il disparaît à la fermeture.

Si un serveur envoie du texte beaucoup plus vite que l'affichage, TwinPanel saute une partie du milieu et le signale par une ligne : il reste réactif et `Ctrl+C` agit tout de suite.

### Chercher dans le texte

`Ctrl+Maj+F` dans un terminal ouvre une barre de recherche, ou **Chercher dans le terminal** dans le menu du bouton Terminal. Le mot est cherché sans tenir compte des majuscules, dans tout le texte de ce terminal, et le résultat s'affiche sélectionné. `Entrée` remonte vers le plus ancien, `Maj+Entrée` redescend, `Échap` ferme la barre. `Ctrl+C` copie le résultat une fois la barre fermée.

### Réglages

**Options → Terminal** : le programme lancé (PowerShell par défaut sous Windows, votre shell habituel sous Linux, parmi ceux installés sur votre PC), la taille du texte, et une case pour désactiver complètement le terminal intégré.

La commande `Ctrl+Maj+T` reste disponible : elle ouvre le terminal de ton choix dans sa propre fenêtre.

## Personnaliser

Tout se règle dans ≡ → Outils → Options.

| Page | Contenu |
|---|---|
| Affichage | Langue, thème, police, colonnes, formats, miniatures, outils médias |
| Couleurs | Couleurs par type de fichier, règles par masque, icônes des disques |
| Comportement | Corbeille, confirmations, transferts en même temps, astuces |
| Démarrage | Onglets et terminaux à rouvrir, dossiers de départ |
| Raccourcis | Toutes les touches, avec recherche |
| Barre de boutons | Vos programmes, dossiers et commandes |
| Menu Démarrer | Votre menu personnel, avec sous-menus |
| Éditeurs | Éditeur par extension |
| Terminal | Terminal à ouvrir |
| Sauvegarde | Réglages, liste des connexions et onglets ouverts dans un seul fichier, au choix |
| Synchronisation | Configuration chiffrée partagée entre vos machines, contenu au choix |
| Sécurité | Mot de passe maître du coffre, code de secours, mot de passe maître oublié |

Paramètres des boutons et du menu : `%P` dossier actif, `%N` nom de l'élément, `%F` son chemin complet, `%T` dossier de l'autre panneau, `%S` éléments marqués.

## Questions fréquentes

### Comment changer la langue ?

Options → Affichage → Langue. TwinPanel propose de redémarrer. Au premier lancement, il suit la langue du système.

### Je ne retrouve pas une fonction.

Le bouton **≡** contient toutes les commandes. Options → Raccourcis permet aussi de chercher une commande par son nom, et ce guide a un champ de recherche.

### Comment annuler une suppression ?

`F8` envoie à la corbeille du système : restaurez le fichier depuis celle-ci. Sur un serveur ou dans une archive, il n'y a pas de corbeille. `Ctrl+Z` annule seulement le dernier renommage multiple.

### Où sont mes réglages ?

Dans `%APPDATA%\TwinPanel` sous Windows, dans `~/.config/TwinPanel` sous Linux. Une mise à jour n'y touche pas.

### Comment sauvegarder mes réglages ?

Options → Sauvegarde → **Sauvegarder…**. Une fenêtre demande ce que le fichier doit contenir : les réglages, la liste des connexions (jamais les mots de passe ni les clés), les onglets ouverts. Pour revenir en arrière, **Restaurer…** montre ce que contient le fichier : décochez ce que vous ne voulez pas reprendre, puis cliquez sur OK pour l'appliquer.

### Comment avoir la même configuration sur plusieurs machines ?

Options → Synchronisation : choisissez un dossier partagé entre vos machines (Nextcloud, OneDrive, Syncthing, clé USB…), puis **Envoyer**. Cochez ce qui doit partir : réglages communs, connexions avec leurs mots de passe et leurs clés, serveurs connus, onglets ouverts. Sur l'autre machine, Windows ou Linux, indiquez le même dossier, cliquez sur **Récupérer**, choisissez ce que vous reprenez, puis cliquez sur OK. Tout le fichier est chiffré avec une phrase secrète que TwinPanel ne garde pas.

### Comment passer sur un autre PC ?

Options → Sauvegarde crée un seul fichier. À chaque sauvegarde, TwinPanel demande ce qu'il doit contenir : les réglages, la liste des connexions, les onglets ouverts. En restaurant, vous choisissez de même ce que vous reprenez ; les onglets reçus remplacent ceux ouverts, avec les mêmes règles que la synchronisation. Les mots de passe et les clés n'y sont jamais : les mots de passe seront redemandés, et une connexion par clé est marquée « clé manquante » jusqu'à ce que vous ajoutiez sa clé (Gestionnaire de connexions, onglet Clés). Un résumé s'affiche à la fin de la restauration. Pour tout transporter d'un ordinateur à l'autre, utilisez plutôt Options → Synchronisation.

### Mes onglets ne reviennent pas sur l'autre machine.

À l'envoi (synchronisation) ou à la sauvegarde, cochez **Onglets ouverts**, puis cochez-les aussi en récupérant ou en restaurant. Ils s'ouvrent quand vous cliquez sur OK. Un onglet de serveur revient si sa connexion existe sur l'autre machine. Un onglet d'un dossier du PC ne revient que sur le même système, et si le dossier y existe : `D:\musique` ne peut pas s'ouvrir sous Linux. Le message de fin dit combien d'onglets ont été écartés.

### J'ai oublié la phrase secrète de la synchronisation.

Elle ne peut pas être retrouvée, et personne ne peut plus ouvrir le fichier. Sur une machine qui a encore vos connexions et vos mots de passe, cliquez sur **Envoyer** avec une nouvelle phrase : le fichier est remplacé. Utilisez ensuite cette nouvelle phrase sur les autres machines.

### Mes mots de passe sont-ils en sécurité ?

Ils sont chiffrés, jamais écrits en clair, et seul votre compte sur ce PC peut les lire. Sous Linux, la clé est gardée dans le trousseau du bureau ; sans trousseau, TwinPanel demande un mot de passe maître. Un mot de passe maître ajoute une deuxième protection.

### J'ai oublié mon mot de passe maître.

Cliquez sur **Mot de passe maître oublié ?** (fenêtre « Coffre verrouillé », ou Options → Sécurité).

- **Avec votre code de secours** : tapez-le (tirets, espaces et majuscules ne comptent pas), puis choisissez un nouveau mot de passe maître. Rien n'est perdu, et un nouveau code de secours vous est donné.
- **Sans code de secours** : personne ne peut relire le coffre. **Tout effacer et recommencer** vide le coffre : les mots de passe seront redemandés et les clés privées seront à réimporter. Vos connexions restent, et l'ancien coffre est gardé à côté (`secrets.dat.oublie-…`), au cas où le mot de passe vous reviendrait.

### J'ai modifié un fichier d'un serveur, mais le serveur n'a pas changé.

Le fichier s'ouvre depuis une copie sur le PC. Enregistrez-le dans votre programme, puis répondez **Renvoyer** à la question de TwinPanel. Sans renvoi, la modification reste sur le PC.

### Comment modifier une archive qui est sur un serveur ?

Ouvrez-la avec `Entrée`, comme une archive du PC : TwinPanel en télécharge une copie. Chaque modification est faite sur la copie, puis renvoyée au serveur. Si l'archive a changé sur le serveur entre-temps, rien n'est remplacé et TwinPanel vous prévient.

### Pourquoi je ne peux pas modifier cette archive ?

rar, gz, bz2 et xz sont en lecture seule, comme les 7z chiffrés. Extrayez le contenu, puis créez une nouvelle archive avec `Alt+F5`.

### F4 n'ouvre pas le bon éditeur.

Choisissez-le dans ≡ → Outils → Éditeur et visionneuse, ou par extension dans Options → Éditeurs. Si l'éditeur choisi n'existe plus, TwinPanel le signale et utilise l'éditeur par défaut.

### Que change la version Linux ?

Elle est en bêta et fait le même travail. Elle a été essayée sur Debian 13 avec KDE, et sur Ubuntu 26.04 et Fedora 45 avec GNOME. Les vraies icônes viennent du thème du bureau (Options → Affichage), le terminal lance votre shell habituel, et une commande tapée dans la ligne du bas s'ouvre dans un nouvel onglet du terminal intégré, où l'on voit son résultat (ou dans le terminal du bureau si le terminal intégré est désactivé). Quelques différences :

- **Glisser-déposer** : en session Wayland (toujours le cas sous GNOME), on ne peut pas lâcher un fichier de TwinPanel dans une autre application ; l'inverse marche.
- **Partager** : par courriel, ou vers un appareil relié par KDE Connect.
- **Mots de passe** : gardés grâce au trousseau du bureau ; sans trousseau, TwinPanel demande un mot de passe maître.
- **Fedora 45** : l'AppImage a besoin de `openssl3-libs` (`sudo dnf install openssl3-libs`) ; le paquet `.rpm` l'installe de lui-même.
- **Ubuntu** : TwinPanel n'apparaît pas dans le Centre d'applications ; on le désinstalle avec `sudo apt remove twinpanel`.

### Comment envoyer un fichier à quelqu'un ?

Clic droit → **Partager…** sur le fichier, ou sur les fichiers marqués. Sous Windows s'ouvre la fenêtre de partage de Windows ; sous Linux, le courriel ou KDE Connect. Un fichier d'un serveur ou d'une archive est d'abord copié sur le PC.

### Mon terminal sur un serveur a perdu la connexion.

Clic droit sur son onglet → **Reconnecter** : le terminal repart dans le même onglet. Au démarrage suivant, TwinPanel propose aussi de rouvrir les terminaux de la dernière fois (Options → Démarrage).

### Comment afficher les fichiers cachés ?

`Ctrl+H`, ou le bouton de la barre d'outils.

### Pourquoi les copies se font-elles l'une après l'autre ?

C'est le réglage par défaut. Augmentez « Transferts en même temps » dans Options → Comportement.

### TwinPanel se connecte-t-il à Internet ?

Seulement pour ce que vous lui demandez (vos connexions à des serveurs), et en ouvrant « À propos » : TwinPanel y lit sur GitHub le total des téléchargements de TwinPanel pour votre système (Windows ou Linux, toutes versions). Rien n'est envoyé : ni réglages, ni fichiers, ni identifiant. Sans Internet, la ligne indique simplement que le nombre est indisponible.

### Qu'est-ce qui a changé dans cette version ?

Cliquez sur le logo en haut à gauche (À propos), puis sur **Nouveautés de cette version** : corrections de bug et nouvelles fonctionnalités de la version installée.

### Comment demander une fonction ou signaler un problème ?

Ouvrez une « Issue » sur la page GitHub de TwinPanel. Décrivez ce que vous avez fait et ce que vous attendiez. Rien n'est envoyé automatiquement depuis le programme.

### Puis-je l'utiliser au travail ?

Oui, gratuitement, aussi en entreprise. Il est seulement interdit de le vendre, le louer ou l'intégrer dans une offre payante. Voir la licence.
<!-- /guide -->

## Compiler

Il faut le SDK .NET 10.

- `build.bat` : compile tout, lance les tests, écrit le résultat dans `build.log`.
- `run.bat` : lance l'application.
- Ou : `dotnet run --project src/TwinPanel.App`

| Projet | Rôle |
|---|---|
| `src/TwinPanel.Core` | Logique sans interface, testable |
| `src/TwinPanel.App` | Interface Avalonia |
| `tests/TwinPanel.Core.Tests` | Tests xUnit |

Composants utilisés et plan du code : `COMPOSANTS.md`.

## Licence

![Licence TwinPanel](logo/licence/licence-twinpanel.png)

TwinPanel est publié sous la **Licence TwinPanel 1.1** — © 2026 Georges Christophe (chrypox). Le texte complet, seul à faire foi, est dans le fichier [`LICENSE`](LICENSE). En résumé :

- **Gratuit** : utilisation libre et gratuite, aussi en entreprise et en interne ; interdit de le vendre, le louer ou l'intégrer dans une offre payante.
- **Partage** : seulement la version d'origine, complète, gratuitement et avec la licence.
- **Sans modification diffusée** : chacun peut modifier le code pour son propre usage, mais ne peut pas diffuser de version modifiée ni réutiliser le code dans un autre projet.
- **Plugins** : autorisés s'ils sont gratuits et ne contiennent pas le code de TwinPanel.
- **Corrections** : proposées par une « Issue » (ticket) ; l'auteur les intègre lui-même, en créditant leur auteur.
- **Droits d'autrui** : un élément qui ne respecterait pas une licence ou vos droits ? Écrivez à admin@chrypox.be.

Ce n'est pas une licence Creative Commons. Les bibliothèques d'autres développeurs livrées avec TwinPanel restent sous leur propre licence : voir [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) (et, dans le programme, **À propos → Bibliothèques utilisées et leurs licences**).
