using System.Diagnostics;
using Hublot.Common;
using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>Horloge d'animation : animations activées ou non, vitesse choisie dans les paramètres.</summary>
internal readonly record struct AnimClock(bool Animate, float Speed)
{
    /// <summary>Millisecondes écoulées, ramenées à la vitesse choisie (négatif avant le départ).</summary>
    public double Elapsed(long start, long now) => (now - start) * 1000.0 / Stopwatch.Frequency / Speed;

    public long Ticks(double milliseconds) => (long)(milliseconds * Speed * Stopwatch.Frequency / 1000);
}

/// <summary>Tout ce qu'il faut pour dessiner une carte à un instant donné.</summary>
/// <param name="Card">Rectangle de la carte (pixels) ; le fond est déjà dessiné.</param>
/// <param name="Hover">Partie survolée par la souris (<see cref="CardContent.HitTest"/>), -1 sinon.</param>
internal readonly record struct CardContext(Canvas Canvas, RectF Card, Theme Theme, float Scale, long Now, AnimClock Clock,
    int Hover, Settings Settings)
{
    public float S(float dip) => dip * Scale;
}

/// <summary>
/// Contenu d'une carte de popup, fourni par un module. Le Shell s'occupe du reste : fenêtre,
/// matériau, ombre, placement, entrée / sortie, fermeture automatique, souris.
/// </summary>
internal abstract class CardContent
{
    /// <summary>Identité de l'élément affiché : deux cartes de même clé se remplacent sur place.</summary>
    public abstract string Key { get; }

    /// <summary>Dimensions en pixels effectifs (DIP), avant mise à l'échelle.</summary>
    public virtual float Width => 360;
    public virtual float Height => 80;

    /// <summary>Début de l'affichage : point de départ des animations internes.</summary>
    public virtual void Begin(long start, AnimClock clock) { }

    /// <summary>
    /// Cette carte remplace <paramref name="previous"/> à l'écran. <paramref name="replay"/> : nouvel
    /// événement (animations rejouées) ; sinon simple complément d'information (continuité).
    /// </summary>
    public virtual void ContinueFrom(CardContent previous, long now, AnimClock clock, bool replay) => Begin(now, clock);

    public abstract void Draw(in CardContext ctx);

    /// <summary>Vrai tant qu'une animation interne est en cours : le Shell continue de demander des images.</summary>
    public virtual bool IsAnimating(long now, AnimClock clock) => false;

    /// <summary>Partie sous le point (DIP relatifs à la carte), -1 si aucune. Sert au survol et au clic.</summary>
    public virtual int HitTest(float x, float y) => 0;

    /// <summary>Clic sur une partie ; vrai pour fermer le popup.</summary>
    public virtual bool Click(int part) => true;

    /// <summary>S'affiche aussi par-dessus une application en plein écran (sinon : réglage général).</summary>
    public virtual bool ShowOverFullscreen(Settings settings) => false;
}

/// <summary>Ligne de la carte du survol de l'icône (une par appareil, par lecture en cours…).</summary>
internal abstract class HoverRow
{
    public virtual float Height => 54;

    /// <param name="progress">0 → 1 à l'ouverture (valeurs qui défilent).</param>
    /// <param name="hover">Partie survolée de cette ligne, -1 sinon.</param>
    public abstract void Draw(in CardContext ctx, RectF row, float progress, int hover);

    /// <summary>Partie sous le point (DIP relatifs à la ligne) : 0 = la ligne elle-même.</summary>
    public virtual int HitTest(float x, float y) => 0;

    /// <summary>Clic ; vrai pour fermer la carte.</summary>
    public virtual bool Click(int part) => false;
}

/// <summary>
/// Carte du survol de l'icône : les lignes de tous les modules, puis un pied « Paramètres ».
/// Parties : index de ligne × 100 + partie de la ligne ; <see cref="FooterPart"/> pour le pied.
/// </summary>
internal sealed class HoverCard(IReadOnlyList<HoverRow> rows, Action openSettings) : CardContent
{
    public const int FooterPart = 99_999;
    private const float Padding = 6, FooterHeight = 40;
    private long _start;

    public override string Key => "hover";
    public override float Width => 300;
    public override float Height => 2 * Padding + rows.Sum(r => r.Height) + 1 + FooterHeight;

    public override void Begin(long start, AnimClock clock) => _start = start + clock.Ticks(60);

    public override bool IsAnimating(long now, AnimClock clock) => clock.Animate && clock.Elapsed(_start, now) < 700;

    public override void Draw(in CardContext ctx)
    {
        var (c, t) = (ctx.Canvas, ctx.Theme);
        float progress = 1;
        if (ctx.Clock.Animate)
        {
            double tp = ctx.Clock.Elapsed(_start, ctx.Now) / 700;
            progress = tp <= 0 ? 0 : (float)Easing.Decelerate(Math.Min(1, tp));
        }

        float y = ctx.Card.Y + ctx.S(Padding);
        float line = Math.Max(1, MathF.Round(ctx.Scale));
        for (int i = 0; i < rows.Count; i++)
        {
            var rect = new RectF(ctx.Card.X + ctx.S(4), y, ctx.Card.Width - ctx.S(8), ctx.S(rows[i].Height));
            bool hovered = ctx.Hover >= 0 && ctx.Hover != FooterPart && ctx.Hover / 100 == i;
            if (hovered && ctx.Hover % 100 == 0) c.FillRoundRect(rect, ctx.S(4), t.SubtleFill);
            else if (i > 0) c.FillRoundRect(new RectF(rect.X + ctx.S(12), y, rect.Width - ctx.S(24), line), 0.5f, t.SubtleStroke);
            rows[i].Draw(ctx, rect, progress, hovered ? ctx.Hover % 100 : -1);
            y += rect.Height;
        }

        // Séparateur pleine largeur puis pied « Paramètres ».
        c.FillRoundRect(new RectF(ctx.Card.X, y, ctx.Card.Width, line), 0.5f, t.Border);
        var footer = new RectF(ctx.Card.X + ctx.S(4), y + ctx.S(3), ctx.Card.Width - ctx.S(8), ctx.S(FooterHeight) - ctx.S(4));
        if (ctx.Hover == FooterPart) c.FillRoundRect(footer, ctx.S(4), t.SubtleFill);
        using var icons = new Font(ctx.S(14), Font.Icons);
        using var caption = new Font(ctx.S(12), Font.Caption);
        c.DrawIcon("", icons, t.TextSecondary, new RectF(footer.X + ctx.S(12), footer.Y, ctx.S(20), footer.Height));
        c.DrawText(L.T("Hover.Settings"), caption, t.TextPrimary, new RectF(footer.X + ctx.S(42), footer.Y, footer.Width - ctx.S(80), footer.Height));
        c.DrawIcon("", icons, t.TextTertiary, new RectF(footer.Right - ctx.S(30), footer.Y, ctx.S(18), footer.Height));
    }

    public override int HitTest(float x, float y)
    {
        float py = y - Padding;
        if (x < 0 || py < 0) return -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (py < rows[i].Height)
            {
                int sub = rows[i].HitTest(x - 4, py);
                return sub < 0 ? -1 : i * 100 + sub;
            }
            py -= rows[i].Height;
        }
        return py >= 1 && py < 1 + FooterHeight ? FooterPart : -1;
    }

    public override bool Click(int part)
    {
        if (part == FooterPart)
        {
            openSettings();
            return true;
        }
        return part >= 0 && rows[part / 100].Click(part % 100);
    }
}
