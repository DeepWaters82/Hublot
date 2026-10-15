using System.Diagnostics;
using Hublot.Common;
using Hublot.Interop;
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
    public static void Render(string directory, float scale = 1.5f)
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
                canvas.FlattenOnto(backdrop);
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
