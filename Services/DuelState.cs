using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using YGODuelSimulator.Models;
using YGODuelSimulator.Models.Duel;

namespace YGODuelSimulator.Services
{
    /// <summary>The phases of a turn (rulebook p.34).</summary>
    public enum DuelPhase { Draw, Standby, Main1, Battle, Main2, End }

    /// <summary>Who a log line is attributed to, for colour-coding: a structural/system
    /// line (neutral), the local player (blue), or the opponent (red). Both actions and
    /// chat are coloured by side; only truly shared lines are <see cref="System"/>.</summary>
    public enum DuelLogSide { System, Player, Opponent }

    /// <summary>One line in the duel log: a timestamp, the text, its side (which drives
    /// the colour — system lines are neutral, a player's are blue/red), and whether it's
    /// a chat line (chat is emphasised so it stands out from action lines of the same
    /// colour).</summary>
    public sealed record DuelLogEntry(string Time, string Text, DuelLogSide Side, bool Chat = false);

    /// <summary>
    /// A two-player manual duel: two <see cref="PlayerBoard"/> halves plus the
    /// shared turn/phase tracker and the card-selection and move operations that
    /// can cross between the two boards. No rules are enforced — the phase tracker
    /// is a guide, not a referee.
    /// </summary>
    public class DuelState : INotifyPropertyChanged
    {
        private readonly Random _rng = new();
        private readonly CardImageService _images = new();

        public PlayerBoard Player { get; }
        public PlayerBoard Opponent { get; }

        public DuelState()
        {
            Player = new PlayerBoard(PlayerSide.Player, _rng, _images);
            Opponent = new PlayerBoard(PlayerSide.Opponent, _rng, _images);
        }

        public PlayerBoard BoardFor(PlayerSide side) =>
            side == PlayerSide.Player ? Player : Opponent;

        // --- Action / chat log (a timestamped feed of everything that happens) ---

        /// <summary>The running log shown in the side panel: every action and chat
        /// line, oldest first.</summary>
        public ObservableCollection<DuelLogEntry> LogEntries { get; } = [];

        /// <summary>Records a neutral, shared line such as a turn/phase change.</summary>
        public void Log(string message) => AddEntry(new DuelLogEntry(Now(), message, DuelLogSide.System));

        /// <summary>Records a line attributed to a side, coloured accordingly (an action
        /// such as "Alice Summoned Dark Magician").</summary>
        public void Log(string message, DuelLogSide side) => AddEntry(new DuelLogEntry(Now(), message, side));

        /// <summary>Records a typed chat line such as "Alice: gg", coloured by side and
        /// emphasised so it reads apart from action lines.</summary>
        public void LogChat(string who, string text, DuelLogSide side) =>
            AddEntry(new DuelLogEntry(Now(), $"{who}: {text}", side, Chat: true));

        private void AddEntry(DuelLogEntry entry)
        {
            LogEntries.Add(entry);
            // Keep the feed bounded so a long game doesn't grow without limit.
            while (LogEntries.Count > 300) LogEntries.RemoveAt(0);
        }

        private static string Now() => DateTime.Now.ToString("HH:mm:ss");

        // --- Table talk: pointing at / declaring on a card (two-player cues) ---

        private string? _announcement;
        /// <summary>A transient banner such as "Player declares the effect of …".</summary>
        public string? Announcement
        {
            get => _announcement;
            set { _announcement = value; Raise(nameof(Announcement)); Raise(nameof(AnnouncementVisibility)); }
        }

        public Visibility AnnouncementVisibility =>
            string.IsNullOrEmpty(_announcement) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Sets the banner to "&lt;owner&gt; &lt;verb&gt; &lt;card&gt;".</summary>
        public void Announce(BoardCard card, string verb)
        {
            var who = FindBoard(card).Side == PlayerSide.Player ? "Player" : "Opponent";
            Announcement = $"{who} {verb} {card.Name}";
        }

        // --- Selection ---

        private BoardCard? _selected;
        /// <summary>The card the player has clicked to act on.</summary>
        public BoardCard? Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                if (_selected is not null) _selected.IsSelected = false;
                _selected = value;
                if (_selected is not null) _selected.IsSelected = true;
            }
        }

        // --- Turn / phase tracking (guidance only) ---

        private int _turnNumber = 1;
        public int TurnNumber
        {
            get => _turnNumber;
            set { _turnNumber = value; Raise(nameof(TurnNumber)); Raise(nameof(TurnSummary)); }
        }

        private PlayerSide _activeSide = PlayerSide.Player;
        public PlayerSide ActiveSide
        {
            get => _activeSide;
            set { _activeSide = value; Raise(nameof(ActiveSide)); Raise(nameof(TurnSummary)); }
        }

        private DuelPhase _phase = DuelPhase.Main1;
        public DuelPhase Phase
        {
            get => _phase;
            set { _phase = value; Raise(nameof(Phase)); Raise(nameof(TurnSummary)); }
        }

        public PlayerBoard ActiveBoard => BoardFor(ActiveSide);

        /// <summary>A short "Turn 3 — Player — Main Phase 1" line for the toolbar.</summary>
        public string TurnSummary =>
            $"Turn {TurnNumber} · {(ActiveSide == PlayerSide.Player ? "Player" : "Opponent")} · {PhaseName(Phase)}";

        private static string PhaseName(DuelPhase p) => p switch
        {
            DuelPhase.Draw => "Draw Phase",
            DuelPhase.Standby => "Standby Phase",
            DuelPhase.Main1 => "Main Phase 1",
            DuelPhase.Battle => "Battle Phase",
            DuelPhase.Main2 => "Main Phase 2",
            _ => "End Phase",
        };

        /// <summary>Advances to the next phase, wrapping End → the next player's turn.</summary>
        public void NextPhase()
        {
            if (Phase == DuelPhase.End) { EndTurn(); return; }
            Phase = (DuelPhase)((int)Phase + 1);
        }

        /// <summary>Passes the turn to the other player and resets to their Draw Phase.</summary>
        public void EndTurn()
        {
            ActiveSide = ActiveSide == PlayerSide.Player ? PlayerSide.Opponent : PlayerSide.Player;
            TurnNumber++;
            Phase = DuelPhase.Draw;
        }

        // --- Moves (may cross between boards) ---

        /// <summary>Places a card into a single-card zone if it's empty.</summary>
        public bool MoveToSlot(BoardCard card, ZoneSlot slot)
        {
            if (!slot.IsEmpty) return false;
            RemoveFromCurrent(card);
            slot.Card = card;
            return true;
        }

        public void MoveToPile(BoardCard card, ObservableCollection<BoardCard> pile, bool toTop = false)
        {
            RemoveFromCurrent(card);
            // Tokens cease to exist anywhere but the field (rulebook p.49).
            if (card.IsToken) return;
            if (toTop) pile.Insert(0, card);
            else pile.Add(card);
        }

        private void RemoveFromCurrent(BoardCard card)
        {
            foreach (var board in Boards())
            {
                if (board.Hand.Remove(card) || board.Deck.Remove(card) || board.ExtraDeck.Remove(card)
                    || board.Graveyard.Remove(card) || board.Banished.Remove(card)) return;

                foreach (var slot in board.AllSlots())
                    if (ReferenceEquals(slot.Card, card)) { slot.Card = null; return; }
            }
        }

        // --- Placement highlighting ---

        /// <summary>Marks every empty zone of the given kinds, on the given board, as a
        /// valid placement target so the board can highlight where the card may go.</summary>
        public void HighlightTargets(PlayerBoard board, params ZoneKind[] kinds)
        {
            ClearHighlights();
            foreach (var slot in board.AllSlots())
                slot.IsTarget = slot.IsEmpty && kinds.Contains(slot.Kind);
        }

        public void ClearHighlights()
        {
            foreach (var board in Boards())
                foreach (var slot in board.AllSlots())
                    slot.IsTarget = false;
        }

        /// <summary>The board that currently holds the card, or the active board if it
        /// can't be found (e.g. a card in hand belongs to its owner's board).</summary>
        public PlayerBoard FindBoard(BoardCard card)
        {
            foreach (var board in Boards())
            {
                if (board.Hand.Contains(card) || board.Deck.Contains(card) || board.ExtraDeck.Contains(card)
                    || board.Graveyard.Contains(card) || board.Banished.Contains(card)) return board;
                foreach (var slot in board.AllSlots())
                    if (ReferenceEquals(slot.Card, card)) return board;
            }
            return ActiveBoard;
        }

        private IEnumerable<PlayerBoard> Boards() { yield return Player; yield return Opponent; }

        // --- Tokens & dice ---

        /// <summary>Creates a Monster Token on the given board's hand (the player then
        /// summons it to a zone). Tokens are treated as Normal Monsters (rulebook p.49).</summary>
        public BoardCard CreateToken(PlayerBoard board)
        {
            var token = new BoardCard(new Card { Id = 0, Name = "Token" }) { IsToken = true };
            board.Hand.Add(token);
            return token;
        }

        public int RollDie() => _rng.Next(1, 7);
        public bool FlipCoin() => _rng.Next(2) == 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
