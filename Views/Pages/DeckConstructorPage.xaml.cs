using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Models;
using YGODuelSimulator.Services;
using YGODuelSimulator.Views.Controls;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>
    /// Deck builder: browse the card pool on the left, assemble a Main/Extra/Side
    /// deck on the right, and save/load decks as .ydk files.
    /// </summary>
    public partial class DeckConstructorPage : Page
    {
        private readonly CardImageService _imageService = new();
        private readonly ObservableCollection<DeckSlot> _main = [];
        private readonly ObservableCollection<DeckSlot> _extra = [];
        private readonly ObservableCollection<DeckSlot> _side = [];

        public DeckConstructorPage()
        {
            InitializeComponent();
            MainList.ItemsSource = _main;
            ExtraList.ItemsSource = _extra;
            SideList.ItemsSource = _side;
            UpdateHeaders();
            RefreshDeckPicker();
        }

        // --- Zone helpers ---

        private ObservableCollection<DeckSlot> Collection(DeckZone zone) => zone switch
        {
            DeckZone.Extra => _extra,
            DeckZone.Side => _side,
            _ => _main,
        };

        private ObservableCollection<DeckSlot>? ZoneOf(DeckSlot slot) =>
            _main.Contains(slot) ? _main
            : _extra.Contains(slot) ? _extra
            : _side.Contains(slot) ? _side
            : null;

        private static bool IsExtraDeckCard(Card card)
        {
            var f = card.FrameType;
            return f.Contains("fusion") || f.Contains("synchro") || f.Contains("xyz") || f.Contains("link");
        }

        private static int MaxCopies(Card card) => card.BanTcg switch
        {
            "Banned" => 0,
            "Limited" => 1,
            "Semi-Limited" => 2,
            _ => 3,
        };

        private int TotalCopies(long cardId) =>
            Sum(_main, cardId) + Sum(_extra, cardId) + Sum(_side, cardId);

        private static int Sum(IEnumerable<DeckSlot> zone, long cardId) =>
            zone.Where(s => s.Card.Id == cardId).Sum(s => s.Count);

        // --- Adding / removing ---

        private void Browser_CardActivated(object? sender, Card card) => AddCard(card);

        private void AddCard(Card card)
        {
            if (TotalCopies(card.Id) >= MaxCopies(card))
            {
                StatusText.Text = MaxCopies(card) == 0
                    ? $"{card.Name} is banned."
                    : $"Max {MaxCopies(card)} copies of {card.Name}.";
                return;
            }

            AddToZone(card, IsExtraDeckCard(card) ? DeckZone.Extra : DeckZone.Main);
        }

        private void AddToZone(Card card, DeckZone zone)
        {
            var collection = Collection(zone);
            var slot = collection.FirstOrDefault(s => s.Card.Id == card.Id);
            if (slot is null)
            {
                slot = new DeckSlot(card);
                collection.Add(slot);
                _ = LoadSlotImageAsync(slot);
            }
            else
            {
                slot.Count++;
            }
            UpdateHeaders();
        }

        private void RemoveOneFrom(DeckSlot slot)
        {
            var zone = ZoneOf(slot);
            if (zone is null) return;
            slot.Count--;
            if (slot.Count <= 0) zone.Remove(slot);
            UpdateHeaders();
        }

        private async Task LoadSlotImageAsync(DeckSlot slot)
        {
            try { slot.Image = await ImageLoading.GetThumbnailAsync(_imageService, slot.Card); }
            catch { /* a single missing image is not fatal */ }
        }

        // --- Slot interactions ---

        private static DeckSlot? SlotFrom(object sender) =>
            (sender as FrameworkElement)?.DataContext as DeckSlot;

        private void Slot_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (SlotFrom(sender) is { } slot) RemoveOneFrom(slot);
        }

        private void RemoveOne_Click(object sender, RoutedEventArgs e)
        {
            if (SlotFrom(sender) is { } slot) RemoveOneFrom(slot);
        }

        private void RemoveAll_Click(object sender, RoutedEventArgs e)
        {
            if (SlotFrom(sender) is { } slot) { ZoneOf(slot)?.Remove(slot); UpdateHeaders(); }
        }

        private void MoveToSide_Click(object sender, RoutedEventArgs e)
        {
            if (SlotFrom(sender) is not { } slot) return;
            var card = slot.Card;
            RemoveOneFrom(slot);
            AddToZone(card, DeckZone.Side);
        }

        // --- Zone headers ---

        private void UpdateHeaders()
        {
            MainHeader.Text = $"Main Deck — {_main.Sum(s => s.Count)} (40–60)";
            ExtraHeader.Text = $"Extra Deck — {_extra.Sum(s => s.Count)} / 15";
            SideHeader.Text = $"Side Deck — {_side.Sum(s => s.Count)} / 15";
        }

        // --- Save / load / files ---

        private void RefreshDeckPicker() => DeckPicker.ItemsSource = YdkFile.ListDeckNames();

        private void New_Click(object sender, RoutedEventArgs e)
        {
            _main.Clear(); _extra.Clear(); _side.Clear();
            DeckNameBox.Text = string.Empty;
            StatusText.Text = "New deck.";
            UpdateHeaders();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = DeckNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = "Untitled";
            YdkFile.Save(ExtractDeck(name));
            DeckNameBox.Text = name;
            RefreshDeckPicker();
            DeckPicker.SelectedItem = name;
            StatusText.Text = $"Saved \"{name}\".";
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if (DeckPicker.SelectedItem is not string name) { StatusText.Text = "Pick a deck to open."; return; }
            await LoadDeckAsync(YdkFile.Load(name));
            DeckNameBox.Text = name;
            StatusText.Text = $"Opened \"{name}\".";
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (DeckPicker.SelectedItem is not string name) { StatusText.Text = "Pick a deck to delete."; return; }
            YdkFile.Delete(name);
            RefreshDeckPicker();
            StatusText.Text = $"Deleted \"{name}\".";
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "YGO deck (*.ydk)|*.ydk",
                InitialDirectory = YdkFile.DecksDirectory,
            };
            if (dlg.ShowDialog() != true) return;

            var deck = YdkFile.ImportFrom(dlg.FileName);
            await LoadDeckAsync(deck);
            DeckNameBox.Text = deck.Name;
            StatusText.Text = $"Imported \"{deck.Name}\".";
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var name = DeckNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = "Untitled";
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "YGO deck (*.ydk)|*.ydk",
                FileName = name + ".ydk",
                InitialDirectory = YdkFile.DecksDirectory,
            };
            if (dlg.ShowDialog() != true) return;

            YdkFile.ExportTo(ExtractDeck(name), dlg.FileName);
            StatusText.Text = $"Exported to {dlg.FileName}.";
        }

        private Deck ExtractDeck(string name)
        {
            var deck = new Deck { Name = name };
            foreach (var s in _main) for (var i = 0; i < s.Count; i++) deck.Main.Add(s.Card.Id);
            foreach (var s in _extra) for (var i = 0; i < s.Count; i++) deck.Extra.Add(s.Card.Id);
            foreach (var s in _side) for (var i = 0; i < s.Count; i++) deck.Side.Add(s.Card.Id);
            return deck;
        }

        private async Task LoadDeckAsync(Deck deck)
        {
            _main.Clear(); _extra.Clear(); _side.Clear();

            var ids = deck.Main.Concat(deck.Extra).Concat(deck.Side).Distinct().ToList();
            Dictionary<long, Card> cards;
            await using (var db = new AppDbContext())
            {
                cards = await db.Cards.Include(c => c.Images)
                    .Where(c => ids.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id);
            }

            LoadZone(deck.Main, _main, cards);
            LoadZone(deck.Extra, _extra, cards);
            LoadZone(deck.Side, _side, cards);
            UpdateHeaders();
        }

        private void LoadZone(List<long> ids, ObservableCollection<DeckSlot> collection, Dictionary<long, Card> cards)
        {
            foreach (var group in ids.GroupBy(id => id))
            {
                if (!cards.TryGetValue(group.Key, out var card)) continue; // unknown passcode
                var slot = new DeckSlot(card, group.Count());
                collection.Add(slot);
                _ = LoadSlotImageAsync(slot);
            }
        }
    }
}
