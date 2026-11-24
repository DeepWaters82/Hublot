using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Caffeine;

/// <summary>
/// Tasse de café minimaliste (grille de 16), pour la zone de notification et les cartes : contour fin à la couleur
/// du texte de la barre des tâches, café à la couleur d’accentuation de Windows (orange pour une extinction
/// programmée), trois niveaux (pleine, moitié, vide) et, à l’activation, la tasse qui se remplit puis deux volutes de
/// fumée qui montent (animation pilotée par <see cref="CaffeineTray"/>).
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
        // Tasse qui occupe tout le carré (3.9.0 : l’ancienne laissait le haut vide pour la fumée et paraissait
        // minuscule à côté des autres icônes) ; la fumée tient dans la bande du haut, au-dessus du bord.
        float u = box.Width / 16f;
        PointF P(float x, float y) => new(box.X + x * u, box.Y + y * u);
        float stroke = Math.Max(1f, 1.5f * u);
        uint line = level < 0 ? Shell.PopupRenderer.WithAlpha(ink, 0.6f) : ink;
        const float rimY = 4.6f, baseY = 13.0f, rimLeft = 1.3f, rimRight = 11.7f, taper = 1.2f;

        // Café : trapèze intérieur, du fond jusqu’au niveau.
        if (level > 0.01f)
        {
            const float top = 5.5f, bottom = 12.2f;
            float surface = bottom - Math.Clamp(level, 0, 1) * (bottom - top);
            float Left(float y) => rimLeft + (y - rimY) * taper / (baseY - rimY) + 0.95f;
            float Right(float y) => rimRight - (y - rimY) * taper / (baseY - rimY) - 0.95f;
            Span<PointF> coffee = [P(Left(surface), surface), P(Right(surface), surface), P(Right(bottom), bottom), P(Left(bottom), bottom)];
            c.FillPolygon(coffee, fill, 0);
        }

        // Tasse, anse, soucoupe.
        Span<PointF> cup = [P(rimLeft, rimY), P(rimLeft + taper, baseY), P(rimRight - taper, baseY), P(rimRight, rimY)];
        c.StrokePolyline(cup, stroke, line);
        Span<PointF> handle = stackalloc PointF[9];
        for (int i = 0; i < handle.Length; i++)
        {
            double a = (-70 + 140.0 * i / (handle.Length - 1)) * Math.PI / 180;
            handle[i] = P(11.2f + 2.6f * (float)Math.Cos(a), 8.3f + 2.6f * (float)Math.Sin(a));
        }
        c.StrokePolyline(handle, stroke, line);
        Span<PointF> saucer = [P(0.7f, 15.0f), P(14.0f, 15.0f)];
        c.StrokePolyline(saucer, stroke, line);

        // Fumée : deux volutes qui ondulent en montant, deux bouffées successives, puis s’estompent.
        if (steam >= 0)
        {
            float phase = Math.Clamp(steam, 0, 1);
            float rise = phase * 2 % 1;
            float alpha = (float)Math.Sin(Math.PI * rise) * (phase < 0.5f ? 0.95f : 0.8f);
            uint smoke = Shell.PopupRenderer.WithAlpha(ink, alpha);
            Span<PointF> wisp = stackalloc PointF[7];
            foreach (float x in new[] { 4.6f, 7.9f })
            {
                for (int i = 0; i < wisp.Length; i++)
                {
                    float t = i / (float)(wisp.Length - 1);
                    float y = 3.6f - rise * 1.0f - t * 2.6f;
                    wisp[i] = P(x + 0.7f * (float)Math.Sin((t + rise) * Math.PI * 2), Math.Max(0.4f, y));
                }
                c.StrokePolyline(wisp, Math.Max(1f, 1.1f * u), smoke);
            }
        }
    }
}
