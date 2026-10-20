using Hublot.Common;
using Windows.Gaming.Input;

namespace Hublot.Modules.Bluetooth;

/// <summary>
/// Manettes via Windows.Gaming.Input. Événementiel : ajout / retrait de contrôleur. Toutes les manettes : celles
/// de l'adaptateur Xbox sans fil (et Bluetooth) se déclarent sans fil ; celles d'un dongle 2,4 GHz (8BitDo, F710,
/// PowerA…) se déclarent filaires, comme une manette à câble (liaison « USB », Windows ne les distingue pas). Avec
/// beaucoup de dongles, la manette existe dès le branchement du dongle, allumée ou non. Les manettes Bluetooth
/// apparaissent aussi dans la source Bluetooth ; le doublon est filtré par le module (même nom, même état).
/// </summary>
internal sealed class XboxSource : DeviceSource
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (DeviceState State, long ConnectedAt)> _controllers = [];
    private readonly HashSet<string> _initial = [];
    private long _baselineUntil;

    public override string Name => "Manettes";

    public override void Start()
    {
        _baselineUntil = Environment.TickCount64 + 3000;
        RawGameController.RawGameControllerAdded += OnAdded;
        RawGameController.RawGameControllerRemoved += OnRemoved;
        // Windows rejoue « Added » pour les manettes déjà présentes : elles forment l'état initial.
        foreach (var controller in RawGameController.RawGameControllers)
        {
            var state = Describe(controller, connected: true);
            lock (_lock)
            {
                _controllers[state.Key] = (state, Environment.TickCount64);
                _initial.Add(state.Key);
            }
        }
    }

    public override void Stop()
    {
        RawGameController.RawGameControllerAdded -= OnAdded;
        RawGameController.RawGameControllerRemoved -= OnRemoved;
    }

    public override (DeviceState State, long ConnectedAt)? LastConnected()
    {
        lock (_lock)
        {
            return _controllers.Values.Where(c => c.State.Connected)
                .OrderByDescending(c => c.ConnectedAt)
                .Select(c => ((DeviceState, long)?)c)
                .FirstOrDefault();
        }
    }

    public override IReadOnlyList<(DeviceState State, long ConnectedAt)> ConnectedDevices()
    {
        lock (_lock)
        {
            return _controllers.Values.Where(c => c.State.Connected).OrderByDescending(c => c.ConnectedAt).ToList();
        }
    }

    private void OnAdded(object? sender, RawGameController controller)
    {
        var state = Describe(controller, connected: true);
        lock (_lock)
        {
            bool replay = _initial.Remove(state.Key) && Environment.TickCount64 < _baselineUntil;
            _controllers[state.Key] = (state, Environment.TickCount64);
            if (replay) return;
        }
        Log.Write($"Manette connectée : {state.Name} batterie={state.Battery}");
        Raise(state, connectionChanged: true);
    }

    private void OnRemoved(object? sender, RawGameController controller)
    {
        string key = KeyOf(controller);
        DeviceState state;
        lock (_lock)
        {
            if (!_controllers.TryGetValue(key, out var known)) return;
            state = DeviceState.Disconnected(key, known.State.Name, DeviceKind.Gamepad, known.State.Battery, known.State.Link);
            _controllers[key] = (state, known.ConnectedAt);
        }
        Log.Write($"Manette déconnectée : {state.Name}");
        Raise(state, connectionChanged: true);
    }

    private static string KeyOf(RawGameController controller) => "pad:" + controller.NonRoamableId;

    private static DeviceState Describe(RawGameController controller, bool connected)
    {
        int? battery = null;
        bool? charging = null;
        try
        {
            var report = controller.TryGetBatteryReport();
            if (report is not null) charging = report.Status == Windows.System.Power.BatteryStatus.Charging;
            if (report?.FullChargeCapacityInMilliwattHours is int full && full > 0
                && report.RemainingCapacityInMilliwattHours is int remaining)
                battery = Math.Clamp(remaining * 100 / full, 0, 100);
        }
        catch (Exception ex)
        {
            Log.Error("Batterie manette", ex);
        }
        string name = string.IsNullOrWhiteSpace(controller.DisplayName) ? L.T("Device.Controller") : controller.DisplayName;
        return new DeviceState(KeyOf(controller), name, connected, DeviceKind.Gamepad, battery,
            battery is null ? BatteryState.Unavailable : BatteryState.Known, controller.IsWireless ? DeviceLink.Dongle : DeviceLink.Usb, charging);
    }
}
