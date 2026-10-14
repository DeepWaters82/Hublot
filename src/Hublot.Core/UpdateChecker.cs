using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Hublot.Common;

namespace HublotCore;

/// <summary>
/// Mises à jour de la version classique (Hublot-Setup.exe) : dernière version publiée sur GitHub (Releases),
/// téléchargée puis lancée — l'installateur fait la mise à jour. La version Store est mise à jour par le Store.
/// Tout se passe dans Hublot Core : le service reste sans réseau.
/// </summary>
internal static class UpdateChecker
{
    /// <summary>Dépôt GitHub « utilisateur/dépôt ». « OWNER » = pas encore configuré : aucune vérification.</summary>
    public const string Repository = "OWNER/Hublot";

    private const string AssetName = "Hublot-Setup.exe";

    public static bool Available => !AppPackage.IsPackaged && !Repository.StartsWith("OWNER/", StringComparison.Ordinal);

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(0, 0, 0);

    public sealed record Release(Version Version, string DownloadUrl);

    /// <summary>Dernière version publiée (tag « v3.1.0 » ou « 3.1.0 » avec Hublot-Setup.exe en pièce jointe), ou null.</summary>
    public static async Task<Release?> LatestAsync()
    {
        using var http = Client();
        string json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == AssetName)
                return new Release(new Version(version.Major, version.Minor, Math.Max(0, version.Build)), asset.GetProperty("browser_download_url").GetString() ?? "");
        }
        return null;
    }

    /// <summary>Télécharge l'installateur dans le dossier temporaire et le lance (il ferme Hublot pour le mettre à jour).</summary>
    public static async Task InstallAsync(Release release)
    {
        string file = Path.Combine(Path.GetTempPath(), $"Hublot-Setup-{release.Version}.exe");
        using (var http = Client())
        await using (var source = await http.GetStreamAsync(release.DownloadUrl))
        await using (var target = File.Create(file))
            await source.CopyToAsync(target);
        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// Vérification quotidienne demandée par le service (HublotCore.exe --check-update, sans fenêtre) : une
    /// nouvelle version est signalée une seule fois au service, qui affiche un popup.
    /// </summary>
    public static async Task CheckInBackgroundAsync()
    {
        if (!Available) return;
        try
        {
            if (await LatestAsync() is not { } release || release.Version <= Current) return;
            string marker = Path.Combine(Settings.DirectoryPath, "update.txt");
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == release.Version.ToString()) return;
            Directory.CreateDirectory(Settings.DirectoryPath);
            File.WriteAllText(marker, release.Version.ToString());
            Native.NotifyUpdate(release.Version);
        }
        catch (Exception)
        {
            // Hors ligne, GitHub indisponible… : on réessaiera demain.
        }
    }

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Hublot-Updater");
        return http;
    }
}
