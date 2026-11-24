using System.Diagnostics;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Caffeine;

/// <summary>
/// Module « Caféine » : garde le PC éveillé (sans limite ou pendant une durée), ou l’éteint après une durée ; l’écran
/// reste allumé ou non (option). Sa propre tasse dans la zone de notification (<see cref="CaffeineTray"/>).
///
/// Éveil : SetThreadExecutionState sur le thread UI (ES_SYSTEM_REQUIRED, + ES_DISPLAY_REQUIRED si l’écran doit
/// rester allumé), comme PowerToys Caféine ; Windows l’annule si Hublot se ferme. Aucun sondage : la minuterie ne
/// se réveille qu’aux instants utiles (paliers de la tasse, avertissement, fin), et chaque seconde seulement tant
/// que la carte du survol est visible. Extinction : shutdown.exe, avec 60 s d’avertissement de Windows, annulable.
/// Rien n’est conservé d’une session à l’autre : au lancement, Caféine est désactivé.
/// </summary>
internal sealed class CaffeineModule : Module
{
    private const int ExtendMinutes = 30, ShutdownDelaySeconds = 60;
    private const long HoverLingerMs = 4000;

    private IHost? _host;
    private CaffeineTray? _tray;
    private Timer? _timer;
    private CaffeineMode _mode;
    private DateTime _start, _end;
    private bool _warned;
    private long _hoverUntil;
    private float _shownLevel = -2;

    public override string Id => "caffeine";
    public override string Name => "Caféine";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");

    public override bool IsEnabled(Settings settings) => settings.ModuleCaffeine;

    public override void Start(IHost host)
    {
        _host = host;
        _mode = CaffeineMode.Off;
        _timer = new Timer(_ => _host?.Post(Tick));
        var tray = new CaffeineTray(Snapshot);
        tray.ModeRequested += (mode, minutes) => SetMode(mode, minutes, "menu");
        tray.CustomDurationRequested += AskDuration;
        tray.KeepDisplayToggled += ToggleKeepDisplay;
        tray.SettingsRequested += () => _host?.OpenCore("Caffeine");
        tray.HotkeyPressed += OnHotkey;
        tray.HoverStarted += OnHoverStarted;
        tray.HoverEnded += OnHoverEnded;
        tray.ThemeChanged += () => _host?.Invalidate();
        tray.Add();
        _tray = tray;
        ApplyHotkeys();
        Log.Write("Caféine : prêt (désactivé)");
    }

    public override void Stop()
    {
        if (_mode != CaffeineMode.Off) Log.Write("Caféine : désactivé (module arrêté)");
        _mode = CaffeineMode.Off;
        Kernel32.SetThreadExecutionState(Kernel32.ES_CONTINUOUS);
        _tray?.Remove();
        _tray?.DestroyHandle(); // sinon la fenêtre Hublot.Caffeine survit et une réactivation en crée une seconde
        _tray = null;
        _timer?.Dispose();
        _timer = null;
        _host = null;
    }

    public override void ApplySettings(Settings settings)
    {
        if (_mode != CaffeineMode.Off) ApplyExecutionState();
        ApplyHotkeys();
        Reschedule();
        _tray?.Refresh();
    }

    public override IEnumerable<CardContent> PreviewCards() =>
    [
        new CaffeineCard(() => new CaffeineSnapshot(CaffeineMode.Timed, TimeSpan.FromMinutes(83), 0.55f, true), CaffeineCardKind.Status),
    ];

    // ── État ───────────────────────────────────────────────────────────────────────────────

    private CaffeineSnapshot Snapshot()
    {
        bool display = _host?.Settings.CaffeineKeepDisplay ?? true;
        if (_mode is not (CaffeineMode.Timed or CaffeineMode.Shutdown)) return new CaffeineSnapshot(_mode, null, 1, display);
        var remaining = _end - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        double total = (_end - _start).TotalSeconds;
        return new CaffeineSnapshot(_mode, remaining, total > 0 ? (float)(remaining.TotalSeconds / total) : 0, display);
    }

    /// <summary>Change de mode (minutes : durée pour « pendant » et « éteindre dans »).</summary>
    private void SetMode(CaffeineMode mode, int minutes, string origin)
    {
        if (_host is null) return;
        if (mode is CaffeineMode.Timed or CaffeineMode.Shutdown && minutes <= 0) return;
        _mode = mode;
        _start = DateTime.Now;
        _end = mode is CaffeineMode.Timed or CaffeineMode.Shutdown ? _start.AddMinutes(minutes) : default;
        _warned = false;
        ApplyExecutionState();
        Log.Write(mode switch
        {
            CaffeineMode.Indefinite => $"Caféine : actif sans limite ({origin})",
            CaffeineMode.Timed => $"Caféine : actif {minutes} min, jusqu’à {_end:HH:mm} ({origin})",
            CaffeineMode.Shutdown => $"Caféine : extinction programmée dans {minutes} min, à {_end:HH:mm} ({origin})",
            _ => $"Caféine : désactivé ({origin})",
        });
        _shownLevel = Snapshot().Level;
        if (mode != CaffeineMode.Off) _tray?.AnimateFill();
        else _tray?.Refresh();
        Reschedule();
        Host.Invalidate();
    }

    private void ApplyExecutionState()
    {
        uint flags = Kernel32.ES_CONTINUOUS;
        if (_mode != CaffeineMode.Off)
        {
            flags |= Kernel32.ES_SYSTEM_REQUIRED;
            if (Host.Settings.CaffeineKeepDisplay) flags |= Kernel32.ES_DISPLAY_REQUIRED;
        }
        uint previous = Kernel32.SetThreadExecutionState(flags);
        if (previous == 0) Log.Write($"Caféine : état d’éveil refusé par Windows (0x{flags:X8})");
        else Log.Write($"Caféine : état d’éveil 0x{flags:X8} (écran {((flags & Kernel32.ES_DISPLAY_REQUIRED) != 0 ? "allumé" : "libre de s’éteindre")})");
    }

    private void ToggleKeepDisplay()
    {
        var settings = Host.Settings;
        settings.CaffeineKeepDisplay = !settings.CaffeineKeepDisplay;
        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Caféine : enregistrement de l’option écran", ex);
        }
        if (_mode != CaffeineMode.Off) ApplyExecutionState();
        Host.Invalidate();
    }

    private void ApplyHotkeys()
    {
        if (_tray is null || _host is null) return;
        var s = Host.Settings;
        Register(CaffeineTray.HotkeyIndefinite, s.CaffeineHotkeyIndefiniteEnabled, s.CaffeineHotkeyIndefinite);
        Register(CaffeineTray.HotkeyTimed, s.CaffeineHotkeyTimedEnabled, s.CaffeineHotkeyTimed);
        Register(CaffeineTray.HotkeyShutdown, s.CaffeineHotkeyShutdownEnabled, s.CaffeineHotkeyShutdown);
        Register(CaffeineTray.HotkeyOff, s.CaffeineHotkeyOffEnabled, s.CaffeineHotkeyOff);

        void Register(int id, bool enabled, int packed)
        {
            if (!_tray.SetHotkey(id, enabled, packed))
                Log.Write($"Caféine : raccourci {id} indisponible (déjà utilisé ?) : modificateurs {packed >> 16}, touche 0x{packed & 0xFFFF:X2}");
        }
    }

    private void OnHotkey(int id)
    {
        int minutes = Host.Settings.CaffeineDefaultMinutes;
        switch (id)
        {
            case CaffeineTray.HotkeyIndefinite:
                SetMode(_mode == CaffeineMode.Indefinite ? CaffeineMode.Off : CaffeineMode.Indefinite, 0, "raccourci");
                break;
            case CaffeineTray.HotkeyTimed: SetMode(CaffeineMode.Timed, minutes, "raccourci"); break;
            case CaffeineTray.HotkeyShutdown: SetMode(CaffeineMode.Shutdown, minutes, "raccourci"); break;
            case CaffeineTray.HotkeyOff: SetMode(CaffeineMode.Off, 0, "raccourci"); break;
        }
        // Le raccourci n’affiche rien d’autre : la carte confirme le nouveau mode.
        Host.Notify(new CaffeineCard(Snapshot, CaffeineCardKind.Status));
    }

    /// <summary>Durée personnalisée : petite fenêtre de Hublot Core, qui renvoie les minutes (<see cref="Settings.CaffeineMessage"/>).</summary>
    private static void AskDuration(CaffeineMode mode)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HublotCore.exe"),
                mode == CaffeineMode.Shutdown ? "--caffeine shutdown" : "--caffeine timed")
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Caféine : durée personnalisée", ex);
        }
    }

    // ── Survol de la tasse ─────────────────────────────────────────────────────────────────

    private bool Hovering => Environment.TickCount64 < _hoverUntil;

    private void OnHoverStarted()
    {
        if (_tray?.GetIconRect() is not RECT icon) return;
        _hoverUntil = long.MaxValue;
        Host.ShowHover(new CaffeineCard(Snapshot, CaffeineCardKind.Status), icon);
        Reschedule();
    }

    private void OnHoverEnded()
    {
        // La carte peut rester ouverte tant que la souris est dessus : quelques secondes de plus.
        _hoverUntil = Environment.TickCount64 + HoverLingerMs;
        _host?.EndHover();
    }

    // ── Minuterie : paliers, avertissement, fin ────────────────────────────────────────────

    private DateTime WarnAt => _end.AddMinutes(-Host.Settings.CaffeineWarningMinutes);

    /// <summary>Prochain réveil : le plus proche des instants utiles ; chaque seconde tant que la carte du survol est visible.</summary>
    private void Reschedule()
    {
        if (_timer is null || _host is null) return;
        var now = DateTime.Now;
        var next = DateTime.MaxValue;
        if (_mode is CaffeineMode.Timed or CaffeineMode.Shutdown)
        {
            var third = TimeSpan.FromTicks((_end - _start).Ticks / 3);
            foreach (var at in new[] { _end, _start + third, _start + third + third, _warned ? DateTime.MaxValue : WarnAt })
                if (at > now && at < next) next = at;
            if (next == DateTime.MaxValue) next = now; // fin déjà passée (sortie de veille) : tout de suite
        }
        if (Hovering && next > now.AddSeconds(1)) next = now.AddSeconds(1);
        if (next == DateTime.MaxValue)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }
        double ms = Math.Clamp((next - now).TotalMilliseconds + 20, 20, int.MaxValue - 1);
        _timer.Change((long)ms, Timeout.Infinite);
    }

    private void Tick()
    {
        if (_host is null) return;
        if (_mode is CaffeineMode.Timed or CaffeineMode.Shutdown)
        {
            var now = DateTime.Now;
            if (now >= _end)
            {
                Expire();
                return;
            }
            if (!_warned && now >= WarnAt && _end - _start > TimeSpan.FromMinutes(Host.Settings.CaffeineWarningMinutes))
            {
                _warned = true;
                Warn();
            }
            float level = Snapshot().Level;
            if (level != _shownLevel)
            {
                _shownLevel = level;
                _tray?.Refresh();
                Log.Write($"Caféine : tasse à {level:P0}");
            }
        }
        if (Hovering) Host.Invalidate();
        Reschedule();
    }

    /// <summary>Fin proche : popup (clic : prolonger de 30 min, ou annuler l’extinction).</summary>
    private void Warn()
    {
        bool shutdown = _mode == CaffeineMode.Shutdown;
        Log.Write(shutdown ? "Caféine : avertissement, extinction proche" : "Caféine : avertissement, fin proche (veille possible ensuite)");
        Host.Notify(new CaffeineCard(Snapshot, CaffeineCardKind.Warning, shutdown
            ? () => SetMode(CaffeineMode.Off, 0, "extinction annulée depuis l’avertissement")
            : Extend));
    }

    private void Extend()
    {
        if (_mode != CaffeineMode.Timed) return;
        float before = Snapshot().Level;
        _end = _end.AddMinutes(ExtendMinutes);
        _warned = false;
        Log.Write($"Caféine : prolongé de {ExtendMinutes} min, jusqu’à {_end:HH:mm}");
        _shownLevel = Snapshot().Level;
        _tray?.AnimateFill(before); // la tasse se remplit de nouveau
        Reschedule();
    }

    private void Expire()
    {
        if (_mode == CaffeineMode.Shutdown)
        {
            SetMode(CaffeineMode.Off, 0, "durée écoulée, extinction");
            StartShutdown();
        }
        else
        {
            SetMode(CaffeineMode.Off, 0, "durée écoulée");
        }
    }

    /// <summary>Extinction par shutdown.exe : Windows prévient aussi pendant 60 s ; la carte permet d’annuler.</summary>
    private void StartShutdown()
    {
        if (!Shutdown($"/s /t {ShutdownDelaySeconds} /d p:0:0 /c \"{L.T("Caf.ShutdownComment")}\"")) return;
        Host.Notify(new CaffeineCard(() => new CaffeineSnapshot(CaffeineMode.Shutdown, null, 0, true),
            CaffeineCardKind.ShuttingDown, () => Shutdown("/a")));
    }

    private static bool Shutdown(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Log.Write(arguments == "/a" ? "Caféine : extinction annulée" : $"Caféine : extinction lancée dans {ShutdownDelaySeconds} s");
            return process is not null;
        }
        catch (Exception ex)
        {
            Log.Error("Caféine : shutdown.exe", ex);
            return false;
        }
    }
}
