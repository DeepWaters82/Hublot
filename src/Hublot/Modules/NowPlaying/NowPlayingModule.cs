using Hublot.Common;
using Hublot.Shell;
using Windows.Foundation;
using Windows.Media.Control;

namespace Hublot.Modules.NowPlaying;

/// <summary>
/// Module « Lecture en cours » : les sessions média déclarées à Windows (Spotify, navigateurs —
/// YouTube, YouTube Music, Deezer… —, Lecteur multimédia, VLC…), via
/// GlobalSystemMediaTransportControlsSessionManager. Purement événementiel, aucun sondage.
///
/// Règles anti-spam : un popup par nouveau titre (artiste + titre), seulement en lecture, une fois
/// les informations stables (<see cref="SettleMs"/> : absorbe les événements en double et les
/// aperçus au survol des miniatures YouTube) ; pas de popup pour pause / reprise / avance (option),
/// ni pour une application exclue ou au premier plan (option).
/// </summary>
internal sealed class NowPlayingModule : Module
{
    private const int SettleMs = 900, PendingMs = 3000;
    /// <summary>Après suivant / précédent depuis une carte : le nouveau titre est attendu, regroupement plus court.</summary>
    private const int SkipSettleMs = 350, SkipWindowMs = 4000;
    /// <summary>Plus rien à montrer : le mini-lecteur attend avant de partir (un navigateur recrée sa session entre deux titres).</summary>
    private const int UnpinDelayMs = 3000;
    /// <summary>Nouveau titre sans sa pochette : l'ancienne reste affichée jusqu'à son arrivée, au plus ce délai.</summary>
    private const int CoverWaitMs = 2500;

    private sealed class Tracker(GlobalSystemMediaTransportControlsSession session, string appId)
    {
        /// <summary>Objet session courant : un navigateur peut recréer sa session (même AUMID) à tout moment.</summary>
        public GlobalSystemMediaTransportControlsSession Session { get; set; } = session;
        public string AppId { get; } = appId;
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? OnProperties;
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? OnPlayback;
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>? OnTimeline;
        public Timer? Debounce;
        /// <summary>Session présente au lancement : son titre courant ne déclenche pas de popup.</summary>
        public bool Baseline;
        public string? LastKey, CoverKey;
        public bool WasPlaying;
        public Cover? Cover;
        public MediaState? State;
        /// <summary>
        /// Lecture / pause demandée depuis une carte, pas encore confirmée par l'application : maintenue
        /// (<see cref="PendingMs"/> au plus) pour que le bouton ne revienne pas en arrière sur un événement
        /// encore porteur de l'ancien état. Avec la progression figée / relancée au moment du clic.
        /// </summary>
        public bool? PendingPlaying;
        public MediaTimeline? PendingTimeline;
        public long PendingUntil;
        /// <summary>Jusqu'à quand le titre suivant / précédent demandé est attendu.</summary>
        public long SkipUntil;
        /// <summary>Titre dont la pochette est attendue, et jusqu'à quand l'ancienne la remplace.</summary>
        public string? CoverWaitKey;
        public long CoverWaitUntil;
    }

    private readonly Dictionary<string, Tracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MediaApp> _apps = new(StringComparer.OrdinalIgnoreCase);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private IHost? _host;
    private Timer? _saveApps;
    private bool _initialSync = true;
    /// <summary>Dernière application à avoir lancé un titre : c'est elle que montre le mini-lecteur.</summary>
    private string? _activeApp;
    private Timer? _tick, _unpin;
    /// <summary>Choix fait au raccourci clavier (afficher / masquer le mini-lecteur), prioritaire sur l'option ; null = l'option.</summary>
    private bool? _pinnedOverride;

    public override string Id => "nowplaying";
    public override string Name => "Lecture en cours";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");

    public override bool IsEnabled(Settings settings) => settings.ModuleNowPlaying;

    public override void Start(IHost host)
    {
        _host = host;
        _initialSync = true;
        _pinnedSetting = host.Settings.MediaPinned;
        foreach (var app in MediaStore.LoadApps()) _apps[app.Id] = app;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _host?.Post(() =>
            {
                if (_host is null) return;
                _manager = manager;
                manager.SessionsChanged += OnSessionsChanged;
                Sync();
                Log.Write("Sessions média : surveillance démarrée");
            });
        }
        catch (Exception ex)
        {
            Log.Error("Sessions média", ex);
        }
    }

    public override void Stop()
    {
        if (_manager is not null) _manager.SessionsChanged -= OnSessionsChanged;
        foreach (var tracker in _trackers.Values) Detach(tracker);
        _trackers.Clear();
        _manager = null;
        _saveApps?.Dispose();
        _saveApps = null;
        SaveApps();
        _tick?.Dispose();
        _tick = null;
        _unpin?.Dispose();
        _unpin = null;
        if (_host?.IsPinned == true) _host.Pin(null);
        _host = null;
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => _host?.Post(Sync);

    /// <summary>
    /// Suit les sessions apparues, oublie celles qui ont disparu, et se rebranche sur les objets session
    /// à jour : Chromium (Brave, Chrome, Edge…) recrée sa session, par exemple après une pause, en gardant
    /// le même AUMID. Rester abonné à l'ancien objet, c'est ne plus recevoir aucun événement.
    /// </summary>
    private void Sync()
    {
        if (_manager is null) return;
        var current = new Dictionary<string, GlobalSystemMediaTransportControlsSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in _manager.GetSessions()) current[session.SourceAppUserModelId] = session;

        bool removed = false;
        foreach (var id in _trackers.Keys.Where(id => !current.ContainsKey(id)).ToList())
        {
            Detach(_trackers[id]);
            _trackers.Remove(id);
            removed = true;
        }
        if (removed) UpdatePinned(trigger: false);
        foreach (var (id, session) in current)
        {
            if (_trackers.TryGetValue(id, out var existing))
            {
                // Même application : on garde son état (dernier titre, lecture…) mais on suit le nouvel objet.
                Detach(existing, keepTimer: true);
                existing.Session = session;
                Attach(existing);
                Schedule(existing, immediate: true);
                continue;
            }
            var tracker = new Tracker(session, id) { Baseline = _initialSync };
            tracker.OnProperties = (_, _) => _host?.Post(() => Schedule(tracker));
            tracker.OnPlayback = (_, _) => _host?.Post(() => Schedule(tracker));
            // Position (avance, recul…) : seulement avec l'option progression, sans relire tout l'état.
            tracker.OnTimeline = (_, _) =>
            {
                if (_host is { Settings.MediaShowProgress: true } host) host.Post(() => RefreshTimeline(tracker));
            };
            Attach(tracker);
            _trackers[id] = tracker;
            Schedule(tracker, immediate: true);
        }
        _initialSync = false;
    }

    private static void Attach(Tracker tracker)
    {
        tracker.Session.MediaPropertiesChanged += tracker.OnProperties;
        tracker.Session.PlaybackInfoChanged += tracker.OnPlayback;
        tracker.Session.TimelinePropertiesChanged += tracker.OnTimeline;
    }

    private static void Detach(Tracker tracker, bool keepTimer = false)
    {
        try
        {
            if (tracker.OnProperties is not null) tracker.Session.MediaPropertiesChanged -= tracker.OnProperties;
            if (tracker.OnPlayback is not null) tracker.Session.PlaybackInfoChanged -= tracker.OnPlayback;
            if (tracker.OnTimeline is not null) tracker.Session.TimelinePropertiesChanged -= tracker.OnTimeline;
        }
        catch (Exception)
        {
            // Session déjà détruite par l'application : rien à désabonner.
        }
        if (!keepTimer)
        {
            tracker.Debounce?.Dispose();
            tracker.Debounce = null;
        }
    }

    /// <summary>Regroupe les événements d'une session : évaluation une fois l'état stable.</summary>
    private void Schedule(Tracker tracker, bool immediate = false)
    {
        tracker.Debounce ??= new Timer(_ => _host?.Post(() => _ = EvaluateAsync(tracker)));
        int delay = immediate ? 50 : Environment.TickCount64 < tracker.SkipUntil ? SkipSettleMs : SettleMs;
        tracker.Debounce.Change(delay, Timeout.Infinite);
    }

    /// <summary>Lit l'état de la session (hors thread UI pour la pochette), puis l'applique sur le thread UI.</summary>
    private async Task EvaluateAsync(Tracker tracker)
    {
        string? coverKey = tracker.CoverKey;
        try
        {
            var props = await tracker.Session.TryGetMediaPropertiesAsync();
            var info = tracker.Session.GetPlaybackInfo();
            string title = props?.Title?.Trim() ?? "";
            string artist = (string.IsNullOrWhiteSpace(props?.Artist) ? props?.AlbumArtist : props.Artist)?.Trim() ?? "";
            string key = artist + "\n" + title;
            Cover? cover = null;
            if (title.Length > 0 && coverKey != key) cover = await CoverLoader.LoadAsync(props?.Thumbnail);

            var controls = info?.Controls;
            bool playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var (appName, appIcon) = MediaApps.Resolve(tracker.AppId);
            string? notMusic = MediaApps.NotMusicReason(tracker.AppId, appName, info?.PlaybackType ?? props?.PlaybackType, props?.AlbumTitle ?? "");
            var timeline = _host?.Settings.MediaShowProgress == true ? ReadTimeline(tracker.Session) : null;
            var state = new MediaState(tracker.AppId, appName, title, artist, playing,
                controls?.IsPreviousEnabled == true, controls?.IsNextEnabled == true,
                controls?.IsPlayPauseToggleEnabled == true || controls?.IsPlayEnabled == true || controls?.IsPauseEnabled == true,
                null, appIcon, notMusic, timeline, controls?.IsPlaybackPositionEnabled == true);
            _host?.Post(() => Apply(tracker, state, key, cover));
        }
        catch (Exception ex)
        {
            Log.Error($"Session média {tracker.AppId}", ex);
        }
    }

    /// <summary>
    /// Pochette à montrer pour ce titre. Pas encore arrivée (le navigateur l'envoie après le titre) :
    /// l'ancienne reste affichée — la carte passera directement de l'une à l'autre en fondu, sans
    /// tuile ni logo intermédiaire — au plus <see cref="CoverWaitMs"/>, puis la tuile « musique ».
    /// </summary>
    private Cover? CoverFor(Tracker tracker, string key)
    {
        if (tracker.CoverKey == key) return tracker.Cover;
        if (tracker.Cover is null) return null;
        long now = Environment.TickCount64;
        if (tracker.CoverWaitKey != key)
        {
            tracker.CoverWaitKey = key;
            tracker.CoverWaitUntil = now + CoverWaitMs;
        }
        if (now >= tracker.CoverWaitUntil) return null;
        // Nouvel essai à l'échéance : la pochette a pu arriver sans événement ; sinon, tuile.
        tracker.Debounce ??= new Timer(_ => _host?.Post(() => _ = EvaluateAsync(tracker)));
        tracker.Debounce.Change(tracker.CoverWaitUntil - now + 50, Timeout.Infinite);
        return tracker.Cover;
    }

    private void Apply(Tracker tracker, MediaState state, string key, Cover? cover)
    {
        if (_host is null || !_trackers.ContainsKey(tracker.AppId)) return;
        if (cover is not null)
        {
            tracker.Cover = cover;
            tracker.CoverKey = key;
        }
        state = WithPending(tracker, state with { Cover = CoverFor(tracker, key) });
        if (state.Title.Length == 0)
        {
            tracker.State = null;
            return;
        }
        tracker.State = state;
        Remember(tracker.AppId, state.AppName);

        // Carte de cette session déjà affichée : pochette arrivée, lecture / pause…
        Host.Update(Card(state));

        bool newTrack = key != tracker.LastKey;
        bool resumed = state.Playing && !tracker.WasPlaying;
        bool paused = !state.Playing && tracker.WasPlaying;
        Log.Write($"Média {state.AppName} : « {state.Title} » lecture={state.Playing} nouveau={newTrack} reprise={resumed} référence={tracker.Baseline} musique={state.NotMusic ?? "oui"}");
        tracker.WasPlaying = state.Playing;
        if (resumed || newTrack && state.Playing) _activeApp = tracker.AppId;
        UpdatePinned(trigger: tracker.Baseline || resumed || newTrack && state.Playing);
        if (tracker.Baseline)
        {
            // Ce qui jouait déjà au lancement de Hublot n'est pas un événement.
            tracker.Baseline = false;
            tracker.LastKey = key;
            return;
        }
        if (!state.Playing)
        {
            // Pause (ou arrêt) d'une lecture en cours ; les événements groupés sur 900 ms absorbent les
            // pauses éclair entre deux titres.
            if (paused && Host.Settings.MediaNotifyOnPause) Show(state, "pause");
            return;
        }
        if (newTrack)
        {
            // Onglet sans album : il peut arriver un peu après le titre ; le titre reste « nouveau » d'ici là.
            if (state.NotMusic != MediaApps.NoAlbum || !NotMusicFiltered(state)) tracker.LastKey = key;
            if (Host.Settings.MediaNotifyTrackChange) Show(state, "nouveau titre");
        }
        else if (resumed && Host.Settings.MediaNotifyOnResume)
        {
            Show(state, "reprise");
        }
    }

    private void Show(MediaState state, string reason)
    {
        if (Host.Settings.MediaExcludedApps.Contains(state.AppId))
        {
            Log.Write($"Média ignoré (application exclue) : {state.AppName}");
            return;
        }
        if (NotMusicFiltered(state))
        {
            Log.Write($"Média ignoré (pas de la musique : {state.NotMusic}) : {state.Title} ({state.AppName})");
            return;
        }
        if (Host.Settings.MediaHideWhenAppFocused && MediaApps.IsForeground(state.AppId))
        {
            Log.Write($"Média ignoré (application au premier plan) : {state.AppName}");
            return;
        }
        if (Host.PinnedCard is MediaCard)
        {
            Log.Write($"Média, {reason} : affiché par le mini-lecteur ({state.AppName})");
            return;
        }
        Log.Write($"Média, {reason} : {state.Artist} — {state.Title} ({state.AppName})");
        Host.Notify(Card(state));
        EnsureTicking();
    }

    /// <summary>
    /// Écarté par « Musique uniquement » : pas de la musique, et l'application n'est pas réglée sur
    /// « Tout afficher » (radio web dans Chrome…).
    /// </summary>
    private bool NotMusicFiltered(MediaState state) =>
        state.NotMusic is not null && Host.Settings.MediaMusicOnly && !Host.Settings.MediaAlwaysShownApps.Contains(state.AppId);

    // ── Mini-lecteur (carte épinglée) et progression ───────────────────────────────────────

    /// <summary>Option « en permanence » au dernier réglage connu : seul son passage à « activé » fait apparaître la carte.</summary>
    private bool _pinnedSetting;

    public override void ApplySettings(Settings settings)
    {
        // Un changement d'un autre réglage ne doit ni relancer le mini-lecteur, ni le rouvrir s'il a été fermé.
        bool turnedOn = settings.MediaPinned && !_pinnedSetting;
        if (settings.MediaPinned != _pinnedSetting) _pinnedOverride = null;
        _pinnedSetting = settings.MediaPinned;
        UpdatePinned(trigger: turnedOn);
    }

    /// <summary>
    /// Raccourci clavier : masque le mini-lecteur, ou l'affiche — même si l'option « en permanence » est
    /// désactivée (mini-lecteur à la demande). Le choix tient jusqu'au prochain appui ou changement de réglages.
    /// </summary>
    public void TogglePinned()
    {
        if (_host is null) return;
        if (Host.IsPinned)
        {
            _pinnedOverride = false;
            Host.Pin(null);
            Log.Write("Mini-lecteur masqué (raccourci)");
            return;
        }
        _pinnedOverride = true;
        UpdatePinned(trigger: true);
        if (!Host.IsPinned)
            Host.Notify(new MessageCard("media-none", Glyphs.Music, L.T("Message.NothingPlaying"), L.T("Message.PlayerNextTrack")));
    }

    /// <summary>
    /// Mini-lecteur : la session qui joue (de préférence la dernière lancée), sinon la dernière active
    /// en pause ; retiré quand plus rien n'est visible. Après une fermeture par clic, il ne revient qu'au
    /// prochain titre ou à la reprise (<paramref name="trigger"/>).
    /// </summary>
    private void UpdatePinned(bool trigger)
    {
        if (_host is null) return;
        if (!(_pinnedOverride ?? Host.Settings.MediaPinned))
        {
            if (Host.IsPinned) Host.Pin(null);
            return;
        }
        var visible = Visible().ToList();
        var state = visible.FirstOrDefault(s => s.Playing && s.AppId == _activeApp)
            ?? visible.FirstOrDefault(s => s.Playing)
            ?? visible.FirstOrDefault(s => s.AppId == _activeApp)
            ?? visible.FirstOrDefault();
        if (state is null)
        {
            // Retrait différé : une session qui disparaît puis revient (navigateur, changement de titre)
            // ne doit pas fermer puis rouvrir le mini-lecteur.
            if (Host.IsPinned)
            {
                _unpin ??= new Timer(_ => _host?.Post(() =>
                {
                    if (_host is not null && Host.IsPinned && !Visible().Any()) Host.Pin(null);
                }));
                _unpin.Change(UnpinDelayMs, Timeout.Infinite);
            }
            return;
        }
        _unpin?.Change(Timeout.Infinite, Timeout.Infinite);
        if (Host.IsPinned || trigger) Host.Pin(Card(state));
        EnsureTicking();
    }

    /// <summary>Nouvelle position communiquée par l'application (avance, recul, reprise…).</summary>
    private void RefreshTimeline(Tracker tracker)
    {
        if (_host is null || tracker.State is not { } state || !_trackers.ContainsKey(tracker.AppId)) return;
        state = WithPending(tracker, state with { Timeline = ReadTimeline(tracker.Session) });
        tracker.State = state;
        Host.Update(Card(state));
        if (Host.PinnedCard is MediaCard { State.AppId: var pinned } && pinned == tracker.AppId) Host.Pin(Card(state));
        EnsureTicking();
    }

    /// <summary>Position et durée, ou null (direct, radio, application qui ne les communique pas).</summary>
    private static MediaTimeline? ReadTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var t = session.GetTimelineProperties();
            var duration = t.EndTime - t.StartTime;
            if (duration < TimeSpan.FromSeconds(1)) return null;
            // Horodatage absent ou incohérent chez certaines applications : on part de maintenant.
            var now = DateTime.UtcNow;
            var updated = t.LastUpdatedTime.UtcDateTime;
            if (updated > now || now - updated > TimeSpan.FromHours(12)) updated = now;
            return new MediaTimeline(t.Position - t.StartTime, duration, updated, t.StartTime);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Un redessin par seconde, et seulement tant qu'une carte affichée joue avec sa progression.</summary>
    private void EnsureTicking()
    {
        if (!NeedsTick()) return;
        _tick ??= new Timer(_ => _host?.Post(Tick));
        _tick.Change(1000, 1000);
    }

    private void Tick()
    {
        if (NeedsTick()) Host.Invalidate();
        else _tick?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private bool NeedsTick() => _host is { Settings.MediaShowProgress: true } host
        && (Ticking(host.CurrentNotification) || Ticking(host.PinnedCard));

    private static bool Ticking(CardContent? card) => card is MediaCard { State: { Playing: true, Timeline: not null } };

    private MediaCard Card(MediaState state)
        => new(state, Execute, Host.Settings.MediaSeekable ? Seek : null, progressRow: Host.Settings.MediaShowProgress);

    /// <summary>Clic sur la progression : position demandée à l'application (elle confirme par ses événements).</summary>
    private void Seek(MediaState state, TimeSpan position)
    {
        if (!_trackers.TryGetValue(state.AppId, out var tracker) || state.Timeline is not { } timeline) return;
        var session = tracker.Session;
        long ticks = (timeline.Start + position).Ticks;
        Log.Write($"Média : position {MediaTimeline.Format(position)} demandée ({state.AppName})");
        _ = Task.Run(async () =>
        {
            try
            {
                await session.TryChangePlaybackPositionAsync(ticks);
            }
            catch (Exception ex)
            {
                Log.Error("Changement de position", ex);
            }
        });
    }

    /// <summary>
    /// État vu par les cartes : la lecture / pause demandée prime tant que l'application ne l'a pas
    /// confirmée (ou au plus <see cref="PendingMs"/>) ; la progression la plus récente l'emporte.
    /// </summary>
    private static MediaState WithPending(Tracker tracker, MediaState state)
    {
        if (tracker.PendingPlaying is not bool wanted) return state;
        if (Environment.TickCount64 > tracker.PendingUntil)
        {
            tracker.PendingPlaying = null;
            tracker.PendingTimeline = null;
            return state;
        }
        var timeline = state.Timeline is { } real && tracker.PendingTimeline is { } pending && pending.UpdatedUtc > real.UpdatedUtc
            ? pending with { Duration = real.Duration, Start = real.Start }
            : state.Timeline;
        if (state.Playing == wanted)
        {
            // Confirmé par l'application.
            tracker.PendingPlaying = null;
            tracker.PendingTimeline = null;
        }
        return state with { Playing = wanted, Timeline = timeline };
    }

    /// <summary>Boutons des cartes : commande transmise à l'application qui joue.</summary>
    private void Execute(MediaState state, MediaCommand command)
    {
        if (!_trackers.TryGetValue(state.AppId, out var tracker)) return;
        if (command is MediaCommand.Next or MediaCommand.Previous) tracker.SkipUntil = Environment.TickCount64 + SkipWindowMs;
        if (command == MediaCommand.TogglePlayPause)
        {
            // La carte a déjà basculé : son état est l'état visé.
            tracker.PendingPlaying = state.Playing;
            tracker.PendingTimeline = state.Timeline;
            tracker.PendingUntil = Environment.TickCount64 + PendingMs;
            if (tracker.State is { } current) tracker.State = WithPending(tracker, current);
            // Autre carte de la même session affichée (mini-lecteur / popup) : même état tout de suite.
            if (tracker.State is { } shown)
            {
                Host.Update(Card(shown));
                if (Host.PinnedCard is MediaCard { State.AppId: var pinned } && pinned == tracker.AppId) Host.Pin(Card(shown));
            }
            EnsureTicking();
        }
        var session = tracker.Session;
        _ = Task.Run(async () =>
        {
            try
            {
                _ = command switch
                {
                    MediaCommand.Previous => await session.TrySkipPreviousAsync(),
                    MediaCommand.Next => await session.TrySkipNextAsync(),
                    _ => await session.TryTogglePlayPauseAsync(),
                };
            }
            catch (Exception ex)
            {
                Log.Error($"Commande média {command}", ex);
            }
        });
    }

    // ── Listes ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Sessions avec un titre, hors applications exclues et (option) hors vidéo ; celles qui jouent d'abord.</summary>
    private IEnumerable<MediaState> Visible() => _trackers.Values
        .Select(t => t.State)
        .OfType<MediaState>()
        .Where(s => _host is not null && !Host.Settings.MediaExcludedApps.Contains(s.AppId) && !NotMusicFiltered(s))
        .OrderByDescending(s => s.Playing);

    // Ouvrir la liste ou le survol resynchronise les sessions : un abonnement perdu se répare de lui-même.
    public override IEnumerable<CardContent> ListCards()
    {
        Sync();
        return Visible().Select(Card).ToList();
    }

    public override IEnumerable<HoverRow> HoverRows()
    {
        Sync();
        return Visible().Select(s => (HoverRow)new MediaRow(s, Execute)).ToList();
    }

    public override IEnumerable<CardContent> PreviewCards() =>
    [
        new MediaCard(new MediaState("preview", "Spotify", L.T("Preview.Track"), L.T("Preview.Artist"), true, true, true, true, null), (_, _) => { }),
    ];

    // ── Applications vues (liste d'exclusion de Hublot Core) ───────────────────────────────

    private void Remember(string id, string name)
    {
        var now = DateTime.Now;
        if (_apps.TryGetValue(id, out var known) && known.Name == name && (now - known.LastSeen).TotalHours < 1) return;
        _apps[id] = new MediaApp(id, name, now);
        _saveApps ??= new Timer(_ => _host?.Post(SaveApps));
        _saveApps.Change(3000, Timeout.Infinite);
    }

    private void SaveApps()
    {
        try
        {
            MediaStore.SaveApps(_apps.Values.OrderByDescending(a => a.LastSeen));
        }
        catch (Exception ex)
        {
            Log.Error("Applications média", ex);
        }
    }
}
