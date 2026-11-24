using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Caffeine;

/// <summary>
/// Tasse de café minimaliste (grille de 16), pour la zone de notification et les cartes : contour fin à la couleur
/// du texte de la barre des tâches, café à la couleur d’accentuation de Windows (orange pour une extinction
/// programmée), trois niveaux (pleine, moitié, vide) et, à l’activation, deux volutes de fumée qui montent.
/// </summary>
internal static class CaffeineIcon
{
    /// <summary>Orange d’extinction, plus soutenu sur une barre des tâches claire.</summary>
    public static uint ShutdownColor(bool light) => light ? 0xFFD83B01 : 0xFFFF9A3C;

    /// <summary>Contour et café pour la zone de notification (barre des tâches claire ou sombre).</summary>
    public static (uint Ink, uint Accent) TrayColors()
    {
        bool light = Theme.SystemUsesLightTheme();
        return (light ? 0xFF1C1C1C : 0xFFFFFFFF, TrayIconRenderer.TrayColors(light).Top);
    }

    /// <summary>Icône de la zone de notification (handle à détruire par l’appelant).</summary>
    public static nint Render(int size, float level, uint fill, uint ink, float steam)
    {
        using var c = new Canvas(size, size);
        c.Clear();
        Draw(c, new RectF(0, 0, size, size), level, fill, ink, steam);
        c.Flush();
        return c.ToIcon();
    }

    /// <summary>
    /// Tasse dans <paramref name="box"/> (carré). <paramref name="level"/> : 0 à 1, négatif = Caféine désactivé
    /// (contour estompé, tasse vide). <paramref name="steam"/> : avancée de la fumée (0 à 1), négatif = pas de fumée.
    /// </summary>
    public static void Draw(Canvas c, RectF box, float level, uint fill, uint ink, float steam)
    {
        float u = box.Width / 16f;
        PointF P(float x, float y) => new(box.X + x * u, box.Y + y * u);
        float stroke = Math.Max(1f, 1.3f * u);
        uint line = level < 0 ? Shell.PopupRenderer.WithAlpha(ink, 0.6f) : ink;

        // Café : trapèze intérieur, du fond jusqu’au niveau.
        if (level > 0.01f)
        {
            const float top = 7.1f, bottom = 12.35f;
            float surface = bottom - Math.Clamp(level, 0, 1) * (bottom - top);
            float Left(float y) => 3.2f + (y - 6.2f) / 7f + 0.85f;
            float Right(float y) => 11.3f - (y - 6.2f) / 7f - 0.85f;
            Span<PointF> coffee = [P(Left(surface), surface), P(Right(surface), surface), P(Right(bottom), bottom), P(Left(bottom), bottom)];
            c.FillPolygon(coffee, fill, 0);
        }

        // Tasse, anse, soucoupe.
        Span<PointF> cup = [P(3.2f, 6.2f), P(4.2f, 13.2f), P(10.3f, 13.2f), P(11.3f, 6.2f)];
        c.StrokePolyline(cup, stroke, line);
        Span<PointF> handle = stackalloc PointF[7];
        for (int i = 0; i < handle.Length; i++)
        {
            double a = (-75 + 150.0 * i / (handle.Length - 1)) * Math.PI / 180;
            handle[i] = P(11.0f + 2.0f * (float)Math.Cos(a), 9.0f + 2.0f * (float)Math.Sin(a));
        }
        c.StrokePolyline(handle, stroke, line);
        Span<PointF> saucer = [P(2.3f, 14.7f), P(12.2f, 14.7f)];
        c.StrokePolyline(saucer, stroke, line);

        // Fumée : deux volutes qui montent et s’estompent.
        if (steam >= 0)
        {
            float rise = Math.Clamp(steam, 0, 1);
            uint smoke = Shell.PopupRenderer.WithAlpha(ink, (float)Math.Sin(Math.PI * rise) * 0.9f);
            Span<PointF> wisp = stackalloc PointF[6];
            foreach (float x in new[] { 5.9f, 8.7f })
            {
                for (int i = 0; i < wisp.Length; i++)
                {
                    float t = i / (float)(wisp.Length - 1);
                    float y = 5.0f - rise * 2.2f - t * 3.2f;
                    wisp[i] = P(x + 0.65f * (float)Math.Sin((t + rise) * Math.PI * 2), y);
                }
                c.StrokePolyline(wisp, Math.Max(1f, 1.0f * u), smoke);
            }
        }
    }
}
