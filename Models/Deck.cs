namespace YGODuelSimulator.Models;

public enum DeckZone { Main, Extra, Side }

/// <summary>
/// A deck as lists of card passcodes (the <see cref="Card.Id"/>) per zone.
/// Persisted as a plain <c>.ydk</c> file, so this is a simple model, not an EF
/// entity. Duplicate copies are represented as repeated ids, matching the file
/// format.
/// </summary>
public class Deck
{
    public string Name { get; set; } = "Untitled";

    public List<long> Main { get; } = [];
    public List<long> Extra { get; } = [];
    public List<long> Side { get; } = [];

    public List<long> this[DeckZone zone] => zone switch
    {
        DeckZone.Extra => Extra,
        DeckZone.Side => Side,
        _ => Main,
    };
}
