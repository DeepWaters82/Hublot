using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Hublot.Common;

namespace HublotCore;

/// <summary>
/// « Identifier » : le numéro de chaque écran, son nom et sa connectique dans son coin supérieur gauche
/// pendant 2,5 s, comme dans Paramètres › Affichage. Les numéros sont ceux des listes d'écrans.
/// </summary>
internal static class ScreenIdentifier
{
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    public static void Show(IReadOnlyList<DisplayInfo> displays)
    {
        var windows = new List<Window>();
        for (int i = 0; i < displays.Count; i++)
        {
            var display = displays[i];
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = SettingsViewModel.DisplayNumber(display, i).ToString(),
                FontSize = 72, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{display.Name} · {display.Connector}", FontSize = 14, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = display.Mode, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            });
            var window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                Topmost = true, ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight, Left = -32000, Top = -32000,
                Content = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(32, 12, 32, 18),
                    Background = (Brush)Application.Current.FindResource("WindowsBlue"), Child = panel,
                },
            };
            // Position en pixels physiques de l'écran visé (coordonnées du bureau virtuel).
            window.SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(window).Handle, 0,
                display.Left + 48, display.Top + 48, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            window.Show();
            windows.Add(window);
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var window in windows) window.Close();
        };
        timer.Start();
    }
}
