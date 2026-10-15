using Hublot.Interop;
using Windows.ApplicationModel;
using Windows.Media;

namespace Hublot.Modules.NowPlaying;

/// <summary>
/// Identité des applications média. Windows ne donne que l'AppUserModelId de la source :
/// « Spotify.exe », « Chrome », « MSEdge », « SpotifyAB.SpotifyMusic_…!Spotify »…
/// </summary>
internal static class MediaApps
{
    // AUMID → (nom affiché, nom de processus). Firefox s'identifie par une empreinte de son dossier d'installation.
    private static readonly Dictionary<string, (string Name, string Process)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chrome"] = ("Google Chrome", "chrome"),
        ["MSEdge"] = ("Microsoft Edge", "msedge"),
        ["Brave"] = ("Brave", "brave"),
        ["Opera"] = ("Opera", "opera"),
        ["308046B0AF4A39CB"] = ("Firefox", "firefox"),
        ["Spotify.exe"] = ("Spotify", "spotify"),
        ["vlc.exe"] = ("VLC", "vlc"),
    };

    /// <summary>Navigateurs : un onglet n'a pas d'identité propre, la session porte l'AUMID du navigateur.</summary>
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chrome", "ChromeBeta", "ChromeDev", "ChromeCanary", "Chromium", "MSEdge", "MSEdgeBeta", "MSEdgeDev",
        "Brave", "BraveBeta", "Opera", "OperaGX", "Vivaldi", "308046B0AF4A39CB",
    };

    private static readonly string[] BrowserNames = ["Firefox", "Google Chrome", "Microsoft Edge", "Brave", "Opera", "Vivaldi", "Chromium"];

    /// <summary>
    /// Plateformes musicales reconnues (nom affiché ou AUMID) : applications de bureau, du Store et applications
    /// web installées. « Apple Music » est aussi le nom affiché de « AppleInc.AppleMusicWin_…!App ».
    /// </summary>
    private static readonly string[] MusicPlatforms =
    [
        "Spotify", "Deezer", "Apple Music", "iTunes", "Amazon Music", "AmazonMusic", "YouTube Music", "TIDAL", "Qobuz",
        "SoundCloud", "Pandora", "Napster",
    ];

    /// <summary>Services et lecteurs vidéo (nom affiché ou AUMID) : applications du Store et applications web installées.</summary>
    private static readonly string[] VideoServices =
    [
        "Netflix", "Prime Video", "PrimeVideo", "AmazonVideo", "Disney", "Twitch", "ZuneVideo", "Films et TV", "Films & TV",
        "Movies & TV", "Apple TV", "AppleTV", "Plex", "Crunchyroll", "Canal+", "myCANAL", "Molotov", "Hulu", "Paramount",
        "Dailymotion", "Vimeo", "france.tv", "Arte", "Kick", "Pluto TV", "Rakuten TV", "OQEE", "Salto",
    ];

    /// <summary>
    /// La session vient-elle d'une plateforme musicale ? Renvoie la raison du refus, ou null. Windows ne donne ni
    /// l'adresse de l'onglet ni le site : on s'appuie sur ce que l'application déclare et sur ce qu'on sait d'elle.
    /// <list type="number">
    /// <item>type déclaré « vidéo » (lecteurs natifs : Films et TV, Lecteur multimédia, VLC…) ;</item>
    /// <item>application vidéo connue (Netflix, Prime Video, Disney+, YouTube — pas YouTube Music…) ;</item>
    /// <item>onglet de navigateur sans album : les sites musicaux (YouTube Music, Spotify, Deezer, Apple Music…)
    /// renseignent l'album, les sites vidéo (YouTube, Netflix, Twitch…) non. Chromium déclare tout comme
    /// « musique », le type n'aide donc pas pour les navigateurs ;</item>
    /// <item>autre application : seulement une plateforme musicale connue (<see cref="MusicPlatforms"/>). Un
    /// lecteur local (VLC, Lecteur multimédia…) se règle sur « Tout afficher » dans les paramètres.</item>
    /// </list>
    /// </summary>
    public static string? NotMusicReason(string appId, string appName, MediaPlaybackType? type, string album)
    {
        if (type is MediaPlaybackType.Video or MediaPlaybackType.Image) return "vidéo déclarée";
        bool youTubeVideo = HasWord(appName, "YouTube") && !HasWord(appName, "Music");
        if (youTubeVideo || VideoServices.Any(v => HasWord(appName, v) || HasWord(appId, v))) return "service vidéo";
        bool browser = Browsers.Contains(appId) || BrowserNames.Any(b => appName.Equals(b, StringComparison.OrdinalIgnoreCase));
        if (browser) return string.IsNullOrWhiteSpace(album) ? NoAlbum : null;
        return MusicPlatforms.Any(m => HasWord(appName, m) || HasWord(appId, m)) ? null : "pas une plateforme musicale";
    }

    public const string NoAlbum = "onglet sans album";

    /// <summary>
    /// <paramref name="word"/> dans <paramref name="s"/>, sans lettre accolée : « Plex » ne doit pas
    /// trouver « Perplexity », mais « Netflix » doit trouver « 4DF9E0F8.Netflix_mcm4njqhnhss8!Netflix.App ».
    /// </summary>
    private static bool HasWord(string s, string word)
    {
        for (int i = s.IndexOf(word, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = s.IndexOf(word, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            int end = i + word.Length;
            if ((i == 0 || !char.IsLetter(s[i - 1])) && (end == s.Length || !char.IsLetter(s[end]))) return true;
        }
        return false;
    }

    /// <summary>Taille de l'icône demandée au shell : nette jusqu'à 20 DIP à 200 %.</summary>
    public const int IconSize = 48;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Name, Cover? Icon)> Identities =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Nom et icône de l'application, d'abord d'après le dossier « Applications » du shell (qui connaît
    /// les applications web : « Brave._crx_… » → « YouTube Music »), sinon d'après l'identifiant.
    /// Résultat mis en cache ; appelable depuis n'importe quel thread.
    /// </summary>
    public static (string Name, Cover? Icon) Resolve(string appId) => Identities.GetOrAdd(appId, id =>
    {
        var (shellName, pixels) = ShellItems.Describe(id, IconSize);
        string name = !string.IsNullOrWhiteSpace(shellName) && shellName != id ? shellName
            : Known.TryGetValue(id, out var known) ? known.Name
            : TryPackagedName(id) ?? Fallback(id);
        return (name, pixels is null ? null : new Cover(pixels, IconSize));
    });

    public static string DisplayName(string appId) => Resolve(appId).Name;

    /// <summary>Application empaquetée (Microsoft Store, lecteur Windows…) : nom officiel.</summary>
    private static string? TryPackagedName(string appId)
    {
        if (!appId.Contains('!')) return null;
        try
        {
            var info = AppInfo.GetFromAppUserModelId(appId);
            return string.IsNullOrWhiteSpace(info?.DisplayInfo?.DisplayName) ? null : info.DisplayInfo.DisplayName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>« Spotify.exe » → « Spotify » ; « Editeur.Appli_xyz!App » → « Appli ».</summary>
    private static string Fallback(string appId)
    {
        string s = appId;
        int bang = s.IndexOf('!');
        if (bang > 0)
        {
            s = s[..bang];
            int underscore = s.LastIndexOf('_');
            if (underscore > 0) s = s[..underscore];
            int dot = s.LastIndexOf('.');
            if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        }
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : appId;
    }

    /// <summary>Session d'un navigateur lui-même (un onglet), pas d'une application web installée.</summary>
    public static bool IsBrowser(string appId) =>
        Browsers.Contains(appId) || BrowserNames.Any(b => DisplayName(appId).Equals(b, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// L'application source est-elle au premier plan (popup inutile : le titre y est déjà visible) ?
    /// <list type="bullet">
    /// <item>onglet de navigateur : jamais — Windows ne dit pas quel onglet joue, et le navigateur au premier
    /// plan affiche sans doute autre chose (le plein écran reste géré par la règle générale) ;</item>
    /// <item>fenêtre qui déclare le même AppUserModelId (application web installée, Store) : oui ;</item>
    /// <item>sinon, même processus (Spotify, VLC…). Une application web installée n'est pas comparée par
    /// processus : « brave.exe » est aussi le navigateur.</item>
    /// </list>
    /// </summary>
    public static unsafe bool IsForeground(string appId)
    {
        var window = User32.GetForegroundWindow();
        if (window == 0 || IsBrowser(appId)) return false;
        if (string.Equals(ShellItems.WindowAppId(User32.GetAncestor(window, User32.GA_ROOT)), appId, StringComparison.OrdinalIgnoreCase)) return true;
        if (appId.Contains("_crx_", StringComparison.OrdinalIgnoreCase)) return false;
        uint pid;
        User32.GetWindowThreadProcessId(window, &pid);
        string process;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch (Exception)
        {
            return false;
        }
        if (Known.TryGetValue(appId, out var known)) return process.Equals(known.Process, StringComparison.OrdinalIgnoreCase);
        string id = appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appId[..^4] : appId;
        return id.Contains(process, StringComparison.OrdinalIgnoreCase) || process.Contains(id, StringComparison.OrdinalIgnoreCase);
    }
}
