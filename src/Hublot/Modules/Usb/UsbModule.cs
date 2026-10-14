using System.Text.RegularExpressions;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;
using Windows.Devices.Enumeration;

namespace Hublot.Modules.Usb;

/// <summary>
/// Module « Périphériques USB » : connexion / déconnexion des périphériques USB et, pour les
/// stockages, espace utilisé / restant. Deux DeviceWatcher, purement événementiels :
/// - interfaces GUID_DEVINTERFACE_USB_DEVICE : une par périphérique physique (pas une par fonction) ;
/// - interfaces de volume : rattachées au périphérique par leur conteneur PnP (ContainerId).
/// Le volume est monté une à deux secondes après le branchement : la carte s'affiche tout de suite,
/// puis se complète. Les périphériques présents au lancement forment l'état initial (aucun popup).
/// </summary>
internal sealed partial class UsbModule : Module
{
    private const string UsbSelector =
        "System.Devices.InterfaceClassGuid:=\"{A5DCBF10-6530-11D2-901F-00C04FB951ED}\" AND " +
        "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
    private const string VolumeSelector =
        "System.Devices.InterfaceClassGuid:=\"{53F5630D-B6BF-11D0-94F2-00A0C91EFB8B}\" AND " +
        "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
    private const string InstanceKey = "System.Devices.DeviceInstanceId";
    private const string ContainerKey = "System.Devices.ContainerId";
    /// <summary>DEVPKEY_Device_BusReportedDeviceDesc : le nom annoncé par le périphérique (« SanDisk Ultra »).</summary>
    private const string BusNameKey = "{540b947e-8b40-45bc-a8a2-6a0b894cbda2} 4";
    private const string CompatibleKey = "System.Devices.CompatibleIds";
    private static readonly string[] InterfaceProperties = [InstanceKey, ContainerKey];
    private static readonly string[] NodeProperties = [BusNameKey, CompatibleKey];
    private static readonly string[] ChildProperties = [CompatibleKey];
    /// <summary>Conteneur de l'ordinateur lui-même : périphériques internes.</summary>
    private static readonly Guid ComputerContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>Déconnexion suivie d'une reconnexion plus rapide que ça : ré-énumération, pas un événement.</summary>
    private const int FlapMs = 2000;
    private const int StorageWaitMs = 6000;
    private const long ResumeGraceMs = 30_000;

    private sealed class Device(string interfaceId, string instanceId, Guid? container)
    {
        public string InterfaceId { get; } = interfaceId;
        public string InstanceId { get; } = instanceId;
        public Guid? Container { get; } = container;
        public string Name = L.T("Usb.Device");
        public UsbKind Kind;
        public bool Hub, Resolved, Announced;
        public long ConnectedAt = Environment.TickCount64;
        public string? LastTitle;
        public Timer? PendingRemoval, StorageTimeout;
    }

    private sealed class Volume(Guid? container)
    {
        public Guid? Container { get; } = container;
        public List<string> Roots = [];
    }

    private readonly Dictionary<string, Device> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Volume> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private DeviceWatcher? _usbWatcher, _volumeWatcher;
    private bool _usbReady;
    private IHost? _host;
    private bool _suspended;
    private long _quietUntil;

    public override string Id => "usb";
    public override string Name => "Périphériques USB";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");

    public override bool IsEnabled(Settings settings) => settings.ModuleUsb;

    public override void Start(IHost host)
    {
        _host = host;
        _usbReady = false;
        _usbWatcher = DeviceInformation.CreateWatcher(UsbSelector, InterfaceProperties);
        _usbWatcher.Added += OnUsbAdded;
        _usbWatcher.Removed += OnUsbRemoved;
        _usbWatcher.EnumerationCompleted += OnUsbEnumerated;
        _volumeWatcher = DeviceInformation.CreateWatcher(VolumeSelector, InterfaceProperties);
        _volumeWatcher.Added += OnVolumeAdded;
        _volumeWatcher.Removed += OnVolumeRemoved;
        _volumeWatcher.Start();
        _usbWatcher.Start();
    }

    public override void Stop()
    {
        if (_usbWatcher is { } usb)
        {
            usb.Added -= OnUsbAdded;
            usb.Removed -= OnUsbRemoved;
            usb.EnumerationCompleted -= OnUsbEnumerated;
            StopWatcher(usb);
        }
        if (_volumeWatcher is { } volumes)
        {
            volumes.Added -= OnVolumeAdded;
            volumes.Removed -= OnVolumeRemoved;
            StopWatcher(volumes);
        }
        _usbWatcher = _volumeWatcher = null;
        foreach (var device in _devices.Values)
        {
            device.PendingRemoval?.Dispose();
            device.StorageTimeout?.Dispose();
        }
        _devices.Clear();
        _volumes.Clear();
        _host = null;
    }

    private static void StopWatcher(DeviceWatcher watcher)
    {
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
    }

    /// <summary>Veille : les périphériques USB se ré-énumèrent en rafale au réveil.</summary>
    public override void OnPowerChanged(bool resumed)
    {
        if (_host is null || !Host.Settings.IgnoreResumeConnections) return;
        _suspended = !resumed;
        if (resumed) _quietUntil = Environment.TickCount64 + ResumeGraceMs;
    }

    // ── Périphériques ──────────────────────────────────────────────────────────────────────

    private static Guid? ContainerOf(IReadOnlyDictionary<string, object> properties)
        => properties.TryGetValue(ContainerKey, out var g) && g is Guid guid ? guid : null;

    private void OnUsbAdded(DeviceWatcher sender, DeviceInformation info)
    {
        string instance = info.Properties.TryGetValue(InstanceKey, out var i) && i is string s ? s : "";
        var container = ContainerOf(info.Properties);
        string name = info.Name;
        _host?.Post(() => OnDeviceArrived(info.Id, instance, container, name));
    }

    private void OnUsbRemoved(DeviceWatcher sender, DeviceInformationUpdate update) => _host?.Post(() => OnDeviceLeft(update.Id));

    private void OnUsbEnumerated(DeviceWatcher sender, object args) => _host?.Post(() =>
    {
        _usbReady = true;
        Log.Write($"Périphériques USB présents au lancement : {_devices.Count}");
    });

    private void OnDeviceArrived(string interfaceId, string instanceId, Guid? container, string fallbackName)
    {
        if (_host is null) return;
        if (_devices.TryGetValue(interfaceId, out var known))
        {
            // Revenu avant l'expiration du délai de déconnexion : simple ré-énumération.
            known.PendingRemoval?.Dispose();
            known.PendingRemoval = null;
            return;
        }
        var device = new Device(interfaceId, instanceId, container) { Name = fallbackName };
        _devices[interfaceId] = device;
        if (!_usbReady)
        {
            // Déjà branché au lancement : jamais de popup, donc pas de requêtes (nom, type, fonctions).
            // Un stockage reste reconnu par ses volumes ; son nom vient alors du volume.
            device.Resolved = true;
            return;
        }
        _ = ResolveAsync(device, announce: true);
    }

    private void OnDeviceLeft(string interfaceId)
    {
        if (_host is null || !_devices.TryGetValue(interfaceId, out var device)) return;
        device.PendingRemoval?.Dispose();
        device.PendingRemoval = new Timer(_ => _host?.Post(() =>
        {
            if (!_devices.Remove(interfaceId)) return;
            device.StorageTimeout?.Dispose();
            if (device.Resolved && !device.Hub)
            {
                // « Retirer le périphérique en toute sécurité » : Windows affiche déjà sa propre notification.
                if (Host.Settings.UsbIgnoreSafeRemoval && PnP.IsHeldForEject(device.InstanceId))
                {
                    Log.Write($"USB retiré en toute sécurité (pas de popup) : {device.Name}");
                    return;
                }
                Log.Write($"USB déconnecté : {device.Name}");
                Notify(Describe(device, connected: false), connected: false);
            }
        }), null, FlapMs, Timeout.Infinite);
    }

    /// <summary>Nom annoncé par le périphérique et type (d'après les classes USB de ses fonctions).</summary>
    private async Task ResolveAsync(Device device, bool announce)
    {
        string name = device.Name;
        var classes = new List<string>();
        try
        {
            var node = await DeviceInformation.CreateFromIdAsync(device.InstanceId, NodeProperties, DeviceInformationKind.Device);
            if (node.Properties.TryGetValue(BusNameKey, out var bus) && bus is string busName && !string.IsNullOrWhiteSpace(busName))
                name = busName.Trim();
            else if (!string.IsNullOrWhiteSpace(node.Name))
                name = node.Name;
            if (node.Properties.TryGetValue(CompatibleKey, out var ids) && ids is string[] list) classes.AddRange(list);

            // Périphérique composite (clavier + souris, webcam + micro…) : le type se lit sur ses fonctions.
            bool composite = classes.Count == 0 || classes.Any(c => c.Contains("DevClass_00", StringComparison.OrdinalIgnoreCase)
                || c.Contains("COMPOSITE", StringComparison.OrdinalIgnoreCase));
            if (composite && device.Container is Guid container && container != ComputerContainer)
            {
                var children = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{container}}}\"",
                    ChildProperties, DeviceInformationKind.Device);
                foreach (var child in children)
                    if (child.Properties.TryGetValue(CompatibleKey, out var c) && c is string[] childIds) classes.AddRange(childIds);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Périphérique USB {device.InstanceId}", ex);
        }

        var (kind, hub) = Classify(classes);
        _host?.Post(() =>
        {
            if (!_devices.ContainsKey(device.InterfaceId)) return;
            device.Name = name;
            device.Kind = kind;
            device.Hub = hub;
            device.Resolved = true;
            if (!announce || hub) return;
            device.Announced = true;
            Log.Write($"USB connecté : {name} ({kind})");
            if (IsStorage(device))
            {
                // Si aucun volume n'est monté d'ici là, la carte l'indiquera.
                device.StorageTimeout = new Timer(_ => _host?.Post(() => Refresh(device)), null, StorageWaitMs, Timeout.Infinite);
            }
            Notify(Describe(device, connected: true), connected: true);
        });
    }

    [GeneratedRegex(@"(?<![A-Za-z])Class_([0-9A-F]{2})(?:&SubClass_([0-9A-F]{2})(?:&Prot_([0-9A-F]{2}))?)?", RegexOptions.IgnoreCase)]
    private static partial Regex UsbClass();

    /// <summary>
    /// Classes USB (identifiants compatibles « USB\Class_08&amp;SubClass_06… »). « DevClass_00 » et
    /// « SubClass_01 » ne sont pas des classes : le motif exige « Class_ » non précédé d'une lettre.
    /// </summary>
    private static (UsbKind Kind, bool Hub) Classify(List<string> ids)
    {
        var found = new HashSet<string>();
        foreach (var id in ids)
        foreach (Match m in UsbClass().Matches(id))
            found.Add($"{m.Groups[1].Value.ToUpperInvariant()}/{m.Groups[2].Value}/{m.Groups[3].Value}");
        bool Has(string cls) => found.Any(f => f.StartsWith(cls + "/", StringComparison.Ordinal));
        bool hub = Has("09");
        UsbKind kind =
            Has("08") ? UsbKind.Storage :
            Has("0E") ? UsbKind.Webcam :
            Has("01") ? UsbKind.Audio :
            found.Contains("03/01/01") ? UsbKind.Keyboard :
            found.Contains("03/01/02") ? UsbKind.Mouse :
            Has("06") ? UsbKind.Phone :
            Has("07") ? UsbKind.Printer :
            Has("E0") ? UsbKind.Bluetooth :
            UsbKind.Generic;
        return (kind, hub);
    }

    // ── Volumes ────────────────────────────────────────────────────────────────────────────

    private void OnVolumeAdded(DeviceWatcher sender, DeviceInformation info)
    {
        var container = ContainerOf(info.Properties);
        _host?.Post(() =>
        {
            if (_host is null) return;
            var volume = new Volume(container);
            _volumes[info.Id] = volume;
            _ = MountAsync(info.Id, volume);
        });
    }

    private void OnVolumeRemoved(DeviceWatcher sender, DeviceInformationUpdate update) => _host?.Post(() =>
    {
        if (_volumes.Remove(update.Id, out var volume)) RefreshContainer(volume.Container);
    });

    /// <summary>Le volume apparaît avant d'être monté : on attend ses lettres de lecteur.</summary>
    private async Task MountAsync(string id, Volume volume)
    {
        foreach (int delay in new[] { 0, 300, 700, 1500, 3000 })
        {
            await Task.Delay(delay);
            var roots = Volumes.RootsOf(id);
            if (roots.Count == 0) continue;
            _host?.Post(() =>
            {
                volume.Roots = roots;
                RefreshContainer(volume.Container);
            });
            return;
        }
    }

    private bool IsStorage(Device device)
        => device.Kind == UsbKind.Storage || _volumes.Values.Any(v => v.Container == device.Container && v.Container is not null);

    /// <summary>Espace réuni de tous les volumes montés du périphérique (via son conteneur).</summary>
    private StorageInfo? StorageOf(Device device)
    {
        if (device.Container is not Guid container || container == ComputerContainer) return null;
        var letters = new List<string>();
        ulong total = 0, free = 0;
        string label = "";
        foreach (var volume in _volumes.Values.Where(v => v.Container == container && v.Roots.Count > 0))
        {
            string root = volume.Roots[0];
            if (Volumes.Describe(root) is not { } info) continue;
            letters.Add(root.TrimEnd('\\'));
            total += info.Total;
            free += info.Free;
            if (label.Length == 0) label = info.Label;
        }
        return letters.Count == 0 ? null : new StorageInfo(string.Join(", ", letters.Order()), label, total, free);
    }

    private UsbState Describe(Device device, bool connected)
    {
        if (!connected) return new UsbState(device.InstanceId, device.LastTitle ?? device.Name, false, device.Kind, null, StorageState.None);
        var storage = StorageOf(device);
        var state = storage is not null ? StorageState.Known
            : !IsStorage(device) ? StorageState.None
            : Environment.TickCount64 - device.ConnectedAt < StorageWaitMs ? StorageState.Pending
            : StorageState.Unavailable;
        var result = new UsbState(device.InstanceId, device.Name, true, storage is not null ? UsbKind.Storage : device.Kind, storage, state);
        if (storage is not null) device.LastTitle = result.Title;
        return result;
    }

    private void RefreshContainer(Guid? container)
    {
        foreach (var device in _devices.Values.Where(d => d.Container == container && d.Container is not null)) Refresh(device);
    }

    /// <summary>Met à jour la carte affichée de ce périphérique (volume monté, délai écoulé…).</summary>
    private void Refresh(Device device)
    {
        if (_host is null || !device.Resolved || device.Hub || !_devices.ContainsKey(device.InterfaceId)) return;
        Host.Update(new UsbCard(Describe(device, connected: true)));
    }

    /// <summary>Règles du module (types de popup, stockages seulement, veille) puis affichage.</summary>
    private void Notify(UsbState state, bool connected)
    {
        var s = Host.Settings;
        if (connected ? !s.UsbNotifyConnect : !s.UsbNotifyDisconnect) return;
        if (s.UsbStorageOnly && state.Kind != UsbKind.Storage) return;
        if (_suspended || Environment.TickCount64 < _quietUntil)
        {
            Log.Write($"Popup USB ignoré (veille) : {state.Name}");
            return;
        }
        Host.Notify(new UsbCard(state));
    }

    // ── Listes ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Stockages USB connectés (les autres périphériques USB restent souvent branchés en permanence).</summary>
    private IEnumerable<UsbState> Storages() => _devices.Values
        .Where(d => d.Resolved && !d.Hub)
        .Select(d => Describe(d, connected: true))
        .Where(s => s.Storage is not null)
        .ToList();

    public override IEnumerable<CardContent> ListCards() => Storages().Select(s => new UsbCard(s));

    public override IEnumerable<HoverRow> HoverRows() => Storages().Select(s => (HoverRow)new UsbRow(s));

    public override IEnumerable<CardContent> PreviewCards() =>
    [
        new UsbCard(new UsbState("preview", "DataTraveler 3.0", true, UsbKind.Storage,
            new StorageInfo("E:", "KINGSTON", 32UL << 30, 19_660UL << 20), StorageState.Known)),
    ];
}
