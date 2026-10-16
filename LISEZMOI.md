# Hublot

Des modules visuels pour Windows, au plus près du **Fluent Design** de Windows 11 — un genre de
« PowerToys visuel » : léger, fluide, et chaque module s'active ou se désactive dans ses **paramètres**.

| Module | Ce qu'il affiche |
|---|---|
| **Appareils sans fil** | Nom, état et batterie des appareils Bluetooth, des récepteurs USB Logitech et des manettes Xbox, à chaque connexion / déconnexion ; alertes de batterie. |
| **Lecture en cours** | Pochette, titre, artiste et application à chaque nouveau morceau, avec précédent, lecture / pause et suivant. |
| **Périphériques USB** | Nom et état de chaque périphérique USB branché / débranché ; pour les clés, disques et cartes mémoire, espace utilisé et restant avec une jauge. |
| **Écrans** | Nom réel, connectique (HDMI, DisplayPort, VGA, USB-C…) et résolution d'un écran branché ou débranché. |
| **Alimentation** | Nom du PC et charge de la batterie quand le PC portable est branché sur secteur ou débranché (en charge, autonomie restante). |

## Installation

Double-cliquez sur **`Hublot-Setup.exe`** puis sur *Installer* : installation pour votre compte, sans
droits administrateur. Relancer le même fichier met à jour Hublot ; une installation de WinTooth
(nom précédent) est remplacée et ses réglages repris. Désinstallation : *Paramètres Windows ›
Applications › Applications installées › Hublot*.

Depuis les sources : `.\build.ps1` (SDK .NET 10) → `dist\Hublot-Setup.exe`, copié aussi dans
`C:\Projets Claude\MAJ Projet Hublot\` avec `version.txt`.

## Utilisation

L'icône de Hublot (un hublot bleu partagé en quatre par une croisée) vit dans la zone de notification :

- **survol** (un court instant, un simple passage de souris n'ouvre rien) : une carte au-dessus de
  l'icône réunit la lecture en cours (avec lecture / pause et suivant) et les appareils connectés avec
  leur batterie (option : aussi les trois derniers déconnectés) ; elle reste ouverte tant que la
  souris est dessus ;
- **clic** ou **Ctrl + Alt + B** (modifiable) : une carte par élément, empilées en coin ou côte à côte au centre ;
- **clic droit** : *Appareils et lecture en cours*, *Écrans connectés* (s'il y a plus d'un écran), *Paramètres…*, *Quitter*.

## Lecture en cours : ce qu'il faut savoir

- Les médias pris en charge sont ceux **déclarés à Windows** (les mêmes que les touches multimédias du
  clavier) : Spotify, navigateurs (YouTube, YouTube Music, Deezer…), Lecteur multimédia, VLC…
  Une application qui joue du son sans se déclarer (certains jeux, Discord) n'apparaît pas.
- Un popup par **nouveau titre en lecture**, une fois les informations stables (pas de doublon, pas de
  popup pour les aperçus au survol des miniatures YouTube). Pas de popup pour une pause, ni (option)
  quand l'application qui joue est au premier plan.
- **Plateformes musicales uniquement** (activé par défaut) : seules les grandes plateformes déclenchent
  la popup et le mini-lecteur — Spotify, Deezer, Apple Music, iTunes, Amazon Music, YouTube Music, TIDAL,
  Qobuz, SoundCloud, Pandora, Napster — en application ou dans le navigateur. Windows ne donne pas
  l'adresse du site joué dans un navigateur ; Hublot y retient les onglets **avec album** : YouTube Music,
  Spotify, Deezer ou Apple Music indiquent l'album, YouTube ou Netflix non. Les vidéos, les autres
  applications (WhatsApp, Teams, VLC, Lecteur multimédia…) et les radios web sont donc écartées.
- Chaque application a un mode dans les paramètres : *Par défaut* (suit « Plateformes musicales
  uniquement »), *Tout afficher* (radio web dans Chrome, VLC… — mais aussi ses vidéos) ou *Masquer*.
  Windows identifie le navigateur, pas l'onglet : le choix vaut pour tous les sites de ce navigateur.
  Microsoft Teams (appels, réunions) est masqué par défaut.
- Options : popup aussi à la reprise, aussi à la mise en pause ; **progression du titre** (temps écoulé,
  barre, durée — si l'application la communique) ; **mini-lecteur permanent** (désactivé par défaut) :
  une carte de lecture reste affichée à son propre emplacement (en bas à droite par défaut) tant qu'un
  titre est en cours ou en pause, effacée en fondu pendant un plein écran ; un clic dessus la ferme jusqu'au titre suivant,
  ou jusqu'au prochain appui sur son raccourci (option « Le clic masque jusqu'au raccourci »).
  On le **déplace à la souris** : il suit le pointeur, y compris vers un autre écran, puis se cale sur
  l'emplacement le plus proche (coins, haut ou bas au centre) ; l'emplacement et l'écran sont retenus,
  comme s'ils avaient été choisis dans les paramètres.
  Un **raccourci clavier** (désactivé par défaut, Ctrl + Alt + P) le masque ou le réaffiche — y compris
  à la demande quand l'option permanente est désactivée.
- **Progression cliquable** (avec la progression) : un clic sur la barre avance ou recule dans le titre,
  si l'application l'accepte ; au survol, la barre s'épaissit et indique le temps visé.
- **Icône de la source cliquable** (option) : un clic sur l'icône de l'application, en haut à droite de
  la carte, ouvre l'application (sa fenêtre passe au premier plan ; pour un navigateur, sa dernière fenêtre) ;
  un second clic, l'application étant au premier plan, la réduit.
- **Couleurs de la pochette** (activé par défaut) : la carte prend une teinte tirée de la pochette, et le
  bouton lecture / pause et la progression sa couleur dominante, à la manière de Material You. Une pochette
  en noir et blanc garde les couleurs de Windows.
- Les notifications de Windows (Teams, Messages, Outlook…) s'affichent toujours **au-dessus** des
  popups de Hublot : Windows les place dans une couche réservée au système. Elles apparaissent en bas à
  droite, comme le mini-lecteur par défaut, et le recouvrent le temps de leur affichage. Pour éviter
  ce chevauchement, placez le mini-lecteur dans un autre coin (réglage « Afficher la lecture en permanence »).
- « Pas de popup si l'application est au premier plan » ne concerne jamais un onglet de navigateur
  (Windows ne dit pas quel onglet joue) : la popup s'affiche devant Chrome, sauf en plein écran.

## Périphériques USB : ce qu'il faut savoir

- Le nom affiché est celui que le périphérique annonce (« HD Pro Webcam C920 »), ou pour un stockage
- « Retirer le périphérique en toute sécurité » : pas de popup (option, activée), Windows affiche déjà
  la sienne. Par défaut, aucune popup en plein écran ; USB et Alimentation peuvent passer par-dessus (option).
  celui du volume, comme dans l'Explorateur : « KINGSTON (E:) ».
- L'espace s'affiche dès que Windows a monté le volume (une à deux secondes après le branchement) ;
  la jauge passe à l'orange au-delà de 90 % d'occupation, au rouge au-delà de 97 %.
- Les concentrateurs (hubs), les périphériques déjà branchés au lancement et les ré-énumérations
  éclair (moins de 2 s) ne déclenchent pas de popup. Option « uniquement les stockages ».

## Alimentation : ce qu'il faut savoir

- Uniquement sur un PC portable (avec batterie) ; sur un PC fixe, le module ne fait rien.
- Le nom affiché est celui de *Paramètres › Système › Informations système* (nom de l'appareil).
- Options : popup quand la charge atteint un niveau sur secteur (80 % par défaut, activé) et quand
  elle descend sous un seuil sur batterie (20 % par défaut, désactivé). Une alerte par franchissement.
- Un débranchement de moins d'une seconde (câble mal enfoncé) ne déclenche rien. L'autonomie
  restante s'affiche quand Windows l'a déjà estimée (souvent pas dans les premières secondes).

## Écrans, démarrage, claviers en veille

- **Écran des popups** (Apparence) et **écran du mini-lecteur** (Lecture en cours) : écran principal,
  écran de la souris (popups), ou un écran précis. Le bouton *Identifier* affiche le numéro de chaque
  écran. Un écran choisi puis débranché : retour à l'écran principal.
- **Module Écrans** : popup au branchement / débranchement d'un écran. Jamais au lancement de Windows ou
  de Hublot, ni quand un écran se rallume (veille des écrans, sortie de veille du PC).
- **Aucun popup au lancement** : quel que soit le module, rien ne s'affiche pendant la minute qui suit le
  démarrage de Windows (10 s si Hublot est lancé à la main).
- **Claviers et souris en veille** (MX Keys, MX Master…) : ils se déconnectent après quelques minutes
  sans être touchés et se reconnectent au premier appui. Ces déconnexions et reconnexions (dans les
  30 min) n'affichent rien ; option dans *Notifications d'appareils*.

## Paramètres

Les mises à jour conservent tous les réglages : ils sont rangés dans `%LocalAppData%\Hublot\`, hors du
dossier d'installation, et l'installateur n'y touche pas (seule la désinstallation les supprime).

### Changer de PC

1. Sur l'ancien PC : *Paramètres › À propos › Ouvrir le dossier des paramètres*, puis copier
   **`settings.ini`** (clé USB, OneDrive…). Ce fichier contient tous les réglages : apparence,
   emplacements, options de chaque module, raccourcis, applications exclues ou « Tout afficher ».
2. Sur le nouveau PC : installer Hublot, puis le quitter (clic droit sur l'icône › *Quitter*).
3. Ouvrir le même dossier (ou taper `%LocalAppData%\Hublot` dans l'Explorateur), y coller `settings.ini`
   en remplaçant l'existant, puis relancer Hublot depuis le menu Démarrer.

Les autres fichiers du dossier ne sont pas à copier : `devices.tsv` (appareils vus, dont la sourdine
dépend d'identifiants propres à chaque PC) et `media-apps.tsv` se reconstituent tout seuls.

Quand un module est désactivé, ses réglages sont grisés et inactifs, avec un bandeau qui le rappelle.

*Accueil* (modules), *Général* (démarrage, durée, clic, raccourci, survol, plein écran), *Apparence*
(emplacement, Acrylique / Mica / Uni, thème, taille, animation ; par défaut : en bas au centre,
Acrylique teinté à 50 %), puis une page par module.
Mises à jour : Hublot vérifie chaque jour s'une nouvelle version est publiée sur GitHub et la propose
(*Paramètres › À propos*). Chaque réglage s'applique immédiatement. Langue : celle de Windows (français ou anglais), ou forcée dans
*Général › Langue*. Fichiers : `%LocalAppData%\Hublot\`. La fenêtre s'ouvre à la
taille de l'écran (portables) et, étroite, réduit son menu aux icônes.

## Consommation

Mesurée au repos sur Windows 11, les deux modules actifs : ~3 Mo de mémoire dans le Gestionnaire des
tâches, 0 % de CPU. Tout est événementiel ; un module désactivé ne démarre pas.

## Diagnostic

`Hublot.exe --log` écrit un journal dans `%LocalAppData%\Hublot\log.txt`.
