using System.Runtime.InteropServices;

namespace Hublot.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NOTIFYICONIDENTIFIER
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public Guid guidItem;
}

internal static unsafe partial class Shell32
{
    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABN_FULLSCREENAPP = 2;

    /// <summary>Barre d'application : seule la notification de plein écran nous intéresse (aucune place réservée).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage, uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("shell32.dll")]
    public static partial nuint SHAppBarMessage(uint message, APPBARDATA* data);
    [LibraryImport("shell32.dll")]
    public static partial int Shell_NotifyIconGetRect(NOTIFYICONIDENTIFIER* identifier, RECT* rect);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecuteW(nint hwnd, string? operation, string file, string? parameters, string? directory, int show);
}

internal static unsafe partial class User32
{
    public const uint WM_HOTKEY = 0x0312, WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMSUSPEND = 0x4, PBT_APMRESUMESUSPEND = 0x7, PBT_APMRESUMEAUTOMATIC = 0x12;
    public const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll")]
    public static partial int RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    public static partial int UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(POINT point);

    public const uint GA_ROOT = 2;

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll")]
    public static partial int GetWindowRect(nint hwnd, RECT* rect);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClassNameW(nint hwnd, char* buffer, int max);
}

internal static unsafe partial class User32
{
    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, uint* processId);

    public const int SW_MINIMIZE = 6, SW_RESTORE = 9;
    public const uint MONITORINFOF_PRIMARY = 1;

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseCapture();
    public const uint GW_OWNER = 4;
    private const uint DWMWA_CLOAKED = 14;

    /// <summary>Fenêtres de premier niveau, dans l'ordre Z (la plus récente d'abord) : <c>FindWindowExW(0, précédente, null, null)</c>.</summary>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [LibraryImport("user32.dll")]
    public static partial int IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    /// <summary>Fenêtre masquée par le compositeur (autre bureau virtuel, application du Store en veille).</summary>
    public static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }
}
