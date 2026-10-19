using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Audio;

/// <summary>
/// Module « Prise audio » : popup quand on branche ou débranche un casque, des écouteurs ou un micro sur une
/// prise jack. Purement événementiel (abonnement MMDevice, aucun sondage) ; Bluetooth et USB sont laissés à
/// leurs modules.
///
/// Deux comportements de pilote :
/// - la plupart décrivent leurs prises (IKsJackDescription : couleur, emplacement, branchée) : à chaque signal
///   d'un point de terminaison filaire, et une fois le pilote calmé (<see cref="SettleMs"/>), l'état de chaque
///   prise est relu et comparé au précédent ;
/// - les autres : seule la transition « débranché » ↔ « actif » du point de terminaison compte.
///
/// Realtek à prises réaffectables (PC fixes) : brancher en façade redémarre tout l'audio (points de
/// terminaison absents puis actifs) et la sortie ne décrit plus que la prise avant ; débrancher, et elle redécrit
/// la prise arrière, ou disparaît jusqu'au prochain branchement s'il n'y a rien à l'arrière. Une prise qui disparaît des descriptions compte comme débranchée, sauf si une autre vient
/// d'être branchée ; une prise qui réapparaît dans son état d'avant (les enceintes) n'est pas un événement.
/// </summary>
internal sealed class JackModule : Module
{
    /// <summary>
    /// Calme après le dernier signal : un redémarrage du pilote dure jusqu'à 4 s, avec des pauses de 2,3 s (mesuré).
    /// Pas d'attente de retour des points de terminaison : débrancher le dernier casque les retire jusqu'au prochain
    /// branchement (Realtek sans rien à l'arrière).
    /// </summary>
    private const int SettleMs = 3000;
    private const long ResumeGraceMs = 15_000;

    private enum Seen { Connected, Disconnected, Vanished }

    /// <summary>Prise jack : sens, emplacement (1 = arrière, 2 = avant), adaptateur.</summary>
    private sealed record JackInfo(bool Render, int Location, string Adapter);

    private sealed class JackEntry(JackInfo info, Seen state, bool lastConnected)
    {
        public JackInfo Info = info;
        public Seen State = state;
        public bool LastConnected = lastConnected;
    }

    private readonly object _lock = new();
    /// <summary>Points de terminaison filaires (description), ou null s'ils sont Bluetooth, USB ou virtuels.</summary>
    private readonly Dictionary<string, CoreAudio.Endpoint?> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Points de terminaison dont le pilote décrit les prises.</summary>
    private readonly HashSet<string> _described = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JackEntry> _jacks = [];
    private Dictionary<string, uint> _states = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _settle;
    private bool _ready;
    private IHost? _host;
    private bool _suspended;
    private long _quietUntil;

    public override string Id => "audio";
    public override string Name => "Prise audio";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");

    public override bool IsEnabled(Settings settings) => settings.ModuleAudio;

    public override void Start(IHost host)
    {
        _host = host;
        // COM multithread : hors du thread UI. L'état initial des prises sert de référence, pas d'événement.
        _ = Task.Run(() =>
        {
            try
            {
                if (!CoreAudio.Subscribe(OnStateChanged))
                {
                    Log.Write("Prise audio : API audio indisponible");
                    return;
                }
                CoreAudio.PropertyChanged = (id, _, _) => OnSignal(id);
                lock (_lock)
                {
                    _states = CoreAudio.States();
                    Read(baseline: true);
                    _ready = true;
                }
                Log.Write($"Prise audio : {_states.Count} points de terminaison, {_jacks.Count} prises décrites");
            }
            catch (Exception ex)
            {
                Log.Error("Prise audio", ex);
            }
        });
    }

    public override void Stop()
    {
        CoreAudio.PropertyChanged = null;
        CoreAudio.Unsubscribe();
        lock (_lock)
        {
            _settle?.Dispose();
            _settle = null;
            _ready = false;
        }
        _host = null;
    }

    /// <summary>Veille : les prises se réannoncent parfois au réveil.</summary>
    public override void OnPowerChanged(bool resumed)
    {
        _suspended = !resumed;
        if (resumed) _quietUntil = Environment.TickCount64 + ResumeGraceMs;
    }

    /// <summary>Description mise en cache ; null pour un point de terminaison Bluetooth, USB ou virtuel (ou absent).</summary>
    private CoreAudio.Endpoint? Wired(string id)
    {
        if (_endpoints.TryGetValue(id, out var known)) return known;
        // Absent (redémarrage en cours) : pas de cache, on réessaiera.
        if (CoreAudio.Describe(id) is not { } e) return null;
        bool wired = !e.Bus.Contains("BTH", StringComparison.OrdinalIgnoreCase)
            && !e.Bus.Contains("USB", StringComparison.OrdinalIgnoreCase)
            && !e.Bus.Contains("SWD", StringComparison.OrdinalIgnoreCase);
        _endpoints[id] = wired ? e : null;
        return wired ? e : null;
    }

    /// <summary>Rappel du service audio (thread quelconque) : état d'un point de terminaison.</summary>
    private void OnStateChanged(string id, uint state)
    {
        uint previous;
        bool known, described;
        lock (_lock)
        {
            known = _states.TryGetValue(id, out previous);
            _states[id] = state;
            described = _described.Contains(id);
        }
        bool plugged = known && previous == CoreAudio.StateUnplugged && state == CoreAudio.StateActive;
        bool unplugged = known && previous == CoreAudio.StateActive && state == CoreAudio.StateUnplugged;
        if (described || (!plugged && !unplugged))
        {
            OnSignal(id);
            return;
        }
        // Pilote qui ne décrit pas ses prises : transition « débranché » ↔ « actif » du point de terminaison.
        _ = Task.Run(() =>
        {
            CoreAudio.Endpoint? endpoint;
            lock (_lock) endpoint = Wired(id);
            if (endpoint is { } e) Post([(new JackInfo(e.Render, 0, e.Adapter), e.Name)], plugged);
        });
    }

    /// <summary>Signal sur un point de terminaison : relecture des prises une fois le pilote calmé.</summary>
    private void OnSignal(string id)
    {
        lock (_lock)
        {
            if (!_ready || _host is null) return;
            if (_endpoints.TryGetValue(id, out var known) && known is null) return; // Bluetooth, USB, virtuel
            _settle ??= new Timer(_ => Settle());
            _settle.Change(SettleMs, Timeout.Infinite);
        }
    }

    private void Settle()
    {
        List<(JackInfo Info, string Name)> plugged, unplugged;
        lock (_lock)
        {
            if (!_ready || _host is null) return;
            _states = CoreAudio.States();
            (plugged, unplugged) = Read(baseline: false);
            Log.Write("Prise audio : relecture, " + string.Join(", ", _jacks.Select(j => $"{j.Key.Split('|').Last()} {(j.Key.Contains("|out|") ? "sortie" : "entrée")} {j.Value.State}")));
        }
        if (plugged.Count > 0) Post(plugged, plugged: true);
        else if (unplugged.Count > 0) Post(unplugged, plugged: false);
    }

    /// <summary>Lit les prises des points de terminaison filaires et met à jour leur état ; renvoie les changements.</summary>
    private (List<(JackInfo, string)> Plugged, List<(JackInfo, string)> Unplugged) Read(bool baseline)
    {
        var now = new Dictionary<string, (JackInfo Info, bool Connected)>();
        foreach (var (id, state) in _states)
        {
            if (state is not (CoreAudio.StateActive or CoreAudio.StateUnplugged) || Wired(id) is not { } e) continue;
            if (CoreAudio.Jacks(id) is not { } jacks) continue;
            _described.Add(id);
            foreach (var j in jacks)
            {
                // Prises analogiques seulement : 3,5 mm, 6,35 mm, autre analogique, combinée (pas HDMI, S/PDIF…).
                if (j.Connection is not (1 or 2 or 7 or 11)) continue;
                string key = $"{e.Bus}|{(e.Render ? "out" : "in")}|{j.Color:X6}|{j.Connection}|{j.Location}";
                bool connected = j.Connected || (now.TryGetValue(key, out var seen) && seen.Connected);
                now[key] = (new JackInfo(e.Render, j.Location, e.Adapter), connected);
            }
        }

        List<(JackInfo, string)> plugged = [], unplugged = [];
        List<JackEntry> vanished = [];
        bool restored = false;
        foreach (var (key, (info, connected)) in now)
        {
            if (!_jacks.TryGetValue(key, out var entry))
            {
                if (connected && !baseline) plugged.Add((info, ""));
                _jacks[key] = new JackEntry(info, connected ? Seen.Connected : Seen.Disconnected, connected);
                continue;
            }
            if (connected && entry.State != Seen.Connected)
            {
                // Réapparue dans son état d'avant (prise arrière après un débranchement en façade) : pas un événement.
                if (entry.State == Seen.Vanished && entry.LastConnected) restored = true;
                else plugged.Add((info, ""));
            }
            else if (!connected && entry.State == Seen.Connected)
            {
                unplugged.Add((info, ""));
            }
            entry.Info = info;
            entry.State = connected ? Seen.Connected : Seen.Disconnected;
            entry.LastConnected = connected;
        }
        foreach (var (key, entry) in _jacks)
        {
            if (now.ContainsKey(key) || entry.State == Seen.Vanished) continue;
            if (entry.State == Seen.Connected) vanished.Add(entry);
            entry.LastConnected = entry.State == Seen.Connected;
            entry.State = Seen.Vanished;
        }
        // Une prise branchée disparaît des descriptions : débranchée (et retenue comme telle : son retour sera un
        // branchement), sauf si une autre vient d'être branchée (le pilote a réaffecté la sortie à la nouvelle prise).
        if (plugged.Count == 0 && !baseline)
        {
            foreach (var entry in vanished)
            {
                unplugged.Add((entry.Info, ""));
                entry.State = Seen.Disconnected;
                entry.LastConnected = false;
            }
        }
        if (!baseline && (plugged.Count > 0 || unplugged.Count > 0 || restored))
            Log.Write($"Prise audio : {plugged.Count} branchée(s), {unplugged.Count} débranchée(s), réapparue(s) : {restored}");
        return (plugged, unplugged);
    }

    /// <summary>Une carte pour les prises changées ensemble : sortie de préférence, « avec micro » si une entrée suit.</summary>
    private void Post(List<(JackInfo Info, string Name)> changes, bool plugged)
    {
        bool output = changes.Any(c => c.Info.Render), microphone = changes.Any(c => !c.Info.Render);
        var (main, name) = output ? changes.First(c => c.Info.Render) : changes[0];
        var state = new JackState(name, main.Adapter, plugged, output, microphone, main.Location);
        _host?.Post(() => Show(state));
    }

    private void Show(JackState state)
    {
        if (_host is null) return;
        var s = Host.Settings;
        if (state.Plugged ? !s.AudioNotifyPlugged : !s.AudioNotifyUnplugged) return;
        if (_suspended || Environment.TickCount64 < _quietUntil)
        {
            Log.Write($"Popup prise audio ignoré (veille) : {state.Title}");
            return;
        }
        Log.Write($"Prise audio : {state.Title}, {state.StatusText}");
        Host.Notify(new JackCard(state));
    }

    public override IEnumerable<CardContent> PreviewCards() =>
    [
        new JackCard(new JackState("", "Realtek(R) Audio", true, true, true, 2)),
    ];
}
