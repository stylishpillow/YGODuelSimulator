using System.ComponentModel;
using System.Net;
using System.Windows.Threading;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Services.Net;

public enum MatchPhase { Idle, Lobby, Connecting, DeckSelect, Rps, ChooseOrder, InDuel, Reconnecting, Disconnected }

/// <summary>
/// Drives a networked match from discovery through the pre-game handshake and into
/// the live duel. Owns the <see cref="P2PConnection"/> and <see cref="LanDiscovery"/>,
/// runs the pre-game state machine (hello → deck exchange → RPS → turn order), and
/// once in the duel is a plain message pipe (<see cref="Send"/> / <see cref="DuelMessage"/>).
/// </summary>
public sealed class DuelSession : INotifyPropertyChanged, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LanDiscovery _discovery;
    private IDuelConnection? _connection;
    private CancellationTokenSource? _connectCts;

    // How we connected, remembered so a dropped in-duel link can be rebuilt without
    // redoing the pre-game handshake (the boards are preserved in memory on both sides).
    private bool _isRelay;
    private string? _relayCode;
    private IPAddress? _lanAddress;
    private int _lanPort;
    private bool _duelStarted;               // reconnect only applies once a duel is underway
    private bool _leaving;                    // a deliberate quit — don't try to reconnect
    private CancellationTokenSource? _reconnectCts;

    public string LocalName { get; }
    public bool IsHost { get; private set; }

    /// <summary>The room code this client is hosting online, shown so the host can share it.</summary>
    public string? RoomCode { get; private set; }

    private MatchPhase _phase = MatchPhase.Idle;
    public MatchPhase Phase
    {
        get => _phase;
        private set { _phase = value; Changed?.Invoke(); Raise(nameof(Phase)); }
    }

    public string? OpponentName { get; private set; }
    public string? StatusMessage { get; private set; }

    // Deck selection
    private DeckSelectedMessage? _localDeck;
    private DeckSelectedMessage? _remoteDeck;
    public bool LocalDeckReady => _localDeck is not null;
    public bool OpponentDeckReady => _remoteDeck is not null;

    // Rock-paper-scissors
    private RpsChoice? _localRps;
    private RpsChoice? _remoteRps;
    public bool? LocalWonRps { get; private set; }
    public string? RpsMessage { get; private set; }
    public bool LocalRpsThrown => _localRps is not null;

    // Turn order (resolved when the winner chooses)
    public bool? LocalGoesFirst { get; private set; }

    /// <summary>Raised on any state change so the overlay UI can refresh.</summary>
    public event Action? Changed;
    /// <summary>Raised with in-duel messages once the match has started.</summary>
    public event Action<NetMessage>? DuelMessage;
    /// <summary>Raised once both sides are ready to enter the duel.</summary>
    public event Action<GameStartInfo>? GameStarting;
    /// <summary>Raised when the link drops mid-duel and reconnection begins.</summary>
    public event Action? Reconnecting;
    /// <summary>Raised when a dropped duel link is re-established and play resumes.</summary>
    public event Action? Reconnected;

    public DuelSession(Dispatcher dispatcher, string localName)
    {
        _dispatcher = dispatcher;
        LocalName = localName;
        _discovery = new LanDiscovery(dispatcher);
        _discovery.RoomsChanged += () => Changed?.Invoke();
    }

    public IReadOnlyList<DiscoveredRoom> Rooms => _discovery.Rooms;

    // --- Lobby ---

    /// <summary>Start listening for rooms on the LAN.</summary>
    public void EnterLobby()
    {
        Phase = MatchPhase.Lobby;
        _discovery.StartListening();
    }

    public async void Host()
    {
        IsHost = true;
        _isRelay = false;
        _lanPort = NetProtocol.DefaultGamePort;
        Phase = MatchPhase.Connecting;
        SetStatus("Waiting for an opponent to join…");

        var roomId = Guid.NewGuid().ToString("N");
        _discovery.StartBroadcasting(new RoomBeacon
        {
            RoomId = roomId,
            HostName = LocalName,
            GamePort = NetProtocol.DefaultGamePort,
        });

        _connectCts = new CancellationTokenSource();
        try
        {
            var conn = await P2PConnection.HostAsync(NetProtocol.DefaultGamePort, _dispatcher, _connectCts.Token);
            _discovery.Stop();
            OnConnected(conn);
        }
        catch (Exception ex)
        {
            _discovery.Stop();
            Fail($"Could not host: {ex.Message}");
        }
    }

    public async void Join(DiscoveredRoom room)
    {
        IsHost = false;
        _isRelay = false;
        _lanAddress = room.Address;
        _lanPort = room.GamePort;
        Phase = MatchPhase.Connecting;
        SetStatus($"Connecting to {room.HostName}…");
        _discovery.Stop();

        _connectCts = new CancellationTokenSource();
        try
        {
            var conn = await P2PConnection.JoinAsync(room.Address, room.GamePort, _dispatcher, _connectCts.Token);
            OnConnected(conn);
        }
        catch (Exception ex)
        {
            Fail($"Could not join: {ex.Message}");
        }
    }

    // --- Online (cross-network via the cloud relay) ---

    /// <summary>Host an Internet game: mints a room code and waits for a joiner on the relay.</summary>
    public async void HostOnline()
    {
        IsHost = true;
        _isRelay = true;
        RoomCode = GenerateRoomCode();
        _relayCode = RoomCode;
        Raise(nameof(RoomCode));
        Phase = MatchPhase.Connecting;
        SetStatus($"Room code: {RoomCode}\nShare it with your opponent — waiting for them to join…");

        _connectCts = new CancellationTokenSource();
        try
        {
            var conn = await RelayConnection.HostAsync(RoomCode, _dispatcher, _connectCts.Token);
            OnConnected(conn);
        }
        catch (OperationCanceledException) { /* cancelled by Back */ }
        catch (Exception ex)
        {
            Fail($"Could not host online: {ex.Message}");
        }
    }

    /// <summary>Join an Internet game by the code the host shared.</summary>
    public async void JoinOnline(string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            Fail("Enter the room code your opponent shared.");
            return;
        }

        IsHost = false;
        _isRelay = true;
        _relayCode = code;
        Phase = MatchPhase.Connecting;
        SetStatus($"Joining room {code}…");

        _connectCts = new CancellationTokenSource();
        try
        {
            var conn = await RelayConnection.JoinAsync(code, _dispatcher, _connectCts.Token);
            OnConnected(conn);
        }
        catch (OperationCanceledException) { /* cancelled by Back */ }
        catch (Exception ex)
        {
            Fail($"Could not join: {ex.Message}");
        }
    }

    /// <summary>A short, human-shareable code using unambiguous characters (no 0/O/1/I).</summary>
    private static string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[4];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(chars);
    }

    private void OnConnected(IDuelConnection conn)
    {
        _connection = conn;
        conn.MessageReceived += OnMessage;
        conn.Disconnected += OnDisconnected;
        conn.Send(new HelloMessage { Username = LocalName });
        conn.Start(); // begin receiving only after the handler is attached
        Phase = MatchPhase.DeckSelect;
        SetStatus(null);
    }

    // --- Deck select ---

    public void SelectDeck(DeckSelectedMessage deck)
    {
        _localDeck = deck;
        _connection?.Send(deck);
        Raise(nameof(LocalDeckReady));
        Changed?.Invoke();   // refresh the overlay to show "You: ready"
        TryStartRps();
    }

    private void TryStartRps()
    {
        if (_localDeck is not null && _remoteDeck is not null && Phase == MatchPhase.DeckSelect)
        {
            ResetRps();
            Phase = MatchPhase.Rps;
        }
    }

    // --- Rock-paper-scissors ---

    public void ThrowRps(RpsChoice choice)
    {
        if (_localRps is not null) return; // already thrown this round
        _localRps = choice;
        _connection?.Send(new RpsThrowMessage { Choice = choice });
        Changed?.Invoke();
        ResolveRps();
    }

    private void ResolveRps()
    {
        if (_localRps is not { } mine || _remoteRps is not { } theirs) return;

        if (mine == theirs)
        {
            RpsMessage = "Tie — throw again!";
            _localRps = null;
            _remoteRps = null;
            Changed?.Invoke();
            return;
        }

        var localWins = (mine, theirs) switch
        {
            (RpsChoice.Rock, RpsChoice.Scissors) => true,
            (RpsChoice.Paper, RpsChoice.Rock) => true,
            (RpsChoice.Scissors, RpsChoice.Paper) => true,
            _ => false,
        };
        LocalWonRps = localWins;
        RpsMessage = localWins
            ? "You won! Choose to go first or second."
            : $"{OpponentName ?? "Opponent"} won and is choosing who goes first…";
        Phase = MatchPhase.ChooseOrder;
    }

    private void ResetRps()
    {
        _localRps = null;
        _remoteRps = null;
        LocalWonRps = null;
        RpsMessage = null;
    }

    // --- Turn order ---

    /// <summary>The RPS winner picks whether they take the first turn.</summary>
    public void ChooseOrder(bool goFirst)
    {
        if (LocalWonRps != true) return;
        LocalGoesFirst = goFirst;
        _connection?.Send(new TurnChoiceMessage { WinnerGoesFirst = goFirst });
        StartDuel();
    }

    private void StartDuel()
    {
        if (_localDeck is null || _remoteDeck is null || LocalGoesFirst is not { } first) return;
        _duelStarted = true;
        Phase = MatchPhase.InDuel;
        GameStarting?.Invoke(new GameStartInfo(_localDeck, _remoteDeck, first));
    }

    // --- In-duel pipe ---

    public void Send(NetMessage message) => _connection?.Send(message);

    // --- Incoming messages ---

    private void OnMessage(NetMessage message)
    {
        switch (message)
        {
            case HelloMessage hello:
                OpponentName = hello.Username;
                Raise(nameof(OpponentName));
                Changed?.Invoke();
                break;

            case DeckSelectedMessage deck:
                _remoteDeck = deck;
                Raise(nameof(OpponentDeckReady));
                Changed?.Invoke();
                TryStartRps();
                break;

            case RpsThrowMessage rps:
                _remoteRps = rps.Choice;
                ResolveRps();
                break;

            case TurnChoiceMessage choice:
                // The remote winner declared the order; we are the loser here.
                LocalGoesFirst = !choice.WinnerGoesFirst;
                StartDuel();
                break;

            case LeaveMessage:
                // The opponent deliberately quit — terminal, so don't reconnect.
                _leaving = true;
                _reconnectCts?.Cancel();
                Phase = MatchPhase.Disconnected;
                SetStatus($"{OpponentName ?? "Opponent"} left the duel.");
                break;

            default:
                if (Phase is MatchPhase.InDuel) DuelMessage?.Invoke(message);
                break;
        }
    }

    private void OnDisconnected()
    {
        if (Phase is MatchPhase.Idle or MatchPhase.Disconnected or MatchPhase.Reconnecting) return;
        // A drop mid-duel (not a deliberate leave) is recoverable: both sides still hold
        // the full board in memory, so we just rebuild the transport and resume.
        if (!_leaving && _duelStarted)
        {
            BeginReconnect();
            return;
        }
        Phase = MatchPhase.Disconnected;
        SetStatus($"{OpponentName ?? "Opponent"} disconnected.");
    }

    // --- Reconnect ---

    /// <summary>Rebuilds the dropped link (same role, same address/room) and, on success,
    /// resumes the duel in place. Retries until it connects or the window elapses.</summary>
    private async void BeginReconnect()
    {
        _reconnectCts?.Cancel();
        var cts = _reconnectCts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(45));
        var ct = cts.Token;

        try { _connection?.Dispose(); } catch { }
        _connection = null;

        Phase = MatchPhase.Reconnecting;
        SetStatus("Connection lost — reconnecting…");
        Reconnecting?.Invoke();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                IDuelConnection conn = (_isRelay, IsHost) switch
                {
                    (true, true) => await RelayConnection.HostAsync(_relayCode!, _dispatcher, ct),
                    (true, false) => await RelayConnection.JoinAsync(_relayCode!, _dispatcher, ct),
                    (false, true) => await P2PConnection.HostAsync(_lanPort, _dispatcher, ct),
                    (false, false) => await P2PConnection.JoinAsync(_lanAddress!, _lanPort, _dispatcher, ct),
                };
                ResumeConnection(conn);
                return;
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Peer not back yet (host not listening / relay room reforming) — wait, retry.
                try { await Task.Delay(1000, ct); } catch { break; }
            }
        }

        if (!_leaving)
        {
            Phase = MatchPhase.Disconnected;
            SetStatus($"Couldn't reconnect to {OpponentName ?? "your opponent"}.");
        }
    }

    private void ResumeConnection(IDuelConnection conn)
    {
        _connection = conn;
        conn.MessageReceived += OnMessage;
        conn.Disconnected += OnDisconnected;
        Phase = MatchPhase.InDuel;   // DuelState was preserved on both sides — just resume
        SetStatus(null);
        conn.Start();
        Reconnected?.Invoke();
    }

    /// <summary>Tell the peer we're leaving on purpose, then tear down. Flushes the Leave
    /// before closing so the peer treats it as terminal rather than reconnecting.</summary>
    public async void LeaveAndDispose()
    {
        _leaving = true;
        _reconnectCts?.Cancel();
        try
        {
            if (_connection is { } c) await c.SendAsync(new LeaveMessage());
        }
        catch { /* best effort */ }
        Dispose();
    }

    private void Fail(string message)
    {
        Phase = MatchPhase.Disconnected;
        SetStatus(message);
    }

    private void SetStatus(string? message)
    {
        StatusMessage = message;
        Raise(nameof(StatusMessage));
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _leaving = true;
        try { _connectCts?.Cancel(); } catch { }
        try { _reconnectCts?.Cancel(); } catch { }
        _discovery.Dispose();
        _connection?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Everything the duel needs when the match begins.</summary>
public sealed record GameStartInfo(DeckSelectedMessage LocalDeck, DeckSelectedMessage RemoteDeck, bool LocalGoesFirst);
