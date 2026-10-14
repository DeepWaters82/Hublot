using Hublot.Modules.Bluetooth;
using Hublot.Common;

namespace Hublot.Modules.Bluetooth;

/// <summary>
/// Appareils Bluetooth appairés (classique et LE). L'état vient du watcher PnP ; le type
/// d'appareil et, si nécessaire, la batterie sont complétés en tâche de fond.
/// </summary>
internal sealed class BluetoothSource : DeviceSource
{
    // Si le nœud ne porte pas encore de niveau à la connexion : Windows ne le publie souvent
    // qu'une à trois secondes après l'établissement du lien.
    private static readonly int[] BatteryRetryDelaysMs = [0, 1200, 1800, 2500];

    private readonly BluetoothMonitor _monitor = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceKind> _kinds = [];
    private readonly Dictionary<string, int> _lastBattery = [];
    private readonly Dictionary<string, CancellationTokenSource> _fetches = [];

    public BluetoothSource()
    {
        _monitor.Ready += () => _ = ResolveKindsAsync();
        _monitor.ConnectionChanged += device => Raise(Describe(device, fetch: true), connectionChanged: true);
        _monitor.BatteryChanged += device =>
        {
            if (device.Battery is not int level) return;
            lock (_lock) _lastBattery[device.Key] = level;
            Raise(Describe(device, fetch: false), connectionChanged: false);
        };
    }

    public override string Name => "Bluetooth";

    /// <summary>Énumération initiale terminée.</summary>
    public event Action? Ready
    {
        add => _monitor.Ready += value;
        remove => _monitor.Ready -= value;
    }

    public override void Start() => _monitor.Start();

    public override void Stop()
    {
        _monitor.Shutdown();
        lock (_lock)
        {
            foreach (var cts in _fetches.Values) cts.Cancel();
            _fetches.Clear();
        }
    }

    public override (DeviceState State, long ConnectedAt)? LastConnected()
        => _monitor.LastConnected() is { } last ? (Describe(last.Device, fetch: true), last.ConnectedAt) : null;

    public override IReadOnlyList<(DeviceState State, long ConnectedAt)> ConnectedDevices()
    {
        // Un appareil bimode a deux nœuds connectés : un seul état par appareil physique.
        var result = new List<(DeviceState, long)>();
        var seen = new HashSet<string>();
        foreach (var (device, at) in _monitor.ConnectedDevices())
        {
            if (seen.Add(device.Key)) result.Add((Describe(device, fetch: false), at));
        }
        return result;
    }

    public override IReadOnlyList<DeviceState> KnownDevices()
    {
        var result = new Dictionary<string, DeviceState>();
        foreach (var device in _monitor.AllDevices())
        {
            // Un nœud connecté l'emporte sur le nœud déconnecté du même appareil bimode.
            if (!result.TryGetValue(device.Key, out var existing) || (device.Connected && !existing.Connected))
                result[device.Key] = Describe(device, fetch: false);
        }
        return [.. result.Values];
    }

    /// <summary>
    /// Une fois au lancement : type de chaque appareil appairé (icône dans les listes de l'utilitaire).
    /// Simple lecture des propriétés de l'appareil, sans connexion radio.
    /// </summary>
    private async Task ResolveKindsAsync()
    {
        foreach (var device in _monitor.AllDevices().DistinctBy(d => d.Key))
        {
            lock (_lock)
            {
                if (_kinds.ContainsKey(device.Key)) continue;
            }
            var kind = await DeviceInfoReader.GetKindAsync(device);
            lock (_lock) _kinds[device.Key] = kind;
            Raise(Describe(device, fetch: false), connectionChanged: false);
        }
    }

    /// <summary>État affichable, à partir du nœud et des informations déjà connues.</summary>
    private DeviceState Describe(BtDevice device, bool fetch)
    {
        DeviceKind kind;
        bool kindKnown;
        int? battery;
        lock (_lock)
        {
            if (device.Battery is int known) _lastBattery[device.Key] = known;
            kindKnown = _kinds.TryGetValue(device.Key, out kind);
            battery = device.Battery ?? (_lastBattery.TryGetValue(device.Key, out var last) ? last : null);
        }

        DeviceState state;
        if (!device.Connected)
            state = DeviceState.Disconnected(device.Key, device.Name, kind, battery, DeviceLink.Bluetooth);
        else if (device.Battery is null)
            state = new DeviceState(device.Key, device.Name, true, kind, null, BatteryState.Pending);
        else
            state = new DeviceState(device.Key, device.Name, true, kind, battery, BatteryState.Known);

        if (fetch && (!kindKnown || state.BatteryState == BatteryState.Pending))
        {
            CancellationTokenSource cts;
            lock (_lock)
            {
                if (_fetches.Remove(device.Key, out var previous)) previous.Cancel();
                _fetches[device.Key] = cts = new CancellationTokenSource();
            }
            _ = FetchAsync(device, state, kindKnown, cts.Token);
        }
        return state;
    }

    /// <summary>Type puis batterie ; chaque résultat est publié comme complément d'information.</summary>
    private async Task FetchAsync(BtDevice device, DeviceState state, bool kindKnown, CancellationToken ct)
    {
        try
        {
            if (!kindKnown)
            {
                var kind = await DeviceInfoReader.GetKindAsync(device);
                lock (_lock) _kinds[device.Key] = kind;
                ct.ThrowIfCancellationRequested();
                state = state with { Kind = kind };
                Raise(state, connectionChanged: false);
            }
            if (state.BatteryState != BatteryState.Pending) return;

            for (int attempt = 0; attempt < BatteryRetryDelaysMs.Length; attempt++)
            {
                await Task.Delay(BatteryRetryDelaysMs[attempt], ct);
                // Le repli GATT (plus coûteux) n'est tenté qu'une fois, à la deuxième tentative.
                var level = await DeviceInfoReader.GetBatteryAsync(device, allowGatt: attempt == 1);
                ct.ThrowIfCancellationRequested();
                if (level is int pct)
                {
                    Log.Write($"Batterie {device.Name} : {pct} %");
                    lock (_lock) _lastBattery[device.Key] = pct;
                    Raise(state with { Battery = pct, BatteryState = BatteryState.Known }, connectionChanged: false);
                    return;
                }
            }
            Raise(state with { BatteryState = BatteryState.Unavailable }, connectionChanged: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Détails {device.Name}", ex);
        }
        finally
        {
            lock (_lock)
            {
                if (_fetches.TryGetValue(device.Key, out var current) && current.Token == ct) _fetches.Remove(device.Key);
            }
        }
    }
}
