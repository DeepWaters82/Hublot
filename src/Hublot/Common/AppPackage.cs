using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Hublot.Common;

/// <summary>
/// Version Microsoft Store (paquet MSIX) ou version classique (Hublot-Setup.exe) ? Dans le paquet :
/// démarrage automatique par la tâche de démarrage du paquet (pas de raccourci), mises à jour par le Store,
/// et %LocalAppData% redirigé vers le dossier du paquet (LocalCache\Local).
/// </summary>
public static class AppPackage
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    /// <summary>Identifiant de la tâche de démarrage déclarée dans AppxManifest.xml.</summary>
    public const string StartupTaskId = "HublotStartup";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int length, StringBuilder? name);

    /// <summary>Nom de famille du paquet (« 12345Editeur.Hublot_abc… »), ou null hors paquet.</summary>
    public static string? FamilyName { get; } = ReadFamilyName();

    public static bool IsPackaged => FamilyName is not null;

    private static string? ReadFamilyName()
    {
        try
        {
            int length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) == APPMODEL_ERROR_NO_PACKAGE || length == 0) return null;
            var name = new StringBuilder(length);
            return GetCurrentPackageFamilyName(ref length, name) == 0 ? name.ToString() : null;
        }
        catch (Exception)
        {
            return null; // Windows trop ancien : pas de paquet
        }
    }

    /// <summary>
    /// Dossier réel des données vu de l'extérieur du paquet (l'Explorateur ne voit pas la redirection) :
    /// %LocalAppData%\Packages\&lt;famille&gt;\LocalCache\Local\Hublot ; hors paquet, le dossier habituel.
    /// </summary>
    public static string DataFolderForExplorer => FamilyName is { } family
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, "LocalCache", "Local", "Hublot")
        : Settings.DirectoryPath;
}
