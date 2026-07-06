using System.Text.Json.Serialization;

namespace YGODuelSimulator.Api;

/// <summary>Top-level envelope: cardinfo.php returns { "data": [ ... ] }.</summary>
public sealed class CardApiResponse
{
    [JsonPropertyName("data")]
    public List<CardDto> Data { get; set; } = [];
}

public sealed class CardDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("frameType")] public string FrameType { get; set; } = string.Empty;
    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("race")] public string? Race { get; set; }
    [JsonPropertyName("archetype")] public string? Archetype { get; set; }
    [JsonPropertyName("attribute")] public string? Attribute { get; set; }
    [JsonPropertyName("ygoprodeck_url")] public string? YgoprodeckUrl { get; set; }

    [JsonPropertyName("atk")] public int? Atk { get; set; }
    [JsonPropertyName("def")] public int? Def { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("scale")] public int? Scale { get; set; }
    [JsonPropertyName("linkval")] public int? LinkVal { get; set; }
    [JsonPropertyName("linkmarkers")] public List<string>? LinkMarkers { get; set; }

    [JsonPropertyName("card_sets")] public List<CardSetDto>? CardSets { get; set; }
    [JsonPropertyName("card_images")] public List<CardImageDto>? CardImages { get; set; }
    [JsonPropertyName("card_prices")] public List<CardPriceDto>? CardPrices { get; set; }
    [JsonPropertyName("banlist_info")] public BanlistInfoDto? BanlistInfo { get; set; }
    [JsonPropertyName("misc_info")] public List<MiscInfoDto>? MiscInfo { get; set; }
}

public sealed class CardSetDto
{
    [JsonPropertyName("set_name")] public string? SetName { get; set; }
    [JsonPropertyName("set_code")] public string? SetCode { get; set; }
    [JsonPropertyName("set_rarity")] public string? SetRarity { get; set; }
    [JsonPropertyName("set_rarity_code")] public string? SetRarityCode { get; set; }
    [JsonPropertyName("set_price")] public string? SetPrice { get; set; }
}

public sealed class CardImageDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("image_url_small")] public string? ImageUrlSmall { get; set; }
    [JsonPropertyName("image_url_cropped")] public string? ImageUrlCropped { get; set; }
}

public sealed class CardPriceDto
{
    [JsonPropertyName("cardmarket_price")] public string? CardmarketPrice { get; set; }
    [JsonPropertyName("tcgplayer_price")] public string? TcgplayerPrice { get; set; }
    [JsonPropertyName("ebay_price")] public string? EbayPrice { get; set; }
    [JsonPropertyName("amazon_price")] public string? AmazonPrice { get; set; }
    [JsonPropertyName("coolstuffinc_price")] public string? CoolstuffincPrice { get; set; }
}

public sealed class BanlistInfoDto
{
    [JsonPropertyName("ban_tcg")] public string? BanTcg { get; set; }
    [JsonPropertyName("ban_ocg")] public string? BanOcg { get; set; }
    [JsonPropertyName("ban_goat")] public string? BanGoat { get; set; }
}

public sealed class MiscInfoDto
{
    [JsonPropertyName("konami_id")] public long? KonamiId { get; set; }
    [JsonPropertyName("tcg_date")] public string? TcgDate { get; set; }
    [JsonPropertyName("ocg_date")] public string? OcgDate { get; set; }
    [JsonPropertyName("views")] public int? Views { get; set; }
    [JsonPropertyName("has_effect")] public int? HasEffect { get; set; }
    [JsonPropertyName("formats")] public List<string>? Formats { get; set; }
}
