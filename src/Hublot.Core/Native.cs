using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Hublot.Common;
using Windows.ApplicationModel;

namespace HublotCore;

/// <summary>Liaison avec le service Hublot.exe (messages fenêtre) et démarrage automatique.</summary>
internal static class Native
{
    private const string ServiceExe = "Hublot.exe";
    private const string ShortcutName = "Hublot.lnk";

    public static string ServicePath => Path.Combine(AppContext.BaseDirectory, ServiceExe);

    public static bool ServiceRunning => FindWindowW(Settings.HostWindowClass, null) != 0;

    /// <summary>Prévient le service que settings.ini a changé.</summary>
    public static void NotifySettingsChanged() => Post(Settings.ChangedMessage);

    /// <summary>Demande au service un aperçu (0 = carte d'appareil, 1 = liste, 2 = lecture en cours, 3 = USB, 4 = alimentation).</summary>
    public static void RequestPreview(int kind) => Post(Settings.PreviewMessage, kind);

    /// <summary>Signale au service une nouvelle version (il affiche un popup).</summary>
    public static void NotifyUpdate(Version version) => Post(Settings.UpdateMessage, (version.Major << 16) | (version.Minor << 8) | Math.Max(0, version.Build));

    /// <summary>Zone de travail (sans la barre des tâches) de l'écran sous la souris, en DIP de cet écran.</summary>
    public static System.Windows.Rect WorkAreaAtCursor()
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        nint monitor = GetCursorPos(out var cursor) ? MonitorFromPoint(cursor, MonitorDefaultToNearest) : 0;
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info)) return System.Windows.SystemParameters.WorkArea;
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        var r = info.rcWork;
        return new System.Windows.Rect(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale);
    }

    /// <summary>Le PC a-t-il une batterie (portable) ?</summary>
    public static bool HasBattery => GetSystemPowerStatus(out var s) && s.BatteryFlag != 255 && (s.BatteryFlag & 128) == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    private static void Post(string message, nint wParam = 0)
    {
        var host = FindWindowW(Settings.HostWindowClass, null);
        if (host != 0) PostMessageW(host, RegisterWindowMessageW(message), wParam, 0);
    }

    public static bool StartService()
    {
        if (!File.Exists(ServicePath)) return false;
        Process.Start(new ProcessStartInfo(ServicePath) { UseShellExecute = true })?.Dispose();
        return true;
    }

    /// <summary>
    /// Remet au premier plan la fenêtre de l'autre instance (même exécutable). Pas de recherche par titre :
    /// « Hublot » est aussi le titre d'une fenêtre de l'Explorateur ouverte sur un dossier « Hublot ».
    /// </summary>
    public static void ActivateExisting()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(self.ProcessName))
        {
            using (other)
            {
                if (other.Id == self.Id || other.MainWindowHandle == 0) continue;
                ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    // ── Démarrage automatique : raccourci dans le dossier Démarrage de l'utilisateur ──────
    // (le même que celui créé par install.ps1 ; pas de clé Run, voir CLAUDE.md)

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    /// <summary>
    /// Démarrage avec Windows. Version Store : tâche de démarrage du paquet (AppxManifest.xml), que
    /// l'utilisateur peut aussi couper dans le Gestionnaire des tâches ; version classique : raccourci.
    /// </summary>
    public static bool StartupEnabled => AppPackage.IsPackaged
        ? StoreStartup()?.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
        : File.Exists(ShortcutPath);

    public static void SetStartup(bool enabled)
    {
        if (AppPackage.IsPackaged)
        {
            if (StoreStartup() is not { } task) return;
            // Refusé si l'utilisateur l'a coupé dans le Gestionnaire des tâches : c'est à lui de le rétablir.
            if (enabled) task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            else task.Disable();
            return;
        }
        SetStartup(enabled, ServicePath);
    }

    private static StartupTask? StoreStartup()
    {
        try
        {
            return StartupTask.GetAsync(AppPackage.StartupTaskId).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void SetStartup(bool enabled, string servicePath)
    {
        if (!enabled)
        {
            File.Delete(ShortcutPath);
            return;
        }
        CreateShortcut(ShortcutPath, servicePath, L.T("Shortcut.Description"));
    }

    public static void CreateShortcut(string path, string target, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetDescription(description);
        ((IPersistFile)link).Save(path, true);
        Marshal.ReleaseComObject(link);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, nint findData, int flags);
        void GetIDList(out nint pidl);
        void SetIDList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(nint hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int cmd);

    private const int DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
    private const string DwmKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);

    /// <summary>
    /// Barre de titre à la couleur d'accentuation actuelle si Windows l'affiche sur les barres de titre (DWM
    /// ColorPrevalence), texte blanc ou noir selon le contraste ; sinon couleurs par défaut du thème. Le thème
    /// Fluent de WPF fixe cette couleur à l'ouverture et ne la change plus quand l'accentuation change.
    /// </summary>
    public static void ApplyCaptionColor(nint hwnd)
    {
        if (hwnd == 0) return;
        uint caption = DWMWA_COLOR_DEFAULT, text = DWMWA_COLOR_DEFAULT;
        if (Microsoft.Win32.Registry.GetValue(DwmKey, "ColorPrevalence", 0) is int on && on == 1
            && Microsoft.Win32.Registry.GetValue(DwmKey, "AccentColor", null) is int abgr)
        {
            caption = (uint)abgr & 0x00FFFFFF; // AccentColor = 0xAABBGGRR, COLORREF = 0x00BBGGRR
            double luminance = (0.2126 * (caption & 0xFF) + 0.7152 * (caption >> 8 & 0xFF) + 0.0722 * (caption >> 16 & 0xFF)) / 255;
            text = luminance > 0.6 ? 0x00000000u : 0x00FFFFFFu;
        }
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(uint));
    }
}
