using System.Runtime.InteropServices;

namespace Hublot.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
    public POINT(int x, int y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int Cx, Cy;
    public SIZE(int cx, int cy) { Cx = cx; Cy = cy; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public POINT Pt;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION
{
    public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT
{
    public uint cbSize;
    public uint dwFlags;
    public nint hwndTrack;
    public uint dwHoverTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth, biHeight;
    public ushort biPlanes, biBitCount;
    public uint biCompression, biSizeImage;
    public int biXPelsPerMeter, biYPelsPerMeter;
    public uint biClrUsed, biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

internal static class Msg
{
    public const uint WM_NULL = 0x0000;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;

    public const uint NIN_SELECT = WM_USER + 0;
    public const uint NIN_KEYSELECT = WM_USER + 1;
}

internal static unsafe partial class User32
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    public const uint ULW_ALPHA = 0x2;
    public const uint TME_LEAVE = 0x2;
    public const nint MA_NOACTIVATE = 3;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    public const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;

    public const int SM_CXSMICON = 49;
    public const nint IDC_ARROW = 32512;

    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    public static partial int TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial int PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    public static partial int DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint elapse, nint timerProc);

    [LibraryImport("user32.dll")]
    public static partial int KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    public static partial int UpdateLayeredWindow(nint hwnd, nint hdcDst, POINT* pptDst, SIZE* psize,
        nint hdcSrc, POINT* pptSrc, uint crKey, BLENDFUNCTION* pblend, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int GetMonitorInfoW(nint monitor, MONITORINFO* info);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [LibraryImport("user32.dll")]
    public static partial int DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    public static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial int TrackMouseEvent(TRACKMOUSEEVENT* tme);

    [LibraryImport("user32.dll")]
    public static partial int DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);
}

internal static unsafe partial class Gdi32
{
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* bmi, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    public static partial int DeleteObject(nint obj);
}

internal static unsafe partial class Shell32
{
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;

    // QUERY_USER_NOTIFICATION_STATE
    public const int QUNS_NOT_PRESENT = 1, QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    [LibraryImport("shell32.dll")]
    public static partial int Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(int* state);
}

internal static unsafe partial class Shcore
{
    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int dpiType, uint* dpiX, uint* dpiY);
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? name);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadLibraryW(string name);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetProcAddress(nint module, nint ordinal);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    public static partial int SetProcessWorkingSetSizeEx(nint process, nint min, nint max, uint flags);
}
