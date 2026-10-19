using System.Globalization;
using System.IO;

namespace Hublot.Common;

public enum PopupPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }
/// <summary>Écran des popups ; <c>Specific</c> = l'écran choisi (<see cref="Settings.MonitorDevice"/>).</summary>
public enum PopupMonitor { Primary, Cursor, Specific }
public enum PopupMaterial { Acrylic, Mica, Solid }
public enum PopupTheme { System, Light, Dark }
public enum PopupSize { Compact, Normal, Large }
public enum PopupAnimation { Slide, Fade, None }
public enum AnimationSpeed { Fast, Normal, Slow }
public enum ClickAction { Dismiss, OpenBluetoothSettings }

/// <summary>
/// Paramètres partagés entre le service (Hublot.exe) et l'utilitaire de réglages
/// (HublotCore.exe, qui inclut ce fichier). Format « clé=valeur » volontairement
/// minimal : aucune dépendance (pas de System.Text.Json) côté service.
/// </summary>
public sealed class Settings
{
    /// <summary>Classe de la fenêtre hôte du service, cible des messages ci-dessous.</summary>
    public const string HostWindowClass = "Hublot.Host";
    /// <summary>Message enregistré : le fichier de paramètres vient d'être modifié.</summary>
    public const string ChangedMessage = "Hublot.SettingsChanged";
    /// <summary>
    /// Message enregistré : aperçu avec les paramètres courants (wParam : 0 = carte d'appareil,
    /// 1 = liste d'appareils, 2 = lecture en cours, 3 = stockage USB).
    /// </summary>
    public const string PreviewMessage = "Hublot.Preview";
    /// <summary>Hublot Core → service : nouvelle version disponible (wParam = majeur &lt;&lt; 16 | mineur &lt;&lt; 8 | correctif).</summary>
    public const string UpdateMessage = "Hublot.UpdateAvailable";

    // Modificateurs RegisterHotKey
    public const int ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    // Emplacement
    /// <summary>Langue de l'interface (Auto : celle de Windows).</summary>
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
    /// <summary>Version classique : recherche quotidienne d'une nouvelle version sur GitHub.</summary>
    public bool CheckUpdates { get; set; } = true;
    public PopupPosition Position { get; set; } = PopupPosition.BottomCenter;
    public PopupMonitor Monitor { get; set; } = PopupMonitor.Primary;
    /// <summary>Écran choisi (<see cref="DisplayInfo.Path"/>, stable d'un branchement à l'autre) ; absent → écran principal.</summary>
    public string MonitorDevice { get; set; } = "";
    public int Margin { get; set; } = 12;
    public int DurationSeconds { get; set; } = 5;

    // Apparence
    public PopupMaterial Material { get; set; } = PopupMaterial.Acrylic;
    /// <summary>Acrylique / Mica : opacité de la teinte posée sur le matériau (0 = matériau pur).</summary>
    public int TintOpacity { get; set; } = 50;
    /// <summary>Uni : opacité du fond (100 = opaque).</summary>
    public int SolidOpacity { get; set; } = 100;
    public PopupTheme Theme { get; set; } = PopupTheme.System;
    public bool UseAccentColor { get; set; } = true;
    public PopupSize Size { get; set; } = PopupSize.Normal;
    public PopupAnimation Animation { get; set; } = PopupAnimation.Slide;
    public AnimationSpeed Speed { get; set; } = AnimationSpeed.Normal;
    /// <summary>« Connecté · Bluetooth » / « Connecté · Récepteur USB ».</summary>
    public bool ShowLinkType { get; set; } = true;

    // Notifications
    public bool NotifyConnect { get; set; } = true;
    public bool ShowDisconnections { get; set; } = true;
    public bool NotifyNewDevice { get; set; } = true;
    public bool NotifyLowBattery { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;
    public bool NotifyCharging { get; set; }
    public bool NotifyChargeTarget { get; set; }
    public int ChargeTarget { get; set; } = 100;

    // Silence
    /// <summary>Pas de popup pour les appareils qui se reconnectent à l'ouverture de session.</summary>
    public bool IgnoreStartupConnections { get; set; } = true;
    /// <summary>Pas de popup pour les déconnexions / reconnexions liées à la mise en veille.</summary>
    public bool IgnoreResumeConnections { get; set; } = true;
    /// <summary>
    /// Claviers qui se déconnectent en veille (MX Keys…) : pas de popup de déconnexion, ni de reconnexion dans
    /// les <see cref="InputSleepMinutes"/> minutes. (Nom d'avant 3.7.0, quand l'option valait aussi pour les souris.)
    /// </summary>
    public bool QuietInputSleep { get; set; } = true;
    /// <summary>
    /// Même chose pour les souris (3.7.0), désactivée par défaut : une souris qu'on éteint puis rallume (Pebble…)
    /// ne se distingue pas d'une souris qui se réveille, et ses popups disparaissaient.
    /// </summary>
    public bool QuietMouseSleep { get; set; }
    public const int InputSleepMinutes = 30;
    public bool ShowInFullscreen { get; set; }
    /// <summary>Clés des appareils dont on ne veut aucune notification.</summary>
    public HashSet<string> MutedDevices { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Interaction
    public ClickAction ClickAction { get; set; } = ClickAction.Dismiss;
    /// <summary>Survol de l'icône de notification : cartes des appareils connectés.</summary>
    public bool HoverList { get; set; } = true;
    /// <summary>Survol : aussi les derniers appareils déconnectés (après les connectés).</summary>
    public bool HoverShowDisconnected { get; set; }
    public bool HotkeyEnabled { get; set; } = true;
    public int HotkeyModifiers { get; set; } = ModControl | ModAlt;
    /// <summary>Code de touche virtuelle (0x42 = B).</summary>
    public int HotkeyKey { get; set; } = 0x42;

    // Modules
    public bool ModuleBluetooth { get; set; } = true;
    public bool ModuleNowPlaying { get; set; } = true;
    public bool ModuleUsb { get; set; } = true;
    /// <summary>Activé par défaut seulement sur un PC avec batterie (portable) : sans, il n'a rien à surveiller.</summary>
    public bool ModulePower { get; set; } = HasBattery();
    public bool ModuleDisplays { get; set; } = true;

    // Module « Écrans »
    public bool DisplayNotifyConnect { get; set; } = true;
    public bool DisplayNotifyDisconnect { get; set; } = true;

    // Module « Prise audio » (3.7.0) : casque, écouteurs ou micro branché / débranché sur une prise jack.
    public bool ModuleAudio { get; set; } = true;
    public bool AudioNotifyPlugged { get; set; } = true;
    public bool AudioNotifyUnplugged { get; set; } = true;

    // Module « Alimentation »
    public bool PowerNotifyPlugged { get; set; } = true;
    public bool PowerNotifyUnplugged { get; set; } = true;
    /// <summary>Popup quand la charge atteint <see cref="PowerLevel"/> sur secteur.</summary>
    public bool PowerNotifyLevel { get; set; } = true;
    public int PowerLevel { get; set; } = 80;
    /// <summary>Popup quand la charge descend à <see cref="PowerLowLevel"/> sur batterie.</summary>
    public bool PowerNotifyLow { get; set; }
    public int PowerLowLevel { get; set; } = 20;
    /// <summary>Popups d'alimentation aussi par-dessus une application en plein écran (désactivé par défaut depuis 3.3.0).</summary>
    public bool PowerShowInFullscreen { get; set; }

    // Module « Périphériques USB »
    public bool UsbNotifyConnect { get; set; } = true;
    public bool UsbNotifyDisconnect { get; set; } = true;
    /// <summary>Popups uniquement pour les stockages (clés, disques, cartes mémoire).</summary>
    public bool UsbStorageOnly { get; set; }
    /// <summary>Pas de popup pour un « retrait en toute sécurité » (Windows affiche déjà le sien).</summary>
    public bool UsbIgnoreSafeRemoval { get; set; } = true;
    /// <summary>Popups USB aussi par-dessus une application en plein écran (désactivé par défaut depuis 3.3.0).</summary>
    public bool UsbShowInFullscreen { get; set; }

    // Module « Lecture en cours »
    /// <summary>Popup à chaque nouveau titre.</summary>
    public bool MediaNotifyTrackChange { get; set; } = true;
    /// <summary>Popup aussi à la reprise de la lecture (même titre).</summary>
    public bool MediaNotifyOnResume { get; set; } = true;
    /// <summary>Popup aussi à la mise en pause (seule option du lecteur désactivée par défaut, 3.7.0).</summary>
    public bool MediaNotifyOnPause { get; set; }
    /// <summary>Pas de popup quand l'application qui joue est au premier plan.</summary>
    public bool MediaHideWhenAppFocused { get; set; } = true;
    /// <summary>
    /// Plateformes musicales uniquement : les grands services de musique (<see cref="Modules.NowPlaying.MediaApps"/>)
    /// et les onglets de navigateur qui déclarent un album ; ni vidéos, ni autres applications.
    /// </summary>
    public bool MediaMusicOnly { get; set; } = true;
    /// <summary>
    /// Lecteur permanent, choisi au raccourci clavier et enregistré par le service (plus d'option dans Hublot Core
    /// depuis 3.6.0). Sinon, le lecteur s'affiche le temps d'une notification à chaque titre.
    /// </summary>
    public bool MediaPinned { get; set; }
    /// <summary>
    /// Emplacement du lecteur, temporaire comme permanent (un seul lecteur), changé en le faisant glisser à la
    /// souris et enregistré par le service.
    /// </summary>
    public PopupPosition MediaPinnedPosition { get; set; } = PopupPosition.BottomCenter;
    /// <summary>Écran du lecteur (principal ou un écran précis), comme son emplacement.</summary>
    public PopupMonitor MediaPinnedMonitor { get; set; } = PopupMonitor.Primary;
    public string MediaPinnedMonitorDevice { get; set; } = "";
    /// <summary>Format des réglages du lecteur : 0 = avant 3.6.0 (popup musical et mini-lecteur séparés), 1 = un seul lecteur.</summary>
    public int MediaPlayerVersion { get; set; }
    /// <summary>Barre de progression du titre (position / durée), si l'application la communique.</summary>
    public bool MediaShowProgress { get; set; } = true;
    /// <summary>Clic sur la barre de progression : avance / recule dans le titre.</summary>
    public bool MediaSeekable { get; set; } = true;
    /// <summary>Couleurs de la pochette : teinte de fond et accentuation de la carte (façon Material You).</summary>
    public bool MediaCoverColors { get; set; } = true;
    /// <summary>Clic sur le mini-lecteur : masqué jusqu'au prochain appui sur son raccourci (sinon jusqu'au titre suivant).</summary>
    public bool MediaPinnedClickUntilHotkey { get; set; } = true;
    /// <summary>Clic sur l'icône de l'application source : l'ouvre (premier plan).</summary>
    public bool MediaOpenAppOnClick { get; set; } = true;
    /// <summary>
    /// Raccourci du lecteur permanent : Ctrl + 0 du pavé numérique par défaut depuis 3.7.0 (0x60 = VK_NUMPAD0 ; le 0
    /// de la rangée du haut remet le zoom à 100 % dans les navigateurs, un raccourci global le leur prendrait).
    /// </summary>
    public bool MediaPinnedHotkeyEnabled { get; set; } = true;
    public int MediaPinnedHotkeyModifiers { get; set; } = ModControl;
    public int MediaPinnedHotkeyKey { get; set; } = 0x60;
    /// <summary>Applications (AppUserModelId) dont les médias ne s'affichent jamais.</summary>
    /// Par défaut : Microsoft Teams, qui déclare ses appels et réunions comme des médias.
    public HashSet<string> MediaExcludedApps { get; } = new(StringComparer.OrdinalIgnoreCase) { "MSTeams_8wekyb3d8bbwe!MSTeams" };
    /// <summary>Applications dont tout s'affiche, même hors musique (radio web dans Chrome…) : échappent à <see cref="MediaMusicOnly"/>.</summary>
    public HashSet<string> MediaAlwaysShownApps { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Sources du module Bluetooth
    public bool Bluetooth { get; set; } = true;
    public bool LogitechReceivers { get; set; } = true;
    public bool XboxControllers { get; set; } = true;

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hublot");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.ini");

    /// <summary>Le PC a-t-il une batterie (portable) ? GetSystemPowerStatus : BatteryFlag 128 = pas de batterie.</summary>
    private static bool HasBattery()
    {
        try
        {
            return GetSystemPowerStatus(out var s) && s.BatteryFlag != 255 && (s.BatteryFlag & 128) == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetSystemPowerStatus")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>
    /// Hublot s'appelait « WinTooth », et avant « Bluetooth Popup » : reprend une fois les fichiers
    /// (paramètres, appareils connus) si le dossier Hublot n'existe pas encore.
    /// </summary>
    public static void MigrateFromPreviousName()
    {
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string? old = new[] { "WinTooth", "BluetoothPopup" }.Select(n => Path.Combine(local, n)).FirstOrDefault(Directory.Exists);
            if (Directory.Exists(DirectoryPath) || old is null) return;
            Directory.CreateDirectory(DirectoryPath);
            foreach (var name in new[] { "settings.ini", "devices.tsv" })
            {
                string source = Path.Combine(old, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(DirectoryPath, name));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public float SizeFactor => Size switch { PopupSize.Compact => 0.9f, PopupSize.Large => 1.15f, _ => 1f };

    public float SpeedFactor => Speed switch { AnimationSpeed.Fast => 0.7f, AnimationSpeed.Slow => 1.5f, _ => 1f };

    /// <summary>Paramètres enregistrés ; la langue de l'interface (L) suit le réglage lu.</summary>
    public static Settings Load()
    {
        var settings = LoadFile();
        settings.MigratePlayer();
        L.Apply(settings.Language);
        return settings;
    }

    /// <summary>
    /// 3.6.0 : popup musical et mini-lecteur ne font plus qu'un lecteur, à un seul emplacement. Sans mini-lecteur, le
    /// lecteur reste là où s'affichaient les popups musicaux (l'emplacement des popups) ; mini-lecteur sans
    /// raccourci : raccourci réactivé, seul moyen désormais de le masquer. Appliqué à chaque lecture tant que le
    /// fichier n'est pas réenregistré (même résultat).
    /// </summary>
    private void MigratePlayer()
    {
        if (MediaPlayerVersion >= 1) return;
        if (!MediaPinned) (MediaPinnedPosition, MediaPinnedMonitor, MediaPinnedMonitorDevice) = (Position, Monitor, MonitorDevice);
        else if (!MediaPinnedHotkeyEnabled) MediaPinnedHotkeyEnabled = true;
        MediaPlayerVersion = 1;
    }

    private static Settings LoadFile()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0 || raw.StartsWith('#')) continue;
                s.Apply(raw[..eq].Trim(), raw[(eq + 1)..].Trim());
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        var lines = new List<string> { "# Hublot — modifié par HublotCore.exe" };
        void Add(string key, object value) => lines.Add($"{key}={Convert.ToString(value, CultureInfo.InvariantCulture)}");

        Add(nameof(Language), Language);
        Add(nameof(CheckUpdates), CheckUpdates);
        Add(nameof(Position), Position);
        Add(nameof(Monitor), Monitor);
        Add(nameof(MonitorDevice), MonitorDevice);
        Add(nameof(Margin), Margin);
        Add(nameof(DurationSeconds), DurationSeconds);
        Add(nameof(Material), Material);
        Add(nameof(TintOpacity), TintOpacity);
        Add(nameof(SolidOpacity), SolidOpacity);
        Add(nameof(Theme), Theme);
        Add(nameof(UseAccentColor), UseAccentColor);
        Add(nameof(Size), Size);
        Add(nameof(Animation), Animation);
        Add(nameof(Speed), Speed);
        Add(nameof(ShowLinkType), ShowLinkType);
        Add(nameof(NotifyConnect), NotifyConnect);
        Add(nameof(ShowDisconnections), ShowDisconnections);
        Add(nameof(NotifyNewDevice), NotifyNewDevice);
        Add(nameof(NotifyLowBattery), NotifyLowBattery);
        Add(nameof(LowBatteryThreshold), LowBatteryThreshold);
        Add(nameof(NotifyCharging), NotifyCharging);
        Add(nameof(NotifyChargeTarget), NotifyChargeTarget);
        Add(nameof(ChargeTarget), ChargeTarget);
        Add(nameof(IgnoreStartupConnections), IgnoreStartupConnections);
        Add(nameof(IgnoreResumeConnections), IgnoreResumeConnections);
        Add(nameof(QuietInputSleep), QuietInputSleep);
        Add(nameof(QuietMouseSleep), QuietMouseSleep);
        Add(nameof(ShowInFullscreen), ShowInFullscreen);
        Add(nameof(MutedDevices), string.Join('|', MutedDevices));
        Add(nameof(ClickAction), ClickAction);
        Add(nameof(HoverList), HoverList);
        Add(nameof(HoverShowDisconnected), HoverShowDisconnected);
        Add(nameof(HotkeyEnabled), HotkeyEnabled);
        Add(nameof(HotkeyModifiers), HotkeyModifiers);
        Add(nameof(HotkeyKey), HotkeyKey);
        Add(nameof(ModuleBluetooth), ModuleBluetooth);
        Add(nameof(ModuleNowPlaying), ModuleNowPlaying);
        Add(nameof(ModuleUsb), ModuleUsb);
        Add(nameof(ModulePower), ModulePower);
        Add(nameof(ModuleDisplays), ModuleDisplays);
        Add(nameof(DisplayNotifyConnect), DisplayNotifyConnect);
        Add(nameof(DisplayNotifyDisconnect), DisplayNotifyDisconnect);
        Add(nameof(ModuleAudio), ModuleAudio);
        Add(nameof(AudioNotifyPlugged), AudioNotifyPlugged);
        Add(nameof(AudioNotifyUnplugged), AudioNotifyUnplugged);
        Add(nameof(PowerNotifyPlugged), PowerNotifyPlugged);
        Add(nameof(PowerNotifyUnplugged), PowerNotifyUnplugged);
        Add(nameof(PowerNotifyLevel), PowerNotifyLevel);
        Add(nameof(PowerLevel), PowerLevel);
        Add(nameof(PowerNotifyLow), PowerNotifyLow);
        Add(nameof(PowerLowLevel), PowerLowLevel);
        Add(nameof(PowerShowInFullscreen), PowerShowInFullscreen);
        Add(nameof(UsbNotifyConnect), UsbNotifyConnect);
        Add(nameof(UsbNotifyDisconnect), UsbNotifyDisconnect);
        Add(nameof(UsbStorageOnly), UsbStorageOnly);
        Add(nameof(UsbIgnoreSafeRemoval), UsbIgnoreSafeRemoval);
        Add(nameof(UsbShowInFullscreen), UsbShowInFullscreen);
        Add(nameof(MediaNotifyTrackChange), MediaNotifyTrackChange);
        Add(nameof(MediaNotifyOnResume), MediaNotifyOnResume);
        Add(nameof(MediaNotifyOnPause), MediaNotifyOnPause);
        Add(nameof(MediaHideWhenAppFocused), MediaHideWhenAppFocused);
        Add(nameof(MediaMusicOnly), MediaMusicOnly);
        Add(nameof(MediaPinned), MediaPinned);
        Add(nameof(MediaPlayerVersion), MediaPlayerVersion);
        Add(nameof(MediaPinnedPosition), MediaPinnedPosition);
        Add(nameof(MediaPinnedMonitor), MediaPinnedMonitor);
        Add(nameof(MediaPinnedMonitorDevice), MediaPinnedMonitorDevice);
        Add(nameof(MediaShowProgress), MediaShowProgress);
        Add(nameof(MediaSeekable), MediaSeekable);
        Add(nameof(MediaCoverColors), MediaCoverColors);
        Add(nameof(MediaPinnedClickUntilHotkey), MediaPinnedClickUntilHotkey);
        Add(nameof(MediaOpenAppOnClick), MediaOpenAppOnClick);
        Add(nameof(MediaPinnedHotkeyEnabled), MediaPinnedHotkeyEnabled);
        Add(nameof(MediaPinnedHotkeyModifiers), MediaPinnedHotkeyModifiers);
        Add(nameof(MediaPinnedHotkeyKey), MediaPinnedHotkeyKey);
        Add(nameof(MediaExcludedApps), string.Join('|', MediaExcludedApps));
        Add(nameof(MediaAlwaysShownApps), string.Join('|', MediaAlwaysShownApps));
        Add(nameof(Bluetooth), Bluetooth);
        Add(nameof(LogitechReceivers), LogitechReceivers);
        Add(nameof(XboxControllers), XboxControllers);
        File.WriteAllLines(FilePath, lines);
    }

    private void Apply(string key, string value)
    {
        switch (key)
        {
            case nameof(Language): Language = ParseEnum(value, Language); break;
            case nameof(CheckUpdates): CheckUpdates = ParseBool(value, CheckUpdates); break;
            case nameof(Position): Position = ParseEnum(value, Position); break;
            case nameof(Monitor): Monitor = ParseEnum(value, Monitor); break;
            case nameof(MonitorDevice): MonitorDevice = value; break;
            case nameof(Margin): Margin = ParseInt(value, Margin, 0, 200); break;
            case nameof(DurationSeconds): DurationSeconds = ParseInt(value, DurationSeconds, 1, 60); break;
            case nameof(Material): Material = ParseEnum(value, Material); break;
            case nameof(TintOpacity): TintOpacity = ParseInt(value, TintOpacity, 0, 100); break;
            case nameof(SolidOpacity): SolidOpacity = ParseInt(value, SolidOpacity, 20, 100); break;
            case nameof(Theme): Theme = ParseEnum(value, Theme); break;
            case nameof(UseAccentColor): UseAccentColor = ParseBool(value, UseAccentColor); break;
            case nameof(Size): Size = ParseEnum(value, Size); break;
            case nameof(Animation): Animation = ParseEnum(value, Animation); break;
            case nameof(Speed): Speed = ParseEnum(value, Speed); break;
            case nameof(ShowLinkType): ShowLinkType = ParseBool(value, ShowLinkType); break;
            case nameof(NotifyConnect): NotifyConnect = ParseBool(value, NotifyConnect); break;
            case nameof(ShowDisconnections): ShowDisconnections = ParseBool(value, ShowDisconnections); break;
            case nameof(NotifyNewDevice): NotifyNewDevice = ParseBool(value, NotifyNewDevice); break;
            case nameof(NotifyLowBattery): NotifyLowBattery = ParseBool(value, NotifyLowBattery); break;
            case nameof(LowBatteryThreshold): LowBatteryThreshold = ParseInt(value, LowBatteryThreshold, 5, 50); break;
            case nameof(NotifyCharging): NotifyCharging = ParseBool(value, NotifyCharging); break;
            case nameof(NotifyChargeTarget): NotifyChargeTarget = ParseBool(value, NotifyChargeTarget); break;
            case nameof(ChargeTarget): ChargeTarget = ParseInt(value, ChargeTarget, 50, 100); break;
            case nameof(IgnoreStartupConnections): IgnoreStartupConnections = ParseBool(value, IgnoreStartupConnections); break;
            case nameof(IgnoreResumeConnections): IgnoreResumeConnections = ParseBool(value, IgnoreResumeConnections); break;
            case nameof(QuietInputSleep): QuietInputSleep = ParseBool(value, QuietInputSleep); break;
            case nameof(QuietMouseSleep): QuietMouseSleep = ParseBool(value, QuietMouseSleep); break;
            case nameof(ShowInFullscreen): ShowInFullscreen = ParseBool(value, ShowInFullscreen); break;
            case nameof(MutedDevices):
                MutedDevices.Clear();
                foreach (var k in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) MutedDevices.Add(k);
                break;
            case nameof(ClickAction): ClickAction = ParseEnum(value, ClickAction); break;
            case nameof(HoverList): HoverList = ParseBool(value, HoverList); break;
            case nameof(HoverShowDisconnected): HoverShowDisconnected = ParseBool(value, HoverShowDisconnected); break;
            case nameof(HotkeyEnabled): HotkeyEnabled = ParseBool(value, HotkeyEnabled); break;
            case nameof(HotkeyModifiers): HotkeyModifiers = ParseInt(value, HotkeyModifiers, 0, 15); break;
            case nameof(HotkeyKey): HotkeyKey = ParseInt(value, HotkeyKey, 0, 255); break;
            case nameof(ModuleBluetooth): ModuleBluetooth = ParseBool(value, ModuleBluetooth); break;
            case nameof(ModuleNowPlaying): ModuleNowPlaying = ParseBool(value, ModuleNowPlaying); break;
            case nameof(ModuleUsb): ModuleUsb = ParseBool(value, ModuleUsb); break;
            case nameof(ModulePower): ModulePower = ParseBool(value, ModulePower); break;
            case nameof(ModuleDisplays): ModuleDisplays = ParseBool(value, ModuleDisplays); break;
            case nameof(DisplayNotifyConnect): DisplayNotifyConnect = ParseBool(value, DisplayNotifyConnect); break;
            case nameof(DisplayNotifyDisconnect): DisplayNotifyDisconnect = ParseBool(value, DisplayNotifyDisconnect); break;
            case nameof(ModuleAudio): ModuleAudio = ParseBool(value, ModuleAudio); break;
            case nameof(AudioNotifyPlugged): AudioNotifyPlugged = ParseBool(value, AudioNotifyPlugged); break;
            case nameof(AudioNotifyUnplugged): AudioNotifyUnplugged = ParseBool(value, AudioNotifyUnplugged); break;
            case nameof(PowerNotifyPlugged): PowerNotifyPlugged = ParseBool(value, PowerNotifyPlugged); break;
            case nameof(PowerNotifyUnplugged): PowerNotifyUnplugged = ParseBool(value, PowerNotifyUnplugged); break;
            case nameof(PowerNotifyLevel): PowerNotifyLevel = ParseBool(value, PowerNotifyLevel); break;
            case nameof(PowerLevel): PowerLevel = ParseInt(value, PowerLevel, 50, 100); break;
            case nameof(PowerNotifyLow): PowerNotifyLow = ParseBool(value, PowerNotifyLow); break;
            case nameof(PowerLowLevel): PowerLowLevel = ParseInt(value, PowerLowLevel, 5, 50); break;
            case nameof(PowerShowInFullscreen): PowerShowInFullscreen = ParseBool(value, PowerShowInFullscreen); break;
            case nameof(UsbNotifyConnect): UsbNotifyConnect = ParseBool(value, UsbNotifyConnect); break;
            case nameof(UsbNotifyDisconnect): UsbNotifyDisconnect = ParseBool(value, UsbNotifyDisconnect); break;
            case nameof(UsbStorageOnly): UsbStorageOnly = ParseBool(value, UsbStorageOnly); break;
            case nameof(UsbIgnoreSafeRemoval): UsbIgnoreSafeRemoval = ParseBool(value, UsbIgnoreSafeRemoval); break;
            case nameof(UsbShowInFullscreen): UsbShowInFullscreen = ParseBool(value, UsbShowInFullscreen); break;
            case nameof(MediaNotifyTrackChange): MediaNotifyTrackChange = ParseBool(value, MediaNotifyTrackChange); break;
            case nameof(MediaNotifyOnResume): MediaNotifyOnResume = ParseBool(value, MediaNotifyOnResume); break;
            case nameof(MediaNotifyOnPause): MediaNotifyOnPause = ParseBool(value, MediaNotifyOnPause); break;
            case nameof(MediaHideWhenAppFocused): MediaHideWhenAppFocused = ParseBool(value, MediaHideWhenAppFocused); break;
            case nameof(MediaMusicOnly): MediaMusicOnly = ParseBool(value, MediaMusicOnly); break;
            case nameof(MediaPinned): MediaPinned = ParseBool(value, MediaPinned); break;
            case nameof(MediaPlayerVersion): MediaPlayerVersion = ParseInt(value, MediaPlayerVersion, 0, 100); break;
            case nameof(MediaPinnedPosition): MediaPinnedPosition = ParseEnum(value, MediaPinnedPosition); break;
            case nameof(MediaPinnedMonitor): MediaPinnedMonitor = ParseEnum(value, MediaPinnedMonitor); break;
            case nameof(MediaPinnedMonitorDevice): MediaPinnedMonitorDevice = value; break;
            case nameof(MediaShowProgress): MediaShowProgress = ParseBool(value, MediaShowProgress); break;
            case nameof(MediaSeekable): MediaSeekable = ParseBool(value, MediaSeekable); break;
            case nameof(MediaCoverColors): MediaCoverColors = ParseBool(value, MediaCoverColors); break;
            case nameof(MediaPinnedClickUntilHotkey): MediaPinnedClickUntilHotkey = ParseBool(value, MediaPinnedClickUntilHotkey); break;
            case nameof(MediaOpenAppOnClick): MediaOpenAppOnClick = ParseBool(value, MediaOpenAppOnClick); break;
            case nameof(MediaPinnedHotkeyEnabled): MediaPinnedHotkeyEnabled = ParseBool(value, MediaPinnedHotkeyEnabled); break;
            case nameof(MediaPinnedHotkeyModifiers): MediaPinnedHotkeyModifiers = ParseInt(value, MediaPinnedHotkeyModifiers, 0, 15); break;
            case nameof(MediaPinnedHotkeyKey): MediaPinnedHotkeyKey = ParseInt(value, MediaPinnedHotkeyKey, 0, 255); break;
            case nameof(MediaExcludedApps):
                MediaExcludedApps.Clear();
                foreach (var k in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) MediaExcludedApps.Add(k);
                break;
            case nameof(MediaAlwaysShownApps):
                MediaAlwaysShownApps.Clear();
                foreach (var k in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) MediaAlwaysShownApps.Add(k);
                break;
            case nameof(Bluetooth): Bluetooth = ParseBool(value, Bluetooth); break;
            case nameof(LogitechReceivers): LogitechReceivers = ParseBool(value, LogitechReceivers); break;
            case nameof(XboxControllers): XboxControllers = ParseBool(value, XboxControllers); break;
        }
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(result) ? result : fallback;

    private static int ParseInt(string value, int fallback, int min, int max)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, min, max) : fallback;

    private static bool ParseBool(string value, bool fallback)
        => bool.TryParse(value, out var b) ? b : fallback;
}
