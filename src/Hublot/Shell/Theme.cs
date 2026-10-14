using Microsoft.Win32;

namespace Hublot.Shell;

/// <summary>
/// Jetons de couleur Fluent (WinUI 3) pour une surface de type « flyout », en ARGB.
/// Le popup suit le mode Windows (barre des tâches, notifications) et la couleur d'accentuation.
/// </summary>
internal sealed record Theme(
    bool Dark,
    uint Background,
    uint Border,
    uint TextPrimary,
    uint TextSecondary,
    uint TextTertiary,
    uint SubtleFill,
    uint SubtleStroke,
    uint Track,
    uint Accent,
    uint OnAccent,
    uint Success,
    uint Caution,
    uint Critical,
    float ShadowOpacity)
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccentKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    public static bool SystemUsesLightTheme()
        => Registry.GetValue(PersonalizeKey, "SystemUsesLightTheme", 0) is int v && v == 1;

    /// <summary>Thème effectif selon les paramètres (mode Système = mode de la barre des tâches).</summary>
    public static Theme Resolve(Common.Settings settings)
    {
        bool light = settings.Theme switch
        {
            Common.PopupTheme.Light => true,
            Common.PopupTheme.Dark => false,
            _ => SystemUsesLightTheme(),
        };
        // Fluent : AccentFillColorDefault = AccentDark1 en clair, AccentLight2 en sombre.
        // Sans accent : gris neutre de même contraste.
        uint accent = settings.UseAccentColor
            ? ReadAccent(light ? 4 : 1) ?? (light ? 0xFF005FB8 : 0xFF60CDFF)
            : light ? 0xFF5C5C5C : 0xFFCECECE;
        return light ? CreateLight(accent) : CreateDark(accent);
    }

    public static Theme CreateLight(uint accent) => new(
        Dark: false,
        Background: 0xFFF9F9F9,
        Border: 0x17000000,
        TextPrimary: 0xE4000000,
        TextSecondary: 0x9E000000,
        TextTertiary: 0x72000000,
        SubtleFill: 0x0A000000,
        SubtleStroke: 0x0F000000,
        Track: 0x1F000000,
        Accent: accent,
        OnAccent: 0xFFFFFFFF,
        Success: 0xFF0F7B0F,
        Caution: 0xFF9D5D00,
        Critical: 0xFFC42B1C,
        ShadowOpacity: 0.20f);

    public static Theme CreateDark(uint accent) => new(
        Dark: true,
        Background: 0xFF2C2C2C,
        Border: 0x1FFFFFFF,
        TextPrimary: 0xFFFFFFFF,
        TextSecondary: 0xC5FFFFFF,
        TextTertiary: 0x87FFFFFF,
        SubtleFill: 0x0FFFFFFF,
        SubtleStroke: 0x12FFFFFF,
        Track: 0x28FFFFFF,
        Accent: accent,
        OnAccent: 0xFF000000,
        Success: 0xFF6CCB5F,
        Caution: 0xFFFCE100,
        Critical: 0xFFFF99A4,
        ShadowOpacity: 0.45f);

    /// <summary>
    /// AccentPalette : 8 couleurs RGBA (Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, —).
    /// </summary>
    private static uint? ReadAccent(int index)
    {
        if (Registry.GetValue(AccentKey, "AccentPalette", null) is not byte[] p || p.Length < 32) return null;
        int i = index * 4;
        return 0xFF000000 | (uint)(p[i] << 16 | p[i + 1] << 8 | p[i + 2]);
    }
}
