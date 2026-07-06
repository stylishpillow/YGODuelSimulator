namespace YGODuelSimulator.Models;

/// <summary>A single Link arrow direction for a Link monster, e.g. "Bottom-Left".</summary>
public class CardLinkMarker
{
    public int Id { get; set; }

    public long CardId { get; set; }
    public Card Card { get; set; } = null!;

    public string Marker { get; set; } = string.Empty;
}
