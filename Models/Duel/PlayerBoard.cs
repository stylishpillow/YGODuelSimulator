using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Models;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Models.Duel
{
    public enum PlayerSide { Player, Opponent }

    /// <summary>
    /// One player's half of the field: the piles (Hand/Deck/Extra/GY/Banished),
    /// the fixed zones, and that player's Life Points. Two of these make up a
    /// <see cref="Services.DuelState"/>. Nothing here is rule-enforced.
    /// </summary>
    public class PlayerBoard : INotifyPropertyChanged
    {
        public PlayerSide Side { get; }

        public ObservableCollection<BoardCard> Hand { get; } = [];
        public ObservableCollection<BoardCard> Deck { get; } = [];
        public ObservableCollection<BoardCard> ExtraDeck { get; } = [];
        public ObservableCollection<BoardCard> Graveyard { get; } = [];
        public ObservableCollection<BoardCard> Banished { get; } = [];

        public IReadOnlyList<ZoneSlot> MainMonsterZones { get; }
        public IReadOnlyList<ZoneSlot> ExtraMonsterZones { get; }
        public IReadOnlyList<ZoneSlot> SpellTrapZones { get; }
        public ZoneSlot FieldZone { get; }

        private int _lifePoints = 8000;
        public int LifePoints
        {
            get => _lifePoints;
            set { _lifePoints = value; Raise(nameof(LifePoints)); }
        }

        private readonly Random _rng;
        private readonly CardImageService _images;

        public PlayerBoard(PlayerSide side, Random rng, CardImageService images)
        {
            Side = side;
            _rng = rng;
            _images = images;
            MainMonsterZones = MakeZones(ZoneKind.MainMonster, 5);
            ExtraMonsterZones = MakeZones(ZoneKind.ExtraMonster, 2);
            SpellTrapZones = MakeZones(ZoneKind.SpellTrap, 5);
            FieldZone = new ZoneSlot(ZoneKind.Field, 0, this);
        }

        private List<ZoneSlot> MakeZones(ZoneKind kind, int count) =>
            Enumerable.Range(0, count).Select(i => new ZoneSlot(kind, i, this)).ToList();

        public IEnumerable<ZoneSlot> AllSlots() =>
            MainMonsterZones.Concat(ExtraMonsterZones).Concat(SpellTrapZones).Append(FieldZone);

        /// <summary>Loads a deck: fetch the cards, fill the Deck and Extra Deck piles,
        /// shuffle, clear the board, and draw an opening hand of five.</summary>
        public async Task LoadDeckAsync(Deck deck)
        {
            var ids = deck.Main.Concat(deck.Extra).Distinct().ToList();
            Dictionary<long, Card> cards;
            await using (var db = new AppDbContext())
            {
                cards = await db.Cards.Include(c => c.Images)
                    .Where(c => ids.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id);
            }

            Deck.Clear(); ExtraDeck.Clear(); Hand.Clear(); Graveyard.Clear(); Banished.Clear();
            foreach (var slot in AllSlots()) slot.Card = null;

            foreach (var id in deck.Main) if (cards.TryGetValue(id, out var c)) AddNew(Deck, c);
            foreach (var id in deck.Extra) if (cards.TryGetValue(id, out var c)) AddNew(ExtraDeck, c);

            Shuffle();
            for (var i = 0; i < 5; i++) Draw();
        }

        private void AddNew(ObservableCollection<BoardCard> pile, Card card)
        {
            var bc = new BoardCard(card);
            pile.Add(bc);
            _ = LoadImageAsync(bc);
        }

        private async Task LoadImageAsync(BoardCard bc)
        {
            try { bc.Image = await ImageLoading.GetThumbnailAsync(_images, bc.Card); }
            catch { /* board still works without the thumbnail */ }
        }

        public void Shuffle()
        {
            for (var i = Deck.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (Deck[i], Deck[j]) = (Deck[j], Deck[i]);
            }
        }

        public BoardCard? Draw()
        {
            if (Deck.Count == 0) return null;
            var card = Deck[0];
            Deck.RemoveAt(0);
            card.FaceDown = false;
            card.Defense = false;
            Hand.Add(card);
            return card;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
