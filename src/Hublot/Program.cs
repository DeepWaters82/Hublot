using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot;

internal static class Program
{
    /// <summary>
    /// Arguments :
    ///   --log              journal dans %LocalAppData%\Hublot\log.txt
    ///   --demo             affiche un popup d'aperçu au démarrage
    ///   --snapshot DOSSIER [ÉCHELLE] rend les variantes du popup en PNG puis quitte (mise au point visuelle)
    ///   --icons FICHIER    planche PNG de l'icône à plusieurs tailles
    ///   --write-ico FICHIER écrit l'icône .ico de l'exécutable 
    ///
    /// Le démarrage automatique est géré par Hublot Core (raccourci dans le dossier Démarrage),
    /// pas par l'exécutable : un binaire qui s'inscrit lui-même dans la clé Run est pris pour
    /// une persistance malveillante par les antivirus d'entreprise.
    /// </summary>
    private static int Main(string[] args)
    {
        Settings.MigrateFromPreviousName(); // avant tout accès au dossier de données (le journal le crée)
        if (args.Contains("--log")) Log.Enable();

        if (args.Length >= 2 && args[0] is "--snapshot" or "--icons" or "--write-ico" or "--store-assets")
        {
            GdiPlus.Startup();
            try
            {
                switch (args[0])
                {
                    case "--snapshot":
                        // --snapshot DOSSIER [ÉCHELLE] [en|fr] : langue des cartes
                        if (args.Length > 3) L.Apply(args[3] == "en" ? AppLanguage.English : AppLanguage.French);
                        Snapshot.Render(args[1], args.Length > 2 && float.TryParse(args[2], System.Globalization.CultureInfo.InvariantCulture, out var k) ? k : 1.5f);
                        break;
                    case "--icons": TrayIconRenderer.RenderSheet(args[1]); break;
                    case "--store-assets": TrayIconRenderer.WriteStoreAssets(args[1]); break;
                    default: TrayIconRenderer.WriteIco(args[1]); break;
                }
            }
            finally { GdiPlus.Shutdown(); }
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\Hublot.SingleInstance", out bool first);
        if (!first) return 0;

        GdiPlus.Startup();
        try
        {
            return new App().Run(demo: args.Contains("--demo"), demoList: args.Contains("--demo-list"));
        }
        catch (Exception ex)
        {
            Log.Error("Fatal", ex);
            return 1;
        }
        finally
        {
            GdiPlus.Shutdown();
        }
    }
}
