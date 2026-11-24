namespace Hublot.Modules;

/// <summary>
/// Dernière manette connectée ou déconnectée (fabricant USB, instant), partagée entre la source Manettes et le
/// module USB. Un dongle de manette (GameSir 3537:1040…) change de mode à chaque branchement : il apparaît
/// comme « Périphérique d’entrée USB », disparaît, puis revient en « XBOX 360 For Windows » (avec un clavier) ;
/// sans cela, deux ou trois cartes USB s’ajoutaient à celle de la manette.
/// </summary>
internal static class RecentGamepad
{
    private const long WindowMs = 5000;
    private static ushort s_vendor;
    private static long s_at = long.MinValue / 2;

    public static void Seen(ushort vendor)
    {
        s_vendor = vendor;
        s_at = Environment.TickCount64;
    }

    /// <summary>
    /// Périphérique USB (pas un stockage) qui fait partie de la manette : même fabricant dans les 5 s d’un événement
    /// de manette, ou n’importe quel fabricant dans les 2,5 s (le mode intermédiaire du dongle peut en changer).
    /// </summary>
    public static bool Covers(ushort? vendor)
    {
        long elapsed = Environment.TickCount64 - s_at;
        return vendor is ushort v && v == s_vendor ? elapsed < WindowMs : elapsed < CloseMs;
    }

    private const long CloseMs = 2500;
}
