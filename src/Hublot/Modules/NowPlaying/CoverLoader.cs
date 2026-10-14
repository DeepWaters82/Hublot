using Hublot.Common;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Hublot.Modules.NowPlaying;

/// <summary>Décodage de la pochette fournie par la session média (décodeur d'images de Windows).</summary>
internal static class CoverLoader
{
    /// <summary>Côté en pixels : net jusqu'à une carte de 64 DIP à 300 %.</summary>
    private const int Size = 192;
    /// <summary>
    /// En dessous, c'est une icône (favicons : 16 à 64 px), pas une pochette. Attention, les vraies
    /// vignettes sont petites : 120 × 120 pour YouTube Music, 150 × 83 pour YouTube (mesuré).
    /// </summary>
    private const int MinSource = 72;

    /// <summary>
    /// Logo plutôt que pochette : une pochette est opaque, un logo (navigateur, site, application web) est
    /// posé sur un fond transparent. Plus de 4 % de pixels transparents = logo.
    /// </summary>
    private static bool IsLogo(byte[] bgra)
    {
        int transparent = 0, count = bgra.Length / 4;
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra[i] < 250) transparent++;
        return transparent > count / 25;
    }

    public static async Task<Cover?> LoadAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var stream = await reference.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            var decoder = await BitmapDecoder.CreateAsync(stream);
            uint w = decoder.OrientedPixelWidth, h = decoder.OrientedPixelHeight;
            if (w == 0 || h == 0) return null;
            // Icône de site ou de navigateur envoyée le temps que la vraie pochette arrive : trop petite.
            if (Math.Min(w, h) < MinSource)
            {
                Log.Write($"Vignette ignorée ({w}×{h} : icône, pas une pochette)");
                return null;
            }

            // Recadrage carré centré (les vignettes de vidéos sont en 16:9).
            double k = (double)Size / Math.Min(w, h);
            uint sw = (uint)Math.Max(Size, Math.Round(w * k)), sh = (uint)Math.Max(Size, Math.Round(h * k));
            var transform = new BitmapTransform
            {
                ScaledWidth = sw,
                ScaledHeight = sh,
                InterpolationMode = BitmapInterpolationMode.Fant,
                Bounds = new BitmapBounds { X = (sw - Size) / 2, Y = (sh - Size) / 2, Width = Size, Height = Size },
            };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            var pixels = data.DetachPixelData();
            if (IsLogo(pixels))
            {
                Log.Write("Vignette ignorée (fond transparent : logo, pas une pochette)");
                return null;
            }
            return new Cover(pixels, Size);
        }
        catch (Exception ex)
        {
            Log.Error("Pochette", ex);
            return null;
        }
    }
}
