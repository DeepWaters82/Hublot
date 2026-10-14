using Hublot.Interop;
using static Hublot.Interop.GdiPlus;

namespace Hublot.Shell;

internal enum TextAlign { Near = StringAlignmentNear, Center = StringAlignmentCenter, Far = StringAlignmentFar }

/// <summary>
/// Surface 32 bits prémultipliée (DIB) partagée entre GDI — pour UpdateLayeredWindow — et GDI+ pour le dessin.
/// </summary>
internal sealed unsafe class Canvas : IDisposable
{
    private readonly nint _dc, _dib, _oldBitmap, _image, _graphics;
    private readonly nint _format;

    public int Width { get; }
    public int Height { get; }
    public nint Hdc => _dc;
    public uint* Bits { get; }

    public Canvas(int width, int height)
    {
        Width = width;
        Height = height;

        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // de haut en bas
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        _dib = Gdi32.CreateDIBSection(0, &bmi, 0, &bits, 0, 0);
        if (_dib == 0) throw new InvalidOperationException("CreateDIBSection a échoué");
        Bits = (uint*)bits;
        _dc = Gdi32.CreateCompatibleDC(0);
        _oldBitmap = Gdi32.SelectObject(_dc, _dib);

        // GDI+ dessine directement dans la mémoire du DIB.
        Check(GdipCreateBitmapFromScan0(width, height, width * 4, PixelFormat32bppPARGB, bits, out _image));
        Check(GdipGetImageGraphicsContext(_image, out _graphics));
        GdipSetSmoothingMode(_graphics, SmoothingModeAntiAlias);
        GdipSetPixelOffsetMode(_graphics, PixelOffsetModeHighQuality);
        GdipSetTextRenderingHint(_graphics, TextRenderingHintAntiAliasGridFit);

        Check(GdipStringFormatGetGenericTypographic(out var typographic));
        Check(GdipCloneStringFormat(typographic, out _format));
        GdipSetStringFormatFlags(_format, StringFormatFlagsNoWrap | StringFormatFlagsLineLimit);
        GdipSetStringFormatLineAlign(_format, StringAlignmentCenter);
    }

    public void Clear() => GdipGraphicsClear(_graphics, 0);

    /// <summary>Remplit tout le canevas d'une couleur (prémultipliée par GDI+).</summary>
    public void Fill(uint argb) => GdipGraphicsClear(_graphics, argb);

    /// <summary>Mise à l'échelle des dessins suivants autour d'un point (jusqu'à <see cref="ResetTransform"/>).</summary>
    public void ScaleAround(float cx, float cy, float scale)
    {
        GdipTranslateWorldTransform(_graphics, cx, cy, MatrixOrderPrepend);
        GdipScaleWorldTransform(_graphics, scale, scale, MatrixOrderPrepend);
        GdipTranslateWorldTransform(_graphics, -cx, -cy, MatrixOrderPrepend);
    }

    public void ResetTransform() => GdipResetWorldTransform(_graphics);

    /// <summary>Copie des pixels : sert à mettre en cache le fond (ombre comprise) entre deux images.</summary>
    public uint[] CopyPixels()
    {
        Flush();
        var pixels = new uint[Width * Height];
        new ReadOnlySpan<uint>(Bits, pixels.Length).CopyTo(pixels);
        return pixels;
    }

    public void RestorePixels(uint[] pixels)
    {
        Flush();
        pixels.AsSpan().CopyTo(new Span<uint>(Bits, Width * Height));
    }

    public void Flush() => GdipFlush(_graphics, FlushIntentionSync);

    public void FillRoundRect(RectF r, float radius, uint argb)
    {
        var path = RoundRect(r, radius);
        GdipCreateSolidFill(argb, out var brush);
        GdipFillPath(_graphics, brush, path);
        GdipDeleteBrush(brush);
        GdipDeletePath(path);
    }

    /// <summary>Trait intérieur : le contour reste dans <paramref name="r"/>.</summary>
    public void StrokeRoundRect(RectF r, float radius, uint argb, float width)
    {
        var inner = r.Inflate(-width / 2);
        var path = RoundRect(inner, Math.Max(0, radius - width / 2));
        GdipCreatePen1(argb, width, UnitPixel, out var pen);
        GdipDrawPath(_graphics, pen, path);
        GdipDeletePen(pen);
        GdipDeletePath(path);
    }

    /// <summary>Superellipse (« squircle ») |x|^n + |y|^n = 1, remplie d'un dégradé vertical.</summary>
    public void FillSquircle(RectF r, float exponent, uint top, uint bottom)
    {
        const int steps = 96;
        var points = stackalloc PointF[steps];
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, ax = r.Width / 2, ay = r.Height / 2;
        for (int i = 0; i < steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            double cos = Math.Cos(t), sin = Math.Sin(t);
            points[i] = new PointF(
                cx + ax * (float)(Math.Sign(cos) * Math.Pow(Math.Abs(cos), 2 / exponent)),
                cy + ay * (float)(Math.Sign(sin) * Math.Pow(Math.Abs(sin), 2 / exponent)));
        }
        GdipCreatePath(0, out var path);
        GdipAddPathPolygon(path, points, steps);
        var p1 = new PointF(r.X, r.Y - 1);
        var p2 = new PointF(r.X, r.Bottom + 1);
        GdipCreateLineBrush(&p1, &p2, top, bottom, WrapModeTileFlipXY, out var brush);
        GdipFillPath(_graphics, brush, path);
        GdipDeleteBrush(brush);
        GdipDeletePath(path);
    }

    /// <summary>Ligne brisée à extrémités et jointures arrondies.</summary>
    public void StrokePolyline(ReadOnlySpan<PointF> points, float width, uint argb)
    {
        GdipCreatePen1(argb, width, UnitPixel, out var pen);
        GdipSetPenLineJoin(pen, LineJoinRound);
        GdipSetPenStartCap(pen, LineCapRound);
        GdipSetPenEndCap(pen, LineCapRound);
        fixed (PointF* p = points) GdipDrawLines(_graphics, pen, p, points.Length);
        GdipDeletePen(pen);
    }

    /// <summary>Polygone plein aux sommets arrondis (contour de même couleur, jointures rondes, épaisseur <paramref name="rounding"/>).</summary>
    public void FillPolygon(ReadOnlySpan<PointF> points, uint argb, float rounding)
    {
        GdipCreatePath(0, out var path);
        fixed (PointF* p = points) GdipAddPathPolygon(path, p, points.Length);
        GdipCreateSolidFill(argb, out var brush);
        GdipFillPath(_graphics, brush, path);
        GdipDeleteBrush(brush);
        if (rounding > 0)
        {
            GdipCreatePen1(argb, rounding, UnitPixel, out var pen);
            GdipSetPenLineJoin(pen, LineJoinRound);
            GdipDrawPath(_graphics, pen, path);
            GdipDeletePen(pen);
        }
        GdipDeletePath(path);
    }

    /// <summary>Forme fermée décrite par des courbes de Bézier cubiques (1 + 3k points).</summary>
    public void FillBeziers(ReadOnlySpan<PointF> points, uint argb)
    {
        GdipCreatePath(0, out var path);
        fixed (PointF* p = points) GdipAddPathBeziers(path, p, points.Length);
        GdipClosePathFigure(path);
        GdipCreateSolidFill(argb, out var brush);
        GdipFillPath(_graphics, brush, path);
        GdipDeleteBrush(brush);
        GdipDeletePath(path);
    }

    /// <summary>
    /// Image 32 bits prémultipliée (BGRA, <paramref name="width"/> × <paramref name="height"/>),
    /// redimensionnée dans <paramref name="dest"/> et découpée en rectangle arrondi (pochettes d'album).
    /// </summary>
    public void DrawImage(byte[] pixels, int width, int height, RectF dest, float radius, float opacity = 1)
    {
        if (opacity <= 0.003f) return;
        fixed (byte* p = pixels)
        {
            if (GdipCreateBitmapFromScan0(width, height, width * 4, PixelFormat32bppPARGB, p, out var image) != 0) return;
            var path = RoundRect(dest, radius);
            GdipSetClipPath(_graphics, path, CombineModeReplace);
            GdipSetInterpolationMode(_graphics, InterpolationModeHighQualityBicubic);
            if (opacity >= 0.997f)
            {
                GdipDrawImageRect(_graphics, image, dest.X, dest.Y, dest.Width, dest.Height);
            }
            else
            {
                var matrix = stackalloc float[25];
                for (int i = 0; i < 5; i++) matrix[i * 6] = 1; // identité
                matrix[18] = opacity;                          // alpha × opacité
                GdipCreateImageAttributes(out var attributes);
                GdipSetImageAttributesColorMatrix(attributes, 0, true, matrix, null, 0);
                GdipDrawImageRectRect(_graphics, image, dest.X, dest.Y, dest.Width, dest.Height, 0, 0, width, height, UnitPixel, attributes, 0, 0);
                GdipDisposeImageAttributes(attributes);
            }
            GdipResetClip(_graphics);
            GdipDeletePath(path);
            GdipDisposeImage(image);
        }
    }

    public void FillEllipse(RectF r, uint argb)
    {
        GdipCreateSolidFill(argb, out var brush);
        GdipFillEllipse(_graphics, brush, r.X, r.Y, r.Width, r.Height);
        GdipDeleteBrush(brush);
    }

    public void DrawText(string text, Font font, uint argb, RectF r, TextAlign align = TextAlign.Near, bool ellipsis = false)
    {
        GdipSetStringFormatAlign(_format, (int)align);
        GdipSetStringFormatTrimming(_format, ellipsis ? StringTrimmingEllipsisCharacter : 0);
        GdipCreateSolidFill(argb, out var brush);
        GdipDrawString(_graphics, text, text.Length, font.Handle, &r, _format, brush);
        GdipDeleteBrush(brush);
    }

    private static readonly Dictionary<(nint Family, string Glyph), (float X, float Y)> s_iconOffsets = [];

    /// <summary>
    /// Icône (glyphe Segoe) centrée sur son dessin réel et non sur son cadratin : beaucoup de glyphes de
    /// Segoe Fluent Icons sont décentrés dans leur boîte (casque, souris, clé USB…). Le décalage est mesuré
    /// une fois par glyphe sur son contour, puis appliqué proportionnellement à la taille.
    /// </summary>
    public void DrawIcon(string glyph, Font font, uint argb, RectF r)
    {
        var key = (font.FamilyHandle, glyph);
        if (!s_iconOffsets.TryGetValue(key, out var o)) s_iconOffsets[key] = o = InkOffset(glyph, font.FamilyHandle);
        DrawText(glyph, font, argb, new RectF(r.X + o.X * font.Pixels, r.Y + o.Y * font.Pixels, r.Width, r.Height), TextAlign.Center);
    }

    /// <summary>Écart (en fraction de la taille) entre le centre de la boîte de mise en page et celui du dessin du glyphe.</summary>
    private (float X, float Y) InkOffset(string glyph, nint family)
    {
        const float size = 100;
        var layout = new RectF(0, 0, 4 * size, 4 * size);
        GdipSetStringFormatAlign(_format, StringAlignmentCenter);
        GdipSetStringFormatTrimming(_format, 0);
        GdipCreatePath(0, out var path);
        GdipAddPathString(path, glyph, glyph.Length, family, 0, size, &layout, _format);
        RectF ink;
        int status = GdipGetPathWorldBounds(path, &ink, 0, 0);
        GdipDeletePath(path);
        if (status != 0 || ink.Width <= 0 || ink.Height <= 0) return (0, 0);
        return ((layout.X + layout.Width / 2 - (ink.X + ink.Width / 2)) / size,
                (layout.Y + layout.Height / 2 - (ink.Y + ink.Height / 2)) / size);
    }

    public float MeasureText(string text, Font font)
    {
        GdipSetStringFormatAlign(_format, StringAlignmentNear);
        GdipSetStringFormatTrimming(_format, 0);
        var layout = new RectF(0, 0, 100_000, 10_000);
        RectF box;
        int fitted, lines;
        GdipMeasureString(_graphics, text, text.Length, font.Handle, &layout, _format, &box, &fitted, &lines);
        return box.Width;
    }

    /// <summary>
    /// Ombre portée douce : silhouette de la carte, floutée (3 flous boîte ≈ gaussien),
    /// écrite en noir prémultiplié. À appeler sur un canevas vide, avant de dessiner la carte.
    /// </summary>
    public void DropShadow(RectF card, float radius, int blur, float offsetY, float opacity)
    {
        Clear();
        FillRoundRect(new RectF(card.X, card.Y + offsetY, card.Width, card.Height), radius, 0xFF000000);
        Flush();

        int n = Width * Height;
        var mask = new byte[n];
        var tmp = new byte[n];
        for (int i = 0; i < n; i++) mask[i] = (byte)(Bits[i] >> 24);
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(mask, tmp, Width, Height, blur, horizontal: true);
            BoxBlur(tmp, mask, Width, Height, blur, horizontal: false);
        }
        for (int i = 0; i < n; i++) Bits[i] = (uint)(mask[i] * opacity) << 24;
    }

    private static void BoxBlur(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        int lines = horizontal ? h : w, len = horizontal ? w : h;
        int step = horizontal ? 1 : w, lineStep = horizontal ? w : 1;
        int div = 2 * r + 1;
        for (int l = 0; l < lines; l++)
        {
            int start = l * lineStep, sum = 0;
            for (int i = -r; i <= r; i++) sum += src[start + Math.Clamp(i, 0, len - 1) * step];
            for (int i = 0; i < len; i++)
            {
                dst[start + i * step] = (byte)(sum / div);
                sum += src[start + Math.Min(i + r + 1, len - 1) * step] - src[start + Math.Max(i - r, 0) * step];
            }
        }
    }

    /// <summary>Compose l'image sur un fond opaque (aperçus PNG uniquement).</summary>
    public void FlattenOnto(uint rgb)
    {
        Flush();
        uint br = (rgb >> 16) & 0xFF, bg = (rgb >> 8) & 0xFF, bb = rgb & 0xFF;
        for (int i = 0, n = Width * Height; i < n; i++)
        {
            uint p = Bits[i], inv = 255 - (p >> 24);
            uint r = ((p >> 16) & 0xFF) + br * inv / 255;
            uint g = ((p >> 8) & 0xFF) + bg * inv / 255;
            uint b = (p & 0xFF) + bb * inv / 255;
            Bits[i] = 0xFF000000 | (r << 16) | (g << 8) | b;
        }
    }

    public void SavePng(string path)
    {
        Flush();
        var png = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e");
        Check(GdipSaveImageToFile(_image, path, &png, 0));
    }

    /// <summary>Convertit l'image en icône (zone de notification).</summary>
    public nint ToIcon()
    {
        Flush();
        Check(GdipCreateHICONFromBitmap(_image, out var icon));
        return icon;
    }

    private static nint RoundRect(RectF r, float radius)
    {
        GdipCreatePath(0, out var path);
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        GdipAddPathArc(path, r.X, r.Y, d, d, 180, 90);
        GdipAddPathArc(path, r.Right - d, r.Y, d, d, 270, 90);
        GdipAddPathArc(path, r.Right - d, r.Bottom - d, d, d, 0, 90);
        GdipAddPathArc(path, r.X, r.Bottom - d, d, d, 90, 90);
        GdipClosePathFigure(path);
        return path;
    }

    public void Dispose()
    {
        GdipDeleteStringFormat(_format);
        GdipDeleteGraphics(_graphics);
        GdipDisposeImage(_image);
        Gdi32.SelectObject(_dc, _oldBitmap);
        Gdi32.DeleteObject(_dib);
        Gdi32.DeleteDC(_dc);
    }
}
