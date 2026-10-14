using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.NowPlaying;

/// <summary>Ligne « Lecture en cours » de la carte du survol : pochette, titre, artiste, lecture / pause, suivant.</summary>
internal sealed class MediaRow(MediaState state, Action<MediaState, MediaCommand> command) : HoverRow
{
    private const float ButtonSize = 34, RowWidth = 292;
    private MediaState _state = state;

    /// <summary>Rectangle d'un bouton en DIP relatifs à la ligne : lecture (2), suivant (3).</summary>
    private static RectF Button(int part)
        => new(RowWidth - 6 - (part == 3 ? ButtonSize : 2 * ButtonSize + 2), (54 - ButtonSize) / 2, ButtonSize, ButtonSize);

    public override int HitTest(float x, float y)
    {
        foreach (int part in new[] { 2, 3 })
        {
            var b = Button(part);
            if (x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom) return part;
        }
        return 0;
    }

    public override bool Click(int part)
    {
        if (part == 2 && _state.CanPlayPause)
        {
            // Le module reçoit l'état visé (comme pour la carte).
            _state = _state with { Playing = !_state.Playing };
            command(_state, MediaCommand.TogglePlayPause);
        }
        else if (part == 3 && _state.CanNext)
        {
            command(_state, MediaCommand.Next);
        }
        return false;
    }

    public override void Draw(in CardContext ctx, RectF row, float progress, int hover)
    {
        var (c, t, p) = (ctx.Canvas, ctx.Theme, _state);
        float k = ctx.Scale;
        RectF Abs(RectF b) => new(row.X + b.X * k, row.Y + b.Y * k, b.Width * k, b.Height * k);

        var cover = new RectF(row.X + ctx.S(8), row.Y + (row.Height - ctx.S(36)) / 2, ctx.S(36), ctx.S(36));
        MediaCard.DrawCover(c, cover, p.Cover, t, k, ctx.S(18));
        if (p.AppIcon is { } appIcon)
        {
            // Pastille de l'application source, à cheval sur le coin de la pochette.
            var badge = new RectF(cover.Right - ctx.S(11), cover.Bottom - ctx.S(11), ctx.S(15), ctx.S(15));
            c.DrawImage(appIcon.Pixels, appIcon.Size, appIcon.Size, badge, ctx.S(2));
        }

        using var title = new Font(ctx.S(14), Font.Title);
        using var caption = new Font(ctx.S(12), Font.Caption);
        using var icons = new Font(ctx.S(15), Font.Icons);
        var play = Abs(Button(2));
        var next = Abs(Button(3));
        float x = cover.Right + ctx.S(12), right = play.X - ctx.S(6);
        c.DrawText(p.Title, title, t.TextPrimary, new RectF(x, row.Y + ctx.S(8), right - x, ctx.S(20)), TextAlign.Near, ellipsis: true);
        c.DrawText(p.Artist, caption, t.TextSecondary, new RectF(x, row.Y + ctx.S(29), right - x, ctx.S(16)), TextAlign.Near, ellipsis: true);

        c.FillEllipse(play, p.CanPlayPause ? (hover == 2 ? PopupRenderer.WithAlpha(t.Accent, 0.85f) : t.Accent) : t.SubtleFill);
        MediaCard.DrawPlayPause(c, play, p.Playing ? 1 : 0, p.CanPlayPause ? t.OnAccent : t.TextTertiary);
        if (hover == 3 && p.CanNext) c.FillRoundRect(next, ctx.S(6), t.SubtleFill);
        c.DrawIcon(Glyphs.Next, icons, p.CanNext ? t.TextPrimary : PopupRenderer.WithAlpha(t.TextTertiary, 0.6f), next);
    }
}
