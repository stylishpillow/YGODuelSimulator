using System.Windows;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Services;
using YGODuelSimulator.Views;

namespace YGODuelSimulator
{
    /// <summary>
    /// Interaction logic for App.xaml. On startup the database is prepared and the
    /// login window is shown; the main shell only opens once a user signs in.
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Keep the app alive while the (modeless-until-shown) login dialog runs,
            // so closing the login window doesn't count as "last window closed".
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // First run: unpack the bundled card database if there's none yet, make
            // sure the schema is current, then ensure the built-in admin exists.
            DatabaseSeeder.EnsureSeeded();
            await using (var db = new AppDbContext())
            {
                await db.Database.MigrateAsync();
            }
            await new AuthService().EnsureAdminSeededAsync();

            var login = new LoginWindow();
            if (login.ShowDialog() == true && login.AuthenticatedUser is { } user)
            {
                Session.CurrentUser = user;

                // Mandatory update gate: if a newer release exists, the user must install
                // it before reaching the app. Installing relaunches the process; quitting
                // (or the offline/dev case, where CheckAsync returns null) falls through.
                // A failed check (offline / unreachable feed) must never lock users out.
                try
                {
                    if (await UpdateService.CheckAsync() is { } pending)
                    {
                        new UpdateWindow(pending.mgr, pending.info).ShowDialog();
                        Shutdown(); // only reached if the user declined the update
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
                }

                var main = new MainWindow();
                MainWindow = main;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                main.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}
