using System.Runtime.InteropServices;

namespace Hublot.Interop;

internal static unsafe partial class Dwm
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_MAINWINDOW = 2;      // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylique

    /// <summary>Matériaux système (DWMWA_SYSTEMBACKDROP_TYPE) : Windows 11 22H2 (build 22621) et suivants.</summary>
    public static bool SupportsSystemBackdrop => Environment.OSVersion.Version.Build >= 22621;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, void* value, int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, int* margins);

    /// <summary>Bloque jusqu'à la prochaine composition de l'écran (synchronisation verticale).</summary>
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmFlush();

    public static void SetInt(nint hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, &value, sizeof(int));
}

internal static unsafe partial class User32
{
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public const nint HWND_TOPMOST = -1;
    public const uint LWA_ALPHA = 0x2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint CS_DROPSHADOW = 0x00020000;

    [LibraryImport("user32.dll")]
    public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("user32.dll")]
    public static partial nint BeginPaint(nint hwnd, byte* paint);

    [LibraryImport("user32.dll")]
    public static partial int EndPaint(nint hwnd, byte* paint);

    [LibraryImport("user32.dll")]
    public static partial int GetCursorPos(POINT* point);
}

internal static partial class Gdi32
{
    public const uint SRCCOPY = 0x00CC0020;

    [LibraryImport("gdi32.dll")]
    public static partial int BitBlt(nint dst, int x, int y, int width, int height, nint src, int srcX, int srcY, uint rop);
}

internal static partial class GdiPlus
{
    public const int MatrixOrderPrepend = 0;

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipTranslateWorldTransform(nint graphics, float dx, float dy, int order);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipScaleWorldTransform(nint graphics, float sx, float sy, int order);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipResetWorldTransform(nint graphics);
}
