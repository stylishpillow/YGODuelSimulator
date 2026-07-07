using System.ComponentModel;

namespace YGODuelSimulator.Models.Duel
{
    public enum ZoneKind
    {
        Hand, Deck, ExtraDeck, Graveyard, Banished,
        MainMonster, ExtraMonster, SpellTrap, Field
    }

    /// <summary>A single-card zone on the board (a monster/spell-trap/field/extra
    /// monster zone). Holds at most one card and knows which zone it is, and which
    /// player owns it, so placement can target it.</summary>
    public class ZoneSlot : INotifyPropertyChanged
    {
        public ZoneKind Kind { get; }
        public int Index { get; }

        /// <summary>The board this zone belongs to (set when the board builds it).</summary>
        public PlayerBoard Owner { get; }

        /// <summary>True for the opponent's zones, so the template can rotate the card
        /// 180° to face away from us (as it would across a real table).</summary>
        public bool IsOpponent => Owner.Side == PlayerSide.Opponent;

        /// <summary>The leftmost and rightmost Spell/Trap zones double as Pendulum
        /// Zones (rulebook p.5).</summary>
        public bool IsPendulumZone => Kind == ZoneKind.SpellTrap && (Index == 0 || Index == 4);

        /// <summary>Faint watermark text shown on the empty zone so it's obvious
        /// what kind of zone it is.</summary>
        public string Label => Kind switch
        {
            ZoneKind.MainMonster => "MONSTER",
            ZoneKind.ExtraMonster => "EXTRA",
            ZoneKind.SpellTrap => IsPendulumZone ? "S / T\nPEND" : "S / T",
            ZoneKind.Field => "FIELD",
            _ => "",
        };

        public ZoneSlot(ZoneKind kind, int index, PlayerBoard owner)
        {
            Kind = kind;
            Index = index;
            Owner = owner;
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
