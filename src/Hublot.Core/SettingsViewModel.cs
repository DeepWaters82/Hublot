using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Hublot.Common;

namespace HublotCore;

/// <summary>
/// Chaque modification est enregistrée immédiatement (comme dans PowerToys) puis signalée au service.
/// Les réglages d'apparence déclenchent en plus un popup d'aperçu, regroupé après une courte pause.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    /// <summary>Capture de la page GitHub : réglages par défaut, langue inchangée, et rien n'est jamais enregistré.</summary>
    public static bool Demo { get; set; }

    private readonly Settings _s = Demo ? new Settings() : Settings.Load();
    private readonly DispatcherTimer _preview = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public SettingsViewModel()
    {
        _preview.Tick += (_, _) =>
        {
            _preview.Stop();
            Native.RequestPreview(_previewKind);
        };
        RefreshDevices();
        RefreshMediaApps();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Acrylique et Mica exigent Windows 11 22H2 ; sinon le service utilise le fond uni.</summary>
    public static bool SystemMaterialsSupported => Environment.OSVersion.Version.Build >= 22621;

    // ── Général ──
    public bool StartWithWindows
    {
        get => Native.StartupEnabled;
        set
        {
            try { Native.SetStartup(value); }
            catch (Exception) { }
            OnPropertyChanged();
        }
    }

    public int DurationSeconds { get => _s.DurationSeconds; set => Set(() => _s.DurationSeconds = value, preview: false); }
    public int ClickActionIndex { get => (int)_s.ClickAction; set => Set(() => _s.ClickAction = (ClickAction)value, preview: false); }
    /// <summary>Nouvelle langue : enregistrée (le service bascule aussitôt), puis les paramètres se rouvrent.</summary>
    public event Action? LanguageChanged;

    public int LanguageIndex
    {
        get => (int)_s.Language;
        set
        {
            if (value == (int)_s.Language || value < 0) return;
            Set(() => _s.Language = (AppLanguage)value, preview: false);
            LanguageChanged?.Invoke();
        }
    }

    public bool CheckUpdates { get => _s.CheckUpdates; set => Set(() => _s.CheckUpdates = value, preview: false); }
    public bool HoverList { get => _s.HoverList; set => Set(() => _s.HoverList = value, preview: false); }
    public bool HoverShowDisconnected { get => _s.HoverShowDisconnected; set => Set(() => _s.HoverShowDisconnected = value, preview: false); }
    public bool HotkeyEnabled { get => _s.HotkeyEnabled; set => Set(() => _s.HotkeyEnabled = value, preview: false); }

    public string HotkeyText => Hotkeys.Describe(_s.HotkeyModifiers, _s.HotkeyKey);

    public void SetHotkey(int modifiers, int key)
    {
        Set(() =>
        {
            _s.HotkeyModifiers = modifiers;
            _s.HotkeyKey = key;
        }, preview: false);
        OnPropertyChanged(nameof(HotkeyText));
    }

    public bool MediaPinnedHotkeyEnabled { get => _s.MediaPinnedHotkeyEnabled; set => Set(() => _s.MediaPinnedHotkeyEnabled = value, preview: false); }

    public string MediaPinnedHotkeyText => Hotkeys.Describe(_s.MediaPinnedHotkeyModifiers, _s.MediaPinnedHotkeyKey);

    public void SetPinnedHotkey(int modifiers, int key)
    {
        Set(() =>
        {
            _s.MediaPinnedHotkeyModifiers = modifiers;
            _s.MediaPinnedHotkeyKey = key;
        }, preview: false);
        OnPropertyChanged(nameof(MediaPinnedHotkeyText));
    }

    // ── Notifications ──
    public bool NotifyConnect { get => _s.NotifyConnect; set => Set(() => _s.NotifyConnect = value, preview: false); }
    public bool ShowDisconnections { get => _s.ShowDisconnections; set => Set(() => _s.ShowDisconnections = value, preview: false); }
    public bool NotifyNewDevice { get => _s.NotifyNewDevice; set => Set(() => _s.NotifyNewDevice = value, preview: false); }
    public bool NotifyLowBattery { get => _s.NotifyLowBattery; set => Set(() => _s.NotifyLowBattery = value, preview: false); }
    public int LowBatteryThreshold { get => _s.LowBatteryThreshold; set => Set(() => _s.LowBatteryThreshold = value, preview: false); }
    public bool NotifyCharging { get => _s.NotifyCharging; set => Set(() => _s.NotifyCharging = value, preview: false); }
    public bool NotifyChargeTarget { get => _s.NotifyChargeTarget; set => Set(() => _s.NotifyChargeTarget = value, preview: false); }
    public int ChargeTarget { get => _s.ChargeTarget; set => Set(() => _s.ChargeTarget = value, preview: false); }
    public bool IgnoreStartupConnections { get => _s.IgnoreStartupConnections; set => Set(() => _s.IgnoreStartupConnections = value, preview: false); }
    public bool IgnoreResumeConnections { get => _s.IgnoreResumeConnections; set => Set(() => _s.IgnoreResumeConnections = value, preview: false); }
    /// <summary>Présenté à l'endroit : « Ne pas afficher pendant une application en plein écran ».</summary>
    public bool HideInFullscreen { get => !_s.ShowInFullscreen; set => Set(() => _s.ShowInFullscreen = !value, preview: false); }

    // ── Apparence ──
    public PopupPosition Position { get => _s.Position; set => Set(() => _s.Position = value); }
    // ── Écrans : écrans actifs à l'ouverture de la fenêtre, désignés comme dans « Identifier » ──
    private readonly List<DisplayInfo> _displays = Displays.Active();

    /// <summary>« Écran 2 · DELL U2720Q · 2560 × 1440 » (numéro = celui de Windows, DISPLAY2).</summary>
    public static string DisplayLabel(DisplayInfo d, int index) => L.F("Display.Label", DisplayNumber(d, index), d.Name, d.Width, d.Height);

    public static int DisplayNumber(DisplayInfo d, int index)
    {
        var digits = new string(d.GdiName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out int n) ? n : index + 1;
    }

    public IReadOnlyList<DisplayInfo> ActiveDisplays => _displays;

    public List<string> MonitorChoices =>
        [L.T("Display.Primary"), L.T("Display.Cursor"), .. _displays.Select((d, i) => DisplayLabel(d, i))];

    public int MonitorChoiceIndex
    {
        get => _s.Monitor switch
        {
            PopupMonitor.Cursor => 1,
            PopupMonitor.Specific when _displays.FindIndex(d => d.Path == _s.MonitorDevice) is int i and >= 0 => i + 2,
            _ => 0,
        };
        set => Set(() =>
        {
            if (value >= 2 && value - 2 < _displays.Count)
            {
                _s.Monitor = PopupMonitor.Specific;
                _s.MonitorDevice = _displays[value - 2].Path;
            }
            else
            {
                _s.Monitor = value == 1 ? PopupMonitor.Cursor : PopupMonitor.Primary;
            }
        });
    }

    /// <summary>
    /// Réglages écrits par le service, pas par ces paramètres : emplacement et écran du lecteur (déplacé à la
    /// souris), lecteur permanent (raccourci clavier). Relus juste avant chaque enregistrement, pour ne pas
    /// écraser un changement fait pendant que la fenêtre est ouverte.
    /// </summary>
    private void SyncServiceOwned()
    {
        Settings disk;
        try { disk = Settings.Load(); }
        catch (Exception) { return; }
        _s.MediaPinned = disk.MediaPinned;
        _s.MediaPinnedPosition = disk.MediaPinnedPosition;
        _s.MediaPinnedMonitor = disk.MediaPinnedMonitor;
        _s.MediaPinnedMonitorDevice = disk.MediaPinnedMonitorDevice;
    }
    public int Margin { get => _s.Margin; set => Set(() => _s.Margin = value); }

    public int MaterialIndex
    {
        get => (int)_s.Material;
        set
        {
            Set(() => _s.Material = (PopupMaterial)value);
            OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(OpacityHeader));
            OnPropertyChanged(nameof(OpacityDescription));
        }
    }

    /// <summary>Un seul curseur : teinte du matériau (Acrylique, Mica) ou opacité du fond (Uni).</summary>
    public int Opacity
    {
        get => _s.Material == PopupMaterial.Solid ? _s.SolidOpacity : _s.TintOpacity;
        set => Set(() =>
        {
            if (_s.Material == PopupMaterial.Solid) _s.SolidOpacity = Math.Max(20, value);
            else _s.TintOpacity = value;
        });
    }

    public string OpacityHeader => L.T(_s.Material == PopupMaterial.Solid ? "Core.BackgroundOpacity" : "Core.TintIntensity");

    public string OpacityDescription => L.T(_s.Material == PopupMaterial.Solid ? "Core.OpacityDescription" : "Core.TintDescription");

    public int ThemeIndex { get => (int)_s.Theme; set => Set(() => _s.Theme = (PopupTheme)value); }
    public bool UseAccentColor { get => _s.UseAccentColor; set => Set(() => _s.UseAccentColor = value); }
    public bool ShowLinkType { get => _s.ShowLinkType; set => Set(() => _s.ShowLinkType = value); }
    public int SizeIndex { get => (int)_s.Size; set => Set(() => _s.Size = (PopupSize)value); }
    public int AnimationIndex { get => (int)_s.Animation; set => Set(() => _s.Animation = (PopupAnimation)value); }
    public int SpeedIndex { get => (int)_s.Speed; set => Set(() => _s.Speed = (AnimationSpeed)value); }

    // ── Modules ──
    public bool ModuleBluetooth { get => _s.ModuleBluetooth; set => Set(() => _s.ModuleBluetooth = value, preview: false); }
    public bool ModuleNowPlaying { get => _s.ModuleNowPlaying; set => Set(() => _s.ModuleNowPlaying = value, preview: false); }
    public bool ModuleUsb { get => _s.ModuleUsb; set => Set(() => _s.ModuleUsb = value, preview: false); }
    public bool ModuleDisplays { get => _s.ModuleDisplays; set => Set(() => _s.ModuleDisplays = value, preview: false); }
    public bool DisplayNotifyConnect { get => _s.DisplayNotifyConnect; set => Set(() => _s.DisplayNotifyConnect = value, preview: false); }
    public bool DisplayNotifyDisconnect { get => _s.DisplayNotifyDisconnect; set => Set(() => _s.DisplayNotifyDisconnect = value, preview: false); }
    public bool QuietInputSleep { get => _s.QuietInputSleep; set => Set(() => _s.QuietInputSleep = value, preview: false); }
    public bool QuietMouseSleep { get => _s.QuietMouseSleep; set => Set(() => _s.QuietMouseSleep = value, preview: false); }
    public bool DiagnosticLog { get => _s.DiagnosticLog; set => Set(() => _s.DiagnosticLog = value, preview: false); }
    public bool ModulePower { get => _s.ModulePower; set => Set(() => _s.ModulePower = value, preview: false); }
    public bool ModuleAudio { get => _s.ModuleAudio; set => Set(() => _s.ModuleAudio = value, preview: false); }
    public bool AudioNotifyPlugged { get => _s.AudioNotifyPlugged; set => Set(() => _s.AudioNotifyPlugged = value, preview: false); }
    public bool AudioNotifyUnplugged { get => _s.AudioNotifyUnplugged; set => Set(() => _s.AudioNotifyUnplugged = value, preview: false); }
    public bool PowerNotifyPlugged { get => _s.PowerNotifyPlugged; set => Set(() => _s.PowerNotifyPlugged = value, preview: false); }
    public bool PowerNotifyUnplugged { get => _s.PowerNotifyUnplugged; set => Set(() => _s.PowerNotifyUnplugged = value, preview: false); }
    public bool PowerShowInFullscreen { get => _s.PowerShowInFullscreen; set => Set(() => _s.PowerShowInFullscreen = value, preview: false); }
    public bool PowerNotifyLevel { get => _s.PowerNotifyLevel; set => Set(() => _s.PowerNotifyLevel = value, preview: false); }
    public int PowerLevel { get => _s.PowerLevel; set => Set(() => _s.PowerLevel = value, preview: false); }
    public bool PowerNotifyLow { get => _s.PowerNotifyLow; set => Set(() => _s.PowerNotifyLow = value, preview: false); }
    public int PowerLowLevel { get => _s.PowerLowLevel; set => Set(() => _s.PowerLowLevel = value, preview: false); }

    // ── Périphériques USB ──
    public bool UsbNotifyConnect { get => _s.UsbNotifyConnect; set => Set(() => _s.UsbNotifyConnect = value, preview: false); }
    public bool UsbNotifyDisconnect { get => _s.UsbNotifyDisconnect; set => Set(() => _s.UsbNotifyDisconnect = value, preview: false); }
    public bool UsbIgnoreSafeRemoval { get => _s.UsbIgnoreSafeRemoval; set => Set(() => _s.UsbIgnoreSafeRemoval = value, preview: false); }
    public bool UsbShowInFullscreen { get => _s.UsbShowInFullscreen; set => Set(() => _s.UsbShowInFullscreen = value, preview: false); }
    public bool UsbStorageOnly { get => _s.UsbStorageOnly; set => Set(() => _s.UsbStorageOnly = value, preview: false); }

    // ── Lecture en cours ──
    public bool MediaNotifyTrackChange { get => _s.MediaNotifyTrackChange; set => Set(() => _s.MediaNotifyTrackChange = value, preview: false); }
    public bool MediaNotifyOnResume { get => _s.MediaNotifyOnResume; set => Set(() => _s.MediaNotifyOnResume = value, preview: false); }
    public bool MediaNotifyOnPause { get => _s.MediaNotifyOnPause; set => Set(() => _s.MediaNotifyOnPause = value, preview: false); }
    public bool MediaHideWhenAppFocused { get => _s.MediaHideWhenAppFocused; set => Set(() => _s.MediaHideWhenAppFocused = value, preview: false); }
    public bool MediaSeekable { get => _s.MediaSeekable; set => Set(() => _s.MediaSeekable = value, preview: false); }
    public bool MediaShowProgress { get => _s.MediaShowProgress; set => Set(() => _s.MediaShowProgress = value, preview: false); }
    public bool MediaCoverColors { get => _s.MediaCoverColors; set => Set(() => _s.MediaCoverColors = value, preview: false); }
    public bool MediaPinnedClickUntilHotkey { get => _s.MediaPinnedClickUntilHotkey; set => Set(() => _s.MediaPinnedClickUntilHotkey = value, preview: false); }
    public bool MediaOpenAppOnClick { get => _s.MediaOpenAppOnClick; set => Set(() => _s.MediaOpenAppOnClick = value, preview: false); }
    public bool MediaMusicOnly { get => _s.MediaMusicOnly; set => Set(() => _s.MediaMusicOnly = value, preview: false); }

    public ObservableCollection<MediaAppItem> MediaApps { get; } = [];
    public bool HasMediaApps => MediaApps.Count > 0;

    public void RefreshMediaApps()
    {
        MediaApps.Clear();
        foreach (var app in MediaStore.LoadApps().OrderByDescending(a => a.LastSeen))
            MediaApps.Add(new MediaAppItem(app,
                _s.MediaExcludedApps.Contains(app.Id) ? 2 : _s.MediaAlwaysShownApps.Contains(app.Id) ? 1 : 0, SetMediaAppMode));
        OnPropertyChanged(nameof(HasMediaApps));
    }

    /// <summary>Applications d'exemple (capture de la page GitHub) : rien de ce que l'utilisateur a écouté.</summary>
    public void UseSampleMediaApps()
    {
        MediaApps.Clear();
        var now = DateTime.Now;
        MediaApps.Add(new MediaAppItem(new MediaApp("Spotify.exe", "Spotify", now.AddMinutes(-3)), 0, (_, _) => { }));
        MediaApps.Add(new MediaAppItem(new MediaApp("YouTubeMusic", "YouTube Music", now.AddHours(-2)), 0, (_, _) => { }));
        MediaApps.Add(new MediaAppItem(new MediaApp("Chrome", "Google Chrome", now.AddDays(-1)), 1, (_, _) => { }));
        OnPropertyChanged(nameof(HasMediaApps));
    }

    private void SetMediaAppMode(string id, int mode) => Set(() =>
    {
        _s.MediaExcludedApps.Remove(id);
        _s.MediaAlwaysShownApps.Remove(id);
        if (mode == 1) _s.MediaAlwaysShownApps.Add(id);
        if (mode == 2) _s.MediaExcludedApps.Add(id);
    }, preview: false);

    // ── Appareils ──
    public bool Bluetooth { get => _s.Bluetooth; set => Set(() => _s.Bluetooth = value, preview: false); }
    public bool LogitechReceivers { get => _s.LogitechReceivers; set => Set(() => _s.LogitechReceivers = value, preview: false); }
    public bool XboxControllers { get => _s.XboxControllers; set => Set(() => _s.XboxControllers = value, preview: false); }

    public ObservableCollection<DeviceItem> BluetoothDevices { get; } = [];
    public ObservableCollection<DeviceItem> DongleDevices { get; } = [];

    public void RefreshDevices()
    {
        BluetoothDevices.Clear();
        DongleDevices.Clear();
        foreach (var d in DeviceStore.LoadDevices().OrderByDescending(d => d.Connected).ThenByDescending(d => d.LastSeen))
        {
            var item = new DeviceItem(d, !_s.MutedDevices.Contains(d.Key), SetMuted);
            (d.Link != DeviceLink.Bluetooth ? DongleDevices : BluetoothDevices).Add(item);
        }
        OnPropertyChanged(nameof(HasBluetoothDevices));
        OnPropertyChanged(nameof(HasDongleDevices));
    }

    public bool HasBluetoothDevices => BluetoothDevices.Count > 0;
    public bool HasDongleDevices => DongleDevices.Count > 0;

    private void SetMuted(string key, bool notify)
    {
        Set(() =>
        {
            if (notify) _s.MutedDevices.Remove(key);
            else _s.MutedDevices.Add(key);
        }, preview: false);
    }

    public void Preview(int kind) => Native.RequestPreview(kind);

    /// <summary>Aperçu automatique après un réglage d'apparence : celui de la page affichée.</summary>
    private int _previewKind;
    public void SetPreviewKind(int kind) => _previewKind = kind;

    private void Set(Action apply, bool preview = true, [CallerMemberName] string? name = null)
    {
        if (Demo) return;
        SyncServiceOwned();
        apply();
        try { _s.Save(); }
        catch (Exception) { return; }
        Native.NotifySettingsChanged();
        OnPropertyChanged(name);
        if (preview)
        {
            _preview.Stop();
            _preview.Start();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
