using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Screens;

/// <summary>
/// Carte d'un écran branché ou débranché (pixels effectifs, mis à l'échelle) :
///
///   ┌────────────────────────────────────────────┐
///   │ ┌──────┐  DELL U2720Q                       │
///   │ │  🖥  │  ● Connecté · HDMI · 2560 × 1440 · 60 Hz
///   │ └──────┘                                    │
///   └────────────────────────────────────────────┘
/// </summary>
internal sealed class DisplayCard(DisplayInfo display, bool connected) : CardContent
{
    private const string MonitorGlyph = "", LaptopGlyph = "";

    public override string Key => "display:" + display.Path;

    public override string? SettingsPage => "Displays";

    // Un écran se branche aussi pendant une présentation ou un jeu : réglage général du plein écran.
    public override void Draw(in CardContext ctx)
    {
        var (c, t, card) = (ctx.Canvas, ctx.Theme, ctx.Card);
        float scale = ctx.Scale;
        float S(float v) => v * scale;

        var tile = new RectF(card.X + S(16), card.Y + S(16), S(48), S(48));
        c.FillRoundRect(tile, S(6), connected ? t.Accent : t.SubtleFill);
        if (!connected) c.StrokeRoundRect(tile, S(6), t.SubtleStroke, Math.Max(1, MathF.Round(scale)));
        using (var icon = new Font(S(22), Font.Icons))
            c.DrawIcon(display.Internal ? LaptopGlyph : MonitorGlyph, icon, connected ? t.OnAccent : t.TextSecondary, tile);

        using var title = new Font(S(14), Font.Title);
        using var caption = new Font(S(12), Font.Caption);
        float x = tile.Right + S(12), right = card.Right - S(16), top = tile.Y + S(5);
        c.DrawText(display.Name, title, t.TextPrimary, new RectF(x, top, right - x, S(20)), TextAlign.Near, ellipsis: true);

        float statusY = top + S(22);
        string status = connected ? L.F("Display.Connected", display.Connector, display.Mode) : L.F("Display.Disconnected", display.Connector);
        c.FillEllipse(new RectF(x, statusY + S(5), S(6), S(6)), connected ? t.Success : t.TextTertiary);
        c.DrawText(status, caption, t.TextSecondary, new RectF(x + S(12), statusY, right - x - S(12), S(16)), TextAlign.Near, ellipsis: true);
    }
}
