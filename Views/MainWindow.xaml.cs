using System.Windows;
using System.Windows.Input;
using YGODuelSimulator.Services;
using YGODuelSimulator.Views.Pages;

namespace YGODuelSimulator.Views
{
    /// <summary>
    /// The app shell: a Fluent window hosting the navigation rail. Individual
    /// screens live as pages under <see cref="Pages"/> and are shown inside the
    /// <c>NavigationView</c>.
    /// </summary>
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            // Keep the window theme / Mica backdrop in sync with the OS setting.
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

            // Pages have parameterless constructors, so a trivial provider is enough.
            RootNavigation.SetPageProviderService(new SimplePageProvider());

            // The card database is an admin-only area.
            if (!Session.IsAdmin) CardDatabaseNavItem.Visibility = Visibility.Collapsed;

            // Show who's signed in on the footer item that opens their profile.
            UserNavItem.Content = Session.CurrentUser?.Username ?? "Account";

            Loaded += (_, _) => RootNavigation.Navigate(typeof(HomePage));
        }

        // --- Full-screen mode ---

        private WindowState _preFullScreenState;
        private WindowStyle _preFullScreenStyle;
        private ResizeMode _preFullScreenResize;

        /// <summary>Whether the shell is currently borderless full-screen.</summary>
        public bool IsFullScreen { get; private set; }

        /// <summary>Enters or leaves borderless full-screen (hides the title bar and
        /// window chrome, covering the whole screen including the taskbar).</summary>
        public void SetFullScreen(bool on)
        {
            if (on == IsFullScreen) return;

            if (on)
            {
                _preFullScreenState = WindowState;
                _preFullScreenStyle = WindowStyle;
                _preFullScreenResize = ResizeMode;

                AppTitleBar.Visibility = Visibility.Collapsed;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                // Toggle through Normal first so a re-maximize takes effect and the
                // borderless window covers the taskbar.
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }
            else
            {
                AppTitleBar.Visibility = Visibility.Visible;
                WindowStyle = _preFullScreenStyle;
                ResizeMode = _preFullScreenResize;
                WindowState = _preFullScreenState;
            }

            IsFullScreen = on;
        }

        /// <summary>Flips between full-screen and windowed.</summary>
        public void ToggleFullScreen() => SetFullScreen(!IsFullScreen);

        // F11 toggles full-screen both ways. (Escape is deliberately left alone — the
        // Duel Room uses it to cancel attacks/placement.)
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        /// <summary>Navigates the rail to a page type. Used by the Home dashboard tiles.</summary>
        public void NavigateTo(Type pageType)
        {
            // Guard the admin-only card database against non-admin navigation.
            if (pageType == typeof(CardDatabasePage) && !Session.IsAdmin) return;
            RootNavigation.Navigate(pageType);
        }

        /// <summary>Signs out and returns to the login window. If the next sign-in is
        /// cancelled, the app exits.</summary>
        public void LogOut()
        {
            Session.CurrentUser = null;

            var login = new LoginWindow { Owner = this };
            if (login.ShowDialog() == true && login.AuthenticatedUser is { } user)
            {
                Session.CurrentUser = user;

                var splash = LoadingWindow.ShowOnBackgroundThread();
                var main = new MainWindow();
                // Re-home the app on the new shell first so closing this window (no
                // longer the MainWindow) doesn't trigger shutdown.
                Application.Current.MainWindow = main;
                main.ContentRendered += (_, _) => splash.Dismiss();
                main.Show();
                Close();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
    }
}
