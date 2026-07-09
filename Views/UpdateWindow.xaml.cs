using System.Windows;
using Velopack;
using Wpf.Ui.Controls;

namespace YGODuelSimulator.Views
{
    /// <summary>Mandatory update gate: shown (modally) when a newer release exists. The
    /// only ways out are to install the update (which downloads and relaunches the app)
    /// or to quit — there is no "later". The caller decides what a quit means (the login
    /// gate shuts the app down; a manual check just returns to Settings).</summary>
    public partial class UpdateWindow : FluentWindow
    {
        private readonly UpdateManager _mgr;
        private readonly UpdateInfo _info;

        public UpdateWindow(UpdateManager mgr, UpdateInfo info)
        {
            _mgr = mgr;
            _info = info;
            InitializeComponent();

            var current = mgr.CurrentVersion?.ToString() ?? "?";
            var next = info.TargetFullRelease.Version.ToString();
            MessageText.Text =
                $"A new version must be installed to continue.\n\n" +
                $"Current version:  v{current}\nLatest version:  v{next}";
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            UpdateButton.IsEnabled = false;
            QuitButton.IsEnabled = false;
            ErrorText.Visibility = Visibility.Collapsed;
            Progress.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Downloading…";

            try
            {
                await _mgr.DownloadUpdatesAsync(_info, p => Dispatcher.Invoke(() => Progress.Value = p));
                StatusText.Text = "Restarting…";
                // Exits the current process and relaunches into the new version.
                _mgr.ApplyUpdatesAndRestart(_info);
            }
            catch (Exception ex)
            {
                Progress.Visibility = Visibility.Collapsed;
                StatusText.Visibility = Visibility.Collapsed;
                ErrorText.Text = $"Update failed: {ex.Message}";
                ErrorText.Visibility = Visibility.Visible;
                UpdateButton.IsEnabled = true;
                QuitButton.IsEnabled = true;
            }
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
