using System.ComponentModel;

namespace YGODuelSimulator.Models.Duel
{
    public enum ZoneKind
    {
        Hand, Deck, ExtraDeck, Graveyard, Banished,
        MainMonster, ExtraMonster, SpellTrap, Field
    }

    /// <summary>A single-card zone on the board (a monster/spell-trap/field/extra
    /// monster zone). Holds at most one card and knows which zone it is so drop
    /// handling can target it.</summary>
    public class ZoneSlot : INotifyPropertyChanged
    {
        public ZoneKind Kind { get; }
        public int Index { get; }

        public ZoneSlot(ZoneKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        private BoardCard? _card;
        public BoardCard? Card
        {
            get => _card;
            set { _card = value; Raise(nameof(Card)); Raise(nameof(IsEmpty)); }
        }

        public bool IsEmpty => _card is null;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
