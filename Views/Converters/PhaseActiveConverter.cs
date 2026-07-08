using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Views.Converters
{
    /// <summary>
    /// Picks a brush for a playmat phase button based on whether the duel's current
    /// <see cref="DuelPhase"/> (values[0]) equals the phase named by the button's Tag
    /// (values[1]). The ConverterParameter selects which aspect to colour:
    /// "border", "fg" (text), or anything else (the background). The active phase reads
    /// bright red; the rest are a muted red.
    /// </summary>
    public class PhaseActiveConverter : IMultiValueConverter
    {
        private static SolidColorBrush Frozen(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }

        private static readonly SolidColorBrush ActiveBg = Frozen("#E53935");
        private static readonly SolidColorBrush ActiveBorder = Frozen("#FFFF8A80");
        private static readonly SolidColorBrush ActiveFg = Frozen("#FFFFFFFF");
        private static readonly SolidColorBrush IdleBg = Frozen("#3E1B18");
        private static readonly SolidColorBrush IdleBorder = Frozen("#66E57373");
        private static readonly SolidColorBrush IdleFg = Frozen("#C8FFE8E6");

        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            bool active = values.Length == 2
                && values[0] is DuelPhase current
                && values[1] is string tag
                && Enum.TryParse<DuelPhase>(tag, out var target)
                && current == target;

            return (parameter as string) switch
            {
                "border" => active ? ActiveBorder : IdleBorder,
                "fg" => active ? ActiveFg : IdleFg,
                _ => active ? ActiveBg : IdleBg,
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
