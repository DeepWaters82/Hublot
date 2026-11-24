using Hublot.Common;
using Hublot.Interop;

namespace Hublot.Modules.Transfers;

/// <summary>
/// Lit les fenêtres de progression de Windows (classe OperationStatusWindow : copies, déplacements, suppressions de
/// l'Explorateur et de toute application qui passe par le shell) par UI Automation, sur un thread à lui : un appel
/// vers une fenêtre qui ne répond plus peut bloquer, jamais le thread de l'interface.
///
/// Rien ne tourne sans fenêtre à lire : le thread dort jusqu'à ce que le module lui en signale une
/// (<see cref="Watch"/>, sur l'événement d'affichage de la fenêtre). Ensuite, une lecture par seconde (toutes les
/// 2,5 s si tout est en pause ou attend une décision) tant qu'une fenêtre est visible ; chaque lecture est
/// publiée telle quelle, liste vide comprise quand la dernière fenêtre disparaît.
///
/// Éléments lus (identifiants d'automatisation de Windows, indépendants de la langue) : eltStartText,
/// eltFirstLocation, eltSecondLocation, eltSummary, eltRateChart_New, eltItemName, eltTimeRemaining,
/// eltItemsRemaining, eltPauseButton, eltCancelButton ; conflit : eltConflictInterruptDescription et les boutons
/// IdTileKeepSource / IdTileKeepDest / IdTileDecideForEach ; fenêtre : eltDisplayModeBtn (« Plus de détails »).
/// </summary>
internal sealed class TransferReader : IDisposable
{
    public const string WindowClass = "OperationStatusWindow";
    public const string PauseId = "eltPauseButton", CancelId = "eltCancelButton";
    private const string TileClass = "OperationTileDialog", DetailsButtonId = "eltDisplayModeBtn";
    private const int ActiveMs = 1000, IdleMs = 2500, AfterCommandMs = 350;

    private readonly Action<IReadOnlyList<TransferTile>> _publish;
    private readonly object _gate = new();
    private readonly List<nint> _windows = [];
    /// <summary>Fenêtres dont Hublot a déjà ouvert les détails : une seule fois, l'utilisateur peut les refermer.</summary>
    private readonly HashSet<nint> _expanded = [];
    private readonly Queue<(string Key, string ElementId)> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _stop;
    private bool _published;

    /// <summary>Option : ouvrir les détails de la fenêtre de Windows quand ils sont masqués.</summary>
    public volatile bool ExpandDetails;

    /// <param name="publish">Appelé sur le thread de lecture : à transmettre au thread UI.</param>
    public TransferReader(Action<IReadOnlyList<TransferTile>> publish, bool expandDetails)
    {
        _publish = publish;
        ExpandDetails = expandDetails;
        _thread = new Thread(Run) { IsBackground = true, Name = "Hublot.Transfers" };
        _thread.Start();
    }

    /// <summary>Fenêtre de progression affichée : à lire jusqu'à ce qu'elle disparaisse.</summary>
    public void Watch(nint window)
    {
        lock (_gate)
        {
            if (_windows.Contains(window)) return;
            _windows.Add(window);
        }
        _wake.Set();
    }

    /// <summary>« Clique » un élément d'une vignette (pause, annuler, choix d'un conflit) à la prochaine lecture, aussitôt.</summary>
    public void Invoke(string key, string elementId)
    {
        lock (_gate) _commands.Enqueue((key, elementId));
        _wake.Set();
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread.Join(2000);
    }

    private readonly record struct Node(nint Element, string Id, string Class, string Name, int Type, int Tile);

    private void Run()
    {
        UiaClient.CoInitializeEx(0, UiaClient.COINIT_MULTITHREADED);
        UiaClient? uia = null;
        try
        {
            while (!_stop)
            {
                nint[] windows;
                lock (_gate)
                {
                    _windows.RemoveAll(w => WinEvents.IsWindow(w) == 0 || User32.IsWindowVisible(w) == 0);
                    _expanded.RemoveWhere(w => !_windows.Contains(w));
                    windows = [.. _windows];
                    if (windows.Length == 0) _commands.Clear();
                }
                if (windows.Length == 0)
                {
                    if (_published)
                    {
                        _publish([]);
                        _published = false;
                    }
                    _wake.WaitOne();
                    continue;
                }

                if (uia is null && (uia = UiaClient.Create()) is null)
                {
                    Log.Write("Transferts : UI Automation indisponible");
                    _wake.WaitOne(10_000);
                    continue;
                }

                var tiles = new List<TransferTile>();
                foreach (var window in windows) ReadWindow(uia, window, tiles);
                _publish(tiles);
                _published = true;
                bool idle = tiles.Count > 0 && tiles.All(t => t.Paused || t.Attention is not null);
                _wake.WaitOne(idle ? IdleMs : ActiveMs);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Transferts : lecture", ex);
        }
        finally
        {
            uia?.Dispose();
            UiaClient.CoUninitialize();
        }
    }

    /// <summary>Vignettes d'une fenêtre ; exécute d'abord les commandes qui la concernent, puis relit.</summary>
    private void ReadWindow(UiaClient uia, nint window, List<TransferTile> tiles)
    {
        var nodes = Collect(uia, window);
        try
        {
            var parsed = Parse(window, nodes);
            if (RunCommands(parsed, nodes) || Expand(window, parsed, nodes))
            {
                Release(nodes);
                Thread.Sleep(AfterCommandMs); // le temps que Windows applique le clic
                nodes = Collect(uia, window);
                parsed = Parse(window, nodes);
            }
            tiles.AddRange(parsed.Select(p => p.Tile));
        }
        finally
        {
            Release(nodes);
        }
    }

    private static void Release(List<Node> nodes)
    {
        foreach (var node in nodes) UiaClient.Release(node.Element);
        nodes.Clear();
    }

    /// <summary>Tous les éléments de la fenêtre (une vingtaine par vignette), avec le numéro de leur vignette (-1 hors vignette).</summary>
    private static List<Node> Collect(UiaClient uia, nint window)
    {
        var nodes = new List<Node>();
        var root = uia.FromHandle(window);
        if (root == 0) return nodes;
        int tiles = 0;
        Visit(root, 0, -1);
        UiaClient.Release(root);
        return nodes;

        void Visit(nint element, int depth, int tile)
        {
            for (nint child = uia.FirstChild(element); child != 0; child = uia.NextSibling(child))
            {
                string cls = UiaClient.ClassName(child);
                int t = cls == TileClass ? tiles++ : tile;
                nodes.Add(new Node(child, UiaClient.AutomationId(child), cls, UiaClient.Name(child), UiaClient.ControlType(child), t));
                if (depth < 5) Visit(child, depth + 1, t);
            }
        }
    }

    private sealed record Parsed(TransferTile Tile, int Index);

    private static List<Parsed> Parse(nint window, List<Node> nodes)
    {
        var list = new List<Parsed>();
        int count = nodes.Count == 0 ? 0 : nodes.Max(n => n.Tile) + 1;
        for (int t = 0; t < count; t++)
        {
            var own = nodes.Where(n => n.Tile == t).ToList();
            string? Text(string id) => own.FirstOrDefault(n => n.Id == id) is { Element: not 0 } n && n.Name.Length > 0 ? n.Name : null;

            string start = Text("eltStartText") ?? "";
            string? source = Text("eltFirstLocation"), target = Text("eltSecondLocation");
            string? summary = Text("eltSummary");
            // Sans résumé (vignette qui attend une décision) : le pourcentage est encore dans le nom de la vignette.
            var (percent, paused) = TransferTile.ParseSummary(summary ?? own.FirstOrDefault(n => n.Class == TileClass).Name);
            string? itemsLeft = Text("eltItemsRemaining");
            string? chart = own.FirstOrDefault(n => n.Id.StartsWith("eltRateChart", StringComparison.Ordinal)).Name;

            var choices = new List<TransferChoice>();
            int buttons = 0;
            foreach (var n in own.Where(n => n.Type == UiaClient.ButtonControlType))
            {
                buttons++;
                if (n.Id is TransferReader.PauseId or TransferReader.CancelId || n.Name.Length == 0) continue;
                choices.Add(new TransferChoice(n.Id.Length > 0 ? n.Id : "#" + buttons, n.Name));
            }
            string? attention = own.FirstOrDefault(n => n.Name.Length > 0
                && (n.Id.Contains("Interrupt", StringComparison.Ordinal) || n.Id.Contains("Description", StringComparison.Ordinal))).Name;
            if (summary is null && attention is null && choices.Count > 0) attention = "";
            if (summary is not null && choices.Count == 0) attention = null;

            string key = $"{window:X}|{start}|{source}|{target}";
            string operation = TransferTile.TrimOperation(start, source is not null);
            list.Add(new Parsed(new TransferTile(window, key, operation, TransferTile.KindOf(operation), source, target,
                percent, paused, Text("eltItemName"), Text("eltTimeRemaining"), itemsLeft, TransferTile.ParseBytes(itemsLeft),
                TransferTile.ParseChartSpeed(chart), attention, choices,
                DetailsHidden: summary is not null && attention is null && itemsLeft is null && Text("eltItemName") is null), t));
        }
        return list;
    }

    /// <summary>Commandes en attente pour les vignettes de cette fenêtre ; vrai si au moins une a été exécutée.</summary>
    private bool RunCommands(List<Parsed> parsed, List<Node> nodes)
    {
        List<(string Key, string ElementId)> mine;
        lock (_gate)
        {
            if (_commands.Count == 0) return false;
            var keys = parsed.Select(p => p.Tile.Key).ToHashSet();
            mine = _commands.Where(c => keys.Contains(c.Key)).ToList();
            if (mine.Count == 0) return false;
            var rest = _commands.Where(c => !keys.Contains(c.Key)).ToList();
            _commands.Clear();
            foreach (var c in rest) _commands.Enqueue(c);
        }
        bool done = false;
        foreach (var (key, elementId) in mine)
        {
            int tile = parsed.First(p => p.Tile.Key == key).Index;
            var own = nodes.Where(n => n.Tile == tile).ToList();
            Node target;
            if (elementId.StartsWith('#') && int.TryParse(elementId.AsSpan(1), out int ordinal))
                target = own.Where(n => n.Type == UiaClient.ButtonControlType).Skip(ordinal - 1).FirstOrDefault();
            else
                target = own.FirstOrDefault(n => n.Id == elementId);
            bool ok = target.Element != 0 && UiaClient.Invoke(target.Element);
            Log.Write($"Transferts : clic sur {elementId} {(ok ? "transmis" : "impossible")}");
            done |= ok;
        }
        return done;
    }

    /// <summary>Option : détails masqués (« Moins de détails ») → Hublot les affiche, une fois par fenêtre.</summary>
    private bool Expand(nint window, List<Parsed> parsed, List<Node> nodes)
    {
        if (!ExpandDetails || !parsed.Any(p => p.Tile.DetailsHidden)) return false;
        lock (_gate)
        {
            if (!_expanded.Add(window)) return false;
        }
        var button = nodes.FirstOrDefault(n => n.Id == DetailsButtonId);
        bool ok = button.Element != 0 && UiaClient.Invoke(button.Element);
        Log.Write($"Transferts : détails de la fenêtre de Windows {(ok ? "affichés" : "non disponibles")}");
        return ok;
    }
}
