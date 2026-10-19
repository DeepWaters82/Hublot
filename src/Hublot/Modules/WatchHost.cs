using Hublot.Common;
using Hublot.Shell;

namespace Hublot.Modules;

/// <summary>
/// Hôte de diagnostic (<c>Hublot.exe --audio-watch</c>) : fait tourner un module hors du service, réglages par
/// défaut, et transmet ses popups au lieu de les afficher. Aucune fenêtre, aucune règle de lancement ou de plein écran.
/// </summary>
internal sealed class WatchHost(Action<CardContent> popup) : IHost
{
    public Settings Settings { get; } = new();
    public void Post(Action action) => action();
    public void Notify(CardContent card) => popup(card);
    public void Notify(IReadOnlyList<CardContent> cards) { foreach (var card in cards) popup(card); }
    public void Update(CardContent card) { }
    public void ShowPlayer(CardContent card) => popup(card);
    public CardContent? CurrentNotification => null;
    public void Invalidate() { }
    public void Pin(CardContent? card) { }
    public bool IsPinned => false;
    public CardContent? PinnedCard => null;
    public void OpenCore(string? page = null) { }
    public void OpenUri(string uri) { }
}
