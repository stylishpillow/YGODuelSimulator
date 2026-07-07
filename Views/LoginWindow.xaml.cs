using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;
using YGODuelSimulator.Models;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Views
{
    /// <summary>Sign-in / sign-up gate shown before the main shell. On success,
    /// exposes the authenticated <see cref="User"/> and closes with a true result.</summary>
    public partial class LoginWindow : FluentWindow
    {
        private readonly AuthService _auth = new();

        public User? AuthenticatedUser { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => UsernameBox.Focus();
        }

        private async void Login_Click(object sender, RoutedEventArgs e) => await LogInAsync();

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            var (user, error) = await _auth.RegisterAsync(UsernameBox.Text, PasswordBox.Password);
            if (error is not null) { ShowError(error); return; }
            Succeed(user!);
        }

        private void Password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = LogInAsync();
        }

        private async Task LogInAsync()
        {
            var user = await _auth.AuthenticateAsync(UsernameBox.Text, PasswordBox.Password);
            if (user is null) { ShowError("Wrong username or password."); return; }
            Succeed(user);
        }

        private void Succeed(User user)
        {
            AuthenticatedUser = user;
            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
