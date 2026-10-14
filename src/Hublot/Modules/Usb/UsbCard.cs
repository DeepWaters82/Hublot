using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Usb;

/// <summary>
/// Carte d'un périphérique USB (pixels effectifs, mis à l'échelle). Stockage :
///
///   ┌────────────────────────────────────────────┐
///   │ ┌──────┐  KINGSTON (E:)        19,6 Go libres│
///   │ │  ▯   │  ● Connecté · 12,4 Go utilisés sur 32 Go
///   │ └──────┘  ━━━━━━━━━━━━───────────────────── │  jauge = espace occupé
///   └────────────────────────────────────────────┘
///
/// Autre périphérique : nom + « Connecté · USB », sans jauge. Pas d'animation propre : tuile et jauge
/// sont affichées directement à leur état final.
/// </summary>
internal sealed class UsbCard(UsbState state) : CardContent
{
    public UsbState State { get; } = state;
    public override string Key => "usb:" + State.Key;

    public override bool ShowOverFullscreen(Settings settings) => settings.UsbShowInFullscreen;

    internal static string StatusText(UsbState p)
    {
        if (!p.Connected) return L.T("Device.Disconnected");
        return p.StorageState switch
        {
            StorageState.Known when p.Storage is { } s => L.F("Usb.Used", UsbState.Size(s.Used), UsbState.Size(s.Total)),
            StorageState.Pending => L.T("Usb.Reading"),
            StorageState.Unavailable => L.T("Usb.NoVolume"),
            _ => L.T("Usb.Connected"),
        };
    }

    /// <summary>Accentuation ; orange au-delà de 90 % d'occupation, rouge au-delà de 97 %.</summary>
    internal static uint UsageColor(float usedRatio, Theme t) => usedRatio >= 0.97f ? t.Critical : usedRatio >= 0.9f ? t.Caution : t.Accent;

    public override void Draw(in CardContext ctx)
    {
        var (c, t, card, p) = (ctx.Canvas, ctx.Theme, ctx.Card, State);
        float scale = ctx.Scale;
        float S(float v) => v * scale;

        var tile = new RectF(card.X + S(16), card.Y + S(16), S(48), S(48));
        c.FillRoundRect(tile, S(6), p.Connected ? t.Accent : t.SubtleFill);
        if (!p.Connected) c.StrokeRoundRect(tile, S(6), t.SubtleStroke, Math.Max(1, MathF.Round(scale)));
        using (var icon = new Font(S(22), Font.Icons))
            c.DrawIcon(UsbGlyphs.For(p.Kind), icon, p.Connected ? t.OnAccent : t.TextSecondary, tile);

        using var title = new Font(S(14), Font.Title);
        using var caption = new Font(S(12), Font.Caption);
        float x = tile.Right + S(12), right = card.Right - S(16);
        var storage = p.Connected ? p.Storage : null;
        float top = tile.Y + (storage is not null ? 0 : S(5));

        // Ligne 1 : titre + espace libre à droite (comme le pourcentage de batterie).
        float titleRight = right;
        if (storage is not null)
        {
            string free = L.F("Usb.Free", UsbState.Size(storage.Free));
            float width = c.MeasureText(free, title);
            c.DrawText(free, title, t.TextPrimary, new RectF(x, top, right - x, S(20)), TextAlign.Far);
            titleRight = right - width - S(12);
        }
        c.DrawText(p.Title, title, t.TextPrimary, new RectF(x, top, titleRight - x, S(20)), TextAlign.Near, ellipsis: true);

        // Ligne 2 : état.
        float statusY = top + S(22);
        c.FillEllipse(new RectF(x, statusY + S(5), S(6), S(6)), p.Connected ? t.Success : t.TextTertiary);
        c.DrawText(StatusText(p), caption, t.TextSecondary, new RectF(x + S(12), statusY, right - x - S(12), S(16)), TextAlign.Near, ellipsis: true);

        // Ligne 3 : jauge d'occupation.
        if (storage is not null)
        {
            var track = new RectF(x, statusY + S(22), right - x, S(4));
            c.FillRoundRect(track, S(2), t.Track);
            float used = Math.Clamp(storage.UsedRatio, 0, 1);
            if (used > 0.005f)
            {
                float w = Math.Max(track.Height, track.Width * used);
                c.FillRoundRect(new RectF(track.X, track.Y, w, track.Height), S(2), UsageColor(storage.UsedRatio, t));
            }
        }
    }
}
