using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using YGODuelSimulator.Models;

namespace YGODuelSimulator.Views.Controls
{
    /// <summary>A stacked entry in a deck zone: one card and how many copies of it.</summary>
    public class DeckSlot : INotifyPropertyChanged
    {
        public Card Card { get; }

        public DeckSlot(Card card, int count = 1)
        {
            Card = card;
            _count = count;
        }

        private int _count;
        public int Count
        {
            get => _count;
            set
            {
                _count = value;
                Raise(nameof(Count));
                Raise(nameof(CountLabel));
                Raise(nameof(CountBadgeVisibility));
            }
        }

        public string CountLabel => $"x{Count}";
        public Visibility CountBadgeVisibility => Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        private BitmapImage? _image;
        public BitmapImage? Image
        {
            get => _image;
            set { _image = value; Raise(nameof(Image)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
