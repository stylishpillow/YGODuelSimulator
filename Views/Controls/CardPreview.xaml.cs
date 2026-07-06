using System.Windows.Controls;
using YGODuelSimulator.Models;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Views.Controls
{
    /// <summary>Shows a close-up of a single card: full artwork, stats, and effect
    /// text. Call <see cref="Show"/> to display a card.</summary>
    public partial class CardPreview : UserControl
    {
        private readonly CardImageService _imageService = new();
        private long _currentImageId;

        public CardPreview() => InitializeComponent();

        public async void Show(Card card)
        {
            PreviewName.Text = card.Name;
            PreviewMeta.Text = BuildMeta(card);
            PreviewText.Text = card.Description ?? string.Empty;
            PreviewImage.Source = null;

            var img = card.Images.FirstOrDefault();
            if (img is null) return;

            _currentImageId = img.ApiImageId;
            try
            {
                var path = await _imageService.GetImagePathAsync(
                    img.ApiImageId, img.ImageUrl, CardImageSize.Full);

                // The user may have clicked another card while this loaded; only
                // apply the image if it's still the one being previewed.
                if (_currentImageId == img.ApiImageId)
                    PreviewImage.Source = ImageLoading.LoadFrozen(path);
            }
            catch
            {
                // Preview image is best-effort.
            }
        }

        private static string BuildMeta(Card card)
        {
            var parts = new List<string> { card.Type };
            if (card.Attribute is not null) parts.Add(card.Attribute);
            if (card.Race is not null) parts.Add(card.Race);
            if (card.Level is not null) parts.Add($"Level/Rank {card.Level}");
            if (card.LinkValue is not null) parts.Add($"LINK-{card.LinkValue}");
            if (card.Atk is not null || card.Def is not null)
                parts.Add($"ATK {card.Atk?.ToString() ?? "—"} / DEF {card.Def?.ToString() ?? "—"}");
            if (card.Archetype is not null) parts.Add(card.Archetype);
            return string.Join("  •  ", parts);
        }
    }
}
