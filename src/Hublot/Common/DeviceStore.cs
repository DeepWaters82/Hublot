using System.Globalization;
using System.IO;

namespace Hublot.Common;

public enum DeviceLink { Bluetooth, Dongle }

/// <summary>Appareil déjà vu par le service (liste « appareils » de l'utilitaire, sourdine).</summary>
public sealed record KnownDevice(string Key, string Name, string Kind, DeviceLink Link, int? Battery, bool Connected,
    DateTime FirstSeen, DateTime LastSeen);

/// <summary>
/// Fichiers texte (TSV) écrits par le service et lus par l'utilitaire de paramètres.
/// Compilé dans les deux projets : aucune dépendance au reste du service.
/// </summary>
public static class DeviceStore
{
    public static string DevicesPath => Path.Combine(Settings.DirectoryPath, "devices.tsv");

    public static List<KnownDevice> LoadDevices()
    {
        var list = new List<KnownDevice>();
        foreach (var f in ReadRows(DevicesPath))
        {
            if (f.Length < 8 || !Enum.TryParse<DeviceLink>(f[3], out var link)) continue;
            list.Add(new KnownDevice(f[0], f[1], f[2], link, ParseBattery(f[4]), f[5] == "1", ParseTime(f[6]), ParseTime(f[7])));
        }
        return list;
    }

    public static void SaveDevices(IEnumerable<KnownDevice> devices)
    {
        Directory.CreateDirectory(Settings.DirectoryPath);
        var lines = devices.Select(d => string.Join('\t',
            Clean(d.Key), Clean(d.Name), d.Kind, d.Link, d.Battery?.ToString(CultureInfo.InvariantCulture) ?? "",
            d.Connected ? "1" : "0", Time(d.FirstSeen), Time(d.LastSeen)));
        string temp = DevicesPath + ".tmp";
        File.WriteAllLines(temp, lines);
        File.Move(temp, DevicesPath, overwrite: true);
    }

    private static IEnumerable<string[]> ReadRows(string path)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path)) yield break;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }
        foreach (var line in lines) yield return line.TrimEnd('\r').Split('\t');
    }

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string Time(DateTime t) => t.ToString("o", CultureInfo.InvariantCulture);

    private static DateTime ParseTime(string s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.MinValue;

    private static int? ParseBattery(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : null;
}
