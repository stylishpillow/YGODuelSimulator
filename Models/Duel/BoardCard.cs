using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace YGODuelSimulator.Models.Duel
{
    /// <summary>
    /// A card in play on the duel board: the underlying <see cref="Card"/> plus its
    /// current presentation (face-up/down, attack/defense) and thumbnail. The app
    /// is a manual simulator, so these flags are purely visual — nothing is enforced.
    /// </summary>
    public class BoardCard : INotifyPropertyChanged
    {
        public Card Card { get; }
        public string Name => Card.Name;

        /// <summary>True for Monster Tokens created by effects. Tokens only ever
        /// exist on the field; sending one anywhere else just removes it.</summary>
        public bool IsToken { get; init; }

        public BoardCard(Card card) => Card = card;

        private int _counters;
        /// <summary>Free-floating counter tally the player can bump up/down for
        /// effects that "place counters" on a card. Purely a manual tracker.</summary>
        public int Counters
        {
            get => _counters;
            set
            {
                _counters = Math.Max(0, value);
                Raise(nameof(Counters));
                Raise(nameof(CounterVisibility));
            }
        }

        public Visibility CounterVisibility => _counters > 0 ? Visibility.Visible : Visibility.Collapsed;

        private bool _faceDown;
        public bool FaceDown
        {
            get => _faceDown;
            set
            {
                _faceDown = value;
                Raise(nameof(FaceDown));
                Raise(nameof(FrontVisibility));
                Raise(nameof(BackVisibility));
                Raise(nameof(TokenVisibility));
            }
        }

        /// <summary>A face-up token has no artwork, so the template shows a labelled
        /// placeholder instead of an image.</summary>
        public Visibility TokenVisibility => IsToken && !_faceDown ? Visibility.Visible : Visibility.Collapsed;

        private bool _defense;
        public bool Defense
        {
            get => _defense;
            set { _defense = value; Raise(nameof(Defense)); Raise(nameof(Rotation)); }
        }

        private bool _isSelected;
        /// <summary>True while this card is the current click-selection, so the
        /// template can draw a highlight outline.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; Raise(nameof(IsSelected)); Raise(nameof(SelectionVisibility)); }
        }

        public Visibility SelectionVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

        private bool _isPointed;
        /// <summary>True while a player is pointing at / declaring on this card, so
        /// the template draws a bright ring to draw the other player's attention.</summary>
        public bool IsPointed
        {
            get => _isPointed;
            set { _isPointed = value; Raise(nameof(IsPointed)); Raise(nameof(PointVisibility)); }
        }

        public Visibility PointVisibility => _isPointed ? Visibility.Visible : Visibility.Collapsed;

        private BitmapImage? _image;
        public BitmapImage? Image
        {
            get => _image;
            set { _image = value; Raise(nameof(Image)); }
        }

        /// <summary>Defense position shows the card rotated 90°.</summary>
        public double Rotation => _defense ? 90 : 0;
        public Visibility FrontVisibility => _faceDown ? Visibility.Collapsed : Visibility.Visible;
        public Visibility BackVisibility => _faceDown ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
