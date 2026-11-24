using System.Runtime.InteropServices;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Transfers;

/// <summary>
/// Module « Transferts de fichiers » : une carte Fluent déplaçable (comme le lecteur) qui reprend la fenêtre de
/// progression de Windows — opération, source → destination, pourcentage, vitesse et son graphique, nom du
/// fichier, temps et éléments restants —, avec pause / reprise, annulation et les choix d'un conflit de noms.
///
/// Détection sans sondage : SetWinEventHook (EVENT_OBJECT_SHOW, hors processus) signale l'affichage de chaque
/// fenêtre ; seule la classe OperationStatusWindow est retenue (fenêtre de l'Explorateur, ou de toute application
/// qui copie par le shell). Les fenêtres déjà ouvertes au démarrage du module sont reprises. La lecture (UI
/// Automation) se fait sur un thread à part (<see cref="TransferReader"/>), une fois par seconde et seulement tant
/// qu'une fenêtre est visible. La fenêtre de Windows reste affichée : un conflit ou une erreur s'y règle aussi.
/// </summary>
internal sealed class TransfersModule : Module
{
    /// <summary>Carte « Terminé » affichée ce temps après la fin d'une opération.</summary>
    private const long DoneMs = 3000;
    /// <summary>Fenêtre de mesure de la vitesse (octets restants qui diminuent).</summary>
    private const long SpeedWindowMs = 5000, SpeedMinimumMs = 1500;
    /// <summary>Retour immédiat d'un clic sur pause : l'état visé est affiché le temps que Windows le confirme.</summary>
    private const long PauseHoldMs = 2500;
    private const int MaxShown = 3;

    private static TransfersModule? s_current;

    private IHost? _host;
    private TransferReader? _reader;
    private nint _hook;
    private Timer? _doneTimer;
    private readonly Dictionary<string, Track> _tracks = [];
    /// <summary>Opérations présentes quand l'utilisateur a fermé la carte : elle ne revient que pour une nouvelle.</summary>
    private readonly HashSet<string> _closed = [];
    private long _order;
    /// <summary>La carte est demandée à l'écran (dernier rendu non vide, pas fermée depuis).</summary>
    private bool _shown;

    private sealed class Track(TransferTile tile, long order)
    {
        public TransferTile Tile = tile;
        public readonly long Order = order;
        public readonly float[] History = NewHistory();
        public readonly Queue<(long At, long Bytes, int Percent)> Samples = new();
        /// <summary>Octets par seconde si Windows communique les octets restants, sinon pour cent par seconde.</summary>
        public bool ByteSpeed;
        public double Speed = -1;
        public long DoneAt, PauseUntil;
        public bool? PauseTarget;
        public bool Cancelled;
    }

    public override string Id => "transfers";
    public override string Name => "Transferts de fichiers";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");

    public override bool IsEnabled(Settings settings) => settings.ModuleTransfers;

    public override unsafe void Start(IHost host)
    {
        _host = host;
        s_current = this;
        _reader = new TransferReader(tiles => _host?.Post(() => OnTiles(tiles)), host.Settings.TransferExpandDetails);
        _doneTimer = new Timer(_ => _host?.Post(Render));
        _hook = WinEvents.SetWinEventHook(WinEvents.EVENT_OBJECT_SHOW, WinEvents.EVENT_OBJECT_SHOW, 0, &OnWinEvent, 0, 0,
            WinEvents.WINEVENT_OUTOFCONTEXT | WinEvents.WINEVENT_SKIPOWNPROCESS);
        if (_hook == 0) Log.Write("Transferts : SetWinEventHook a échoué");
        int existing = 0;
        for (nint w = User32.FindWindowExW(0, 0, TransferReader.WindowClass, null); w != 0; w = User32.FindWindowExW(0, w, TransferReader.WindowClass, null))
        {
            if (User32.IsWindowVisible(w) == 0) continue;
            _reader.Watch(w);
            existing++;
        }
        Log.Write($"Transferts : prêt ({existing} fenêtre(s) de progression déjà ouverte(s))");
    }

    public override void Stop()
    {
        if (_hook != 0) WinEvents.UnhookWinEvent(_hook);
        _hook = 0;
        _reader?.Dispose();
        _reader = null;
        _doneTimer?.Dispose();
        _doneTimer = null;
        _tracks.Clear();
        _closed.Clear();
        _host?.ShowTransfers(null);
        _host = null;
        if (s_current == this) s_current = null;
    }

    public override void ApplySettings(Settings settings)
    {
        if (_reader is not null) _reader.ExpandDetails = settings.TransferExpandDetails;
        Render();
    }

    public override IEnumerable<CardContent> PreviewCards() => [new TransferCard(Sample(), DemoActions, controls: true)];

    /// <summary>La carte a été fermée par l'utilisateur (clic droit › Fermer) : masquée jusqu'à une nouvelle opération ou au raccourci.</summary>
    public void ClosedByUser()
    {
        foreach (var key in _tracks.Keys) _closed.Add(key);
        _shown = false;
        Log.Write("Transferts : carte fermée jusqu'au prochain transfert ou au raccourci");
    }

    /// <summary>
    /// Raccourci (Ctrl + Alt + T par défaut) : carte affichée → masquée, comme « Fermer » ; masquée → de nouveau
    /// affichée. Aucun transfert en cours : un popup le dit.
    /// </summary>
    public void ToggleFromHotkey()
    {
        if (_host is null) return;
        if (!_tracks.Values.Any(t => !t.Cancelled))
        {
            Host.Notify(new MessageCard("transfers-none", "", L.T("Xfer.Name"), L.T("Xfer.None")));
            return;
        }
        if (_shown)
        {
            foreach (var key in _tracks.Keys) _closed.Add(key);
            Log.Write("Transferts : carte masquée (raccourci)");
        }
        else
        {
            _closed.Clear();
            Log.Write("Transferts : carte affichée (raccourci)");
        }
        Render();
    }

    /// <summary>Affichage d'une fenêtre, n'importe où dans le système (thread UI, par la boucle de messages).</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnWinEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != WinEvents.OBJID_WINDOW || idChild != 0 || hwnd == 0 || s_current?._reader is not { } reader) return;
        var name = stackalloc char[32];
        int length = User32.GetClassNameW(hwnd, name, 32);
        if (length == TransferReader.WindowClass.Length && new ReadOnlySpan<char>(name, length).SequenceEqual(TransferReader.WindowClass))
        {
            Log.Write("Transferts : fenêtre de progression affichée");
            reader.Watch(hwnd);
        }
    }

    // ── Lectures ─────────────────────────────────────────────────────────────────────────

    private void OnTiles(IReadOnlyList<TransferTile> tiles)
    {
        if (_host is null) return;
        long now = Environment.TickCount64;
        var seen = new HashSet<string>();
        foreach (var tile in tiles)
        {
            seen.Add(tile.Key);
            if (!_tracks.TryGetValue(tile.Key, out var track))
            {
                _tracks[tile.Key] = track = new Track(tile, _order++) { ByteSpeed = tile.BytesLeft >= 0 };
                Log.Write($"Transferts : nouvelle opération ({tile.Kind}, {(tile.Percent >= 0 ? tile.Percent + " %" : "début")})");
            }
            if (track.DoneAt != 0) track.DoneAt = 0; // réapparue (lecture manquée)
            Measure(track, tile, now);
            track.Tile = tile;
            if (track.PauseTarget is { } target && (tile.Paused == target || now > track.PauseUntil)) track.PauseTarget = null;
        }

        foreach (var (key, track) in _tracks.ToList())
        {
            if (seen.Contains(key)) continue;
            if (track.DoneAt == 0 && !track.Cancelled && track.Tile.Attention is null && track.Tile.Percent >= 90)
            {
                track.DoneAt = now;
                Log.Write("Transferts : opération terminée");
            }
            else if (track.DoneAt == 0 || now - track.DoneAt >= DoneMs)
            {
                _tracks.Remove(key);
                _closed.Remove(key);
            }
        }
        Render();
    }

    /// <summary>
    /// Vitesse : octets restants (ou pour cent) qui diminuent sur les 5 dernières secondes ; rien pendant une pause
    /// ou un conflit. Mémorisée par pour cent de progression pour le graphique.
    /// </summary>
    private static void Measure(Track track, TransferTile tile, long now)
    {
        if (tile.Paused || tile.Attention is not null || tile.Percent < 0)
        {
            track.Samples.Clear();
            track.Speed = -1;
            return;
        }
        if (!track.ByteSpeed && tile.BytesLeft >= 0)
        {
            // Octets enfin lisibles (détails ouverts après coup) : la vitesse passe en octets, le graphique repart.
            track.ByteSpeed = true;
            track.Samples.Clear();
            Array.Fill(track.History, float.NaN);
        }
        if (track.ByteSpeed && tile.BytesLeft < 0) return; // détails momentanément illisibles : on garde la mesure
        long amount = track.ByteSpeed ? tile.BytesLeft : -tile.Percent;
        if (track.Samples.Count > 0 && amount > track.Samples.Last().Bytes) track.Samples.Clear(); // recalcul de Windows
        track.Samples.Enqueue((now, amount, tile.Percent));
        while (track.Samples.Count > 2 && now - track.Samples.Peek().At > SpeedWindowMs) track.Samples.Dequeue();

        var first = track.Samples.Peek();
        long elapsed = now - first.At;
        if (elapsed < SpeedMinimumMs) return;
        double speed = (first.Bytes - amount) * 1000.0 / elapsed;
        // Octets restants arrondis par Windows (0,1 Go) : sans changement depuis peu, la vitesse précédente tient ;
        // rien sur toute la fenêtre de mesure, c'est vraiment à l'arrêt.
        if (speed > 0) track.Speed = speed;
        else if (elapsed >= SpeedWindowMs - 1000) track.Speed = 0;
        if (track.Speed > 0)
        {
            // Toutes les cases franchies depuis la mesure précédente reçoivent la vitesse : une courbe sans trou.
            int from = Math.Clamp(first.Percent, 0, 100), to = Math.Clamp(tile.Percent, 0, 100);
            for (int p = from; p <= to; p++) track.History[p] = (float)track.Speed;
        }
    }

    // ── Carte ────────────────────────────────────────────────────────────────────────────

    private void Render()
    {
        if (_host is null) return;
        long now = Environment.TickCount64;
        var views = _tracks.Values.Where(t => !t.Cancelled).OrderBy(t => t.Order).Select(t => View(t, now)).ToList();
        _closed.RemoveWhere(k => !_tracks.ContainsKey(k));

        bool pendingDone = _tracks.Values.Any(t => t.DoneAt != 0);
        _doneTimer?.Change(pendingDone ? 1000 : Timeout.Infinite, Timeout.Infinite);
        if (pendingDone)
        {
            foreach (var (key, track) in _tracks.ToList())
                if (track.DoneAt != 0 && now - track.DoneAt >= DoneMs) _tracks.Remove(key);
            views = views.Where(v => _tracks.ContainsKey(v.Key)).ToList();
        }

        if (views.Count == 0 || views.All(v => _closed.Contains(v.Key)))
        {
            _shown = false;
            Host.ShowTransfers(null);
            return;
        }
        if (views.Count > MaxShown) views = views.Take(MaxShown).ToList();
        _shown = true;
        Host.ShowTransfers(new TransferCard(views, Actions, Host.Settings.TransferControls));
    }

    private static TransferView View(Track track, long now)
    {
        var tile = track.Tile;
        bool paused = track.PauseTarget ?? tile.Paused;
        var phase = track.DoneAt != 0 ? TransferPhase.Done
            : tile.Attention is not null ? TransferPhase.Attention
            : paused ? TransferPhase.Paused
            : TransferPhase.Running;
        string? speed = phase == TransferPhase.Running
            ? track.ByteSpeed && track.Speed > 0 ? L.F("Xfer.PerSecond", L.Size((ulong)track.Speed)) : tile.ChartSpeed
            : null;
        int percent = phase == TransferPhase.Done ? 100 : tile.Percent;
        return new TransferView(tile, phase, percent, speed, (float[])track.History.Clone());
    }

    private TransferActions Actions => new(Open, TogglePause, Cancel, Choose);

    /// <summary>Clic sur la carte : la fenêtre de Windows au premier plan (Hublot vient de recevoir le clic, Windows l'autorise).</summary>
    private static void Open(TransferView view)
    {
        var window = view.Tile.Window;
        if (WinEvents.IsWindow(window) == 0) return;
        if (User32.IsIconic(window) != 0) User32.ShowWindow(window, User32.SW_RESTORE);
        User32.SetForegroundWindow(window);
    }

    private void TogglePause(TransferView view)
    {
        if (!_tracks.TryGetValue(view.Key, out var track) || view.Phase is not (TransferPhase.Running or TransferPhase.Paused)) return;
        track.PauseTarget = view.Phase == TransferPhase.Running;
        track.PauseUntil = Environment.TickCount64 + PauseHoldMs;
        _reader?.Invoke(view.Key, TransferReader.PauseId);
        Log.Write(track.PauseTarget == true ? "Transferts : pause demandée" : "Transferts : reprise demandée");
        Render();
    }

    private void Cancel(TransferView view)
    {
        if (!_tracks.TryGetValue(view.Key, out var track)) return;
        track.Cancelled = true;
        _reader?.Invoke(view.Key, TransferReader.CancelId);
        Log.Write("Transferts : annulation demandée");
        Render();
    }

    private void Choose(TransferView view, TransferChoice choice)
    {
        _reader?.Invoke(view.Key, choice.Id);
        Log.Write($"Transferts : choix « {choice.Id} »");
        // « Me laisser décider pour chaque fichier » (ou un bouton inconnu) : la suite se passe dans la fenêtre de Windows.
        if (choice.Id is not ("IdTileKeepSource" or "IdTileKeepDest")) Open(view);
    }

    // ── Aperçu ───────────────────────────────────────────────────────────────────────────

    private static readonly TransferActions DemoActions = new(_ => { }, _ => { }, _ => { }, (_, _) => { });

    private static float[] NewHistory()
    {
        var history = new float[101];
        Array.Fill(history, float.NaN);
        return history;
    }

    /// <summary>Copie d'exemple (aperçu de Hublot Core, planche --snapshot) : 62 %, vitesse qui varie.</summary>
    internal static List<TransferView> Sample(TransferPhase phase = TransferPhase.Running, int percent = 62, bool conflict = false)
    {
        var history = NewHistory();
        for (int p = 0; p <= Math.Min(percent, 100); p++)
            history[p] = (float)(180e6 + 60e6 * Math.Sin(p / 7.0) + 25e6 * Math.Sin(p / 2.3) - (p > 30 && p < 40 ? 90e6 : 0));
        var choices = conflict
            ? new List<TransferChoice> { new("IdTileKeepSource", L.T("Xfer.Preview.Replace")), new("IdTileKeepDest", L.T("Xfer.Preview.Skip")), new("IdTileDecideForEach", L.T("Xfer.Preview.Decide")) }
            : [];
        var tile = new TransferTile(0, "preview", L.T("Xfer.Preview.Operation"), TransferKind.Copy, L.T("Xfer.Preview.Source"), L.T("Xfer.Preview.Target"),
            percent, phase == TransferPhase.Paused, L.T("Xfer.Preview.File"), L.T("Xfer.Preview.Time"), L.T("Xfer.Preview.Items"), 4L << 30,
            null, conflict ? L.T("Xfer.Preview.Conflict") : null, choices, false);
        var speed = phase == TransferPhase.Running ? L.F("Xfer.PerSecond", L.Size(187UL << 20)) : null;
        return [new TransferView(tile, conflict ? TransferPhase.Attention : phase, phase == TransferPhase.Done ? 100 : percent, speed, history)];
    }
}
