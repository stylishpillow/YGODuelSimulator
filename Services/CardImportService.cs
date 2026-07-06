using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Api;
using YGODuelSimulator.Data;
using YGODuelSimulator.Models;

namespace YGODuelSimulator.Services;

public readonly record struct ImportProgress(string Message);

public readonly record struct ImportResult(int Cards, int Images, int Sets, int Prices);

/// <summary>
/// Downloads the full card database from the YGOPRODeck API and replaces the
/// local SQLite contents with it. The API guide asks callers to pull the whole
/// database in one request and cache locally rather than query repeatedly.
/// </summary>
public class CardImportService
{
    // Pulling with no filters returns every card; misc=yes adds the extra metadata.
    private const string AllCardsUrl =
        "https://db.ygoprodeck.com/api/v7/cardinfo.php?misc=yes";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public async Task<ImportResult> ImportAllAsync(
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ImportProgress("Downloading card database from YGOPRODeck…"));

        var response = await FetchAsync(cancellationToken);
        var cards = response?.Data ?? [];
        if (cards.Count == 0)
            throw new InvalidOperationException("The API returned no cards.");

        progress?.Report(new ImportProgress($"Downloaded {cards.Count:N0} cards. Writing to database…"));

        await using var db = new AppDbContext();
        await db.Database.MigrateAsync(cancellationToken);

        // Full refresh: clear existing data, then bulk insert. Cascade delete on
        // the children means removing cards is enough.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Cards", cancellationToken);

        db.ChangeTracker.AutoDetectChangesEnabled = false;

        int images = 0, sets = 0, prices = 0;
        foreach (var dto in cards)
        {
            var (card, imgCount, setCount, priceCount) = MapCard(dto);
            db.Cards.Add(card);
            images += imgCount;
            sets += setCount;
            prices += priceCount;
        }

        progress?.Report(new ImportProgress("Saving changes… (this can take a moment)"));
        await db.SaveChangesAsync(cancellationToken);

        progress?.Report(new ImportProgress($"Done. Imported {cards.Count:N0} cards."));
        return new ImportResult(cards.Count, images, sets, prices);
    }

    private static async Task<CardApiResponse?> FetchAsync(CancellationToken cancellationToken)
    {
        return await HttpClient.GetFromJsonAsync<CardApiResponse>(AllCardsUrl, cancellationToken);
    }

    private static (Card card, int images, int sets, int prices) MapCard(CardDto dto)
    {
        var card = new Card
        {
            Id = dto.Id,
            Name = dto.Name,
            Type = dto.Type,
            FrameType = dto.FrameType,
            Description = dto.Desc,
            Race = dto.Race,
            Archetype = dto.Archetype,
            Attribute = dto.Attribute,
            YgoprodeckUrl = dto.YgoprodeckUrl,
            Atk = dto.Atk,
            Def = dto.Def,
            Level = dto.Level,
            Scale = dto.Scale,
            LinkValue = dto.LinkVal,
            BanTcg = dto.BanlistInfo?.BanTcg,
            BanOcg = dto.BanlistInfo?.BanOcg,
            BanGoat = dto.BanlistInfo?.BanGoat,
        };

        var misc = dto.MiscInfo?.FirstOrDefault();
        if (misc is not null)
        {
            card.KonamiId = misc.KonamiId;
            card.TcgDate = misc.TcgDate;
            card.OcgDate = misc.OcgDate;
            card.Views = misc.Views;
            card.HasEffect = misc.HasEffect switch { null => null, 0 => false, _ => true };
            if (misc.Formats is not null)
                foreach (var f in misc.Formats)
                    card.Formats.Add(new CardFormat { Format = f });
        }

        if (dto.LinkMarkers is not null)
            foreach (var m in dto.LinkMarkers)
                card.LinkMarkers.Add(new CardLinkMarker { Marker = m });

        if (dto.CardImages is not null)
            foreach (var img in dto.CardImages)
                card.Images.Add(new CardImage
                {
                    ApiImageId = img.Id,
                    ImageUrl = img.ImageUrl ?? string.Empty,
                    ImageUrlSmall = img.ImageUrlSmall ?? string.Empty,
                    ImageUrlCropped = img.ImageUrlCropped ?? string.Empty,
                });

        if (dto.CardSets is not null)
            foreach (var s in dto.CardSets)
                card.Sets.Add(new CardSet
                {
                    SetName = s.SetName,
                    SetCode = s.SetCode,
                    SetRarity = s.SetRarity,
                    SetRarityCode = s.SetRarityCode,
                    SetPrice = s.SetPrice,
                });

        if (dto.CardPrices is not null)
            foreach (var p in dto.CardPrices)
                card.Prices.Add(new CardPrice
                {
                    CardmarketPrice = p.CardmarketPrice,
                    TcgplayerPrice = p.TcgplayerPrice,
                    EbayPrice = p.EbayPrice,
                    AmazonPrice = p.AmazonPrice,
                    CoolstuffincPrice = p.CoolstuffincPrice,
                });

        return (card, card.Images.Count, card.Sets.Count, card.Prices.Count);
    }
}
