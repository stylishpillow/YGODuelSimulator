namespace YGODuelSimulator.Services.Net;

/// <summary>
/// A duplex message pipe between the two peers. Implemented by <see cref="P2PConnection"/>
/// (direct TCP on a LAN) and <see cref="RelayConnection"/> (WebSocket through the cloud
/// relay for cross-network play). <see cref="DuelSession"/> talks only to this interface,
/// so the handshake and in-duel sync are identical regardless of transport.
/// </summary>
public interface IDuelConnection : IDisposable
{
    /// <summary>Raised on the UI thread for each message received from the peer.</summary>
    event Action<NetMessage>? MessageReceived;

    /// <summary>Raised on the UI thread when the link drops (once).</summary>
    event Action? Disconnected;

    bool IsConnected { get; }

    /// <summary>Begins the receive loop. Call only after subscribing to
    /// <see cref="MessageReceived"/> so the first message isn't missed.</summary>
    void Start();

    Task SendAsync(NetMessage message);

    /// <summary>Fire-and-forget send that never throws to the caller.</summary>
    void Send(NetMessage message);
}
