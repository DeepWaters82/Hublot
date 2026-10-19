using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>
/// Logo de Hublot : un hublot de navire — disque bleu Windows, huit rivets, cerclage clair — et derrière la
/// vitre, la mer (une vague dans la moitié basse) et un reflet. Dessiné en vectoriel à la taille exacte
/// demandée (net à tout DPI) ; en dessous de 24 px, sans rivets ni reflet (vitre plus grande, lisible). La même
/// image sert à la zone de notification (couleur d'accentuation) et aux exécutables (app.ico, bleu).
/// </summary>
internal static class TrayIconRenderer
{
    // Bleu du logo Windows 11 (#0078D4), en léger dégradé vertical.
    private const uint BlueTop = 0xFF1A8CE6, BlueBottom = 0xFF0067C0;

    /// <summary>
    /// Couleurs du logo pour la zone de notification : la couleur d'accentuation de Windows, plus claire sur une barre
    /// des tâches sombre (comme les icônes du système), et des traits blancs ou sombres selon le contraste.
    /// </summary>
    public static (uint Top, uint Bottom, uint Ink) TrayColors(bool lightTaskbar)
    {
        // AccentPalette : 1 = Light2, 2 = Light1, 3 = Accent, 4 = Dark1.
        uint? top = Theme.ReadAccent(lightTaskbar ? 3 : 2), bottom = Theme.ReadAccent(lightTaskbar ? 4 : 3);
        if (top is not uint t || bottom is not uint b) return (BlueTop, BlueBottom, 0xFFFFFFFF);
        float Luminance(uint c) => (0.2126f * (c >> 16 & 0xFF) + 0.7152f * (c >> 8 & 0xFF) + 0.0722f * (c & 0xFF)) / 255f;
        return (t, b, (Luminance(t) + Luminance(b)) / 2 > 0.6f ? 0xFF1C1C1C : 0xFFFFFFFF);
    }

    public static Canvas Draw(int size) => Draw(size, BlueTop, BlueBottom, 0xFFFFFFFF);

    /// <summary>Logo aux couleurs données (disque en dégradé, traits <paramref name="ink"/>).</summary>
    public static Canvas Draw(int size, uint top, uint bottom, uint ink)
    {
        var c = new Canvas(size, size);
        c.Clear();
        float inset = Math.Max(0.5f, size / 32f);
        var disc = new RectF(inset, inset, size - 2 * inset, size - 2 * inset);
        c.FillSquircle(disc, 2f, top, bottom); // exposant 2 : un cercle
        float unit = disc.Width / 24f;
        uint Ink(uint alpha) => alpha << 24 | (ink & 0x00FFFFFF);

        bool detailed = size >= 24;
        float cx = disc.X + disc.Width / 2, cy = disc.Y + disc.Height / 2;
        float ringWidth = Math.Max(1f, 1.4f * unit);
        // Vitre (grille de 24) : rayon 7,6, ou 8,6 en petit, sans rivets autour.
        float glass = (detailed ? 7.6f : 8.6f) * unit;

        // Mer : une vague (deux ondulations) sous le centre, puis l'arc du bas de la vitre, sous le cerclage.
        float water = glass - ringWidth * 0.4f, line = 1.6f * unit, half = MathF.Sqrt(water * water - line * line);
        float amplitude = 0.55f * unit, from = MathF.Atan2(line, half);
        Span<PointF> sea = stackalloc PointF[2 * 17];
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f;
            sea[i] = new PointF(cx - half + 2 * half * t, cy + line - amplitude * MathF.Sin(t * 4 * MathF.PI));
        }
        for (int i = 1; i <= 17; i++)
        {
            float a = from + (MathF.PI - 2 * from) * i / 17f; // de la droite à la gauche, par le bas
            sea[16 + i] = new PointF(cx + water * MathF.Cos(a), cy + water * MathF.Sin(a));
        }
        c.FillPolygon(sea[..33], Ink(0x55), 0);

        // Cerclage de la vitre.
        var ring = new RectF(cx - glass, cy - glass, 2 * glass, 2 * glass);
        c.StrokeRoundRect(ring, ring.Width, Ink(0xE6), ringWidth);
        float reach = glass - ringWidth / 2;

        if (detailed)
        {
            // Huit rivets, à mi-chemin entre le cerclage et le bord du disque.
            float orbit = (glass + ringWidth / 2 + disc.Width / 2) / 2, d = Math.Max(1.5f, 1.5f * unit);
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4;
                c.FillEllipse(new RectF(cx + orbit * MathF.Cos(a) - d / 2, cy + orbit * MathF.Sin(a) - d / 2, d, d), Ink(0xCC));
            }

            // Reflet sur la vitre, en haut à gauche.
            Span<PointF> glint = stackalloc PointF[9];
            float r = reach * 0.62f;
            for (int i = 0; i < glint.Length; i++)
            {
                double a = Math.PI * (1.08 + 0.30 * i / (glint.Length - 1)); // de 194° à 248°
                glint[i] = new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
            }
            c.StrokePolyline(glint, Math.Max(1f, 1.1f * unit), Ink(0x8C));
        }
        c.Flush();
        return c;
    }

    /// <summary>Planche de contrôle : le logo à plusieurs tailles, sur fond sombre et clair, agrandi ×4.</summary>
    public static void RenderSheet(string path)
    {
        int[] sizes = [16, 20, 24, 32, 48];
        const int zoom = 4, cell = 48 * zoom + 16;
        using var sheet = new Canvas(sizes.Length * cell, 2 * cell);
        sheet.Flush();
        for (int dark = 0; dark < 2; dark++)
        {
            uint bg = dark == 0 ? 0xFF202020u : 0xFFF3F3F3u;
            int y0 = dark * cell;
            unsafe
            {
                for (int y = y0; y < y0 + cell; y++)
                for (int x = 0; x < sheet.Width; x++) sheet.Bits[y * sheet.Width + x] = bg;
            }
            for (int k = 0; k < sizes.Length; k++)
            {
                using var icon = Draw(sizes[k]);
                icon.FlattenOnto(bg & 0xFFFFFF);
                unsafe
                {
                    for (int y = 0; y < sizes[k] * zoom; y++)
                    for (int x = 0; x < sizes[k] * zoom; x++)
                        sheet.Bits[(y0 + 8 + y) * sheet.Width + k * cell + 8 + x] = icon.Bits[(y / zoom) * icon.Width + x / zoom];
                }
            }
        }
        sheet.SavePng(path);
    }

    /// <summary>
    /// Logos du paquet Microsoft Store (AppxManifest.xml) : fond transparent, hublot centré. Une seule taille
    /// par logo (la plus grande utile) : sans fichier resources.pri, Windows les met à l'échelle.
    /// </summary>
    public static void WriteStoreAssets(string directory)
    {
        Directory.CreateDirectory(directory);
        Save("Square44x44Logo.png", 176, 176, 176);     // menu Démarrer, barre des tâches (44 DIP jusqu'à 400 %)
        Save("Square150x150Logo.png", 300, 300, 200);   // vignette moyenne (150 DIP à 200 %)
        Save("Wide310x150Logo.png", 620, 300, 200);     // vignette large
        Save("StoreLogo.png", 100, 100, 100);           // logo du paquet (50 DIP à 200 %)

        void Save(string name, int width, int height, int logo)
        {
            using var canvas = new Canvas(width, height);
            canvas.Clear();
            using (var icon = Draw(logo))
            {
                int x0 = (width - logo) / 2, y0 = (height - logo) / 2;
                unsafe
                {
                    for (int y = 0; y < logo; y++)
                    for (int x = 0; x < logo; x++)
                        canvas.Bits[(y0 + y) * width + x0 + x] = icon.Bits[y * logo + x];
                }
            }
            canvas.SavePng(Path.Combine(directory, name));
        }
    }

    /// <summary>Fichier .ico multi-tailles (PNG) pour les exécutables.</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using var icon = Draw(size);
            string temp = Path.Combine(Path.GetTempPath(), $"hublot-icon-{size}.png");
            icon.SavePng(temp);
            images.Add(File.ReadAllBytes(temp));
            File.Delete(temp);
        }
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(dim);
            w.Write(dim);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) w.Write(image);
    }
}
