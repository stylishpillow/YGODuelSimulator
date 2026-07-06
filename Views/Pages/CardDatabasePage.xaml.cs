using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>
    /// Card database screen: import/refresh the local database, export the bundled
    /// seed, and look up individual cards with their artwork.
    /// </summary>
    public partial class CardDatabasePage : Page
    {
        private readonly CardImportService _importService = new();
        private readonly CardImageService _imageService = new();

        public CardDatabasePage()
        {
            InitializeComponent();
            Loaded += CardDatabasePage_Loaded;
        }

        private async void CardDatabasePage_Loaded(object sender, RoutedEventArgs e)
        {
            // The database is already created/migrated by the app shell on startup,
            // so here we only need the current count.
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

        private async void ExportSeedButton_Click(object sender, RoutedEventArgs e)
        {
            ExportSeedButton.IsEnabled = false;
            StatusText.Text = "Exporting database seed…";
            try
            {
                await DatabaseSeeder.ExportSeedAsync();
                var seed = DatabaseSeeder.GetSeedPath();
                var sizeMb = new System.IO.FileInfo(seed).Length / 1024d / 1024d;
                StatusText.Text = $"Wrote {seed} ({sizeMb:N1} MB). Commit it as the bundled seed.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Export failed: {ex.Message}";
            }
            finally
            {
                ExportSeedButton.IsEnabled = true;
            }
        }

        private void CardNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ShowCardButton_Click(sender, e);
        }

        private async void ShowCardButton_Click(object sender, RoutedEventArgs e)
        {
            var name = CardNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            ShowCardButton.IsEnabled = false;
            CardImage.Source = null;
            CardDetailsText.Text = "Looking up card…";
            try
            {
                await using var db = new AppDbContext();

                // Exact match first, then fall back to a partial match. Both use
                // LIKE so the search is case-insensitive (SQLite's == and instr,
                // which EF uses for Contains, are case-sensitive).
                var card = await db.Cards.Include(c => c.Images)
                    .FirstOrDefaultAsync(c => EF.Functions.Like(c.Name, name))
                    ?? await db.Cards.Include(c => c.Images)
                        .OrderBy(c => c.Name)
                        .FirstOrDefaultAsync(c => EF.Functions.Like(c.Name, "%" + name + "%"));

                if (card is null)
                {
                    CardDetailsText.Text = $"No card found matching \"{name}\".";
                    return;
                }

                CardDetailsText.Text = BuildDetails(card);

                var image = card.Images.FirstOrDefault();
                if (image is not null)
                {
                    var cached = _imageService.IsCached(image.ApiImageId, CardImageSize.Full);
                    if (!cached) CardDetailsText.Text += "\n\nDownloading image…";

                    var path = await _imageService.GetImagePathAsync(
                        image.ApiImageId, image.ImageUrl, CardImageSize.Full);
                    CardImage.Source = LoadFrozenBitmap(path);

                    CardDetailsText.Text = BuildDetails(card) +
                        (cached ? "\n\n(image loaded from local cache)"
                                : "\n\n(image downloaded and cached)");
                }
            }
            catch (Exception ex)
            {
                CardDetailsText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                ShowCardButton.IsEnabled = true;
            }
        }

        private static string BuildDetails(Models.Card card)
        {
            var lines = new List<string> { card.Name, card.Type };
            if (card.Attribute is not null) lines.Add($"Attribute: {card.Attribute}");
            if (card.Race is not null) lines.Add($"Race/Type: {card.Race}");
            if (card.Level is not null) lines.Add($"Level/Rank: {card.Level}");
            if (card.LinkValue is not null) lines.Add($"Link: {card.LinkValue}");
            if (card.Atk is not null || card.Def is not null)
                lines.Add($"ATK {card.Atk?.ToString() ?? "—"} / DEF {card.Def?.ToString() ?? "—"}");
            if (card.Archetype is not null) lines.Add($"Archetype: {card.Archetype}");
            lines.Add("");
            lines.Add(card.Description ?? string.Empty);
            return string.Join("\n", lines);
        }

        /// <summary>Loads an image fully into memory and freezes it so the file
        /// isn't locked and the bitmap can be used across threads.</summary>
        private static BitmapImage LoadFrozenBitmap(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private async Task RefreshCountAsync()
        {
            await using var db = new AppDbContext();
            var count = await db.Cards.CountAsync();
            CountText.Text = $"Cards in database: {count:N0}";
        }
    }
}
