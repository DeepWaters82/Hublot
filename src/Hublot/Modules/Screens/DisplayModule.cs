using System.Runtime.InteropServices;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Screens;

/// <summary>
/// Module « Écrans » : popup quand un écran est branché ou débranché (HDMI, DisplayPort, VGA, USB-C…),
/// avec son nom réel, sa connectique et sa résolution. Purement événementiel : WM_DISPLAYCHANGE (relayé
/// par l'hôte), puis comparaison des écrans actifs avant / après (Displays, QueryDisplayConfig).
///
/// Jamais de popup pour un écran qui se rallume : la mise en veille des écrans (DisplayPort coupe la
/// liaison quand l'écran dort) et la sortie de veille du PC sont suivies (GUID_CONSOLE_DISPLAY_STATE,
/// sans fenêtre) et suivies d'un délai de grâce ; les écrans présents au lancement sont la référence.
/// </summary>
internal sealed unsafe class DisplayModule : Module
{
    /// <summary>
    /// Regroupement des WM_DISPLAYCHANGE : une station d'accueil allume ses écrans l'un après l'autre, à une ou
    /// deux secondes d'intervalle ; ils arrivent ainsi dans le même popup.
    /// </summary>
    private const int SettleMs = 2500, WakeGraceMs = 20_000, ResumeGraceMs = 30_000;

    /// <summary>GUID_CONSOLE_DISPLAY_STATE : données DWORD, 0 = écrans éteints, 1 = allumés, 2 = assombris.</summary>
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    /// <summary>Destinataire des rappels du système (une seule instance du module).</summary>
    private static DisplayModule? s_active;

    private IHost? _host;
    private Dictionary<string, DisplayInfo> _known = [];
    private Timer? _settle;
    private nint _stateHandle;
    private bool _displaysOff, _suspended;
    private long _quietUntil;

    public override string Id => "displays";
    public override string Name => "Écrans";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");

    public override bool IsEnabled(Settings settings) => settings.ModuleDisplays;

    public override void Start(IHost host)
    {
        _host = host;
        _known = Snapshot();
        s_active = this;
        var setting = ConsoleDisplayState;
        var recipient = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = &OnDisplayState };
        if (PowerApi.PowerSettingRegisterNotification(&setting, PowerApi.DEVICE_NOTIFY_CALLBACK, &recipient, out var handle) == 0)
            _stateHandle = handle;
        Log.Write($"Écrans : {string.Join(", ", _known.Values.Select(d => $"{d.Name} ({d.Connector}, {d.Mode})"))}");
    }

    public override void Stop()
    {
        if (_stateHandle != 0) PowerApi.PowerSettingUnregisterNotification(_stateHandle);
        _stateHandle = 0;
        s_active = null;
        _settle?.Dispose();
        _settle = null;
        _host = null;
    }

    /// <summary>Plusieurs WM_DISPLAYCHANGE se suivent pour un même branchement : comparaison une fois l'état stable.</summary>
    public override void OnDisplayChanged()
    {
        _settle ??= new Timer(_ => _host?.Post(Compare));
        _settle.Change(SettleMs, Timeout.Infinite);
    }

    public override void OnPowerChanged(bool resumed)
    {
        _suspended = !resumed;
        if (resumed) _quietUntil = Environment.TickCount64 + ResumeGraceMs;
    }

    [UnmanagedCallersOnly]
    private static uint OnDisplayState(nint context, uint type, nint setting)
    {
        if (type != PowerApi.PBT_POWERSETTINGCHANGE || setting == 0 || s_active is not { _host: { } host } module) return 0;
        uint state = *(uint*)(setting + 20); // POWERBROADCAST_SETTING : GUID (16), longueur (4), données
        host.Post(() => module.SetDisplaysOn(state != 0));
        return 0;
    }

    /// <summary>Écrans éteints (veille des écrans) puis rallumés : leurs reconnexions ne sont pas des événements.</summary>
    private void SetDisplaysOn(bool on)
    {
        if (_displaysOff && on) _quietUntil = Math.Max(_quietUntil, Environment.TickCount64 + WakeGraceMs);
        _displaysOff = !on;
    }

    private void Compare()
    {
        if (_host is null) return;
        var now = Snapshot();
        bool quiet = _displaysOff || _suspended || Environment.TickCount64 < _quietUntil;
        var cards = new List<CardContent>();
        foreach (var (path, display) in now)
        {
            if (_known.ContainsKey(path)) continue;
            Log.Write($"Écran branché : {display.Name} ({display.Connector}, {display.Mode}){(quiet ? " — sans popup (veille)" : "")}");
            if (Host.Settings.DisplayNotifyConnect) cards.Add(new DisplayCard(display, connected: true));
        }
        foreach (var (path, display) in _known)
        {
            if (now.ContainsKey(path)) continue;
            Log.Write($"Écran débranché : {display.Name} ({display.Connector}){(quiet ? " — sans popup (veille)" : "")}");
            if (Host.Settings.DisplayNotifyDisconnect) cards.Add(new DisplayCard(display, connected: false));
        }
        _known = now;
        // Station d'accueil, hub : tous les écrans branchés (et l'écran intégré éteint, couvercle fermé) ensemble.
        if (!quiet) Host.Notify(cards);
    }

    private static Dictionary<string, DisplayInfo> Snapshot()
    {
        var map = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in Displays.Active()) map.TryAdd(display.Path, display);
        return map;
    }

    /// <summary>Aperçu : les écrans réellement connectés (un exemple si l'API ne répond pas).</summary>
    public override IEnumerable<CardContent> PreviewCards()
    {
        var displays = Displays.Active();
        if (displays.Count == 0) displays.Add(new DisplayInfo("preview", "", "DELL U2720Q", "HDMI", 2560, 1440, 60, false, 0, 0));
        return displays.Select(d => (CardContent)new DisplayCard(d, connected: true)).ToList();
    }
}
