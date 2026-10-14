using Hublot.Common;
using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>
/// Fenêtre hôte invisible : icône de la zone de notification (clic, menu, survol), raccourci clavier
/// global, messages système (thème, veille, redémarrage de l'Explorateur, utilitaire de paramètres)
/// et file de travail du thread UI.
/// </summary>
internal sealed unsafe class TrayIcon : NativeWindow
{
    private const uint WM_TRAY = Msg.WM_APP + 1;
    private const uint WM_WORK = Msg.WM_APP + 2;
    private const uint WM_APPBAR = Msg.WM_APP + 3;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint NIN_POPUPOPEN = Msg.WM_USER + 6, NIN_POPUPCLOSE = Msg.WM_USER + 7;
    private const nuint IdList = 1, IdExit = 2, IdSettings = 3, IdDisplays = 4;
    private const nuint TrimTimer = 1, HoverTimer = 2;
    /// <summary>
    /// Attente ajoutée au délai d'info-bulle du système avant d'ouvrir la carte du survol : un simple
    /// passage de la souris sur l'icône ne doit rien afficher.
    /// </summary>
    private const uint HoverDelayMs = 450;
    /// <summary>Raccourcis globaux : liste des éléments, mini-lecteur.</summary>
    public const int ListHotkey = 1, PinnedHotkey = 2;

    private readonly uint _taskbarCreated, _settingsChanged, _settingsPreview, _updateAvailable;
    private nint _icon;

    public event Action? ListRequested;
    public event Action<string?>? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? WorkPending;
    /// <summary>L'utilitaire de réglages a enregistré de nouveaux paramètres.</summary>
    public event Action? SettingsChanged;
    /// <summary>Hublot Core demande un aperçu (voir <see cref="Settings.PreviewMessage"/>).</summary>
    public event Action<int>? SettingsPreviewRequested;
    /// <summary>Survol de l'icône (délai d'info-bulle du système + <see cref="HoverDelayMs"/>) / fin du survol.</summary>
    public event Action? HoverStarted;
    public event Action? HoverEnded;
    /// <summary>Raccourci global pressé (<see cref="ListHotkey"/>, <see cref="PinnedHotkey"/>).</summary>
    public event Action<int>? HotkeyPressed;
    /// <summary>Mise en veille (false) / sortie de veille (true).</summary>
    public event Action<bool>? PowerChanged;
    /// <summary>Une application passe en plein écran (true) ou en sort (false).</summary>
    public event Action<bool>? FullscreenChanged;
    /// <summary>Configuration des écrans modifiée (diffusé à toutes les fenêtres de premier niveau).</summary>
    public event Action? DisplayChanged;
    /// <summary>Menu : une carte par écran connecté.</summary>
    public event Action? DisplaysRequested;
    /// <summary>Mode clair / sombre ou couleur d'accentuation de Windows modifiés.</summary>
    public event Action? ThemeChanged;
    /// <summary>Hublot Core a trouvé une nouvelle version (« 3.1.0 »).</summary>
    public event Action<string>? UpdateAvailable;

    public TrayIcon()
    {
        CreateHandle(Settings.HostWindowClass, User32.WS_EX_TOOLWINDOW, 0);
        _taskbarCreated = User32.RegisterWindowMessageW("TaskbarCreated");
        _settingsChanged = User32.RegisterWindowMessageW(Settings.ChangedMessage);
        _settingsPreview = User32.RegisterWindowMessageW(Settings.PreviewMessage);
        _updateAvailable = User32.RegisterWindowMessageW(Settings.UpdateMessage);
        EnableDarkMenus();
    }

    public void Add()
    {
        UpdateIconImage();
        // Sans NIF_SHOWTIP, l'info-bulle standard laisse place à NIN_POPUPOPEN / NIN_POPUPCLOSE.
        var data = Data(Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP);
        Shell32.Shell_NotifyIconW(Shell32.NIM_ADD, &data);
        data.uVersion = Shell32.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(Shell32.NIM_SETVERSION, &data);
    }

    private bool _appBar;

    /// <summary>
    /// Suivi du plein écran par la notification ABN_FULLSCREENAPP du shell (barre d'application déclarée,
    /// sans position ni place réservée) : aucun sondage. Activé seulement quand une carte épinglée existe.
    /// </summary>
    public void WatchFullscreen(bool enable)
    {
        if (enable == _appBar) return;
        var data = new Shell32.APPBARDATA { cbSize = (uint)sizeof(Shell32.APPBARDATA), hWnd = Handle, uCallbackMessage = WM_APPBAR };
        Shell32.SHAppBarMessage(enable ? Shell32.ABM_NEW : Shell32.ABM_REMOVE, &data);
        _appBar = enable;
    }

    public void Remove()
    {
        WatchFullscreen(false);
        var data = Data(0);
        Shell32.Shell_NotifyIconW(Shell32.NIM_DELETE, &data);
        if (_icon != 0) User32.DestroyIcon(_icon);
        _icon = 0;
        User32.UnregisterHotKey(Handle, ListHotkey);
        User32.UnregisterHotKey(Handle, PinnedHotkey);
    }

    /// <summary>(Ré)enregistre le raccourci global ; faux si une autre application l'utilise déjà.</summary>
    public bool SetHotkey(int id, bool enabled, int modifiers, int key)
    {
        User32.UnregisterHotKey(Handle, id);
        if (!enabled || key == 0) return true;
        return User32.RegisterHotKey(Handle, id, (uint)modifiers | User32.MOD_NOREPEAT, (uint)key) != 0;
    }

    /// <summary>Rectangle de l'icône à l'écran (pour placer les cartes au survol).</summary>
    public RECT? GetIconRect()
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER), hWnd = Handle, uID = 1 };
        RECT rect;
        return Shell32.Shell_NotifyIconGetRect(&id, &rect) == 0 ? rect : null;
    }

    /// <summary>Réveille le thread UI depuis n'importe quel thread.</summary>
    public void Wake() => User32.PostMessageW(Handle, WM_WORK, 0, 0);

    /// <summary>Planifie un allègement mémoire une fois l'application au repos.</summary>
    public void ScheduleTrim(uint delayMs) => User32.SetTimer(Handle, TrimTimer, delayMs, 0);

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
        "Hublot".AsSpan().CopyTo(new Span<char>(data.szTip, 127));
        return data;
    }

    private void RefreshIcon()
    {
        UpdateIconImage();
        var data = Data(Shell32.NIF_ICON);
        Shell32.Shell_NotifyIconW(Shell32.NIM_MODIFY, &data);
    }

    private void UpdateIconImage()
    {
        uint dpi = User32.GetDpiForSystem();
        int size = User32.GetSystemMetricsForDpi(User32.SM_CXSMICON, dpi);
        nint old = _icon;
        using (var canvas = TrayIconRenderer.Draw(size))
            _icon = canvas.ToIcon();
        if (old != 0) User32.DestroyIcon(old);
    }

    private void ShowMenu(int x, int y)
    {
        var menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, User32.MF_STRING | User32.MF_GRAYED, 0, "Hublot");
        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, User32.MF_STRING, IdList, L.T("Tray.Devices"));
        // Un seul écran : rien à comparer, l'entrée n'apparaît pas.
        if (Displays.Active().Count > 1) User32.AppendMenuW(menu, User32.MF_STRING, IdDisplays, L.T("Tray.Displays"));
        User32.AppendMenuW(menu, User32.MF_STRING, IdSettings, L.T("Tray.Settings"));
        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, User32.MF_STRING, IdExit, L.T("Tray.Quit"));

        // Contournement classique : sans premier plan, le menu ne se ferme pas en cliquant ailleurs.
        User32.SetForegroundWindow(Handle);
        int command = User32.TrackPopupMenuEx(menu, User32.TPM_RIGHTBUTTON | User32.TPM_RETURNCMD, x, y, Handle, 0);
        User32.PostMessageW(Handle, Msg.WM_NULL, 0, 0);
        User32.DestroyMenu(menu);

        switch ((nuint)command)
        {
            case IdList: ListRequested?.Invoke(); break;
            case IdDisplays: DisplaysRequested?.Invoke(); break;
            case IdSettings: SettingsRequested?.Invoke(null); break;
            case IdExit: ExitRequested?.Invoke(); break;
        }
    }

    /// <summary>
    /// Menus Win32 sombres quand Windows est en mode sombre (uxtheme, ordinaux 135 SetPreferredAppMode
    /// et 136 FlushMenuThemes — API non documentée mais stable depuis Windows 10 1903).
    /// </summary>
    private static void EnableDarkMenus()
    {
        var uxtheme = Kernel32.LoadLibraryW("uxtheme.dll");
        if (uxtheme == 0) return;
        var setPreferredAppMode = (delegate* unmanaged<int, int>)Kernel32.GetProcAddress(uxtheme, 135);
        var flushMenuThemes = (delegate* unmanaged<void>)Kernel32.GetProcAddress(uxtheme, 136);
        if (setPreferredAppMode != null) setPreferredAppMode(1); // AllowDark : suit le mode des applications
        if (flushMenuThemes != null) flushMenuThemes();
    }

    /// <summary>Fin du survol, ou survol trop bref pour avoir ouvert la carte.</summary>
    private void CancelHover()
    {
        User32.KillTimer(Handle, HoverTimer);
        HoverEnded?.Invoke();
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_WORK:
                WorkPending?.Invoke();
                return 0;

            case WM_DISPLAYCHANGE:
                DisplayChanged?.Invoke();
                return 0;

            case WM_APPBAR when (uint)wParam == Shell32.ABN_FULLSCREENAPP:
                FullscreenChanged?.Invoke(lParam != 0);
                return 0;

            case WM_TRAY:
                switch ((uint)LoWord(lParam) & 0xFFFF)
                {
                    case Msg.WM_CONTEXTMENU:
                        CancelHover();
                        ShowMenu(LoWord(wParam), HiWord(wParam));
                        break;
                    case Msg.NIN_SELECT:
                    case Msg.NIN_KEYSELECT:
                        User32.KillTimer(Handle, HoverTimer);
                        ListRequested?.Invoke();
                        break;
                    case NIN_POPUPOPEN:
                        User32.SetTimer(Handle, HoverTimer, HoverDelayMs, 0);
                        break;
                    case NIN_POPUPCLOSE:
                        CancelHover();
                        break;
                }
                return 0;

            case User32.WM_HOTKEY when wParam is ListHotkey or PinnedHotkey:
                HotkeyPressed?.Invoke((int)wParam);
                return 0;

            case User32.WM_POWERBROADCAST:
                if (wParam == User32.PBT_APMSUSPEND) PowerChanged?.Invoke(false);
                else if (wParam is User32.PBT_APMRESUMESUSPEND or User32.PBT_APMRESUMEAUTOMATIC) PowerChanged?.Invoke(true);
                return 1;

            case Msg.WM_TIMER when (nuint)wParam == HoverTimer:
                User32.KillTimer(Handle, HoverTimer);
                HoverStarted?.Invoke();
                return 0;

            case Msg.WM_TIMER when (nuint)wParam == TrimTimer:
                User32.KillTimer(Handle, TrimTimer);
                Memory.Trim();
                return 0;

            case Msg.WM_SETTINGCHANGE when lParam != 0 && new string((char*)lParam) == "ImmersiveColorSet":
                // Bascule clair/sombre, couleur d'accentuation : icône redessinée, cartes affichées repeintes.
                RefreshIcon();
                ThemeChanged?.Invoke();
                return 0;

            case Msg.WM_ENDSESSION when wParam != 0:
                Remove();
                return 0;
        }

        if (msg != 0 && msg == _settingsChanged)
        {
            SettingsChanged?.Invoke();
            return 0;
        }
        if (msg != 0 && msg == _settingsPreview)
        {
            SettingsPreviewRequested?.Invoke((int)wParam);
            return 0;
        }
        if (msg != 0 && msg == _updateAvailable)
        {
            long v = wParam;
            UpdateAvailable?.Invoke($"{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}");
            return 0;
        }
        if (msg != 0 && msg == _taskbarCreated)
        {
            Add(); // l'Explorateur a redémarré : la zone de notification est vierge
            return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}
