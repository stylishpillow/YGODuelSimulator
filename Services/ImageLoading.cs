using System.Windows.Media.Imaging;
using YGODuelSimulator.Models;

namespace YGODuelSimulator.Services;

/// <summary>Helpers for turning cached card images into WPF bitmaps.</summary>
public static class ImageLoading
{
    /// <summary>Loads an image file fully into memory and freezes it so the file
    /// isn't locked and the bitmap can be used across threads.</summary>
    public static BitmapImage LoadFrozen(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Downloads (once, cached) and returns the small thumbnail for a card, or
    /// null if the card has no image. Falls back to the full-size URL if no small
    /// URL is present.
    /// </summary>
    public static async Task<BitmapImage?> GetThumbnailAsync(CardImageService imageService, Card card)
    {
        var img = card.Images.FirstOrDefault();
        if (img is null) return null;

        var url = string.IsNullOrEmpty(img.ImageUrlSmall) ? img.ImageUrl : img.ImageUrlSmall;
        if (string.IsNullOrEmpty(url)) return null;

        var path = await imageService.GetImagePathAsync(img.ApiImageId, url, CardImageSize.Small);
        return LoadFrozen(path);
    }
}
