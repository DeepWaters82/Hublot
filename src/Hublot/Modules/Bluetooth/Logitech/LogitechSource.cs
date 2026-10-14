using System.Text;
using System.Text.RegularExpressions;
using Hublot.Common;
using Windows.Devices.Enumeration;

namespace Hublot.Modules.Bluetooth.Logitech;

/// <summary>
/// Périphériques sans fil reliés à un récepteur USB Logitech (Unifying, Bolt, Lightspeed, Nano).
/// Le récepteur reste branché : c'est lui qui signale, par une notification HID++ 0x41, la
/// connexion ou la déconnexion de chaque périphérique appairé (jusqu'à 6).
/// Protocole : HID++ 1.0 (registres du récepteur) et 2.0 (fonctionnalités des périphériques),
/// tel que documenté par Logitech et implémenté par Solaar.
/// </summary>
internal sealed partial class LogitechSource : DeviceSource
{
    private const string Selector =
        "System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\" AND " +
        "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True AND " +
        "System.DeviceInterface.Hid.VendorId:=1133 AND System.DeviceInterface.Hid.UsagePage:=65280";
    private const string ProductIdKey = "System.DeviceInterface.Hid.ProductId";
    private const string UsageIdKey = "System.DeviceInterface.Hid.UsageId";
    private static readonly string[] Properties = [ProductIdKey, UsageIdKey];

    private const ushort BoltPid = 0xC548;
    private static readonly HashSet<ushort> ReceiverPids =
    [
        0xC52B, 0xC532,                                                     // Unifying
        0xC548,                                                             // Bolt
        0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC54D,     // Lightspeed
        0xC517, 0xC518, 0xC51A, 0xC51B, 0xC521, 0xC525, 0xC526, 0xC52E, 0xC52F, 0xC531, 0xC534, 0xC537, // Nano
    ];

    // Fonctionnalités HID++ 2.0
    private const ushort FeatureDeviceName = 0x0005;
    private const ushort FeatureBatteryStatus = 0x1000;
    private const ushort FeatureBatteryVoltage = 0x1001;
    private const ushort FeatureUnifiedBattery = 0x1004;

    private readonly object _lock = new();
    private readonly Dictionary<string, ReceiverGroup> _groups = [];
    private DeviceWatcher? _watcher;
    private bool _enumerated;

    private sealed class ReceiverGroup(string key)
    {
        public string Key { get; } = key;
        public string? ShortId, LongId;
        public ushort ProductId;
        public bool PresentAtStartup;
        public Receiver? Open;
        public bool Opening;
    }

    private sealed class Receiver(string key, ushort productId, HidppReceiver hid)
    {
        public string Key { get; } = key;
        public ushort ProductId { get; } = productId;
        public HidppReceiver Hid { get; } = hid;
        public long BaselineUntil;
        public readonly Dictionary<byte, Paired> Devices = [];
    }

    private sealed class Paired(byte index)
    {
        public byte Index { get; } = index;
        public string? Name;
        public DeviceKind Kind;
        public bool Connected;
        public bool Seen;
        public int? Battery;
        public bool? Charging;
        public byte BatteryFeatureIndex;
        public ushort BatteryFeature;
        public long ConnectedAt;
        public bool Loading;
    }

    public override string Name => "Logitech";

    public override void Start()
    {
        var watcher = DeviceInformation.CreateWatcher(Selector, Properties);
        watcher.Added += OnAdded;
        watcher.Removed += OnRemoved;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        _watcher = watcher;
        watcher.Start();
    }

    public override void Stop()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is not null)
        {
            watcher.Added -= OnAdded;
            watcher.Removed -= OnRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
        }
        lock (_lock)
        {
            foreach (var group in _groups.Values) group.Open?.Hid.Dispose();
            _groups.Clear();
        }
    }

    public override (DeviceState State, long ConnectedAt)? LastConnected()
    {
        lock (_lock)
        {
            (DeviceState, long)? best = null;
            foreach (var group in _groups.Values)
            {
                if (group.Open is not { } receiver) continue;
                foreach (var p in receiver.Devices.Values)
                {
                    if (p.Connected && (best is null || p.ConnectedAt > best.Value.Item2))
                        best = (Describe(receiver, p), p.ConnectedAt);
                }
            }
            return best;
        }
    }

    public override IReadOnlyList<(DeviceState State, long ConnectedAt)> ConnectedDevices()
    {
        lock (_lock)
        {
            return _groups.Values
                .Where(g => g.Open is not null)
                .SelectMany(g => g.Open!.Devices.Values.Where(p => p.Connected).Select(p => (Describe(g.Open!, p), p.ConnectedAt)))
                .OrderByDescending(x => x.ConnectedAt)
                .ToList();
        }
    }

    // ── Découverte des récepteurs ──────────────────────────────────────────────────────────

    [GeneratedRegex("&col[0-9a-f]{2}", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionSuffix();

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (sender != _watcher) return;
        if (!info.Properties.TryGetValue(ProductIdKey, out var p) || p is not ushort pid || !ReceiverPids.Contains(pid)) return;
        if (!info.Properties.TryGetValue(UsageIdKey, out var u) || u is not ushort usage) return;

        // Les deux collections d'une même interface ne diffèrent que par « &ColNN ».
        string key = CollectionSuffix().Replace(info.Id, "").ToUpperInvariant();
        ReceiverGroup group;
        lock (_lock)
        {
            if (!_groups.TryGetValue(key, out group!))
                _groups[key] = group = new ReceiverGroup(key) { ProductId = pid, PresentAtStartup = !_enumerated };
            if (usage == 1) group.ShortId = info.Id;
            else if (usage == 2) group.LongId = info.Id;
        }
        Log.Write($"Récepteur Logitech {pid:X4} usage {usage} : {info.Id}");
        // Laisse aux deux collections le temps d'apparaître avant d'ouvrir le récepteur.
        _ = Task.Delay(400).ContinueWith(_ => OpenAsync(group), TaskScheduler.Default);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (sender != _watcher) return;
        string key = CollectionSuffix().Replace(update.Id, "").ToUpperInvariant();
        lock (_lock)
        {
            if (!_groups.Remove(key, out var group)) return;
            group.Open?.Hid.Dispose();
            Log.Write($"Récepteur Logitech retiré : {key}");
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_lock) _enumerated = true;
    }

    private async Task OpenAsync(ReceiverGroup group)
    {
        string? shortId, longId;
        lock (_lock)
        {
            if (group.Open is not null || group.Opening || group.LongId is null || !_groups.ContainsKey(group.Key)) return;
            group.Opening = true;
            shortId = group.ShortId;
            longId = group.LongId;
        }
        try
        {
            var hid = await HidppReceiver.OpenAsync(shortId, longId);
            if (hid is null)
            {
                Log.Write($"Récepteur Logitech inaccessible : {group.Key}");
                return;
            }
            var receiver = new Receiver(group.Key, group.ProductId, hid);
            lock (_lock)
            {
                if (!_groups.ContainsKey(group.Key))
                {
                    hid.Dispose();
                    return;
                }
                group.Open = receiver;
            }
            // Récepteur présent au lancement : ses périphériques déjà connectés ne déclenchent pas de popup.
            receiver.BaselineUntil = group.PresentAtStartup ? Environment.TickCount64 + 2500 : 0;
            hid.Notification += report => OnNotification(receiver, report);

            await EnableConnectionNotificationsAsync(hid);
            // Registre 0x02 = 0x02 : le récepteur renvoie une notification 0x41 par périphérique appairé.
            await hid.WriteRegisterAsync(0x02, 0x02, 0x00, 0x00);
            Log.Write($"Récepteur Logitech ouvert : {group.ProductId:X4} (canal court : {hid.HasShortChannel})");
        }
        catch (Exception ex)
        {
            Log.Error("Ouverture récepteur Logitech", ex);
        }
        finally
        {
            lock (_lock) group.Opening = false;
        }
    }

    /// <summary>Registre 0x00 : active le drapeau « notifications sans fil » (0x000100) sans toucher aux autres.</summary>
    private static async Task EnableConnectionNotificationsAsync(HidppReceiver hid)
    {
        var current = await hid.ReadRegisterAsync(0x00);
        int flags = current is null ? 0 : current[4] << 16 | current[5] << 8 | current[6];
        int wanted = flags | 0x000100;
        if (current is null || wanted != flags)
            await hid.WriteRegisterAsync(0x00, (byte)(wanted >> 16), (byte)(wanted >> 8), (byte)wanted);
    }

    // ── Notifications ──────────────────────────────────────────────────────────────────────

    private void OnNotification(Receiver receiver, byte[] r)
    {
        byte index = r[1];
        if (index is < 1 or > 6) return;

        if (r[2] == 0x41)
        {
            OnConnection(receiver, index, r);
            return;
        }

        // Événement de batterie diffusé par le périphérique : index de fonctionnalité, ID logiciel 0.
        DeviceState? update = null;
        lock (_lock)
        {
            if (!receiver.Devices.TryGetValue(index, out var p) || p.BatteryFeatureIndex == 0) return;
            if (r[2] != p.BatteryFeatureIndex || (r[3] & 0x0F) != 0 || r[0] != 0x11) return;
            var (level, charging) = ParseBattery(p.BatteryFeature, r.AsSpan(4));
            if (level is null || (level == p.Battery && charging == p.Charging)) return;
            p.Battery = level;
            p.Charging = charging;
            update = Describe(receiver, p);
        }
        Log.Write($"Logitech {update.Name} batterie={update.Battery}");
        Raise(update, connectionChanged: false);
    }

    /// <summary>
    /// Notification 0x41 : octet 4 = drapeaux (0x40 : liaison non établie) + type d'appareil (4 bits bas).
    /// </summary>
    private void OnConnection(Receiver receiver, byte index, byte[] r)
    {
        bool connected = (r[4] & 0x40) == 0;
        var kind = KindFromNotification(r[4] & 0x0F);
        DeviceState state;
        bool raise, load;
        lock (_lock)
        {
            if (!receiver.Devices.TryGetValue(index, out var p))
                receiver.Devices[index] = p = new Paired(index);
            bool wasConnected = p.Connected, seen = p.Seen;
            p.Seen = true;
            p.Connected = connected;
            if (kind != DeviceKind.Generic && p.Kind == DeviceKind.Generic) p.Kind = kind;
            if (connected && !wasConnected) p.ConnectedAt = Environment.TickCount64;

            bool baseline = Environment.TickCount64 < receiver.BaselineUntil;
            // Popup : vraie transition, hors état initial ; une première notification « déconnecté » n'est pas un événement.
            raise = !baseline && (connected != wasConnected) && (connected || seen);
            load = connected && !p.Loading && (p.Name is null || p.BatteryFeatureIndex == 0 || raise);
            if (load) p.Loading = true;
            state = Describe(receiver, p);
        }
        Log.Write($"Logitech #{index} connecté={connected} popup={raise}");
        if (raise) Raise(state, connectionChanged: true);
        if (load) _ = LoadDetailsAsync(receiver, index);
    }

    // ── Interrogation du périphérique (HID++ 2.0) ──────────────────────────────────────────

    private async Task LoadDetailsAsync(Receiver receiver, byte index)
    {
        var hid = receiver.Hid;
        try
        {
            string? name;
            lock (_lock) name = receiver.Devices.GetValueOrDefault(index)?.Name;

            byte nameFeature = await FeatureIndexAsync(hid, index, FeatureDeviceName);
            DeviceKind kind = DeviceKind.Generic;
            if (nameFeature != 0)
            {
                name ??= await ReadNameAsync(hid, index, nameFeature);
                if (await hid.CallAsync(index, nameFeature, 2) is { } type) kind = KindFromDeviceType(type[4]);
            }
            name ??= await ReadPairingNameAsync(receiver, index);

            byte batteryIndex = 0;
            ushort batteryFeature = 0;
            int? battery = null;
            bool? charging = null;
            foreach (var feature in new[] { FeatureUnifiedBattery, FeatureBatteryStatus, FeatureBatteryVoltage })
            {
                batteryIndex = await FeatureIndexAsync(hid, index, feature);
                if (batteryIndex == 0) continue;
                batteryFeature = feature;
                // Unified Battery : get_status est la fonction 1 ; les deux autres répondent en fonction 0.
                var status = await hid.CallAsync(index, batteryIndex, feature == FeatureUnifiedBattery ? (byte)1 : (byte)0);
                if (status is not null) (battery, charging) = ParseBattery(feature, status.AsSpan(4));
                break;
            }

            DeviceState state;
            lock (_lock)
            {
                if (!receiver.Devices.TryGetValue(index, out var p)) return;
                p.Name = name ?? p.Name;
                if (kind != DeviceKind.Generic) p.Kind = kind;
                p.BatteryFeatureIndex = batteryIndex;
                p.BatteryFeature = batteryFeature;
                p.Battery = battery ?? p.Battery;
                p.Charging = charging ?? p.Charging;
                p.Loading = false;
                state = Describe(receiver, p);
                if (battery is null && p.Connected) state = state with { BatteryState = BatteryState.Unavailable };
            }
            Log.Write($"Logitech #{index} : {state.Name}, {state.Kind}, batterie={state.Battery}");
            Raise(state, connectionChanged: false);
        }
        catch (Exception ex)
        {
            Log.Error($"Logitech #{index}", ex);
            lock (_lock)
            {
                if (receiver.Devices.TryGetValue(index, out var p)) p.Loading = false;
            }
        }
    }

    /// <summary>Fonctionnalité Root (index 0), fonction 0 : index d'une fonctionnalité (0 = absente).</summary>
    private static async Task<byte> FeatureIndexAsync(HidppReceiver hid, byte device, ushort feature)
        => await hid.CallAsync(device, 0x00, 0, (byte)(feature >> 8), (byte)feature) is { } r ? r[4] : (byte)0;

    /// <summary>DEVICE_NAME : fonction 0 = longueur, fonction 1 = morceau du nom à partir d'un index.</summary>
    private static async Task<string?> ReadNameAsync(HidppReceiver hid, byte device, byte feature)
    {
        if (await hid.CallAsync(device, feature, 0) is not { } count || count[4] == 0) return null;
        int length = count[4];
        var bytes = new List<byte>(length);
        while (bytes.Count < length)
        {
            if (await hid.CallAsync(device, feature, 1, (byte)bytes.Count) is not { } chunk) return null;
            int take = Math.Min(16, length - bytes.Count);
            bytes.AddRange(chunk.AsSpan(4, take).ToArray());
        }
        return Clean(bytes.ToArray());
    }

    /// <summary>Repli HID++ 1.0 : nom enregistré dans le registre d'appairage du récepteur (0xB5).</summary>
    private static async Task<string?> ReadPairingNameAsync(Receiver receiver, byte index)
    {
        if (receiver.ProductId == BoltPid)
        {
            // Bolt : sous-registre 0x60 + n, données = [sous-reg, page, longueur, nom…]
            if (await receiver.Hid.ReadLongRegisterAsync(0xB5, (byte)(0x60 + index), 0x01) is { } bolt && bolt.Length > 7)
                return Clean(bolt.AsSpan(7, Math.Min(bolt[6], bolt.Length - 7)).ToArray());
            return null;
        }
        // Unifying : sous-registre 0x40 + n - 1, données = [sous-reg, longueur, nom…]
        if (await receiver.Hid.ReadLongRegisterAsync(0xB5, (byte)(0x40 + index - 1)) is { } r && r.Length > 6)
            return Clean(r.AsSpan(6, Math.Min(r[5], r.Length - 6)).ToArray());
        return null;
    }

    private static string? Clean(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ');
        return text.Length > 0 ? text : null;
    }

    // ── Conversions ────────────────────────────────────────────────────────────────────────

    private static DeviceState Describe(Receiver receiver, Paired p)
    {
        string key = $"logi:{receiver.Key}:{p.Index}";
        string name = p.Name ?? L.T("Device.Logitech");
        var kind = p.Kind == DeviceKind.Generic ? DeviceKind.Dongle : p.Kind;
        if (!p.Connected) return DeviceState.Disconnected(key, name, kind, p.Battery, DeviceLink.Dongle);
        return p.Battery is int level
            ? new DeviceState(key, name, true, kind, level, BatteryState.Known, DeviceLink.Dongle, p.Charging)
            : new DeviceState(key, name, true, kind, null, BatteryState.Pending, DeviceLink.Dongle, p.Charging);
    }

    /// <summary>Niveau en % et état de charge selon la fonctionnalité de batterie (données = octets utiles).</summary>
    private static (int? Level, bool? Charging) ParseBattery(ushort feature, ReadOnlySpan<byte> d)
    {
        switch (feature)
        {
            case FeatureUnifiedBattery:
                // [état de charge %, niveaux (1 critique, 2 bas, 4 bon, 8 plein), charge (1, 2 = en charge), alim. externe]
                int? level = d[0] is > 0 and <= 100 ? d[0] : (d[1] & 0x0F) switch
                {
                    >= 8 => 100,
                    >= 4 => 60,
                    >= 2 => 20,
                    >= 1 => 5,
                    _ => null,
                };
                return (level, d[2] is 1 or 2);
            case FeatureBatteryStatus:
                // [niveau %, niveau suivant, état (1 recharge, 2 presque plein, 4 recharge lente)]
                return (d[0] is > 0 and <= 100 ? d[0] : null, d[2] is 1 or 2 or 4);
            case FeatureBatteryVoltage:
                // [tension mV (16 bits), drapeaux (bit 7 : alimentation externe)]
                return (VoltageToPercent(d[0] << 8 | d[1]), (d[2] & 0x80) != 0);
            default:
                return (null, null);
        }
    }

    /// <summary>Courbe de décharge typique d'un accu Li-Po (valeurs reprises de Solaar).</summary>
    private static readonly (int MilliVolts, int Percent)[] DischargeCurve =
    [
        (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
        (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
    ];

    private static int? VoltageToPercent(int mv)
    {
        if (mv <= 0) return null;
        if (mv >= DischargeCurve[0].MilliVolts) return 100;
        for (int i = 1; i < DischargeCurve.Length; i++)
        {
            var (hiV, hiP) = DischargeCurve[i - 1];
            var (loV, loP) = DischargeCurve[i];
            if (mv >= loV) return loP + (hiP - loP) * (mv - loV) / (hiV - loV);
        }
        return 0;
    }

    /// <summary>Type porté par la notification 0x41 du récepteur.</summary>
    private static DeviceKind KindFromNotification(int nibble) => nibble switch
    {
        1 or 3 => DeviceKind.Keyboard,  // clavier, pavé numérique
        2 or 8 or 9 => DeviceKind.Mouse, // souris, trackball, pavé tactile
        4 => DeviceKind.Presenter,
        _ => DeviceKind.Generic,
    };

    /// <summary>DEVICE_NAME fonction 2 (getDeviceType).</summary>
    private static DeviceKind KindFromDeviceType(byte type) => type switch
    {
        0 or 2 => DeviceKind.Keyboard,
        3 or 4 or 5 => DeviceKind.Mouse,
        6 => DeviceKind.Presenter,
        8 => DeviceKind.Headset,
        10 or 11 or 12 => DeviceKind.Gamepad,
        14 => DeviceKind.Speaker,
        _ => DeviceKind.Generic,
    };
}
