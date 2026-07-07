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

        /// <summary>Faint watermark text shown on the empty zone so it's obvious
        /// what kind of zone it is.</summary>
        public string Label => Kind switch
        {
            ZoneKind.MainMonster => "MONSTER",
            ZoneKind.ExtraMonster => "EXTRA",
            ZoneKind.SpellTrap => "S / T",
            ZoneKind.Field => "FIELD",
            _ => "",
        };

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

        private bool _isTarget;
        /// <summary>True while this zone is a valid destination for the card the
        /// player is currently placing, so the template can highlight it.</summary>
        public bool IsTarget
        {
            get => _isTarget;
            set { _isTarget = value; Raise(nameof(IsTarget)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
