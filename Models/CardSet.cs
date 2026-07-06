namespace YGODuelSimulator.Models;

/// <summary>A printing of a card in a specific set (from card_sets).</summary>
public class CardSet
{
    public int Id { get; set; }

    public long CardId { get; set; }
    public Card Card { get; set; } = null!;

    public string? SetName { get; set; }
    public string? SetCode { get; set; }
    public string? SetRarity { get; set; }
    public string? SetRarityCode { get; set; }

    /// <summary>Stored as the raw API string to avoid locale-dependent parsing.</summary>
    public string? SetPrice { get; set; }
}
