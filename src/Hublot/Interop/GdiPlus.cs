using System.Runtime.InteropServices;

namespace Hublot.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RectF
{
    public float X, Y, Width, Height;
    public RectF(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    public readonly float Right => X + Width;
    public readonly float Bottom => Y + Height;
    public readonly RectF Inflate(float d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);
}

/// <summary>API « flat » de GDI+ (gdiplus.dll), utilisée sans System.Drawing.</summary>
internal static unsafe partial class GdiPlus
{
    public const int PixelFormat32bppPARGB = 0x000E200B;
    public const int UnitPixel = 2;
    public const int SmoothingModeAntiAlias = 4;
    public const int PixelOffsetModeHighQuality = 2;
    public const int TextRenderingHintAntiAliasGridFit = 3;
    public const int StringAlignmentNear = 0, StringAlignmentCenter = 1, StringAlignmentFar = 2;
    public const int StringTrimmingEllipsisCharacter = 3;
    public const int StringFormatFlagsNoWrap = 0x1000, StringFormatFlagsLineLimit = 0x2000;
    public const int FlushIntentionSync = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupOutput
    {
        public delegate* unmanaged[Stdcall]<nuint*, int> NotificationHook;
        public delegate* unmanaged[Stdcall]<nuint, void> NotificationUnhook;
    }

    private static nuint s_token, s_hookToken;
    private static delegate* unmanaged[Stdcall]<nuint, void> s_unhook;

    /// <summary>Démarre GDI+ sans son thread de fond : le hook est porté par le thread UI.</summary>
    public static void Startup()
    {
        var input = new StartupInput { GdiplusVersion = 1, SuppressBackgroundThread = 1 };
        StartupOutput output;
        nuint token;
        Check(GdiplusStartup(&token, &input, &output));
        s_token = token;
        nuint hook;
        if (output.NotificationHook(&hook) == 0)
        {
            s_hookToken = hook;
            s_unhook = output.NotificationUnhook;
        }
    }

    public static void Shutdown()
    {
        if (s_unhook != null) s_unhook(s_hookToken);
        if (s_token != 0) GdiplusShutdown(s_token);
        s_unhook = null;
        s_token = 0;
    }

    public static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"GDI+ : statut {status}");
    }

    [LibraryImport("gdiplus.dll")]
    private static partial int GdiplusStartup(nuint* token, StartupInput* input, StartupOutput* output);

    [LibraryImport("gdiplus.dll")]
    private static partial void GdiplusShutdown(nuint token);

    // Images
    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, void* scan0, out nint bitmap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetImageGraphicsContext(nint image, out nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDisposeImage(nint image);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateHICONFromBitmap(nint bitmap, out nint icon);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipSaveImageToFile(nint image, string fileName, Guid* encoder, nint parameters);

    // Graphics
    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetSmoothingMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPixelOffsetMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetTextRenderingHint(nint graphics, int hint);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGraphicsClear(nint graphics, uint argb);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFlush(nint graphics, int intention);

    // Pinceaux, stylos, tracés
    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateSolidFill(uint argb, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteBrush(nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePen1(uint argb, float width, int unit, out nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePen(nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(int fillMode, out nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArc(nint path, float x, float y, float width, float height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipClosePathFigure(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillPath(nint graphics, nint brush, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawPath(nint graphics, nint pen, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);

    // Texte
    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipCreateFontFamilyFromName(string name, nint collection, out nint family);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateFont(nint family, float emSize, int style, int unit, out nint font);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteFont(nint font);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipStringFormatGetGenericTypographic(out nint format);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCloneStringFormat(nint format, out nint clone);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteStringFormat(nint format);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetStringFormatFlags(nint format, int flags);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetStringFormatAlign(nint format, int align);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetStringFormatLineAlign(nint format, int align);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetStringFormatTrimming(nint format, int trimming);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipDrawString(nint graphics, string text, int length, nint font, RectF* layout, nint format, nint brush);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipMeasureString(nint graphics, string text, int length, nint font, RectF* layout, nint format,
        RectF* boundingBox, int* codepointsFitted, int* linesFilled);
}
