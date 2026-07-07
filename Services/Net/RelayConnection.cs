using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace YGODuelSimulator.Services.Net;

/// <summary>
/// A duel link over the cloud relay for cross-network play. Both peers dial *out* to a
/// Cloudflare Worker (see the <c>relay/</c> folder) over WebSocket, so it punches through
/// home routers/NAT with no port forwarding. Each <see cref="NetMessage"/> is one JSON
/// text frame — WebSocket already delimits messages, so no length framing is needed.
///
/// A short room code pairs the two peers: the host creates the room and waits; the joiner
/// connects to the same code. Once both are present the relay sends a control frame that
/// completes <see cref="HostAsync"/>/<see cref="JoinAsync"/>.
/// </summary>
public sealed class RelayConnection : IDuelConnection
{
    private readonly Dispatcher _dispatcher;
    private readonly ClientWebSocket _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private bool _disconnectedRaised;

    public event Action<NetMessage>? MessageReceived;
    public event Action? Disconnected;

    public bool IsConnected => _ws.State == WebSocketState.Open;

    private RelayConnection(ClientWebSocket ws, Dispatcher dispatcher)
    {
        _ws = ws;
        _dispatcher = dispatcher;
    }

    /// <summary>Create a room with the given code and wait for a joiner to arrive.</summary>
    public static Task<RelayConnection> HostAsync(string code, Dispatcher dispatcher, CancellationToken ct)
        => ConnectAsync(code, "host", dispatcher, ct);

    /// <summary>Join an existing room by code.</summary>
    public static Task<RelayConnection> JoinAsync(string code, Dispatcher dispatcher, CancellationToken ct)
        => ConnectAsync(code, "join", dispatcher, ct);

    private static async Task<RelayConnection> ConnectAsync(string code, string role, Dispatcher dispatcher, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        ws.Options.CollectHttpResponseDetails = true; // surfaces the reject status below
        var uri = new Uri($"{NetProtocol.RelayBaseUrl}/room/{Uri.EscapeDataString(code)}?role={role}");
        try
        {
            await ws.ConnectAsync(uri, ct);
        }
        catch (WebSocketException) when (ws.HttpStatusCode != 0)
        {
            ws.Dispose();
            throw new RelayException(ws.HttpStatusCode, role);
        }

        var conn = new RelayConnection(ws, dispatcher);
        try
        {
            // Host blocks here until the joiner arrives; joiner returns almost immediately.
            await conn.WaitForStartAsync(ct);
        }
        catch
        {
            conn.Dispose(); // cancelled (Back) or the relay closed early — don't leak the socket
            throw;
        }
        return conn;
    }

    public void Start() => _ = ReceiveLoopAsync(_cts.Token);

    public async Task SendAsync(NetMessage message)
    {
        if (_ws.State != WebSocketState.Open) return;
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, NetProtocol.Json);

        await _sendLock.WaitAsync();
        try
        {
            await _ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, _cts.Token);
        }
        catch
        {
            RaiseDisconnected();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Send(NetMessage message) => _ = SendAsync(message);

    /// <summary>Reads frames until the relay's <c>start</c> control frame, which means both
    /// peers are connected. Anything else before that is ignored.</summary>
    private async Task WaitForStartAsync(CancellationToken ct)
    {
        while (true)
        {
            var text = await ReceiveTextAsync(ct);
            if (text is null) throw new IOException("The relay closed before the match started.");
            if (IsRelayEvent(text, "start")) return;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(ct);
                if (text is null) break;               // peer/relay closed
                if (IsRelayEvent(text, null)) continue; // relay control frame, not a game message

                NetMessage? message;
                try { message = JsonSerializer.Deserialize<NetMessage>(text, NetProtocol.Json); }
                catch { continue; }

                if (message is not null)
                    _ = _dispatcher.BeginInvoke(() => MessageReceived?.Invoke(message));
            }
        }
        catch
        {
            // Any socket/parse failure ends the session.
        }
        RaiseDisconnected();
    }

    /// <summary>Reads one full WebSocket text message (reassembling fragments), or null on close.</summary>
    private async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            WebSocketReceiveResult result;
            try { result = await _ws.ReceiveAsync(buffer, ct); }
            catch { return null; }

            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (ms.Length + result.Count > NetProtocol.MaxMessageBytes) return null;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>True if <paramref name="text"/> is a relay control frame
    /// (<c>{"type":"__relay",...}</c>); when <paramref name="evt"/> is given, also that its
    /// <c>event</c> matches.</summary>
    private static bool IsRelayEvent(string text, string? evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "__relay") return false;
            if (evt is null) return true;
            return root.TryGetProperty("event", out var e) && e.GetString() == evt;
        }
        catch { return false; }
    }

    private void RaiseDisconnected()
    {
        if (_disconnectedRaised) return;
        _disconnectedRaised = true;
        _dispatcher.BeginInvoke(() => Disconnected?.Invoke());
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _ws.Abort(); } catch { }
        _ws.Dispose();
        _sendLock.Dispose();
        _cts.Dispose();
    }
}

/// <summary>A relay handshake rejection, mapped to a message the overlay can show.</summary>
public sealed class RelayException(HttpStatusCode status, string role)
    : Exception(MessageFor(status, role))
{
    public HttpStatusCode Status { get; } = status;

    private static string MessageFor(HttpStatusCode status, string role) => (status, role) switch
    {
        (HttpStatusCode.NotFound, _) => "No room found with that code.",
        (HttpStatusCode.Conflict, "host") => "That room code is already in use — try again.",
        (HttpStatusCode.Conflict, _) => "That room is already full.",
        _ => $"Could not reach the relay (error {(int)status}).",
    };
}
