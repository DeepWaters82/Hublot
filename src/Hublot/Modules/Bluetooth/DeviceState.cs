using Hublot.Common;

namespace Hublot.Modules.Bluetooth;

internal enum DeviceKind { Generic, Headphones, Headset, Speaker, Keyboard, Mouse, Gamepad, Phone, Computer, Pen, Presenter, Dongle }

internal enum BatteryState { Pending, Known, Unavailable }

/// <summary>Motif d'un popup qui n'est pas un simple changement de connexion.</summary>
internal enum NotifyReason { None, NewDevice, LowBattery, Charging, ChargeReached }

/// <summary>
/// Ce que le popup affiche d'un appareil, quelle que soit sa liaison (Bluetooth, récepteur USB…).
/// </summary>
/// <param name="Key">Identifiant stable de l'appareil physique, unique toutes sources confondues.</param>
/// <param name="Charging">En charge : fourni par Logitech / Xbox ; null si la liaison ne le dit pas (Bluetooth).</param>
internal sealed record DeviceState(string Key, string Name, bool Connected, DeviceKind Kind, int? Battery, BatteryState BatteryState,
    DeviceLink Link = DeviceLink.Bluetooth, bool? Charging = null, NotifyReason Reason = NotifyReason.None)
{
    public static DeviceState Disconnected(string key, string name, DeviceKind kind, int? lastBattery, DeviceLink link)
        => new(key, name, false, kind, lastBattery, lastBattery is null ? BatteryState.Unavailable : BatteryState.Known, link);
}

/// <summary>Une famille d'appareils surveillée (événementiel : aucune source ne sonde en boucle).</summary>
internal abstract class DeviceSource
{
    /// <summary>
    /// Nouvel état d'un appareil. <c>connectionChanged</c> : vrai pour une connexion/déconnexion
    /// (popup), faux pour un simple complément d'information (nom, type, batterie).
    /// Peut être levé depuis n'importe quel thread.
    /// </summary>
    public event Action<DeviceState, bool>? Changed;

    public abstract string Name { get; }

    public abstract void Start();

    public abstract void Stop();

    /// <summary>Appareil connecté le plus récemment et l'instant de sa connexion (Environment.TickCount64).</summary>
    public abstract (DeviceState State, long ConnectedAt)? LastConnected();

    /// <summary>Tous les appareils connectés, du plus récemment connecté au plus ancien.</summary>
    public abstract IReadOnlyList<(DeviceState State, long ConnectedAt)> ConnectedDevices();

    /// <summary>Tous les appareils connus de la source (appairés, même déconnectés), sans popup.</summary>
    public virtual IReadOnlyList<DeviceState> KnownDevices() => ConnectedDevices().Select(x => x.State).ToList();

    protected void Raise(DeviceState state, bool connectionChanged) => Changed?.Invoke(state, connectionChanged);
}
