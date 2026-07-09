using System.Text.Json;
using System.Text.Json.Serialization;
using YGODuelSimulator.Models.Duel;

namespace YGODuelSimulator.Services.Net;

/// <summary>Shared constants and JSON options for the peer-to-peer wire protocol.</summary>
public static class NetProtocol
{
    /// <summary>Bumped whenever the message shapes change; peers must match.</summary>
    public const int ProtocolVersion = 6;

    public const int DiscoveryPort = 47772;   // UDP room beacons
    public const int DefaultGamePort = 47771;  // TCP duel connection

    /// <summary>Cloudflare Worker relay for cross-network (Internet) play. Peers open
    /// <c>{RelayBaseUrl}/room/{code}?role=host|join</c>; see the <c>relay/</c> folder.</summary>
    public const string RelayBaseUrl = "wss://ygo-duel-relay.eelco-hansma-mail.workers.dev";

    /// <summary>Guards the receive loop against absurd frame sizes.</summary>
    public const int MaxMessageBytes = 1 << 20; // 1 MB

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum RpsChoice { Rock, Paper, Scissors }

/// <summary>Where a table-talk target sits, from the receiver's viewpoint, so they can
/// ring the right card: on the sender's own field (the receiver's opponent shadow), on
/// the receiver's own field (the sender pointed at one of the receiver's cards), or
/// nowhere on a field (a hand card / not locatable).</summary>
public enum AnnounceSide { None, SenderField, ReceiverField }

/// <summary>
/// Base type for everything sent over the TCP link. Serialized polymorphically with a
/// "type" discriminator so a single receive loop can deserialize any message.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
// Pre-game handshake / lobby
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(DeckSelectedMessage), "deck")]
[JsonDerivedType(typeof(RpsThrowMessage), "rps")]
[JsonDerivedType(typeof(TurnChoiceMessage), "turnChoice")]
[JsonDerivedType(typeof(GameStartMessage), "gameStart")]
// In-duel (each describes a publicly visible change on the *sender's* side)
[JsonDerivedType(typeof(SummonMessage), "summon")]
[JsonDerivedType(typeof(SetCardMessage), "set")]
[JsonDerivedType(typeof(RevealMessage), "reveal")]
[JsonDerivedType(typeof(PositionChangeMessage), "position")]
[JsonDerivedType(typeof(FieldToPileMessage), "fieldToPile")]
[JsonDerivedType(typeof(HandToPileMessage), "handToPile")]
[JsonDerivedType(typeof(PileMoveMessage), "pileMove")]
[JsonDerivedType(typeof(DeckToPileMessage), "deckToPile")]
[JsonDerivedType(typeof(DrawMessage), "draw")]
[JsonDerivedType(typeof(LifePointsMessage), "lp")]
[JsonDerivedType(typeof(TokenSummonMessage), "token")]
[JsonDerivedType(typeof(CounterMessage), "counter")]
[JsonDerivedType(typeof(ShuffleMessage), "shuffle")]
[JsonDerivedType(typeof(AnnounceMessage), "announce")]
[JsonDerivedType(typeof(RevealCardsMessage), "revealCards")]
[JsonDerivedType(typeof(TurnStateMessage), "turnState")]
[JsonDerivedType(typeof(ChatMessage), "chat")]
[JsonDerivedType(typeof(EmoteMessage), "emote")]
[JsonDerivedType(typeof(AttackMessage), "attack")]
[JsonDerivedType(typeof(ControlSwapMessage), "controlSwap")]
[JsonDerivedType(typeof(ConcedeMessage), "concede")]
[JsonDerivedType(typeof(RematchMessage), "rematch")]
[JsonDerivedType(typeof(LeaveMessage), "leave")]
public abstract class NetMessage { }

// --- Pre-game ---

public sealed class HelloMessage : NetMessage
{
    public string Username { get; set; } = "";
    public int ProtocolVersion { get; set; } = NetProtocol.ProtocolVersion;
}

public sealed class DeckSelectedMessage : NetMessage
{
    public string DeckName { get; set; } = "";
    public List<long> MainIds { get; set; } = [];
    public List<long> ExtraIds { get; set; } = [];
}

public sealed class RpsThrowMessage : NetMessage
{
    public RpsChoice Choice { get; set; }
}

/// <summary>Sent by the RPS winner to declare the turn order.</summary>
public sealed class TurnChoiceMessage : NetMessage
{
    public bool WinnerGoesFirst { get; set; }
}

public sealed class GameStartMessage : NetMessage { }

// --- In-duel: zone/index reference the sender's own side ---

public sealed class SummonMessage : NetMessage
{
    public long CardId { get; set; }
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
    public bool Defense { get; set; }
    /// <summary>Where the card came from, so the opponent removes it from the right
    /// place in their shadow (hand / deck / extra / GY / banished, or a field kind
    /// when it's relocated between zones).</summary>
    public ZoneKind From { get; set; } = ZoneKind.Hand;
}

/// <summary>A face-down card placed to a zone — no id, so the opponent only sees a back.</summary>
public sealed class SetCardMessage : NetMessage
{
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
    public bool Defense { get; set; }
    public ZoneKind From { get; set; } = ZoneKind.Hand;
    /// <summary>Source slot index when <see cref="From"/> is a field zone (relocating a
    /// face-down card). A face-down card carries no id, so the receiver clears the origin
    /// by coordinates rather than by looking it up by passcode.</summary>
    public int FromIndex { get; set; }
}

/// <summary>A previously hidden card turns face-up, revealing its id.</summary>
public sealed class RevealMessage : NetMessage
{
    public long CardId { get; set; }
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
    public bool Defense { get; set; }
}

public sealed class PositionChangeMessage : NetMessage
{
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
    public bool FaceDown { get; set; }
    public bool Defense { get; set; }
}

public sealed class FieldToPileMessage : NetMessage
{
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
    public ZoneKind Pile { get; set; }
    /// <summary>Set when the moved card is/was public (e.g. a face-up card to the GY).</summary>
    public long? CardId { get; set; }
    public bool ToTop { get; set; }
    /// <summary>A token simply vanishes off the field rather than entering a pile.</summary>
    public bool IsToken { get; set; }
}

public sealed class HandToPileMessage : NetMessage
{
    public ZoneKind Pile { get; set; }
    public long? CardId { get; set; }
    public bool ToTop { get; set; }
}

/// <summary>The sender moved one of their cards between two non-field piles — e.g. a
/// Deck search into the hand, or recovering a card from the Graveyard. <see cref="From"/>
/// and <see cref="To"/> are pile kinds (Hand / Deck / ExtraDeck / Graveyard / Banished).
/// <see cref="CardId"/> is set only when a public pile (GY/Banished) is involved, so the
/// receiver can remove the right card and/or reveal it at a public destination; a move
/// into a private zone (like a search into the hand) stays a hidden placeholder.</summary>
public sealed class PileMoveMessage : NetMessage
{
    public ZoneKind From { get; set; }
    public ZoneKind To { get; set; }
    public long? CardId { get; set; }
    public bool ToTop { get; set; }
}

public sealed class DrawMessage : NetMessage
{
    /// <summary>How many cards moved from the deck to the hand.</summary>
    public int Count { get; set; } = 1;
}

/// <summary>The sender milled a card off the top or bottom of their own Deck into a pile
/// (e.g. Lightsworn sending to the Graveyard). The card becomes public there, so its id
/// is included for the receiver to rebuild it.</summary>
public sealed class DeckToPileMessage : NetMessage
{
    public long CardId { get; set; }
    public ZoneKind Pile { get; set; } = ZoneKind.Graveyard;
    public bool FromBottom { get; set; }
}

public sealed class LifePointsMessage : NetMessage
{
    public int LifePoints { get; set; }
}

public sealed class TokenSummonMessage : NetMessage
{
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
    public bool Defense { get; set; }
}

public sealed class CounterMessage : NetMessage
{
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
    public int Counters { get; set; }
}

public sealed class ShuffleMessage : NetMessage { }

/// <summary>Shows a set of the sender's cards to the opponent (single card or whole
/// hand). An empty list closes the reveal viewer (an "unreveal").</summary>
public sealed class RevealCardsMessage : NetMessage
{
    public List<long> CardIds { get; set; } = [];
    public string Label { get; set; } = "reveals";
}

public sealed class AnnounceMessage : NetMessage
{
    public string Verb { get; set; } = "";
    /// <summary>Public description of the target for the log/banner — its name if it's
    /// face-up, else a generic phrase ("a set card on the field" / "a card in hand").</summary>
    public string Target { get; set; } = "";
    /// <summary>Which field the target sits on, so the receiver rings the right card.</summary>
    public AnnounceSide Side { get; set; } = AnnounceSide.None;
    public ZoneKind Zone { get; set; }
    public int Index { get; set; }
}

/// <summary>The full shared turn state after a change, so both clients stay in sync
/// regardless of how it changed. <see cref="ActiveIsSender"/> is resolved to the
/// receiver's own perspective (the sender is their opponent).</summary>
public sealed class TurnStateMessage : NetMessage
{
    public int TurnNumber { get; set; }
    public DuelPhaseWire Phase { get; set; }
    public bool ActiveIsSender { get; set; }
}

public sealed class ChatMessage : NetMessage
{
    public string Text { get; set; } = "";
}

/// <summary>A status emote shown on the sender's portrait ("thinking", "ok",
/// "respond"). An empty string clears it.</summary>
public sealed class EmoteMessage : NetMessage
{
    public string Emote { get; set; } = "";
}

/// <summary>An attack declaration: the sender's monster (AttackerZone/Index on their
/// own side) attacks either the receiver's monster (TargetZone/Index) or, when
/// <see cref="Direct"/> is set, the receiver directly. Purely a visual/table-talk
/// cue — no damage is applied.</summary>
public sealed class AttackMessage : NetMessage
{
    public ZoneKind AttackerZone { get; set; }
    public int AttackerIndex { get; set; }
    public bool Direct { get; set; }
    public ZoneKind TargetZone { get; set; }
    public int TargetIndex { get; set; }
}

/// <summary>A control change ("brain control"): a monster moves to the other player's
/// side, who becomes its owner. Coordinates are the sender's; the receiver mirrors the
/// boards. <see cref="FromSendersField"/> is true when the sender is giving away one of
/// their own monsters, false when taking one of the opponent's.</summary>
public sealed class ControlSwapMessage : NetMessage
{
    public bool FromSendersField { get; set; }
    public ZoneKind SourceZone { get; set; }
    public int SourceIndex { get; set; }
    public ZoneKind DestZone { get; set; }
    public int DestIndex { get; set; }
    public bool FaceDown { get; set; }
    public bool Defense { get; set; }
}

/// <summary>The sender is surrendering the duel — a table-talk gesture that ends the
/// game in the receiver's favour. <see cref="Verb"/> is the past-tense phrase for the
/// log ("conceded", "admitted defeat").</summary>
public sealed class ConcedeMessage : NetMessage
{
    public string Verb { get; set; } = "conceded";
}

/// <summary>Sent from the end screen to ask for a rematch. When both peers have sent
/// one, each restarts the duel with the same decks (the previous loser goes first).</summary>
public sealed class RematchMessage : NetMessage { }

/// <summary>Sent when a peer deliberately leaves (quits to the menu). It tells the other
/// side the match is over so they don't sit waiting to reconnect — an abrupt drop, by
/// contrast, has no Leave and triggers the reconnect flow.</summary>
public sealed class LeaveMessage : NetMessage { }

/// <summary>Wire copy of the phase enum so the protocol doesn't depend on the
/// Services namespace layout (kept in sync with <c>DuelPhase</c>).</summary>
public enum DuelPhaseWire { Draw, Standby, Main1, Battle, Main2, End }
