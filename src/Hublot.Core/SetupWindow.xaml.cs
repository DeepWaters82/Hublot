using System.Diagnostics;
using Hublot.Common;
using System.Windows;

namespace HublotCore;

/// <summary>Installation ou mise à jour en un clic (Hublot-Setup.exe lancé hors du dossier d'installation).</summary>
public partial class SetupWindow : Window
{
    private bool _done;

    public SetupWindow()
    {
        InitializeComponent();
        VersionText.Text = L.F(Installer.IsInstalled ? "Setup.Update" : "Core.Version", Installer.Version);
        InstallButton.Content = L.T(Installer.IsInstalled ? "Setup.UpdateButton" : "Installer");
        StartupBox.IsChecked = !Installer.IsInstalled || Native.StartupEnabled;
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            Process.Start(new ProcessStartInfo(Installer.SettingsPath) { UseShellExecute = true })?.Dispose();
            Close();
            return;
        }

        InstallButton.IsEnabled = CloseButton.IsEnabled = StartupBox.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        bool startup = StartupBox.IsChecked == true;
        try
        {
            await Task.Run(() => Installer.Install(startup, text => Dispatcher.Invoke(() => StatusText.Text = text)));
            _done = true;
            StatusText.Text = L.T("Setup.Done");
            InstallButton.Content = L.T("Setup.OpenSettings");
            CloseButton.Content = L.T("Setup.Close");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Setup.Failed", ex.Message);
            InstallButton.Content = L.T("Setup.Retry");
        }
        finally
        {
            Progress.Visibility = Visibility.Hidden;
            InstallButton.IsEnabled = CloseButton.IsEnabled = true;
            StartupBox.IsEnabled = !_done;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
