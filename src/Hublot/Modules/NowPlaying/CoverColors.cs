namespace Hublot.Modules.NowPlaying;

/// <summary>
/// Couleurs tirées de la pochette, à la manière de Material You : on retient la teinte dominante des pixels
/// colorés (les plus vifs comptent davantage), puis on en dérive une teinte de fond discrète et une couleur
/// d'accentuation lisible, réglées pour le thème clair ou sombre. Pochette en noir et blanc ou très terne :
/// pas de couleur, la carte garde le thème normal.
/// </summary>
internal static class CoverColors
{
    private const int Buckets = 36; // tranches de 10° de teinte

    /// <summary>Couleur source (ARGB opaque), ou 0 si la pochette n'a pas de couleur marquée.</summary>
    public static uint Seed(byte[] bgra)
    {
        Span<double> weight = stackalloc double[Buckets], r = stackalloc double[Buckets],
            g = stackalloc double[Buckets], b = stackalloc double[Buckets];
        int pixels = 0, colored = 0;
        // Un pixel sur deux suffit (pochette de 192 × 192).
        for (int i = 0; i + 3 < bgra.Length; i += 8)
        {
            int bl = bgra[i], gr = bgra[i + 1], rd = bgra[i + 2];
            int max = Math.Max(rd, Math.Max(gr, bl)), min = Math.Min(rd, Math.Min(gr, bl));
            pixels++;
            if (max < 40) continue; // presque noir
            double s = (max - min) / (double)max, v = max / 255.0;
            if (s < 0.2) continue; // gris
            int k = (int)(Hue(rd, gr, bl, max, min) / 360 * Buckets) % Buckets;
            double w = s * s * v;
            weight[k] += w; r[k] += rd * w; g[k] += gr * w; b[k] += bl * w;
            colored++;
        }
        if (pixels == 0 || colored < pixels / 20) return 0;

        // Tranche dominante, lissée par ses deux voisines (une teinte à cheval sur deux tranches).
        int best = 0;
        double bestScore = -1;
        for (int k = 0; k < Buckets; k++)
        {
            double score = weight[k] + 0.5 * (weight[(k + Buckets - 1) % Buckets] + weight[(k + 1) % Buckets]);
            if (score > bestScore) (best, bestScore) = (k, score);
        }
        double sw = 0, sr = 0, sg = 0, sb = 0;
        for (int d = -1; d <= 1; d++)
        {
            int k = (best + d + Buckets) % Buckets;
            sw += weight[k]; sr += r[k]; sg += g[k]; sb += b[k];
        }
        if (sw <= 0) return 0;
        return 0xFF000000 | (uint)Math.Round(sr / sw) << 16 | (uint)Math.Round(sg / sw) << 8 | (uint)Math.Round(sb / sw);
    }

    /// <summary>
    /// Teinte de fond (ARGB translucide, posée sur le matériau) et accentuation pour le thème donné. Sombre :
    /// fond très foncé, accentuation pastel (texte noir dessus) ; clair : fond pâle, accentuation soutenue.
    /// </summary>
    public static (uint Tint, uint Accent) For(uint seed, bool dark)
    {
        var (h, s) = HueSaturation(seed);
        return dark
            ? (FromHsl(h, Math.Min(s, 0.6), 0.24, 0.6), FromHsl(h, Math.Clamp(s, 0.35, 0.75), 0.74))
            : (FromHsl(h, Math.Min(s, 0.7), 0.86, 0.6), FromHsl(h, Math.Clamp(s, 0.4, 0.8), 0.38));
    }

    /// <summary>Mélange de deux couleurs ARGB ; une couleur transparente prend la teinte de l'autre (pas de passage par le noir).</summary>
    public static uint Lerp(uint from, uint to, float t)
    {
        if (from >> 24 == 0) from = to & 0x00FFFFFF;
        if (to >> 24 == 0) to = from & 0x00FFFFFF;
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            float a = (from >> shift) & 0xFF, b = (to >> shift) & 0xFF;
            result |= (uint)Math.Clamp(MathF.Round(a + (b - a) * t), 0, 255) << shift;
        }
        return result;
    }

    private static double Hue(int r, int g, int b, int max, int min)
    {
        double d = max - min;
        if (d == 0) return 0;
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    private static (double H, double S) HueSaturation(uint argb)
    {
        int r = (int)(argb >> 16 & 0xFF), g = (int)(argb >> 8 & 0xFF), b = (int)(argb & 0xFF);
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 510.0, d = (max - min) / 255.0;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (Hue(r, g, b, max, min), Math.Clamp(s, 0, 1));
    }

    private static uint FromHsl(double h, double s, double l, double alpha = 1)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static uint Byte(double v) => (uint)Math.Clamp(Math.Round(v * 255), 0, 255);
        return Byte(alpha) << 24 | Byte(r + m) << 16 | Byte(g + m) << 8 | Byte(b + m);
    }
}
