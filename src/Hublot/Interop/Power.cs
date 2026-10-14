using System.Runtime.InteropServices;

namespace Hublot.Interop;

/// <summary>État de l'alimentation (GetSystemPowerStatus).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    /// <summary>0 = batterie, 1 = secteur, 255 = inconnu.</summary>
    public byte ACLineStatus;
    /// <summary>Indicateurs : 8 = en charge, 128 = pas de batterie, 255 = inconnu.</summary>
    public byte BatteryFlag;
    /// <summary>0-100, 255 = inconnu.</summary>
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    /// <summary>Autonomie restante en secondes, -1 = inconnue.</summary>
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
{
    public delegate* unmanaged<nint, uint, nint, uint> Callback;
    public nint Context;
}

internal static unsafe partial class PowerApi
{
    public const uint DEVICE_NOTIFY_CALLBACK = 2;
    public const uint PBT_POWERSETTINGCHANGE = 0x8013;
    public const int ComputerNameDnsHostname = 1;

    /// <summary>GUID_ACDC_POWER_SOURCE : données DWORD, 0 = secteur, 1 = batterie, 2 = onduleur.</summary>
    public static readonly Guid AcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    /// <summary>GUID_BATTERY_PERCENTAGE_REMAINING : données DWORD, pourcentage.</summary>
    public static readonly Guid BatteryPercentage = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetComputerNameExW(int format, char* buffer, ref uint size);

    /// <summary>Abonnement à un paramètre d'alimentation, rappel sur un thread du système (pas de fenêtre).</summary>
    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSettingRegisterNotification(Guid* setting, uint flags, DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS* recipient, out nint handle);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSettingUnregisterNotification(nint handle);
}
