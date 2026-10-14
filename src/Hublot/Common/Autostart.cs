namespace Hublot.Common;

/// <summary>
/// État du démarrage automatique, en lecture seule. L'inscription elle-même est faite par
/// install.ps1 (raccourci dans le dossier Démarrage) : un exécutable qui s'inscrit lui-même
/// au démarrage est classé comme persistance malveillante par les antivirus d'entreprise.
/// </summary>
internal static class Autostart
{
    public const string ShortcutName = "Hublot.lnk";

    public static bool IsInstalled =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName));
}
