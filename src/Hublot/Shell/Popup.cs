using System.Diagnostics;
using Hublot.Common;
using Hublot.Interop;

namespace Hublot.Shell;

internal enum PopupMode
{
    /// <summary>Une carte d'événement (appareil, nouveau titre…), fermeture automatique.</summary>
    Notification,
    /// <summary>Une carte par élément (raccourci clavier, clic sur l'icône), fermeture automatique.</summary>
    List,
    /// <summary>Carte du survol de l'icône de notification, tant que la souris est sur l'icône ou la carte.</summary>
    Hover,
    /// <summary>
    /// Carte du lecteur, à son propre emplacement : permanente (pas de fermeture automatique) ou le temps
    /// d'une notification (<see cref="Popup.ShowPinned"/> avec <c>temporary</c>).
    /// </summary>
    Pinned,
}

/// <summary>Fenêtre invisible qui cadence les images (une par composition de l'écran) et porte les minuteurs.</summary>
internal sealed class PopupClock : NativeWindow
{
    private const uint WM_FRAME = Msg.WM_APP + 10;

    public event Action? Frame;
    public event Action<nuint>? Timer;

    public PopupClock() => CreateHandle("Hublot.Clock", User32.WS_EX_TOOLWINDOW, 0);

    public void RequestFrame() => User32.PostMessageW(Handle, WM_FRAME, 0, 0);

    public void Start(nuint id, uint milliseconds) => User32.SetTimer(Handle, id, milliseconds, 0);

    public void Stop(nuint id) => User32.KillTimer(Handle, id);

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_FRAME)
        {
            Frame?.Invoke();
            return 0;
        }
        if (msg == Msg.WM_TIMER)
        {
            Timer?.Invoke((nuint)wParam);
            return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}

/// <summary>
/// Moteur de popup partagé par tous les modules : fenêtres, matériau, placement, chorégraphie
/// d'entrée / sortie, fermeture automatique, souris. Le contenu des cartes vient des modules
/// (<see cref="CardContent"/>) ; ce contrôleur ne sait rien des appareils ni des médias.
///
/// Chorégraphie (durées × vitesse choisie) : entrée par glissement depuis le bord le plus proche
/// + fondu (480 ms, courbe Fluent « decelerate »), cartes décalées de 45 ms ; sortie par fondu
/// + léger recul (220 ms, « accelerate »). Les animations internes des cartes (icône, jauge…)
/// sont gérées par les cartes. Images cadencées sur la composition de l'écran (DwmFlush), et
/// seulement pendant une animation : au repos, aucun minuteur ne tourne.
/// </summary>
internal sealed class Popup
{
    private const nuint DismissTimer = 1;
    private const uint AfterHoverMs = 2000;
    private const double EnterMs = 480, FadeInMs = 220, LeaveMs = 220, StaggerMs = 45, SnapMs = 300;
    /// <summary>Déplacement (DIP) au-delà duquel un appui devient un glissement : un clic garde son effet.</summary>
    private const float DragThresholdDip = 6;

    private enum Phase { Hidden, Entering, Shown, Leaving }

    private sealed class Card(CardContent content, PopupSurface surface, Placement placement)
    {
        public CardContent Content = content;
        public PopupSurface Surface { get; } = surface;
        public Placement Placement { get; set; } = placement;
        public long EnterStart;
        public int Hover = -1;
    }

    private readonly PopupClock _clock = new();
    private readonly List<LayeredSurface> _layered = [];
    private readonly List<BackdropSurface> _backdrop = [];
    private List<Card> _cards = [];
    private Settings _settings;
    private PopupMode _mode;
    private Phase _phase = Phase.Hidden;
    private long _phaseStart;
    private int _slideX, _slideY, _hovered;
    private bool _pumping;
    // Glissement à la souris (carte épinglée) : appui en cours, point d'appui à l'écran, décalage suivi ;
    // puis calage animé sur le nouvel emplacement (décalage de départ qui revient à zéro).
    private bool _pressed, _dragging, _snapping;
    /// <summary>Carte du lecteur affichée le temps d'une notification (fermeture automatique), et non en permanence.</summary>
    private bool _temporary;
    private POINT _pressAt;
    private int _dragX, _dragY, _snapX, _snapY;
    private long _snapStart;

    public Popup(Settings settings)
    {
        _settings = settings;
        _clock.Frame += OnFrame;
        _clock.Timer += id =>
        {
            if (id != DismissTimer) return;
            _clock.Stop(DismissTimer);
            Dismiss();
        };
    }

    /// <summary>Le popup vient d'être masqué (ressources graphiques libérées).</summary>
    public event Action? Hidden;

    /// <summary>
    /// Carte épinglée déplaçable à la souris. Lâchée : emplacement le plus proche (tiers de l'écran en largeur,
    /// moitié en hauteur) et écran visé ; le propriétaire l'enregistre puis appelle <see cref="Settle"/>.
    /// </summary>
    public bool Draggable { get; set; }

    public event Action<PopupPosition, nint>? Dropped;

    /// <summary>Clic droit sur une carte : menu contextuel (modal), fermeture automatique suspendue pendant ce temps.</summary>
    public event Action<Popup, CardContent>? ContextMenuRequested;

    public bool IsVisible => _phase != Phase.Hidden;

    public PopupMode? Mode => IsVisible ? _mode : null;

    /// <summary>Carte d'événement affichée (null pour une liste, un survol, ou si rien n'est affiché).</summary>
    public CardContent? Notification =>
        _phase is Phase.Entering or Phase.Shown && _mode == PopupMode.Notification && _cards.Count == 1 ? _cards[0].Content : null;

    /// <summary>Nouveaux paramètres : pris en compte au prochain affichage.</summary>
    public void ApplySettings(Settings settings) => _settings = settings;

    /// <summary>Emplacement propre à ce popup (carte épinglée), à la place de celui des paramètres.</summary>
    public PopupPosition? PositionOverride { get; set; }

    /// <summary>Écran propre à ce popup (carte épinglée), à la place de celui des paramètres.</summary>
    public (PopupMonitor Kind, string Device)? MonitorOverride { get; set; }

    /// <summary>
    /// Place occupée au même emplacement par une carte épinglée (DIP) : les cartes de ce popup
    /// s'empilent au-delà au lieu de la recouvrir.
    /// </summary>
    public float ReservedDip { get; set; }

    /// <summary>Carte épinglée affichée, s'il y en a une.</summary>
    public CardContent? PinnedCard => _phase is Phase.Entering or Phase.Shown && _mode == PopupMode.Pinned ? _cards[0].Content : null;

    /// <summary>
    /// Carte du lecteur : en permanence, ou le temps d'une notification (<paramref name="temporary"/> : fermeture
    /// automatique comme une carte d'événement). Une carte de même taille déjà visible est remplacée sur place
    /// (nouveau titre, lecture / pause…, ou passage du temporaire au permanent).
    /// </summary>
    public void ShowPinned(CardContent content, bool temporary = false)
    {
        if (PinnedCard is { } current && current.Width == content.Width && current.Height == content.Height)
        {
            content.ContinueFrom(current, Stopwatch.GetTimestamp(), Clock, replay: false);
            _cards[0].Content = content;
            _temporary = temporary;
            if (temporary) RestartDismiss();
            else _clock.Stop(DismissTimer);
            Pump();
            return;
        }
        _temporary = temporary;
        Present([content], PopupMode.Pinned, null);
    }

    private AnimClock Clock => new(_settings.Animation != PopupAnimation.None,
        _settings.SpeedFactor * (_mode == PopupMode.Hover ? 0.6f : 1f));

    /// <summary>
    /// Carte d'événement. Si une carte d'événement de même taille est déjà affichée, elle est
    /// remplacée sur place (animations internes rejouées) au lieu de rejouer l'entrée.
    /// </summary>
    public void Show(CardContent content)
    {
        if (Notification is { } current && current.Width == content.Width && current.Height == content.Height)
        {
            long now = Stopwatch.GetTimestamp();
            content.ContinueFrom(current, now, Clock, replay: true);
            _cards[0].Content = content;
            _cards[0].Hover = -1;
            RestartDismiss();
            Pump();
            return;
        }
        Present([content], PopupMode.Notification, null);
    }

    /// <summary>Une carte par élément (raccourci clavier, clic sur l'icône).</summary>
    /// <param name="longer">Liste à parcourir (appareils, lecture) : 3 s de plus que la durée d'affichage.</param>
    public void ShowList(IReadOnlyList<CardContent> contents, bool longer = true)
    {
        _listLonger = longer;
        Present(contents, PopupMode.List, null);
    }

    private bool _listLonger = true;

    /// <summary>
    /// Survol de l'icône : une carte au-dessus de l'icône (ou du menu des icônes cachées qui la contient).
    /// Reste ouverte tant que la souris est sur l'icône ou sur la carte.
    /// </summary>
    public void ShowHover(CardContent content, RECT icon)
    {
        if (_mode == PopupMode.Hover && _phase is Phase.Entering or Phase.Shown)
        {
            _clock.Stop(DismissTimer);
            return;
        }
        Present([content], PopupMode.Hover, icon);
    }

    /// <summary>La souris a quitté l'icône : fermeture, sauf si elle est passée sur la carte.</summary>
    public void EndTrayHover()
    {
        if (_mode == PopupMode.Hover && _phase is Phase.Entering or Phase.Shown && _hovered == 0)
            _clock.Start(DismissTimer, 400);
    }

    /// <summary>Complément d'information pour une carte affichée (même clé), sans rejouer l'entrée.</summary>
    public void Update(CardContent content)
    {
        if (_phase is Phase.Hidden or Phase.Leaving) return;
        var card = _cards.FirstOrDefault(c => c.Content.Key == content.Key
            && c.Content.Width == content.Width && c.Content.Height == content.Height);
        if (card is null) return;
        content.ContinueFrom(card.Content, Stopwatch.GetTimestamp(), Clock, replay: false);
        card.Content = content;
        Pump();
    }

    /// <summary>Masque immédiatement, sans animation.</summary>
    public void Reset()
    {
        _clock.Stop(DismissTimer);
        foreach (var card in _cards) card.Surface.Conceal();
        _cards = [];
        _hovered = 0;
        _phase = Phase.Hidden;
        _pressed = _dragging = _snapping = false;
        _dragX = _dragY = 0;
    }

    public void Dismiss()
    {
        if (_phase is Phase.Hidden or Phase.Leaving) return;
        _clock.Stop(DismissTimer);
        _phase = Phase.Leaving;
        _phaseStart = Stopwatch.GetTimestamp();
        Pump();
    }

    private void Present(IReadOnlyList<CardContent> contents, PopupMode mode, RECT? trayIcon)
    {
        Reset();
        if (contents.Count == 0) return;
        _mode = mode;

        bool backdrop = _settings.Material != PopupMaterial.Solid && Dwm.SupportsSystemBackdrop;
        var theme = Theme.Resolve(_settings);
        var placements = trayIcon is RECT icon
            ? [HoverPlacement(contents[0], icon)]
            : ComputeLayout(contents);
        long now = Stopwatch.GetTimestamp();
        var clock = Clock;
        var cards = new List<Card>(contents.Count);
        for (int i = 0; i < contents.Count; i++)
        {
            var surface = Rent(backdrop, i);
            surface.Prepare(theme, _settings, placements[i]);
            var card = new Card(contents[i], surface, placements[i]) { EnterStart = now + clock.Ticks(i * StaggerMs) };
            contents[i].Begin(card.EnterStart, clock);
            cards.Add(card);
        }
        _cards = cards;
        _phase = Phase.Entering;
        _phaseStart = now;
        RestartDismiss();
        Pump();
    }

    /// <summary>Surfaces réutilisées d'un affichage à l'autre (une fenêtre par carte).</summary>
    private PopupSurface Rent(bool backdrop, int index)
    {
        if (backdrop)
        {
            while (_backdrop.Count <= index) _backdrop.Add(Wire(new BackdropSurface()));
            return _backdrop[index];
        }
        while (_layered.Count <= index) _layered.Add(Wire(new LayeredSurface()));
        return _layered[index];
    }

    private T Wire<T>(T surface) where T : PopupSurface
    {
        surface.Input += OnInput;
        return surface;
    }

    private void OnInput(PopupSurface surface, SurfaceInput input, int x, int y)
    {
        var card = _cards.FirstOrDefault(c => c.Surface == surface);
        switch (input)
        {
            case SurfaceInput.HoverStart:
                _hovered++;
                _clock.Stop(DismissTimer); // reste affiché tant que la souris survole une carte
                break;

            case SurfaceInput.Press when card is not null:
                _pressed = true;
                _pressAt = CursorPos();
                break;

            case SurfaceInput.Move when card is not null:
                if (_pressed && Drag(card)) break;
                int hit = card.Content.HitTest(x / card.Placement.Scale, y / card.Placement.Scale);
                if (hit != card.Hover)
                {
                    card.Hover = hit;
                    Pump();
                }
                break;

            case SurfaceInput.HoverEnd:
                _hovered = Math.Max(0, _hovered - 1);
                if (card is not null && card.Hover != -1)
                {
                    card.Hover = -1;
                    Pump();
                }
                if (_hovered == 0 && (_mode != PopupMode.Pinned || _temporary) && _phase is Phase.Entering or Phase.Shown)
                    _clock.Start(DismissTimer, _mode == PopupMode.Hover ? 350 : AfterHoverMs);
                break;

            case SurfaceInput.Release when card is not null:
                bool pressed = _pressed;
                _pressed = false;
                if (_dragging) Drop(card);
                else if (pressed) ClickCard(card, x, y);
                break;

            case SurfaceInput.Cancel:
                _pressed = false;
                if (_dragging && card is not null) Drop(card);
                break;

            case SurfaceInput.Click when card is not null:
                _clock.Stop(DismissTimer);
                ContextMenuRequested?.Invoke(this, card.Content);
                if (_hovered == 0 && _phase is Phase.Entering or Phase.Shown) RestartDismiss();
                break;
        }
    }

    private void ClickCard(Card card, int x, int y)
    {
        int part = card.Content.HitTest(x / card.Placement.Scale, y / card.Placement.Scale);
        if (part < 0) return;
        if (card.Content.Click(part)) Dismiss();
        else Pump(); // l'état a pu changer (lecture / pause…)
    }

    private static unsafe POINT CursorPos()
    {
        POINT p;
        User32.GetCursorPos(&p);
        return p;
    }

    /// <summary>Appui en cours : la carte épinglée suit la souris une fois le seuil franchi (vrai si elle la suit).</summary>
    private bool Drag(Card card)
    {
        if (!Draggable || _mode != PopupMode.Pinned || _phase is not (Phase.Entering or Phase.Shown)) return false;
        var p = CursorPos();
        int dx = p.X - _pressAt.X, dy = p.Y - _pressAt.Y;
        if (!_dragging)
        {
            float threshold = DragThresholdDip * card.Placement.Scale;
            if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold) return false;
            _dragging = true;
            card.Hover = -1;
            // Calage encore en cours : la carte repart de là où elle est affichée.
            var (sx, sy) = SnapOffset(Stopwatch.GetTimestamp(), Clock);
            _snapping = false;
            _pressAt = new POINT(_pressAt.X - sx, _pressAt.Y - sy);
            (dx, dy) = (dx + sx, dy + sy);
        }
        (_dragX, _dragY) = (dx, dy);
        Pump();
        return true;
    }

    /// <summary>Carte lâchée : emplacement le plus proche de son centre, sur l'écran où elle se trouve.</summary>
    private void Drop(Card card)
    {
        _dragging = false;
        var pl = card.Placement;
        var center = new POINT(pl.X + _dragX + pl.Width / 2, pl.Y + _dragY + pl.Height / 2);
        var (monitor, _, r) = MonitorAt(center);
        int third = (r.Right - r.Left) / 3;
        bool top = center.Y < (r.Top + r.Bottom) / 2;
        var position = center.X < r.Left + third ? (top ? PopupPosition.TopLeft : PopupPosition.BottomLeft)
            : center.X >= r.Right - third ? (top ? PopupPosition.TopRight : PopupPosition.BottomRight)
            : top ? PopupPosition.TopCenter : PopupPosition.BottomCenter;
        if (Dropped is { } dropped) dropped(position, monitor);
        else Settle();
    }

    /// <summary>
    /// Carte épinglée reposée à son emplacement (après un glissement, <see cref="PositionOverride"/> et
    /// <see cref="MonitorOverride"/> à jour) : elle y glisse en douceur depuis là où elle a été lâchée.
    /// </summary>
    public void Settle()
    {
        if (_mode != PopupMode.Pinned || _cards.Count == 0 || _phase is Phase.Hidden or Phase.Leaving)
        {
            _dragX = _dragY = 0;
            return;
        }
        var card = _cards[0];
        int fromX = card.Placement.X + _dragX, fromY = card.Placement.Y + _dragY;
        var target = ComputeLayout([card.Content])[0];
        if (target != card.Placement)
        {
            card.Placement = target;
            card.Surface.Prepare(Theme.Resolve(_settings), _settings, target);
        }
        _dragX = _dragY = 0;
        (_snapX, _snapY, _snapStart, _snapping) = (fromX - target.X, fromY - target.Y, Stopwatch.GetTimestamp(), true);
        // Tout de suite : Prepare a déjà déplacé la fenêtre, elle est redessinée au point de départ du calage.
        OnFrame();
    }

    /// <summary>Décalage restant du calage (pixels), qui revient à zéro ; zéro une fois arrivé.</summary>
    private (int X, int Y) SnapOffset(long now, AnimClock clock)
    {
        if (!_snapping) return (0, 0);
        double s = clock.Animate ? Easing.Clamp01(clock.Elapsed(_snapStart, now) / SnapMs) : 1;
        if (s >= 1)
        {
            _snapping = false;
            return (0, 0);
        }
        double k = 1 - Easing.Decelerate(s);
        return ((int)Math.Round(_snapX * k), (int)Math.Round(_snapY * k));
    }

    private void RestartDismiss()
    {
        if (_mode == PopupMode.Hover || _mode == PopupMode.Pinned && !_temporary || _hovered > 0) return;
        int seconds = _settings.DurationSeconds + (_mode == PopupMode.List && _listLonger ? 3 : 0);
        _clock.Start(DismissTimer, (uint)seconds * 1000);
    }

    /// <summary>Redessine les cartes affichées (changement d'état venu d'un module).</summary>
    public void Invalidate() => Pump();

    /// <summary>
    /// Thème, couleur d'accentuation ou opacité changés : les cartes affichées sont repeintes sur place (même
    /// matériau, même position), sans rejouer leur entrée — le mini-lecteur suit la bascule clair / sombre.
    /// </summary>
    public void Restyle()
    {
        if (_phase is Phase.Hidden or Phase.Leaving) return;
        var theme = Theme.Resolve(_settings);
        foreach (var card in _cards) card.Surface.Prepare(theme, _settings, card.Placement);
        Pump();
    }

    private void Pump()
    {
        if (_pumping || _phase == Phase.Hidden) return;
        _pumping = true;
        _clock.RequestFrame();
    }

    /// <summary>Une image pour toutes les cartes ; en redemande une autre tant que quelque chose bouge.</summary>
    private void OnFrame()
    {
        _pumping = false;
        if (_phase == Phase.Hidden || _cards.Count == 0) return;

        long now = Stopwatch.GetTimestamp();
        var clock = Clock;
        bool slide = _settings.Animation == PopupAnimation.Slide;
        bool busy = false;

        double leave = 0;
        if (_phase == Phase.Leaving)
        {
            double l = clock.Elapsed(_phaseStart, now);
            if (!clock.Animate || l >= LeaveMs)
            {
                Reset();
                Hidden?.Invoke();
                return;
            }
            leave = Easing.Accelerate(l / LeaveMs);
            busy = true;
        }

        // Glissement à la souris, puis calage (carte épinglée).
        var (snapX, snapY) = SnapOffset(now, clock);
        if (_snapping) busy = true;
        int extraX = _dragX + snapX, extraY = _dragY + snapY;

        bool entering = false;
        foreach (var card in _cards)
        {
            double opacity = 1, offset = 0;
            if (_phase == Phase.Leaving)
            {
                opacity = 1 - leave;
                offset = slide ? 0.5 * leave : 0;
            }
            else if (_phase == Phase.Entering && clock.Animate)
            {
                double e = clock.Elapsed(card.EnterStart, now);
                if (e < EnterMs)
                {
                    entering = true;
                    opacity = 1 - Math.Pow(1 - Easing.Clamp01(e / FadeInMs), 2);
                    offset = slide ? 1 - Easing.Decelerate(Easing.Clamp01(e / EnterMs)) : 0;
                }
            }

            if (card.Content.IsAnimating(now, clock)) busy = true;
            var content = card.Content;
            int hover = card.Hover;
            var settings = _settings;
            card.Surface.Render((c, r, t, s) => content.Draw(new CardContext(c, r, t, s, now, clock, hover, settings)),
                opacity, (int)Math.Round(_slideX * offset) + extraX, (int)Math.Round(_slideY * offset) + extraY);
        }

        if (_phase == Phase.Entering)
        {
            if (entering) busy = true;
            else _phase = Phase.Shown;
        }

        if (busy)
        {
            Dwm.DwmFlush(); // attend la prochaine composition : animation calée sur l'écran
            Pump();
        }
    }

    private unsafe (nint Monitor, float DpiScale, RECT Work) MonitorAt(POINT? point)
    {
        nint monitor;
        if (point is POINT p)
        {
            monitor = User32.MonitorFromPoint(p, User32.MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            var (kind, device) = MonitorOverride ?? (_settings.Monitor, _settings.MonitorDevice);
            if (kind == PopupMonitor.Cursor)
            {
                POINT cursor;
                User32.GetCursorPos(&cursor);
                monitor = User32.MonitorFromPoint(cursor, User32.MONITOR_DEFAULTTONEAREST);
            }
            else if (kind == PopupMonitor.Specific && Displays.Active().FirstOrDefault(d => d.Path == device) is { } display)
            {
                // Écran choisi, retrouvé par son identifiant ; débranché : écran principal.
                monitor = User32.MonitorFromPoint(new POINT(display.Left + display.Width / 2, display.Top + display.Height / 2), User32.MONITOR_DEFAULTTOPRIMARY);
            }
            else
            {
                monitor = User32.MonitorFromPoint(default, User32.MONITOR_DEFAULTTOPRIMARY);
            }
        }
        uint dpiX = 96, dpiY = 96;
        Shcore.GetDpiForMonitor(monitor, 0, &dpiX, &dpiY);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(monitor, &info);
        return (monitor, dpiX / 96f, info.rcWork); // zone de travail : la barre des tâches est exclue, où qu'elle soit
    }

    /// <summary>
    /// Carte du survol : centrée sur l'icône, juste au-dessus de la fenêtre qui la contient — la barre
    /// des tâches, ou le menu des icônes cachées s'il est ouvert —, donc sans jamais le masquer.
    /// En dessous si la barre est en haut de l'écran, à côté si elle est sur un côté.
    /// </summary>
    private unsafe Placement HoverPlacement(CardContent content, RECT icon)
    {
        var center = new POINT((icon.Left + icon.Right) / 2, (icon.Top + icon.Bottom) / 2);
        var (_, dpiScale, r) = MonitorAt(center);
        float scale = dpiScale * _settings.SizeFactor;
        int w = (int)MathF.Round(content.Width * scale), h = (int)MathF.Round(content.Height * scale);
        int gap = (int)MathF.Round(8 * dpiScale), m = gap, v = (int)MathF.Round(16 * scale);

        RECT anchor = icon;
        var host = User32.GetAncestor(User32.WindowFromPoint(center), User32.GA_ROOT);
        if (host != 0) User32.GetWindowRect(host, &anchor);

        int x = Math.Clamp(center.X - w / 2, r.Left + m, r.Right - m - w);
        int y;
        if (anchor.Right <= r.Left || anchor.Left >= r.Right)
        {
            bool left = anchor.Right <= r.Left; // barre des tâches verticale : à côté, centrée sur l'icône
            x = left ? r.Left + m : r.Right - m - w;
            y = Math.Clamp(center.Y - h / 2, r.Top + m, r.Bottom - m - h);
            (_slideX, _slideY) = (left ? -v : v, 0);
        }
        else if (anchor.Top - gap - h >= r.Top)
        {
            y = anchor.Top - gap - h;
            (_slideX, _slideY) = (0, v);
        }
        else
        {
            y = Math.Min(anchor.Bottom + gap, r.Bottom - m - h);
            (_slideX, _slideY) = (0, -v);
        }
        return new Placement(x, y, w, h, scale);
    }

    /// <summary>
    /// Rectangles des cartes (tailles variables selon le module) et sens du glissement. Positions en
    /// coin : cartes empilées depuis le coin (nouvelle colonne si elles ne tiennent pas en hauteur) ;
    /// positions centrées : côte à côte (nouvelle rangée si elles ne tiennent pas en largeur).
    /// </summary>
    private List<Placement> ComputeLayout(IReadOnlyList<CardContent> contents)
    {
        var (_, dpiScale, r) = MonitorAt(null);
        float scale = dpiScale * _settings.SizeFactor;
        int m = (int)MathF.Round(_settings.Margin * dpiScale);
        int gap = (int)MathF.Round(8 * dpiScale);
        int d = (int)MathF.Round(56 * scale), v = (int)MathF.Round(24 * scale);
        var sizes = contents.Select(c => ((int)MathF.Round(c.Width * scale), (int)MathF.Round(c.Height * scale))).ToList();
        var list = new List<Placement>(contents.Count);

        var pos = PositionOverride ?? _settings.Position;
        bool top = pos is PopupPosition.TopLeft or PopupPosition.TopCenter or PopupPosition.TopRight;
        bool center = pos is PopupPosition.TopCenter or PopupPosition.BottomCenter;
        bool left = pos is PopupPosition.TopLeft or PopupPosition.BottomLeft;

        if (center)
        {
            // Rangées côte à côte, centrées ; les rangées suivantes vers le milieu de l'écran.
            int i = 0, rowOffset = (int)MathF.Round(ReservedDip * scale);
            while (i < sizes.Count)
            {
                int width = 0, height = 0, start = i;
                while (i < sizes.Count && (i == start || width + gap + sizes[i].Item1 <= r.Right - r.Left - 2 * m))
                {
                    width += (i == start ? 0 : gap) + sizes[i].Item1;
                    height = Math.Max(height, sizes[i].Item2);
                    i++;
                }
                int x = (r.Left + r.Right - width) / 2;
                for (int k = start; k < i; k++)
                {
                    var (w, h) = sizes[k];
                    int y = top ? r.Top + m + rowOffset : r.Bottom - m - rowOffset - h;
                    list.Add(new Placement(x, y, w, h, scale));
                    x += w + gap;
                }
                rowOffset += height + gap;
            }
            (_slideX, _slideY) = (0, top ? -v : v);
        }
        else
        {
            // Pile depuis le coin ; colonne suivante vers le milieu de l'écran.
            int offsetY = (int)MathF.Round(ReservedDip * scale), offsetX = 0, columnWidth = 0;
            foreach (var (w, h) in sizes)
            {
                if (offsetY > 0 && offsetY + h > r.Bottom - r.Top - 2 * m)
                {
                    offsetX += columnWidth + gap;
                    offsetY = columnWidth = 0;
                }
                int x = left ? r.Left + m + offsetX : r.Right - m - offsetX - w;
                int y = top ? r.Top + m + offsetY : r.Bottom - m - offsetY - h;
                list.Add(new Placement(x, y, w, h, scale));
                offsetY += h + gap;
                columnWidth = Math.Max(columnWidth, w);
            }
            (_slideX, _slideY) = (left ? -d : d, 0);
        }
        return list;
    }
}
