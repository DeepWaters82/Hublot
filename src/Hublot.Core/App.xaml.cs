using System.Diagnostics;
using System.Windows;

namespace HublotCore;

/// <summary>
/// Trois rôles pour un même exécutable :
/// - « --uninstall » : désinstallation (lancée depuis « Applications installées ») ;
/// - hors du dossier d'installation, avec le service embarqué : installateur (Hublot-Setup.exe) ;
/// - sinon : utilitaire de paramètres (« --page Nom » pour ouvrir une page).
/// </summary>
public partial class App : Application
{
    private Mutex? _single;

    // Langue choisie appliquée avant la lecture d'App.xaml (libellés « Activé / Désactivé » des interrupteurs).
    static App()
    {
        Hublot.Common.Settings.Load();
        // Capture de la page GitHub en anglais, sans toucher à la langue choisie.
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--screenshot") && args.Contains("en")) Hublot.Common.L.Apply(Hublot.Common.AppLanguage.English);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--check-update"))
        {
            // Vérification quotidienne demandée par le service : sans fenêtre, puis on quitte.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = UpdateChecker.CheckInBackgroundAsync().ContinueWith(_ => Dispatcher.Invoke(Shutdown));
            return;
        }

        if (e.Args.Contains("--uninstall"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var dialog = new UninstallDialog();
            if (dialog.ShowDialog() == true)
            {
                Installer.Uninstall(keepSettings: dialog.KeepSettings);
                Installer.RemoveInstallFolderAfterExit(); // attend la fermeture de cet exécutable
                MessageBox.Show(Hublot.Common.L.T("Uninstall.Done"), "Hublot", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        int shot = Array.IndexOf(e.Args, "--screenshot");
        if (shot >= 0 && shot + 2 < e.Args.Length)
        {
            // --screenshot PAGE FICHIER [en] : image d'une page entière, sans afficher la fenêtre à l'écran.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SettingsViewModel.Demo = true;
            var hidden = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 1100,
                ShowActivated = false, ShowInTaskbar = false,
            };
            hidden.Show();
            hidden.SaveScreenshot(e.Args[shot + 1], e.Args[shot + 2], 1.5, Shutdown);
            return;
        }

        if ((!Installer.IsInstalledCopy && Installer.HasPayload) || e.Args.Contains("--setup"))
        {
            MainWindow = new SetupWindow();
            MainWindow.Show();
            return;
        }

        // Une seule fenêtre de paramètres : une seconde ouverture remet la première au premier plan.
        _single = new Mutex(true, @"Local\Hublot.Settings", out bool first);
        if (!first)
        {
            Native.ActivateExisting();
            Shutdown();
            return;
        }
        Hublot.Common.Settings.MigrateFromPreviousName();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        int page = Array.IndexOf(e.Args, "--page");
        if (page >= 0 && page + 1 < e.Args.Length) window.ShowPage(e.Args[page + 1]);
    }

    /// <summary>
    /// Rouvre la fenêtre des paramètres (nouvelle langue) : libère d'abord le verrou d'instance unique, sinon
    /// le nouveau processus se contenterait de réactiver celui-ci avant qu'il ne se ferme.
    /// </summary>
    public void Restart(string page)
    {
        _single?.ReleaseMutex();
        _single?.Dispose();
        _single = null;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--page {page}") { UseShellExecute = true })?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
