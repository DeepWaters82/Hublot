using System.Globalization;

namespace Hublot.Modules.Usb;

/// <summary><c>Receiver</c> : récepteur sans fil (dongle) d'un clavier, d'une souris, d'un casque…</summary>
internal enum UsbKind { Generic, Storage, Keyboard, Mouse, Audio, Webcam, Phone, Printer, Bluetooth, Gamepad, Receiver }

internal enum StorageState { None, Pending, Known, Unavailable }

/// <summary>Espace d'un stockage USB (tous ses volumes réunis).</summary>
/// <param name="Letters">Lettres de lecteur (« E: », « F: »).</param>
internal sealed record StorageInfo(string Letters, string Label, ulong Total, ulong Free)
{
    public ulong Used => Total > Free ? Total - Free : 0;
    public float UsedRatio => Total == 0 ? 0 : (float)Used / Total;
}

/// <summary>Ce que la carte affiche d'un périphérique USB.</summary>
/// <param name="Key">Identifiant d'instance PnP du périphérique (« USB\VID_…\… »).</param>
internal sealed record UsbState(string Key, string Name, bool Connected, UsbKind Kind, StorageInfo? Storage, StorageState StorageState)
{
    /// <summary>Titre de la carte : nom du volume et lettre pour un stockage, comme dans l'Explorateur.</summary>
    public string Title => Storage is { } s
        ? $"{(string.IsNullOrWhiteSpace(s.Label) ? Name : s.Label)} ({s.Letters})"
        : Name;

    /// <summary>« 19,6 Go » / « 19.6 GB » selon la langue (Strings.cs).</summary>
    public static string Size(ulong bytes) => L.Size(bytes);
}
