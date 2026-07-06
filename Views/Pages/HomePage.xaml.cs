using System.Windows;
using System.Windows.Controls;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>Dashboard landing page with big tiles that jump to each section.</summary>
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
        }

        private static void NavigateTo(Type pageType) =>
            (Application.Current.MainWindow as MainWindow)?.NavigateTo(pageType);

        private void DuelRoomTile_Click(object sender, RoutedEventArgs e) => NavigateTo(typeof(DuelRoomPage));
        private void DeckConstructorTile_Click(object sender, RoutedEventArgs e) => NavigateTo(typeof(DeckConstructorPage));
        private void CardDatabaseTile_Click(object sender, RoutedEventArgs e) => NavigateTo(typeof(CardDatabasePage));
        private void ProfileTile_Click(object sender, RoutedEventArgs e) => NavigateTo(typeof(ProfilePage));
    }
}
