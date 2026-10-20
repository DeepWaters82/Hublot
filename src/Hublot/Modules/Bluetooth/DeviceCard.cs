using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Bluetooth;

/// <summary>
/// Carte d'un appareil (dimensions en pixels effectifs, mises à l'échelle) :
///
///   ┌───────────────────────────────────────────┐
///   │ ┌──────┐  Nom de l'appareil     ▭  85 %   │
///   │ │  🎧  │  ● Connecté · Bluetooth          │
///   │ └──────┘  ━━━━━━━━━━━━━━━━━━━━━━━━──────  │
///   └───────────────────────────────────────────┘
///
/// Pas d'animation propre : la carte entre et sort comme les autres, tuile, pourcentage et jauge sont
/// affichés directement à leur état final.
/// </summary>
internal sealed class DeviceCard(DeviceState state, Action? onClick) : CardContent
{
    public DeviceState State { get; private set; } = state;

    public override string Key => State.Key;

    public override string? SettingsPage => "Notifications";

    public override void ContinueFrom(CardContent previous, long now, AnimClock clock, bool replay)
    {
        // Complément d'information (batterie, nom…) : garde le motif du popup.
        if (!replay && previous is DeviceCard old) State = State with { Reason = old.State.Reason };
    }

    public override bool Click(int part)
    {
        onClick?.Invoke();
        return true;
    }

    public override void Draw(in CardContext ctx)
    {
        var (c, t, card, p) = (ctx.Canvas, ctx.Theme, ctx.Card, State);
        float scale = ctx.Scale;
        float S(float v) => v * scale;

        // Tuile d'icône : accentuée si connecté (rouge si batterie faible), neutre sinon.
        var tile = new RectF(card.X + S(16), card.Y + S(16), S(48), S(48));
        if (p.Connected)
        {
            c.FillRoundRect(tile, S(6), p.Reason == NotifyReason.LowBattery ? t.Critical : t.Accent);
        }
        else
        {
            c.FillRoundRect(tile, S(6), t.SubtleFill);
            c.StrokeRoundRect(tile, S(6), t.SubtleStroke, Math.Max(1, MathF.Round(scale)));
        }
        using (var icon = new Font(S(22), Font.Icons))
            c.DrawIcon(DeviceGlyphs.For(p.Kind), icon, p.Connected ? t.OnAccent : t.TextSecondary, tile);

        using var title = new Font(S(14), Font.Title);
        using var caption = new Font(S(12), Font.Caption);
        float x = tile.Right + S(12), right = card.Right - S(16);
        // Avec la jauge, trois lignes occupent la hauteur de la tuile ; sans, deux lignes centrées.
        float top = tile.Y + (p.Battery is not null ? 0 : S(5));

        // Ligne 1 : nom + batterie à droite.
        float nameRight = right;
        if (p.Battery is int level)
        {
            float labelWidth = c.MeasureText(L.Percent(level), title);
            c.DrawText(L.Percent(level), title, p.Connected ? t.TextPrimary : t.TextSecondary, new RectF(x, top, right - x, S(20)), TextAlign.Far);
            using var batteryIcon = new Font(S(16), Font.Icons);
            float glyphRight = right - labelWidth - S(6);
            c.DrawText(Glyphs.Battery(level, p.Charging == true), batteryIcon, BatteryColor(level, p.Connected, t, text: true),
                new RectF(glyphRight - S(20), top, S(20), S(20)), TextAlign.Far);
            nameRight = glyphRight - S(20) - S(12);
        }
        c.DrawText(p.Name, title, t.TextPrimary, new RectF(x, top, nameRight - x, S(20)), TextAlign.Near, ellipsis: true);

        // Ligne 2 : pastille d'état + libellé.
        float statusY = top + S(22);
        uint dot = !p.Connected ? t.TextTertiary : p.Reason == NotifyReason.LowBattery ? t.Critical : t.Success;
        c.FillEllipse(new RectF(x, statusY + S(5), S(6), S(6)), dot);
        c.DrawText(StatusText(p, ctx.Settings.ShowLinkType), caption, t.TextSecondary,
            new RectF(x + S(12), statusY, right - x - S(12), S(16)), TextAlign.Near, ellipsis: true);

        // Ligne 3 : jauge de batterie (ProgressBar Fluent, extrémités arrondies).
        if (p.Battery is int value)
        {
            var track = new RectF(x, statusY + S(22), right - x, S(4));
            c.FillRoundRect(track, S(2), t.Track);
            if (value > 0)
            {
                float w = Math.Max(track.Height, track.Width * Math.Clamp(value, 0, 100) / 100f);
                c.FillRoundRect(new RectF(track.X, track.Y, w, track.Height), S(2), BatteryColor(value, p.Connected, t, text: false));
            }
        }
    }

    /// <summary>Deuxième ligne : motif de la notification, état, liaison.</summary>
    public static string StatusText(DeviceState p, bool showLink)
    {
        string status = p.Reason switch
        {
            NotifyReason.NewDevice => L.T("Device.NewConnected"),
            NotifyReason.LowBattery => L.T("Device.LowBattery"),
            NotifyReason.Charging => L.T("Device.Charging"),
            NotifyReason.ChargeReached => L.T("Device.ChargeReached"),
            _ => L.T(p.Connected ? "Device.Connected" : "Device.Disconnected"),
        };
        if (p.Connected && p.Reason == NotifyReason.None)
        {
            if (p.Charging == true) status += " · " + L.T("Device.Charging");
            // Manette USB (dongle 2,4 GHz, câble) : jamais de batterie communiquée, inutile de le dire.
            else if (p.BatteryState == BatteryState.Unavailable && p.Link != DeviceLink.Usb) status += " · " + L.T("Device.BatteryUnknown");
        }
        if (showLink && p.Key != "none")
            status += " · " + p.Link switch { DeviceLink.Dongle => L.T("Device.Dongle"), DeviceLink.Usb => "USB", _ => "Bluetooth" };
        return status;
    }

    internal static uint BatteryColor(int pct, bool connected, Theme t, bool text)
    {
        if (!connected) return t.TextTertiary;
        if (pct <= 10) return t.Critical;
        if (pct <= 20) return t.Caution;
        return text ? t.TextPrimary : t.Accent;
    }
}

/// <summary>Ligne compacte d'un appareil dans la carte du survol de l'icône.</summary>
internal sealed class DeviceRow(DeviceState p, Action? onClick) : HoverRow
{
    public override bool Click(int part)
    {
        onClick?.Invoke();
        return onClick is not null;
    }

    public override void Draw(in CardContext ctx, RectF row, float progress, int hover)
    {
        var (c, t) = (ctx.Canvas, ctx.Theme);
        float scale = ctx.Scale;
        float S(float v) => v * scale;
        using var title = new Font(S(14), Font.Title);
        using var caption = new Font(S(12), Font.Caption);
        using var icons = new Font(S(18), Font.Icons);
        using var small = new Font(S(14), Font.Icons);

        var tile = new RectF(row.X + S(8), row.Y + (row.Height - S(36)) / 2, S(36), S(36));
        if (p.Connected)
        {
            c.FillRoundRect(tile, S(6), t.Accent);
        }
        else
        {
            c.FillRoundRect(tile, S(6), t.SubtleFill);
            c.StrokeRoundRect(tile, S(6), t.SubtleStroke, Math.Max(1, MathF.Round(scale)));
        }
        c.DrawIcon(DeviceGlyphs.For(p.Kind), icons, p.Connected ? t.OnAccent : t.TextSecondary, tile);

        float x = tile.Right + S(12), right = row.Right - S(12);
        float top = row.Y + S(8);
        c.DrawText(p.Name, title, p.Connected ? t.TextPrimary : t.TextSecondary, new RectF(x, top, right - x, S(20)), TextAlign.Near, ellipsis: true);

        // Ligne 2 : état à gauche, batterie à droite.
        float y2 = top + S(21);
        float statusRight = right;
        if (p.Battery is int level)
        {
            float width = c.MeasureText(L.Percent(level), caption);
            uint color = !p.Connected ? t.TextTertiary : level <= 10 ? t.Critical : level <= 20 ? t.Caution : t.TextSecondary;
            c.DrawText(L.Percent(level), caption, p.Connected ? t.TextPrimary : t.TextTertiary, new RectF(x, y2, right - x, S(16)), TextAlign.Far);
            float glyphRight = right - width - S(5);
            c.DrawText(Glyphs.Battery(level, p.Charging == true), small, color, new RectF(glyphRight - S(18), y2, S(18), S(16)), TextAlign.Far);
            statusRight = glyphRight - S(18) - S(8);
        }
        c.FillEllipse(new RectF(x, y2 + S(5), S(6), S(6)), p.Connected ? t.Success : t.TextTertiary);
        string status = p.Key == "none" ? L.T("Message.Nothing") : p.Connected ? L.T(p.Charging == true ? "Device.ConnectedCharging" : "Device.Connected") : L.T("Device.Disconnected");
        c.DrawText(status, caption, t.TextSecondary, new RectF(x + S(12), y2, statusRight - x - S(12), S(16)), TextAlign.Near, ellipsis: true);
    }
}
