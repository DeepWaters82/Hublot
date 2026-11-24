using System.Windows;
using System.Windows.Controls;
using Hublot.Common;

namespace HublotCore;

/// <summary>
/// Durée personnalisée de Caféine (menu de la tasse › Autre durée…) : heures et minutes, puis la durée est envoyée
/// au service (<see cref="Native.PostCaffeine"/>). <paramref name="shutdown"/> : « éteindre le PC dans ».
/// </summary>
internal sealed class CaffeineDurationDialog : Window
{
    private readonly ComboBox _hours = new() { Width = 90 }, _minutes = new() { Width = 90 };
    private readonly TextBlock _error = new() { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly bool _shutdown;

    public CaffeineDurationDialog(bool shutdown)
    {
        _shutdown = shutdown;
        Title = L.T("Caf.Name");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        for (int h = 0; h <= 23; h++) _hours.Items.Add($"{h} h");
        for (int m = 0; m < 60; m += 5) _minutes.Items.Add($"{m} min");
        _hours.SelectedIndex = 1;
        _minutes.SelectedIndex = 0;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        row.Children.Add(_hours);
        row.Children.Add(new TextBlock { Width = 12 });
        row.Children.Add(_minutes);

        var start = new Button { Content = L.T("Caf.Dialog.Start"), IsDefault = true, Padding = new Thickness(16, 6, 16, 6), MinWidth = 110 };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, Padding = new Thickness(16, 6, 16, 6), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        start.Click += (_, _) => Start();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(start);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(24), MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = L.T(shutdown ? "Caf.Dialog.Shutdown" : "Caf.Dialog.Timed"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(row);
        panel.Children.Add(_error);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void Start()
    {
        int minutes = _hours.SelectedIndex * 60 + _minutes.SelectedIndex * 5;
        if (minutes <= 0)
        {
            ShowError(L.T("Caf.Dialog.Zero"));
            return;
        }
        if (!Native.PostCaffeine(_shutdown ? 2 : 1, minutes))
        {
            ShowError(L.T("Caf.Dialog.NotRunning"));
            return;
        }
        DialogResult = true;
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }
}
