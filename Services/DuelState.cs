using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Models;
using YGODuelSimulator.Models.Duel;

namespace YGODuelSimulator.Services
{
    /// <summary>
    /// The board state for a single-player manual duel: the piles, the fixed zones,
    /// life points, and the operations to move cards around, draw, shuffle, and
    /// roll a die / flip a coin. No rules are enforced.
    /// </summary>
    public class DuelState
    {
        public ObservableCollection<BoardCard> Hand { get; } = [];
        public ObservableCollection<BoardCard> Deck { get; } = [];
        public ObservableCollection<BoardCard> ExtraDeck { get; } = [];
        public ObservableCollection<BoardCard> Graveyard { get; } = [];
        public ObservableCollection<BoardCard> Banished { get; } = [];

        public IReadOnlyList<ZoneSlot> MainMonsterZones { get; }
        public IReadOnlyList<ZoneSlot> ExtraMonsterZones { get; }
        public IReadOnlyList<ZoneSlot> SpellTrapZones { get; }
        public ZoneSlot FieldZone { get; }

        public int LifePoints { get; set; } = 8000;

        private readonly Random _rng = new();
        private readonly CardImageService _images = new();

        public DuelState()
        {
            MainMonsterZones = MakeZones(ZoneKind.MainMonster, 5);
            ExtraMonsterZones = MakeZones(ZoneKind.ExtraMonster, 2);
            SpellTrapZones = MakeZones(ZoneKind.SpellTrap, 5);
            FieldZone = new ZoneSlot(ZoneKind.Field, 0);
        }

        private static List<ZoneSlot> MakeZones(ZoneKind kind, int count) =>
            Enumerable.Range(0, count).Select(i => new ZoneSlot(kind, i)).ToList();

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

        /// <summary>Places a card into a single-card zone if it's empty.</summary>
        public bool MoveToSlot(BoardCard card, ZoneSlot slot)
        {
            if (!slot.IsEmpty) return false;
            RemoveFromCurrent(card);
            slot.Card = card;
            return true;
        }

        public void MoveToPile(BoardCard card, ObservableCollection<BoardCard> pile, bool toTop = false)
        {
            RemoveFromCurrent(card);
            if (toTop) pile.Insert(0, card);
            else pile.Add(card);
        }

        private void RemoveFromCurrent(BoardCard card)
        {
            if (Hand.Remove(card) || Deck.Remove(card) || ExtraDeck.Remove(card)
                || Graveyard.Remove(card) || Banished.Remove(card)) return;

            foreach (var slot in AllSlots())
                if (ReferenceEquals(slot.Card, card)) { slot.Card = null; return; }
        }

        private IEnumerable<ZoneSlot> AllSlots() =>
            MainMonsterZones.Concat(ExtraMonsterZones).Concat(SpellTrapZones).Append(FieldZone);

        public int RollDie() => _rng.Next(1, 7);
        public bool FlipCoin() => _rng.Next(2) == 0;
    }
}
