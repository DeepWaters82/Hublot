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
        if (args.Contains("--log")) Log.Force();

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

        if (args.Length >= 2 && args[0] == "--audio")
        {
            // --audio FICHIER : points de terminaison audio (état, sens, nom, adaptateur, bus), pour diagnostiquer la prise jack.
            var lines = new List<string>();
            if (Interop.CoreAudio.Subscribe((_, _) => { }))
            {
                foreach (var (id, state) in Interop.CoreAudio.States())
                    if (Interop.CoreAudio.Describe(id) is { } e)
                    {
                        lines.Add($"{state}\t{(e.Render ? "sortie" : "entrée")}\t{e.Name}\t{e.Adapter}\t{e.Bus}");
                        foreach (var j in Interop.CoreAudio.Jacks(id) ?? [])
                            lines.Add($"\tprise {j.Index} : couleur {j.Color:X6} type {j.Connection} emplacement {j.Location} branchée {j.Connected}");
                    }
                Interop.CoreAudio.Unsubscribe();
            }
            else lines.Add("API audio indisponible");
            File.WriteAllLines(args[1], lines);
            return 0;
        }

        if (args.Length >= 2 && args[0] == "--gamepads")
        {
            // --gamepads FICHIER : manettes vues par Windows.Gaming.Input (la liste se remplit après quelques instants).
            var seen = new List<string>();
            Windows.Gaming.Input.RawGameController.RawGameControllerAdded += (_, c) =>
            {
                lock (seen) seen.Add($"{c.DisplayName}\tsans fil={c.IsWireless}\tVID={c.HardwareVendorId:X4} PID={c.HardwareProductId:X4}\t{c.NonRoamableId}");
            };
            Thread.Sleep(2500);
            lock (seen) File.WriteAllLines(args[1], seen.Count > 0 ? seen : ["aucune manette"]);
            return 0;
        }

        if (args.Length >= 3 && args[0] == "--audio-watch")
        {
            // --audio-watch FICHIER SECONDES : journal des signaux audio (états, propriétés) pendant le branchement d'une prise.
            var path = args[1];
            void Write(string line)
            {
                lock (path) File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            // Le module lui-même, avec un hôte qui écrit les popups au lieu de les afficher (journal dans le même fichier).
            Log.Enable(path);
            var module = new Modules.Audio.JackModule();
            module.Start(new Modules.WatchHost(card => Write($"POPUP {((Modules.Audio.JackCard)card).State.Title} : {((Modules.Audio.JackCard)card).State.StatusText}")));
            Thread.Sleep(TimeSpan.FromSeconds(int.TryParse(args[2], out int s) ? s : 120));
            module.Stop();
            Write("fin");
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
