using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>
/// Logo de Hublot : un hublot — disque bleu Windows, cerclage clair — dont la vitre est partagée en
/// quatre par une croisée, clin d'œil à la fenêtre de Windows sans reprendre son logo. Dessiné en vectoriel à la taille exacte demandée (net à tout DPI) ; la même image
/// sert à la zone de notification et aux exécutables (app.ico).
/// </summary>
internal static class TrayIconRenderer
{
    // Bleu du logo Windows 11 (#0078D4), en léger dégradé vertical.
    private const uint BlueTop = 0xFF1A8CE6, BlueBottom = 0xFF0067C0;

    public static Canvas Draw(int size)
    {
        var c = new Canvas(size, size);
        c.Clear();
        float inset = Math.Max(0.5f, size / 32f);
        var disc = new RectF(inset, inset, size - 2 * inset, size - 2 * inset);
        c.FillSquircle(disc, 2f, BlueTop, BlueBottom); // exposant 2 : un cercle
        float unit = disc.Width / 24f;

        // Cerclage du hublot : anneau clair à l'intérieur du disque.
        float ringWidth = Math.Max(1f, 1.6f * unit);
        var ring = disc.Inflate(-2.4f * unit);
        c.StrokeRoundRect(ring, disc.Width, 0xE6FFFFFF, ringWidth);

        // Croisée : deux barres qui rejoignent le cerclage (une fenêtre, pas un viseur).
        float cx = disc.X + disc.Width / 2, cy = disc.Y + disc.Height / 2;
        float reach = ring.Width / 2 - ringWidth / 2, bar = Math.Max(1f, 1.5f * unit);
        if (size <= 20) bar = 1f; // trait d'un pixel exact : net à 16 et 20 px
        // (rayon non nul : un rectangle arrondi de rayon 0 ne dessine rien ; les bouts disparaissent sous le cerclage)
        c.FillRoundRect(new RectF(cx - bar / 2, cy - reach, bar, 2 * reach), bar / 2, 0xFFFFFFFF);
        c.FillRoundRect(new RectF(cx - reach, cy - bar / 2, 2 * reach, bar), bar / 2, 0xFFFFFFFF);

        // Reflet sur la vitre, en haut à gauche ; illisible et inutile aux petites tailles.
        if (size >= 24)
        {
            Span<PointF> glint = stackalloc PointF[9];
            float r = reach * 0.62f;
            for (int i = 0; i < glint.Length; i++)
            {
                double a = Math.PI * (1.08 + 0.30 * i / (glint.Length - 1)); // de 194° à 248°
                glint[i] = new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
            }
            c.StrokePolyline(glint, Math.Max(1f, 1.1f * unit), 0x8CFFFFFF);
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
