using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Models;
using YGODuelSimulator.Models.Duel;
using YGODuelSimulator.Services;
using YGODuelSimulator.Services.Net;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>
    /// Local two-player manual duel playfield, driven the Dueling Book way:
    /// left-click a card to select / point at it, right-click for the action menu,
    /// then (for placement actions) left-click the highlighted destination zone.
    /// A turn/phase tracker guides play but nothing is rule-enforced.
    /// </summary>
    public partial class DuelRoomPage : Page
    {
        private readonly DuelState _state = new();

        // A placement armed from the action menu: the face/position to apply once a
        // highlighted zone is clicked. Null when nothing is pending.
        private (bool faceDown, bool defense)? _pending;
        // The log verb for the armed placement ("Normal Summoned", "Set", …).
        private string _pendingVerb = "played";

        // Last-seen LP per board, so a change can be logged as a delta. Logging is off
        // until a game starts (so the initial 8000 setup isn't logged).
        private int _playerLpShadow = 8000;
        private int _oppLpShadow = 8000;
        private bool _loggingLp;

        // A "point"/"declare" cue clears itself after a moment.
        private readonly DispatcherTimer _cueTimer;
        private BoardCard? _pointedCard;

        // An armed "declare attack": the attacking monster, waiting for a target click.
        // Null when no attack is being declared. The drawn arrow clears after a moment.
        private BoardCard? _attackFrom;
        private readonly DispatcherTimer _attackTimer;

        // An armed "swap control": the monster, waiting for a destination zone click on
        // the other player's side. Null when no swap is pending.
        private BoardCard? _swapCard;

        // Networked play. Null / false while offline (hot-seat practice).
        private DuelSession? _session;
        private bool _networked;
        private readonly CardImageService _images = new();
        private readonly Random _rng = new();

        // Cards currently revealed to the opponent (single-card reveals).
        private readonly HashSet<BoardCard> _revealed = new();

        // End-of-duel state. A duel ends when someone concedes / admits defeat; the
        // end screen then offers a rematch or a quit to the menu.
        private GameStartInfo? _gameInfo;            // last networked setup, reused for a rematch
        private Deck? _playerDeckSource;             // last decks loaded offline, reused for a rematch
        private Deck? _opponentDeckSource;
        private bool _duelOver;
        private bool _loadingDecks;                  // true while pre-caching card images before a duel
        private bool _playerLpZeroPrompted;          // guards the "LP hit 0" prompt against re-firing per crossing
        private bool _oppLpZeroPrompted;
        private bool _iLost;                         // did *I* lose the last duel (loser goes first on rematch)
        private bool _localWantsRematch;
        private bool _remoteWantsRematch;

        public DuelRoomPage()
        {
            InitializeComponent();
            DataContext = _state;
            var deckNames = YdkFile.ListDeckNames();
            DeckPicker.ItemsSource = deckNames;
            OnlineDeckPicker.ItemsSource = deckNames;

            _cueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _cueTimer.Tick += (_, _) => ClearCue();

            _attackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
            _attackTimer.Tick += (_, _) => ClearArrow();

            // In a networked duel my own LP changes (buttons or the text box) mirror
            // to the opponent's view of me. Either player's LP change is also logged.
            _state.Player.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(PlayerBoard.LifePoints)) return;
                if (_networked) _session?.Send(new LifePointsMessage { LifePoints = _state.Player.LifePoints });
                LogLifePoints(_state.Player, ref _playerLpShadow);
                CheckLifePointsZero(_state.Player);
            };
            _state.Opponent.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(PlayerBoard.LifePoints)) return;
                LogLifePoints(_state.Opponent, ref _oppLpShadow);
                CheckLifePointsZero(_state.Opponent);
            };

            // Keep the log scrolled to the newest entry.
            _state.LogEntries.CollectionChanged += (_, _) =>
                Dispatcher.BeginInvoke(new Action(() => LogScroll.ScrollToBottom()), DispatcherPriority.Background);
        }

        // --- Game log helpers ---

        /// <summary>Logs an action attributed to a player, e.g. "Alice drew a card",
        /// coloured by whose action it is (my board blue, the opponent's red).</summary>
        private void LogAction(PlayerBoard actor, string verb) =>
            _state.Log($"{actor.DisplayName} {verb}",
                actor.Side == PlayerSide.Player ? DuelLogSide.Player : DuelLogSide.Opponent);

        /// <summary>The card's name for the log, or a neutral word for tokens.</summary>
        private static string NameOf(BoardCard card) => card.IsToken ? "a token" : card.Name;

        private void LogLifePoints(PlayerBoard board, ref int shadow)
        {
            var now = board.LifePoints;
            if (_loggingLp && now != shadow)
            {
                var delta = now - shadow;
                LogAction(board, delta < 0 ? $"lost {-delta} LP (now {now})" : $"gained {delta} LP (now {now})");
            }
            shadow = now;
        }

        /// <summary>Resets the log and enables LP-change logging for a fresh game.</summary>
        private void StartLog()
        {
            _state.LogEntries.Clear();
            _playerLpShadow = _state.Player.LifePoints;
            _oppLpShadow = _state.Opponent.LifePoints;
            _loggingLp = true;
        }

        private static string PileLabel(ZoneKind pile, bool toTop) => pile switch
        {
            ZoneKind.Hand => "their hand",
            ZoneKind.Graveyard => "the Graveyard",
            ZoneKind.Banished => "banishment",
            ZoneKind.ExtraDeck => "the Extra Deck",
            ZoneKind.Deck => toTop ? "the top of the Deck" : "the bottom of the Deck",
            _ => "a pile",
        };

        private static string EmoteVerb(string key) => key switch
        {
            "thinking" => "is thinking…",
            "ok" => "is OK with that",
            "respond" => "wants to respond",
            _ => "reacts",
        };

        // --- Status emotes (shown on my portrait; broadcast so the opponent sees) ---

        private void Emote_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string key) return;
            var me = _state.Player;
            // Pressing the active emote again clears it.
            me.Emote = me.Emote == key ? null : key;
            if (me.Emote is { } active) LogAction(me, EmoteVerb(active));
            if (_networked) _session?.Send(new EmoteMessage { Emote = me.Emote ?? "" });
        }

        // --- Chat ---

        private void ChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { SendChat(); e.Handled = true; }
        }

        private void SendChat_Click(object sender, RoutedEventArgs e) => SendChat();

        private void SendChat()
        {
            var text = ChatInput.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            ChatInput.Text = "";
            _state.LogChat(_state.Player.DisplayName, text, DuelLogSide.Player);
            if (_networked) _session?.Send(new ChatMessage { Text = text });
        }

        // The navigation host places pages in a ScrollViewer, which hands the page an
        // unbounded height — so the field's Viewbox would render at full natural size
        // and overflow the top. Cap the page to the visible viewport so the Viewbox
        // scales the whole board to fit.
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            for (DependencyObject? d = this; d is not null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is ScrollViewer sv)
                {
                    RootGrid.SetBinding(MaxHeightProperty,
                        new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = sv });
                    break;
                }
            }
        }

        // --- Selection & menus ---

        // Left-click selects the card and opens the "view" menu (inspect / declare /
        // point) — the table-talk actions of a two-player game.
        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not BoardCard card) return;

            // While a "declare attack" is armed, this click picks the target instead of
            // opening the card menu.
            if (_attackFrom is { } attacker)
            {
                CompleteAttack(attacker, card);
                _attackFrom = null;
                e.Handled = true;
                return;
            }

            _state.Selected = card;
            RevealButton.Content = card.IsRevealed ? "Unreveal" : "Reveal to opponent";
            ViewMenu.IsOpen = true;
            e.Handled = true;
        }

        // Right-click selects the card and opens the play-action menu at the cursor.
        private void Card_RightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not BoardCard card) return;
            CancelAttackDeclaration();
            EndSwap();
            _state.Selected = card;
            // Online, you can only play your own cards.
            if (_networked && _state.FindBoard(card) != _state.Player) { e.Handled = true; return; }
            CardActions.IsOpen = true;
            e.Handled = true;
        }

        private bool IsMine(BoardCard card) => _state.FindBoard(card) == _state.Player;

        // --- View menu: inspect / declare / point ---

        private void Inspect_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { IsToken: false } c) Preview.Show(c.Card);
            ViewMenu.IsOpen = false;
        }

        private void DeclareEffect_Click(object sender, RoutedEventArgs e) => TableTalk("declares the effect of");
        private void Point_Click(object sender, RoutedEventArgs e) => TableTalk("points to");

        private void TableTalk(string verb)
        {
            ViewMenu.IsOpen = false;
            if (_state.Selected is not { } c) return;

            // The actor is whoever is doing the pointing — me online, the active player
            // in offline hot-seat — never the card's owner.
            var actor = _networked ? _state.Player : _state.ActiveBoard;
            var target = DescribeTarget(c);

            _state.Announce(actor.DisplayName, verb, target);
            PointAt(c);
            LogAction(actor, $"{verb} {target}");

            if (_networked)
            {
                // Tell the opponent, locating the card so they can ring it — their own
                // card when I'm pointing at them, the sender's card when it's mine.
                var msg = new AnnounceMessage { Verb = verb, Target = target };
                if (LocateOnField(c) is { } mine)
                    (msg.Side, msg.Zone, msg.Index) = (AnnounceSide.SenderField, mine.kind, mine.index);
                else if (LocateOnOppField(c) is { } theirs)
                    (msg.Side, msg.Zone, msg.Index) = (AnnounceSide.ReceiverField, theirs.kind, theirs.index);
                _session?.Send(msg);
            }
        }

        /// <summary>A public description of a table-talk target that respects hidden info:
        /// the card's name when it's face-up, else a generic phrase.</summary>
        private string DescribeTarget(BoardCard c)
        {
            if (c.IsToken) return "a token";
            if (_state.Player.Hand.Contains(c) || _state.Opponent.Hand.Contains(c)) return "a card in hand";
            if (c.FaceDown) return "a set card on the field";
            return c.Name;
        }

        // --- Reveal cards to the opponent ---

        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            ViewMenu.IsOpen = false;
            if (_state.Selected is not { } c) return;
            if (!_networked) { ResultText.Text = "Revealing is for online duels."; return; }
            if (!IsMine(c)) return;

            if (_revealed.Remove(c)) c.IsRevealed = false;
            else { _revealed.Add(c); c.IsRevealed = true; }

            LogAction(_state.Player, c.IsRevealed ? $"revealed {NameOf(c)}" : "hid a revealed card");

            _session?.Send(new RevealCardsMessage
            {
                CardIds = _revealed.Where(x => !x.IsToken).Select(x => x.Card.Id).ToList(),
                Label = _revealed.Count == 1 ? "reveals a card" : "reveals cards",
            });
        }

        private void RevealHand_Click(object sender, RoutedEventArgs e)
        {
            if (!_networked) { ResultText.Text = "Revealing is for online duels."; return; }
            var ids = _state.Player.Hand.Where(c => !c.IsToken).Select(c => c.Card.Id).ToList();
            _session?.Send(new RevealCardsMessage { CardIds = ids, Label = "reveals their hand" });
            LogAction(_state.Player, "revealed their hand");
            ResultText.Text = "Revealed your hand to the opponent.";
        }

        /// <summary>Drops a card from the "revealed" set (e.g. when it leaves the hand).</summary>
        private void Unreveal(BoardCard card)
        {
            if (!_revealed.Remove(card)) return;
            card.IsRevealed = false;
            if (_networked)
                _session?.Send(new RevealCardsMessage
                {
                    CardIds = _revealed.Where(x => !x.IsToken).Select(x => x.Card.Id).ToList(),
                    Label = _revealed.Count == 1 ? "reveals a card" : "reveals cards",
                });
        }

        private void PointAt(BoardCard? card)
        {
            if (_pointedCard is { } prev) prev.IsPointed = false;
            _pointedCard = card;
            if (card is { }) card.IsPointed = true;
            // Start the auto-clear even with no card so the banner still fades on its own.
            _cueTimer.Stop();
            _cueTimer.Start();
        }

        private void ClearCue()
        {
            _cueTimer.Stop();
            if (_pointedCard is { } c) c.IsPointed = false;
            _pointedCard = null;
            _state.Announcement = null;
        }

        // --- Attack declarations (visual arrow + log; no damage is applied) ---

        // "Declare Attack" arms the selected monster; the next card click is its target.
        private void DeclareAttack_Click(object sender, RoutedEventArgs e)
        {
            ViewMenu.IsOpen = false;
            if (_state.Selected is not { } c) return;
            if (MonsterZoneOf(c) is null) { ResultText.Text = "Only a monster on the field can attack."; return; }
            if (_networked && !IsMine(c)) { ResultText.Text = "You can only attack with your own monsters."; return; }
            _attackFrom = c;
            ResultText.Text = $"Pick the monster for {NameOf(c)} to attack (Esc to cancel).";
        }

        // "Declare Direct Attack" needs no target — for when the opponent has no monsters.
        private void DeclareDirectAttack_Click(object sender, RoutedEventArgs e)
        {
            ViewMenu.IsOpen = false;
            _attackFrom = null;
            if (_state.Selected is not { } c) return;
            if (MonsterZoneOf(c) is not { } loc) { ResultText.Text = "Only a monster on the field can attack."; return; }
            if (_networked && !IsMine(c)) { ResultText.Text = "You can only attack with your own monsters."; return; }

            AnimateAttack(c, null, direct: true);
            LogAction(loc.board, $"declares a direct attack with {NameOf(c)}");
            if (_networked && loc.board == _state.Player)
                _session?.Send(new AttackMessage { AttackerZone = loc.kind, AttackerIndex = loc.index, Direct = true });
        }

        private void CompleteAttack(BoardCard attacker, BoardCard target)
        {
            ViewMenu.IsOpen = false;
            if (MonsterZoneOf(attacker) is not { } aloc) return;
            var defender = aloc.board == _state.Player ? _state.Opponent : _state.Player;

            if (MonsterZoneOf(target) is not { } tloc || tloc.board != defender)
            {
                ResultText.Text = "Pick one of the opponent's monsters as the target.";
                return;
            }

            AnimateAttack(attacker, target, direct: false);
            LogAction(aloc.board, $"declares an attack with {NameOf(attacker)} targeting {NameOf(target)}");
            if (_networked && aloc.board == _state.Player)
                _session?.Send(new AttackMessage
                {
                    AttackerZone = aloc.kind, AttackerIndex = aloc.index,
                    TargetZone = tloc.kind, TargetIndex = tloc.index, Direct = false,
                });
        }

        /// <summary>The board + zone of a card sitting in a monster zone (main or extra),
        /// or null if it isn't on one.</summary>
        private (PlayerBoard board, ZoneKind kind, int index)? MonsterZoneOf(BoardCard card)
        {
            foreach (var board in new[] { _state.Player, _state.Opponent })
                foreach (var slot in board.MainMonsterZones.Concat(board.ExtraMonsterZones))
                    if (ReferenceEquals(slot.Card, card)) return (board, slot.Kind, slot.Index);
            return null;
        }

        // --- Control swap ("brain control": the monster moves to the other side, whose
        //     player then owns it) ---

        // Arms the swap: highlight the other player's empty Monster Zones so the player
        // chooses exactly where the monster lands (in case a zone is already occupied).
        private void SwapControl_Click(object sender, RoutedEventArgs e)
        {
            ViewMenu.IsOpen = false;
            if (_state.Selected is not { } c) return;
            if (MonsterZoneOf(c) is not { } src)
            {
                ResultText.Text = "Only a monster on the field can change control.";
                return;
            }
            var toBoard = src.board == _state.Player ? _state.Opponent : _state.Player;
            if (!toBoard.MainMonsterZones.Any(s => s.IsEmpty))
            {
                ResultText.Text = $"{toBoard.DisplayName} has no free Monster Zone.";
                return;
            }

            _swapCard = c;
            _pending = null; // not a normal placement
            _state.HighlightTargets(toBoard, ZoneKind.MainMonster);
            ResultText.Text = $"Pick a Monster Zone on {toBoard.DisplayName}'s side for {NameOf(c)} (Esc to cancel).";
        }

        // Completes an armed swap into the clicked, highlighted zone.
        private void CompleteSwap(BoardCard c, ZoneSlot dest)
        {
            if (MonsterZoneOf(c) is not { } src) { EndSwap(); return; }
            var fromBoard = src.board;
            var toBoard = dest.Owner;

            var faceDown = c.FaceDown;
            var defense = c.Defense;
            _state.MoveToSlot(c, dest); // moving to the other board makes it the new owner
            _state.Log($"Control of {NameOf(c)} passes from {fromBoard.DisplayName} to {toBoard.DisplayName}");
            if (_networked)
                _session?.Send(new ControlSwapMessage
                {
                    FromSendersField = fromBoard == _state.Player,
                    SourceZone = src.kind, SourceIndex = src.index,
                    DestZone = dest.Kind, DestIndex = dest.Index,
                    FaceDown = faceDown, Defense = defense,
                });
            EndSwap();
            Deselect();
        }

        private void EndSwap()
        {
            _swapCard = null;
            _state.ClearHighlights();
        }

        private void AnimateAttack(BoardCard attacker, BoardCard? target, bool direct)
        {
            if (FindCardElement(attacker) is not { } fromEl) return;
            var start = CenterIn(fromEl, ArrowOverlay);

            Point end;
            if (!direct && target is not null && FindCardElement(target) is { } toEl)
                end = CenterIn(toEl, ArrowOverlay);
            else
            {
                // Direct attack: aim at the defending player's side of the board — the top
                // edge when the local player attacks, the bottom edge when the opponent does.
                var attackerIsMine = _state.FindBoard(attacker) == _state.Player;
                var y = attackerIsMine ? 6 : Math.Max(6, ArrowOverlay.ActualHeight - 6);
                end = new Point(ArrowOverlay.ActualWidth / 2, y);
                direct = true;
            }
            DrawArrow(start, end, direct);
        }

        private static Point CenterIn(FrameworkElement el, Visual relativeTo) =>
            el.TransformToVisual(relativeTo).Transform(new Point(el.ActualWidth / 2, el.ActualHeight / 2));

        private void DrawArrow(Point start, Point end, bool direct)
        {
            ArrowOverlay.Children.Clear();

            var color = direct ? Color.FromRgb(0xFF, 0xB0, 0x3A) : Color.FromRgb(0xFF, 0x4D, 0x4D);
            var brush = new SolidColorBrush(color);
            var thickness = direct ? 6.0 : 4.0;

            ArrowOverlay.Children.Add(new Line
            {
                X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
                Stroke = brush, StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });

            var dir = end - start;
            if (dir.Length >= 1)
            {
                dir.Normalize();
                var normal = new Vector(-dir.Y, dir.X);
                var headLen = direct ? 24.0 : 18.0;
                var headHalf = direct ? 14.0 : 10.0;
                var basePt = end - dir * headLen;
                ArrowOverlay.Children.Add(new Polygon
                {
                    Fill = brush,
                    Points = new PointCollection { end, basePt + normal * headHalf, basePt - normal * headHalf },
                });
            }

            // Pop in, then the timer fades it out.
            ArrowOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            ArrowOverlay.Opacity = 1;
            ArrowOverlay.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            _attackTimer.Stop();
            _attackTimer.Start();
        }

        private void ClearArrow()
        {
            _attackTimer.Stop();
            var fade = new DoubleAnimation(ArrowOverlay.Opacity, 0, TimeSpan.FromMilliseconds(250));
            // Don't wipe a newer arrow that started during the fade (it restarts the timer).
            fade.Completed += (_, _) => { if (!_attackTimer.IsEnabled) ArrowOverlay.Children.Clear(); };
            ArrowOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>Finds the on-screen element rendering a board card, so an arrow endpoint
        /// can anchor to it. Depth-first over the playmat visual tree.</summary>
        private FrameworkElement? FindCardElement(BoardCard card) => FindByDataContext(PlaymatArea, card);

        private static FrameworkElement? FindByDataContext(DependencyObject root, object data)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && ReferenceEquals(fe.DataContext, data) && fe.ActualWidth > 0)
                    return fe;
                if (FindByDataContext(child, data) is { } found) return found;
            }
            return null;
        }

        private void Zone_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ZoneSlot slot) return;

            // A pending control swap drops the monster into the chosen highlighted zone.
            if (_swapCard is { } swapCard && slot.IsTarget)
            {
                CompleteSwap(swapCard, slot);
                e.Handled = true;
                return;
            }

            // Only meaningful while placing a card into a highlighted, empty zone.
            if (_pending is { } p && slot.IsTarget && _state.Selected is { } card)
            {
                var emit = _networked && IsMine(card);
                var from = emit ? SourceKindOf(card) : ZoneKind.Hand; // capture before the move
                var fromIndex = emit ? LocateOnField(card)?.index ?? 0 : 0; // source slot for field relocation
                _state.MoveToSlot(card, slot);
                card.FaceDown = p.faceDown;
                card.Defense = p.defense;
                LogAction(_state.FindBoard(card), $"{_pendingVerb} {NameOf(card)}");
                if (emit) EmitPlacement(card, slot, p.faceDown, p.defense, from, fromIndex);
                EndPlacement();
                Deselect();
                e.Handled = true;
            }
        }

        private void EmitPlacement(BoardCard card, ZoneSlot slot, bool faceDown, bool defense, ZoneKind from, int fromIndex)
        {
            if (card.IsToken)
                _session?.Send(new TokenSummonMessage { Zone = slot.Kind, Index = slot.Index, Defense = defense });
            else if (faceDown)
                _session?.Send(new SetCardMessage { Zone = slot.Kind, Index = slot.Index, Defense = defense, From = from, FromIndex = fromIndex });
            else
                _session?.Send(new SummonMessage { CardId = card.Card.Id, Zone = slot.Kind, Index = slot.Index, Defense = defense, From = from });
        }

        /// <summary>Which of my non-field piles the card sits in, or null if it's on the
        /// field / in hand / not mine. Used to sync pile-to-pile moves (search, recovery).</summary>
        private ZoneKind? PileKindOf(BoardCard card)
        {
            var p = _state.Player;
            if (p.Deck.Contains(card)) return ZoneKind.Deck;
            if (p.ExtraDeck.Contains(card)) return ZoneKind.ExtraDeck;
            if (p.Graveyard.Contains(card)) return ZoneKind.Graveyard;
            if (p.Banished.Contains(card)) return ZoneKind.Banished;
            return null;
        }

        /// <summary>Where a card of mine currently lives, for the network source hint.</summary>
        private ZoneKind SourceKindOf(BoardCard card)
        {
            if (LocateOnField(card) is { } loc) return loc.kind; // relocation between zones
            var p = _state.Player;
            if (p.Hand.Contains(card)) return ZoneKind.Hand;
            if (p.Deck.Contains(card)) return ZoneKind.Deck;
            if (p.ExtraDeck.Contains(card)) return ZoneKind.ExtraDeck;
            if (p.Graveyard.Contains(card)) return ZoneKind.Graveyard;
            if (p.Banished.Contains(card)) return ZoneKind.Banished;
            return ZoneKind.Hand;
        }

        private void Playmat_Click(object sender, MouseButtonEventArgs e)
        {
            // A click that reached the playmat background cancels the selection.
            CancelAttackDeclaration();
            EndSwap();
            EndPlacement();
            Deselect();
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CancelAttackDeclaration(); EndSwap(); EndPlacement(); Deselect(); }
        }

        private void CancelAttackDeclaration()
        {
            if (_attackFrom is null) return;
            _attackFrom = null;
            ResultText.Text = "Attack declaration cancelled.";
        }

        private void BeginPlacement(string verb, bool faceDown, bool defense, params ZoneKind[] kinds)
        {
            CardActions.IsOpen = false;
            // Close the pile viewer so the board is visible and clickable for the drop
            // (e.g. Special Summon chosen from the Deck/GY list).
            PileViewer.IsOpen = false;
            if (_state.Selected is not { } card) return;
            _pending = (faceDown, defense);
            _pendingVerb = verb;
            // Summon into the zones of whichever board owns the selected card.
            _state.HighlightTargets(_state.FindBoard(card), kinds);
        }

        private void EndPlacement()
        {
            _pending = null;
            _state.ClearHighlights();
        }

        private void Deselect()
        {
            _state.Selected = null;
            CardActions.IsOpen = false;
        }

        // --- Action menu: placement (arm a zone) ---

        private void Summon_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement("Normal Summoned", faceDown: false, defense: false, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void SetMonster_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement("Set", faceDown: true, defense: true, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void SpecialAtk_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement("Special Summoned", faceDown: false, defense: false, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void SpecialDef_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement("Special Summoned", faceDown: false, defense: true, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void ActivateST_Click(object sender, RoutedEventArgs e)
        {
            // A card already Set face-down on the field: activating it flips it face-up
            // in place (revealing it), rather than placing a new one from the hand.
            if (_state.Selected is { FaceDown: true } c && IsOnField(c))
            {
                c.FaceDown = false;
                LogAction(_state.FindBoard(c), $"activated {NameOf(c)}");
                if (_networked && IsMine(c) && LocateOnField(c) is { } loc)
                    _session?.Send(new RevealMessage { CardId = c.Card.Id, Zone = loc.kind, Index = loc.index, Defense = c.Defense });
                CardActions.IsOpen = false;
                return;
            }
            BeginPlacement("activated", faceDown: false, defense: false, ZoneKind.SpellTrap);
        }

        private void SetST_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement("Set", faceDown: true, defense: false, ZoneKind.SpellTrap);

        private void FieldSpell_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement("activated a Field Spell,", faceDown: false, defense: false, ZoneKind.Field);

        // --- Action menu: position (act now) ---

        private void SelAttack_Click(object sender, RoutedEventArgs e) => SetPosition(faceDown: false, defense: false);
        private void SelDefense_Click(object sender, RoutedEventArgs e) => SetPosition(faceDown: false, defense: true);

        // Flip Summon: a face-down monster turns face-up in attack position, revealing it.
        private void SelFlipSummon_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { } c)
            {
                c.FaceDown = false;
                c.Defense = false; // a Flip Summon is always into attack position
                LogAction(_state.FindBoard(c), $"Flip Summoned {NameOf(c)}");

                if (_networked && IsMine(c) && LocateOnField(c) is { } loc)
                    _session?.Send(new RevealMessage { CardId = c.Card.Id, Zone = loc.kind, Index = loc.index, Defense = false });
            }
            CardActions.IsOpen = false;
        }

        // Plain flip: just turns the card over, keeping its current battle position.
        private void SelFlip_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { } c)
            {
                c.FaceDown = !c.FaceDown;
                LogAction(_state.FindBoard(c), c.FaceDown ? $"flipped {NameOf(c)} face-down" : $"flipped {NameOf(c)} face-up");

                if (_networked && IsMine(c) && LocateOnField(c) is { } loc)
                {
                    // Turning face-up reveals the card's identity to the opponent; the
                    // battle position is preserved either way.
                    if (!c.FaceDown)
                        _session?.Send(new RevealMessage { CardId = c.Card.Id, Zone = loc.kind, Index = loc.index, Defense = c.Defense });
                    else
                        _session?.Send(new PositionChangeMessage { Zone = loc.kind, Index = loc.index, FaceDown = true, Defense = c.Defense });
                }
            }
            CardActions.IsOpen = false;
        }

        private void SetPosition(bool faceDown, bool defense)
        {
            if (_state.Selected is { } c)
            {
                c.FaceDown = faceDown;
                c.Defense = defense;
                LogAction(_state.FindBoard(c), $"switched {NameOf(c)} to {(defense ? "defense" : "attack")} position");
                if (_networked && IsMine(c) && LocateOnField(c) is { } loc)
                    _session?.Send(new PositionChangeMessage { Zone = loc.kind, Index = loc.index, FaceDown = faceDown, Defense = defense });
            }
            CardActions.IsOpen = false;
        }

        /// <summary>The (kind, index) of a card on the local player's field, or null.</summary>
        private (ZoneKind kind, int index)? LocateOnField(BoardCard card)
        {
            foreach (var slot in _state.Player.AllSlots())
                if (ReferenceEquals(slot.Card, card)) return (slot.Kind, slot.Index);
            return null;
        }

        /// <summary>The (kind, index) of a card on the opponent's field, or null.</summary>
        private (ZoneKind kind, int index)? LocateOnOppField(BoardCard card)
        {
            foreach (var slot in _state.Opponent.AllSlots())
                if (ReferenceEquals(slot.Card, card)) return (slot.Kind, slot.Index);
            return null;
        }

        /// <summary>True if the card currently occupies a field zone on either board.</summary>
        private bool IsOnField(BoardCard card) =>
            _state.Player.AllSlots().Concat(_state.Opponent.AllSlots())
                  .Any(s => ReferenceEquals(s.Card, card));

        // --- Action menu: counters (stay open so several can be added) ---

        private void AddCounter_Click(object sender, RoutedEventArgs e) => ChangeCounter(+1);
        private void RemoveCounter_Click(object sender, RoutedEventArgs e) => ChangeCounter(-1);

        private void ChangeCounter(int delta)
        {
            if (_state.Selected is not { } c) return;
            c.Counters += delta;
            LogAction(_state.FindBoard(c), delta > 0 ? $"added a counter to {NameOf(c)}" : $"removed a counter from {NameOf(c)}");
            if (_networked && IsMine(c) && LocateOnField(c) is { } loc)
                _session?.Send(new CounterMessage { Zone = loc.kind, Index = loc.index, Counters = c.Counters });
        }

        // --- Action menu: move to a pile (act now) ---

        private void SelToHand_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Hand, ZoneKind.Hand);
        private void SelToGrave_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Graveyard, ZoneKind.Graveyard);
        private void SelToBanish_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Banished, ZoneKind.Banished);
        private void SelToBanishFaceDown_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Banished, ZoneKind.Banished, faceDown: true);
        private void SelToDeckTop_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Deck, ZoneKind.Deck, toTop: true);
        private void SelToDeckBottom_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Deck, ZoneKind.Deck);
        private void SelToExtra_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.ExtraDeck, ZoneKind.ExtraDeck);

        private void MoveSelected(Func<PlayerBoard, ObservableCollection<BoardCard>> pick, ZoneKind pile, bool toTop = false, bool faceDown = false)
        {
            if (_state.Selected is not { } card) return;
            Unreveal(card);
            var board = _state.FindBoard(card);
            var mine = board == _state.Player;

            // Capture where it came from before the move, for the network message.
            var fromField = mine ? LocateOnField(card) : null;
            var fromHand = mine && _state.Player.Hand.Contains(card);
            var fromPile = mine && fromField is null && !fromHand ? PileKindOf(card) : null;
            // A public destination (GY/Banished) reveals the card's identity — unless it's
            // being banished face-down, which stays hidden from the opponent.
            long? publicId = !faceDown && !card.IsToken && (pile is ZoneKind.Graveyard or ZoneKind.Banished) ? card.Card.Id : null;
            var isToken = card.IsToken;

            ResetPosition(card);
            if (faceDown) card.FaceDown = true; // a face-down banished card sits as a back
            var movedName = NameOf(card);
            _state.MoveToPile(card, pick(board), toTop);
            var verb = faceDown ? $"banished {movedName} face-down" : $"moved {movedName} to {PileLabel(pile, toTop)}";
            LogAction(board, verb);

            if (_networked && mine)
            {
                if (fromField is { } f)
                    _session?.Send(new FieldToPileMessage { Zone = f.kind, Index = f.index, Pile = pile, CardId = publicId, ToTop = toTop, IsToken = isToken });
                else if (fromHand)
                    _session?.Send(new HandToPileMessage { Pile = pile, CardId = publicId, ToTop = toTop });
                else if (fromPile is { } sp)
                {
                    // Pile → pile (search / recovery). Reveal the id only when a public pile
                    // (GY/Banished) is involved — a search into the hand, or a face-down
                    // banish, stays hidden.
                    var sourcePublic = sp is ZoneKind.Graveyard or ZoneKind.Banished;
                    long? moveId = !faceDown && !card.IsToken && (sourcePublic || publicId is not null) ? card.Card.Id : null;
                    _session?.Send(new PileMoveMessage { From = sp, To = pile, CardId = moveId, ToTop = toTop });
                }
            }

            EndPlacement();
            Deselect();
        }

        private static void ResetPosition(BoardCard card)
        {
            card.FaceDown = false;
            card.Defense = false;
        }

        // --- Pile viewer ---

        private void Pile_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag) return;

            var parts = tag.Split(':');
            var board = parts[0] == "o" ? _state.Opponent : _state.Player;
            var who = parts[0] == "o" ? "Opponent" : "Player";

            (string? title, System.Collections.IEnumerable? source) = parts[1] switch
            {
                "deck" => ("Deck", board.Deck),
                "extra" => ("Extra Deck", board.ExtraDeck),
                "gy" => ("Graveyard", board.Graveyard),
                "ban" => ("Banished", board.Banished),
                _ => (null, null),
            };
            if (title is null) return;

            PileViewerTitle.Text = $"{who} · {title}";
            PileViewerItems.ItemsSource = source;
            PileViewer.IsOpen = true;
            e.Handled = true;
        }

        // Which deck the DeckActions menu was opened over (true = opponent's deck).
        private bool _deckMenuIsOpponent;

        // Click a deck pile (either button) → the deck menu (View / Shuffle + surrender),
        // opened at the cursor and styled like the card menus. Left-click routes here too
        // rather than opening the viewer directly, so looking through the Deck is always a
        // deliberate, announced action. Online you can only act on your own deck, so the
        // opponent's deck offers nothing and the menu doesn't open there.
        private void Deck_LeftClick(object sender, MouseButtonEventArgs e) => OpenDeckMenu(sender, e);
        private void Deck_RightClick(object sender, MouseButtonEventArgs e) => OpenDeckMenu(sender, e);

        private void OpenDeckMenu(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag) return;
            e.Handled = true;

            _deckMenuIsOpponent = tag.StartsWith("o:");
            var isOwn = !_deckMenuIsOpponent;
            // Offline you may act on either deck; online only your own.
            var canShuffle = isOwn || !_networked;

            DeckPlayGroup.Visibility = canShuffle ? Visibility.Visible : Visibility.Collapsed;
            DeckSurrenderGroup.Visibility = isOwn ? Visibility.Visible : Visibility.Collapsed;

            if (!canShuffle && !isOwn) return; // the opponent's deck online: nothing to offer
            DeckActions.IsOpen = true;
        }

        // Look through the Deck. Doing this to your own Deck is a public action in
        // Yu-Gi-Oh! (only allowed for specific card effects), so it's announced to the
        // opponent — both players must be aware the Deck was searched.
        private void ViewDeck_Click(object sender, RoutedEventArgs e)
        {
            DeckActions.IsOpen = false;
            var board = _deckMenuIsOpponent ? _state.Opponent : _state.Player;
            var who = _deckMenuIsOpponent ? "Opponent" : "Player";
            PileViewerTitle.Text = $"{who} · Deck";
            PileViewerItems.ItemsSource = board.Deck;
            PileViewer.IsOpen = true;

            if (_deckMenuIsOpponent) return; // viewing your own deck is the announced case
            _state.Announce(_state.Player.DisplayName, "is looking through", "their Deck");
            PointAt(null); // arm the auto-clear so the banner fades on its own
            LogAction(_state.Player, "is looking through their Deck");
            if (_networked)
                _session?.Send(new AnnounceMessage { Verb = "is looking through", Target = "their Deck", Side = AnnounceSide.None });
        }

        private void DeckShuffle_Click(object sender, RoutedEventArgs e)
        {
            DeckActions.IsOpen = false;
            if (_networked && _deckMenuIsOpponent) return;

            var board = _deckMenuIsOpponent ? _state.Opponent : _state.Player;
            board.Shuffle();
            LogAction(board, "shuffled their deck");
            ResultText.Text = "Shuffled deck.";
            if (_networked && board == _state.Player) _session?.Send(new ShuffleMessage());
        }

        // Mill a card off the top / bottom of the deck to the Graveyard (Lightsworn &co).
        private void DeckMillTop_Click(object sender, RoutedEventArgs e) => DeckMill(fromBottom: false);
        private void DeckMillBottom_Click(object sender, RoutedEventArgs e) => DeckMill(fromBottom: true);

        private void DeckMill(bool fromBottom)
        {
            DeckActions.IsOpen = false;
            if (_networked && _deckMenuIsOpponent) return;

            var board = _deckMenuIsOpponent ? _state.Opponent : _state.Player;
            if (board.Deck.Count == 0) { ResultText.Text = "Deck is empty."; return; }

            var card = fromBottom ? board.Deck[^1] : board.Deck[0];
            ResetPosition(card); // milled cards sit face-up in the Graveyard
            _state.MoveToPile(card, board.Graveyard);
            var where = fromBottom ? "bottom" : "top";
            LogAction(board, $"sent {NameOf(card)} from the {where} of their Deck to {PileLabel(ZoneKind.Graveyard, false)}");
            ResultText.Text = $"Milled {NameOf(card)} to the Graveyard.";

            if (_networked && board == _state.Player)
                _session?.Send(new DeckToPileMessage { CardId = card.Card.Id, Pile = ZoneKind.Graveyard, FromBottom = fromBottom });
        }

        private void Surrender_Click(object sender, RoutedEventArgs e) => Surrender("surrendered");

        // Surrender the duel — a table-talk gesture (no rules engine): log it, tell the
        // opponent online so they see they've won, then raise the end screen.
        private void Surrender(string verb)
        {
            DeckActions.IsOpen = false;
            if (_duelOver) return;
            var me = _state.Player;
            LogAction(me, verb);
            _iLost = true;
            if (_networked)
            {
                _session?.Send(new ConcedeMessage { Verb = verb });
                EndDuel("You lost", $"You {verb}. {_state.Opponent.DisplayName} wins.");
            }
            else
            {
                // Hot-seat: the Player board surrenders, so the Opponent wins.
                EndDuel($"{_state.Opponent.DisplayName} wins", $"{me.DisplayName} {verb}.");
            }
        }

        // When a board's life points cross to 0 (or below), offer to end the duel. It's a
        // prompt, not an auto-end: this is a manual sim, so the LP may be about to be
        // corrected, or an effect that prevents the loss applies. We ask once per crossing
        // — the guard resets when LP climbs back above 0.
        private async void CheckLifePointsZero(PlayerBoard board)
        {
            bool isPlayer = board.Side == PlayerSide.Player;

            // Recovered above 0: re-arm the prompt for a future crossing.
            if (board.LifePoints > 0)
            {
                if (isPlayer) _playerLpZeroPrompted = false; else _oppLpZeroPrompted = false;
                return;
            }

            if (_duelOver) return;
            if (isPlayer ? _playerLpZeroPrompted : _oppLpZeroPrompted) return;
            // Online I can only end my *own* loss; the opponent concedes on their client
            // when their own LP hits 0.
            if (_networked && !isPlayer) return;

            if (isPlayer) _playerLpZeroPrompted = true; else _oppLpZeroPrompted = true;

            var winner = isPlayer ? _state.Opponent : _state.Player;
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Life points depleted",
                Content = $"{board.DisplayName}'s life points reached 0. End the duel?",
                PrimaryButtonText = "End duel",
                CloseButtonText = "Not yet",
            };
            var result = await box.ShowDialogAsync();
            if (result != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

            // The LP may have been corrected (or the duel already ended) while the prompt was open.
            if (_duelOver || board.LifePoints > 0) return;

            _iLost = isPlayer;
            LogAction(board, "was defeated (0 LP)");
            if (_networked)
            {
                // Only reachable when I'm the loser; the opponent sees their win via this.
                _session?.Send(new ConcedeMessage { Verb = "was defeated" });
                EndDuel("You lost", $"Your life points reached 0. {winner.DisplayName} wins.");
            }
            else
            {
                EndDuel($"{winner.DisplayName} wins", $"{board.DisplayName}'s life points reached 0.");
            }
        }

        // --- Turn / phase ---

        private void Phase_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string tag }
                || !Enum.TryParse<DuelPhase>(tag, out var phase)) return;
            if (_state.Phase == phase) return;
            _state.Phase = phase;
            _state.Log($"— {_state.TurnSummary} —");
            EmitTurnState();
        }

        private void EndTurn_Click(object sender, RoutedEventArgs e)
        {
            var ender = _state.ActiveBoard;
            _state.EndTurn();
            _state.Log($"{ender.DisplayName} ended their turn — {_state.TurnSummary}");
            EmitTurnState();
        }

        private void EmitTurnState()
        {
            if (!_networked) return;
            _session?.Send(new TurnStateMessage
            {
                TurnNumber = _state.TurnNumber,
                Phase = (DuelPhaseWire)(int)_state.Phase,
                ActiveIsSender = _state.ActiveSide == PlayerSide.Player,
            });
        }

        // --- Toolbar: decks, draw, shuffle, tokens ---

        private void LoadPlayer_Click(object sender, RoutedEventArgs e) => _ = LoadDeckAsync(_state.Player);
        private void LoadOpponent_Click(object sender, RoutedEventArgs e) => _ = LoadDeckAsync(_state.Opponent);

        private async Task LoadDeckAsync(PlayerBoard board)
        {
            if (DeckPicker.SelectedItem is not string name)
            {
                ResultText.Text = "Pick a deck to load.";
                return;
            }
            try
            {
                var deck = YdkFile.Load(name);
                await board.LoadDeckAsync(deck);
                // Remember it so an offline rematch can reload the same deck.
                if (board.Side == PlayerSide.Player) _playerDeckSource = deck; else _opponentDeckSource = deck;
                var who = board.Side == PlayerSide.Player ? "Player" : "Opponent";
                ResultText.Text = $"Loaded \"{name}\" for {who}.";
            }
            catch (Exception ex)
            {
                ResultText.Text = $"Load failed: {ex.Message}";
            }
        }

        // Online, you only act on your own (Player) board; offline, on the active one.
        private PlayerBoard SelfBoard => _networked ? _state.Player : _state.ActiveBoard;

        private void Draw_Click(object sender, RoutedEventArgs e)
        {
            var board = SelfBoard;
            if (board.Draw() is null) { ResultText.Text = "Deck is empty."; return; }
            LogAction(board, "drew a card");
            if (_networked) _session?.Send(new DrawMessage { Count = 1 });
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            var board = SelfBoard;
            board.Shuffle();
            LogAction(board, "shuffled their deck");
            ResultText.Text = "Shuffled.";
            if (_networked) _session?.Send(new ShuffleMessage());
        }

        private void CreateToken_Click(object sender, RoutedEventArgs e)
        {
            // A token appears in hand; the opponent only sees it once it's summoned.
            var board = SelfBoard;
            _state.CreateToken(board);
            LogAction(board, "created a token");
            ResultText.Text = "Token added to hand.";
        }

        private void DiscardRandom_Click(object sender, RoutedEventArgs e)
        {
            var board = SelfBoard;
            if (board.Hand.Count == 0) { ResultText.Text = "Hand is empty."; return; }

            var card = board.Hand[_rng.Next(board.Hand.Count)];
            var name = card.IsToken ? "a token" : card.Name;
            var isToken = card.IsToken;
            Unreveal(card);
            ResetPosition(card);
            _state.MoveToPile(card, board.Graveyard);
            LogAction(board, $"discarded {name}");
            // Discarding to the GY is public, so the opponent sees which card it was.
            if (_networked && !isToken)
                _session?.Send(new HandToPileMessage { Pile = ZoneKind.Graveyard, CardId = card.Card.Id });
            ResultText.Text = $"Discarded {name}.";
        }

        // --- Life points (Player LP mirrors via Player.PropertyChanged) ---

        private void PlayerLpMinus1000_Click(object sender, RoutedEventArgs e) => _state.Player.LifePoints -= 1000;
        private void PlayerLpMinus500_Click(object sender, RoutedEventArgs e) => _state.Player.LifePoints -= 500;
        private void OppLpMinus1000_Click(object sender, RoutedEventArgs e) { if (!_networked) _state.Opponent.LifePoints -= 1000; }
        private void OppLpMinus500_Click(object sender, RoutedEventArgs e) { if (!_networked) _state.Opponent.LifePoints -= 500; }

        // --- Dice ---

        private void Die_Click(object sender, RoutedEventArgs e)
        {
            var roll = _state.RollDie();
            DiceResultText.Text = $"Rolled a {roll}";
            AnnounceRandom($"rolled a {roll}");
        }

        private void Coin_Click(object sender, RoutedEventArgs e)
        {
            var heads = _state.FlipCoin();
            DiceResultText.Text = heads ? "Coin: Heads" : "Coin: Tails";
            AnnounceRandom($"flipped {(heads ? "Heads" : "Tails")}");
        }

        // Records a dice/coin result in my log and, online, sends it so the opponent
        // sees it in theirs too (both players share the same outcome).
        private void AnnounceRandom(string result)
        {
            LogAction(_state.Player, result);
            if (_networked) _session?.Send(new DiceRollMessage { Result = result });
        }

        // ===== Pre-game overlay: practice vs. online, lobby, deck, RPS, order =====

        private void Practice_Click(object sender, RoutedEventArgs e)
        {
            EndSession();
            _networked = false;
            // Use the logged-in account name for the local player, even offline.
            _state.Player.DisplayName = Session.CurrentUser?.Username ?? "Player";
            _state.Opponent.DisplayName = "Opponent";
            StartLog();
            _state.Log("Offline practice started.");
            Overlay.Visibility = Visibility.Collapsed;
            ResultText.Text = "Offline practice — load a deck to begin.";
        }

        private DuelSession CreateSession()
        {
            EndSession();
            var name = Session.CurrentUser?.Username ?? "Player";
            var session = new DuelSession(Dispatcher, name);
            session.Changed += RefreshOverlay;
            session.GameStarting += OnGameStarting;
            session.DuelMessage += OnDuelMessage;
            session.Reconnecting += () => _state.Log("Connection lost — reconnecting…");
            session.Reconnected += () => _state.Log("Reconnected — resume play.");
            _session = session;
            return session;
        }

        // Cross-network play over the cloud relay (room codes).
        private void PlayInternet_Click(object sender, RoutedEventArgs e)
        {
            CreateSession();
            RoomCodeBox.Text = "";
            ShowOnlyPanel(InternetPanel);
        }

        private void HostInternet_Click(object sender, RoutedEventArgs e) => _session?.HostOnline();

        private void JoinInternet_Click(object sender, RoutedEventArgs e) => _session?.JoinOnline(RoomCodeBox.Text);

        private void RoomCodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _session?.JoinOnline(RoomCodeBox.Text);
        }

        // Same-network play via UDP discovery.
        private void PlayLan_Click(object sender, RoutedEventArgs e)
        {
            CreateSession();
            _session!.EnterLobby();
            RefreshOverlay();
        }

        private void CreateRoom_Click(object sender, RoutedEventArgs e) => _session?.Host();

        private void JoinRoom_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is DiscoveredRoom room) _session?.Join(room);
        }

        private void BackToEntry_Click(object sender, RoutedEventArgs e)
        {
            EndSession();
            ShowOnlyPanel(EntryPanel);
        }

        private void ReadyDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_session is null) return;
            if (OnlineDeckPicker.SelectedItem is not string name)
            {
                DeckStatusText.Text = "Pick a deck first.";
                return;
            }
            var deck = YdkFile.Load(name);
            _session.SelectDeck(new DeckSelectedMessage
            {
                DeckName = name,
                MainIds = deck.Main.ToList(),
                ExtraIds = deck.Extra.ToList(),
            });
            ReadyButton.IsEnabled = false;
            OnlineDeckPicker.IsEnabled = false;
        }

        private void RpsRock_Click(object sender, RoutedEventArgs e) => _session?.ThrowRps(RpsChoice.Rock);
        private void RpsPaper_Click(object sender, RoutedEventArgs e) => _session?.ThrowRps(RpsChoice.Paper);
        private void RpsScissors_Click(object sender, RoutedEventArgs e) => _session?.ThrowRps(RpsChoice.Scissors);

        private void GoFirst_Click(object sender, RoutedEventArgs e) => _session?.ChooseOrder(true);
        private void GoSecond_Click(object sender, RoutedEventArgs e) => _session?.ChooseOrder(false);

        private void RefreshOverlay()
        {
            if (_session is null) return;
            // While the end screen is up the session is still InDuel; don't let a stray
            // session event pull the overlay back to a blank in-duel state.
            if (_duelOver && _session.Phase == MatchPhase.InDuel) return;
            switch (_session.Phase)
            {
                case MatchPhase.Lobby:
                    ShowOnlyPanel(LobbyPanel);
                    RoomsList.ItemsSource = _session.Rooms;
                    NoRoomsText.Visibility = _session.Rooms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case MatchPhase.Connecting:
                    ShowOnlyPanel(ConnectingPanel);
                    ConnectingText.Text = _session.StatusMessage ?? "Connecting…";
                    break;
                case MatchPhase.Reconnecting:
                    // Cover the board (blocks input) while we rebuild the dropped link;
                    // the boards stay put and play resumes when it reconnects.
                    ShowOnlyPanel(ConnectingPanel);
                    ConnectingText.Text = _session.StatusMessage ?? "Reconnecting…";
                    break;
                case MatchPhase.DeckSelect:
                    ShowOnlyPanel(DeckPanel);
                    DeckOppText.Text = _session.OpponentName is { } n ? $"Connected to {n}." : "Connected.";
                    DeckStatusText.Text =
                        $"You: {(_session.LocalDeckReady ? "ready" : "choosing…")}\n" +
                        $"{_session.OpponentName ?? "Opponent"}: {(_session.OpponentDeckReady ? "ready" : "choosing…")}";
                    break;
                case MatchPhase.Rps:
                    ShowOnlyPanel(RpsPanel);
                    RpsButtons.IsEnabled = !_session.LocalRpsThrown;
                    RpsStatusText.Text = _session.RpsMessage ??
                        (_session.LocalRpsThrown ? "Waiting for opponent…" : "Make your choice.");
                    break;
                case MatchPhase.ChooseOrder:
                    ShowOnlyPanel(OrderPanel);
                    OrderText.Text = _session.RpsMessage ?? "";
                    OrderButtons.Visibility = _session.LocalWonRps == true ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case MatchPhase.Disconnected:
                    ShowOnlyPanel(DisconnectedPanel);
                    DisconnectedText.Text = _session.StatusMessage ?? "Disconnected.";
                    break;
                case MatchPhase.InDuel:
                    // While decks are still pre-loading, keep the loading panel up; the
                    // duel reveals the board itself once caching finishes.
                    if (!_loadingDecks) Overlay.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ShowOnlyPanel(FrameworkElement panel)
        {
            Overlay.Visibility = Visibility.Visible;
            foreach (var p in new FrameworkElement[]
                { EntryPanel, InternetPanel, LobbyPanel, ConnectingPanel, DeckPanel, RpsPanel, OrderPanel, LoadingPanel, DisconnectedPanel, EndPanel })
                p.Visibility = ReferenceEquals(p, panel) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnGameStarting(GameStartInfo info)
        {
            _gameInfo = info;
            _ = BeginDuelAsync(info.LocalGoesFirst);
        }

        // Sets up (or resets, for a rematch) a networked duel from the stored GameStartInfo.
        private async Task BeginDuelAsync(bool localGoesFirst)
        {
            if (_gameInfo is not { } info) return;
            _networked = true;
            ClearDuelEnd();
            // Keep the overlay up on a "Loading decks…" panel until every card image
            // for both players is fetched and cached, so the board never pops in blanks.
            _loadingDecks = true;
            ShowOnlyPanel(LoadingPanel);
            EndPanel.Visibility = Visibility.Collapsed;

            // Set up the opponent shadow and turn state synchronously first, so any
            // early inbound message applies to a ready board (not one reset later).
            SetupOpponentShadow(
                deckCount: info.RemoteDeck.MainIds.Count - 5,
                handCount: 5,
                extraCount: info.RemoteDeck.ExtraIds.Count);
            _state.TurnNumber = 1;
            _state.Phase = DuelPhase.Main1;
            _state.ActiveSide = localGoesFirst ? PlayerSide.Player : PlayerSide.Opponent;

            // Portrait names come from the usernames exchanged in the handshake.
            _state.Player.DisplayName = _session?.LocalName ?? "You";
            _state.Opponent.DisplayName = _session?.OpponentName ?? "Opponent";
            // Reset LP for a fresh game (a rematch reuses these boards). Suppress logging
            // of the reset itself; StartLog re-enables it from a clean 8000 baseline.
            _loggingLp = false;
            _state.Player.LifePoints = 8000;
            StartLog();
            _state.Log($"Duel started — {_state.Player.DisplayName} vs {_state.Opponent.DisplayName}.");

            // Online, decks and the opponent's LP aren't manually editable.
            SetOfflineControlsEnabled(false);
            ResultText.Text = localGoesFirst
                ? "You go first."
                : $"{_session?.OpponentName ?? "Opponent"} goes first.";

            // Pre-fetch (and cache) every card image for both decks while the loading
            // panel is up, so nothing downloads mid-duel.
            await PreloadDeckImagesAsync(info);

            // Local player is always the bottom ("Player") board; load their real deck.
            var myDeck = new Deck { Name = info.LocalDeck.DeckName };
            myDeck.Main.AddRange(info.LocalDeck.MainIds);
            myDeck.Extra.AddRange(info.LocalDeck.ExtraIds);
            await _state.Player.LoadDeckAsync(myDeck);

            // Everything's cached and the board is ready — drop the overlay into the duel.
            _loadingDecks = false;
            Overlay.Visibility = Visibility.Collapsed;
        }

        // Fetches and caches the small board thumbnail for every unique card across both
        // players' decks, updating the loading panel's progress as it goes. Best-effort:
        // a card that can't be fetched just falls back to a card-back at the table.
        private async Task PreloadDeckImagesAsync(GameStartInfo info)
        {
            var ids = new HashSet<long>();
            foreach (var id in info.LocalDeck.MainIds) ids.Add(id);
            foreach (var id in info.LocalDeck.ExtraIds) ids.Add(id);
            foreach (var id in info.RemoteDeck.MainIds) ids.Add(id);
            foreach (var id in info.RemoteDeck.ExtraIds) ids.Add(id);

            // Resolve each passcode to its image record (one DB round-trip).
            var idList = ids.ToList();
            List<CardImage> images;
            await using (var db = new AppDbContext())
            {
                images = await db.Cards
                    .Where(c => idList.Contains(c.Id))
                    .SelectMany(c => c.Images)
                    .ToListAsync();
            }

            int total = images.Count;
            int done = 0;
            void Report() => Dispatcher.Invoke(() =>
            {
                LoadingProgress.Maximum = Math.Max(total, 1);
                LoadingProgress.Value = done;
                LoadingStatusText.Text = total == 0 ? "No card images to load." : $"{done} / {total} cards";
            });
            Report();

            // The image service caps concurrency and rate-limits internally, so we can
            // safely fan every fetch out at once and let it pace them.
            var tasks = images.Select(async img =>
            {
                var url = string.IsNullOrEmpty(img.ImageUrlSmall) ? img.ImageUrl : img.ImageUrlSmall;
                if (!string.IsNullOrEmpty(url))
                {
                    try { await _images.GetImagePathAsync(img.ApiImageId, url, CardImageSize.Small); }
                    catch { /* best-effort; a missing image just shows a card-back */ }
                }
                Interlocked.Increment(ref done);
                Report();
            });
            await Task.WhenAll(tasks);
        }

        // --- End of duel: result screen, rematch, quit ---

        // Raises the end screen over the board. Reachable only via a concede / admit-defeat.
        private void EndDuel(string result, string detail)
        {
            _duelOver = true;
            _localWantsRematch = false;
            _remoteWantsRematch = false;
            // Popups float above the overlay — close any so they don't hover the end screen.
            ViewMenu.IsOpen = CardActions.IsOpen = DeckActions.IsOpen = PileViewer.IsOpen = RevealViewer.IsOpen = false;
            EndResultText.Text = result;
            EndDetailText.Text = detail;
            EndRematchStatus.Text = "";
            RematchButton.IsEnabled = true;
            ShowOnlyPanel(EndPanel);
        }

        private void ClearDuelEnd()
        {
            _duelOver = false;
            _localWantsRematch = false;
            _remoteWantsRematch = false;
            _playerLpZeroPrompted = false;
            _oppLpZeroPrompted = false;
        }

        private void Rematch_Click(object sender, RoutedEventArgs e)
        {
            if (!_duelOver) return;
            if (!_networked) { _ = RestartOfflineAsync(); return; }
            if (_localWantsRematch) return;

            _localWantsRematch = true;
            RematchButton.IsEnabled = false;
            _session?.Send(new RematchMessage());
            UpdateRematchStatus();
            TryStartRematch();
        }

        // Both sides agreed: restart with the same decks; the previous loser goes first.
        private void TryStartRematch()
        {
            if (!_duelOver || !_localWantsRematch || !_remoteWantsRematch) return;
            _ = BeginDuelAsync(localGoesFirst: _iLost);
        }

        private void UpdateRematchStatus()
        {
            var opp = _state.Opponent.DisplayName;
            EndRematchStatus.Text = (_localWantsRematch, _remoteWantsRematch) switch
            {
                (true, false) => $"Waiting for {opp}…",
                (false, true) => $"{opp} wants a rematch.",
                _ => "",
            };
        }

        // Offline restart: reset both boards to a fresh game, reloading the same decks.
        private async Task RestartOfflineAsync()
        {
            ClearDuelEnd();
            EndPanel.Visibility = Visibility.Collapsed;
            Overlay.Visibility = Visibility.Collapsed;

            _loggingLp = false;
            _state.Player.LifePoints = 8000;
            _state.Opponent.LifePoints = 8000;
            _state.TurnNumber = 1;
            _state.Phase = DuelPhase.Main1;
            _state.ActiveSide = PlayerSide.Player;
            StartLog();
            _state.Log("New duel started.");
            ResultText.Text = "New duel.";

            if (_playerDeckSource is { } pd) await _state.Player.LoadDeckAsync(pd);
            if (_opponentDeckSource is { } od) await _state.Opponent.LoadDeckAsync(od);
        }

        private void QuitDuel_Click(object sender, RoutedEventArgs e)
        {
            EndSession();
            ClearDuelEnd();
            ShowOnlyPanel(EntryPanel);
        }

        private void SetupOpponentShadow(int deckCount, int handCount, int extraCount)
        {
            var o = _state.Opponent;
            o.Hand.Clear(); o.Deck.Clear(); o.ExtraDeck.Clear(); o.Graveyard.Clear(); o.Banished.Clear();
            foreach (var slot in o.AllSlots()) slot.Card = null;
            for (var i = 0; i < Math.Max(0, handCount); i++) o.Hand.Add(BoardCard.Hidden());
            for (var i = 0; i < Math.Max(0, deckCount); i++) o.Deck.Add(BoardCard.Hidden());
            for (var i = 0; i < Math.Max(0, extraCount); i++) o.ExtraDeck.Add(BoardCard.Hidden());
            o.LifePoints = 8000;
        }

        // --- Applying the opponent's actions to their shadow (top) board ---

        private readonly Queue<NetMessage> _incoming = new();
        private bool _processing;

        // Queue inbound messages and apply them strictly in order (each may await a
        // card lookup), so nothing is applied out of sequence.
        private void OnDuelMessage(NetMessage message)
        {
            _incoming.Enqueue(message);
            if (!_processing) _ = ProcessIncomingAsync();
        }

        private async Task ProcessIncomingAsync()
        {
            _processing = true;
            try
            {
                while (_incoming.Count > 0) await ApplyRemoteAsync(_incoming.Dequeue());
            }
            finally { _processing = false; }
        }

        private async Task ApplyRemoteAsync(NetMessage message)
        {
            var o = _state.Opponent; // the sender is my opponent
            switch (message)
            {
                case SummonMessage s:
                    RemoveFromOppSource(s.From, s.CardId);
                    var summoned = await BuildCardAsync(s.CardId, defense: s.Defense);
                    PlaceInZone(o, s.Zone, s.Index, summoned);
                    LogAction(o, $"Summoned {NameOf(summoned)}");
                    break;

                case SetCardMessage set:
                    // A face-down card has no id, so a field relocation is cleared by
                    // coordinates; other sources fall back to the id/last-card removal.
                    if (set.From is ZoneKind.MainMonster or ZoneKind.ExtraMonster or ZoneKind.SpellTrap or ZoneKind.Field)
                    {
                        if (o.Slot(set.From, set.FromIndex) is { } srcSlot) srcSlot.Card = null;
                    }
                    else RemoveFromOppSource(set.From, null);
                    PlaceInZone(o, set.Zone, set.Index, new BoardCard(new Card { Name = "Card" }) { FaceDown = true, Defense = set.Defense });
                    LogAction(o, set.Zone == ZoneKind.SpellTrap ? "Set a Spell/Trap" : "Set a monster");
                    break;

                case RevealMessage r:
                    var flipped = await BuildCardAsync(r.CardId, defense: r.Defense);
                    if (o.Slot(r.Zone, r.Index) is { } rslot) rslot.Card = flipped;
                    LogAction(o, $"revealed {NameOf(flipped)}");
                    break;

                case PositionChangeMessage pc:
                    if (o.Slot(pc.Zone, pc.Index)?.Card is { } pcard) { pcard.FaceDown = pc.FaceDown; pcard.Defense = pc.Defense; }
                    LogAction(o, "changed a card's position");
                    break;

                case FieldToPileMessage f:
                    var moved = o.Slot(f.Zone, f.Index)?.Card;
                    if (o.Slot(f.Zone, f.Index) is { } fslot) fslot.Card = null;
                    if (!f.IsToken)
                    {
                        var dest = f.CardId is { } fid ? await BuildCardAsync(fid) : (moved ?? BoardCard.Hidden());
                        if (f.CardId is null) { dest.FaceDown = true; dest.Defense = false; }
                        AddToPile(o, f.Pile, dest, f.ToTop);
                        var name = f.CardId is not null ? NameOf(dest) : "a card";
                        LogAction(o, $"moved {name} to {PileLabel(f.Pile, f.ToTop)}");
                    }
                    else LogAction(o, "removed a token from the field");
                    break;

                case HandToPileMessage h:
                    RemoveOneHidden(o.Hand);
                    var hdest = h.CardId is { } hid ? await BuildCardAsync(hid) : BoardCard.Hidden();
                    AddToPile(o, h.Pile, hdest, h.ToTop);
                    LogAction(o, $"moved {(h.CardId is not null ? NameOf(hdest) : "a card")} from their hand to {PileLabel(h.Pile, h.ToTop)}");
                    break;

                case PileMoveMessage pm:
                    RemoveFromOppSource(pm.From, pm.CardId);
                    // Reveal at a public destination; a move into a private zone (hand/deck)
                    // stays a hidden back even if the id came across for source removal.
                    var pmPublicDest = pm.To is ZoneKind.Graveyard or ZoneKind.Banished;
                    var pmDest = pmPublicDest && pm.CardId is { } pmid ? await BuildCardAsync(pmid) : BoardCard.Hidden();
                    AddToPile(o, pm.To, pmDest, pm.ToTop);
                    LogAction(o, $"moved {(pmPublicDest && pm.CardId is not null ? NameOf(pmDest) : "a card")} to {PileLabel(pm.To, pm.ToTop)}");
                    break;

                case DrawMessage d:
                    for (var i = 0; i < d.Count; i++)
                    {
                        if (o.Deck.Count > 0) o.Deck.RemoveAt(0);
                        o.Hand.Add(BoardCard.Hidden());
                    }
                    LogAction(o, d.Count == 1 ? "drew a card" : $"drew {d.Count} cards");
                    break;

                case DeckToPileMessage dp:
                    // Drop a placeholder off my view of their deck (top/bottom), then add
                    // the now-public milled card to the pile.
                    if (o.Deck.Count > 0) o.Deck.RemoveAt(dp.FromBottom ? o.Deck.Count - 1 : 0);
                    var milled = await BuildCardAsync(dp.CardId);
                    AddToPile(o, dp.Pile, milled, toTop: false);
                    LogAction(o, $"sent {NameOf(milled)} from the {(dp.FromBottom ? "bottom" : "top")} of their Deck to {PileLabel(dp.Pile, false)}");
                    break;

                case LifePointsMessage lp:
                    o.LifePoints = lp.LifePoints; // the Opponent LP handler logs the change
                    break;

                case TokenSummonMessage t:
                    PlaceInZone(o, t.Zone, t.Index, new BoardCard(new Card { Name = "Token" }) { IsToken = true, Defense = t.Defense });
                    LogAction(o, "Special Summoned a token");
                    break;

                case CounterMessage c:
                    if (o.Slot(c.Zone, c.Index)?.Card is { } ccard) ccard.Counters = c.Counters;
                    LogAction(o, "changed counters on a card");
                    break;

                case AnnounceMessage a:
                {
                    // The sender's own card lives on my Opponent shadow; a card they
                    // pointed at on my side is on my Player board.
                    var pointed = a.Side switch
                    {
                        AnnounceSide.SenderField => o.Slot(a.Zone, a.Index)?.Card,
                        AnnounceSide.ReceiverField => _state.Player.Slot(a.Zone, a.Index)?.Card,
                        _ => null,
                    };
                    _state.Announce(o.DisplayName, a.Verb, a.Target);
                    PointAt(pointed);
                    LogAction(o, $"{a.Verb} {a.Target}");
                    break;
                }

                case RevealCardsMessage rc:
                    if (rc.CardIds.Count == 0)
                    {
                        RevealViewer.IsOpen = false;
                    }
                    else
                    {
                        var revealed = new List<BoardCard>();
                        foreach (var id in rc.CardIds) revealed.Add(await BuildCardAsync(id));
                        RevealViewerTitle.Text = $"{o.DisplayName} {rc.Label}";
                        RevealViewerItems.ItemsSource = revealed;
                        RevealViewer.IsOpen = true;
                        LogAction(o, rc.Label);
                    }
                    break;

                case TurnStateMessage ts:
                    _state.TurnNumber = ts.TurnNumber;
                    _state.Phase = (DuelPhase)(int)ts.Phase;
                    _state.ActiveSide = ts.ActiveIsSender ? PlayerSide.Opponent : PlayerSide.Player;
                    _state.Log($"— {_state.TurnSummary} —");
                    break;

                case ChatMessage cm:
                    _state.LogChat(o.DisplayName, cm.Text, DuelLogSide.Opponent);
                    break;

                case DiceRollMessage dr:
                    LogAction(o, dr.Result);
                    break;

                case ConcedeMessage cc:
                    if (!_duelOver)
                    {
                        LogAction(o, $"{cc.Verb} — you win!");
                        _iLost = false;
                        EndDuel("You win!", $"{o.DisplayName} {cc.Verb}.");
                    }
                    break;

                case RematchMessage:
                    _remoteWantsRematch = true;
                    if (_duelOver) { UpdateRematchStatus(); TryStartRematch(); }
                    break;

                case EmoteMessage em:
                    o.Emote = em.Emote;
                    if (o.Emote is { } oe) LogAction(o, EmoteVerb(oe));
                    break;

                case ControlSwapMessage cs:
                {
                    // Sides are mirrored for the receiver: whatever the sender did on
                    // their Player board happens on our Opponent shadow and vice versa.
                    var srcBoard = cs.FromSendersField ? o : _state.Player;
                    var dstBoard = cs.FromSendersField ? _state.Player : o;
                    var srcSlot = srcBoard.Slot(cs.SourceZone, cs.SourceIndex);
                    var dstSlot = dstBoard.Slot(cs.DestZone, cs.DestIndex);
                    if (srcSlot?.Card is { } moving && dstSlot is not null)
                    {
                        srcSlot.Card = null;
                        moving.FaceDown = cs.FaceDown;
                        moving.Defense = cs.Defense;
                        dstSlot.Card = moving;
                        _state.Log($"Control of {NameOf(moving)} passes from {srcBoard.DisplayName} to {dstBoard.DisplayName}");
                    }
                    break;
                }

                case AttackMessage atk:
                    if (o.Slot(atk.AttackerZone, atk.AttackerIndex)?.Card is { } atkCard)
                    {
                        if (atk.Direct)
                        {
                            AnimateAttack(atkCard, null, direct: true);
                            LogAction(o, $"declares a direct attack with {NameOf(atkCard)}");
                        }
                        else
                        {
                            var tgt = _state.Player.Slot(atk.TargetZone, atk.TargetIndex)?.Card;
                            AnimateAttack(atkCard, tgt, direct: false);
                            LogAction(o, tgt is null
                                ? $"declares an attack with {NameOf(atkCard)}"
                                : $"declares an attack with {NameOf(atkCard)} targeting {NameOf(tgt)}");
                        }
                    }
                    break;

                case ShuffleMessage:
                    LogAction(o, "shuffled their deck");
                    break;
            }
        }

        private ZoneSlot? FindOppSlotById(long cardId)
        {
            foreach (var slot in _state.Opponent.AllSlots())
                if (slot.Card is { IsToken: false } bc && bc.Card.Id == cardId) return slot;
            return null;
        }

        /// <summary>Removes the card being placed from wherever it came from in the
        /// opponent shadow, using the sender's source hint.</summary>
        private void RemoveFromOppSource(ZoneKind from, long? cardId)
        {
            var o = _state.Opponent;
            switch (from)
            {
                case ZoneKind.MainMonster or ZoneKind.ExtraMonster or ZoneKind.SpellTrap or ZoneKind.Field:
                    if (cardId is { } id && FindOppSlotById(id) is { } slot) slot.Card = null; // relocation
                    break;
                case ZoneKind.Hand: RemoveOneHidden(o.Hand); break;
                case ZoneKind.Deck: RemoveOneHidden(o.Deck); break;
                case ZoneKind.ExtraDeck: RemoveOneHidden(o.ExtraDeck); break;
                case ZoneKind.Graveyard: RemoveByIdOrLast(o.Graveyard, cardId); break;
                case ZoneKind.Banished: RemoveByIdOrLast(o.Banished, cardId); break;
            }
        }

        private static void RemoveByIdOrLast(ObservableCollection<BoardCard> pile, long? cardId)
        {
            if (cardId is { } id)
            {
                var match = pile.FirstOrDefault(c => !c.IsToken && c.Card.Id == id);
                if (match is not null) { pile.Remove(match); return; }
            }
            if (pile.Count > 0) pile.RemoveAt(pile.Count - 1);
        }

        private static void PlaceInZone(PlayerBoard board, ZoneKind kind, int index, BoardCard card)
        {
            if (board.Slot(kind, index) is { } slot) slot.Card = card;
        }

        private static void RemoveOneHidden(ObservableCollection<BoardCard> hand)
        {
            if (hand.Count > 0) hand.RemoveAt(hand.Count - 1);
        }

        private static void AddToPile(PlayerBoard board, ZoneKind pile, BoardCard card, bool toTop)
        {
            if (board.Pile(pile) is not { } p) return;
            if (toTop) p.Insert(0, card); else p.Add(card);
        }

        /// <summary>Builds a face-up board card for a passcode using the local card DB
        /// (both players share the same seeded database) and its cached image.</summary>
        private async Task<BoardCard> BuildCardAsync(long cardId, bool defense = false)
        {
            Card? card;
            await using (var db = new AppDbContext())
                card = await db.Cards.Include(c => c.Images).FirstOrDefaultAsync(c => c.Id == cardId);
            card ??= new Card { Id = cardId, Name = $"#{cardId}" };

            var bc = new BoardCard(card) { Defense = defense };
            try { bc.Image = await ImageLoading.GetThumbnailAsync(_images, card); }
            catch { /* image best-effort */ }
            return bc;
        }

        private void SetOfflineControlsEnabled(bool enabled)
        {
            LoadPlayerBtn.IsEnabled = enabled;
            LoadOpponentBtn.IsEnabled = enabled;
            DeckPicker.IsEnabled = enabled;
            OppLpBox.IsEnabled = enabled;
            OppLpMinus1000Btn.IsEnabled = enabled;
            OppLpMinus500Btn.IsEnabled = enabled;
        }

        private void EndSession()
        {
            // Tell a live opponent we're leaving on purpose (so they don't wait to
            // reconnect), then tear the session down.
            _session?.LeaveAndDispose();
            _session = null;
            _networked = false;
            _loadingDecks = false;
            _incoming.Clear();
            _revealed.Clear();
            _gameInfo = null;
            ClearDuelEnd();
            _state.Player.Emote = null;
            _state.Opponent.Emote = null;
            RevealViewer.IsOpen = false;
            DeckActions.IsOpen = false;
            SetOfflineControlsEnabled(true);
        }
    }
}
