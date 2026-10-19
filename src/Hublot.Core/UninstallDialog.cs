using System.Windows;
using System.Windows.Controls;
using Hublot.Common;

namespace HublotCore;

/// <summary>
/// Confirmation de la désinstallation, avec le choix de conserver les paramètres (réglages, appareils,
/// applications de lecture) pour une prochaine installation. Coché par défaut : rien n'est perdu sans le vouloir.
/// </summary>
internal sealed class UninstallDialog : Window
{
    private readonly CheckBox _keep;

    /// <summary>Paramètres à conserver (lu après une confirmation).</summary>
    public bool KeepSettings => _keep.IsChecked == true;

    public UninstallDialog()
    {
        Title = "Hublot";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        _keep = new CheckBox { Content = L.T("Uninstall.KeepSettings"), IsChecked = true, Margin = new Thickness(0, 16, 0, 0) };
        var uninstall = new Button { Content = L.T("Uninstall.Button"), IsDefault = true, Padding = new Thickness(16, 6, 16, 6), MinWidth = 110 };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, Padding = new Thickness(16, 6, 16, 6), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        uninstall.Click += (_, _) => DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(uninstall);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(24), MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = L.T("Uninstall.Confirm"), FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_keep);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
