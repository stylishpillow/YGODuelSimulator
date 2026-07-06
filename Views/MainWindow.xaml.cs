using System.Windows;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
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

            Loaded += MainWindow_Loaded;
        }

        /// <summary>Navigates the rail to a page type. Used by the Home dashboard tiles.</summary>
        public void NavigateTo(Type pageType) => RootNavigation.Navigate(pageType);

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // First run: unpack the bundled card database if there's none yet, then
            // make sure the schema is current. Done once here so it happens no matter
            // which page the user opens first.
            DatabaseSeeder.EnsureSeeded();
            await using (var db = new AppDbContext())
            {
                await db.Database.MigrateAsync();
            }

            RootNavigation.Navigate(typeof(HomePage));
        }
    }
}
