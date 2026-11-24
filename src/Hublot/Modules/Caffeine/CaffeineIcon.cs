using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Caffeine;

/// <summary>
/// Mug de café (grille de 16, toute la hauteur), pour la zone de notification et les cartes : contour à la couleur
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
    /// <remarks>
    /// 3.9.2 : forme de mug (bord fermé, parois droites, fond arrondi, anse ronde), sans soucoupe, qui occupe toute
    /// la hauteur ; trait d'un nombre entier de pixels et parois / bord calés sur la grille : net à 16, 20 et 24 px.
    /// </remarks>
    public static void Draw(Canvas c, RectF box, float level, uint fill, uint ink, float steam)
    {
        float u = box.Width / 16f;
        float stroke = Math.Max(1, MathF.Round(box.Width / 11f));
        // Trait impair : centre sur un demi-pixel ; pair : sur un pixel entier (bords nets).
        float Snap(float v) => (int)stroke % 2 == 1 ? MathF.Floor(v) + 0.5f : MathF.Round(v);
        uint line = level < 0 ? Shell.PopupRenderer.WithAlpha(ink, 0.6f) : ink;

        float left = Snap(box.X + 1.5f * u), right = Snap(box.X + 11.3f * u), rim = Snap(box.Y + 3.4f * u);
        float bend = box.Y + 9.8f * u, cx = (left + right) / 2, rx = (right - left) / 2;
        float ry = box.Y + 14.9f * u - stroke / 2 - bend;

        // Café : intérieur de la tasse (retrait du trait et d'un petit jour), du fond jusqu'au niveau.
        if (level > 0.01f)
        {
            float inset = stroke / 2 + Math.Max(1, MathF.Round(0.8f * u));
            float li = left + inset, ri = right - inset, rxi = rx - inset, ryi = ry - inset;
            float top = rim + inset, bottom = bend + ryi;
            float surface = bottom - Math.Clamp(level, 0, 1) * (bottom - top);
            Span<PointF> coffee = stackalloc PointF[24];
            int n = 0;
            const int arc = 18;
            if (surface <= bend)
            {
                coffee[n++] = new(li, surface);
                for (int i = 0; i <= arc; i++)
                {
                    double a = Math.PI - Math.PI * i / arc;
                    coffee[n++] = new(cx + rxi * (float)Math.Cos(a), bend + ryi * (float)Math.Sin(a));
                }
                coffee[n++] = new(ri, surface);
            }
            else
            {
                // Plus bas que le début de l'arrondi : seulement la calotte sous la surface.
                double k = Math.Asin(Math.Clamp((surface - bend) / ryi, 0, 1));
                for (int i = 0; i <= arc; i++)
                {
                    double a = Math.PI - k - (Math.PI - 2 * k) * i / arc;
                    coffee[n++] = new(cx + rxi * (float)Math.Cos(a), bend + ryi * (float)Math.Sin(a));
                }
            }
            if (n >= 3) c.FillPolygon(coffee[..n], fill, 0);
        }

        // Contour : bord, paroi gauche, fond arrondi, paroi droite, retour au bord (tracé fermé, jointures rondes).
        Span<PointF> cup = stackalloc PointF[26];
        int m = 0;
        cup[m++] = new(right, rim);
        cup[m++] = new(left, rim);
        const int bottomSteps = 20;
        for (int i = 0; i <= bottomSteps; i++)
        {
            double a = Math.PI - Math.PI * i / bottomSteps;
            cup[m++] = new(cx + rx * (float)Math.Cos(a), bend + ry * (float)Math.Sin(a));
        }
        cup[m++] = new(right, rim);
        cup[m++] = new(left, rim);
        c.StrokePolyline(cup[..m], stroke, line);

        // Anse : demi-ellipse accrochée à la paroi droite.
        Span<PointF> handle = stackalloc PointF[13];
        float hcy = box.Y + 7.0f * u, hrx = 3.3f * u, hry = 2.6f * u;
        for (int i = 0; i < handle.Length; i++)
        {
            double a = -Math.PI / 2 + Math.PI * i / (handle.Length - 1);
            handle[i] = new(right + hrx * (float)Math.Cos(a), hcy + hry * (float)Math.Sin(a));
        }
        c.StrokePolyline(handle, stroke, line);

        // Fumée : deux volutes au-dessus du bord, deux bouffées successives, puis elles s'estompent.
        if (steam >= 0)
        {
            float phase = Math.Clamp(steam, 0, 1);
            float rise = phase * 2 % 1;
            float alpha = (float)Math.Sin(Math.PI * rise) * (phase < 0.5f ? 0.95f : 0.8f);
            uint smoke = Shell.PopupRenderer.WithAlpha(ink, alpha);
            float wispStroke = Math.Max(1, stroke * 0.75f);
            Span<PointF> wisp = stackalloc PointF[7];
            foreach (float x in new[] { cx - 1.7f * u, cx + 1.7f * u })
            {
                for (int i = 0; i < wisp.Length; i++)
                {
                    float t = i / (float)(wisp.Length - 1);
                    float y = rim - stroke - (0.2f + rise * 0.6f + t * 1.7f) * u;
                    wisp[i] = new(x + 0.6f * u * (float)Math.Sin((t + rise) * Math.PI * 2), Math.Max(box.Y + wispStroke / 2, y));
                }
                c.StrokePolyline(wisp, wispStroke, smoke);
            }
        }
    }
}
