namespace YGODuelSimulator.Models;

/// <summary>
/// Marketplace prices for a card (from card_prices). Values are stored as the
/// raw API strings to stay faithful to the source and avoid locale parsing issues.
/// </summary>
public class CardPrice
{
    public int Id { get; set; }

    public long CardId { get; set; }
    public Card Card { get; set; } = null!;

    public string? CardmarketPrice { get; set; }
    public string? TcgplayerPrice { get; set; }
    public string? EbayPrice { get; set; }
    public string? AmazonPrice { get; set; }
    public string? CoolstuffincPrice { get; set; }
}
