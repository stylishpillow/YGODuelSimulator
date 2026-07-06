using System.ComponentModel;
using System.Windows.Media.Imaging;
using YGODuelSimulator.Models;

namespace YGODuelSimulator.Views.Controls
{
    /// <summary>A search result: a card plus its lazily-loaded thumbnail image.</summary>
    public class CardThumbnail : INotifyPropertyChanged
    {
        public Card Card { get; }
        public string Name => Card.Name;

        public CardThumbnail(Card card) => Card = card;

        private BitmapImage? _image;
        public BitmapImage? Image
        {
            get => _image;
            set { _image = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
