using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>Carte simple : icône, titre, sous-titre (« Rien à afficher », aperçus…).</summary>
/// <param name="onClick">Action au clic (mise à jour disponible…) ; sans action, le clic ferme la carte.</param>
internal sealed class MessageCard(string key, string glyph, string title, string subtitle, Action? onClick = null) : CardContent
{
    public override string Key => key;

    public override bool Click(int part)
    {
        onClick?.Invoke();
        return true;
    }

    public override void Draw(in CardContext ctx)
    {
        var (c, t, card) = (ctx.Canvas, ctx.Theme, ctx.Card);
        var tile = new RectF(card.X + ctx.S(16), card.Y + ctx.S(16), ctx.S(48), ctx.S(48));
        c.FillRoundRect(tile, ctx.S(6), t.SubtleFill);
        c.StrokeRoundRect(tile, ctx.S(6), t.SubtleStroke, Math.Max(1, MathF.Round(ctx.Scale)));
        using (var icon = new Font(ctx.S(22), Font.Icons)) c.DrawIcon(glyph, icon, t.TextSecondary, tile);
        using var titleFont = new Font(ctx.S(14), Font.Title);
        using var caption = new Font(ctx.S(12), Font.Caption);
        float x = tile.Right + ctx.S(12), w = card.Right - ctx.S(16) - x;
        c.DrawText(title, titleFont, t.TextPrimary, new RectF(x, tile.Y + ctx.S(5), w, ctx.S(20)), TextAlign.Near, ellipsis: true);
        c.DrawText(subtitle, caption, t.TextSecondary, new RectF(x, tile.Y + ctx.S(27), w, ctx.S(16)), TextAlign.Near, ellipsis: true);
    }
}

/// <summary>Ligne simple de la carte du survol (« Rien à afficher »).</summary>
internal sealed class MessageRow(string glyph, string title, string subtitle) : HoverRow
{
    public override int HitTest(float x, float y) => -1;

    public override void Draw(in CardContext ctx, RectF row, float progress, int hover)
    {
        var (c, t) = (ctx.Canvas, ctx.Theme);
        var tile = new RectF(row.X + ctx.S(8), row.Y + (row.Height - ctx.S(36)) / 2, ctx.S(36), ctx.S(36));
        c.FillRoundRect(tile, ctx.S(6), t.SubtleFill);
        using (var icon = new Font(ctx.S(18), Font.Icons)) c.DrawIcon(glyph, icon, t.TextSecondary, tile);
        using var titleFont = new Font(ctx.S(14), Font.Title);
        using var caption = new Font(ctx.S(12), Font.Caption);
        float x = tile.Right + ctx.S(12), w = row.Right - ctx.S(12) - x;
        c.DrawText(title, titleFont, t.TextSecondary, new RectF(x, row.Y + ctx.S(8), w, ctx.S(20)), TextAlign.Near, ellipsis: true);
        c.DrawText(subtitle, caption, t.TextTertiary, new RectF(x, row.Y + ctx.S(29), w, ctx.S(16)), TextAlign.Near, ellipsis: true);
    }
}
