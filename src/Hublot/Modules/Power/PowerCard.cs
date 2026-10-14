using Hublot.Common;
using Hublot.Interop;
using Hublot.Modules.Bluetooth;
using Hublot.Shell;

namespace Hublot.Modules.Power;

/// <summary>
/// Carte « PC branché / débranché » (pixels effectifs, mis à l'échelle), sur le modèle des appareils
/// sans fil :
///
///   ┌────────────────────────────────────────────┐
///   │ ┌──────┐  LAPTOP-PC                   ▭ 76 % │
///   │ │  💻  │  ● Branché · en charge              │
///   │ └──────┘  ━━━━━━━━━━━━━━━━━━━━━━━──────────  │  jauge = charge de la batterie
///   └────────────────────────────────────────────┘
///
/// Pas d'animation propre : tuile, pourcentage et jauge sont affichés directement à leur état final.
/// </summary>
internal sealed class PowerCard(PowerState state) : CardContent
{
    public const string CardKey = "power";
    private const string LaptopGlyph = "\uE7F8";

    public PowerState State { get; private set; } = state;
    public override string Key => CardKey;

    public override bool ShowOverFullscreen(Settings settings) => settings.PowerShowInFullscreen;

    public override void ContinueFrom(CardContent previous, long now, AnimClock clock, bool replay)
    {
        // Nouveau pourcentage pendant l'affichage : garde le motif du popup (niveau atteint…).
        if (!replay && previous is PowerCard old) State = State with { Reason = old.State.Reason };
    }

    public override void Draw(in CardContext ctx)
    {
        var (c, t, card, p) = (ctx.Canvas, ctx.Theme, ctx.Card, State);
        float scale = ctx.Scale;
        float S(float v) => v * scale;

        // Tuile : accentuation, rouge en batterie faible.
        bool critical = p.Reason == PowerReason.Low || !p.Plugged && p.Percent <= 10;
        var tile = new RectF(card.X + S(16), card.Y + S(16), S(48), S(48));
        c.FillRoundRect(tile, S(6), critical ? t.Critical : t.Accent);
        using (var icon = new Font(S(22), Font.Icons))
            c.DrawIcon(LaptopGlyph, icon, t.OnAccent, tile);

        using var title = new Font(S(14), Font.Title);
        using var caption = new Font(S(12), Font.Caption);
        float x = tile.Right + S(12), right = card.Right - S(16);
        bool hasLevel = p.Percent is not null;
        float top = tile.Y + (hasLevel ? 0 : S(5));

        // Ligne 1 : nom du PC + pourcentage.
        float nameRight = right;
        if (p.Percent is int level)
        {
            float labelWidth = c.MeasureText(L.Percent(level), title);
            c.DrawText(L.Percent(level), title, t.TextPrimary, new RectF(x, top, right - x, S(20)), TextAlign.Far);
            using var batteryIcon = new Font(S(16), Font.Icons);
            float glyphRight = right - labelWidth - S(6);
            c.DrawText(Glyphs.Battery(level, p.Plugged), batteryIcon, DeviceCard.BatteryColor(level, true, t, text: true),
                new RectF(glyphRight - S(20), top, S(20), S(20)), TextAlign.Far);
            nameRight = glyphRight - S(20) - S(12);
        }
        c.DrawText(p.Name, title, t.TextPrimary, new RectF(x, top, nameRight - x, S(20)), TextAlign.Near, ellipsis: true);

        // Ligne 2 : source d'alimentation.
        float statusY = top + S(22);
        c.FillEllipse(new RectF(x, statusY + S(5), S(6), S(6)), p.Reason == PowerReason.Low ? t.Critical : p.Plugged ? t.Success : t.TextTertiary);
        c.DrawText(p.StatusText, caption, t.TextSecondary, new RectF(x + S(12), statusY, right - x - S(12), S(16)), TextAlign.Near, ellipsis: true);

        // Ligne 3 : jauge de charge.
        if (p.Percent is int pct)
        {
            var track = new RectF(x, statusY + S(22), right - x, S(4));
            c.FillRoundRect(track, S(2), t.Track);
            float value = Math.Clamp(pct / 100f, 0, 1);
            if (value > 0.005f)
            {
                float w = Math.Max(track.Height, track.Width * value);
                c.FillRoundRect(new RectF(track.X, track.Y, w, track.Height), S(2), DeviceCard.BatteryColor(pct, true, t, text: false));
            }
        }
    }
}
