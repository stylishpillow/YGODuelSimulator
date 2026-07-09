using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YGODuelSimulator.Views.Converters
{
    /// <summary>
    /// Turns a card's position in a face-up pile (its <c>ItemsControl.AlternationIndex</c>)
    /// into a small diagonal offset, so the pile fans from top-left (oldest) to
    /// bottom-right (newest). Paired with <c>Panel.ZIndex</c> = the same index, the
    /// last-added card sits fully visible on top. The step is capped so a large
    /// Graveyard still fits inside the pile slot rather than sprawling across the board.
    /// </summary>
    public class PileOffsetConverter : IValueConverter
    {
        private const double Step = 2.0;   // pixels of shift per card
        private const int MaxSteps = 7;     // deepest visible fan; older cards stack flush

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var index = value is int i ? i : 0;
            var shift = Math.Min(Math.Max(index, 0), MaxSteps) * Step;
            return new Thickness(shift, shift, 0, 0);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
