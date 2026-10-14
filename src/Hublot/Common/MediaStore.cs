using System.Globalization;
using System.IO;

namespace Hublot.Common;

/// <summary>Application qui a déjà déclaré un média à Windows (liste d'exclusion de Hublot Core).</summary>
public sealed record MediaApp(string Id, string Name, DateTime LastSeen);

/// <summary>
/// media-apps.tsv : écrit par le service (module « Lecture en cours »), lu par Hublot Core.
/// Compilé dans les deux projets : aucune dépendance au reste du service.
/// </summary>
public static class MediaStore
{
    public static string AppsPath => Path.Combine(Settings.DirectoryPath, "media-apps.tsv");

    private const int ForgetAfterDays = 30;

    public static List<MediaApp> LoadApps()
    {
        var list = new List<MediaApp>();
        try
        {
            if (!File.Exists(AppsPath)) return list;
            using var stream = new FileStream(AppsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            foreach (var line in reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = line.TrimEnd('\r').Split('\t');
                if (f.Length < 3) continue;
                var seen = DateTime.TryParse(f[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.MinValue;
                // Pas revue depuis plus de 30 jours (application désinstallée, essai…) : oubliée.
                if (DateTime.Now - seen > TimeSpan.FromDays(ForgetAfterDays)) continue;
                list.Add(new MediaApp(f[0], f[1], seen));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return list;
    }

    public static void SaveApps(IEnumerable<MediaApp> apps)
    {
        Directory.CreateDirectory(Settings.DirectoryPath);
        string temp = AppsPath + ".tmp";
        File.WriteAllLines(temp, apps.Select(a => string.Join('\t',
            a.Id.Replace('\t', ' '), a.Name.Replace('\t', ' '), a.LastSeen.ToString("o", CultureInfo.InvariantCulture))));
        File.Move(temp, AppsPath, overwrite: true);
    }
}
