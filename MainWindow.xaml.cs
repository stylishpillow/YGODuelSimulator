using System.Windows;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Services;

namespace YGODuelSimulator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CardImportService _importService = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Make sure the database and schema exist, then show the current count.
            await using var db = new AppDbContext();
            await db.Database.MigrateAsync();
            await RefreshCountAsync();
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            ImportButton.IsEnabled = false;
            var progress = new Progress<ImportProgress>(p => StatusText.Text = p.Message);
            try
            {
                var result = await _importService.ImportAllAsync(progress);
                StatusText.Text =
                    $"Imported {result.Cards:N0} cards, {result.Images:N0} images, " +
                    $"{result.Sets:N0} set printings, {result.Prices:N0} price rows.";
                await RefreshCountAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Import failed: {ex.Message}";
            }
            finally
            {
                ImportButton.IsEnabled = true;
            }
        }

        private async Task RefreshCountAsync()
        {
            await using var db = new AppDbContext();
            var count = await db.Cards.CountAsync();
            CountText.Text = $"Cards in database: {count:N0}";
        }
    }
}
