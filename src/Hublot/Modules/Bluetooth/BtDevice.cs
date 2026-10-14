using System.Globalization;

namespace Hublot.Modules.Bluetooth;

/// <summary>
/// Nœud PnP d'un appareil Bluetooth appairé : « BTHENUM\DEV_xxxxxxxxxxxx\… » (classique)
/// ou « BTHLE\DEV_xxxxxxxxxxxx\… » (Low Energy).
/// </summary>
/// <param name="ContainerId">Conteneur PnP : regroupe les nœuds classique + LE d'un même appareil physique.</param>
/// <param name="Battery">Niveau publié par Windows sur le nœud (DEVPKEY_Bluetooth_Battery), s'il existe.</param>
internal sealed record BtDevice(string Id, string Name, bool Connected, Guid? ContainerId, int? Battery)
{
    private const string ClassicPrefix = @"BTHENUM\DEV_";
    private const string LowEnergyPrefix = @"BTHLE\DEV_";

    public static bool IsDeviceNode(string id)
        => id.StartsWith(ClassicPrefix, StringComparison.OrdinalIgnoreCase)
        || id.StartsWith(LowEnergyPrefix, StringComparison.OrdinalIgnoreCase);

    public bool IsLowEnergy => Id.StartsWith(LowEnergyPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Adresse radio (12 chiffres hexadécimaux qui suivent « DEV_ »).</summary>
    public ulong? Address
    {
        get
        {
            int start = Id.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase) + 4;
            return start >= 4 && Id.Length >= start + 12
                && ulong.TryParse(Id.AsSpan(start, 12), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address)
                ? address : null;
        }
    }

    /// <summary>Clé de l'appareil physique : un appareil bimode expose deux nœuds pour un seul conteneur.</summary>
    public string Key => "bt:" + (ContainerId?.ToString() ?? Id);
}
