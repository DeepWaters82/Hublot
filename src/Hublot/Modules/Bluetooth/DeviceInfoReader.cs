using Hublot.Common;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Hublot.Modules.Bluetooth;

/// <summary>Lectures ponctuelles, à la demande : niveau de batterie (replis) et type d'appareil.</summary>
internal static class DeviceInfoReader
{
    private static readonly string[] BatteryProperties = [BluetoothMonitor.BatteryKey];
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Le nœud principal ne porte pas toujours le niveau : on cherche sur tous les nœuds du même
    /// conteneur, puis (LE uniquement) directement dans le service GATT Battery.
    /// </summary>
    public static async Task<int?> GetBatteryAsync(BtDevice device, bool allowGatt)
    {
        if (await FromContainerAsync(device) is int level) return level;
        if (allowGatt && device.IsLowEnergy && device.Connected) return await FromGattAsync(device);
        return null;
    }

    private static async Task<int?> FromContainerAsync(BtDevice device)
    {
        if (device.ContainerId is not Guid container) return null;
        try
        {
            var aqs = $"System.Devices.ContainerId:=\"{{{container}}}\"";
            var nodes = await DeviceInformation.FindAllAsync(aqs, BatteryProperties, DeviceInformationKind.Device);
            foreach (var node in nodes)
            {
                if (node.Properties.TryGetValue(BluetoothMonitor.BatteryKey, out var value)
                    && BluetoothMonitor.ToPercent(value) is int level)
                    return level;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Batterie PnP {device.Name}", ex);
        }
        return null;
    }

    /// <summary>
    /// Repli LE : lecture de la caractéristique Battery Level (0x2A19). Uniquement si l'appareil est
    /// déjà connecté, pour ne jamais provoquer de connexion.
    /// </summary>
    private static async Task<int?> FromGattAsync(BtDevice device)
    {
        if (device.Address is not ulong address) return null;
        BluetoothLEDevice? le = null;
        GattDeviceServicesResult? services = null;
        try
        {
            le = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask().WaitAsync(Timeout);
            if (le is null || le.ConnectionStatus != BluetoothConnectionStatus.Connected) return null;

            services = await le.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached)
                .AsTask().WaitAsync(Timeout);
            if (services.Status != GattCommunicationStatus.Success) return null;

            foreach (var service in services.Services)
            {
                var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached)
                    .AsTask().WaitAsync(Timeout);
                if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0) continue;

                var read = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().WaitAsync(Timeout);
                if (read.Status != GattCommunicationStatus.Success || read.Value.Length < 1) continue;

                using var reader = DataReader.FromBuffer(read.Value);
                return BluetoothMonitor.ToPercent(reader.ReadByte());
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Batterie GATT {device.Name}", ex);
        }
        finally
        {
            // Libérer tout de suite : un objet GATT conservé maintiendrait la liaison radio.
            if (services is not null)
                foreach (var s in services.Services) s.Dispose();
            le?.Dispose();
        }
        return null;
    }

    public static async Task<DeviceKind> GetKindAsync(BtDevice device)
    {
        if (device.Address is not ulong address) return DeviceKind.Generic;
        try
        {
            if (device.IsLowEnergy)
            {
                using var le = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                return le is null ? DeviceKind.Generic : FromAppearance(le.Appearance.Category, le.Appearance.SubCategory);
            }
            using var classic = await BluetoothDevice.FromBluetoothAddressAsync(address);
            return classic is null ? DeviceKind.Generic : FromClassOfDevice(classic.ClassOfDevice.RawValue);
        }
        catch (Exception ex)
        {
            Log.Error($"Type d'appareil {device.Name}", ex);
            return DeviceKind.Generic;
        }
    }

    /// <summary>Class of Device Bluetooth : classe majeure (bits 8-12) et mineure (bits 2-7).</summary>
    private static DeviceKind FromClassOfDevice(uint raw)
    {
        uint major = (raw >> 8) & 0x1F, minor = (raw >> 2) & 0x3F;
        return major switch
        {
            1 => DeviceKind.Computer,
            2 => DeviceKind.Phone,
            4 => minor switch
            {
                1 or 2 => DeviceKind.Headset,         // casque-micro, mains-libres
                5 or 7 or 8 or 10 => DeviceKind.Speaker,
                _ => DeviceKind.Headphones,
            },
            5 when (minor & 0x10) != 0 => DeviceKind.Keyboard,
            5 when (minor & 0x20) != 0 => DeviceKind.Mouse,
            5 => (minor & 0x0F) switch
            {
                1 or 2 => DeviceKind.Gamepad,
                5 => DeviceKind.Pen,                  // tablette graphique
                _ => DeviceKind.Generic,
            },
            _ => DeviceKind.Generic,
        };
    }

    /// <summary>Apparence GAP (Bluetooth LE) : catégorie sur 10 bits, sous-catégorie sur 6 bits.</summary>
    private static DeviceKind FromAppearance(ushort category, ushort sub) => category switch
    {
        0x01 => DeviceKind.Phone,
        0x02 => DeviceKind.Computer,
        0x0F => sub switch
        {
            1 => DeviceKind.Keyboard,
            2 => DeviceKind.Mouse,
            3 or 4 => DeviceKind.Gamepad,
            5 => DeviceKind.Pen,
            _ => DeviceKind.Generic,
        },
        0x21 => DeviceKind.Speaker,                   // Audio Sink
        0x25 => sub == 2 ? DeviceKind.Headset : DeviceKind.Headphones, // Wearable Audio Device
        _ => DeviceKind.Generic,
    };
}
