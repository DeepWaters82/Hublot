using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Usb;

/// <summary>Ligne d'un stockage USB dans la carte du survol de l'icône : nom, espace libre, mini-jauge.</summary>
internal sealed class UsbRow(UsbState p) : HoverRow
{
    public override void Draw(in CardContext ctx, RectF row, float progress, int hover)
    {
        var (c, t) = (ctx.Canvas, ctx.Theme);
        float scale = ctx.Scale;
        float S(float v) => v * scale;
        var tile = new RectF(row.X + S(8), row.Y + (row.Height - S(36)) / 2, S(36), S(36));
        c.FillRoundRect(tile, S(6), t.Accent);
        using (var icon = new Font(S(18), Font.Icons)) c.DrawIcon(UsbGlyphs.For(p.Kind), icon, t.OnAccent, tile);

        using var title = new Font(S(14), Font.Title);
        using var caption = new Font(S(12), Font.Caption);
        float x = tile.Right + S(12), right = row.Right - S(12);
        c.DrawText(p.Title, title, t.TextPrimary, new RectF(x, row.Y + S(8), right - x, S(20)), TextAlign.Near, ellipsis: true);
        float y2 = row.Y + S(29);
        if (p.Storage is { } s)
        {
            c.DrawText(L.F("Usb.FreeOf", UsbState.Size(s.Free), UsbState.Size(s.Total)), caption, t.TextSecondary,
                new RectF(x, y2, right - x - S(64), S(16)), TextAlign.Near, ellipsis: true);
            var track = new RectF(right - S(56), y2 + S(6), S(56), S(4));
            c.FillRoundRect(track, S(2), t.Track);
            float used = s.UsedRatio;
            if (used > 0.005f)
                c.FillRoundRect(new RectF(track.X, track.Y, Math.Max(track.Height, track.Width * used), track.Height), S(2), UsbCard.UsageColor(s.UsedRatio, t));
        }
        else
        {
            c.DrawText(UsbCard.StatusText(p), caption, t.TextSecondary, new RectF(x, y2, right - x, S(16)), TextAlign.Near, ellipsis: true);
        }
    }
}

/// <summary>Icône Segoe Fluent selon le type de périphérique USB.</summary>
internal static class UsbGlyphs
{
    public static string For(UsbKind kind) => kind switch
    {
        UsbKind.Storage => "",
        UsbKind.Keyboard => "",
        UsbKind.Mouse => "",
        UsbKind.Audio => "",
        UsbKind.Webcam => "",
        UsbKind.Phone => "",
        UsbKind.Printer => "",
        UsbKind.Bluetooth => "",
        UsbKind.Receiver => "\uE703",
        UsbKind.Gamepad => "",
        _ => "",
    };
}
