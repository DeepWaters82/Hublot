using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>
/// Habillage commun à toutes les cartes : surface, contour et ombre (matériau « Uni »).
/// Le contenu est dessiné par la carte elle-même (<see cref="CardContent.Draw"/>).
/// </summary>
internal static class PopupRenderer
{
    public const float CornerRadius = 8; // OverlayCornerRadius Fluent

    public static int ShadowMargin(float scale) => (int)MathF.Ceiling(28 * scale);

    /// <summary>Carte opaque ou translucide avec ombre portée (popup sans matériau système).</summary>
    public static void DrawCard(Canvas c, RectF card, Theme t, float scale, int opacityPercent)
    {
        c.DropShadow(card, CornerRadius * scale, blur: Math.Max(1, (int)MathF.Round(6 * scale)), offsetY: 6 * scale,
            t.ShadowOpacity * Math.Clamp(opacityPercent, 20, 100) / 100f);
        c.FillRoundRect(card, CornerRadius * scale, WithAlpha(t.Background, opacityPercent / 100f));
        c.StrokeRoundRect(card, CornerRadius * scale, t.Border, Math.Max(1, MathF.Round(scale)));
    }

    public static uint WithAlpha(uint argb, float factor)
    {
        uint a = (uint)Math.Clamp(MathF.Round((argb >> 24) * factor), 0, 255);
        return (a << 24) | (argb & 0x00FFFFFF);
    }
}
