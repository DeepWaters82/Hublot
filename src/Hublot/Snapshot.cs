using System.Diagnostics;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Modules.Audio;
using Hublot.Modules.Bluetooth;
using Hublot.Modules.NowPlaying;
using Hublot.Modules.Power;
using Hublot.Modules.Screens;
using Hublot.Modules.Usb;
using Hublot.Shell;

namespace Hublot;

/// <summary>Rendu hors écran des cartes de chaque module (clair / sombre), pour contrôler le design.</summary>
internal static class Snapshot
{
    /// <param name="transparent">Fond transparent autour des cartes (visuels du Store composés à part).</param>
    public static void Render(string directory, float scale = 1.5f, bool transparent = false)
    {
        Directory.CreateDirectory(directory);
        var settings = new Settings();
        var media = new MediaState("Spotify.exe", "Spotify", "Bohemian Rhapsody - Remastered 2011", "Queen", true, true, true, true, SampleCover());
        CardContent[] cards =
        [
            new MediaCard(media, (_, _) => { }),
            new MediaCard(media with { Playing = false, CanSeek = true, Timeline = new MediaTimeline(TimeSpan.FromSeconds(84), TimeSpan.FromSeconds(354), DateTime.UtcNow) }, (_, _) => { }, (_, _) => { }),
            new MediaCard(media with { Playing = false, CanPrevious = false, Cover = null, Title = "Lo-fi beats to relax", Artist = "Lofi Girl", AppName = "Google Chrome" }, (_, _) => { }),
            new DeviceCard(new DeviceState("a", "Galaxy Buds2 Pro", true, DeviceKind.Headphones, 85, BatteryState.Known), null),
            new UsbCard(new UsbState("u1", "DataTraveler 3.0", true, UsbKind.Storage, new StorageInfo("E:", "KINGSTON", 32UL << 30, 19_660UL << 20), StorageState.Known)),
            new UsbCard(new UsbState("u2", "HD Pro Webcam C920", true, UsbKind.Webcam, null, StorageState.None)),
            new UsbCard(new UsbState("u3", "Samsung T7", true, UsbKind.Storage, new StorageInfo("F:", "Sauvegardes", 1000UL << 30, 38UL << 30), StorageState.Known)),
            new PowerCard(new PowerState("Laptop-PC", true, 76, true, null)),
            new PowerCard(new PowerState("Laptop-PC", false, 18, false, 2 * 3600 + 20 * 60)),
            new PowerCard(new PowerState("Laptop-PC", true, 80, true, null, PowerReason.LevelReached)),
            new DisplayCard(new DisplayInfo("p", "", "DELL U2720Q", "HDMI", 2560, 1440, 60, false, 0, 0), connected: true),
            new JackCard(new JackState("", "Realtek(R) Audio", true, true, true, 2)),
            new HoverCard(
            [
                new MediaRow(media, (_, _) => { }),
                new DeviceRow(new DeviceState("a", "Galaxy Buds2 Pro", true, DeviceKind.Headphones, 85, BatteryState.Known), null),
                new DeviceRow(new DeviceState("b", "MX Master 3S", false, DeviceKind.Mouse, 56, BatteryState.Known, DeviceLink.Dongle), null),
            ], () => { }),
        ];
        var themes = new (Theme Theme, string Name, uint Backdrop)[]
        {
            (Theme.CreateLight(0xFF005FB8), "clair", 0xD9E3EE),
            (Theme.CreateDark(0xFF60CDFF), "sombre", 0x1B2633),
        };
        foreach (var (theme, name, backdrop) in themes)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                int m = PopupRenderer.ShadowMargin(scale);
                int w = (int)MathF.Round(cards[i].Width * scale), h = (int)MathF.Round(cards[i].Height * scale);
                using var canvas = new Canvas(w + 2 * m, h + 2 * m);
                var card = new RectF(m, m, w, h);
                PopupRenderer.DrawCard(canvas, card, theme, scale, 100);
                cards[i].Begin(0, new AnimClock(false, 1));
                cards[i].Draw(new CardContext(canvas, card, theme, scale, Stopwatch.GetTimestamp(), new AnimClock(false, 1), i == 0 ? 2 : i == 1 ? 1400 : -1, settings));
                if (!transparent) canvas.FlattenOnto(backdrop);
                canvas.SavePng(Path.Combine(directory, $"{name}-{i + 1}.png"));
            }
        }

        // Pellicule du morphing lecture → pause (triangle → deux barres), 7 étapes, bouton de 38 DIP.
        int d = (int)MathF.Round(38 * scale * 2), step = d + 16;
        using var strip = new Canvas(7 * step, d + 16);
        strip.Fill(0xFF202020);
        for (int k = 0; k < 7; k++)
        {
            var circle = new RectF(8 + k * step, 8, d, d);
            strip.FillEllipse(circle, 0xFF60CDFF);
            MediaCard.DrawPlayPause(strip, circle, k / 6f, 0xFF000000);
        }
        strip.SavePng(Path.Combine(directory, "lecture-pause.png"));

        // Pellicule du changement de titre (carte remplacée sur place) : 0, 80, 160, 240, 320 et 420 ms.
        {
            var dark = Theme.CreateDark(0xFF60CDFF);
            var oldCard = new MediaCard(new MediaState("a", "Spotify", "Bohemian Rhapsody", "Queen", true, true, true, true, null), (_, _) => { });
            var newCard = new MediaCard(new MediaState("a", "Spotify", "Under Pressure", "Queen & David Bowie", true, true, true, true, SampleCover()), (_, _) => { });
            var clock = new AnimClock(true, 1);
            long t0 = Stopwatch.GetTimestamp();
            newCard.ContinueFrom(oldCard, t0, clock, replay: true);
            int[] times = [0, 80, 160, 240, 320, 420];
            int m = PopupRenderer.ShadowMargin(scale), w = (int)MathF.Round(newCard.Width * scale), h = (int)MathF.Round(newCard.Height * scale);
            using var film = new Canvas(w + 2 * m, times.Length * (h + m) + m);
            film.Fill(0xFF1B2633);
            for (int f = 0; f < times.Length; f++)
            {
                var card = new RectF(m, m + f * (h + m), w, h);
                film.FillRoundRect(card, PopupRenderer.CornerRadius * scale, dark.Background);
                newCard.Draw(new CardContext(film, card, dark, scale, t0 + clock.Ticks(times[f]), clock, -1, settings));
            }
            film.SavePng(Path.Combine(directory, "transition-titre.png"));
        }

        // Couleurs de la pochette : une pochette par teinte (et une en noir et blanc), en clair et en sombre.
        {
            uint[] tones = [0xFFD8402F, 0xFFE8B030, 0xFF3FA65A, 0xFF2F6FD8, 0xFF9C3FD0, 0xFF808080];
            int m = PopupRenderer.ShadowMargin(scale);
            var sample = new MediaCard(media, (_, _) => { });
            int w = (int)MathF.Round(sample.Width * scale), h = (int)MathF.Round(sample.Height * scale);
            using var sheet = new Canvas(2 * (w + m) + m, tones.Length * (h + m) + m);
            sheet.Fill(0xFF808890);
            for (int col = 0; col < 2; col++)
            {
                var theme = themes[col].Theme;
                for (int row = 0; row < tones.Length; row++)
                {
                    var card = new RectF(m + col * (w + m), m + row * (h + m), w, h);
                    sheet.FillRoundRect(card, PopupRenderer.CornerRadius * scale, theme.Background);
                    var tinted = new MediaCard(media with { Cover = SampleCover(tones[row]) }, (_, _) => { });
                    tinted.Draw(new CardContext(sheet, card, theme, scale, Stopwatch.GetTimestamp(), new AnimClock(false, 1), -1, settings));
                }
            }
            sheet.SavePng(Path.Combine(directory, "couleurs-pochette.png"));
        }

        // Page GitHub : à gauche les couleurs de Windows, à droite celles de la pochette (même carte, thème sombre).
        {
            (string Title, string Artist, uint Top, uint Bottom, uint Disc)[] tracks =
            [
                ("Neon Skyline", "Aurora Drive", 0xFF24104F, 0xFF5B2A86, 0xFFFF6EC7),
                ("Golden Hour", "The Lanterns", 0xFF8A4B08, 0xFFF2A93B, 0xFFFFE8A3),
                ("Deep Blue", "Coastline", 0xFF0B3D5C, 0xFF1E88A8, 0xFF9BE7F0),
            ];
            var dark = themes[1].Theme;
            var classic = new Settings { MediaCoverColors = false };
            int m = PopupRenderer.ShadowMargin(scale);
            var probe = new MediaCard(media with { Timeline = new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTime.UtcNow) }, (_, _) => { });
            int w = (int)MathF.Round(probe.Width * scale), h = (int)MathF.Round(probe.Height * scale);
            using var board = new Canvas(2 * (w + m) + m, tracks.Length * (h + m) + m);
            if (!transparent) board.Fill(0xFF1B2633);
            for (int row = 0; row < tracks.Length; row++)
            {
                var (title, artist, top, bottom, disc) = tracks[row];
                var state = media with
                {
                    Title = title, Artist = artist, Cover = SampleArt(top, bottom, disc), CanSeek = true,
                    Timeline = new MediaTimeline(TimeSpan.FromSeconds(48 + 37 * row), TimeSpan.FromSeconds(214), DateTime.UtcNow),
                };
                for (int col = 0; col < 2; col++)
                {
                    var card = new RectF(m + col * (w + m), m + row * (h + m), w, h);
                    board.FillRoundRect(card, PopupRenderer.CornerRadius * scale, dark.Background);
                    new MediaCard(state, (_, _) => { }).Draw(new CardContext(board, card, dark, scale, Stopwatch.GetTimestamp(),
                        new AnimClock(false, 1), -1, col == 0 ? classic : settings));
                }
            }
            board.SavePng(Path.Combine(directory, "album-colors.png"));
        }

        // Caféine : cartes (survol, avertissement, extinction) en clair et en sombre, puis tasses de la zone de notification.
        {
            (Modules.Caffeine.CaffeineSnapshot State, Modules.Caffeine.CaffeineCardKind Kind)[] states =
            [
                (new(Modules.Caffeine.CaffeineMode.Indefinite, null, 1, true), Modules.Caffeine.CaffeineCardKind.Status),
                (new(Modules.Caffeine.CaffeineMode.Timed, TimeSpan.FromMinutes(83), 0.55f, true), Modules.Caffeine.CaffeineCardKind.Status),
                (new(Modules.Caffeine.CaffeineMode.Shutdown, TimeSpan.FromMinutes(4.5), 0.08f, true), Modules.Caffeine.CaffeineCardKind.Warning),
                (new(Modules.Caffeine.CaffeineMode.Off, null, 1, true), Modules.Caffeine.CaffeineCardKind.Status),
            ];
            int m = PopupRenderer.ShadowMargin(scale);
            int w = (int)MathF.Round(360 * scale), h = (int)MathF.Round(96 * scale);
            using var sheet = new Canvas(2 * (w + m) + m, states.Length * (h + m) + m + (int)(80 * scale));
            sheet.Fill(0xFF808890);
            for (int col = 0; col < 2; col++)
            for (int row = 0; row < states.Length; row++)
            {
                var theme = themes[col].Theme;
                var card = new RectF(m + col * (w + m), m + row * (h + m), w, h);
                sheet.FillRoundRect(card, PopupRenderer.CornerRadius * scale, theme.Background);
                var (state, kind) = states[row];
                new Modules.Caffeine.CaffeineCard(() => state, kind).Draw(new CardContext(sheet, card, theme, scale,
                    Stopwatch.GetTimestamp(), new AnimClock(false, 1), -1, settings));
            }
            // Tasses : 16, 20, 24, 32 px × (désactivé, pleine, moitié, vide, extinction, fumée), agrandies ×2, fond sombre.
            int y0 = states.Length * (h + m) + m, x = m;
            foreach (int size in new[] { 16, 20, 24, 32 })
            {
                foreach (var (level, fill, steam) in new[] { (-1f, 0xFF60CDFFu, -1f), (1f, 0xFF60CDFFu, -1f), (0.5f, 0xFF60CDFFu, -1f), (0f, 0xFF60CDFFu, -1f), (1f, 0xFFFF9A3Cu, -1f), (1f, 0xFF60CDFFu, 0.45f) })
                {
                    sheet.FillRoundRect(new RectF(x - 2, y0 - 2, size * 2 + 4, size * 2 + 4), 3, 0xFF202020);
                    Modules.Caffeine.CaffeineIcon.Draw(sheet, new RectF(x, y0, size * 2, size * 2), level, fill, 0xFFFFFFFF, steam);
                    x += size * 2 + 10;
                }
                x += 16;
            }
            sheet.SavePng(Path.Combine(directory, "cafeine.png"));
        }

        // Transferts de fichiers : en cours (bouton pause survolé), en pause, conflit de noms, terminé, deux opérations.
        {
            var noop = new Modules.Transfers.TransferActions(_ => { }, _ => { }, _ => { }, (_, _) => { });
            var two = Modules.Transfers.TransfersModule.Sample(percent: 18);
            var second = Modules.Transfers.TransfersModule.Sample(percent: 81)[0];
            two.Add(second with
            {
                Tile = second.Tile with { Key = "b", Operation = L.English ? "Moving 1 item" : "Déplacement de 1 élément", Kind = Modules.Transfers.TransferKind.Move, Target = "Documents", ItemName = "Rapport annuel.pdf" },
            });
            (List<Modules.Transfers.TransferView> Views, int Hover)[] states =
            [
                (Modules.Transfers.TransfersModule.Sample(), 1),
                (Modules.Transfers.TransfersModule.Sample(Modules.Transfers.TransferPhase.Paused, 47), -1),
                (Modules.Transfers.TransfersModule.Sample(percent: 26, conflict: true), 4),
                (Modules.Transfers.TransfersModule.Sample(Modules.Transfers.TransferPhase.Done), -1),
                (two, -1),
            ];
            int m = PopupRenderer.ShadowMargin(scale);
            int w = (int)MathF.Round(new Modules.Transfers.TransferCard(two, noop, true).Width * scale);
            var heights = states.Select(s => (int)MathF.Round(new Modules.Transfers.TransferCard(s.Views, noop, true).Height * scale)).ToArray();
            using var sheet = new Canvas(2 * (w + m) + m, heights.Sum() + (states.Length + 1) * m);
            sheet.Fill(0xFF808890);
            for (int col = 0; col < 2; col++)
            {
                int y = m;
                for (int row = 0; row < states.Length; row++)
                {
                    var theme = themes[col].Theme;
                    var card = new RectF(m + col * (w + m), y, w, heights[row]);
                    sheet.FillRoundRect(card, PopupRenderer.CornerRadius * scale, theme.Background);
                    new Modules.Transfers.TransferCard(states[row].Views, noop, true).Draw(new CardContext(sheet, card, theme, scale,
                        Stopwatch.GetTimestamp(), new AnimClock(false, 1), states[row].Hover, settings));
                    y += heights[row] + m;
                }
            }
            sheet.SavePng(Path.Combine(directory, "transferts.png"));
        }
    }

    /// <summary>Pochette de démonstration « illustrée » : dégradé vertical et disque (soleil) d'une troisième couleur.</summary>
    private static Cover SampleArt(uint top, uint bottom, uint disc)
    {
        const int size = 192;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float k = y / (float)(size - 1);
            float dx = x - size * 0.62f, dy = y - size * 0.42f;
            bool sun = dx * dx + dy * dy < size * size * 0.06f;
            uint c = sun ? disc : 0;
            int i = (y * size + x) * 4;
            for (int ch = 0; ch < 3; ch++)
            {
                int shift = ch * 8;
                float a = (top >> shift) & 0xFF, b = (bottom >> shift) & 0xFF;
                pixels[i + ch] = sun ? (byte)((c >> shift) & 0xFF) : (byte)(a + (b - a) * k);
            }
            pixels[i + 3] = 255;
        }
        return new Cover(pixels, size);
    }

    /// <summary>Pochette de démonstration d'une teinte : dégradé vers le noir, avec un coin clair.</summary>
    private static Cover SampleCover(uint tone)
    {
        const int size = 96;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            float shade = 0.35f + 0.65f * (1 - y / (float)size), light = x + y < 30 ? 1 : 0;
            pixels[i] = (byte)Math.Min(255, (tone & 0xFF) * shade + 200 * light);
            pixels[i + 1] = (byte)Math.Min(255, (tone >> 8 & 0xFF) * shade + 200 * light);
            pixels[i + 2] = (byte)Math.Min(255, (tone >> 16 & 0xFF) * shade + 200 * light);
            pixels[i + 3] = 255;
        }
        return new Cover(pixels, size);
    }

    /// <summary>Pochette de démonstration : dégradé diagonal.</summary>
    private static Cover SampleCover()
    {
        const int size = 96;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            pixels[i] = (byte)(120 + x);          // B
            pixels[i + 1] = (byte)(40 + y);       // G
            pixels[i + 2] = (byte)(200 - x / 2);  // R
            pixels[i + 3] = 255;
        }
        return new Cover(pixels, size);
    }
}
