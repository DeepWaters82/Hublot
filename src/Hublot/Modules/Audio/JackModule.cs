using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Audio;

/// <summary>
/// Module « Prise audio » : popup quand on branche ou débranche un casque, des écouteurs ou un micro sur une
/// prise jack. Windows le signale par l'état du point de terminaison audio, « débranché » puis « actif » et
/// inversement (détection de prise du pilote : Realtek…). Les appareils Bluetooth et USB, qui passent aussi par
/// ces états, sont laissés à leurs modules. Purement événementiel : abonnement MMDevice, aucun sondage.
///
/// Un casque à micro fait changer deux points de terminaison (sortie et entrée) : regroupés sur
/// <see cref="GroupMs"/> en une seule carte. Limite : sur certains PC, un seul point de terminaison
/// « haut-parleurs / casque » ne change jamais d'état ; Windows ne signale alors rien.
/// </summary>
internal sealed class JackModule : Module
{
    private const int GroupMs = 700;
    private const long ResumeGraceMs = 15_000;

    private readonly object _lock = new();
    private readonly List<(CoreAudio.Endpoint Endpoint, bool Plugged)> _pending = [];
    private Dictionary<string, uint> _states = [];
    private Timer? _group;
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
        // COM multithread : hors du thread UI. Les états initiaux servent de référence, pas d'événement.
        _ = Task.Run(() =>
        {
            try
            {
                if (!CoreAudio.Subscribe(OnStateChanged))
                {
                    Log.Write("Prise audio : API audio indisponible");
                    return;
                }
                var states = CoreAudio.States();
                lock (_lock) _states = states;
                Log.Write($"Prise audio : {states.Count} points de terminaison surveillés");
            }
            catch (Exception ex)
            {
                Log.Error("Prise audio", ex);
            }
        });
    }

    public override void Stop()
    {
        CoreAudio.Unsubscribe();
        lock (_lock)
        {
            _group?.Dispose();
            _group = null;
            _pending.Clear();
        }
        _host = null;
    }

    /// <summary>Veille : les prises se réannoncent parfois au réveil.</summary>
    public override void OnPowerChanged(bool resumed)
    {
        _suspended = !resumed;
        if (resumed) _quietUntil = Environment.TickCount64 + ResumeGraceMs;
    }

    /// <summary>Rappel du service audio (thread quelconque) : seule la transition « débranché » ↔ « actif » compte.</summary>
    private void OnStateChanged(string id, uint state)
    {
        uint previous;
        lock (_lock)
        {
            bool known = _states.TryGetValue(id, out previous);
            _states[id] = state;
            if (!known) return;
        }
        bool plugged = previous == CoreAudio.StateUnplugged && state == CoreAudio.StateActive;
        bool unplugged = previous == CoreAudio.StateActive && state == CoreAudio.StateUnplugged;
        if (!plugged && !unplugged) return;
        // Propriétés lues hors du rappel : le service audio attend son retour.
        _ = Task.Run(() =>
        {
            try
            {
                if (CoreAudio.Describe(id) is not { } endpoint) return;
                if (IsWireless(endpoint.Bus))
                {
                    Log.Write($"Prise audio : {endpoint.Name} ignoré (Bluetooth / USB)");
                    return;
                }
                Log.Write($"Prise audio : {endpoint.Name} ({endpoint.Adapter}) {(plugged ? "branché" : "débranché")}");
                lock (_lock)
                {
                    if (_host is null) return;
                    _pending.Add((endpoint, plugged));
                    _group ??= new Timer(_ => _host?.Post(Flush));
                    _group.Change(GroupMs, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Prise audio", ex);
            }
        });
    }

    /// <summary>Bluetooth et USB : leurs modules s'en chargent (un casque Bluetooth déconnecté est « débranché »).</summary>
    private static bool IsWireless(string bus)
        => bus.Contains("BTH", StringComparison.OrdinalIgnoreCase) || bus.Contains("USB", StringComparison.OrdinalIgnoreCase);

    /// <summary>Une carte pour les changements regroupés : le dernier état l'emporte, sortie de préférence.</summary>
    private void Flush()
    {
        List<(CoreAudio.Endpoint Endpoint, bool Plugged)> events;
        lock (_lock)
        {
            events = [.. _pending];
            _pending.Clear();
        }
        if (_host is null || events.Count == 0) return;
        bool plugged = events[^1].Plugged;
        var same = events.Where(e => e.Plugged == plugged).Select(e => e.Endpoint).ToList();
        bool output = same.Any(e => e.Render), microphone = same.Any(e => !e.Render);
        var main = output ? same.First(e => e.Render) : same[0];
        var state = new JackState(main.Name, main.Adapter, plugged, output, microphone);

        var s = Host.Settings;
        if (plugged ? !s.AudioNotifyPlugged : !s.AudioNotifyUnplugged) return;
        if (_suspended || Environment.TickCount64 < _quietUntil)
        {
            Log.Write($"Popup prise audio ignoré (veille) : {state.Title}");
            return;
        }
        Host.Notify(new JackCard(state));
    }

    public override IEnumerable<CardContent> PreviewCards() =>
    [
        new JackCard(new JackState(L.T("Jack.Headphones"), "Realtek(R) Audio", true, true, true)),
    ];
}
