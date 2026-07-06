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

        public BoardCard(Card card) => Card = card;

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
            }
        }

        private bool _defense;
        public bool Defense
        {
            get => _defense;
            set { _defense = value; Raise(nameof(Defense)); Raise(nameof(Rotation)); }
        }

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
