using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Caffeine;

/// <summary>
/// Fenêtre invisible de Caféine : sa tasse dans la zone de notification (clic : marche / arrêt, clic droit : menu,
/// survol : carte du temps restant), ses raccourcis globaux, l’animation de fumée, et la durée personnalisée
/// envoyée par Hublot Core (<see cref="Settings.CaffeineMessage"/>). Tout se passe sur le thread UI.
/// </summary>
internal sealed unsafe class CaffeineTray : NativeWindow
{
    private const uint WM_TRAY = Msg.WM_APP + 1;
    private const uint NIN_POPUPOPEN = Msg.WM_USER + 6, NIN_POPUPCLOSE = Msg.WM_USER + 7;
    private const nuint HoverTimer = 1, SteamTimer = 2;
    private const uint HoverDelayMs = 450, SteamFrameMs = 60;
    private const double SteamMs = 1600;

    /// <summary>Raccourcis : sans limite (bascule), pendant une durée, extinction, désactiver.</summary>
    public const int HotkeyIndefinite = 1, HotkeyTimed = 2, HotkeyShutdown = 3, HotkeyOff = 4;

    // Menu
    private const nuint IdIndefinite = 10, IdOff = 11, IdDisplay = 12, IdSettings = 13, IdTimedCustom = 30, IdShutdownCustom = 50;
    private const nuint IdTimedBase = 20, IdShutdownBase = 40;
    public static readonly int[] TimedPresets = [15, 30, 60, 120, 240, 480];
    public static readonly int[] ShutdownPresets = [15, 30, 60, 120, 240];

    private readonly uint _taskbarCreated, _caffeineMessage;
    private readonly Func<CaffeineSnapshot> _state;
    private nint _icon;
    private bool _added;
    private long _steamStart;

    public event Action<CaffeineMode, int>? ModeRequested;      // mode, minutes (0 = sans limite / désactiver)
    public event Action<CaffeineMode>? CustomDurationRequested; // Timed ou Shutdown
    public event Action? KeepDisplayToggled;
    public event Action? SettingsRequested;
    public event Action<int>? HotkeyPressed;
    public event Action? HoverStarted;
    public event Action? HoverEnded;
    public event Action? ThemeChanged;

    public CaffeineTray(Func<CaffeineSnapshot> state)
    {
        _state = state;
        CreateHandle(Settings.CaffeineWindowClass, User32.WS_EX_TOOLWINDOW, 0);
        _taskbarCreated = User32.RegisterWindowMessageW("TaskbarCreated");
        _caffeineMessage = User32.RegisterWindowMessageW(Settings.CaffeineMessage);
    }

    public void Add()
    {
        UpdateIconImage();
        var data = Data(Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP);
        Shell32.Shell_NotifyIconW(Shell32.NIM_ADD, &data);
        data.uVersion = Shell32.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(Shell32.NIM_SETVERSION, &data);
        _added = true;
    }

    public void Remove()
    {
        User32.KillTimer(Handle, SteamTimer);
        User32.KillTimer(Handle, HoverTimer);
        for (int id = HotkeyIndefinite; id <= HotkeyOff; id++) User32.UnregisterHotKey(Handle, id);
        if (_added)
        {
            var data = Data(0);
            Shell32.Shell_NotifyIconW(Shell32.NIM_DELETE, &data);
            _added = false;
        }
        if (_icon != 0) User32.DestroyIcon(_icon);
        _icon = 0;
    }

    public bool SetHotkey(int id, bool enabled, int packed)
    {
        User32.UnregisterHotKey(Handle, id);
        int key = packed & 0xFFFF, modifiers = packed >> 16;
        if (!enabled || key == 0) return true;
        return User32.RegisterHotKey(Handle, id, (uint)modifiers | User32.MOD_NOREPEAT, (uint)key) != 0;
    }

    public RECT? GetIconRect()
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER), hWnd = Handle, uID = 1 };
        RECT rect;
        return Shell32.Shell_NotifyIconGetRect(&id, &rect) == 0 ? rect : null;
    }

    /// <summary>Tasse redessinée (mode, niveau, thème).</summary>
    public void Refresh()
    {
        if (!_added) return;
        UpdateIconImage();
        var data = Data(Shell32.NIF_ICON);
        Shell32.Shell_NotifyIconW(Shell32.NIM_MODIFY, &data);
    }

    /// <summary>Fumée qui monte au-dessus de la tasse (1,6 s, une image toutes les 60 ms) : à l’activation.</summary>
    public void AnimateSteam()
    {
        _steamStart = Environment.TickCount64;
        User32.SetTimer(Handle, SteamTimer, SteamFrameMs, 0);
        Refresh();
    }

    private float SteamPhase
    {
        get
        {
            long elapsed = Environment.TickCount64 - _steamStart;
            return elapsed < SteamMs && _steamStart != 0 ? (float)(elapsed / SteamMs) : -1;
        }
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
        };
        L.T("Caf.Name").AsSpan().CopyTo(new Span<char>(data.szTip, 127));
        return data;
    }

    private void UpdateIconImage()
    {
        uint dpi = User32.GetDpiForSystem();
        int size = User32.GetSystemMetricsForDpi(User32.SM_CXSMICON, dpi);
        var s = _state();
        var (ink, accent) = CaffeineIcon.TrayColors();
        uint fill = s.Mode == CaffeineMode.Shutdown ? CaffeineIcon.ShutdownColor(Theme.SystemUsesLightTheme()) : accent;
        nint old = _icon;
        _icon = CaffeineIcon.Render(size, s.Level, fill, ink, s.Mode == CaffeineMode.Off ? -1 : SteamPhase);
        if (old != 0) User32.DestroyIcon(old);
    }

    private static string Preset(int minutes) => minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h";

    private void ShowMenu(int x, int y)
    {
        var s = _state();
        string header = s.Mode switch
        {
            CaffeineMode.Indefinite => L.T("Caf.Name") + " — " + L.T("Caf.NoLimit"),
            CaffeineMode.Timed or CaffeineMode.Shutdown when s.Remaining is { } r => L.T("Caf.Name") + " — " + CaffeineCard.Format(r),
            _ => L.T("Caf.Name"),
        };
        var menu = User32.CreatePopupMenu();
        var timed = User32.CreatePopupMenu();
        var shutdown = User32.CreatePopupMenu();
        for (int i = 0; i < TimedPresets.Length; i++) User32.AppendMenuW(timed, User32.MF_STRING, IdTimedBase + (nuint)i, Preset(TimedPresets[i]));
        User32.AppendMenuW(timed, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(timed, User32.MF_STRING, IdTimedCustom, L.T("Caf.Menu.Custom"));
        for (int i = 0; i < ShutdownPresets.Length; i++) User32.AppendMenuW(shutdown, User32.MF_STRING, IdShutdownBase + (nuint)i, Preset(ShutdownPresets[i]));
        User32.AppendMenuW(shutdown, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(shutdown, User32.MF_STRING, IdShutdownCustom, L.T("Caf.Menu.Custom"));

        User32.AppendMenuW(menu, User32.MF_STRING | User32.MF_GRAYED, 0, header);
        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, User32.MF_STRING | (s.Mode == CaffeineMode.Indefinite ? User32.MF_CHECKED : 0), IdIndefinite, L.T("Caf.Menu.Indefinite"));
        User32.AppendMenuW(menu, User32.MF_POPUP | (s.Mode == CaffeineMode.Timed ? User32.MF_CHECKED : 0), (nuint)timed, L.T("Caf.Menu.Timed"));
        User32.AppendMenuW(menu, User32.MF_POPUP | (s.Mode == CaffeineMode.Shutdown ? User32.MF_CHECKED : 0), (nuint)shutdown, L.T("Caf.Menu.Shutdown"));
        User32.AppendMenuW(menu, User32.MF_STRING | (s.Mode == CaffeineMode.Off ? User32.MF_GRAYED : 0), IdOff, L.T("Caf.Menu.Off"));
        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, User32.MF_STRING | (s.KeepDisplay ? User32.MF_CHECKED : 0), IdDisplay, L.T("Caf.Menu.Display"));
        User32.AppendMenuW(menu, User32.MF_STRING, IdSettings, L.T("Caf.Menu.Settings"));

        User32.SetForegroundWindow(Handle);
        int command = User32.TrackPopupMenuEx(menu, User32.TPM_RIGHTBUTTON | User32.TPM_RETURNCMD, x, y, Handle, 0);
        User32.PostMessageW(Handle, Msg.WM_NULL, 0, 0);
        User32.DestroyMenu(menu); // détruit aussi les sous-menus rattachés

        var id = (nuint)command;
        if (id == IdIndefinite) ModeRequested?.Invoke(s.Mode == CaffeineMode.Indefinite ? CaffeineMode.Off : CaffeineMode.Indefinite, 0);
        else if (id == IdOff) ModeRequested?.Invoke(CaffeineMode.Off, 0);
        else if (id == IdDisplay) KeepDisplayToggled?.Invoke();
        else if (id == IdSettings) SettingsRequested?.Invoke();
        else if (id == IdTimedCustom) CustomDurationRequested?.Invoke(CaffeineMode.Timed);
        else if (id == IdShutdownCustom) CustomDurationRequested?.Invoke(CaffeineMode.Shutdown);
        else if (id >= IdTimedBase && id < IdTimedBase + (nuint)TimedPresets.Length) ModeRequested?.Invoke(CaffeineMode.Timed, TimedPresets[(int)(id - IdTimedBase)]);
        else if (id >= IdShutdownBase && id < IdShutdownBase + (nuint)ShutdownPresets.Length) ModeRequested?.Invoke(CaffeineMode.Shutdown, ShutdownPresets[(int)(id - IdShutdownBase)]);
    }

    private void CancelHover()
    {
        User32.KillTimer(Handle, HoverTimer);
        HoverEnded?.Invoke();
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_TRAY:
                switch ((uint)LoWord(lParam) & 0xFFFF)
                {
                    case Msg.WM_CONTEXTMENU:
                        CancelHover();
                        ShowMenu(LoWord(wParam), HiWord(wParam));
                        break;
                    case Msg.NIN_SELECT:
                    case Msg.NIN_KEYSELECT:
                        // Clic : marche (sans limite) / arrêt.
                        CancelHover();
                        ModeRequested?.Invoke(_state().Mode == CaffeineMode.Off ? CaffeineMode.Indefinite : CaffeineMode.Off, 0);
                        break;
                    case NIN_POPUPOPEN:
                        User32.SetTimer(Handle, HoverTimer, HoverDelayMs, 0);
                        break;
                    case NIN_POPUPCLOSE:
                        CancelHover();
                        break;
                }
                return 0;

            case User32.WM_HOTKEY when wParam is >= HotkeyIndefinite and <= HotkeyOff:
                HotkeyPressed?.Invoke((int)wParam);
                return 0;

            case Msg.WM_TIMER when (nuint)wParam == HoverTimer:
                User32.KillTimer(Handle, HoverTimer);
                HoverStarted?.Invoke();
                return 0;

            case Msg.WM_TIMER when (nuint)wParam == SteamTimer:
                if (SteamPhase < 0)
                {
                    User32.KillTimer(Handle, SteamTimer);
                    _steamStart = 0;
                }
                Refresh();
                return 0;

            case Msg.WM_SETTINGCHANGE when lParam != 0 && new string((char*)lParam) == "ImmersiveColorSet":
                Refresh();
                ThemeChanged?.Invoke();
                return 0;
        }
        if (msg != 0 && msg == _caffeineMessage)
        {
            int minutes = (int)lParam;
            if (minutes > 0) ModeRequested?.Invoke(wParam == 2 ? CaffeineMode.Shutdown : CaffeineMode.Timed, minutes);
            return 0;
        }
        if (msg != 0 && msg == _taskbarCreated)
        {
            _added = false;
            Add(); // l’Explorateur a redémarré : la zone de notification est vierge
            return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}
