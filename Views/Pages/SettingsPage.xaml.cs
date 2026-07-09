using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using YGODuelSimulator.Services;
using YGODuelSimulator.Views;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>App settings: a manual light/dark theme override on top of the shell's
    /// "follow the OS" default, plus the app version and a manual update check.</summary>
    public partial class SettingsPage : Page
    {
        private readonly bool _initialized;

        public SettingsPage()
        {
            InitializeComponent();

            // Reflect the currently applied theme without re-applying it.
            if (ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark)
                ThemeDarkRadio.IsChecked = true;
            else
                ThemeLightRadio.IsChecked = true;

            VersionText.Text = $"Version {UpdateService.CurrentVersion}";

            _initialized = true;
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Visibility = Visibility.Visible;

            if (!UpdateService.IsInstalled)
            {
                UpdateStatusText.Text = "Updates are only available in installed builds.";
                CheckUpdatesButton.IsEnabled = true;
                return;
            }

            UpdateStatusText.Text = "Checking…";
            try
            {
                if (await UpdateService.CheckAsync() is { } pending)
                {
                    // Same mandatory window as the login gate; here a quit just cancels.
                    new UpdateWindow(pending.mgr, pending.info) { Owner = Window.GetWindow(this) }.ShowDialog();
                    UpdateStatusText.Text = "Update available.";
                }
                else
                {
                    UpdateStatusText.Text = "You're on the latest version.";
                }
            }
            catch (Exception ex)
            {
                // Surface the real reason instead of a misleading "up to date".
                UpdateStatusText.Text = $"Couldn't check for updates: {ex.Message}";
            }
            CheckUpdatesButton.IsEnabled = true;
        }

        private void ThemeSystemRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_initialized) ApplyTheme(SystemToApplicationTheme(ApplicationThemeManager.GetSystemTheme()));
        }

        private void ThemeLightRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_initialized) ApplyTheme(ApplicationTheme.Light);
        }

        private void ThemeDarkRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_initialized) ApplyTheme(ApplicationTheme.Dark);
        }

        private static void ApplyTheme(ApplicationTheme theme) => ApplicationThemeManager.Apply(theme);

        private static ApplicationTheme SystemToApplicationTheme(SystemTheme system) =>
            system == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }
}
