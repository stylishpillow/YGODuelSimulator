using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Threading;

namespace YGODuelSimulator.Services.Net;

/// <summary>The payload a host broadcasts so joiners can find its room.</summary>
public sealed class RoomBeacon
{
    public string RoomId { get; set; } = "";
    public string HostName { get; set; } = "";
    public int GamePort { get; set; }
    public int ProtocolVersion { get; set; } = NetProtocol.ProtocolVersion;
}

/// <summary>A room seen on the network, with the address to connect to.</summary>
public sealed class DiscoveredRoom
{
    public required string RoomId { get; init; }
    public required string HostName { get; init; }
    public required IPAddress Address { get; init; }
    public required int GamePort { get; init; }
    public DateTime LastSeenUtc { get; set; }
}

/// <summary>
/// LAN room discovery over UDP broadcast. A host repeatedly broadcasts a
/// <see cref="RoomBeacon"/>; joiners listen and maintain a live, self-expiring list.
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RoomTimeout = TimeSpan.FromSeconds(4);

    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cts;
    private UdpClient? _udp;

    private readonly Dictionary<string, DiscoveredRoom> _rooms = new();
    private DispatcherTimer? _expiryTimer;

    /// <summary>Raised (on the UI thread) whenever the discovered room list changes.</summary>
    public event Action? RoomsChanged;

    public LanDiscovery(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public IReadOnlyList<DiscoveredRoom> Rooms
    {
        get { lock (_rooms) return _rooms.Values.OrderBy(r => r.HostName).ToList(); }
    }

    /// <summary>Host: broadcast the given beacon on the LAN until stopped.</summary>
    public void StartBroadcasting(RoomBeacon beacon)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = BroadcastLoopAsync(beacon, _cts.Token);
    }

    /// <summary>Joiner: listen for beacons and keep the room list current.</summary>
    public void StartListening()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = ListenLoopAsync(_cts.Token);

        _expiryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _expiryTimer.Tick += (_, _) => ExpireStale();
        _expiryTimer.Start();
    }

    private async Task BroadcastLoopAsync(RoomBeacon beacon, CancellationToken ct)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        var endpoint = new IPEndPoint(IPAddress.Broadcast, NetProtocol.DiscoveryPort);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(beacon, NetProtocol.Json);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await udp.SendAsync(bytes, bytes.Length, endpoint); } catch { }
                await Task.Delay(BroadcastInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        try
        {
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, NetProtocol.DiscoveryPort));
        }
        catch
        {
            return; // another listener already holds the port
        }

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _udp.ReceiveAsync(ct); }
            catch { break; }

            RoomBeacon? beacon;
            try { beacon = JsonSerializer.Deserialize<RoomBeacon>(result.Buffer, NetProtocol.Json); }
            catch { continue; }
            if (beacon is null || beacon.ProtocolVersion != NetProtocol.ProtocolVersion) continue;

            var room = new DiscoveredRoom
            {
                RoomId = beacon.RoomId,
                HostName = beacon.HostName,
                Address = result.RemoteEndPoint.Address,
                GamePort = beacon.GamePort,
                LastSeenUtc = DateTime.UtcNow,
            };

            bool changed;
            lock (_rooms)
            {
                changed = !_rooms.ContainsKey(room.RoomId);
                _rooms[room.RoomId] = room;
            }
            if (changed) _ = _dispatcher.BeginInvoke(() => RoomsChanged?.Invoke());
        }
    }

    private void ExpireStale()
    {
        var now = DateTime.UtcNow;
        bool removed;
        lock (_rooms)
        {
            var stale = _rooms.Where(kv => now - kv.Value.LastSeenUtc > RoomTimeout)
                              .Select(kv => kv.Key).ToList();
            foreach (var key in stale) _rooms.Remove(key);
            removed = stale.Count > 0;
        }
        if (removed) RoomsChanged?.Invoke();
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        _expiryTimer?.Stop();
        _expiryTimer = null;
        _udp?.Dispose();
        _udp = null;
        lock (_rooms) _rooms.Clear();
    }

    public void Dispose() => Stop();
}
