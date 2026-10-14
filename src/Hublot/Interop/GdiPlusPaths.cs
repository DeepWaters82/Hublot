using System.Runtime.InteropServices;

namespace Hublot.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PointF
{
    public float X, Y;
    public PointF(float x, float y) { X = x; Y = y; }
}

internal static unsafe partial class GdiPlus
{
    public const int WrapModeTileFlipXY = 3;
    public const int LineJoinRound = 2, LineCapRound = 2;

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathPolygon(nint path, PointF* points, int count);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathBeziers(nint path, PointF* points, int count);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateLineBrush(PointF* point1, PointF* point2, uint color1, uint color2, int wrapMode, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenLineJoin(nint pen, int join);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenStartCap(nint pen, int cap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenEndCap(nint pen, int cap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawLines(nint graphics, nint pen, PointF* points, int count);
}

internal static unsafe partial class GdiPlus
{
    public const int CombineModeReplace = 0;
    public const int InterpolationModeHighQualityBicubic = 7;

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawImageRect(nint graphics, nint image, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetClipPath(nint graphics, nint path, int combineMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipResetClip(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetInterpolationMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipAddPathString(nint path, string text, int length, nint family, int style, float emSize, RectF* layout, nint format);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetPathWorldBounds(nint path, RectF* bounds, nint matrix, nint pen);

    // Image translucide (fondu des pochettes) : matrice de couleur dont seul le canal alpha est réduit.
    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateImageAttributes(out nint attributes);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetImageAttributesColorMatrix(nint attributes, int adjustType, [MarshalAs(UnmanagedType.Bool)] bool enable,
        float* colorMatrix, float* grayMatrix, int flags);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDisposeImageAttributes(nint attributes);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawImageRectRect(nint graphics, nint image, float dstX, float dstY, float dstWidth, float dstHeight,
        float srcX, float srcY, float srcWidth, float srcHeight, int srcUnit, nint attributes, nint callback, nint callbackData);
}
