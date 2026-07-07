using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YGODuelSimulator.Models.Duel;
using YGODuelSimulator.Services;

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

        // A "point"/"declare" cue clears itself after a moment.
        private readonly DispatcherTimer _cueTimer;
        private BoardCard? _pointedCard;

        public DuelRoomPage()
        {
            InitializeComponent();
            DataContext = _state;
            DeckPicker.ItemsSource = YdkFile.ListDeckNames();

            _cueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _cueTimer.Tick += (_, _) => ClearCue();
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
            _state.Selected = card;
            ViewMenu.IsOpen = true;
            e.Handled = true;
        }

        // Right-click selects the card and opens the play-action menu at the cursor.
        private void Card_RightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not BoardCard card) return;
            _state.Selected = card;
            CardActions.IsOpen = true;
            e.Handled = true;
        }

        // --- View menu: inspect / declare / point ---

        private void Inspect_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { IsToken: false } c) Preview.Show(c.Card);
            ViewMenu.IsOpen = false;
        }

        private void DeclareEffect_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { } c) { _state.Announce(c, "declares the effect of"); PointAt(c); }
            ViewMenu.IsOpen = false;
        }

        private void Point_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { } c) { _state.Announce(c, "points to"); PointAt(c); }
            ViewMenu.IsOpen = false;
        }

        private void PointAt(BoardCard card)
        {
            if (_pointedCard is { } prev) prev.IsPointed = false;
            _pointedCard = card;
            card.IsPointed = true;
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

        private void Zone_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ZoneSlot slot) return;

            // Only meaningful while placing a card into a highlighted, empty zone.
            if (_pending is { } p && slot.IsTarget && _state.Selected is { } card)
            {
                _state.MoveToSlot(card, slot);
                card.FaceDown = p.faceDown;
                card.Defense = p.defense;
                EndPlacement();
                Deselect();
                e.Handled = true;
            }
        }

        private void Playmat_Click(object sender, MouseButtonEventArgs e)
        {
            // A click that reached the playmat background cancels the selection.
            EndPlacement();
            Deselect();
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { EndPlacement(); Deselect(); }
        }

        private void BeginPlacement(bool faceDown, bool defense, params ZoneKind[] kinds)
        {
            CardActions.IsOpen = false;
            // Close the pile viewer so the board is visible and clickable for the drop
            // (e.g. Special Summon chosen from the Deck/GY list).
            PileViewer.IsOpen = false;
            if (_state.Selected is not { } card) return;
            _pending = (faceDown, defense);
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
            BeginPlacement(faceDown: false, defense: false, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void SetMonster_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement(faceDown: true, defense: true, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void SpecialAtk_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement(faceDown: false, defense: false, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void SpecialDef_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement(faceDown: false, defense: true, ZoneKind.MainMonster, ZoneKind.ExtraMonster);

        private void ActivateST_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement(faceDown: false, defense: false, ZoneKind.SpellTrap);

        private void SetST_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement(faceDown: true, defense: false, ZoneKind.SpellTrap);

        private void FieldSpell_Click(object sender, RoutedEventArgs e) =>
            BeginPlacement(faceDown: false, defense: false, ZoneKind.Field);

        // --- Action menu: position (act now) ---

        private void SelAttack_Click(object sender, RoutedEventArgs e) => SetPosition(faceDown: false, defense: false);
        private void SelDefense_Click(object sender, RoutedEventArgs e) => SetPosition(faceDown: false, defense: true);

        private void SelFlip_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { } c)
            {
                c.FaceDown = !c.FaceDown;
                // A flip summon is always to attack (upright) — you can't flip a
                // monster face-up into defense position.
                if (!c.FaceDown) c.Defense = false;
            }
            CardActions.IsOpen = false;
        }

        private void SetPosition(bool faceDown, bool defense)
        {
            if (_state.Selected is { } c) { c.FaceDown = faceDown; c.Defense = defense; }
            CardActions.IsOpen = false;
        }

        // --- Action menu: counters (stay open so several can be added) ---

        private void AddCounter_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { } c) c.Counters++;
        }

        private void RemoveCounter_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Selected is { } c) c.Counters--;
        }

        // --- Action menu: move to a pile (act now) ---

        private void SelToHand_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Hand);
        private void SelToGrave_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Graveyard);
        private void SelToBanish_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Banished);
        private void SelToDeckTop_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Deck, toTop: true);
        private void SelToDeckBottom_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.Deck);
        private void SelToExtra_Click(object sender, RoutedEventArgs e) => MoveSelected(b => b.ExtraDeck);

        private void MoveSelected(Func<PlayerBoard, ObservableCollection<BoardCard>> pick, bool toTop = false)
        {
            if (_state.Selected is not { } card) return;
            var board = _state.FindBoard(card);
            ResetPosition(card);
            _state.MoveToPile(card, pick(board), toTop);
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

        // --- Turn / phase ---

        private void NextPhase_Click(object sender, RoutedEventArgs e) => _state.NextPhase();
        private void EndTurn_Click(object sender, RoutedEventArgs e) => _state.EndTurn();

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
                await board.LoadDeckAsync(YdkFile.Load(name));
                var who = board.Side == PlayerSide.Player ? "Player" : "Opponent";
                ResultText.Text = $"Loaded \"{name}\" for {who}.";
            }
            catch (Exception ex)
            {
                ResultText.Text = $"Load failed: {ex.Message}";
            }
        }

        private void Draw_Click(object sender, RoutedEventArgs e)
        {
            if (_state.ActiveBoard.Draw() is null) ResultText.Text = "Deck is empty.";
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _state.ActiveBoard.Shuffle();
            ResultText.Text = "Shuffled.";
        }

        private void CreateToken_Click(object sender, RoutedEventArgs e)
        {
            _state.CreateToken(_state.ActiveBoard);
            ResultText.Text = "Token added to hand.";
        }

        // --- Life points ---

        private void PlayerLpMinus1000_Click(object sender, RoutedEventArgs e) => _state.Player.LifePoints -= 1000;
        private void PlayerLpMinus500_Click(object sender, RoutedEventArgs e) => _state.Player.LifePoints -= 500;
        private void OppLpMinus1000_Click(object sender, RoutedEventArgs e) => _state.Opponent.LifePoints -= 1000;
        private void OppLpMinus500_Click(object sender, RoutedEventArgs e) => _state.Opponent.LifePoints -= 500;

        // --- Dice ---

        private void Die_Click(object sender, RoutedEventArgs e) => ResultText.Text = $"Rolled a {_state.RollDie()}.";
        private void Coin_Click(object sender, RoutedEventArgs e) => ResultText.Text = $"Coin: {(_state.FlipCoin() ? "Heads" : "Tails")}.";
    }
}
