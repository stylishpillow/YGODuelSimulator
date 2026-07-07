using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Threading;

namespace YGODuelSimulator.Services.Net;

/// <summary>
/// One duplex TCP link between two peers. Messages are framed as a 4-byte big-endian
/// length followed by UTF-8 JSON. A background loop reads messages and raises
/// <see cref="MessageReceived"/> / <see cref="Disconnected"/> on the UI thread.
/// </summary>
public sealed class P2PConnection : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private bool _disconnectedRaised;

    public event Action<NetMessage>? MessageReceived;
    public event Action? Disconnected;

    public bool IsConnected => _client?.Connected == true;

    private P2PConnection(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Listens on <paramref name="port"/> and returns once one peer connects.</summary>
    public static async Task<P2PConnection> HostAsync(int port, Dispatcher dispatcher, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        try
        {
            var client = await listener.AcceptTcpClientAsync(ct);
            var conn = new P2PConnection(dispatcher);
            conn.Attach(client);
            return conn;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Connects to a hosting peer.</summary>
    public static async Task<P2PConnection> JoinAsync(IPAddress address, int port, Dispatcher dispatcher, CancellationToken ct)
    {
        var client = new TcpClient();
        await client.ConnectAsync(address, port, ct);
        var conn = new P2PConnection(dispatcher);
        conn.Attach(client);
        return conn;
    }

    private void Attach(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        _cts = new CancellationTokenSource();
    }

    /// <summary>Begins reading messages. Call this only after subscribing to
    /// <see cref="MessageReceived"/>, so the very first message isn't missed.</summary>
    public void Start()
    {
        if (_cts is not null) _ = ReceiveLoopAsync(_cts.Token);
    }

    public async Task SendAsync(NetMessage message)
    {
        var stream = _stream;
        if (stream is null) return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, NetProtocol.Json);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);

        await _sendLock.WaitAsync();
        try
        {
            await stream.WriteAsync(header);
            await stream.WriteAsync(payload);
            await stream.FlushAsync();
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

    /// <summary>Fire-and-forget send that never throws to the caller.</summary>
    public void Send(NetMessage message) => _ = SendAsync(message);

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var header = new byte[4];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactAsync(header, ct)) break;
                var length = BinaryPrimitives.ReadInt32BigEndian(header);
                if (length <= 0 || length > NetProtocol.MaxMessageBytes) break;

                var payload = new byte[length];
                if (!await ReadExactAsync(payload, ct)) break;

                var message = JsonSerializer.Deserialize<NetMessage>(payload, NetProtocol.Json);
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

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) return false; // peer closed
            offset += read;
        }
        return true;
    }

    private void RaiseDisconnected()
    {
        if (_disconnectedRaised) return;
        _disconnectedRaised = true;
        _dispatcher.BeginInvoke(() => Disconnected?.Invoke());
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _stream?.Dispose();
        _client?.Close();
        _sendLock.Dispose();
    }
}
