namespace YGODuelSimulator.Models;

/// <summary>A play format a card is legal/available in, e.g. "TCG", "OCG", "Master Duel".</summary>
public class CardFormat
{
    public int Id { get; set; }

    public long CardId { get; set; }
    public Card Card { get; set; } = null!;

    public string Format { get; set; } = string.Empty;
}
