namespace Hublot.Common;

/// <summary>
/// Journal de diagnostic, désactivé par défaut (aucune E/S). Activé par l'argument --log :
/// %LocalAppData%\Hublot\log.txt
/// </summary>
internal static class Log
{
    private static readonly object s_lock = new();
    private static string? s_path;

    public static void Enable()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hublot");
        Directory.CreateDirectory(dir);
        s_path = Path.Combine(dir, "log.txt");
    }

    public static void Write(string message)
    {
        if (s_path is null) return;
        lock (s_lock)
        {
            try { File.AppendAllText(s_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}"); }
            catch (IOException) { }
        }
    }

    public static void Error(string context, Exception ex) => Write($"ERREUR {context} : {ex.GetType().Name} {ex.Message}");
}
