namespace YGODuelSimulator.Models;

/// <summary>An artwork/print image for a card (cards can have multiple alt arts).</summary>
public class CardImage
{
    public int Id { get; set; }

    /// <summary>The image id from the API (used in the image file names).</summary>
    public long ApiImageId { get; set; }

    public long CardId { get; set; }
    public Card Card { get; set; } = null!;

    public string ImageUrl { get; set; } = string.Empty;
    public string ImageUrlSmall { get; set; } = string.Empty;
    public string ImageUrlCropped { get; set; } = string.Empty;
}
