using System.Globalization;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Caffeine;

internal enum CaffeineMode { Off, Indefinite, Timed, Shutdown }

/// <summary>Carte d’état (survol de la tasse), d’avertissement (fin proche) ou d’extinction lancée.</summary>
internal enum CaffeineCardKind { Status, Warning, ShuttingDown }

/// <param name="Fraction">Temps restant / durée totale (1 = début, 0 = fin) ; 1 sans limite.</param>
internal readonly record struct CaffeineSnapshot(CaffeineMode Mode, TimeSpan? Remaining, float Fraction, bool KeepDisplay)
{
    /// <summary>Remplissage de la tasse : pleine, moitié, vide (trois paliers, comme l’icône).</summary>
    public float Level => Mode switch
    {
        CaffeineMode.Off => -1,
        CaffeineMode.Indefinite => 1,
        _ => Fraction > 2f / 3 ? 1 : Fraction > 1f / 3 ? 0.5f : 0,
    };
}

/// <summary>
/// Carte de Caféine (pixels effectifs, mis à l’échelle) :
///
///   ┌──────────────────────────────────────────┐
///   │  ╭───╮   Caféine · actif                │   anneau = temps restant, à la couleur d’accentuation
///   │  │ ☕ │   1 h 23 min                     │   (orange pour une extinction programmée)
///   │  ╰───╯   avant que le PC puisse dormir   │
///   └──────────────────────────────────────────┘
///
/// L’anneau se remplit à l’ouverture (700 ms, decelerate) et la fumée monte au-dessus de la tasse (1,6 s). Le
/// temps restant est relu à chaque image : le module redessine la carte chaque seconde tant qu’elle est visible.
/// </summary>
internal sealed class CaffeineCard(Func<CaffeineSnapshot> state, CaffeineCardKind kind, Action? onClick = null) : CardContent
{
    private const double SweepMs = 700, SteamMs = 1600;
    private long _start;
    private bool _started;

    public override string Key => kind == CaffeineCardKind.Status ? "caffeine" : "caffeine-alert";
    public override float Height => 96;
    public override string LogName => kind switch
    {
        CaffeineCardKind.Warning => "Caféine (avertissement)",
        CaffeineCardKind.ShuttingDown => "Caféine (extinction)",
        _ => "Caféine",
    };
    public override string? SettingsPage => "Caffeine";

    /// <summary>Avertissements : aussi par-dessus le plein écran (on doit savoir que le PC va s’éteindre).</summary>
    public override bool ShowOverFullscreen(Settings settings) => kind != CaffeineCardKind.Status;

    public override void Begin(long start, AnimClock clock) => (_start, _started) = (start, true);

    public override bool IsAnimating(long now, AnimClock clock)
        => clock.Animate && _started && clock.Elapsed(_start, now) < SteamMs;

    public override bool Click(int part)
    {
        onClick?.Invoke();
        return true;
    }

    public override void Draw(in CardContext ctx)
    {
        var (c, t, card) = (ctx.Canvas, ctx.Theme, ctx.Card);
        var s = state();
        bool shutdown = s.Mode == CaffeineMode.Shutdown || kind == CaffeineCardKind.ShuttingDown;
        uint accent = shutdown ? CaffeineIcon.ShutdownColor(!t.Dark) : t.Accent;
        double elapsed = _started && ctx.Clock.Animate ? ctx.Clock.Elapsed(_start, ctx.Now) : double.MaxValue;

        // Anneau : piste, puis arc du temps restant (depuis midi, dans le sens des aiguilles).
        float cx = card.X + ctx.S(16 + 28), cy = card.Y + card.Height / 2, r = ctx.S(25), width = ctx.S(4);
        Span<PointF> ring = stackalloc PointF[65];
        for (int i = 0; i < ring.Length; i++)
        {
            double a = -Math.PI / 2 + 2 * Math.PI * i / (ring.Length - 1);
            ring[i] = new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
        }
        c.StrokePolyline(ring, width, t.Track);
        float fraction = s.Mode switch { CaffeineMode.Off => 0, CaffeineMode.Indefinite => 1, _ => Math.Clamp(s.Fraction, 0, 1) };
        fraction *= (float)Easing.Decelerate(Easing.Clamp01(elapsed / SweepMs));
        int points = (int)Math.Round(fraction * (ring.Length - 1));
        if (points >= 1) c.StrokePolyline(ring[..(points + 1)], width, accent);

        // Tasse au centre, fumée à l’ouverture.
        float steam = s.Mode != CaffeineMode.Off && elapsed < SteamMs ? (float)(elapsed / SteamMs) : -1;
        float cup = ctx.S(28); // la tasse occupe tout son carré (3.9.0) : un peu plus petit pour rester dans l’anneau
        CaffeineIcon.Draw(c, new RectF(cx - cup / 2, cy - cup / 2 - ctx.S(1), cup, cup), s.Level, accent, t.TextPrimary, steam);

        // Textes.
        var (title, value, hint) = Texts(s);
        float x = cx + r + ctx.S(18), right = card.Right - ctx.S(16);
        using var small = new Font(ctx.S(12), Font.Caption);
        using var big = new Font(ctx.S(20), Font.Title);
        c.DrawText(title, small, t.TextSecondary, new RectF(x, card.Y + ctx.S(14), right - x, ctx.S(16)), TextAlign.Near, ellipsis: true);
        c.DrawText(value, big, t.TextPrimary, new RectF(x, card.Y + ctx.S(32), right - x, ctx.S(28)), TextAlign.Near, ellipsis: true);
        c.DrawText(hint, small, t.TextSecondary, new RectF(x, card.Y + ctx.S(64), right - x, ctx.S(16)), TextAlign.Near, ellipsis: true);
    }

    private (string Title, string Value, string Hint) Texts(CaffeineSnapshot s)
    {
        string remaining = s.Remaining is { } r ? Format(r) : "";
        return kind switch
        {
            CaffeineCardKind.ShuttingDown => (L.T("Caf.ShuttingDown"), L.T("Caf.InOneMinute"), L.T("Caf.ClickCancel")),
            CaffeineCardKind.Warning when s.Mode == CaffeineMode.Shutdown => (L.T("Caf.WarnShutdown"), remaining, L.T("Caf.ClickCancelShutdown")),
            CaffeineCardKind.Warning => (L.T("Caf.WarnEnd"), remaining, L.T("Caf.ClickExtend")),
            _ => s.Mode switch
            {
                CaffeineMode.Indefinite => (L.T("Caf.Active"), L.T("Caf.NoLimit"), L.T(s.KeepDisplay ? "Caf.KeepsDisplay" : "Caf.KeepsSystem")),
                CaffeineMode.Timed => (L.T("Caf.Active"), remaining, L.T("Caf.BeforeSleep")),
                CaffeineMode.Shutdown => (L.T("Caf.ShutdownPlanned"), remaining, L.T("Caf.BeforeShutdown")),
                _ => (L.T("Caf.Name"), L.T("Caf.Off"), L.T("Caf.OffHint")),
            },
        };
    }

    /// <summary>« 1 h 05 min », « 23 min », « 4 min 32 s », « 45 s ».</summary>
    public static string Format(TimeSpan r)
    {
        var ci = CultureInfo.InvariantCulture;
        if (r < TimeSpan.Zero) r = TimeSpan.Zero;
        if (r.TotalHours >= 1) return string.Format(ci, "{0} h {1:00} min", (int)r.TotalHours, r.Minutes);
        if (r.TotalMinutes >= 10) return string.Format(ci, "{0} min", (int)Math.Ceiling(r.TotalMinutes - 1e-9));
        if (r.TotalMinutes >= 1) return string.Format(ci, "{0} min {1:00} s", (int)r.TotalMinutes, r.Seconds);
        return string.Format(ci, "{0} s", (int)Math.Ceiling(r.TotalSeconds));
    }
}
