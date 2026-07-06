using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>App settings. For now: a manual light/dark theme override on top of
    /// the shell's default "follow the OS" behavior.</summary>
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

            _initialized = true;
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
