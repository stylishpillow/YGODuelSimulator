using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>The signed-in user's profile: identity, password change, and log out.</summary>
    public partial class ProfilePage : Page
    {
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x7A));

        private readonly AuthService _auth = new();

        public ProfilePage()
        {
            InitializeComponent();

            if (Session.CurrentUser is { } user)
            {
                UsernameText.Text = user.Username;
                RoleText.Text = user.IsAdmin ? "Administrator" : "Player";
                MemberSinceText.Text = $"Member since {user.CreatedUtc.ToLocalTime():d MMM yyyy}";
            }
        }

        private async void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (Session.CurrentUser is not { } user) return;

            if (NewPw.Password != ConfirmPw.Password)
            {
                SetStatus("New passwords don't match.", isError: true);
                return;
            }

            var error = await _auth.ChangePasswordAsync(user.Id, CurrentPw.Password, NewPw.Password);
            if (error is not null)
            {
                SetStatus(error, isError: true);
                return;
            }

            CurrentPw.Password = string.Empty;
            NewPw.Password = string.Empty;
            ConfirmPw.Password = string.Empty;
            SetStatus("Password updated.", isError: false);
        }

        private void Logout_Click(object sender, RoutedEventArgs e) =>
            (Application.Current.MainWindow as MainWindow)?.LogOut();

        private void SetStatus(string message, bool isError)
        {
            PwStatus.Text = message;
            PwStatus.Foreground = isError ? ErrorBrush : OkBrush;
            PwStatus.Visibility = Visibility.Visible;
        }
    }
}
