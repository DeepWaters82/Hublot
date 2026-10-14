using static Hublot.Interop.GdiPlus;

namespace Hublot.Shell;

/// <summary>Police GDI+ en pixels, avec repli sur la première famille installée.</summary>
internal sealed class Font : IDisposable
{
    // Windows 11 : Segoe UI Variable / Segoe Fluent Icons. Windows 10 : Segoe UI / Segoe MDL2 Assets.
    public static readonly string[] Title = ["Segoe UI Variable Text Semibold", "Segoe UI Semibold"];
    public static readonly string[] Caption = ["Segoe UI Variable Small", "Segoe UI"];
    public static readonly string[] Icons = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    // Les familles sont créées une seule fois et conservées : quelques octets chacune.
    private static readonly Dictionary<string, nint> s_families = [];

    public nint Handle { get; }
    public nint FamilyHandle { get; }
    public float Pixels { get; }

    public Font(float pixels, string[] families)
    {
        FamilyHandle = Family(families);
        Pixels = pixels;
        Check(GdipCreateFont(FamilyHandle, pixels, 0, UnitPixel, out var handle));
        Handle = handle;
    }

    private static nint Family(string[] candidates)
    {
        var key = candidates[0];
        if (s_families.TryGetValue(key, out var family)) return family;
        foreach (var name in candidates)
        {
            if (GdipCreateFontFamilyFromName(name, 0, out family) == 0)
                return s_families[key] = family;
        }
        throw new InvalidOperationException($"Aucune police disponible parmi : {string.Join(", ", candidates)}");
    }

    public void Dispose() => GdipDeleteFont(Handle);
}
