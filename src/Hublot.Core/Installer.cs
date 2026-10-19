using System.Diagnostics;
using Hublot.Common;
using System.IO;
using Microsoft.Win32;

namespace HublotCore;

/// <summary>
/// Installation par utilisateur, sans droits administrateur. Hublot-Setup.exe est l'utilitaire de
/// paramètres lui-même, qui embarque le service (ressource « Hublot.exe ») :
///   %LocalAppData%\Programs\Hublot\Hublot.exe          service (extrait de la ressource)
///   %LocalAppData%\Programs\Hublot\HublotCore.exe  copie de l'installateur = paramètres
/// + raccourcis (menu Démarrer, dossier Démarrage) + entrée « Applications installées » (HKCU).
/// </summary>
internal static class Installer
{
    private const string PayloadName = "Hublot.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hublot";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Hublot");

    public static string ServicePath => Path.Combine(InstallDir, "Hublot.exe");
    public static string SettingsPath => Path.Combine(InstallDir, "HublotCore.exe");
    private static string MenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hublot.lnk");
    /// <summary>Raccourci du menu Démarrer des versions 2.0 à 2.4.</summary>
    private static string OldMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hublot Core.lnk");
    public static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Exécuté depuis le dossier d'installation : simple utilitaire de paramètres.</summary>
    public static bool IsInstalledCopy =>
        string.Equals(Path.GetDirectoryName(Environment.ProcessPath), InstallDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>Le service est embarqué (build de publication) : cet exécutable peut installer.</summary>
    public static bool HasPayload => typeof(Installer).Assembly.GetManifestResourceInfo(PayloadName) is not null;

    public static bool IsInstalled => File.Exists(ServicePath);

    public static void Install(bool startWithWindows, Action<string> progress)
    {
        progress(L.T("Setup.Stopping"));
        StopProcesses("Hublot", "HublotCore", "WinTooth", "WinToothSettings", "BluetoothPopup", "BluetoothPopupSettings");
        RemovePreviousName();

        progress(L.T("Setup.Copying"));
        Directory.CreateDirectory(InstallDir);
        using (var payload = typeof(Installer).Assembly.GetManifestResourceStream(PayloadName)!)
        using (var file = File.Create(ServicePath))
            payload.CopyTo(file);
        if (!string.Equals(Environment.ProcessPath, SettingsPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(Environment.ProcessPath!, SettingsPath, overwrite: true);

        progress(L.T("Setup.Shortcuts"));
        TryDelete(OldMenuShortcut);
        Native.CreateShortcut(MenuShortcut, SettingsPath, "Hublot");
        Native.SetStartup(startWithWindows, ServicePath);
        Native.RefreshShellIcons(ServicePath, SettingsPath, MenuShortcut);

        progress(L.T("Setup.Register"));
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Hublot");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Hublot");
            key.SetValue("DisplayIcon", ServicePath);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{SettingsPath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            long size = new DirectoryInfo(InstallDir).GetFiles().Sum(f => f.Length) / 1024;
            key.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
        }

        progress(L.T("Setup.Starting"));
        Process.Start(new ProcessStartInfo(ServicePath) { UseShellExecute = true, WorkingDirectory = InstallDir })?.Dispose();
    }

    /// <summary>
    /// Désinstallation (depuis « Applications installées ») : fichiers, raccourcis, et données sauf si
    /// l'utilisateur choisit de les conserver (réinstallation : tout est retrouvé).
    /// </summary>
    public static void Uninstall(bool keepSettings)
    {
        StopProcesses("Hublot");
        Native.SetStartup(false, ServicePath);
        TryDelete(MenuShortcut);
        TryDelete(OldMenuShortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        TryDelete(ServicePath);
        if (keepSettings) return;
        try { Directory.Delete(Hublot.Common.Settings.DirectoryPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Cet exécutable fait partie du dossier à supprimer : un processus cmd réessaie chaque seconde
    /// (5 min au plus) jusqu'à ce qu'il soit fermé.
    /// </summary>
    public static void RemoveInstallFolderAfterExit()
    {
        string loop = $"/c for /l %i in (1,1,300) do (timeout /t 1 /nobreak > nul & rmdir /s /q \"{InstallDir}\" 2> nul & if not exist \"{InstallDir}\" exit)";
        Process.Start(new ProcessStartInfo("cmd.exe", loop)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        })?.Dispose();
    }

    /// <summary>
    /// Hublot s'appelait « WinTooth », et avant « Bluetooth Popup » : retire ces installations
    /// (les données sont migrées par le service au premier lancement).
    /// </summary>
    private static void RemovePreviousName()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        TryDelete(Path.Combine(startup, "Bluetooth Popup.lnk"));
        TryDelete(Path.Combine(programs, "Paramètres de Bluetooth Popup.lnk"));
        TryDelete(Path.Combine(startup, "WinTooth.lnk"));
        TryDelete(Path.Combine(programs, "WinTooth.lnk"));
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinTooth", throwOnMissingSubKey: false);
        foreach (var name in new[] { "BluetoothPopup", "WinTooth" })
        {
            string oldDir = Path.Combine(local, "Programs", name);
            try { if (Directory.Exists(oldDir)) Directory.Delete(oldDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void StopProcesses(params string[] names)
    {
        int self = Environment.ProcessId;
        foreach (var name in names)
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.Id == self) continue;
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch (Exception) { }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
