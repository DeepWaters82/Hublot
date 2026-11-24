using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Transfers;

/// <summary>
/// Carte « Transferts de fichiers » : une section par opération (trois au plus), pixels effectifs :
///
///   ┌────────────────────────────────────────────────┐
///   │ ┌────┐ Copie de 11 éléments             [⏸] [✕] │
///   │ │ ⧉  │ Fichiers  →  Series                       │
///   │ └────┘                                            │
///   │ 13 %                                   120 Mo/s   │
///   │ ┌────────────────────────────────────────────┐ │   graphique : vitesse au fil de la progression,
///   │ │▁▂▃▅▆▅▄│                                     │ │   zone parcourue teintée, trait à la progression
///   │ └────────────────────────────────────────────┘ │
///   │ 🗎 Cyberpunk.Edgerunners.S02E02….mkv              │
///   │ ⏱ Environ 14 minutes            ≡ 9 (29,8 Go)    │
///   └────────────────────────────────────────────────┘
///
/// Couleurs : accentuation en cours, jaune / ambre en pause ou en attente d'une décision (comme Windows), vert une
/// fois terminé. Conflit de noms : le texte de Windows et ses choix (Remplacer, Ignorer, Me laisser décider), en
/// boutons. Parties cliquables : section × 100 + 0 (la carte : fenêtre de Windows au premier plan), 1 (pause /
/// reprise), 2 (annuler), 3 et plus (choix). Aucune ne ferme la carte. Le pourcentage avance en douceur d'une
/// lecture à la suivante (<see cref="ContinueFrom"/>).
/// </summary>
internal sealed class TransferCard(IReadOnlyList<TransferView> views, TransferActions actions, bool controls) : CardContent
{
    private const float CardWidth = 400, Section = 200, Margin = 16, ButtonSize = 32;
    private const float GraphTop = 90, GraphHeight = 44, RowTop = 146, RowGap = 22;
    private const double ProgressMs = 450;
    private const int MaxChoices = 3;

    /// <summary>Glyphes Segoe Fluent Icons.</summary>
    private const string CopyGlyph = "", MoveGlyph = "", DeleteGlyph = "", FolderGlyph = "",
        DoneGlyph = "", WarningGlyph = "", PauseGlyph = "", PlayGlyph = "", CloseGlyph = "",
        ArrowGlyph = "", FileGlyph = "", ClockGlyph = "", ItemsGlyph = "", ChevronGlyph = "";

    /// <summary>Pourcentage affiché au moment de la mise à jour, et début de l'avancée vers la nouvelle valeur.</summary>
    private readonly Dictionary<string, (float From, long Start)> _progress = [];

    public IReadOnlyList<TransferView> Views => views;

    public override string Key => "transfers";
    public override float Width => CardWidth;
    public override float Height => Math.Max(1, views.Count) * Section;
    public override string? SettingsPage => "Transfers";
    public override string LogName => "Transferts de fichiers";

    // ── Animation du pourcentage ─────────────────────────────────────────────────────────

    private float ShownPercent(TransferView view, long now, AnimClock clock)
    {
        float target = Math.Max(0, view.Percent);
        if (!clock.Animate || !_progress.TryGetValue(view.Key, out var a)) return target;
        double e = clock.Elapsed(a.Start, now) / ProgressMs;
        return e >= 1 ? target : a.From + (target - a.From) * (float)Easing.Decelerate(Easing.Clamp01(e));
    }

    public override void ContinueFrom(CardContent previous, long now, AnimClock clock, bool replay)
    {
        if (previous is not TransferCard old) return;
        foreach (var view in views)
        {
            if (old.Views.FirstOrDefault(v => v.Key == view.Key) is not { } before) continue;
            float from = old.ShownPercent(before, now, clock);
            if (Math.Abs(from - Math.Max(0, view.Percent)) > 0.01f) _progress[view.Key] = (from, now);
        }
    }

    public override bool IsAnimating(long now, AnimClock clock)
        => clock.Animate && _progress.Values.Any(a => clock.Elapsed(a.Start, now) < ProgressMs);

    // ── Parties cliquables ───────────────────────────────────────────────────────────────

    private static RectF CancelButton => new(CardWidth - 12 - ButtonSize, 14, ButtonSize, ButtonSize);
    private static RectF PauseButton => new(CardWidth - 12 - 2 * ButtonSize - 4, 14, ButtonSize, ButtonSize);
    private static RectF ChoiceRow(int i) => new(Margin, 104 + i * 31, CardWidth - 2 * Margin, 27);

    private bool ShowsCancel(TransferView v) => controls && v.Phase is not TransferPhase.Done;
    private bool ShowsPause(TransferView v) => controls && v.Phase is TransferPhase.Running or TransferPhase.Paused;

    private static bool Inside(RectF r, float x, float y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;

    public override int HitTest(float x, float y)
    {
        int s = (int)(y / Section);
        if (x < 0 || y < 0 || s >= views.Count) return -1;
        float ly = y - s * Section;
        var v = views[s];
        if (ShowsCancel(v) && Inside(CancelButton, x, ly)) return s * 100 + 2;
        if (ShowsPause(v) && Inside(PauseButton, x, ly)) return s * 100 + 1;
        if (v.Phase == TransferPhase.Attention)
            for (int i = 0; i < Math.Min(MaxChoices, v.Tile.Choices.Count); i++)
                if (Inside(ChoiceRow(i), x, ly)) return s * 100 + 3 + i;
        return s * 100;
    }

    public override bool Click(int part)
    {
        int s = part / 100, p = part % 100;
        if (s < 0 || s >= views.Count) return false;
        var v = views[s];
        switch (p)
        {
            case 1: actions.TogglePause(v); break;
            case 2: actions.Cancel(v); break;
            case >= 3 when p - 3 < v.Tile.Choices.Count: actions.Choose(v, v.Tile.Choices[p - 3]); break;
            default: actions.Open(v); break;
        }
        return false;
    }

    // ── Dessin ───────────────────────────────────────────────────────────────────────────

    public override void Draw(in CardContext ctx)
    {
        float line = Math.Max(1, MathF.Round(ctx.Scale));
        for (int i = 0; i < views.Count; i++)
        {
            float y0 = ctx.Card.Y + ctx.S(i * Section);
            if (i > 0)
                ctx.Canvas.FillRoundRect(new RectF(ctx.Card.X + ctx.S(Margin), y0, ctx.Card.Width - ctx.S(2 * Margin), line), 0.5f, ctx.Theme.SubtleStroke);
            int hover = ctx.Hover >= 0 && ctx.Hover / 100 == i ? ctx.Hover % 100 : -1;
            DrawSection(ctx, views[i], y0, hover);
        }
    }

    private void DrawSection(in CardContext ctx, TransferView v, float y0, int hover)
    {
        var (c, t, card) = (ctx.Canvas, ctx.Theme, ctx.Card);
        float k = ctx.Scale;
        RectF Abs(RectF b) => new(card.X + b.X * k, y0 + b.Y * k, b.Width * k, b.Height * k);
        uint tone = v.Phase switch
        {
            TransferPhase.Done => t.Success,
            TransferPhase.Paused or TransferPhase.Attention => t.Caution,
            _ => t.Accent,
        };

        using var title = new Font(ctx.S(14), Font.Title);
        using var caption = new Font(ctx.S(12), Font.Caption);
        using var big = new Font(ctx.S(15), Font.Title);
        using var tileIcons = new Font(ctx.S(20), Font.Icons);
        using var icons = new Font(ctx.S(14), Font.Icons);
        using var small = new Font(ctx.S(12), Font.Icons);

        // Tuile : nature de l'opération, coche une fois terminé, alerte s'il faut décider.
        var tile = Abs(new RectF(Margin, 16, 40, 40));
        c.FillRoundRect(tile, ctx.S(8), tone);
        string glyph = v.Phase switch
        {
            TransferPhase.Done => DoneGlyph,
            TransferPhase.Attention => WarningGlyph,
            _ => v.Tile.Kind switch { TransferKind.Copy => CopyGlyph, TransferKind.Move => MoveGlyph, TransferKind.Delete => DeleteGlyph, _ => FolderGlyph },
        };
        c.DrawIcon(glyph, tileIcons, t.OnAccent, tile);

        // Boutons pause / annuler en haut à droite ; le titre s'arrête avant.
        float textX = card.X + ctx.S(68), right = card.Right - ctx.S(Margin);
        if (ShowsPause(v))
        {
            var pause = Abs(PauseButton);
            if (hover == 1) c.FillRoundRect(pause, ctx.S(6), t.SubtleFill);
            c.DrawIcon(v.Phase == TransferPhase.Paused ? PlayGlyph : PauseGlyph, icons, t.TextPrimary, pause);
            right = pause.X - ctx.S(6);
        }
        if (ShowsCancel(v))
        {
            var cancel = Abs(CancelButton);
            if (hover == 2) c.FillRoundRect(cancel, ctx.S(6), t.SubtleFill);
            c.DrawIcon(CloseGlyph, icons, t.TextPrimary, cancel);
            if (!ShowsPause(v)) right = cancel.X - ctx.S(6);
        }
        c.DrawText(v.Tile.Operation, title, t.TextPrimary, new RectF(textX, y0 + ctx.S(16), right - textX, ctx.S(20)), TextAlign.Near, ellipsis: true);
        DrawRoute(c, t, caption, small, v.Tile, new RectF(textX, y0 + ctx.S(37), right - textX, ctx.S(18)), ctx.Scale);

        if (v.Phase == TransferPhase.Attention)
        {
            DrawAttention(ctx, v, y0, hover, caption, small);
            return;
        }

        // Pourcentage (ou « Terminé »), état, vitesse.
        float shown = ShownPercent(v, ctx.Now, ctx.Clock);
        var stats = new RectF(card.X + ctx.S(Margin), y0 + ctx.S(64), card.Width - ctx.S(2 * Margin), ctx.S(22));
        string head = v.Phase == TransferPhase.Done ? L.T("Xfer.Done") : v.Percent < 0 ? "…" : L.Percent((int)MathF.Round(shown));
        c.DrawText(head, big, v.Phase == TransferPhase.Done ? t.Success : t.TextPrimary, stats);
        if (v.Phase == TransferPhase.Paused)
        {
            float w = c.MeasureText(head, big);
            c.DrawText("·  " + L.T("Xfer.Paused"), caption, t.Caution, new RectF(stats.X + w + ctx.S(8), stats.Y, stats.Width - w, stats.Height));
        }
        else if (v.Speed is { } speed)
        {
            c.DrawText(speed, caption, t.TextSecondary, stats, TextAlign.Far);
        }

        DrawGraph(ctx, v, Abs(new RectF(Margin, GraphTop, CardWidth - 2 * Margin, GraphHeight)), shown, tone);

        // Détails : nom de l'élément, temps et éléments restants (textes de Windows).
        var row1 = Abs(new RectF(Margin, RowTop, CardWidth - 2 * Margin, 18));
        var row2 = Abs(new RectF(Margin, RowTop + RowGap, CardWidth - 2 * Margin, 18));
        if (v.Tile.DetailsHidden && v.Phase != TransferPhase.Done)
        {
            c.DrawText(L.T("Xfer.DetailsHidden"), caption, t.TextTertiary, row1, TextAlign.Near, ellipsis: true);
            return;
        }
        DrawRow(c, t, caption, small, FileGlyph, v.Tile.ItemName ?? "…", row1, ctx.Scale);
        if (v.Phase == TransferPhase.Done) return;
        string items = v.Tile.ItemsLeft ?? "";
        float itemsWidth = items.Length > 0 ? Math.Min(c.MeasureText(items, caption) + ctx.S(22), row2.Width / 2) : 0;
        DrawRow(c, t, caption, small, ClockGlyph, v.Tile.TimeLeft ?? "…", new RectF(row2.X, row2.Y, row2.Width - itemsWidth - ctx.S(12), row2.Height), ctx.Scale);
        if (items.Length > 0)
            DrawRow(c, t, caption, small, ItemsGlyph, items, new RectF(row2.Right - itemsWidth, row2.Y, itemsWidth, row2.Height), ctx.Scale);
    }

    /// <summary>« Source → Destination » ; place partagée, le plus court garde tout son texte.</summary>
    private static void DrawRoute(Canvas c, Theme t, Font caption, Font icons, TransferTile tile, RectF r, float scale)
    {
        string? from = tile.Source, to = tile.Target;
        if (from is null && to is null) return;
        if (to is null || from is null)
        {
            c.DrawText(from ?? to!, caption, t.TextSecondary, r, TextAlign.Near, ellipsis: true);
            return;
        }
        float arrow = 22 * scale, available = Math.Max(0, r.Width - arrow);
        float wFrom = c.MeasureText(from, caption), wTo = c.MeasureText(to, caption);
        if (wFrom + wTo > available)
        {
            float half = available / 2;
            (wFrom, wTo) = wFrom < half ? (wFrom, available - wFrom) : wTo < half ? (available - wTo, wTo) : (half, half);
        }
        c.DrawText(from, caption, t.TextSecondary, new RectF(r.X, r.Y, wFrom + 1, r.Height), TextAlign.Near, ellipsis: true);
        c.DrawIcon(ArrowGlyph, icons, t.TextTertiary, new RectF(r.X + wFrom, r.Y, arrow, r.Height));
        c.DrawText(to, caption, t.TextSecondary, new RectF(r.X + wFrom + arrow, r.Y, wTo + 1, r.Height), TextAlign.Near, ellipsis: true);
    }

    private static void DrawRow(Canvas c, Theme t, Font caption, Font icons, string glyph, string text, RectF r, float scale)
    {
        c.DrawIcon(glyph, icons, t.TextTertiary, new RectF(r.X, r.Y, 16 * scale, r.Height));
        c.DrawText(text, caption, t.TextSecondary, new RectF(r.X + 22 * scale, r.Y, Math.Max(0, r.Width - 22 * scale), r.Height), TextAlign.Near, ellipsis: true);
    }

    /// <summary>
    /// Graphique de la vitesse au fil de la progression (comme celui de Windows) : zone parcourue teintée, courbe
    /// remplie jusqu'à la progression, trait vertical à la progression. Sans mesure, la zone parcourue seule.
    /// </summary>
    private static void DrawGraph(in CardContext ctx, TransferView v, RectF g, float shown, uint tone)
    {
        var (c, t) = (ctx.Canvas, ctx.Theme);
        float radius = ctx.S(6);
        c.FillRoundRect(g, radius, t.SubtleFill);
        if (v.Percent < 0) return;
        float ratio = Math.Clamp(shown / 100f, 0, 1);
        if (ratio > 0) c.FillRoundRect(new RectF(g.X, g.Y, Math.Max(2 * radius, g.Width * ratio), g.Height), radius, PopupRenderer.WithAlpha(tone, 0.12f));

        float max = 0;
        foreach (var s in v.History) if (!float.IsNaN(s) && s > max) max = s;
        int last = Math.Clamp((int)MathF.Floor(shown), 0, 100);
        float inset = ctx.S(3), plotW = g.Width - 2 * inset, plotH = g.Height - ctx.S(10);
        if (max > 0)
        {
            var points = new List<PointF>(last + 3);
            for (int p = 0; p <= last; p++)
            {
                float s = v.History[p];
                if (float.IsNaN(s)) continue;
                points.Add(new PointF(g.X + inset + plotW * p / 100f, g.Bottom - inset - plotH * s / (max * 1.1f)));
            }
            if (points.Count >= 2)
            {
                var area = new PointF[points.Count + 2];
                points.CopyTo(area);
                area[^2] = new PointF(points[^1].X, g.Bottom - inset);
                area[^1] = new PointF(points[0].X, g.Bottom - inset);
                c.FillPolygon(area, PopupRenderer.WithAlpha(tone, 0.32f), 0);
                c.StrokePolyline(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(points), ctx.S(1.5f), tone);
            }
        }
        // Trait à la progression.
        float x = Math.Clamp(g.X + g.Width * ratio - ctx.S(1), g.X + ctx.S(2), g.Right - ctx.S(4));
        c.FillRoundRect(new RectF(x, g.Y + ctx.S(5), ctx.S(2), g.Height - ctx.S(10)), ctx.S(1), tone);
    }

    /// <summary>Opération en attente (conflit de noms, erreur) : texte de Windows sur deux lignes, puis ses choix.</summary>
    private static void DrawAttention(in CardContext ctx, TransferView v, float y0, int hover, Font caption, Font icons)
    {
        var (c, t, card) = (ctx.Canvas, ctx.Theme, ctx.Card);
        string text = string.IsNullOrEmpty(v.Tile.Attention) ? L.T("Xfer.ActionNeeded") : v.Tile.Attention;
        var area = new RectF(card.X + ctx.S(Margin), y0 + ctx.S(64), card.Width - ctx.S(2 * Margin), ctx.S(18));
        var (first, second) = SplitLines(c, text, caption, area.Width);
        c.DrawText(first, caption, t.TextPrimary, area, TextAlign.Near, ellipsis: true);
        if (second.Length > 0) c.DrawText(second, caption, t.TextPrimary, new RectF(area.X, area.Y + ctx.S(18), area.Width, area.Height), TextAlign.Near, ellipsis: true);

        var choices = v.Tile.Choices;
        if (choices.Count == 0)
        {
            c.DrawText(L.T("Xfer.OpenWindow"), caption, t.TextTertiary, new RectF(area.X, y0 + ctx.S(108), area.Width, area.Height), TextAlign.Near, ellipsis: true);
            return;
        }
        for (int i = 0; i < Math.Min(MaxChoices, choices.Count); i++)
        {
            var b = ChoiceRow(i);
            var r = new RectF(card.X + b.X * ctx.Scale, y0 + b.Y * ctx.Scale, b.Width * ctx.Scale, b.Height * ctx.Scale);
            c.FillRoundRect(r, ctx.S(5), hover == 3 + i ? t.Track : t.SubtleFill);
            c.DrawText(choices[i].Label, caption, t.TextPrimary, new RectF(r.X + ctx.S(12), r.Y, r.Width - ctx.S(40), r.Height), TextAlign.Near, ellipsis: true);
            c.DrawIcon(ChevronGlyph, icons, t.TextTertiary, new RectF(r.Right - ctx.S(28), r.Y, ctx.S(16), r.Height));
        }
    }

    /// <summary>Texte coupé en deux lignes au dernier espace qui tient (le reste, s'il y en a, finit par « … »).</summary>
    private static (string First, string Second) SplitLines(Canvas c, string text, Font font, float width)
    {
        if (c.MeasureText(text, font) <= width) return (text, "");
        int cut = -1;
        for (int i = text.IndexOf(' '); i > 0; i = text.IndexOf(' ', i + 1))
        {
            if (c.MeasureText(text[..i], font) > width) break;
            cut = i;
        }
        return cut <= 0 ? (text, "") : (text[..cut], text[(cut + 1)..]);
    }
}
