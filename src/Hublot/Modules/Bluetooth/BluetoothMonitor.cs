using Hublot.Common;
using Windows.Devices.Enumeration;

namespace Hublot.Modules.Bluetooth;

/// <summary>
/// Surveille les nœuds PnP des appareils Bluetooth appairés via un DeviceWatcher.
/// Contrairement à un watcher d'« association endpoints », il ne déclenche aucun balayage radio :
/// il ne fait que relayer les notifications PnP (connexion, niveau de batterie). Aucun sondage.
/// Les événements arrivent sur le pool de threads.
/// </summary>
internal sealed class BluetoothMonitor
{
    /// <summary>DEVPKEY_Device_IsConnected (booléen) — mis à jour par la pile Bluetooth.</summary>
    public const string ConnectedKey = "{83DA6326-97A6-4088-9453-A1923F573B29} 15";
    /// <summary>DEVPKEY_Bluetooth_Battery (octet, %) — la valeur affichée dans Paramètres › Bluetooth.</summary>
    public const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ContainerKey = "System.Devices.ContainerId";
    private const string NameKey = "System.ItemNameDisplay";

    // Classe d'installation « Bluetooth » ; les nœuds de service (BTHLEDEVICE\…, BTHENUM\{…}) sont filtrés ensuite.
    private const string Selector = "System.Devices.ClassGuid:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";

    private static readonly string[] Properties = [ConnectedKey, BatteryKey, ContainerKey];

    private readonly Dictionary<string, BtDevice> _devices = [];
    private readonly Dictionary<string, long> _connectedAt = [];
    private DeviceWatcher? _watcher;
    private bool _ready;
    private volatile bool _shutdown;

    /// <summary>Changement d'état de connexion, après l'énumération initiale.</summary>
    public event Action<BtDevice>? ConnectionChanged;

    /// <summary>Nouveau niveau de batterie publié par Windows.</summary>
    public event Action<BtDevice>? BatteryChanged;

    /// <summary>Énumération initiale terminée (aucun popup pour les appareils déjà connectés).</summary>
    public event Action? Ready;

    public void Start()
    {
        if (_shutdown) return;
        lock (_devices)
        {
            _ready = false;
            _devices.Clear();
            _connectedAt.Clear();
        }

        var watcher = DeviceInformation.CreateWatcher(Selector, Properties, DeviceInformationKind.Device);
        watcher.Added += OnAdded;
        watcher.Updated += OnUpdated;
        watcher.Removed += OnRemoved;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        watcher.Stopped += OnStopped;
        _watcher = watcher;
        watcher.Start();
        Log.Write("DeviceWatcher démarré");
    }

    /// <summary>Arrêt définitif (fermeture de l'application).</summary>
    public void Shutdown()
    {
        _shutdown = true;
        Detach();
    }

    private void Detach()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is null) return;
        watcher.Added -= OnAdded;
        watcher.Updated -= OnUpdated;
        watcher.Removed -= OnRemoved;
        watcher.EnumerationCompleted -= OnEnumerationCompleted;
        watcher.Stopped -= OnStopped;
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            watcher.Stop();
    }

    /// <summary>Appareil connecté le plus récemment et l'instant de sa connexion, ou null.</summary>
    public (BtDevice Device, long ConnectedAt)? LastConnected()
    {
        lock (_devices)
        {
            var device = _devices.Values
                .Where(d => d.Connected)
                .OrderByDescending(d => _connectedAt.GetValueOrDefault(d.Id))
                .FirstOrDefault();
            return device is null ? null : (device, _connectedAt.GetValueOrDefault(device.Id));
        }
    }

    /// <summary>Appareils connectés et instant de leur connexion, du plus récent au plus ancien.</summary>
    public List<(BtDevice Device, long ConnectedAt)> ConnectedDevices()
    {
        lock (_devices)
        {
            return _devices.Values
                .Where(d => d.Connected)
                .Select(d => (d, _connectedAt.GetValueOrDefault(d.Id)))
                .OrderByDescending(x => x.Item2)
                .ToList();
        }
    }

    public List<BtDevice> AllDevices()
    {
        lock (_devices) return [.. _devices.Values];
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (sender != _watcher || !BtDevice.IsDeviceNode(info.Id)) return;
        var p = info.Properties;
        var device = new BtDevice(
            info.Id,
            info.Name,
            p.TryGetValue(ConnectedKey, out var c) && c is true,
            p.TryGetValue(ContainerKey, out var g) && g is Guid guid ? guid : null,
            p.TryGetValue(BatteryKey, out var b) ? ToPercent(b) : null);

        bool raise;
        lock (_devices)
        {
            _devices[device.Id] = device;
            if (device.Connected) _connectedAt[device.Id] = Environment.TickCount64;
            raise = _ready && device.Connected; // appairage d'un nouvel appareil
        }
        Log.Write($"+ {device.Name} [{device.Id}] connecté={device.Connected} batterie={device.Battery}");
        if (raise) ConnectionChanged?.Invoke(device);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (sender != _watcher) return;
        BtDevice? connection = null, battery = null;
        lock (_devices)
        {
            if (!_devices.TryGetValue(update.Id, out var before)) return;
            var after = before;
            var p = update.Properties;
            if (p.TryGetValue(ConnectedKey, out var c) && c is bool connected)
                after = after with { Connected = connected };
            if (p.TryGetValue(BatteryKey, out var b) && ToPercent(b) is int level)
                after = after with { Battery = level };
            if (p.TryGetValue(NameKey, out var n) && n is string name && name.Length > 0)
                after = after with { Name = name };
            if (p.TryGetValue(ContainerKey, out var g) && g is Guid guid)
                after = after with { ContainerId = guid };

            _devices[update.Id] = after;
            if (after.Connected != before.Connected)
            {
                if (after.Connected) _connectedAt[after.Id] = Environment.TickCount64;
                if (_ready) connection = after;
            }
            if (after.Battery != before.Battery && after.Battery is not null) battery = after;
        }
        if (connection is not null)
        {
            Log.Write($"~ {connection.Name} connecté={connection.Connected} batterie={connection.Battery}");
            ConnectionChanged?.Invoke(connection);
        }
        if (battery is not null)
        {
            Log.Write($"~ {battery.Name} batterie={battery.Battery}");
            BatteryChanged?.Invoke(battery);
        }
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (sender != _watcher) return;
        lock (_devices)
        {
            _devices.Remove(update.Id);
            _connectedAt.Remove(update.Id);
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        if (sender != _watcher) return;
        lock (_devices) _ready = true;
        Log.Write("Énumération initiale terminée");
        Ready?.Invoke();
    }

    private void OnStopped(DeviceWatcher sender, object args)
    {
        if (sender != _watcher || _shutdown) return;
        // Arrêt inattendu (service Bluetooth redémarré, etc.) : on relance après un délai.
        Log.Write($"DeviceWatcher arrêté ({sender.Status}), relance dans 5 s");
        Detach();
        _ = Task.Delay(5000).ContinueWith(_ =>
        {
            try { Start(); }
            catch (Exception ex) { Log.Error("Relance DeviceWatcher", ex); }
        }, TaskScheduler.Default);
    }

    public static int? ToPercent(object? value) => value switch
    {
        byte b when b <= 100 => b,
        int i when i is >= 0 and <= 100 => i,
        uint u when u <= 100 => (int)u,
        _ => null,
    };
}
