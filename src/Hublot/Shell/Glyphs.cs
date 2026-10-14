namespace Hublot.Shell;

/// <summary>Glyphes Segoe Fluent Icons (identiques dans Segoe MDL2 Assets) communs aux modules.</summary>
internal static class Glyphs
{
    public const string Bluetooth = "";
    public const string Play = "", Pause = "", Previous = "", Next = "";
    public const string Music = "";
    /// <summary>Téléchargement (mise à jour disponible).</summary>
    public const string Download = "\uE896";

    /// <summary>Battery0 à Battery9 (U+E850…E859) puis Battery10 (U+E83F), par tranche de 10 %.</summary>
    public static string Battery(int percent, bool charging = false)
    {
        int step = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        if (charging)
        {
            // BatteryCharging0…8 (U+E85A…E862), BatteryCharging9 (U+E83E), BatteryCharging10 (U+EA93)
            return ((char)(step switch { 10 => 0xEA93, 9 => 0xE83E, _ => 0xE85A + step })).ToString();
        }
        return ((char)(step == 10 ? 0xE83F : 0xE850 + step)).ToString();
    }
}
