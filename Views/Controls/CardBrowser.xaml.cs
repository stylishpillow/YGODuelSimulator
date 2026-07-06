using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Models;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Views.Controls
{
    /// <summary>
    /// Reusable card-pool browser: a debounced name search plus category/attribute
    /// filters, showing results as a grid of thumbnails. Double-clicking a card
    /// raises <see cref="CardActivated"/> so a host (e.g. the deck builder) can add it.
    /// </summary>
    public partial class CardBrowser : UserControl
    {
        private const int MaxResults = 200;

        private readonly CardImageService _imageService = new();
        private readonly DispatcherTimer _debounce;

        public ObservableCollection<CardThumbnail> Results { get; } = [];

        /// <summary>Raised when the user double-clicks a card in the results.</summary>
        public event EventHandler<Card>? CardActivated;

        public CardBrowser()
        {
            InitializeComponent();
            ResultsList.ItemsSource = Results;

            CategoryFilter.ItemsSource = new[] { "All types", "Monster", "Spell", "Trap" };
            CategoryFilter.SelectedIndex = 0;
            AttributeFilter.ItemsSource = new[]
            {
                "All attributes", "DARK", "LIGHT", "EARTH", "WATER", "FIRE", "WIND", "DIVINE"
            };
            AttributeFilter.SelectedIndex = 0;

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };

            Loaded += async (_, _) => await SearchAsync();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RestartDebounce();
        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RestartDebounce();

        private void RestartDebounce()
        {
            if (_debounce is null) return;   // guard during initialization
            _debounce.Stop();
            _debounce.Start();
        }

        private async Task SearchAsync()
        {
            var term = SearchBox.Text.Trim();
            var category = CategoryFilter.SelectedIndex;   // 0 all, 1 Monster, 2 Spell, 3 Trap
            var attribute = AttributeFilter.SelectedItem as string;

            List<Card> cards;
            await using (var db = new AppDbContext())
            {
                IQueryable<Card> q = db.Cards.Include(c => c.Images);

                if (!string.IsNullOrEmpty(term))
                    q = q.Where(c => EF.Functions.Like(c.Name, "%" + term + "%"));

                q = category switch
                {
                    1 => q.Where(c => c.Type.Contains("Monster")),
                    2 => q.Where(c => c.Type.Contains("Spell")),
                    3 => q.Where(c => c.Type.Contains("Trap")),
                    _ => q,
                };

                if (!string.IsNullOrEmpty(attribute) && attribute != "All attributes")
                    q = q.Where(c => c.Attribute == attribute);

                cards = await q.OrderBy(c => c.Name).Take(MaxResults).ToListAsync();
            }

            Results.Clear();
            foreach (var card in cards)
            {
                var thumb = new CardThumbnail(card);
                Results.Add(thumb);
                _ = LoadThumbnailAsync(thumb);
            }
        }

        private async Task LoadThumbnailAsync(CardThumbnail thumb)
        {
            try
            {
                thumb.Image = await ImageLoading.GetThumbnailAsync(_imageService, thumb.Card);
            }
            catch
            {
                // A single failed image shouldn't break the whole grid.
            }
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem is CardThumbnail t)
                CardActivated?.Invoke(this, t.Card);
        }
    }
}
