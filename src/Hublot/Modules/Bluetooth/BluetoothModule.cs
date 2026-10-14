using Hublot.Common;
using Hublot.Modules.Bluetooth.Logitech;
using Hublot.Shell;

namespace Hublot.Modules.Bluetooth;

/// <summary>
/// Module « Appareils sans fil » : Bluetooth, récepteurs Logitech, manettes Xbox.
/// Sources d'appareils → règles (connexion, batterie, sourdine, démarrage / veille) → cartes.
/// État sur le thread UI ; les sources (pool de threads) passent par <see cref="IHost.Post"/>.
/// </summary>
internal sealed class BluetoothModule : Module
{
    private const long DuplicateWindowMs = 4000;
    /// <summary>Lancement à l'ouverture de session : les appareils se reconnectent dans la minute qui suit.</summary>
    private const long StartupGraceMs = 60_000, StartupUptimeMs = 5 * 60_000, ResumeGraceMs = 45_000;

    private enum NotifyKind { Connect, NewDevice, Disconnect, LowBattery, Charging, ChargeReached }

    /// <summary>Suivi de la batterie d'un appareil connecté, pour les notifications de seuil et de charge.</summary>
    private sealed class BatteryWatch
    {
        public int? Last;
        public int Rises;
        public bool Charging, LowNotified, TargetNotified;
    }

    private readonly Dictionary<string, DeviceSource> _sources = [];
    private readonly Dictionary<string, BatteryWatch> _batteries = [];
    /// <summary>Claviers et souris déconnectés, sans doute en veille : depuis quand (TickCount64).</summary>
    private readonly Dictionary<string, long> _asleep = [];
    private DeviceRegistry? _registry;
    private IHost? _host;
    private (string Key, string Name, bool Connected, long At) _lastEvent;
    private long _quietUntil;
    private bool _suspended, _seeded;
    private Timer? _saveTimer, _seedTimer;

    public override string Id => "bluetooth";
    public override string Name => "Appareils sans fil";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");
    private Settings Settings => Host.Settings;

    public override bool IsEnabled(Settings settings) => settings.ModuleBluetooth;

    public override void Start(IHost host)
    {
        _host = host;
        _registry = new DeviceRegistry();
        if (Settings.IgnoreStartupConnections && Environment.TickCount64 < StartupUptimeMs)
        {
            _quietUntil = Environment.TickCount64 + StartupGraceMs;
            Log.Write("Lancement à l'ouverture de session : connexions ignorées pendant 60 s");
        }
        SyncSources();
        // Les appareils déjà appairés ne sont pas « nouveaux » : registre amorcé une fois les sources énumérées.
        _seedTimer = new Timer(_ => Host.Post(SeedRegistry), null, 4000, Timeout.Infinite);
    }

    public override void Stop()
    {
        foreach (var source in _sources.Values) source.Stop();
        _sources.Clear();
        _batteries.Clear();
        _seedTimer?.Dispose();
        _saveTimer?.Dispose();
        _seedTimer = _saveTimer = null;
        _registry?.Save();
        _registry = null;
        _host = null;
        _seeded = false;
    }

    public override void ApplySettings(Settings settings) => SyncSources();

    /// <summary>Veille : les appareils se déconnectent puis se reconnectent en rafale, sans intérêt.</summary>
    public override void OnPowerChanged(bool resumed)
    {
        if (!Settings.IgnoreResumeConnections) return;
        _suspended = !resumed;
        if (resumed) _quietUntil = Math.Max(_quietUntil, Environment.TickCount64 + ResumeGraceMs);
    }

    // ── Sources ────────────────────────────────────────────────────────────────────────────

    private void SyncSources()
    {
        Toggle("bt", Settings.Bluetooth, () => new BluetoothSource());
        Toggle("logi", Settings.LogitechReceivers, () => new LogitechSource());
        Toggle("pad", Settings.XboxControllers, () => new XboxSource());
    }

    private void Toggle(string id, bool enabled, Func<DeviceSource> create)
    {
        if (enabled && !_sources.ContainsKey(id))
        {
            var source = create();
            source.Changed += (state, connection) => _host?.Post(() => OnDeviceChanged(state, connection));
            try
            {
                source.Start();
                _sources[id] = source;
                Log.Write($"Source démarrée : {source.Name}");
            }
            catch (Exception ex)
            {
                Log.Error($"Source {source.Name}", ex);
            }
        }
        else if (!enabled && _sources.Remove(id, out var running))
        {
            running.Stop();
            Log.Write($"Source arrêtée : {running.Name}");
        }
    }

    private void SeedRegistry()
    {
        if (_registry is null) return;
        foreach (var source in _sources.Values)
        foreach (var state in source.KnownDevices())
            _registry.Observe(state);
        _seeded = true;
        _registry.Save();
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new Timer(_ => _host?.Post(() => _registry?.Save()));
        _saveTimer.Change(2000, Timeout.Infinite);
    }

    /// <summary>Type mémorisé si la source ne l'a pas encore résolu (lecture WinRT en cours).</summary>
    private DeviceState Enrich(DeviceState state)
        => state.Kind is DeviceKind.Generic or DeviceKind.Dongle && _registry?.KindOf(state.Key) is { } kind && kind != DeviceKind.Generic
            ? state with { Kind = kind }
            : state;

    private DeviceCard Card(DeviceState state) => new(state, OnCardClicked);

    private void OnCardClicked()
    {
        if (_host is not null && Settings.ClickAction == ClickAction.OpenBluetoothSettings) Host.OpenUri("ms-settings:bluetooth");
    }

    // ── Événements d'appareils ─────────────────────────────────────────────────────────────

    private void OnDeviceChanged(DeviceState state, bool connectionChanged)
    {
        if (_registry is null) return;
        state = Enrich(state);
        bool isNew = _registry.Observe(state) && _seeded;
        ScheduleSave();

        if (!connectionChanged)
        {
            Host.Update(Card(state));
            EvaluateBattery(state);
            return;
        }

        // Doublons : appareil bimode (deux nœuds Bluetooth), manette vue à la fois en Bluetooth
        // et par Windows.Gaming.Input… même nom ou même clé, même état, à quelques secondes d'écart.
        if (Host.CurrentNotification is DeviceCard { State: var current } && current.Connected == state.Connected
            && (current.Key == state.Key || current.Name == state.Name)) return;
        long now = Environment.TickCount64;
        if (_lastEvent.Connected == state.Connected && now - _lastEvent.At < DuplicateWindowMs
            && (_lastEvent.Key == state.Key || _lastEvent.Name == state.Name)) return;
        _lastEvent = (state.Key, state.Name, state.Connected, now);

        // Claviers et souris (MX Keys, MX Master…) : ils coupent la liaison après quelques minutes sans être
        // touchés et la rétablissent au premier appui. Ni la déconnexion, ni la reconnexion qui suit (dans les
        // 30 min) ne sont des événements ; une reconnexion après une longue absence s'affiche normalement.
        bool sleepy = Settings.QuietInputSleep && state.Kind is DeviceKind.Keyboard or DeviceKind.Mouse;

        if (state.Connected)
        {
            int low = Settings.LowBatteryThreshold, target = Settings.ChargeTarget;
            // Le popup de connexion montre déjà le niveau : pas de seconde alerte pour le même niveau.
            _batteries[state.Key] = new BatteryWatch
            {
                Last = state.Battery,
                Charging = state.Charging == true,
                LowNotified = state.Battery <= low,
                TargetNotified = state.Battery >= target,
            };
            if (_asleep.Remove(state.Key, out long since) && sleepy && now - since < Settings.InputSleepMinutes * 60_000L)
                Log.Write($"Réveil après veille, sans popup : {state.Name}");
            else if (isNew) Notify(state with { Reason = NotifyReason.NewDevice }, NotifyKind.NewDevice);
            else Notify(state, NotifyKind.Connect);
        }
        else
        {
            _batteries.Remove(state.Key);
            if (sleepy)
            {
                _asleep[state.Key] = now;
                Log.Write($"Déconnexion d'un clavier / d'une souris (veille probable), sans popup : {state.Name}");
            }
            else
            {
                Notify(state, NotifyKind.Disconnect);
            }
        }
    }

    /// <summary>Batterie faible, début de charge, niveau de charge atteint.</summary>
    private void EvaluateBattery(DeviceState state)
    {
        if (!state.Connected || state.Battery is not int level) return;
        if (!_batteries.TryGetValue(state.Key, out var w)) _batteries[state.Key] = w = new BatteryWatch();
        int? previous = w.Last;
        w.Last = level;
        int low = Settings.LowBatteryThreshold, target = Settings.ChargeTarget;

        // Charge : indiquée par Logitech / Xbox ; en Bluetooth, déduite de deux hausses successives.
        bool charging;
        if (state.Charging is bool reported)
        {
            charging = reported;
        }
        else
        {
            if (previous is int p) w.Rises = level > p ? w.Rises + 1 : level < p ? 0 : w.Rises;
            charging = w.Rises >= 2;
        }
        bool startedCharging = charging && !w.Charging && previous is not null;
        w.Charging = charging;
        var shown = state with { Charging = charging || state.Charging == true };

        if (startedCharging)
        {
            Notify(shown with { Reason = NotifyReason.Charging }, NotifyKind.Charging);
        }

        if (!w.LowNotified && level <= low && !charging)
        {
            w.LowNotified = true;
            // Niveau arrivé juste après le popup de connexion de ce même appareil : déjà affiché.
            if (Host.CurrentNotification?.Key != state.Key || previous is not null)
            {
                Notify(shown with { Reason = NotifyReason.LowBattery }, NotifyKind.LowBattery);
            }
        }
        if (level >= low + 5 || charging) w.LowNotified = false; // réarmé après recharge

        if (!w.TargetNotified && previous is int before && before < target && level >= target)
        {
            w.TargetNotified = true;
            Notify(shown with { Reason = NotifyReason.ChargeReached }, NotifyKind.ChargeReached);
        }
        if (level < target - 5) w.TargetNotified = false;
    }

    /// <summary>Règles propres au module (type activé, sourdine, démarrage / veille) puis affichage.</summary>
    private void Notify(DeviceState state, NotifyKind kind)
    {
        bool enabled = kind switch
        {
            NotifyKind.Connect => Settings.NotifyConnect,
            NotifyKind.NewDevice => Settings.NotifyNewDevice || Settings.NotifyConnect,
            NotifyKind.Disconnect => Settings.ShowDisconnections,
            NotifyKind.LowBattery => Settings.NotifyLowBattery,
            NotifyKind.Charging => Settings.NotifyCharging,
            NotifyKind.ChargeReached => Settings.NotifyChargeTarget,
            _ => true,
        };
        if (!enabled) return;
        if (kind == NotifyKind.NewDevice && !Settings.NotifyNewDevice) state = state with { Reason = NotifyReason.None };

        if (Settings.MutedDevices.Contains(state.Key))
        {
            Log.Write($"Popup ignoré (appareil en sourdine) : {state.Name}");
            return;
        }
        if (kind is NotifyKind.Connect or NotifyKind.Disconnect && (_suspended || Environment.TickCount64 < _quietUntil))
        {
            Log.Write($"Popup ignoré (démarrage / veille) : {state.Name}");
            return;
        }
        Host.Notify(Card(state));
    }

    // ── Listes ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Appareils connectés, toutes sources confondues, du plus récent au plus ancien.</summary>
    private List<DeviceState> Connected()
    {
        var result = new List<DeviceState>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (state, _) in _sources.Values.SelectMany(s => s.ConnectedDevices()).OrderByDescending(x => x.ConnectedAt))
        {
            // Une manette Bluetooth est vue par deux sources : une seule carte.
            if (names.Add(state.Name)) result.Add(Enrich(state));
        }
        return result;
    }

    public override IEnumerable<CardContent> ListCards() => Connected().Select(Card);

    /// <summary>Survol : appareils connectés, puis (option) les trois derniers déconnectés.</summary>
    public override IEnumerable<HoverRow> HoverRows()
    {
        var devices = Connected();
        if (!Settings.HoverShowDisconnected) return devices.Select(d => (HoverRow)new DeviceRow(d, OnCardClicked)).ToList();
        var shown = new HashSet<string>(devices.Select(d => d.Key).Concat(devices.Select(d => d.Name)), StringComparer.OrdinalIgnoreCase);
        if (_registry is not null) devices.AddRange(_registry.RecentlyDisconnected(3, shown));
        return devices.Select(d => (HoverRow)new DeviceRow(d, OnCardClicked));
    }

    public override IEnumerable<CardContent> PreviewCards() =>
    [
        Card(new DeviceState("demo-1", L.T("Preview.Headphones"), true, DeviceKind.Headphones, 72, BatteryState.Known)),
        Card(new DeviceState("demo-2", L.T("Preview.Mouse"), true, DeviceKind.Mouse, 18, BatteryState.Known, DeviceLink.Dongle)),
        Card(new DeviceState("demo-3", L.T("Preview.Keyboard"), true, DeviceKind.Keyboard, 95, BatteryState.Known, DeviceLink.Dongle, true)),
    ];
}
