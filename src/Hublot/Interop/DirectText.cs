using System.Runtime.InteropServices;

namespace Hublot.Interop;

/// <summary>
/// Texte par DirectWrite (3.9.2) : Direct2D dessine dans le DIB du canevas (cible « DC » logicielle, alpha
/// prémultiplié), en niveaux de gris symétriques, comme les applications Fluent. Le rendu GDI+ ajusté à la grille
/// de pixels paraissait pixellisé, surtout sur l'acrylique (où ClearType est impossible : fond translucide).
/// Les glyphes d'icônes (Segoe Fluent Icons) restent en GDI+, leur centrage étant mesuré sur ses contours.
/// Un jeu d'objets par thread ; interfaces COM appelées par leur table virtuelle (d2d1.h, dwrite.h). Le moindre
/// échec renvoie false et le canevas retombe sur GDI+.
/// </summary>
internal static unsafe partial class DirectText
{
    [ThreadStatic] private static State? t_state;
    [ThreadStatic] private static bool t_failed;

    private static readonly Dictionary<string, (string Family, int Weight)> s_families = [];
    private static readonly Lock s_lock = new();

    /// <summary>Dessine <paramref name="text"/> dans <paramref name="r"/> (coordonnées du canevas, avant la transformation <paramref name="m"/>).</summary>
    public static bool Draw(nint hdc, int width, int height, ReadOnlySpan<float> m, string text, string[] families, float size,
        uint argb, RectF r, int align, bool ellipsis)
    {
        var s = Get();
        if (s is null || text.Length == 0) return s is not null;
        var (format, sign) = s.Format(families, size);
        if (format == 0) return false;

        // Alignement : proche → LEADING (0), centré → CENTER (2), lointain → TRAILING (1).
        Call(format, 3, align switch { 1 => 2, 2 => 1, _ => 0 });
        var trimming = new DWRITE_TRIMMING { Granularity = ellipsis ? 1 : 0 };
        ((delegate* unmanaged[Stdcall]<nint, DWRITE_TRIMMING*, nint, int>)Slot(format, 9))(format, &trimming, ellipsis ? sign : 0);

        // Zone de pixels touchée : le rectangle transformé, plus une marge pour le lissage.
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        Span<(float X, float Y)> corners = [(r.X, r.Y), (r.Right, r.Y), (r.X, r.Bottom), (r.Right, r.Bottom)];
        foreach (var (x, y) in corners)
        {
            float dx = m[0] * x + m[2] * y + m[4], dy = m[1] * x + m[3] * y + m[5];
            minX = Math.Min(minX, dx); maxX = Math.Max(maxX, dx);
            minY = Math.Min(minY, dy); maxY = Math.Max(maxY, dy);
        }
        var rc = new RECT
        {
            Left = Math.Max(0, (int)MathF.Floor(minX) - 2),
            Top = Math.Max(0, (int)MathF.Floor(minY) - 2),
            Right = Math.Min(width, (int)MathF.Ceiling(maxX) + 2),
            Bottom = Math.Min(height, (int)MathF.Ceiling(maxY) + 2),
        };
        if (rc.Right <= rc.Left || rc.Bottom <= rc.Top) return true;

        nint target = s.Target;
        if (((delegate* unmanaged[Stdcall]<nint, nint, RECT*, int>)Slot(target, 57))(target, hdc, &rc) < 0) return false; // BindDC
        ((delegate* unmanaged[Stdcall]<nint, void>)Slot(target, 48))(target); // BeginDraw
        var matrix = new D2D1_MATRIX_3X2_F { M11 = m[0], M12 = m[1], M21 = m[2], M22 = m[3], Dx = m[4] - rc.Left, Dy = m[5] - rc.Top };
        ((delegate* unmanaged[Stdcall]<nint, D2D1_MATRIX_3X2_F*, void>)Slot(target, 30))(target, &matrix); // SetTransform
        var color = new D2D1_COLOR_F
        {
            R = ((argb >> 16) & 0xFF) / 255f,
            G = ((argb >> 8) & 0xFF) / 255f,
            B = (argb & 0xFF) / 255f,
            A = (argb >> 24) / 255f,
        };
        ((delegate* unmanaged[Stdcall]<nint, D2D1_COLOR_F*, void>)Slot(s.Brush, 8))(s.Brush, &color); // SetColor
        var layout = new D2D1_RECT_F { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };
        fixed (char* p = text)
        {
            // DrawText(texte, longueur, format, rectangle, pinceau, D2D1_DRAW_TEXT_OPTIONS_CLIP, mesure naturelle)
            ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, D2D1_RECT_F*, nint, int, int, void>)Slot(target, 27))(
                target, p, (uint)text.Length, format, &layout, s.Brush, 2, 0);
        }
        int hr = ((delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>)Slot(target, 49))(target, null, null); // EndDraw
        Gdi32.GdiFlush();
        if (hr < 0)
        {
            // D2DERR_RECREATE_TARGET ou autre : on repart de zéro au prochain appel.
            s.Dispose();
            t_state = null;
            return false;
        }
        return true;
    }

    /// <summary>Largeur du texte sur une ligne (espaces finaux exclus, comme la mesure typographique de GDI+), -1 si impossible.</summary>
    public static float Measure(string text, string[] families, float size)
    {
        var s = Get();
        if (s is null) return -1;
        if (text.Length == 0) return 0;
        var (format, _) = s.Format(families, size);
        if (format == 0) return -1;
        Call(format, 3, 0);
        var none = new DWRITE_TRIMMING();
        ((delegate* unmanaged[Stdcall]<nint, DWRITE_TRIMMING*, nint, int>)Slot(format, 9))(format, &none, 0);
        nint layout;
        fixed (char* p = text)
        {
            // IDWriteFactory::CreateTextLayout
            if (((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float, float, nint*, int>)Slot(s.DWrite, 18))(
                    s.DWrite, p, (uint)text.Length, format, 100_000, 10_000, &layout) < 0) return -1;
        }
        DWRITE_TEXT_METRICS metrics;
        int hr = ((delegate* unmanaged[Stdcall]<nint, DWRITE_TEXT_METRICS*, int>)Slot(layout, 60))(layout, &metrics); // GetMetrics
        Release(layout);
        return hr < 0 ? -1 : metrics.Width;
    }

    private static State? Get()
    {
        if (t_state is not null || t_failed) return t_state;
        try
        {
            t_state = new State();
        }
        catch (Exception e)
        {
            t_failed = true;
            Hublot.Common.Log.Write("DirectWrite indisponible, texte en GDI+ : " + e.Message);
        }
        return t_state;
    }

    private sealed class State : IDisposable
    {
        public readonly nint D2D, DWrite, Target, Brush, Params, Collection;
        private readonly Dictionary<(string, int, float), (nint Format, nint Sign)> _formats = [];

        public State()
        {
            var iidD2D = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
            nint d2d;
            Check(D2D1CreateFactory(0, &iidD2D, null, &d2d), "D2D1CreateFactory");
            D2D = d2d;
            var iidDWrite = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
            nint dwrite;
            Check(DWriteCreateFactory(0, &iidDWrite, &dwrite), "DWriteCreateFactory");
            DWrite = dwrite;

            nint collection;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int, int>)Slot(DWrite, 3))(DWrite, &collection, 0), "GetSystemFontCollection");
            Collection = collection;

            // Cible DC logicielle : pas d'aller-retour GPU pour quelques mots ; 96 ppp, le canevas est déjà à l'échelle.
            var props = new D2D1_RENDER_TARGET_PROPERTIES
            {
                Type = 1,          // D2D1_RENDER_TARGET_TYPE_SOFTWARE
                Format = 87,       // DXGI_FORMAT_B8G8R8A8_UNORM
                AlphaMode = 1,     // D2D1_ALPHA_MODE_PREMULTIPLIED
                DpiX = 96,
                DpiY = 96,
            };
            nint target;
            Check(((delegate* unmanaged[Stdcall]<nint, D2D1_RENDER_TARGET_PROPERTIES*, nint*, int>)Slot(D2D, 16))(D2D, &props, &target), "CreateDCRenderTarget");
            Target = target;
            ((delegate* unmanaged[Stdcall]<nint, int, void>)Slot(Target, 34))(Target, 2); // SetTextAntialiasMode(GRAYSCALE)

            // Gamma 1,8, contraste renforcé, sans ClearType, rendu naturel symétrique (lissage vertical aussi).
            nint parameters;
            Check(((delegate* unmanaged[Stdcall]<nint, float, float, float, int, int, nint*, int>)Slot(DWrite, 12))(
                DWrite, 1.8f, 1.0f, 0f, 0, 5, &parameters), "CreateCustomRenderingParams");
            Params = parameters;
            ((delegate* unmanaged[Stdcall]<nint, nint, void>)Slot(Target, 36))(Target, Params); // SetTextRenderingParams

            var black = new D2D1_COLOR_F { A = 1 };
            nint brush;
            Check(((delegate* unmanaged[Stdcall]<nint, D2D1_COLOR_F*, void*, nint*, int>)Slot(Target, 8))(Target, &black, null, &brush), "CreateSolidColorBrush");
            Brush = brush;
        }

        public (nint Format, nint Sign) Format(string[] families, float size)
        {
            var (family, weight) = Resolve(families);
            var key = (family, weight, MathF.Round(size, 2));
            if (_formats.TryGetValue(key, out var cached)) return cached;
            if (_formats.Count >= 64) ClearFormats();

            nint format, sign;
            fixed (char* f = family)
            fixed (char* locale = Locale)
            {
                // CreateTextFormat(famille, collection système, graisse, style normal, chasse normale, taille, locale)
                if (((delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int, float, char*, nint*, int>)Slot(DWrite, 15))(
                        DWrite, f, 0, weight, 0, 5, size, locale, &format) < 0) return default;
            }
            Call(format, 4, 2); // SetParagraphAlignment(CENTER) : centré verticalement, comme GDI+
            Call(format, 5, 1); // SetWordWrapping(NO_WRAP)
            // CreateEllipsisTrimmingSign
            if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(DWrite, 20))(DWrite, format, &sign) < 0) sign = 0;
            return _formats[key] = (format, sign);
        }

        /// <summary>« Segoe UI Variable Text Semibold » (nom GDI) → famille « Segoe UI Variable Text », graisse 600.</summary>
        private (string Family, int Weight) Resolve(string[] candidates)
        {
            lock (s_lock)
            {
                if (s_families.TryGetValue(candidates[0], out var known)) return known;
                (string, int) found = (candidates[^1], 400);
                foreach (var name in candidates)
                {
                    var family = name;
                    int weight = 400;
                    if (family.EndsWith(" Semibold", StringComparison.Ordinal)) { family = family[..^9]; weight = 600; }
                    uint index;
                    int exists = 0;
                    fixed (char* f = family)
                    {
                        // IDWriteFontCollection::FindFamilyName
                        ((delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)Slot(Collection, 5))(Collection, f, &index, &exists);
                    }
                    if (exists != 0) { found = (family, weight); break; }
                }
                return s_families[candidates[0]] = found;
            }
        }

        private void ClearFormats()
        {
            foreach (var (format, sign) in _formats.Values)
            {
                Release(sign);
                Release(format);
            }
            _formats.Clear();
        }

        public void Dispose()
        {
            ClearFormats();
            Release(Brush);
            Release(Params);
            Release(Target);
            Release(Collection);
            Release(DWrite);
            Release(D2D);
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException($"{what} : 0x{hr:X8}");
        }
    }

    private static readonly string Locale = System.Globalization.CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-us";

    private static void* Slot(nint obj, int index) => (*(void***)obj)[index];

    private static int Call(nint obj, int slot, int value) => ((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(obj, slot))(obj, value);

    private static void Release(nint obj)
    {
        if (obj != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);
    }

    [LibraryImport("d2d1.dll")]
    private static partial int D2D1CreateFactory(int type, Guid* iid, void* options, nint* factory);

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int type, Guid* iid, nint* factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_RENDER_TARGET_PROPERTIES
    {
        public int Type, Format, AlphaMode;
        public float DpiX, DpiY;
        public int Usage, MinLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_COLOR_F { public float R, G, B, A; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_RECT_F { public float Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_MATRIX_3X2_F { public float M11, M12, M21, M22, Dx, Dy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWRITE_TRIMMING { public int Granularity; public uint Delimiter, DelimiterCount; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWRITE_TEXT_METRICS
    {
        public float Left, Top, Width, WidthIncludingTrailingWhitespace, Height, LayoutWidth, LayoutHeight;
        public uint MaxBidiReorderingDepth, LineCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
}

internal static partial class Gdi32
{
    [LibraryImport("gdi32.dll")]
    public static partial int GdiFlush();
}

internal static unsafe partial class GdiPlus
{
    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateMatrix(out nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteMatrix(nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetWorldTransform(nint graphics, nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetMatrixElements(nint matrix, float* elements);
}
