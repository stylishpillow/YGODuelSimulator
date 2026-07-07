using System.ComponentModel;
using System.Windows.Threading;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Services.Net;

public enum MatchPhase { Idle, Lobby, Connecting, DeckSelect, Rps, ChooseOrder, InDuel, Disconnected }

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
    private P2PConnection? _connection;
    private CancellationTokenSource? _connectCts;

    public string LocalName { get; }
    public bool IsHost { get; private set; }

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

    private void OnConnected(P2PConnection conn)
    {
        _connection = conn;
        conn.MessageReceived += OnMessage;
        conn.Disconnected += OnDisconnected;
        conn.Send(new HelloMessage { Username = LocalName });
        Phase = MatchPhase.DeckSelect;
        SetStatus(null);
    }

    // --- Deck select ---

    public void SelectDeck(DeckSelectedMessage deck)
    {
        _localDeck = deck;
        _connection?.Send(deck);
        Raise(nameof(LocalDeckReady));
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

            default:
                if (Phase == MatchPhase.InDuel) DuelMessage?.Invoke(message);
                break;
        }
    }

    private void OnDisconnected()
    {
        if (Phase is MatchPhase.Idle or MatchPhase.Disconnected) return;
        Phase = MatchPhase.Disconnected;
        SetStatus($"{OpponentName ?? "Opponent"} disconnected.");
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
        try { _connectCts?.Cancel(); } catch { }
        _discovery.Dispose();
        _connection?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Everything the duel needs when the match begins.</summary>
public sealed record GameStartInfo(DeckSelectedMessage LocalDeck, DeckSelectedMessage RemoteDeck, bool LocalGoesFirst);
