using System.Runtime.InteropServices;

namespace Hublot.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType, bmWidth, bmHeight, bmWidthBytes;
    public ushort bmPlanes, bmBitsPixel;
    public nint bmBits;
}

/// <summary>
/// Nom et icône d'une application d'après son AppUserModelId, via le dossier « Applications » du shell
/// (shell:AppsFolder\AUMID) : couvre les applications empaquetées, les applications classiques
/// et les applications web installées (« YouTube Music » pour « Brave._crx_… »).
/// COM appelé directement par la table de méthodes : pas de génération de code, compatible trimming.
/// </summary>
internal static unsafe partial class ShellItems
{
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int SIGDN_NORMALDISPLAY = 0, SIIGBF_ICONONLY = 0x4;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, Guid* riid, nint* item);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint memory);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);

    /// <summary>Nom affiché et icône (BGRA prémultiplié, carré de <paramref name="iconSize"/> px), ou null.</summary>
    public static (string? Name, byte[]? Icon) Describe(string appId, int iconSize)
    {
        nint item;
        var iid = IID_IShellItem;
        if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + appId, 0, &iid, &item) != 0 || item == 0) return default;
        try
        {
            var vtable = *(nint**)item;
            string? name = null;
            char* text;
            // IShellItem::GetDisplayName (méthode 5)
            if (((delegate* unmanaged[Stdcall]<nint, int, char**, int>)vtable[5])(item, SIGDN_NORMALDISPLAY, &text) == 0)
            {
                name = new string(text);
                CoTaskMemFree((nint)text);
            }

            byte[]? icon = null;
            nint factory;
            var iidFactory = IID_IShellItemImageFactory;
            // IUnknown::QueryInterface (méthode 0)
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtable[0])(item, &iidFactory, &factory) == 0)
            {
                nint bitmap;
                // IShellItemImageFactory::GetImage (méthode 3)
                var getImage = (delegate* unmanaged[Stdcall]<nint, SIZE, int, nint*, int>)(*(nint**)factory)[3];
                if (getImage(factory, new SIZE(iconSize, iconSize), SIIGBF_ICONONLY, &bitmap) == 0)
                {
                    icon = ReadBitmap(bitmap, iconSize);
                    Gdi32.DeleteObject(bitmap);
                }
                Release(factory);
            }
            return (name, icon);
        }
        finally
        {
            Release(item);
        }
    }

    private static readonly Guid IID_IPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    /// <summary>PKEY_AppUserModel_ID.</summary>
    private static readonly Guid AppUserModelFormat = new("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3");
    private const uint AppUserModelIdPid = 5;
    private const ushort VT_LPWSTR = 31;

    [LibraryImport("shell32.dll")]
    private static partial int SHGetPropertyStoreForWindow(nint hwnd, Guid* riid, nint* store);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(void* value);

    /// <summary>
    /// AppUserModelId déclaré par une fenêtre (applications web installées, applications du Store…),
    /// ou null : la plupart des fenêtres classiques n'en déclarent pas.
    /// </summary>
    public static string? WindowAppId(nint window)
    {
        nint store;
        var iid = IID_IPropertyStore;
        if (window == 0 || SHGetPropertyStoreForWindow(window, &iid, &store) != 0 || store == 0) return null;
        try
        {
            // PROPERTYKEY { GUID fmtid; DWORD pid; } — PROPVARIANT : 24 octets en 64 bits, valeur à l'offset 8.
            var key = stackalloc byte[20];
            *(Guid*)key = AppUserModelFormat;
            *(uint*)(key + 16) = AppUserModelIdPid;
            var value = stackalloc byte[24];
            new Span<byte>(value, 24).Clear();
            // IPropertyStore::GetValue (méthode 5)
            if (((delegate* unmanaged[Stdcall]<nint, void*, void*, int>)(*(nint**)store)[5])(store, key, value) != 0) return null;
            string? id = *(ushort*)value == VT_LPWSTR && *(nint*)(value + 8) != 0 ? new string(*(char**)(value + 8)) : null;
            PropVariantClear(value);
            return id;
        }
        finally
        {
            Release(store);
        }
    }

    private static void Release(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)unknown)[2])(unknown);

    /// <summary>Pixels 32 bits de haut en bas, alpha prémultiplié (le shell rend selon les cas l'un ou l'autre).</summary>
    private static byte[]? ReadBitmap(nint bitmap, int expected)
    {
        BITMAP bm;
        if (GetObjectW(bitmap, sizeof(BITMAP), &bm) == 0 || bm.bmWidth != expected || bm.bmHeight != expected) return null;
        var pixels = new byte[expected * expected * 4];
        var info = stackalloc byte[sizeof(BITMAPINFOHEADER) + 16];
        var header = (BITMAPINFOHEADER*)info;
        *header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = expected, biHeight = -expected, biPlanes = 1, biBitCount = 32 };
        var dc = User32.GetDC(0);
        int lines;
        fixed (byte* p = pixels) lines = GetDIBits(dc, bitmap, 0, (uint)expected, p, header, 0);
        User32.ReleaseDC(0, dc);
        if (lines == 0) return null;

        bool anyAlpha = false, straight = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a != 0) anyAlpha = true;
            if (pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a) straight = true;
        }
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (!anyAlpha) { pixels[i + 3] = 255; continue; }
            if (!straight) continue;
            int a = pixels[i + 3];
            pixels[i] = (byte)(pixels[i] * a / 255);
            pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
            pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
        }
        return pixels;
    }
}
