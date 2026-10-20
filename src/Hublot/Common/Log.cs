namespace Hublot.Common;

/// <summary>
/// Journal de diagnostic, désactivé par défaut (aucune E/S). Activé par l'argument --log ou par l'option
/// « Journal de diagnostic » (Hublot Core › À propos) : %LocalAppData%\Hublot\log.txt, limité à 1 Mo (l'ancien
/// journal devient log.old.txt).
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1 << 20;

    private static readonly object s_lock = new();
    private static string? s_path;
    private static bool s_forced;
    private static int s_writes;

    /// <summary>Option « Journal de diagnostic » : active ou coupe le journal, sauf s'il a été demandé par --log.</summary>
    public static void Configure(bool enabled)
    {
        if (s_forced) return;
        if (enabled) Enable();
        else s_path = null;
    }

    /// <summary>Argument --log : journal actif quelle que soit l'option.</summary>
    public static void Force()
    {
        s_forced = true;
        Enable();
    }

    /// <summary>Journal dans log.txt du dossier de données, ou dans <paramref name="path"/> (diagnostics).</summary>
    public static void Enable(string? path = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hublot");
        if (path is null) Directory.CreateDirectory(dir);
        s_path = path ?? Path.Combine(dir, "log.txt");
    }

    public static void Write(string message)
    {
        if (s_path is null) return;
        lock (s_lock)
        {
            try
            {
                // Taille vérifiée de temps en temps : le journal ne grossit pas indéfiniment.
                if (++s_writes % 200 == 0 && new FileInfo(s_path).Length > MaxBytes)
                    File.Move(s_path, Path.ChangeExtension(s_path, ".old.txt"), overwrite: true);
                File.AppendAllText(s_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
        }
    }

    public static void Error(string context, Exception ex) => Write($"ERREUR {context} : {ex.GetType().Name} {ex.Message}");
}
