using System.Collections.Concurrent;
using System.Diagnostics;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Modules;
using Hublot.Modules.Bluetooth;
using Hublot.Modules.NowPlaying;
using Hublot.Modules.Power;
using Hublot.Modules.Screens;
using Hublot.Modules.Usb;
using Hublot.Shell;

namespace Hublot;

/// <summary>
/// Hôte de Hublot : icône de notification, raccourci clavier, popup partagé, règles communes
/// (plein écran) et cycle de vie des modules selon les paramètres. Un seul thread UI ; les modules
/// et leurs sources (pool de threads) passent par <see cref="Post"/>.
/// </summary>
internal sealed class App : IHost
{
    private readonly TrayIcon _tray;
    private readonly Popup _popup;
    private readonly ConcurrentQueue<Action> _work = new();
    /// <summary>Ordre d'affichage dans les listes : la lecture en cours d'abord, puis les appareils, puis les stockages USB.</summary>
    private readonly Module[] _modules = [new NowPlayingModule(), new BluetoothModule(), new UsbModule(), new PowerModule(), new DisplayModule()];
    private readonly HashSet<Module> _running = [];
    private Settings _settings;
    private Popup? _pinned;
    private CardContent? _pinnedWanted;
    /// <summary>Sortie en fondu demandée par Hublot (plein écran), pas par un clic : la carte reste demandée.</summary>
    private bool _pinnedFading;
    private Timer? _restyle, _updates;

    public App()
    {
        _settings = Settings.Load();
        _tray = new TrayIcon();
        _popup = new Popup(_settings);

        _tray.WorkPending += DrainWork;
        _tray.ListRequested += ToggleList;
        _tray.HotkeyPressed += id =>
        {
            if (id == TrayIcon.ListHotkey) ToggleList();
            else if (Running.OfType<NowPlayingModule>().FirstOrDefault() is { } media) media.TogglePinned();
        };
        _tray.HoverStarted += OnTrayHover;
        _tray.HoverEnded += _popup.EndTrayHover;
        _tray.SettingsRequested += page => OpenCore(page);
        _tray.SettingsChanged += ReloadSettings;
        _tray.SettingsPreviewRequested += ShowPreview;
        _tray.PowerChanged += resumed =>
        {
            foreach (var module in _running) module.OnPowerChanged(resumed);
            Log.Write(resumed ? "Sortie de veille" : "Mise en veille");
        };
        _tray.FullscreenChanged += OnFullscreenChanged;
        _tray.DisplaysRequested += ShowDisplays;
        _tray.UpdateAvailable += version => Notify(new MessageCard("update", Glyphs.Download, L.T("Update.Available"),
            L.F("Update.AvailableDetail", version), () => OpenCore("About")));
        _tray.ThemeChanged += () =>
        {
            // Windows envoie le message avant d'avoir fini d'écrire le nouveau mode : on relit un peu après.
            _restyle ??= new Timer(_ => Post(() =>
            {
                _popup.Restyle();
                _pinned?.Restyle();
            }));
            _restyle.Change(300, Timeout.Infinite);
        };
        _tray.DisplayChanged += () =>
        {
            foreach (var module in _running) module.OnDisplayChanged();
        };
        _tray.ExitRequested += Exit;
        _popup.Hidden += () => _tray.ScheduleTrim(1500);
    }

    /// <summary>
    /// Version classique : recherche d'une nouvelle version sur GitHub 2 min après le lancement, puis chaque jour.
    /// Le travail réseau est fait par Hublot Core (--check-update, sans fenêtre) : le service reste sans HTTP.
    /// </summary>
    private void ScheduleUpdateChecks()
    {
        if (AppPackage.IsPackaged) return; // le Store s'en charge
        _updates = new Timer(_ => Post(() =>
        {
            if (!_settings.CheckUpdates) return;
            var exe = Path.Combine(AppContext.BaseDirectory, "HublotCore.exe");
            try
            {
                if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe, "--check-update") { UseShellExecute = false })?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Recherche de mise à jour", ex);
            }
        }), null, TimeSpan.FromMinutes(2), TimeSpan.FromDays(1));
    }

    public unsafe int Run(bool demo, bool demoList)
    {
        _tray.Add();
        ApplyHotkey();
        SyncModules();
        ScheduleUpdateChecks();
        _tray.ScheduleTrim(5000);
        if (demo) ShowPreview(0);
        if (demoList) ShowPreview(1);

        MSG msg;
        while (User32.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }
        return 0;
    }

    // ── IHost ──────────────────────────────────────────────────────────────────────────────

    public Settings Settings => _settings;

    public void Post(Action action)
    {
        _work.Enqueue(action);
        _tray.Wake();
    }

    private void DrainWork()
    {
        while (_work.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Log.Error("Tâche UI", ex); }
        }
    }

    /// <summary>
    /// Silence au lancement, quel que soit le module : les appareils, écrans et lectures déjà là ne sont pas
    /// des événements, et ceux qui se réveillent juste après l'ouverture de session (Bluetooth, USB, écrans
    /// DisplayPort) non plus. 60 s si Hublot démarre avec Windows (moins de 10 min après le démarrage du
    /// PC), sinon 10 s.
    /// </summary>
    private readonly long _quietUntil = Environment.TickCount64 + (Environment.TickCount64 < 600_000 ? 60_000 : 10_000);

    public void Notify(CardContent card)
    {
        if (Environment.TickCount64 < _quietUntil)
        {
            Log.Write($"Popup ignoré (lancement) : {card.Key}");
            return;
        }
        if (!_settings.ShowInFullscreen && !card.ShowOverFullscreen(_settings) && UserIsBusy())
        {
            Log.Write($"Popup ignoré (plein écran) : {card.Key}");
            return;
        }
        _popup.Show(card);
    }

    public void Notify(IReadOnlyList<CardContent> cards)
    {
        if (cards.Count <= 1)
        {
            if (cards.Count == 1) Notify(cards[0]);
            return;
        }
        if (Environment.TickCount64 < _quietUntil)
        {
            Log.Write($"Popups ignorés (lancement) : {cards.Count}");
            return;
        }
        if (!_settings.ShowInFullscreen && !cards.All(c => c.ShowOverFullscreen(_settings)) && UserIsBusy())
        {
            Log.Write($"Popups ignorés (plein écran) : {cards.Count}");
            return;
        }
        _popup.ShowList(cards, longer: false);
    }

    public void Update(CardContent card)
    {
        _popup.Update(card);
        _pinned?.Update(card);
    }

    /// <summary>
    /// Lecteur le temps d'une notification, à son emplacement (celui du lecteur permanent) ; mêmes règles que
    /// <see cref="Notify(CardContent)"/>. Sans effet si le lecteur permanent est demandé : c'est lui qui change.
    /// </summary>
    public void ShowPlayer(CardContent card)
    {
        if (IsPinned) return;
        if (Environment.TickCount64 < _quietUntil)
        {
            Log.Write($"Lecteur ignoré (lancement) : {card.Key}");
            return;
        }
        if (!_settings.ShowInFullscreen && !card.ShowOverFullscreen(_settings) && UserIsBusy())
        {
            Log.Write($"Lecteur ignoré (plein écran) : {card.Key}");
            return;
        }
        PinnedPopup().ShowPinned(card, temporary: true);
        UpdateReserve();
    }

    public CardContent? CurrentNotification => _popup.Notification;

    public void Invalidate()
    {
        _popup.Invalidate();
        _pinned?.Invalidate();
    }

    // ── Carte épinglée (mini-lecteur) ──────────────────────────────────────────────────────

    public bool IsPinned => _pinnedWanted is not null;

    public CardContent? PinnedCard => _pinned?.PinnedCard;

    public void Pin(CardContent? card)
    {
        _pinnedWanted = card;
        if (card is null)
        {
            _pinned?.Dismiss();
            _tray.WatchFullscreen(false);
            return;
        }
        var pinned = PinnedPopup();
        _tray.WatchFullscreen(true);
        if (!_settings.ShowInFullscreen && UserIsBusy())
        {
            if (pinned.PinnedCard is not null) Log.Write("Mini-lecteur masqué (plein écran)");
            FadePinned();
        }
        else
        {
            if (pinned.PinnedCard is null) Log.Write($"Mini-lecteur affiché : {card.Key}");
            _pinnedFading = false;
            pinned.ShowPinned(card);
        }
        UpdateReserve();
    }

    /// <summary>Popup du lecteur (créé au premier affichage), à l'emplacement et sur l'écran du lecteur.</summary>
    private Popup PinnedPopup()
    {
        if (_pinned is null)
        {
            _pinned = new Popup(_settings) { Draggable = true };
            _pinned.Dropped += OnPinnedDropped;
            _pinned.Hidden += () =>
            {
                if (_pinnedFading)
                {
                    _pinnedFading = false;
                    UpdateReserve();
                    return;
                }
                // Fermée par un clic : plus demandée jusqu'au prochain titre (Reset, pour le plein écran, ne passe pas ici),
                // ou jusqu'au raccourci (option). Retrait par Pin(null) : déjà plus demandée, rien à faire.
                bool clicked = _pinnedWanted is not null;
                _pinnedWanted = null;
                if (clicked && _settings is { MediaPinnedClickUntilHotkey: true, MediaPinnedHotkeyEnabled: true })
                    Running.OfType<NowPlayingModule>().FirstOrDefault()?.HideUntilHotkey();
                UpdateReserve();
                _tray.ScheduleTrim(1500);
            };
        }
        _pinned.PositionOverride = _settings.MediaPinnedPosition;
        _pinned.MonitorOverride = (_settings.MediaPinnedMonitor, _settings.MediaPinnedMonitorDevice);
        return _pinned;
    }

    /// <summary>
    /// Mini-lecteur déplacé à la souris : emplacement et écran enregistrés comme s'ils avaient été choisis dans
    /// les paramètres (écran principal, sinon cet écran précis), puis la carte s'y cale.
    /// </summary>
    private unsafe void OnPinnedDropped(PopupPosition position, nint monitor)
    {
        if (_pinned is null) return;
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(monitor, &info);
        var (kind, device) = (PopupMonitor.Primary, "");
        var (left, top) = (info.rcMonitor.Left, info.rcMonitor.Top);
        if ((info.dwFlags & User32.MONITORINFOF_PRIMARY) == 0
            && Displays.Active().FirstOrDefault(d => d.Left == left && d.Top == top) is { } display)
            (kind, device) = (PopupMonitor.Specific, display.Path);

        _settings.MediaPinnedPosition = position;
        _settings.MediaPinnedMonitor = kind;
        _settings.MediaPinnedMonitorDevice = device;
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Enregistrement de l'emplacement du mini-lecteur", ex);
        }
        _pinned.PositionOverride = position;
        _pinned.MonitorOverride = (kind, device);
        _pinned.Settle();
        UpdateReserve();
        Log.Write($"Mini-lecteur déplacé : {position} ({(kind == PopupMonitor.Primary ? "écran principal" : device)})");
    }

    /// <summary>Plein écran (notification du shell) : la carte épinglée s'efface, puis revient.</summary>
    private void OnFullscreenChanged(bool fullscreen)
    {
        if (_pinned is null || _settings.ShowInFullscreen) return;
        if (fullscreen)
        {
            FadePinned();
        }
        else if (_pinnedWanted is { } card)
        {
            _pinnedFading = false;
            _pinned.ShowPinned(card);
        }
        UpdateReserve();
    }

    /// <summary>
    /// Plein écran : la carte épinglée sort en fondu (la sortie habituelle des popups, ~220 ms, quelques
    /// images seulement) au lieu de disparaître d'un coup ; elle reste demandée et revient ensuite.
    /// </summary>
    private void FadePinned()
    {
        if (_pinned?.PinnedCard is null) return;
        _pinnedFading = true;
        _pinned.Dismiss();
    }

    /// <summary>Carte épinglée au même emplacement que les popups : ceux-ci s'empilent au-dessus.</summary>
    private void UpdateReserve()
        => _popup.ReservedDip = _pinned?.PinnedCard is { } card && _settings.MediaPinnedPosition == _settings.Position && SameScreen() ? card.Height + 8 : 0;

    /// <summary>Popups et carte épinglée sur le même écran (écran de la souris : inconnu d'avance, considéré différent).</summary>
    private bool SameScreen() => _settings.Monitor == _settings.MediaPinnedMonitor
        && (_settings.Monitor != PopupMonitor.Specific || _settings.MonitorDevice == _settings.MediaPinnedMonitorDevice);

    public void OpenCore(string? page = null)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "HublotCore.exe");
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = true };
            if (page is not null) info.Arguments = $"--page {page}";
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Ouverture des paramètres", ex);
        }
    }

    public void OpenUri(string uri) => Shell32.ShellExecuteW(0, "open", uri, null, null, 1);

    // ── Modules ────────────────────────────────────────────────────────────────────────────

    /// <summary>Démarre / arrête les modules selon les paramètres ; les modules actifs reçoivent les nouveaux réglages.</summary>
    private void SyncModules()
    {
        foreach (var module in _modules)
        {
            bool enabled = module.IsEnabled(_settings);
            if (enabled && _running.Add(module))
            {
                try
                {
                    module.Start(this);
                    Log.Write($"Module démarré : {module.Name}");
                }
                catch (Exception ex)
                {
                    _running.Remove(module);
                    Log.Error($"Module {module.Name}", ex);
                }
            }
            else if (!enabled && _running.Remove(module))
            {
                module.Stop();
                Log.Write($"Module arrêté : {module.Name}");
            }
            else if (enabled)
            {
                module.ApplySettings(_settings);
            }
        }
    }

    private IEnumerable<Module> Running => _modules.Where(_running.Contains);

    /// <summary>Raccourci clavier ou clic sur l'icône : une carte par élément, ou fermeture si la liste est ouverte.</summary>
    private void ToggleList()
    {
        if (_popup.Mode == PopupMode.List)
        {
            _popup.Dismiss();
            return;
        }
        var cards = Running.SelectMany(m => m.ListCards()).ToList();
        if (cards.Count == 0) cards.Add(new MessageCard("none", Glyphs.Bluetooth, L.T("Message.Nothing"), L.T("Message.NothingList")));
        _popup.ShowList(cards);
    }

    /// <summary>Survol de l'icône : une carte réunissant les lignes de tous les modules.</summary>
    private void OnTrayHover()
    {
        if (!_settings.HoverList || _popup.Mode == PopupMode.List || _tray.GetIconRect() is not RECT icon) return;
        var rows = Running.SelectMany(m => m.HoverRows()).ToList();
        if (rows.Count == 0) rows.Add(new MessageRow(Glyphs.Bluetooth, L.T("Message.Nothing"), L.T("Message.NothingHover")));
        _popup.ShowHover(new HoverCard(rows, () => OpenCore()), icon);
    }

    /// <summary>Aperçu demandé par Hublot Core : 0 = carte d'appareil, 1 = liste, 2 = lecture en cours, 3 = stockage USB, 4 = alimentation.</summary>
    private void ShowPreview(int kind)
    {
        ReloadSettings();
        _popup.Reset();
        var bluetooth = _modules.OfType<BluetoothModule>().First().PreviewCards().ToList();
        var media = _modules.OfType<NowPlayingModule>().First().PreviewCards().ToList();
        var usb = _modules.OfType<UsbModule>().First().PreviewCards().ToList();
        var power = _modules.OfType<PowerModule>().First().PreviewCards().ToList();
        switch (kind)
        {
            case 1: _popup.ShowList([.. media, .. bluetooth, .. usb]); break;
            // Le lecteur à son emplacement ; lecteur permanent affiché : l'aperçu passe par les popups, sans le remplacer.
            case 2:
                if (IsPinned) _popup.Show(media[0]);
                else PinnedPopup().ShowPinned(media[0], temporary: true);
                UpdateReserve();
                break;
            case 3: _popup.Show(usb[0]); break;
            case 4: _popup.Show(power[0]); break;
            case 5: ShowDisplays(); break;
            default: _popup.Show(bluetooth[0]); break;
        }
    }

    /// <summary>Une carte par écran réellement connecté (menu de l'icône, aperçu de la page Écrans), durée normale.</summary>
    private void ShowDisplays()
    {
        var cards = _modules.OfType<DisplayModule>().First().PreviewCards().ToList();
        _popup.Reset();
        if (cards.Count == 1) _popup.Show(cards[0]);
        else _popup.ShowList(cards, longer: false);
    }

    private void ReloadSettings()
    {
        var previous = _settings;
        _settings = Settings.Load();
        _popup.ApplySettings(_settings);
        if (_pinned is not null)
        {
            _pinned.ApplySettings(_settings);
            // Emplacement ou apparence modifiés : la carte épinglée est reposée. Pour tout autre réglage, elle
            // reste en place, sans rejouer son entrée.
            if (PinnedLayoutChanged(previous, _settings))
            {
                _pinned.Reset();
                _pinned.PositionOverride = _settings.MediaPinnedPosition;
                _pinned.MonitorOverride = (_settings.MediaPinnedMonitor, _settings.MediaPinnedMonitorDevice);
                if (_pinnedWanted is { } card && (_settings.ShowInFullscreen || !UserIsBusy())) _pinned.ShowPinned(card);
            }
            else if (ColorsChanged(previous, _settings))
            {
                _pinned.Restyle();
            }
            UpdateReserve();
        }
        ApplyHotkey();
        SyncModules();
        Log.Write("Paramètres rechargés");
    }

    /// <summary>Réglages qui changent la place ou le matériau de la carte épinglée : elle est reposée.</summary>
    private static bool PinnedLayoutChanged(Settings a, Settings b) =>
        a.MediaPinnedPosition != b.MediaPinnedPosition || a.MediaPinnedMonitor != b.MediaPinnedMonitor
        || a.MediaPinnedMonitorDevice != b.MediaPinnedMonitorDevice || a.Margin != b.Margin
        || a.Material != b.Material || a.Size != b.Size;

    /// <summary>Réglages de couleur seulement : la carte est repeinte sur place.</summary>
    private static bool ColorsChanged(Settings a, Settings b) =>
        a.Theme != b.Theme || a.UseAccentColor != b.UseAccentColor || a.TintOpacity != b.TintOpacity || a.SolidOpacity != b.SolidOpacity
        || a.MediaCoverColors != b.MediaCoverColors;

    private void ApplyHotkey()
    {
        if (!_tray.SetHotkey(TrayIcon.ListHotkey, _settings.HotkeyEnabled, _settings.HotkeyModifiers, _settings.HotkeyKey))
            Log.Write($"Raccourci indisponible (déjà utilisé ?) : modificateurs {_settings.HotkeyModifiers}, touche 0x{_settings.HotkeyKey:X2}");
        bool pinned = _settings.ModuleNowPlaying && _settings.MediaPinnedHotkeyEnabled;
        if (!_tray.SetHotkey(TrayIcon.PinnedHotkey, pinned, _settings.MediaPinnedHotkeyModifiers, _settings.MediaPinnedHotkeyKey))
            Log.Write($"Raccourci du mini-lecteur indisponible (déjà utilisé ?) : modificateurs {_settings.MediaPinnedHotkeyModifiers}, touche 0x{_settings.MediaPinnedHotkeyKey:X2}");
    }

    /// <summary>
    /// Application en plein écran (jeu, vidéo, navigateur en F11), présentation ou écran verrouillé :
    /// état signalé par le shell, complété par la taille de la fenêtre au premier plan.
    /// </summary>
    private static unsafe bool UserIsBusy()
    {
        int state;
        if (Shell32.SHQueryUserNotificationState(&state) == 0
            && state is Shell32.QUNS_NOT_PRESENT or Shell32.QUNS_BUSY or Shell32.QUNS_RUNNING_D3D_FULL_SCREEN or Shell32.QUNS_PRESENTATION_MODE)
            return true;

        var window = User32.GetForegroundWindow();
        if (window == 0 || window == User32.GetShellWindow() || window == User32.GetDesktopWindow()) return false;
        var name = stackalloc char[64];
        int length = User32.GetClassNameW(window, name, 64);
        var cls = new string(name, 0, length);
        if (cls is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;

        RECT rect;
        User32.GetWindowRect(window, &rect);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromWindow(window, User32.MONITOR_DEFAULTTONEAREST), &info);
        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    private void Exit()
    {
        foreach (var module in _running) module.Stop();
        _running.Clear();
        _tray.Remove();
        _tray.DestroyHandle();
        User32.PostQuitMessage(0);
    }
}
