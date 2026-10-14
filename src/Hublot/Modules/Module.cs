using Hublot.Common;
using Hublot.Shell;

namespace Hublot.Modules;

/// <summary>Services que Hublot offre aux modules. Thread UI uniquement, sauf <see cref="Post"/>.</summary>
internal interface IHost
{
    Settings Settings { get; }

    /// <summary>Exécute une action sur le thread UI (depuis n'importe quel thread).</summary>
    void Post(Action action);

    /// <summary>
    /// Carte d'événement. Hublot applique les règles communes (application en plein écran…) avant
    /// d'afficher ; une carte d'événement déjà visible est remplacée sur place.
    /// </summary>
    void Notify(CardContent card);

    /// <summary>
    /// Plusieurs événements simultanés (deux écrans d'une station d'accueil…) : une carte chacun, empilées
    /// comme la liste, mêmes règles que <see cref="Notify(CardContent)"/>.
    /// </summary>
    void Notify(IReadOnlyList<CardContent> cards);

    /// <summary>Complément d'information pour une carte affichée de même clé (pas de nouvel affichage).</summary>
    void Update(CardContent card);

    /// <summary>Carte d'événement actuellement affichée, s'il y en a une.</summary>
    CardContent? CurrentNotification { get; }

    /// <summary>Redessine les cartes affichées (état modifié sans nouvelle carte).</summary>
    void Invalidate();

    /// <summary>
    /// Carte épinglée (mini-lecteur) : affichée en permanence à son propre emplacement, masquée pendant
    /// une application en plein écran ; null pour la retirer. Une carte de même taille est remplacée sur place.
    /// </summary>
    void Pin(CardContent? card);

    /// <summary>Une carte épinglée est demandée (affichée, ou masquée le temps d'un plein écran).
    /// Faux après un clic de l'utilisateur qui l'a fermée.</summary>
    bool IsPinned { get; }

    /// <summary>Carte épinglée actuellement visible.</summary>
    CardContent? PinnedCard { get; }

    /// <summary>Ouvre Hublot Core, éventuellement sur une page.</summary>
    void OpenCore(string? page = null);

    /// <summary>Ouvre une URI système (« ms-settings:bluetooth »…).</summary>
    void OpenUri(string uri);
}

/// <summary>
/// Module de Hublot : démarré seulement s'il est activé dans Hublot Core, arrêté sinon. Tous les
/// modules vivent dans le même processus (un seul runtime .NET) : un module désactivé ne coûte rien.
/// </summary>
internal abstract class Module
{
    /// <summary>Identifiant stable (paramètres, pages de Hublot Core).</summary>
    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract bool IsEnabled(Settings settings);

    public abstract void Start(IHost host);

    public abstract void Stop();

    /// <summary>Nouveaux paramètres (le module reste démarré).</summary>
    public virtual void ApplySettings(Settings settings) { }

    /// <summary>Mise en veille (false) / sortie de veille (true).</summary>
    public virtual void OnPowerChanged(bool resumed) { }

    /// <summary>Configuration des écrans modifiée (WM_DISPLAYCHANGE : écran branché, débranché, résolution…).</summary>
    public virtual void OnDisplayChanged() { }

    /// <summary>Cartes de la liste (raccourci clavier, clic sur l'icône), dans l'ordre d'affichage.</summary>
    public virtual IEnumerable<CardContent> ListCards() => [];

    /// <summary>Lignes de la carte du survol de l'icône.</summary>
    public virtual IEnumerable<HoverRow> HoverRows() => [];

    /// <summary>Cartes d'exemple pour les aperçus de Hublot Core.</summary>
    public virtual IEnumerable<CardContent> PreviewCards() => [];
}
