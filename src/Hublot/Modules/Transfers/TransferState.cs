using System.Globalization;
using System.Text.RegularExpressions;

namespace Hublot.Modules.Transfers;

internal enum TransferKind { Copy, Move, Delete, Other }

/// <summary>Bouton d'une vignette qui attend une décision (conflit de noms, erreur) : libellé de Windows.</summary>
/// <param name="Id">Identifiant d'automatisation (« IdTileKeepDest »…) ou « #n » (n-ième bouton) s'il n'en a pas.</param>
internal sealed record TransferChoice(string Id, string Label);

/// <summary>
/// Une opération lue dans la fenêtre de progression de Windows (OperationStatusWindow), c'est-à-dire une
/// « vignette » : textes tels que Windows les affiche (déjà dans la langue de Windows), plus quelques valeurs
/// extraites (pourcentage, octets restants).
/// </summary>
/// <param name="Key">Identité stable d'une lecture à l'autre : fenêtre, phrase de départ, source, destination.</param>
/// <param name="Operation">« Copie de 11 éléments » (phrase de Windows sans la préposition finale).</param>
/// <param name="Percent">0-100, -1 si inconnu.</param>
/// <param name="BytesLeft">Octets restants (« 9 (29,8 Go) »), -1 si inconnus.</param>
/// <param name="ChartSpeed">Vitesse annoncée par le graphique de Windows (« 2,91 Go par seconde »), si lisible.</param>
/// <param name="Attention">Texte d'une vignette qui attend une décision (conflit, erreur), sinon null.</param>
/// <param name="DetailsHidden">Fenêtre en mode « Moins de détails » : ni nom, ni temps, ni éléments restants.</param>
internal sealed record TransferTile(
    nint Window, string Key, string Operation, TransferKind Kind, string? Source, string? Target,
    int Percent, bool Paused, string? ItemName, string? TimeLeft, string? ItemsLeft, long BytesLeft,
    string? ChartSpeed, string? Attention, IReadOnlyList<TransferChoice> Choices, bool DetailsHidden)
{
    /// <summary>Nature de l'opération d'après la phrase de Windows (français, anglais ; sinon « autre »).</summary>
    public static TransferKind KindOf(string operation)
    {
        var s = operation.ToLowerInvariant();
        if (s.Contains("suppr") || s.Contains("delet") || s.Contains("corbeille") || s.Contains("recycl")) return TransferKind.Delete;
        if (s.Contains("dépla") || s.Contains("depla") || s.Contains("mov")) return TransferKind.Move;
        if (s.Contains("copi") || s.Contains("copy")) return TransferKind.Copy;
        return TransferKind.Other;
    }

    /// <summary>« Copie de 11 éléments de » → « Copie de 11 éléments » : la préposition qui précède la source.</summary>
    public static string TrimOperation(string start, bool followedByLocation)
    {
        var s = start.Trim();
        if (!followedByLocation) return s;
        int space = s.LastIndexOf(' ');
        if (space <= 0) return s;
        string last = s[(space + 1)..];
        // Une préposition courte (« de », « from », « d' », « vers »…), jamais un nombre ni le mot principal.
        return last.Length <= 5 && !last.Any(char.IsDigit) ? s[..space].TrimEnd() : s;
    }

    private static readonly Regex PercentPattern = new(@"(\d{1,3})\s*%", RegexOptions.CultureInvariant);

    /// <summary>Pourcentage et pause d'après « 20% terminé » / « En pause - 46% terminé » (un état précède le nombre).</summary>
    public static (int Percent, bool Paused) ParseSummary(string? summary)
    {
        if (string.IsNullOrEmpty(summary)) return (-1, false);
        var m = PercentPattern.Match(summary);
        if (!m.Success) return (-1, false);
        int percent = Math.Clamp(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0, 100);
        string before = summary[..m.Index].TrimEnd();
        return (percent, before.EndsWith('-') || before.Contains(" - "));
    }

    private static readonly Regex SizePattern = new(@"\(\s*([\d\s  .,]+?)\s*([A-Za-zÀ-ÿ]+)\s*\)", RegexOptions.CultureInvariant);

    /// <summary>Octets restants d'après « 9 (29,8 Go) » / « 9 (29.8 GB) » (unités binaires, comme l'Explorateur), -1 sinon.</summary>
    public static long ParseBytes(string? itemsLeft)
    {
        if (string.IsNullOrEmpty(itemsLeft)) return -1;
        var m = SizePattern.Match(itemsLeft);
        if (!m.Success) return -1;
        var number = m.Groups[1].Value.Replace(" ", "").Replace(" ", "").Replace(" ", "");
        // « 1,234.5 » (anglais) : la virgule sépare les milliers ; « 29,8 » (français) : c'est la décimale.
        number = number.Contains('.') ? number.Replace(",", "") : number.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return -1;
        double unit = char.ToUpperInvariant(m.Groups[2].Value[0]) switch
        {
            'K' => 1L << 10,
            'M' => 1L << 20,
            'G' => 1L << 30,
            'T' => 1L << 40,
            'O' or 'B' => 1,
            _ => -1,
        };
        return unit < 0 ? -1 : (long)(value * unit);
    }

    /// <summary>« Progression de l'opération : 6% ; vitesse : 2,91 Go par seconde » → « 2,91 Go/s ».</summary>
    public static string? ParseChartSpeed(string? chart)
    {
        if (string.IsNullOrEmpty(chart)) return null;
        int colon = chart.LastIndexOf(':');
        if (colon < 0) return null;
        var speed = chart[(colon + 1)..].Trim();
        if (!speed.Any(char.IsDigit) || speed.Contains('%')) return null;
        return speed.Replace(" par seconde", "/s").Replace(" per second", "/s");
    }
}

internal enum TransferPhase { Running, Paused, Attention, Done }

/// <summary>Ce que la carte affiche pour une opération : la vignette lue, plus ce que Hublot en déduit.</summary>
/// <param name="History">Vitesse par pour cent de progression (101 cases, NaN sans mesure) : le graphique.</param>
internal sealed record TransferView(TransferTile Tile, TransferPhase Phase, int Percent, string? Speed, float[] History)
{
    public string Key => Tile.Key;
}

/// <summary>Actions de la carte, exécutées par le module.</summary>
internal sealed record TransferActions(
    Action<TransferView> Open, Action<TransferView> TogglePause, Action<TransferView> Cancel, Action<TransferView, TransferChoice> Choose);
