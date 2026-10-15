using System.Diagnostics;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.NowPlaying;

/// <summary>
/// Carte « Lecture en cours » (pixels effectifs, mis à l'échelle) :
///
///   ┌───────────────────────────────────────────┐
///   │ ┌──────┐  Titre du morceau           [▣] │   [▣] icône de l'application source
///   │ │pochette│ Artiste                        │
///   │ └──────┘                   ⏮   (⏯)   ⏭   │
///   │ 1:24  ━━━━━━━━━━━━━──────────────────  3:52 │   progression (option, si communiquée)
///   └───────────────────────────────────────────┘
///
/// Parties cliquables : 0 = la carte (ferme), 1 = précédent, 2 = lecture / pause, 3 = suivant ;
/// 1000 à 2000 = la progression (option), position visée en millièmes de la durée.
/// Les boutons ne ferment pas la carte ; ceux que l'application n'autorise pas sont grisés.
///
/// Transitions (la carte est remplacée à chaque mise à jour : elles se poursuivent d'une instance à la
/// suivante, <see cref="ContinueFrom"/>) : nouveau titre → l'ancien texte s'efface en montant, le nouveau
/// arrive par le bas ; nouvelle pochette → fondu enchaîné ; lecture ↔ pause → morphing du bouton ;
/// appui sur un bouton → léger enfoncement et éclat.
/// </summary>
internal sealed class MediaCard(MediaState state, Action<MediaState, MediaCommand> command,
    Action<MediaState, TimeSpan>? seek = null, bool progressRow = false) : CardContent
{
    private const float CoverSize = 76, ButtonHeight = 38, ButtonTop = 54;
    /// <summary>
    /// Boutons : précédent / suivant de 34 DIP, lecture de 38, espacés de 6. Le pictogramme (≈ 14 DIP) du bouton
    /// « suivant » s'arrête sur la marge de 16 DIP, comme l'icône de l'application et la durée : on aligne ce que
    /// l'œil voit, la zone cliquable (plus large) déborde dans la marge.
    /// </summary>
    private const float SideButton = 34, PlayButton = 38, ButtonGap = 6, GlyphInk = 14, Margin = 16;
    /// <summary>Rangée de progression : barre entre le temps écoulé et la durée.</summary>
    private const float RowTop = 98, RowHeight = 16, BarInset = 60;
    private const int SeekPart = 1000;
    /// <summary>Durées : morphing lecture ↔ pause, changement de titre, fondu de pochette, appui.</summary>
    private const double MorphMs = 260, TextMs = 420, CoverMs = 340, PressMs = 320;

    // Forme affichée : 0 = triangle (lecture possible), 1 = deux barres (en lecture). -1 = pas d'animation.
    private float _morphFrom = -1;
    private long _morphStart;
    // Titre précédent (texte qui s'efface), pochette précédente (fondu), bouton enfoncé.
    private MediaState? _textFrom;
    private long _textStart;
    private bool _coverFading;
    private Cover? _coverFrom;
    private long _coverStart;
    private int _pressPart;
    private long _pressStart;
    private AnimClock _clock = new(true, 1);

    public MediaState State { get; private set; } = state;

    public override string Key => State.Key;

    /// <summary>
    /// Avec la progression : une rangée de plus. Hauteur fixe dès que l'option est active, même sans
    /// durée connue (direct, changement de titre) : une carte de taille différente serait recréée.
    /// </summary>
    public override float Height => progressRow || State.Timeline is not null ? 126 : 108;

    private float MorphTarget => State.Playing ? 1 : 0;

    /// <summary>Forme du bouton à l'instant donné (0 = triangle, 1 = barres).</summary>
    private float MorphAt(long now, AnimClock clock)
    {
        if (_morphFrom < 0 || !clock.Animate) return MorphTarget;
        double e = clock.Elapsed(_morphStart, now) / MorphMs;
        if (e >= 1) return MorphTarget;
        return _morphFrom + (float)((MorphTarget - _morphFrom) * Easing.CubicBezier(0.55, 0, 0.1, 1, Easing.Clamp01(e)));
    }

    /// <summary>Avancement (0-1) d'une transition ; 1 si elle est finie ou si les animations sont coupées.</summary>
    private static double Progress(long start, double ms, long now, AnimClock clock)
        => clock.Animate ? Easing.Clamp01(clock.Elapsed(start, now) / ms) : 1;

    /// <summary>Même session : morphing, changement de titre, fondu de pochette et appui se poursuivent ou commencent.</summary>
    public override void ContinueFrom(CardContent previous, long now, AnimClock clock, bool replay)
    {
        if (previous is not MediaCard old || old.Key != Key) return;
        if (old.MorphTarget == MorphTarget) (_morphFrom, _morphStart) = (old._morphFrom, old._morphStart);
        else (_morphFrom, _morphStart) = (old.MorphAt(now, clock), now);

        if (old.State.Title != State.Title || old.State.Artist != State.Artist) (_textFrom, _textStart) = (old.State, now);
        else (_textFrom, _textStart) = (old._textFrom, old._textStart);

        if (!ReferenceEquals(old.State.Cover, State.Cover)) (_coverFading, _coverFrom, _coverStart) = (true, old.State.Cover, now);
        else (_coverFading, _coverFrom, _coverStart) = (old._coverFading, old._coverFrom, old._coverStart);

        (_pressPart, _pressStart) = (old._pressPart, old._pressStart);
    }

    public override bool IsAnimating(long now, AnimClock clock)
    {
        if (!clock.Animate) return false;
        return _morphFrom >= 0 && clock.Elapsed(_morphStart, now) < MorphMs
            || _textFrom is not null && clock.Elapsed(_textStart, now) < TextMs
            || _coverFading && clock.Elapsed(_coverStart, now) < CoverMs
            || _pressPart != 0 && clock.Elapsed(_pressStart, now) < PressMs;
    }

    /// <summary>Rectangle d'un bouton, en DIP relatifs à la carte ; de droite à gauche : suivant (3), lecture (2), précédent (1).</summary>
    private RectF Button(int part)
    {
        float right = Width - Margin + (SideButton - GlyphInk) / 2;
        float next = right - SideButton, play = next - ButtonGap - PlayButton, previous = play - ButtonGap - SideButton;
        return part switch
        {
            3 => new RectF(next, ButtonTop, SideButton, ButtonHeight),
            2 => new RectF(play, ButtonTop, PlayButton, ButtonHeight),
            _ => new RectF(previous, ButtonTop, SideButton, ButtonHeight),
        };
    }

    /// <summary>Progression cliquable : option, et application qui accepte un changement de position.</summary>
    private bool Seekable => seek is not null && State.CanSeek && State.Timeline is not null;

    /// <summary>Position (0-1) sur la barre pour une abscisse en DIP relatifs à la carte.</summary>
    private float BarRatio(float x) => Math.Clamp((x - BarInset) / (Width - 2 * BarInset), 0, 1);

    public override int HitTest(float x, float y)
    {
        // Zone un peu plus large que la barre (4 DIP) : facile à viser.
        if (Seekable && y >= RowTop - 4 && y < RowTop + RowHeight + 4 && x >= BarInset - 6 && x <= Width - BarInset + 6)
            return SeekPart + (int)MathF.Round(BarRatio(x) * 1000);
        for (int part = 1; part <= 3; part++)
        {
            var b = Button(part);
            if (x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom) return part;
        }
        return 0;
    }

    public override bool Click(int part)
    {
        if (part >= SeekPart && Seekable && State.Timeline is { } timeline)
        {
            var target = timeline.Duration * ((part - SeekPart) / 1000.0);
            seek!(State, target);
            // Retour immédiat : la barre saute à la position visée, l'application confirme ensuite.
            State = State with { Timeline = timeline with { Position = target, UpdatedUtc = DateTime.UtcNow } };
            return false;
        }
        MediaCommand? cmd = part switch { 1 => MediaCommand.Previous, 2 => MediaCommand.TogglePlayPause, 3 => MediaCommand.Next, _ => null };
        if (cmd is null) return true;
        if (!Enabled(part)) return false;
        long now = Stopwatch.GetTimestamp();
        (_pressPart, _pressStart) = (part, now);
        if (cmd == MediaCommand.TogglePlayPause)
        {
            // Retour immédiat : le bouton se transforme, la progression se fige (ou repart) à la position
            // affichée. Le module reçoit l'état visé et le maintient jusqu'à confirmation de l'application.
            _morphFrom = MorphAt(now, _clock);
            _morphStart = now;
            var frozen = State.Timeline is { } t ? t with { Position = t.Current(State.Playing), UpdatedUtc = DateTime.UtcNow } : null;
            State = State with { Playing = !State.Playing, Timeline = frozen };
        }
        command(State, cmd.Value);
        return false;
    }

    private bool Enabled(int part) => part switch
    {
        1 => State.CanPrevious,
        2 => State.CanPlayPause,
        3 => State.CanNext,
        _ => true,
    };

    /// <summary>
    /// Appui sur un bouton : enfoncement rapide (−12 %) puis retour en douceur, et un éclat qui s'estompe.
    /// Renvoie (échelle, intensité de l'éclat 0-1).
    /// </summary>
    private (float Scale, float Flash) PressAt(int part, long now, AnimClock clock)
    {
        if (_pressPart != part) return (1, 0);
        double e = Progress(_pressStart, PressMs, now, clock);
        if (e >= 1) return (1, 0);
        double dip = e < 0.3 ? Easing.Decelerate(e / 0.3) : 1 - Easing.Decelerate((e - 0.3) / 0.7);
        return ((float)(1 - 0.12 * dip), (float)(1 - e));
    }

    public override void Draw(in CardContext ctx)
    {
        var (c, t, card, p) = (ctx.Canvas, ctx.Theme, ctx.Card, State);
        float k = ctx.Scale;
        _clock = ctx.Clock;
        RectF Abs(RectF b) => new(card.X + b.X * k, card.Y + b.Y * k, b.Width * k, b.Height * k);
        double coverFade = _coverFading ? Progress(_coverStart, CoverMs, ctx.Now, ctx.Clock) : 1;

        // Couleurs de la pochette (option) : teinte de fond sur le matériau, et accentuation (bouton, progression)
        // qui remplace celle de Windows. Elles suivent le fondu de la pochette.
        if (ctx.Settings.MediaCoverColors)
        {
            var (tint, accent, track) = Colors(p.Cover, t);
            if (coverFade < 1)
            {
                var (tintFrom, accentFrom, trackFrom) = Colors(_coverFrom, t);
                float e = (float)Easing.Decelerate(coverFade);
                (tint, accent, track) = (CoverColors.Lerp(tintFrom, tint, e), CoverColors.Lerp(accentFrom, accent, e), CoverColors.Lerp(trackFrom, track, e));
            }
            if (tint >> 24 != 0)
            {
                // En retrait du contour (matériau « Uni ») ; le matériau système arrondit déjà la fenêtre.
                float inset = Math.Max(1, MathF.Round(k));
                c.FillRoundRect(new RectF(card.X + inset, card.Y + inset, card.Width - 2 * inset, card.Height - 2 * inset),
                    PopupRenderer.CornerRadius * k - inset, tint);
            }
            t = t with { Accent = accent, Track = track };
        }

        // Pochette : fondu enchaîné de l'ancienne vers la nouvelle.
        var cover = Abs(new RectF(16, 16, CoverSize, CoverSize));
        if (coverFade < 1) DrawCover(c, cover, _coverFrom, t, k, ctx.S(30));
        DrawCover(c, cover, p.Cover, t, k, ctx.S(30), (float)Easing.Decelerate(coverFade));

        using var title = new Font(ctx.S(14), Font.Title);
        using var caption = new Font(ctx.S(12), Font.Caption);
        using var icons = new Font(ctx.S(16), Font.Icons);
        // Icône de l'application source en haut à droite ; le texte s'arrête avant.
        float x = cover.Right + ctx.S(12), right = card.Right - ctx.S(16);
        if (p.AppIcon is { } appIcon)
        {
            var icon = Abs(new RectF(Width - 16 - 20, 14, 20, 20));
            c.DrawImage(appIcon.Pixels, appIcon.Size, appIcon.Size, icon, ctx.S(2));
            right = icon.X - ctx.S(10);
        }

        // Titre et artiste : l'ancien s'efface en montant, le nouveau arrive par le bas.
        float titleY = card.Y + ctx.S(16), artistY = card.Y + ctx.S(37), titleH = ctx.S(20), artistH = ctx.S(16);
        double tt = _textFrom is not null ? Progress(_textStart, TextMs, ctx.Now, ctx.Clock) : 1;
        if (tt < 1 && _textFrom is { } before)
        {
            double a = Easing.Clamp01(tt / 0.45);
            DrawLines(before, 1 - (float)a, -ctx.S(6) * (float)Easing.Accelerate(a));
        }
        double arrive = Easing.Decelerate(Easing.Clamp01((tt - 0.25) / 0.75));
        DrawLines(p, (float)arrive, ctx.S(8) * (1 - (float)arrive));

        void DrawLines(MediaState s, float opacity, float offset)
        {
            if (opacity <= 0.01f) return;
            c.DrawText(s.Title, title, PopupRenderer.WithAlpha(t.TextPrimary, opacity),
                new RectF(x, titleY + offset, right - x, titleH), TextAlign.Near, ellipsis: true);
            c.DrawText(s.Artist, caption, PopupRenderer.WithAlpha(t.TextSecondary, opacity),
                new RectF(x, artistY + offset, right - x, artistH), TextAlign.Near, ellipsis: true);
        }

        for (int part = 1; part <= 3; part++)
        {
            var bt = Abs(Button(part));
            bool enabled = Enabled(part), hovered = ctx.Hover == part && enabled;
            var (pressScale, flash) = PressAt(part, ctx.Now, ctx.Clock);
            if (pressScale < 0.999f) c.ScaleAround(bt.X + bt.Width / 2, bt.Y + bt.Height / 2, pressScale);
            if (part == 2)
            {
                // Lecture / pause : pastille d'accentuation, comme le bouton principal des lecteurs Windows.
                float d = bt.Height;
                var circle = new RectF(bt.X + (bt.Width - d) / 2, bt.Y, d, d);
                c.FillEllipse(circle, enabled ? (hovered ? PopupRenderer.WithAlpha(t.Accent, 0.85f) : t.Accent) : t.SubtleFill);
                if (flash > 0) c.FillEllipse(circle, PopupRenderer.WithAlpha(0xFFFFFFFF, 0.25f * flash));
                DrawPlayPause(c, circle, MorphAt(ctx.Now, ctx.Clock), enabled ? t.OnAccent : t.TextTertiary);
            }
            else
            {
                if (hovered) c.FillRoundRect(bt, ctx.S(6), t.SubtleFill);
                // Éclat d'accentuation qui s'estompe : on « sent » que le bouton a agi.
                if (flash > 0) c.FillRoundRect(bt, ctx.S(6), PopupRenderer.WithAlpha(t.Accent, 0.28f * flash));
                c.DrawIcon(part == 1 ? Glyphs.Previous : Glyphs.Next, icons,
                    enabled ? t.TextPrimary : PopupRenderer.WithAlpha(t.TextTertiary, 0.6f), bt);
            }
            if (pressScale < 0.999f) c.ResetTransform();
        }

        DrawProgress(ctx, t, p);
    }

    /// <summary>
    /// Teinte de fond, accentuation et piste de la progression (l'accentuation estompée) tirées d'une pochette ;
    /// sans pochette colorée, le thème tel quel.
    /// </summary>
    private static (uint Tint, uint Accent, uint Track) Colors(Cover? cover, Theme t)
    {
        if (cover is not { Seed: not 0 and var seed }) return (0, t.Accent, t.Track);
        var (tint, accent) = CoverColors.For(seed, t.Dark);
        return (tint, accent, PopupRenderer.WithAlpha(accent, 0.32f));
    }

    /// <summary>
    /// Progression : redessinée chaque seconde par le module tant que la lecture continue. Survolée (si
    /// cliquable) : barre épaissie, pastille et temps à la position visée. Rangée réservée sans durée connue
    /// (direct, titre en cours de changement) : piste seule.
    /// </summary>
    private void DrawProgress(in CardContext ctx, Theme t, MediaState p)
    {
        var (c, card) = (ctx.Canvas, ctx.Card);
        float rowY = card.Y + ctx.S(RowTop), rowH = ctx.S(RowHeight);
        if (p.Timeline is not { } timeline)
        {
            if (progressRow)
                c.FillRoundRect(new RectF(card.X + ctx.S(BarInset), rowY + rowH / 2 - ctx.S(2), card.Width - ctx.S(2 * BarInset), ctx.S(4)), ctx.S(2), t.Track);
            return;
        }
        int hoverPart = ctx.Hover >= SeekPart && Seekable ? ctx.Hover - SeekPart : -1;
        var position = timeline.Current(p.Playing);
        var shownTime = hoverPart >= 0 ? timeline.Duration * (hoverPart / 1000.0) : position;
        using var small = new Font(ctx.S(11), Font.Caption);
        c.DrawText(MediaTimeline.Format(shownTime), small, hoverPart >= 0 ? t.TextPrimary : t.TextSecondary,
            new RectF(card.X + ctx.S(16), rowY, ctx.S(40), rowH), TextAlign.Near);
        c.DrawText(MediaTimeline.Format(timeline.Duration), small, t.TextSecondary, new RectF(card.Right - ctx.S(56), rowY, ctx.S(40), rowH), TextAlign.Far);
        float thickness = ctx.S(hoverPart >= 0 ? 6 : 4);
        var track = new RectF(card.X + ctx.S(BarInset), rowY + rowH / 2 - thickness / 2, card.Width - ctx.S(2 * BarInset), thickness);
        c.FillRoundRect(track, thickness / 2, t.Track);
        float ratio = (float)(position.TotalSeconds / timeline.Duration.TotalSeconds);
        if (ratio > 0.002f)
            c.FillRoundRect(new RectF(track.X, track.Y, Math.Max(track.Height, track.Width * Math.Min(ratio, 1)), track.Height), thickness / 2, t.Accent);
        if (hoverPart >= 0)
        {
            float cx = track.X + track.Width * hoverPart / 1000f, d = ctx.S(12);
            c.FillEllipse(new RectF(cx - d / 2, track.Y + track.Height / 2 - d / 2, d, d), t.Accent);
        }
    }

    /// <summary>
    /// Bouton lecture / pause dessiné au centre de <paramref name="circle"/>, en vectoriel (le triangle du
    /// glyphe « Play » n'est pas centré dans son cadratin). <paramref name="shape"/> va de 0 (triangle :
    /// lecture possible) à 1 (deux barres : en lecture) ; entre les deux, chaque moitié du triangle se
    /// transforme en une barre — le morphing des lecteurs Windows. Le triangle est légèrement décalé vers
    /// la droite (centrage optique : centré sur sa boîte, sa masse paraît à gauche).
    /// </summary>
    internal static void DrawPlayPause(Canvas c, RectF circle, float shape, uint color)
    {
        float d = circle.Width, cx = circle.X + d / 2, cy = circle.Y + d / 2;
        float h = d * 0.36f, round = Math.Max(1, d * 0.07f);
        // Le contour arrondi élargit chaque forme de round / 2 de chaque côté : on le retranche.
        float bh = h - round, bar = d * 0.11f - round, gap = d * 0.11f + round;
        float th = h - round, tw = th * 0.9f, left = cx - tw * 0.38f, right = left + tw, mid = (left + right) / 2;

        Span<PointF> pauseLeft = [new(cx - gap / 2 - bar, cy - bh / 2), new(cx - gap / 2, cy - bh / 2), new(cx - gap / 2, cy + bh / 2), new(cx - gap / 2 - bar, cy + bh / 2)];
        Span<PointF> pauseRight = [new(cx + gap / 2, cy - bh / 2), new(cx + gap / 2 + bar, cy - bh / 2), new(cx + gap / 2 + bar, cy + bh / 2), new(cx + gap / 2, cy + bh / 2)];
        Span<PointF> playLeft = [new(left, cy - th / 2), new(mid, cy - th / 4), new(mid, cy + th / 4), new(left, cy + th / 2)];
        Span<PointF> playRight = [new(mid, cy - th / 4), new(right, cy), new(right, cy), new(mid, cy + th / 4)];

        float v = Math.Clamp(shape, 0, 1);
        Span<PointF> quad = stackalloc PointF[4];
        for (int half = 0; half < 2; half++)
        {
            var from = half == 0 ? playLeft : playRight;
            var to = half == 0 ? pauseLeft : pauseRight;
            for (int i = 0; i < 4; i++)
                quad[i] = new PointF(from[i].X + (to[i].X - from[i].X) * v, from[i].Y + (to[i].Y - from[i].Y) * v);
            c.FillPolygon(quad, color, round);
        }
    }


    /// <summary>Pochette arrondie, ou à défaut une tuile d'accentuation avec une note de musique.</summary>
    internal static void DrawCover(Canvas c, RectF rect, Cover? cover, Theme t, float scale, float glyphSize, float opacity = 1)
    {
        if (opacity <= 0.003f) return;
        if (cover is not null)
        {
            c.DrawImage(cover.Pixels, cover.Size, cover.Size, rect, 6 * scale, opacity);
            c.StrokeRoundRect(rect, 6 * scale, PopupRenderer.WithAlpha(t.SubtleStroke, opacity), Math.Max(1, MathF.Round(scale)));
            return;
        }
        c.FillRoundRect(rect, 6 * scale, PopupRenderer.WithAlpha(t.Accent, opacity));
        using var icon = new Font(glyphSize, Font.Icons);
        c.DrawIcon(Glyphs.Music, icon, PopupRenderer.WithAlpha(t.OnAccent, opacity), rect);
    }
}
