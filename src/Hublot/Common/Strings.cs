using System.Globalization;
using System.Runtime.InteropServices;

namespace Hublot.Common;

/// <summary>Langue de l'interface : celle de Windows (français si Windows est en français, anglais sinon), ou forcée.</summary>
public enum AppLanguage { Auto, French, English }

/// <summary>
/// Tous les textes de Hublot, en français et en anglais, partagés par le service et Hublot Core.
///
/// Pour modifier un texte : le changer ici, dans les deux langues (la clé ne change pas). Dans les fenêtres
/// de Hublot Core, un texte s'écrit <c>Text="{local:Tr Cle}"</c> ; dans le code, <c>L.T("Cle")</c>, ou
/// <c>L.F("Cle", valeur…)</c> pour un texte avec des valeurs <c>{0}</c>, <c>{1}</c>…
/// Une clé absente s'affiche telle quelle : facile à repérer.
/// </summary>
public static partial class L
{
    /// <summary>Interface en anglais (sinon français).</summary>
    public static bool English { get; private set; } = !WindowsIsFrench();

    /// <summary>Langue choisie dans les paramètres ; Auto = celle de Windows.</summary>
    public static void Apply(AppLanguage language) => English = language switch
    {
        AppLanguage.French => false,
        AppLanguage.English => true,
        _ => !WindowsIsFrench(),
    };

    /// <summary>Langue d'affichage de Windows (indépendante de la culture .NET, invariante dans le service).</summary>
    private static bool WindowsIsFrench()
    {
        try
        {
            return (GetUserDefaultUILanguage() & 0x3FF) == 0x0C; // LANG_FRENCH, toutes variantes (France, Belgique, Canada…)
        }
        catch (Exception)
        {
            return true;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    /// <summary>Culture des dates et nombres affichés.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(English ? "en-US" : "fr-FR");

    /// <summary>Texte d'une clé dans la langue active.</summary>
    public static string T(string key) => Texts.TryGetValue(key, out var t) ? (English ? t.En : t.Fr) : key;

    /// <summary>Texte avec valeurs : <c>L.F("Usb.Free", "19,6 Go")</c>.</summary>
    public static string F(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, T(key), values);

    /// <summary>« 85 % » (espace fine insécable) en français, « 85% » en anglais.</summary>
    public static string Percent(int value) => English ? $"{value}%" : $"{value}\u202F%";

    /// <summary>« 19,6 Go » / « 19.6 GB » : unités binaires, comme l'Explorateur (« Go » pour 2³⁰ octets).</summary>
    public static string Size(ulong bytes)
    {
        string[] units = English ? ["bytes", "KB", "MB", "GB", "TB"] : ["octets", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string text = value.ToString(value >= 100 || unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return (English ? text : text.Replace('.', ',')) + "\u00A0" + units[unit];
    }

    /// <summary>Clé → (français, anglais).</summary>
    private static readonly Dictionary<string, (string Fr, string En)> Texts = new()
    {
        // ── Menu de l'icône, carte du survol, messages ─────────────────────────────────────────
        ["Tray.Devices"] = ("Appareils et lecture en cours", "Devices and now playing"),
        ["Tray.Displays"] = ("Écrans connectés", "Connected displays"),
        ["Tray.Settings"] = ("Paramètres…", "Settings…"),
        ["Tray.Quit"] = ("Quitter", "Quit"),
        ["Hover.Settings"] = ("Paramètres", "Settings"),
        ["Message.Nothing"] = ("Rien à afficher", "Nothing to show"),
        ["Message.NothingList"] = ("Aucun appareil connecté, aucune lecture en cours", "No device connected, nothing playing"),
        ["Message.NothingHover"] = ("Aucun appareil, aucune lecture", "No device, nothing playing"),
        ["Message.NothingPlaying"] = ("Rien en lecture", "Nothing playing"),
        ["Message.PlayerNextTrack"] = ("Le lecteur permanent s'affichera au prochain titre", "The always-on player will appear with the next track"),

        // ── Appareils sans fil ────────────────────────────────────────────────────────────────
        ["Device.Connected"] = ("Connecté", "Connected"),
        ["Device.Disconnected"] = ("Déconnecté", "Disconnected"),
        ["Device.NewConnected"] = ("Nouvel appareil · Connecté", "New device · Connected"),
        ["Device.LowBattery"] = ("Batterie faible", "Low battery"),
        ["Device.Charging"] = ("En charge", "Charging"),
        ["Device.ChargeReached"] = ("Niveau de charge atteint", "Charge level reached"),
        ["Device.BatteryUnknown"] = ("Batterie non communiquée", "Battery not reported"),
        ["Device.Dongle"] = ("Récepteur USB", "USB receiver"),
        ["Device.ConnectedCharging"] = ("Connecté · En charge", "Connected · Charging"),
        ["Device.Logitech"] = ("Périphérique Logitech", "Logitech device"),
        ["Device.Controller"] = ("Manette sans fil", "Wireless controller"),
        ["Device.Seen"] = ("Vu {0}", "Seen {0}"),
        ["Device.WithBattery"] = ("{0} · Batterie {1}", "{0} · Battery {1}"),
        ["Preview.Headphones"] = ("Casque (aperçu)", "Headphones (preview)"),
        ["Preview.Mouse"] = ("Souris (aperçu)", "Mouse (preview)"),
        ["Preview.Keyboard"] = ("Clavier (aperçu)", "Keyboard (preview)"),
        ["Preview.Track"] = ("Titre du morceau (aperçu)", "Track title (preview)"),
        ["Preview.Artist"] = ("Artiste", "Artist"),

        // ── Périphériques USB ────────────────────────────────────────────────────────────────
        ["Usb.Device"] = ("Périphérique USB", "USB device"),
        ["Usb.Free"] = ("{0} libres", "{0} free"),
        ["Usb.FreeOf"] = ("{0} libres sur {1}", "{0} free of {1}"),
        ["Usb.Used"] = ("Connecté · {0} utilisés sur {1}", "Connected · {0} used of {1}"),
        ["Usb.Reading"] = ("Connecté · lecture du stockage…", "Connected · reading storage…"),
        ["Usb.NoVolume"] = ("Connecté · aucun volume accessible", "Connected · no accessible volume"),
        ["Usb.Connected"] = ("Connecté · USB", "Connected · USB"),
        ["CardMenu.Settings"] = ("Paramètres de ce popup…", "Settings for this popup…"),
        ["CardMenu.Close"] = ("Fermer", "Close"),
        ["Jack.Plugged"] = ("Branché", "Plugged in"),
        ["PriseAudio"] = ("Prise audio", "Audio jack"),
        ["ModulePriseAudio"] = ("Module Prise audio", "Audio jack module"),
        ["CasqueOuMicroSurLaPrise"] = ("Casque, écouteurs ou micro branché sur la prise jack.", "Headphones, earphones or microphone plugged into the jack."),
        ["BranchementDUnCasque"] = ("Branchement d'un casque ou d'un micro", "Headphones or microphone plugged in"),
        ["DebranchementDUnCasque"] = ("Débranchement", "Unplugged"),
        ["LaDetectionDependDuPilote"] = ("La détection dépend du pilote audio : certains PC ne signalent pas le branchement de la prise casque. Les casques Bluetooth et USB s'affichent avec leurs propres modules.", "Detection depends on the audio driver: some PCs do not report the headphone jack. Bluetooth and USB headsets appear through their own modules."),
        ["Jack.Unplugged"] = ("Débranché", "Unplugged"),
        ["Jack.WithMic"] = ("avec micro", "with mic"),
        ["Jack.Headphones"] = ("Casque", "Headphones"),
        ["Jack.Microphone"] = ("Microphone", "Microphone"),
        ["Usb.ConnectedReceiver"] = ("Connecté · récepteur sans fil", "Connected · wireless receiver"),

        // ── Alimentation ─────────────────────────────────────────────────────────────────────
        ["Power.LevelReached"] = ("Niveau de charge atteint", "Charge level reached"),
        ["Power.LowPlugged"] = ("Batterie faible · en charge", "Low battery · charging"),
        ["Power.LowUnplugged"] = ("Batterie faible · branchez le chargeur", "Low battery · plug in the charger"),
        ["Power.OnBatteryLeft"] = ("Sur batterie · environ {0} restantes", "On battery · about {0} left"),
        ["Power.OnBattery"] = ("Sur batterie", "On battery"),
        ["Power.Charging"] = ("Branché · en charge", "Plugged in · charging"),
        ["Power.Full"] = ("Branché · batterie pleine", "Plugged in · fully charged"),
        ["Power.Plugged"] = ("Branché", "Plugged in"),
        ["Power.Minutes"] = ("{0} min", "{0} min"),
        ["Power.Hours"] = ("{0} h {1:00}", "{0} h {1:00} min"),

        // ── Écrans ───────────────────────────────────────────────────────────────────────────
        ["Display.Connected"] = ("Connecté · {0} · {1}", "Connected · {0} · {1}"),
        ["Display.Disconnected"] = ("Déconnecté · {0}", "Disconnected · {0}"),
        ["Display.Internal"] = ("Écran intégré", "Built-in display"),
        ["Display.Generic"] = ("Écran", "Display"),
        ["Display.Builtin"] = ("Intégré", "Built-in"),
        ["Display.Wireless"] = ("Sans fil (Miracast)", "Wireless (Miracast)"),
        ["Display.Dock"] = ("USB-C / station", "USB-C / dock"),
        ["Display.Virtual"] = ("Virtuel", "Virtual"),
        ["Display.Label"] = ("Écran {0} · {1} · {2} × {3}", "Display {0} · {1} · {2} × {3}"),
        ["Display.Primary"] = ("Écran principal", "Primary display"),
        ["Display.Cursor"] = ("Écran où se trouve la souris", "Display with the mouse pointer"),

        // ── Hublot Core : textes du code ─────────────────────────────────────────────────────
        ["Core.AcrylicNeeds"] = ("Acrylique (Windows 11 22H2+)", "Acrylic (Windows 11 22H2+)"),
        ["Core.MicaNeeds"] = ("Mica (Windows 11 22H2+)", "Mica (Windows 11 22H2+)"),
        ["Core.Version"] = ("Version {0}", "Version {0}"),
        ["Core.ServiceMissing"] = ("Service introuvable : {0}", "Service not found: {0}"),
        ["Core.Running"] = ("Hublot est actif", "Hublot is running"),
        ["Core.NotRunning"] = ("Hublot n'est pas lancé", "Hublot is not running"),
        ["Core.AppliedNow"] = ("Les modifications sont appliquées immédiatement.", "Changes apply immediately."),
        ["Core.AppliedNext"] = ("Les paramètres seront pris en compte au prochain lancement.", "Settings will apply the next time Hublot starts."),
        ["Core.BackgroundOpacity"] = ("Opacité du fond", "Background opacity"),
        ["Core.TintIntensity"] = ("Intensité de la teinte", "Tint intensity"),
        ["Core.OpacityDescription"] = ("En dessous de 100 %, le bureau est visible à travers le popup.", "Below 100%, the desktop shows through the popup."),
        ["Core.TintDescription"] = ("0 % : matériau Windows pur. Plus la valeur est haute, plus le fond est couvrant.", "0%: pure Windows material. The higher the value, the more opaque the background."),
        ["Ago.Now"] = ("à l'instant", "just now"),
        ["Ago.Minutes"] = ("il y a {0} min", "{0} min ago"),
        ["Ago.Today"] = ("aujourd'hui à {0}", "today at {0}"),
        ["Ago.Yesterday"] = ("hier à {0}", "yesterday at {0}"),
        ["Ago.DateFormat"] = ("d MMMM à HH:mm", "MMMM d, h:mm tt"),
        ["Ago.TimeFormat"] = ("HH:mm", "h:mm tt"),
        ["Media.LastPlayed"] = ("Dernière lecture {0}", "Last played {0}"),
        ["Key.None"] = ("Aucun", "None"),
        ["Key.Shift"] = ("Maj", "Shift"),
        ["Key.Numpad"] = ("Pavé {0}", "Num {0}"),
        ["Key.Space"] = ("Espace", "Space"),
        ["Shortcut.Description"] = ("Hublot : modules visuels pour Windows", "Hublot: visual modules for Windows"),
        ["Uninstall.Confirm"] = ("Désinstaller Hublot ?", "Uninstall Hublot?"),
        ["Uninstall.KeepSettings"] = ("Conserver mes paramètres pour une prochaine installation", "Keep my settings for a future installation"),
        ["Uninstall.Button"] = ("Désinstaller", "Uninstall"),
        ["Uninstall.Done"] = ("Hublot a été désinstallé.", "Hublot has been uninstalled."),
        ["Setup.Update"] = ("Version {0} — mise à jour de l'installation existante", "Version {0} — update of the existing installation"),
        ["Setup.UpdateButton"] = ("Mettre à jour", "Update"),
        ["Setup.Done"] = ("Hublot est installé et lancé. Son icône (le hublot) est dans la zone de notification.", "Hublot is installed and running. Its icon (the porthole) is in the notification area."),
        ["Setup.OpenSettings"] = ("Ouvrir les paramètres", "Open settings"),
        ["Setup.Close"] = ("Fermer", "Close"),
        ["Setup.Failed"] = ("Échec de l'installation : {0}", "Installation failed: {0}"),
        ["Setup.Retry"] = ("Réessayer", "Retry"),
        ["Setup.Stopping"] = ("Arrêt de l'ancienne version…", "Stopping the previous version…"),
        ["Setup.Copying"] = ("Copie des fichiers…", "Copying files…"),
        ["Setup.Shortcuts"] = ("Création des raccourcis…", "Creating shortcuts…"),
        ["Setup.Register"] = ("Inscription dans les applications installées…", "Registering in installed apps…"),
        ["Setup.Starting"] = ("Démarrage de Hublot…", "Starting Hublot…"),
        ["Language"] = ("Langue", "Language"),
        ["LanguageDescription"] = ("Les paramètres se rouvrent dans la nouvelle langue.", "Settings reopen in the new language."),
        ["LanguageAuto"] = ("Automatique (langue de Windows)", "Automatic (Windows language)"),
        ["LanguageFrench"] = ("Français", "Français"),
        ["LanguageEnglish"] = ("English", "English"),
        ["Update.Available"] = ("Mise à jour disponible", "Update available"),
        ["Update.AvailableDetail"] = ("Hublot {0} · cliquez pour l'installer", "Hublot {0} · click to install"),
        ["Update.Section"] = ("Mises à jour", "Updates"),
        ["Update.Auto"] = ("Rechercher automatiquement les mises à jour", "Check for updates automatically"),
        ["Update.AutoDescription"] = ("Une fois par jour, sur GitHub.", "Once a day, on GitHub."),
        ["Update.Check"] = ("Rechercher les mises à jour", "Check for updates"),
        ["Update.Checking"] = ("Recherche en cours…", "Checking…"),
        ["Update.UpToDate"] = ("Hublot est à jour.", "Hublot is up to date."),
        ["Update.NewVersion"] = ("Version {0} disponible.", "Version {0} is available."),
        ["Update.Install"] = ("Installer la mise à jour", "Install update"),
        ["Update.Downloading"] = ("Téléchargement…", "Downloading…"),
        ["Update.Failed"] = ("Impossible de joindre GitHub.", "Couldn't reach GitHub."),

        // ── Hublot Core : fenêtre des paramètres (MainWindow.xaml) ───────────────────────────
        ["PopupsPourCetAppareil"] = ("Popups pour cet appareil", "Popups for this device"),
        ["Accueil"] = ("Accueil", "Home"),
        ["General"] = ("Général", "General"),
        ["Apparence"] = ("Apparence", "Appearance"),
        ["Modules"] = ("Modules", "Modules"),
        ["AppareilsSansFil"] = ("Appareils sans fil", "Wireless devices"),
        ["NotificationsDAppareils"] = ("Notifications d'appareils", "Device notifications"),
        ["LectureEnCours"] = ("Lecture en cours", "Now playing"),
        ["PeripheriquesUsb"] = ("Périphériques USB", "USB devices"),
        ["Ecrans"] = ("Écrans", "Displays"),
        ["Alimentation"] = ("Alimentation", "Power"),
        ["APropos"] = ("À propos", "About"),
        ["Demarrer"] = ("Démarrer", "Start"),
        ["BatterieEtConnexionDesAppareils"] = ("Batterie et connexion des appareils Bluetooth, Logitech et Xbox.", "Battery and connection of Bluetooth, Logitech and Xbox devices."),
        ["Configurer"] = ("Configurer", "Configure"),
        ["AfficheLeTitreEtL"] = ("Affiche le titre et l'artiste en cours de lecture.", "Shows the title and artist of what's playing."),
        ["NomEtEtatDesPeripheriques"] = ("Nom et état des périphériques USB, espace libre des clés et disques.", "Name and state of USB devices, free space on drives."),
        ["NomConnectiqueEtResolutionD"] = ("Nom, connectique et résolution d'un écran branché ou débranché.", "Name, connection and resolution of a display plugged in or unplugged."),
        ["NomDuPcEtCharge"] = ("Nom du PC et charge de la batterie au branchement et au débranchement.", "PC name and battery level when plugged in or unplugged."),
        ["Demarrage"] = ("Démarrage", "Startup"),
        ["LancerAuDemarrageDeWindows"] = ("Lancer au démarrage de Windows", "Start with Windows"),
        ["DemarreHublotALOuverture"] = ("Démarre Hublot à l'ouverture de session.", "Starts Hublot when you sign in."),
        ["Popup"] = ("Popup", "Popup"),
        ["DureeDAffichage"] = ("Durée d'affichage", "Display duration"),
        ["LePopupResteAussiAffiche"] = ("Le popup reste aussi affiché au survol de la souris.", "The popup also stays while the mouse is over it."),
        ["ClicSurLePopup"] = ("Clic sur le popup", "Clicking the popup"),
        ["FermerLePopupOuOuvrir"] = ("Fermer le popup, ou ouvrir les paramètres Bluetooth de Windows.", "Close the popup, or open the Windows Bluetooth settings."),
        ["FermerLePopup"] = ("Fermer le popup", "Close the popup"),
        ["OuvrirLesParametresBluetooth"] = ("Ouvrir les paramètres Bluetooth", "Open Bluetooth settings"),
        ["AppareilsConnectes"] = ("Appareils connectés", "Connected devices"),
        ["RaccourciClavier"] = ("Raccourci clavier", "Keyboard shortcut"),
        ["AfficheTousLesAppareilsConnectes"] = ("Affiche tous les appareils connectés. Cliquez dans la case, puis tapez la combinaison.", "Shows all connected devices. Click in the box, then press the key combination."),
        ["SurvolDeLIconeDe"] = ("Survol de l'icône de notification", "Hovering the notification icon"),
        ["LesAppareilsConnectesEtLeur"] = ("Les appareils connectés et leur batterie s'affichent au survol de l'icône.", "Connected devices and their battery appear when hovering the icon."),
        ["AfficherAussiLesAppareilsDeconnectes"] = ("Afficher aussi les appareils déconnectés au survol", "Also show disconnected devices on hover"),
        ["NePasDeranger"] = ("Ne pas déranger", "Do not disturb"),
        ["NePasAfficherPendantUne"] = ("Ne pas afficher pendant une application en plein écran", "Don't show while an app is full screen"),
        ["JeuxVideosPresentationsLesModules"] = ("Jeux, vidéos, présentations. Les modules USB et Alimentation ont leur propre réglage.", "Games, videos, presentations. The USB and Power modules have their own setting."),
        ["Notifications"] = ("Notifications", "Notifications"),
        ["ModuleDesactive"] = ("Module désactivé.", "Module turned off."),
        ["Connexion"] = ("Connexion", "Connection"),
        ["ConnexionDUnAppareil"] = ("Connexion d'un appareil", "Device connected"),
        ["DeconnexionDUnAppareil"] = ("Déconnexion d'un appareil", "Device disconnected"),
        ["NouveauPeripheriqueDetecte"] = ("Nouveau périphérique détecté", "New device detected"),
        ["PremiereConnexionDUnAppareil"] = ("Première connexion d'un appareil.", "First connection of a device."),
        ["Batterie"] = ("Batterie", "Battery"),
        ["BatterieFaible"] = ("Batterie faible", "Low battery"),
        ["UneAlerteParDecharge"] = ("Une alerte par décharge.", "One alert per discharge."),
        ["MiseEnCharge"] = ("Mise en charge", "Charging started"),
        ["QuandUnAppareilCommenceA"] = ("Quand un appareil commence à charger.", "When a device starts charging."),
        ["NiveauDeChargeAtteint"] = ("Niveau de charge atteint", "Charge level reached"),
        ["QuandLaBatterieAtteintCe"] = ("Quand la batterie atteint ce niveau en charge.", "When the battery reaches this level while charging."),
        ["IgnorerLesAppareilsConnectesAu"] = ("Ignorer les appareils connectés au démarrage de Windows", "Ignore devices connecting at Windows startup"),
        ["PasDePopupPourLes"] = ("Pas de popup pour les appareils qui se reconnectent à l'ouverture de session.", "No popup for devices reconnecting when you sign in."),
        ["IgnorerLesReconnexionsApresLa"] = ("Ignorer les reconnexions après la mise en veille", "Ignore reconnections after sleep"),
        ["PasDePopupALa"] = ("Pas de popup à la mise en veille ni au réveil.", "No popup when the PC goes to sleep or wakes up."),
        ["IgnorerLaVeilleDesClaviers"] = ("Ignorer la veille des claviers", "Ignore keyboard sleep"),
        ["PasDePopupQuandIls"] = ("Pas de popup quand ils se mettent en veille puis se réveillent au premier appui.", "No popup when they go to sleep and wake up on the next key press."),
        ["IgnorerLaVeilleDesSouris"] = ("Ignorer la veille des souris", "Ignore mouse sleep"),
        ["PasDePopupQuandEllesSeReveillent"] = ("Pour les souris qui se déconnectent après quelques minutes d'inactivité. Une souris éteinte puis rallumée dans les 30 min n'aura plus de popup.", "For mice that disconnect after a few idle minutes. A mouse turned off and back on within 30 min will no longer show a popup."),
        ["AppareilsEnSourdine"] = ("Appareils en sourdine", "Muted devices"),
        ["ChoisissezLesAppareilsAutorisesA"] = ("Choisissez les appareils autorisés à afficher un popup.", "Choose which devices may show a popup."),
        ["OuvrirLaPageAppareils"] = ("Ouvrir la page Appareils", "Open the Devices page"),
        ["Apercu"] = ("Aperçu", "Preview"),
        ["ApercuDeLaListe"] = ("Aperçu de la liste", "Preview the list"),
        ["Emplacement"] = ("Emplacement", "Position"),
        ["EnHautAGauche"] = ("En haut à gauche", "Top left"),
        ["EnHautAuCentre"] = ("En haut au centre", "Top center"),
        ["EnHautADroite"] = ("En haut à droite", "Top right"),
        ["EnBasAGauche"] = ("En bas à gauche", "Bottom left"),
        ["EnBasAuCentre"] = ("En bas au centre", "Bottom center"),
        ["EnBasADroite"] = ("En bas à droite", "Bottom right"),
        ["Ecran"] = ("Écran", "Display"),
        ["Identifier"] = ("Identifier", "Identify"),
        ["DistanceAuBordDeL"] = ("Distance au bord de l'écran", "Distance from the screen edge"),
        ["ListeDesAppareilsCartesEmpilees"] = ("Liste des appareils : cartes empilées pour un coin, côte à côte pour une position centrée.", "Device list: stacked cards in a corner, side by side when centered."),
        ["Style"] = ("Style", "Style"),
        ["Materiau"] = ("Matériau", "Material"),
        ["AcryliqueEtMicaFondFloute"] = ("Acrylique et Mica : fond flouté de Windows 11. Uni : couleur pleine.", "Acrylic and Mica: Windows 11 blurred background. Solid: plain color."),
        ["Acrylique"] = ("Acrylique", "Acrylic"),
        ["Mica"] = ("Mica", "Mica"),
        ["Uni"] = ("Uni", "Solid"),
        ["Theme"] = ("Thème", "Theme"),
        ["SystemeSuitLeModeDe"] = ("Système : suit le mode de la barre des tâches.", "System: follows the taskbar mode."),
        ["Systeme"] = ("Système", "System"),
        ["Clair"] = ("Clair", "Light"),
        ["Sombre"] = ("Sombre", "Dark"),
        ["CouleurDAccentuation"] = ("Couleur d'accentuation", "Accent color"),
        ["TuileDIconeEtJauge"] = ("Tuile d'icône et jauge à la couleur de Windows.", "Icon tile and gauge in the Windows color."),
        ["AfficherLeTypeDeLiaison"] = ("Afficher le type de liaison", "Show the connection type"),
        ["ConnecteBluetoothOuConnecteRecepteur"] = ("« Connecté · Bluetooth » ou « Connecté · Récepteur USB ».", "“Connected · Bluetooth” or “Connected · USB receiver”."),
        ["Taille"] = ("Taille", "Size"),
        ["Compacte"] = ("Compacte", "Compact"),
        ["Normale"] = ("Normale", "Normal"),
        ["Grande"] = ("Grande", "Large"),
        ["Animation"] = ("Animation", "Animation"),
        ["EntreeEtSortie"] = ("Entrée et sortie", "Entrance and exit"),
        ["GlissementDepuisLeBordLe"] = ("Glissement depuis le bord le plus proche, ou simple fondu.", "Slide in from the nearest edge, or a simple fade."),
        ["Glissement"] = ("Glissement", "Slide"),
        ["Fondu"] = ("Fondu", "Fade"),
        ["Aucune"] = ("Aucune", "None"),
        ["Vitesse"] = ("Vitesse", "Speed"),
        ["Rapide"] = ("Rapide", "Fast"),
        ["Lente"] = ("Lente", "Slow"),
        ["Actualiser"] = ("Actualiser", "Refresh"),
        ["ModuleAppareilsSansFil"] = ("Module Appareils sans fil", "Wireless devices module"),
        ["DesactiveIlNeSurveilleRien"] = ("Désactivé, il ne surveille rien et ne consomme rien.", "When off, it watches nothing and uses nothing."),
        ["DesactivezUnAppareilPourNe"] = ("Désactivez un appareil pour ne plus recevoir de popup à son sujet.", "Turn a device off to stop getting popups about it."),
        ["Bluetooth"] = ("Bluetooth", "Bluetooth"),
        ["AppareilsBluetooth"] = ("Appareils Bluetooth", "Bluetooth devices"),
        ["AppareilsAppairesAWindows"] = ("Appareils appairés à Windows.", "Devices paired with Windows."),
        ["DonglesUsb"] = ("Dongles USB", "USB dongles"),
        ["RecepteursUsbLogitech"] = ("Récepteurs USB Logitech", "Logitech USB receivers"),
        ["DonglesUnifyingBoltLightspeedEt"] = ("Dongles Unifying, Bolt, Lightspeed et Nano.", "Unifying, Bolt, Lightspeed and Nano dongles."),
        ["ManettesSansFil"] = ("Manettes sans fil", "Wireless controllers"),
        ["AvecLAdaptateurSansFil"] = ("Avec l'adaptateur sans fil Xbox.", "With the Xbox Wireless Adapter."),
        ["LesDonglesDAutresMarques"] = ("Les dongles d'autres marques (Corsair, Razer, SteelSeries…) ne sont pas pris en charge : ils ne transmettent ni la connexion ni la batterie.", "Dongles from other brands (Corsair, Razer, SteelSeries…) aren't supported: they report neither connection nor battery."),
        ["ModuleLectureEnCours"] = ("Module Lecture en cours", "Now playing module"),
        ["SpotifyNavigateursLecteurMultimediaVlc"] = ("Spotify, navigateurs, Lecteur multimédia, VLC… toute application déclarée à Windows.", "Spotify, browsers, Media Player, VLC… any app that reports to Windows."),
        ["MusiqueUniquement"] = ("Plateformes musicales uniquement", "Music platforms only"),
        ["IgnoreFilmsSeriesEtVideos"] = ("Spotify, Deezer, Apple Music, YouTube Music, Amazon Music, TIDAL, Qobuz, SoundCloud… et leurs sites web. Pour une autre application (radio web, VLC…), réglez-la sur « Tout afficher » plus bas.", "Spotify, Deezer, Apple Music, YouTube Music, Amazon Music, TIDAL, Qobuz, SoundCloud… and their websites. For another app (web radio, VLC…), set it to “Show everything” below."),
        ["AChaqueNouveauTitre"] = ("À chaque nouveau titre", "On every new track"),
        ["UnPopupAChaqueChangement"] = ("Un popup à chaque changement de titre.", "A popup each time the track changes."),
        ["AussiALaRepriseDe"] = ("Aussi à la reprise de la lecture", "Also when playback resumes"),
        ["ApresUnePause"] = ("Après une pause.", "After a pause."),
        ["AussiALaMiseEn"] = ("Aussi à la mise en pause", "Also when paused"),
        ["QuandLaLectureSArrete"] = ("Quand la lecture s'arrête.", "When playback stops."),
        ["PasDePopupSiL"] = ("Pas de popup si l'application est au premier plan", "No popup if the app is in the foreground"),
        ["PasDePopupSiL2"] = ("Pas de popup si l'application qui joue est déjà à l'écran (hors onglets de navigateur).", "No popup if the playing app is already on screen (browser tabs excepted)."),
        ["MiniLecteur"] = ("Lecteur", "Player"),
        ["LeLecteurSAfficheAChaqueTitre"] = ("Le lecteur s'affiche à chaque titre. Faites-le glisser à la souris pour le déplacer : coins, haut ou bas au centre, autre écran.", "The player appears with each track. Drag it with the mouse to move it: corners, top or bottom center, another display."),
        ["RaccourciClavierDuMiniLecteur"] = ("Lecteur permanent", "Always-on player"),
        ["MasqueOuAfficheLeMini"] = ("Ce raccourci garde le lecteur affiché en permanence, ou le masque. Hublot s'en souvient.", "This shortcut keeps the player on screen, or hides it. Hublot remembers."),
        ["ProgressionDuTitre"] = ("Progression du titre", "Track progress"),
        ["BarreDeProgressionEtDuree"] = ("Barre de progression et durée sous les boutons.", "Progress bar and duration below the buttons."),
        ["ProgressionCliquable"] = ("Progression cliquable", "Clickable progress"),
        ["CouleursDeLaPochette"] = ("Couleurs de la pochette", "Album art colors"),
        ["LeClicMasqueJusquAuRaccourci"] = ("Le clic masque jusqu'au raccourci", "Click hides until the shortcut"),
        ["SinonLeMiniLecteurRevient"] = ("Sinon, le lecteur permanent revient au titre suivant.", "Otherwise the always-on player comes back on the next track."),
        ["IconeDeLaSourceCliquable"] = ("Icône de la source cliquable", "Clickable source icon"),
        ["UnClicSurLIconeOuvre"] = ("Un clic sur l'icône de l'application l'ouvre, un second la réduit.", "Clicking the app icon opens the app, clicking again minimizes it."),
        ["TeinteLaCarteSelonLaPochette"] = ("Teinte la carte selon la pochette.", "Tints the card to match the album art."),
        ["UnClicSurLaBarre"] = ("Un clic sur la barre avance ou recule dans le titre.", "Clicking the bar skips forward or back in the track."),
        ["Applications"] = ("Applications", "Apps"),
        ["ParDefautSelonMusiqueUniquement"] = ("Par défaut : selon « Plateformes musicales uniquement ». Tout afficher : tout ce que joue l'application. Masquer : rien. Le choix vaut pour tous les onglets d'un navigateur.", "Default: follows “Music platforms only”. Show everything: all the app plays. Hide: nothing. The choice applies to all tabs of a browser."),
        ["ParDefaut"] = ("Par défaut", "Default"),
        ["SuitLOptionMusiqueUniquement"] = ("Suit l'option « Plateformes musicales uniquement »", "Follows the “Music platforms only” option"),
        ["ToutAfficher"] = ("Tout afficher", "Show everything"),
        ["RadiosWebVideosMemeHors"] = ("Radios web, vidéos… même hors musique", "Web radios, videos… even when not music"),
        ["Masquer"] = ("Masquer", "Hide"),
        ["NeJamaisAfficherLesMedias"] = ("Ne jamais afficher les médias de cette application", "Never show media from this app"),
        ["LesApplicationsApparaissentIciDes"] = ("Les applications apparaissent ici dès qu'elles jouent un média.", "Apps appear here as soon as they play media."),
        ["ModulePeripheriquesUsb"] = ("Module Périphériques USB", "USB devices module"),
        ["ClesDisquesClaviersSourisWebcams"] = ("Clés, disques, claviers, souris, webcams…", "Drives, keyboards, mice, webcams…"),
        ["BranchementDUnPeripherique"] = ("Branchement d'un périphérique", "Device plugged in"),
        ["AvecLEspaceLibrePour"] = ("Avec l'espace libre pour une clé ou un disque.", "With the free space of a flash drive or disk."),
        ["DebranchementDUnPeripherique"] = ("Débranchement d'un périphérique", "Device unplugged"),
        ["UniquementLesSupportsDeStockage"] = ("Uniquement les supports de stockage", "Storage devices only"),
        ["IgnorerLesRetraitsEnToute"] = ("Ignorer les retraits en toute sécurité", "Ignore safe removals"),
        ["WindowsAfficheDejaSaPropre"] = ("Windows affiche déjà sa propre notification.", "Windows already shows its own notification."),
        ["AfficherAussiParDessusLe"] = ("Afficher aussi par-dessus le plein écran", "Also show over full screen apps"),
        ["AuReveilDuPcLes"] = ("Au réveil du PC, les reconnexions sont ignorées si l'option est active dans Notifications d'appareils.", "When the PC wakes up, reconnections are ignored if the option is on in Device notifications."),
        ["ModuleAlimentation"] = ("Module Alimentation", "Power module"),
        ["NomDuPcEtCharge2"] = ("Nom du PC et charge de la batterie. Un débranchement éclair ne déclenche rien.", "PC name and battery level. A brief unplug triggers nothing."),
        ["BranchementSurSecteur"] = ("Branchement sur secteur", "Plugged in"),
        ["Debranchement"] = ("Débranchement", "Unplugged"),
        ["AvecLAutonomieRestanteSi"] = ("Avec l'autonomie restante, si Windows l'a estimée.", "With the remaining time, if Windows has estimated it."),
        ["MemePendantUnJeuUne"] = ("Même pendant un jeu, une vidéo ou une présentation.", "Even during a game, a video or a presentation."),
        ["NiveauxDeBatterie"] = ("Niveaux de batterie", "Battery levels"),
        ["SurSecteurParExemple80"] = ("Sur secteur, par exemple 80 % pour ménager la batterie.", "While plugged in, for example 80% to preserve the battery."),
        ["SurBatterie"] = ("Sur batterie.", "On battery."),
        ["CePcNAPas"] = ("Ce PC n'a pas de batterie : rien à surveiller.", "This PC has no battery: nothing to watch."),
        ["ModuleEcrans"] = ("Module Écrans", "Displays module"),
        ["HdmiDisplayportVgaUsbC"] = ("HDMI, DisplayPort, VGA, USB-C…", "HDMI, DisplayPort, VGA, USB-C…"),
        ["BranchementDUnEcran"] = ("Branchement d'un écran", "Display plugged in"),
        ["DebranchementDUnEcran"] = ("Débranchement d'un écran", "Display unplugged"),
        ["UneSerieDePopupsFluent"] = (
            "Une série de popups Fluent Design pour un Windows plus agréable." + Environment.NewLine
            + "Un seul processus léger, sans interrogation en boucle. Hublot attend les notifications de Windows et ne s'active que pendant les animations.",
            "A set of Fluent Design popups for a nicer Windows." + Environment.NewLine
            + "A single lightweight process with no polling. Hublot waits for Windows notifications and only works during animations."),
        ["OuvrirLeDossierDesParametres"] = ("Ouvrir le dossier des paramètres", "Open the settings folder"),
        ["Desactive"] = ("Désactivé", "Off"),
        ["Active"] = ("Activé", "On"),

        // ── Installation (SetupWindow.xaml) ──────────────────────────────────────────────────
        ["InstallationDeHublot"] = ("Installation de Hublot", "Hublot setup"),
        ["DesModulesVisuelsPourWindows"] = ("Des modules visuels pour Windows, au plus près du Fluent Design : vos appareils sans fil et leur batterie, la musique en cours de lecture… Installation pour votre compte uniquement, sans droits administrateur.", "Visual modules for Windows, as close as possible to Fluent Design: your wireless devices and their battery, the music playing… Installs for your account only, no administrator rights needed."),
        ["ModifiableEnsuiteDansLesParametres"] = ("Modifiable ensuite dans les paramètres.", "You can change this later in settings."),
        ["Annuler"] = ("Annuler", "Cancel"),
        ["Installer"] = ("Installer", "Install"),
    };
}
