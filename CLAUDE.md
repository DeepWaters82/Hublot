# CLAUDE.md — Hublot

« PowerToys visuel » pour Windows : modules activables, Fluent Design, animations fluides.
Successeur de WinTooth / Bluetooth Popup (dossier `../BluetoothPopup/`, figé). Documentation
utilisateur : [`LISEZMOI.md`](LISEZMOI.md). Règle n° 1 : **le service doit rester le plus léger possible**.

| Projet | Exe | Techno | Rôle |
|---|---|---|---|
| `src/Hublot` | `Hublot.exe` | C# .NET 10, **Win32 pur**, trimmé | service résident : icône, popup, modules |
| `src/Hublot.Core` | `HublotCore.exe` | WPF .NET 10, thème Fluent | hub (modules, réglages) **et** installateur |

**« Core » n'apparaît que dans le nom du processus / de l'exe** (`HublotCore.exe`, projet `Hublot.Core`).
L'interface dit « Hublot » ou « Paramètres » : titre de fenêtre, menu de l'icône, pied du survol,
raccourci du menu Démarrer (`Hublot.lnk` ; l'ancien `Hublot Core.lnk` est supprimé à l'installation),
métadonnées de l'exe. L'instance déjà ouverte est retrouvée par processus, pas par titre.

Livrable : `dist\Hublot-Setup.exe` (= Hublot Core avec le service embarqué), recopié par `build.ps1`
dans `C:\Projets Claude\MAJ Projet Hublot\` avec `version.txt`. Distribution publique : GitHub Releases
(voir « Publication GitHub et mises à jour ») ; paquet Microsoft Store : `build-store.ps1`.

## Compiler

```powershell
.\build.ps1                                 # -> dist\Hublot-Setup.exe + dépôt dans MAJ Projet Hublot
dotnet build src\Hublot ; dotnet build src\Hublot.Core
Hublot.exe --snapshot <dossier>             # PNG des cartes de chaque module (clair / sombre)
Hublot.exe --snapshot <dossier> 1.15        # idem à une autre échelle (1.15 = 100 % DPI, taille Grande)
Hublot.exe --icons <fichier.png>            # planche du logo ; --write-ico src\Hublot\app.ico
Hublot.exe --log [--demo | --demo-list]     # journal %LocalAppData%\Hublot\log.txt
```

## Architecture du service

```
src/Hublot/
  Program.cs, App.cs   point d'entrée ; App = hôte (IHost) : icône, raccourci, popup, plein écran, modules
  Common/              Settings, DeviceStore, MediaStore (partagés avec Hublot Core), Log, Memory, Autostart
  Shell/               moteur partagé, sans connaissance des modules :
                       Popup (1..N cartes, placement, entrée/sortie), CardContent / HoverRow / HoverCard,
                       PopupSurface → LayeredSurface | BackdropSurface, Canvas (DIB + GDI+), Theme,
                       Easing, Glyphs, TrayIcon, TrayIconRenderer (logo), NativeWindow, MessageCard
  Modules/
    Module.cs          contrat Module + IHost
    Bluetooth/         BluetoothModule (règles), DeviceCard / DeviceRow, sources (PnP, Logitech HID++, Xbox)
    NowPlaying/        NowPlayingModule (sessions média), MediaCard / MediaRow, CoverLoader, MediaApps
    Usb/               UsbModule (périphériques + volumes), UsbCard / UsbRow, Volumes (API Win32)
    Power/             PowerModule (secteur / batterie), PowerCard, PowerState
    Screens/           DisplayModule (écrans branchés / débranchés), DisplayCard
  Interop/             P/Invoke (LibraryImport)
```

- **Un seul processus, un seul thread UI** (boucle `GetMessage`). Tous les modules sont intégrés ;
  `App.SyncModules` démarre / arrête selon `Settings.Module*` (un module arrêté ne coûte rien).
  Pas de processus par module, pas de DLL chargées à chaud : ce serait contraire à la légèreté
  (un runtime par processus, pas de trimming possible).
- **Contrat de carte** (`Shell/CardContent.cs`) : `Key` (remplacement sur place), `Width`/`Height`
  (DIP), `Begin`/`ContinueFrom` (animations internes), `Draw(CardContext)`, `IsAnimating`,
  `HitTest`/`Click` (parties cliquables ; `Click` renvoie « fermer ? »). Le Shell fait le reste.
- **Hôte** (`IHost`) : `Notify` (règle plein écran puis affichage ; remplace la carte d'événement
  visible si même taille), `Update` (même clé, sans rejouer), `Post` (vers le thread UI),
  `OpenCore`, `OpenUri`. Les règles propres à un module (sourdine, démarrage, doublons…) restent
  dans le module.
- Survol de l'icône : `HoverCard` réunit les `HoverRow` de tous les modules + pied « Hublot Core ».
  Parties : index de ligne × 100 + partie de la ligne.
  Ouverture `HoverDelayMs` (450 ms) après `NIN_POPUPOPEN`, annulée par `NIN_POPUPCLOSE` (passages de
  souris). Appareils déconnectés : option `HoverShowDisconnected` (désactivée par défaut).
- Hublot Core : taille d'ouverture bornée à la zone de travail de l'écran sous la souris
  (`Native.WorkAreaAtCursor`) ; sous 860 DIP de large, menu réduit aux icônes (`UpdateCompactLayout`).
- Carte média : lecture / pause dessinés en vectoriel (`MediaCard.DrawPlayPause`, triangle centré
  optiquement) — le glyphe Segoe « Play » n'est pas centré dans son cadratin. Pas d'animation de pochette.
- Cartes d'appareils (Bluetooth, USB, alimentation) : **aucune animation interne** (demande utilisateur) —
  ni rebond de tuile, ni jauge qui se remplit, ni pourcentage qui défile ; seule l'entrée / sortie de la carte.
- Messages de Hublot Core → service : `Hublot.SettingsChanged`, `Hublot.Preview`
  (wParam 0 = carte d'appareil, 1 = liste, 2 = lecture en cours, 3 = USB, 4 = alimentation), fenêtre `Hublot.Host`.

## Module « Lecture en cours »

- API : `GlobalSystemMediaTransportControlsSessionManager` (sessions déclarées à Windows) ;
  événements `SessionsChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`. Une session par
  AppUserModelId ; l'exclusion se fait par AUMID (pas par onglet de navigateur).
- **Plateformes musicales uniquement** (`MediaMusicOnly`, `MediaApps.NotMusicReason`, liste depuis 3.3.0) :
  écarte le type déclaré Vidéo / Image, les applications vidéo connues (nom ou AUMID, mot entier : « Plex »
  ≠ « Perplexity »), les onglets de navigateur sans `AlbumTitle`, et toute autre application qui n'est pas
  dans `MediaApps.MusicPlatforms` (Spotify, Deezer, Apple Music, iTunes, Amazon Music, YouTube Music,
  TIDAL, Qobuz, SoundCloud, Pandora, Napster ; WhatsApp, Teams, VLC, Lecteur multimédia → « Tout afficher »). Chromium déclare **tout** comme `Music` (vérifié) ; YouTube n'a pas
  d'album, YouTube Music si. Un onglet sans album ne « consomme » pas le titre (l'album peut arriver
  après). S'applique aux popups et aux listes (survol, raccourci). Exception par application :
  `MediaAlwaysShownApps` (« Tout afficher » dans Hublot Core, ex. radio web dans Chrome).
- Anti-spam : regroupement 900 ms par session, popup seulement pour un nouveau couple artiste + titre
  **en lecture** ; état présent au lancement = référence (pas de popup) ; options reprise et pause ; option
  « application au premier plan » (`MediaApps.IsForeground` : processus de la fenêtre active).
- Pochette : `BitmapDecoder` → 192 px carré recadré (BGRA prémultiplié) → `Canvas.DrawImage`.
- Teams est exclu par défaut (il déclare appels et réunions comme médias).
- **Chromium (Brave, Chrome, Edge) recrée sa session** (même AUMID) en cours de route, p. ex. après
  une pause : `Sync` se rebranche à chaque `SessionsChanged` sur les objets session à jour, et chaque
  ouverture de la liste / du survol resynchronise. Rester sur l'ancien objet = plus aucun événement.
- Nom et icône de la source : `shell:AppsFolder\<AUMID>` (`Interop/ShellItems.cs`, COM par vtable) ;
  couvre les applications web installées (« Brave._crx_… » → « YouTube Music »). L'icône est
  affichée en haut à droite de la carte, à la place du nom.
- Test sans son : session `Windows.Media.Playback.MediaPlayer` silencieuse depuis PowerShell
  (WAV muet + `MediaPlaybackItem.ApplyDisplayProperties`).

## Module « Périphériques USB »

- Deux DeviceWatcher : interfaces `GUID_DEVINTERFACE_USB_DEVICE` {A5DCBF10-…} (une par périphérique
  physique, pas par fonction) et `GUID_DEVINTERFACE_VOLUME` {53F5630D-…}. Volume ↔ périphérique par
  **ContainerId** (pas d'appariement à l'heure). Lettres : `GetVolumeNameForVolumeMountPoint(interface + "\")`
  puis `GetVolumePathNamesForVolumeName` (réessais : le volume apparaît avant d'être monté).
- Nom : `DEVPKEY_Device_BusReportedDeviceDesc` ; type : classes USB des identifiants compatibles,
  du périphérique et, s'il est composite (« DevClass_00 »), de toutes les fonctions du conteneur.
  Motif `(?<![A-Za-z])Class_XX` : « SubClass_01 » et « DevClass_00 » ne sont pas des classes.
  Classe 09 = concentrateur → ignoré.
- Périphériques présents au lancement : aucune requête (sobriété mémoire, ~0,3 Mo pour le module).
- Déconnexion différée de 2 s (annulée si le périphérique revient). Veille : même option que le Bluetooth.
- **Non testé avec une vraie clé** au moment de l'écriture (aucune n'était branchée) : vérifier avec `--log`.

## Lecteur, progression, premier plan (module « Lecture en cours »)

- **Un seul lecteur** (3.6.0) : second `Popup` créé à la demande par `App` (`PinnedPopup()`),
  `PopupMode.Pinned`, `PositionOverride` = `MediaPinnedPosition`, `MonitorOverride` = `MediaPinnedMonitor(Device)`.
  - **Temporaire** (`IHost.ShowPlayer`, règles de `Notify` : lancement, plein écran) : `Popup.ShowPinned(card,
    temporary: true)` → fermeture automatique comme une carte d'événement (`RestartDismiss`, survol). C'est la
    « popup musicale » : plus de carte musicale dans le popup principal (sauf liste, survol, et l'aperçu quand
    le permanent est affiché).
  - **Permanent** (`IHost.Pin` / `IsPinned` = `_pinnedWanted`) : `MediaPinned` **et** raccourci actif
    (`NowPlayingModule.Permanent`). `MediaPinned` n'a plus d'option dans Hublot Core : le raccourci
    (`TogglePinned`) le bascule et le service l'enregistre (`SetPermanent` → `Settings.Save`). Un temporaire
    affiché devient permanent sur place. Fermé par clic (`Hidden` avec `_pinnedWanted` non nul) → plus demandé
    jusqu'au prochain titre / reprise. Le module choisit la session (qui joue, de préférence la dernière
    lancée) ; tant qu'il est demandé, `ShowPlayer` ne fait rien (c'est lui qui change).
  - Emplacement unique, changé **seulement à la souris** (plus de listes dans Hublot Core). Même emplacement
    que les popups : `ReservedDip` les empile au-dessus. Hublot Core relit `MediaPinned*` sur le disque avant
    chaque enregistrement (`SettingsViewModel.SyncServiceOwned`) : réglages écrits par le service.
  - Migration (`Settings.MigratePlayer`, `MediaPlayerVersion` 0 → 1) : sans mini-lecteur, l'emplacement du
    lecteur = celui des popups (là où s'affichaient les popups musicaux) ; mini-lecteur sans raccourci →
    raccourci réactivé.
- **Plein écran** : `TrayIcon.WatchFullscreen` déclare une barre d'application (ABM_NEW, sans position)
  pour recevoir `ABN_FULLSCREENAPP` — aucun sondage ; seulement quand une carte épinglée existe. La carte
  sort en fondu (`App.FadePinned` → `Popup.Dismiss`, ~220 ms) ; `_pinnedFading` distingue cette sortie
  d'une fermeture par clic (la carte reste demandée et revient à la sortie du plein écran).
- **Notifications Windows (toasts)** : affichées par le shell dans une couche au-dessus de toutes les
  fenêtres « toujours visibles » — une popup Hublot ne peut pas passer devant. Elles ne sont pas
  énumérables par `EnumWindows` (test du 13/10/2026) ; les détecter demanderait l'accès aux notifications
  (`UserNotificationListener`, réservé aux applications empaquetées). Conseil : mini-lecteur ailleurs
  qu'en bas à droite si les notifications le recouvrent.
- **Progression** : `GetTimelineProperties` (position, durée, horodatage) → `MediaTimeline`, position
  extrapolée en lecture ; `TimelinePropertiesChanged` lu seulement avec l'option. Redessin 1 fois / s
  (`_tick`) tant qu'une carte affichée joue avec une progression, sinon aucun minuteur.
- **Progression cliquable** (`MediaSeekable`) : parties 1000 à 2000 de `MediaCard` = position en
  millièmes (le survol change de partie à chaque déplacement → redessin, pastille + temps visé).
  `TryChangePlaybackPositionAsync((Start + position).Ticks)` ; la carte saute tout de suite à la position
  visée (certaines applications ne republient pas leur position). Vérifié avec une session `MediaPlayer`.
- **Couleurs de la pochette** (`MediaCoverColors`, activé par défaut, 3.2.0) : `CoverColors.Seed` = teinte
  dominante (tranches de 10°, pixels pondérés par saturation² × valeur ; < 5 % de pixels colorés → 0, thème
  normal), calculée paresseusement (`Cover.Seed`). `CoverColors.For(seed, dark)` → teinte de fond HSL
  (L 0,24 / 0,86, alpha 0,6, posée sur le matériau en retrait du contour) + accentuation (L 0,74 / 0,38) qui
  remplace `Theme.Accent` pour la carte (bouton, progression) ; piste de la progression = accentuation à 32 % (3.2.1). Fondu avec la pochette (`CoverMs`).
  Planche : `--snapshot` → `couleurs-pochette.png`.
- **Icône de la source cliquable** (`MediaOpenAppOnClick`, désactivé, 3.4.0) : partie 4 de `MediaCard`
  (zone de 32 DIP autour de l'icône, survol = `SubtleFill`), la carte reste affichée.
  `MediaApps.Activate` : fenêtres de premier niveau dans l'ordre Z (`FindWindowExW`), visibles, sans
  propriétaire, non masquées par DWM (`DWMWA_CLOAKED`), qui appartiennent à l'application (`Owns` : même
  AUMID déclaré, sinon même processus ; jamais entre application web `_crx_` et navigateur) → restaurée si
  réduite + `SetForegroundWindow` (permis : Hublot vient de recevoir le clic). Sinon, si l'AUMID est dans
  `shell:AppsFolder` et que ce n'est pas un navigateur : `explorer.exe shell:AppsFolder\<AUMID>`.
  Premier plan déjà à l'application (second clic ; la popup ne prend jamais le focus) → `SW_MINIMIZE`.
- **Mini-lecteur déplaçable** (3.5.0) : `PopupSurface` capture la souris entre `WM_LBUTTONDOWN` et
  `WM_LBUTTONUP` (`Press` / `Release` ; `Cancel` si la capture est perdue ; bouton droit = `Click`). `Popup`
  (`Draggable`, carte épinglée seulement) : au-delà de 6 DIP la carte suit la souris (`_dragX/_dragY` ajoutés
  au décalage de rendu, curseur lu par `GetCursorPos`) ; en deçà, c'est un clic. Lâchée : centre de la carte →
  écran (`MonitorAt`) et emplacement (tiers de la zone de travail en largeur, moitié en hauteur) →
  `Dropped` → `App.OnPinnedDropped` enregistre `MediaPinnedPosition` + écran (principal, sinon `Specific` =
  `DisplayInfo` de même origine) dans settings.ini, puis `Popup.Settle` : nouvelle `Placement` (`Prepare`,
  échelle de l'écran visé) et calage animé de 300 ms (decelerate) depuis le point de lâcher. Vaut pour le
  lecteur temporaire comme permanent (même `Popup`).
- **Clic = masqué jusqu'au raccourci** (`MediaPinnedClickUntilHotkey`, désactivé, 3.4.0, exige le
  raccourci) : `App`, événement `Hidden` du mini-lecteur fermé par un clic (`_pinnedWanted` non nul ;
  `Pin(null)` l'a déjà vidé) → `NowPlayingModule.HideUntilHotkey` (`SetPermanent(false)`, comme le raccourci). Changement d'option → `Restyle` du mini-lecteur.
- **Raccourci du mini-lecteur** : second `RegisterHotKey` (`TrayIcon.PinnedHotkey`) →
  `NowPlayingModule.TogglePinned` : bascule le lecteur permanent, retenu dans `MediaPinned`.
- **Bouton lecture / pause** : morphing (`MediaCard.DrawPlayPause(shape 0→1)`, 260 ms) — chaque moitié du
  triangle devient une barre ; l'animation se poursuit d'une carte à la suivante (`ContinueFrom`).
  Au clic, l'état visé est maintenu côté module (`Tracker.PendingPlaying`, 3 s au plus) jusqu'à
  confirmation : sans ça, un événement encore porteur de l'ancien état faisait revenir le bouton en
  arrière puis rebasculer. La progression est figée / relancée à la position affichée au moment du clic.
  Planche : `Hublot.exe --snapshot` → `lecture-pause.png`.
- **Changement de titre sans fermeture** : la carte a une hauteur fixe dès que l'option progression est
  active (`progressRow`, même sans durée connue) — sinon, la progression absente le temps du changement
  de titre changeait la taille et recréait la carte. Le mini-lecteur ne part que 3 s après qu'il n'y a plus
  rien à montrer (`_unpin`) : un navigateur recrée sa session entre deux titres. Après suivant / précédent
  depuis une carte, regroupement des événements ramené à 350 ms pendant 4 s (`SkipUntil`).
- **Transitions de `MediaCard`** (poursuivies d'une instance à l'autre par `ContinueFrom`) : texte (l'ancien
  monte et s'efface, le nouveau arrive par le bas, 420 ms), pochette (fondu enchaîné, 340 ms,
  `Canvas.DrawImage(opacity)` par matrice de couleur), appui sur un bouton (−12 % puis retour + éclat
  d'accentuation, 320 ms). Pellicules : `--snapshot` → `transition-titre.png`, `lecture-pause.png`.
- **Pochettes** : le navigateur envoie parfois un logo (site, navigateur) le temps que la vraie pochette
  arrive → `CoverLoader` écarte les images de moins de 72 px et celles à fond transparent (> 4 % de
  pixels transparents ; une pochette est opaque). Vignettes réelles : 120 × 120 (YouTube Music), 150 × 83
  (YouTube). Nouveau titre sans sa pochette : l'ancienne reste affichée jusqu'à 2,5 s (`CoverFor`), puis
  fondu direct vers la nouvelle — jamais de tuile ou de logo intermédiaire.
- **Alignement de `MediaCard`** : marge de 16 DIP à droite pour l'icône de l'application, la durée et le
  pictogramme « suivant » (on aligne l'encre, pas la zone cliquable : boutons 34 / 38 / 34 DIP, la zone de
  « suivant » déborde de 10 DIP dans la marge). Mesuré au pixel sur `--snapshot`.
- **Premier plan** (`MediaApps.IsForeground`) : onglet de navigateur → jamais ; sinon AUMID de la fenêtre
  au premier plan (`ShellItems.WindowAppId`, PKEY_AppUserModel_ID — « Brave._crx_… » pour une
  application web, « Chrome.UserData.Profile1 » pour Chrome) ; sinon processus (pas pour `_crx_`).
- Surfaces : `BringToTop` (HWND_TOPMOST) à chaque affichage — une fenêtre masquée garde sa place dans
  l'ordre Z. `CardContent.ShowOverFullscreen` : USB et Alimentation passent outre la règle plein écran
  si leur option est active (désactivées par défaut depuis 3.3.0 : aucun popup en plein écran).

## Module « Alimentation »

- `PowerSettingRegisterNotification` (powrprof) avec `DEVICE_NOTIFY_CALLBACK` : rappel sans fenêtre
  (`[UnmanagedCallersOnly]`, thread du système → `Host.Post`). Paramètres `GUID_ACDC_POWER_SOURCE`
  (carte) et `GUID_BATTERY_PERCENTAGE_REMAINING` (mise à jour de la carte affichée). Windows envoie
  l'état courant dès l'abonnement : c'est la référence. Relecture `GetSystemPowerStatus` 800 ms
  après le dernier changement (anti-rebond). Pas de batterie (`BatteryFlag & 128`) → aucun abonnement.
- Seuils (`OnPercentChanged`) : niveau de charge atteint en **montant sur secteur**, batterie faible en
  **descendant sur batterie** ; alerte au franchissement (pas de répétition). Motif = `PowerReason`,
  conservé par une mise à jour de la carte affichée (`ContinueFrom` sans rejouer).
- Nom : `GetComputerNameExW(ComputerNameDnsHostname)` (casse conservée). Interop : `Interop/Power.cs`
  (classe `PowerApi` : `Power` entrerait en conflit avec l'espace de noms du module).
- Testé : abonnement et rappel (état secteur reçu). **Non testé avec un vrai portable** (poste fixe).

## Module « Écrans » et choix de l'écran

- `Common/Displays.cs` (partagé avec Hublot Core) : `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo`
  (nom EDID, connectique `outputTechnology`, chemin du moniteur, nom GDI), lecture par décalages
  (`Marshal.Read*`, structures documentées : chemin 72 o, mode 64 o, nom de cible 420 o). Vérifié sur
  3 écrans (DisplayPort, DVI).
- Choix de l'écran : `Settings.Monitor = Specific` + `MonitorDevice` (= `DisplayInfo.Path`, stable) ;
  lecteur : `MediaPinnedMonitor` / `MediaPinnedMonitorDevice` (`Popup.MonitorOverride`, choisi à la souris). Placement :
  `MonitorFromPoint(centre de l'écran)`. Numéros = ceux de Windows (DISPLAYn) ; *Identifier* =
  `ScreenIdentifier` (fenêtres WPF positionnées en pixels physiques).
- `DisplayModule` : `WM_DISPLAYCHANGE` (fenêtre hôte de premier niveau → `Module.OnDisplayChanged`),
  comparaison des écrans actifs 2,5 s après le dernier message ; plusieurs changements à la fois
  (station d'accueil à 2 écrans, écran intégré éteint couvercle fermé) → `IHost.Notify(cartes)`, cartes
  empilées comme la liste. Aperçu = une carte par écran réellement connecté. Silence : écrans éteints
  (`GUID_CONSOLE_DISPLAY_STATE`, rappel sans fenêtre) + 20 s après rallumage, veille du PC + 30 s.
  **Non testé avec un vrai branchement** (on ne débranche pas les écrans du poste de dev).
- Menu de l'icône : *Écrans connectés* seulement si `Displays.Active().Count > 1` (lu à l'ouverture du menu) ;
  cartes des écrans et événements groupés : `ShowList(longer: false)` = durée normale (la liste
  d'appareils garde 3 s de plus).
- Applications média vues (`media-apps.tsv`) : oubliées après 30 jours sans lecture (`MediaStore.LoadApps`).
  Les essais de développement (session `MediaPlayer` silencieuse, `seektest`, `toasttest`) y laissent une
  entrée : la retirer du fichier service arrêté.
- Silence global au lancement : `App.Notify` ignore tout pendant 60 s (Hublot lancé < 10 min après le
  démarrage de Windows) ou 10 s.
- Claviers / souris (`QuietInputSleep`) : déconnexion → `_asleep[clé]`, pas de popup ; reconnexion dans
  les 30 min → pas de popup.
- Hublot Core : barre de sélection du menu = une seule `NavPill` sur un `Canvas` superposé, animée par
  `CompositionTarget.Rendering` (extrémité de tête puis de queue, 350 ms), comme NavigationView.

## Module « Prise audio » (3.7.0, mis de côté en 3.7.2)

**Désactivé** : retiré de `App._modules`, tuile, entrée de navigation et page masquées dans Hublot Core
(code, réglages et chaînes conservés). Test du 19/10/2026 sur un PC fixe Realtek (ALC623, prise combinée en
façade, prises arrière réaffectables) : brancher ou débrancher redémarre tout l'audio (sortie et micro Realtek
absents puis actifs, sorties HDMI NVIDIA qui clignotent pendant 1 à 4 s) ; sans rien à l'arrière, débrancher le
casque retire la sortie et le micro jusqu'au prochain branchement. La relecture des prises (IKsJackDescription,
`CoreAudio.Jacks`) a vu le débranchement en façade et le branchement / débranchement à l'arrière, mais pas le
rebranchement en façade (relecture faite alors que les points de terminaison étaient encore absents ?). Pour
reprendre : `Hublot.exe --audio-watch FICHIER SECONDES` fait tourner le module hors service (`Modules/WatchHost`)
et journalise relectures et popups ; `--audio FICHIER` liste points de terminaison et prises.


- `Interop/CoreAudio.cs` : MMDevice API par la table de méthodes ; objet `IMMNotificationClient` fait main
  (vtable de pointeurs `[UnmanagedCallersOnly]`, jamais libéré). `Subscribe` sur un thread du pool
  (`CoInitializeEx` multithread), `States()` = référence initiale, `Describe(id)` = description
  (PKEY_Device_DeviceDesc), adaptateur (PKEY_DeviceInterface_FriendlyName), sens (`IMMEndpoint`), chemin PnP
  de l'adaptateur (`{b3f8fa53-0004-438e-9003-51a46e139bfc},2` : « {1}.HDAUDIO… »).
- `JackModule` : seule la transition débranché (4) ↔ actif (1) d'un point de terminaison connu compte ; bus
  BTH / USB ignorés (un casque Bluetooth déconnecté est « débranché ») ; regroupement 700 ms (casque à micro =
  sortie + entrée → une carte, sortie de préférence) ; veille : 15 s de silence au réveil. `JackCard` (tuile
  casque ou micro). Réglages `ModuleAudio`, `AudioNotifyPlugged`, `AudioNotifyUnplugged` ; page `Audio`
  (aperçu 6). Diagnostic : `Hublot.exe --audio FICHIER` (états, sens, noms, bus). Limite : un point de
  terminaison « haut-parleurs / casque » unique ne change jamais d'état sur certains PC.

## Corrections 3.7.0 (appareils, dongles, paramètres)

- **Veille** : `QuietInputSleep` = claviers seulement ; `QuietMouseSleep` (désactivé) pour les souris. Avant,
  une souris éteinte / rallumée (Pebble) était prise pour une souris en veille : ni déconnexion ni reconnexion
  (30 min). Seule la première connexion après un nouvel appairage passait (type pas encore résolu).
- **Doublons** : un appareil Bluetooth appairé de nouveau change de conteneur, donc de clé.
  `DeviceRegistry.Supersede` (nouvelle clé `bt:` inconnue, entrée de même nom plus appairée) reprend
  l'ancienne entrée ; `PruneStale` au lancement (après `SeedRegistry`) ; la sourdine suit (`TransferMute`).
- **Dongles** : `UsbKind.Receiver` (VID 046D + PID de `LogitechSource.IsReceiver`, ou nom « receiver »,
  « dongle », « unifying »…). Récepteur Logitech avec la source active : carte USB différée de 3,5 s et
  abandonnée si une `DeviceCard` liée par dongle est affichée (la carte de l'appareil, avec nom et batterie).
- **Clic droit sur une carte** : `SurfaceInput.Click` (bouton droit) → `Popup.ContextMenuRequested` →
  `App.OnCardMenu` : *Paramètres de ce popup…* (`CardContent.SettingsPage`) et *Fermer* ; menu par
  `TrayIcon.ShowContextMenu`, fermeture automatique suspendue pendant le menu.
- **Désinstallation** : `UninstallDialog` (case « Conserver mes paramètres », cochée) → `Installer.Uninstall(keepSettings)`.
- **Hublot Core** : tuiles des modules et logo en `AccentFillColorDefaultBrush` / `TextOnAccentFillColorPrimaryBrush`
  (suivent l'accentuation et le thème), logo aussi en icône de fenêtre. Barre de titre : avec « couleur
  d'accentuation sur les barres de titre » (DWM `ColorPrevalence`), le thème Fluent de WPF fixe sa couleur à
  l'ouverture et ne la change plus ; `Native.ApplyCaptionColor` (DWMWA_CAPTION_COLOR / TEXT_COLOR = DWM
  `AccentColor`, texte noir ou blanc selon la luminance, sinon couleurs par défaut) au chargement et à chaque
  `WM_SETTINGCHANGE` / `WM_DWMCOLORIZATIONCOLORCHANGED`.
- **Icône de la zone de notification** : `TrayIconRenderer.TrayColors` = AccentPalette (Light1 → Accent sur barre
  des tâches sombre, Accent → Dark1 sur barre claire), traits blancs ou sombres selon la luminance ; redessinée
  à chaque `ImmersiveColorSet`. Exécutables et logos du Store : bleu Windows (`Draw(size)`).
- **Défauts** de « Lecture en cours » : tout activé sauf « aussi à la mise en pause » ; raccourci du lecteur
  permanent Ctrl + 0 du pavé numérique (0x60) en 3.7.0, puis Ctrl + Alt + P en 3.8.0 (Ctrl + 0, pavé ou non, est le
  zoom 100 % des navigateurs, et beaucoup de portables n'ont pas de pavé numérique).
- **Défauts 3.8.0** (revue UX) : USB stockages uniquement ; « niveau de charge atteint » (Alimentation) désactivé.
- **Manettes** (3.8.0) : `XboxSource` ne filtre plus `IsWireless` (une manette sur dongle 2,4 GHz se déclare filaire :
  GameSir VID 3537 « Contrôleur XBOX 360 pour Windows ») ; liaison `DeviceLink.Usb` (« · USB », pas de « batterie non
  communiquée ») ; avec beaucoup de dongles la manette existe dès le branchement du dongle. `Hublot.exe --gamepads FICHIER`.
  3.8.1 : batterie lue seulement pour une manette sans fil (une filaire renvoie un rapport « pleine ») et ignorée si
  `BatteryStatus.NotPresent`. Le module USB reconnaît les manettes (`UsbKind.Gamepad` : classes FF/5D Xbox 360,
  FF/47 Xbox One, ou fonction HID « HID_DEVICE_SYSTEM_GAME » / UP:0001_U:0004-0005, enfants lus aussi pour un
  périphérique HID) et n'affiche rien si la source Manettes est active (`GamepadsShownElsewhere`) : deux cartes à
  70 ms d'écart (« Controller (Generic) » et « Contrôleur XBOX 360 pour Windows ») se remplaçaient au hasard.
- **Journal** (3.8.0) : option `DiagnosticLog` (À propos) → `Log.Configure` au lancement et à chaque rechargement ;
  `--log` → `Log.Force` (prime sur l'option) ; 1 Mo au plus (`log.old.txt`).
- **Page GitHub** : `HublotCore.exe --screenshot PAGE FICHIER en` (fenêtre hors écran, réglages par défaut,
  applications d'exemple, rien n'est enregistré : `SettingsViewModel.Demo`) → `now-playing-settings.png` ;
  `Hublot.exe --snapshot DOSSIER 1.5 en` → `sombre-12.png` = `audio-jack.png` (et `album-colors.png`, planche
  de contrôle Windows / pochette). Section « Album art colors » du README : captures réelles du lecteur
  fournies par l'utilisateur (`album-colors-1.png` à `-6.png`).

## Langues (français / anglais)

- **Tous les textes** sont dans `src/Hublot/Common/Strings.cs` (partagé avec Hublot Core) : clé → (français,
  anglais). Code : `L.T("Cle")`, `L.F("Cle", valeurs…)` (`{0}`, `{1}`), `L.Percent`, `L.Size`, `L.Culture`.
  XAML : `Text="{local:Tr Cle}"` (`Hublot.Core/Tr.cs`). Clé absente → affichée telle quelle.
- Langue : réglage `Language` (Auto = langue d'affichage de Windows via `GetUserDefaultUILanguage`, le
  service étant en `InvariantGlobalization`) ; `Settings.Load` applique `L.Apply`. Le service bascule à la
  relecture des paramètres ; Hublot Core se rouvre (`App.Restart`, verrou d'instance libéré d'abord) ; la
  langue est appliquée avant `App.xaml` (constructeur statique d'`App`).
- Les clés des fenêtres ont été générées depuis le texte français (« LancerAuDemarrageDeWindows ») ; elles
  ne changent plus quand on retouche le texte.
- `Hublot.exe --snapshot <dossier> <échelle> en` : cartes en anglais.

## Paquet Microsoft Store (MSIX)

- `.\build-store.ps1` → `dist\Hublot-<version>.msix` (non signé : Partner Center signe). Contenu dans
  `obj\store\layout` : Hublot.exe (trimmé), HublotCore.exe **sans installateur embarqué**, Assets\ (générés
  par `Hublot.exe --store-assets`), AppxManifest.xml (modèle `store\AppxManifest.template.xml`, identité
  `store\identity.ini` = identité Partner Center : DeepWaterStudio.Hublot, éditeur CN=B8ABA198-59B1-43C9-8EAF-D0544874023D,
  « DeepWaterStudio » ; version = celle des exe + « .0 »). Fiche du Store : `store\listing.md` ; confidentialité : `PRIVACY.md`.
- Assemblage : `makeappx.exe` du SDK Windows ou du paquet NuGet `Microsoft.Windows.SDK.BuildTools`.
- Deux applications : « Hublot » (HublotCore.exe, menu Démarrer) et « HublotService » (Hublot.exe,
  `AppListEntry="none"`, tâche de démarrage `HublotStartup` activée par défaut). `runFullTrust` seulement.
- Comportements en paquet (`Common/AppPackage.cs`, `GetCurrentPackageFamilyName`) : démarrage avec Windows
  par `StartupTask` (pas de raccourci) ; ouvrir les paramètres lance le service s'il ne tourne pas ;
  « Ouvrir le dossier des paramètres » → `%LocalAppData%\Packages\<famille>\LocalCache\Local\Hublot`
  (écritures dans %LocalAppData% redirigées par MSIX).
- Essai local sans signature : mode développeur + `Add-AppxPackage -Register obj\store\layout\AppxManifest.xml`.
- Ne pas installer la version Store et la version classique en même temps (deux services, deux icônes).
- Testé le 14/10/2026 (paquet enregistré en mode développeur, identité d'essai) : lancement depuis le menu
  Démarrer → paramètres + service lancés depuis le paquet ; données dans LocalCache\Local\Hublot ; tâche
  de démarrage activée (State = 2). Pas encore passé au Windows App Certification Kit ni soumis.

## Publication GitHub et mises à jour

- Dépôt public, licence MIT (`LICENSE`), présentation `README.md` (anglais, captures dans `docs/screenshots`,
  générées par `--snapshot … 1.5 en`), `LISEZMOI.md` (français).
- Version : `<Version>` identique dans les deux .csproj, puis étiquette `vX.Y.Z` poussée →
  `.github/workflows/release.yml` (vérifie étiquette = version, `build.ps1 -Updates ''`, joint
  `Hublot-Setup.exe` à la Release).
- Mises à jour (version classique seulement) : `Hublot.Core/UpdateChecker.cs`, constante `Repository`
  (« OWNER/Hublot » tant que le dépôt n'existe pas = aucune vérification). API
  `api.github.com/repos/<dépôt>/releases/latest`, pièce jointe `Hublot-Setup.exe`, téléchargée dans %TEMP% puis
  lancée (l'installateur fait la mise à jour ; pas de marque « Internet » sur le fichier, donc pas de SmartScreen).
- Le service reste sans réseau : 2 min après le lancement puis chaque jour, il lance
  `HublotCore.exe --check-update` (sans fenêtre) ; une nouvelle version est notée dans `update.txt` (une seule
  alerte par version) et signalée par `Hublot.UpdateAvailable` (wParam = version) → popup cliquable → page
  À propos, qui relance la recherche et propose « Installer la mise à jour ». Option `CheckUpdates`.

## Pièges (hérités et nouveaux)

- Pas d'historique des appareils (retiré en 2.8.0 : ni page, ni fichier). `history.tsv` d'anciennes
  versions peut rester dans le dossier de données, il n'est plus lu ni écrit.
- Valeurs par défaut (`Settings.cs`) : ne valent que pour les clés absentes de `settings.ini` ; Hublot Core
  réécrit toutes les clés à chaque modification. `ModulePower` = présence d'une batterie (PC fixe : désactivé).
- `App.ReloadSettings` ne repose la carte épinglée que si son emplacement ou son apparence change
  (`PinnedLayoutChanged`) ; le module ne la fait réapparaître que lorsque l'option passe à « activé ».

- Hublot Core : le style explicite `ToggleSwitch` remplace le style Fluent implicite ; le libellé doit
  avoir un `Foreground` explicite (sinon noir en mode sombre). Options d'un module : `ModuleOptions`
  (IsEnabled lié au module), bandeau `ModuleOffNotice`, cartes d'accueil `ModuleDim` (Tag = module).
- USB « retiré en toute sécurité » : nœud encore présent avec le problème 47 (CM_PROB_HELD_FOR_EJECT,
  `Interop/PnP.cs`), testé 2 s après la disparition de l'interface. **Non testé avec une vraie clé.**

- **Bitdefender** : ne jamais écrire la clé `HKCU\…\Run` depuis le service (mis en quarantaine) ;
  démarrage automatique par raccourci du dossier Démarrage (Hublot Core / installateur).
- Matériaux Acrylique / Mica sur une fenêtre jamais active : `WM_NCACTIVATE(TRUE)` forcé.
- Glyphes batterie : Battery10 = U+E83F (U+E85A est BatteryCharging0).
- Les fichiers de `Common/` sont compilés dans les deux projets : aucune dépendance au reste du service.
- Scripts `.ps1` en UTF-8 **avec BOM**.
- Logo (`TrayIconRenderer`, `HublotLogo` dans App.xaml de Hublot Core, `app.ico`) : hublot à huit rivets,
  cerclage, la mer (vague) dans la moitié basse de la vitre et un reflet (3.7.1, choisi parmi des concepts ;
  avant : une croisée) ; sous 24 px, ni rivets ni reflet. **Sans** le logo Windows (marque de Microsoft).
  Les trois doivent rester identiques (grille de 24 : vitre 7,6, cerclage 1,4, rivets à 9,95, mer à +1,6).
  `app.ico` : `Hublot.exe --write-ico src\Hublot\app.ico` ; planche : `Hublot.exe --icons FICHIER.png`. Le nom
  « Hublot » est une marque d'horloger : à revoir avant toute diffusion publique.
- Icônes Segoe dans les tuiles : `Canvas.DrawIcon` (centrage sur le dessin du glyphe, pas sur son cadratin).
- Logitech et manettes Xbox : toujours **non testés sur matériel**.
