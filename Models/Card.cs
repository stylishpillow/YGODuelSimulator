namespace YGODuelSimulator.Models;

/// <summary>
/// A Yu-Gi-Oh! card as returned by the YGOPRODeck cardinfo API.
/// Core gameplay fields live here; banlist and misc metadata are flattened
/// onto this entity, while multi-valued data lives in the related tables.
/// </summary>
public class Card
{
    /// <summary>The card passcode / API "id". Used as the primary key.</summary>
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>e.g. "Effect Monster", "Spell Card", "Link Monster".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Visual frame, e.g. "effect", "spell", "xyz", "link".</summary>
    public string FrameType { get; set; } = string.Empty;

    /// <summary>Card text ("desc" in the API).</summary>
    public string? Description { get; set; }

    /// <summary>Monster type / spell-trap property, e.g. "Warrior", "Continuous".</summary>
    public string? Race { get; set; }

    public string? Archetype { get; set; }

    /// <summary>Monster attribute, e.g. "DARK", "LIGHT".</summary>
    public string? Attribute { get; set; }

    public string? YgoprodeckUrl { get; set; }

    // --- Monster stats (null for spells/traps) ---
    public int? Atk { get; set; }
    public int? Def { get; set; }
    public int? Level { get; set; }

    /// <summary>Pendulum scale.</summary>
    public int? Scale { get; set; }

    /// <summary>Link rating ("linkval" in the API).</summary>
    public int? LinkValue { get; set; }

    // --- Banlist status (flattened from banlist_info) ---
    public string? BanTcg { get; set; }
    public string? BanOcg { get; set; }
    public string? BanGoat { get; set; }

    // --- Selected misc_info fields (requires misc=yes on the request) ---
    public long? KonamiId { get; set; }
    public string? TcgDate { get; set; }
    public string? OcgDate { get; set; }
    public bool? HasEffect { get; set; }
    public int? Views { get; set; }

    // --- Navigation collections ---
    public List<CardImage> Images { get; set; } = [];
    public List<CardSet> Sets { get; set; } = [];
    public List<CardPrice> Prices { get; set; } = [];
    public List<CardLinkMarker> LinkMarkers { get; set; } = [];
    public List<CardFormat> Formats { get; set; } = [];
}
