using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Audio;

/// <summary>
/// Ce que la carte affiche d'un branchement : description (« Casque »), adaptateur, sortie et / ou micro, emplacement
/// de la prise (EPcxGeoLocation : 1 = arrière, 2 = avant ; 0 = inconnu).
/// </summary>
internal sealed record JackState(string Name, string Adapter, bool Plugged, bool Output, bool Microphone, int Location = 0)
{
    public string Title => Name.Length > 0 ? Name : L.T(Output ? "Jack.Headphones" : "Jack.Microphone");

    /// <summary>« Branché · avec micro · Realtek(R) Audio ».</summary>
    public string StatusText
    {
        get
        {
            string text = L.T(Plugged ? "Jack.Plugged" : "Jack.Unplugged");
            if (Output && Microphone) text += " · " + L.T("Jack.WithMic");
            if (Location is 1 or 2) text += " · " + L.T(Location == 2 ? "Jack.Front" : "Jack.Rear");
            else if (Adapter.Length > 0) text += " · " + Adapter;
            return text;
        }
    }
}

/// <summary>
/// Carte « prise audio », sur le modèle de la carte d'alimentation : tuile (casque ou micro), description,
/// état. Pas d'animation propre.
/// </summary>
internal sealed class JackCard(JackState state) : CardContent
{
    private const string HeadphonesGlyph = "", MicrophoneGlyph = "";

    public JackState State { get; } = state;
    public override string Key => "jack";

    public override string? SettingsPage => "Audio";

    public override void Draw(in CardContext ctx)
    {
        var (c, t, card, p) = (ctx.Canvas, ctx.Theme, ctx.Card, State);
        float scale = ctx.Scale;
        float S(float v) => v * scale;

        var tile = new RectF(card.X + S(16), card.Y + S(16), S(48), S(48));
        c.FillRoundRect(tile, S(6), p.Plugged ? t.Accent : t.SubtleFill);
        using (var icon = new Font(S(22), Font.Icons))
            c.DrawIcon(p.Output ? HeadphonesGlyph : MicrophoneGlyph, icon, p.Plugged ? t.OnAccent : t.TextSecondary, tile);

        using var title = new Font(S(14), Font.Title);
        using var caption = new Font(S(12), Font.Caption);
        float x = tile.Right + S(12), right = card.Right - S(16), top = tile.Y + S(5);
        c.DrawText(p.Title, title, t.TextPrimary, new RectF(x, top, right - x, S(20)), TextAlign.Near, ellipsis: true);

        float statusY = top + S(22);
        c.FillEllipse(new RectF(x, statusY + S(5), S(6), S(6)), p.Plugged ? t.Success : t.TextTertiary);
        c.DrawText(p.StatusText, caption, t.TextSecondary, new RectF(x + S(12), statusY, right - x - S(12), S(16)), TextAlign.Near, ellipsis: true);
    }
}
