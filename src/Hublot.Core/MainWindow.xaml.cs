using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hublot.Common;

namespace HublotCore;

public partial class MainWindow : Window
{
    private static readonly Dictionary<PopupPosition, string> PositionNames = new()
    {
        [PopupPosition.TopLeft] = L.T("EnHautAGauche"),
        [PopupPosition.TopCenter] = L.T("EnHautAuCentre"),
        [PopupPosition.TopRight] = L.T("EnHautADroite"),
        [PopupPosition.BottomLeft] = L.T("EnBasAGauche"),
        [PopupPosition.BottomCenter] = L.T("EnBasAuCentre"),
        [PopupPosition.BottomRight] = L.T("EnBasADroite"),
    };

    private readonly SettingsViewModel _vm = new();
    private readonly DispatcherTimer _status = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, FrameworkElement> _pages;
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();
        SizeChanged += (_, _) =>
        {
            UpdateCompactLayout();
            Dispatcher.BeginInvoke(() => MovePill(animate: false), DispatcherPriority.Loaded);
        };
        Loaded += (_, _) => MovePill(animate: false);
        Nav.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => MovePill(animate: false)));
        DataContext = _vm;
        _vm.LanguageChanged += () => Dispatcher.BeginInvoke(() => ((App)Application.Current).Restart("General"));
        _pages = new()
        {
            ["Home"] = HomePage,
            ["General"] = GeneralPage,
            ["NowPlaying"] = NowPlayingPage,
            ["Usb"] = UsbPage,
            ["Power"] = PowerPage,
            ["Displays"] = DisplaysPage,
            ["Notifications"] = NotificationsPage,
            ["Appearance"] = AppearancePage,
            ["Devices"] = DevicesPage,
            ["About"] = AboutPage,
        };

        foreach (var spot in Spots.Children.OfType<RadioButton>())
            spot.IsChecked = spot.Tag as string == _vm.Position.ToString();
        SpotLabel.Text = PositionNames[_vm.Position];

        if (!SettingsViewModel.SystemMaterialsSupported)
        {
            AcrylicItem.IsEnabled = MicaItem.IsEnabled = false;
            AcrylicItem.Content = L.T("Core.AcrylicNeeds");
            MicaItem.Content = L.T("Core.MicaNeeds");
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = L.F("Core.Version", version?.ToString(3) ?? "");
        NoBatteryText.Visibility = Native.HasBattery ? Visibility.Collapsed : Visibility.Visible;

        UpdatesSection.Visibility = UpdateChecker.Available ? Visibility.Visible : Visibility.Collapsed;
        _status.Tick += (_, _) => RefreshStatus();
        // Version Store : ouvrir Hublot depuis le menu Démarrer lance aussi le service (premier lancement compris).
        if (AppPackage.IsPackaged && !Native.ServiceRunning) Native.StartService();
        _status.Start();
        RefreshStatus();
        _loading = false;
    }

    /// <summary>
    /// Taille d'ouverture bornée à l'écran : 1000 × 740 DIP dépassent la zone de travail d'un portable
    /// (1366 × 768, ou 1920 × 1080 à 150 % = 1280 × 720 DIP). L'écran est celui sous la souris, comme
    /// pour le centrage de WPF.
    /// </summary>
    private void FitToScreen()
    {
        var area = Native.WorkAreaAtCursor();
        Width = Math.Min(Width, Math.Max(480, area.Width - 48));
        Height = Math.Min(Height, Math.Max(360, area.Height - 48));
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
    }

    private bool? _compact;

    /// <summary>Fenêtre étroite : volet de navigation réduit aux icônes (libellés en info-bulle), marges resserrées.</summary>
    private void UpdateCompactLayout()
    {
        bool compact = ActualWidth < 860;
        if (_compact == compact) return;
        _compact = compact;
        NavColumn.Width = new GridLength(compact ? 68 : 250);
        NavTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (ListBoxItem item in Nav.Items)
        {
            if (item.Content is StackPanel panel && panel.Children.Count > 1 && panel.Children[1] is TextBlock label)
            {
                label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                item.ToolTip = compact ? label.Text : null;
            }
            else if (item.Content is TextBlock)
            {
                item.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; // intertitre « Modules »
            }
        }
        PageContent.Margin = compact ? new Thickness(20, 20, 20, 28) : new Thickness(40, 28, 40, 40);
        // Les trois boutons d'aperçu passent sous le titre.
        DockPanel.SetDock(AppearanceActions, compact ? Dock.Bottom : Dock.Right);
        AppearanceActions.Margin = new Thickness(0, compact ? 12 : 0, 0, 0);
    }

    // ── Barre de sélection du menu (comme NavigationView de Windows 11) ──────────────────────
    private const double PillHeight = 16, PillMs = 350;
    private double _pillTop = double.NaN, _fromTop, _toTop;
    private DateTime _pillStart;
    private bool _pillAnimating;

    /// <summary>
    /// Place la barre sur l'élément sélectionné. Animée : l'extrémité vers la destination part la première,
    /// l'autre suit (la barre s'étire puis se rétracte), comme dans Windows ; sinon, saut direct.
    /// </summary>
    private void MovePill(bool animate)
    {
        if (Nav.SelectedItem is not ListBoxItem item || !item.IsLoaded || !NavOverlay.IsLoaded) return;
        Point origin;
        try
        {
            // La surcouche est à côté de la liste, pas au-dessus : TransformToVisual (pas TransformToAncestor).
            origin = item.TransformToVisual(NavOverlay).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return; // élément pas encore relié à l'arbre visuel : la prochaine mise en page replacera la barre
        }
        double top = origin.Y + (item.ActualHeight - PillHeight) / 2;
        Canvas.SetLeft(NavPill, origin.X);
        NavPill.Visibility = Visibility.Visible;
        if (!animate || double.IsNaN(_pillTop) || Math.Abs(top - _pillTop) < 0.5 || !SystemParameters.ClientAreaAnimation)
        {
            StopPill();
            SetPill(top, top + PillHeight);
            return;
        }
        (_fromTop, _toTop, _pillStart) = (_pillTop, top, DateTime.UtcNow);
        if (!_pillAnimating)
        {
            _pillAnimating = true;
            CompositionTarget.Rendering += OnPillFrame;
        }
    }

    private void OnPillFrame(object? sender, EventArgs e)
    {
        double t = Math.Min(1, (DateTime.UtcNow - _pillStart).TotalMilliseconds / PillMs);
        // Extrémité de tête : 0 → 70 % du temps ; de queue : 30 → 100 %. Courbe « decelerate » de Fluent.
        double lead = Decelerate(Math.Min(1, t / 0.7)), trail = Decelerate(Math.Max(0, (t - 0.3) / 0.7));
        double fromBottom = _fromTop + PillHeight, toBottom = _toTop + PillHeight;
        bool down = _toTop > _fromTop;
        double top = _fromTop + (_toTop - _fromTop) * (down ? trail : lead);
        double bottom = fromBottom + (toBottom - fromBottom) * (down ? lead : trail);
        SetPill(top, bottom);
        if (t >= 1) StopPill();
    }

    private void SetPill(double top, double bottom)
    {
        _pillTop = top;
        Canvas.SetTop(NavPill, top);
        NavPill.Height = Math.Max(PillHeight, bottom - top);
    }

    private void StopPill()
    {
        if (!_pillAnimating) return;
        _pillAnimating = false;
        CompositionTarget.Rendering -= OnPillFrame;
    }

    private static double Decelerate(double t) => 1 - Math.Pow(1 - t, 3);

    // ── Mises à jour (version classique) ─────────────────────────────────────────────────────
    private UpdateChecker.Release? _release;

    /// <summary>Recherche sur GitHub ; si une version plus récente existe, le bouton devient « Installer ».</summary>
    private async void OnCheckUpdate(object? sender, RoutedEventArgs? e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            if (_release is { } release)
            {
                UpdateStatus.Text = L.T("Update.Downloading");
                await UpdateChecker.InstallAsync(release); // l'installateur ferme Hublot et le met à jour
                return;
            }
            UpdateStatus.Text = L.T("Update.Checking");
            var latest = await UpdateChecker.LatestAsync();
            if (latest is not null && latest.Version > UpdateChecker.Current)
            {
                _release = latest;
                UpdateStatus.Text = L.F("Update.NewVersion", latest.Version);
                UpdateButton.Content = L.T("Update.Install");
            }
            else
            {
                UpdateStatus.Text = L.T("Update.UpToDate");
            }
        }
        catch (Exception)
        {
            UpdateStatus.Text = L.T("Update.Failed");
        }
        UpdateButton.IsEnabled = true;
    }

    /// <summary>Page affichée à l'ouverture (argument --page, depuis le menu de l'icône).</summary>
    public void ShowPage(string page)
    {
        foreach (ListBoxItem item in Nav.Items)
            if ((string)item.Tag == page) item.IsSelected = true;
        // Ouvert depuis le popup « Mise à jour disponible » : la recherche est lancée d'office.
        if (page == "About" && UpdateChecker.Available) OnCheckUpdate(null, null);
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (_pages is null || Nav.SelectedItem is not ListBoxItem item) return;
        MovePill(animate: true);
        string page = (string)item.Tag;
        foreach (var (name, element) in _pages)
            element.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        Scroller.ScrollToTop();
        if (page == "Devices") _vm.RefreshDevices();
        if (page == "NowPlaying")
        {
            _vm.RefreshMediaApps();
            EmptyMediaApps.Visibility = _vm.HasMediaApps ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OnSpot(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse<PopupPosition>(tag, out var position)) return;
        if (SpotLabel is not null) SpotLabel.Text = PositionNames[position];
        if (!_loading) _vm.Position = position;
    }

    /// <summary>Capture d'une combinaison : au moins un modificateur + une touche.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab or Key.Escape) return;

        int modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= Settings.ModControl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= Settings.ModAlt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= Settings.ModShift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= Settings.ModWin;
        if (modifiers == 0) return;

        // Même saisie pour les deux raccourcis : Tag="Pinned" = celui du mini-lecteur.
        if (sender is FrameworkElement { Tag: "Pinned" }) _vm.SetPinnedHotkey(modifiers, KeyInterop.VirtualKeyFromKey(key));
        else _vm.SetHotkey(modifiers, KeyInterop.VirtualKeyFromKey(key));
    }

    private void OnPreview(object sender, RoutedEventArgs e) => _vm.Preview(0);

    private void OnPreviewList(object sender, RoutedEventArgs e) => _vm.Preview(1);

    private void OnPreviewMedia(object sender, RoutedEventArgs e) => _vm.Preview(2);

    private void OnPreviewUsb(object sender, RoutedEventArgs e) => _vm.Preview(3);

    private void OnPreviewPower(object sender, RoutedEventArgs e) => _vm.Preview(4);

    private void OnPreviewDisplays(object sender, RoutedEventArgs e) => _vm.Preview(5);

    /// <summary>Numéro, nom et connectique sur chaque écran : pour savoir lequel choisir.</summary>
    private void OnIdentifyScreens(object sender, RoutedEventArgs e) => ScreenIdentifier.Show(_vm.ActiveDisplays);

    /// <summary>Accueil : bouton « Configurer » d'un module.</summary>
    private void OnConfigure(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string page }) ShowPage(page);
    }

    private void OnOpenDevices(object sender, RoutedEventArgs e) => ShowPage("Devices");

    private void OnRefreshDevices(object sender, RoutedEventArgs e) => _vm.RefreshDevices();


    private void OnStartService(object sender, RoutedEventArgs e)
    {
        if (!Native.StartService())
            MessageBox.Show(this, L.F("Core.ServiceMissing", Native.ServicePath), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshStatus();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Settings.DirectoryPath);
        // Version Store : le dossier réel est celui du paquet (l'Explorateur ne voit pas la redirection).
        string folder = AppPackage.DataFolderForExplorer;
        Process.Start(new ProcessStartInfo(Directory.Exists(folder) ? folder : Settings.DirectoryPath) { UseShellExecute = true })?.Dispose();
    }

    private void RefreshStatus()
    {
        bool running = Native.ServiceRunning;
        StatusTitle.Text = L.T(running ? "Core.Running" : "Core.NotRunning");
        StatusText.Text = L.T(running ? "Core.AppliedNow" : "Core.AppliedNext");
        StatusDot.Fill = new SolidColorBrush(running ? Color.FromRgb(0x6C, 0xCB, 0x5F) : Color.FromRgb(0x9E, 0x9E, 0x9E));
        StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
    }
}
