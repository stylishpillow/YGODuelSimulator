using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YGODuelSimulator.Models.Duel;
using YGODuelSimulator.Services;
using YGODuelSimulator.Views.Controls;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>
    /// Local manual duel playfield: load a deck and move cards around the board by
    /// dragging, with right-click menus for position/flip and quick moves. Nothing
    /// is rule-enforced.
    /// </summary>
    public partial class DuelRoomPage : Page
    {
        private readonly DuelState _state = new();

        // Drag tracking.
        private Point _pressPoint;
        private BoardCard? _pressCard;
        private bool _dragging;

        public DuelRoomPage()
        {
            InitializeComponent();
            DataContext = _state;
            DeckPicker.ItemsSource = YdkFile.ListDeckNames();
            UpdateLp();
        }

        // --- Dragging a card / left-click to inspect ---

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressCard = (sender as FrameworkElement)?.DataContext as BoardCard;
            _pressPoint = e.GetPosition(null);
            _dragging = false;
        }

        private void Card_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pressCard is null || _dragging) return;

            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _dragging = true;
            var data = new DataObject(typeof(BoardCard), _pressCard);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
            _pressCard = null;
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging && _pressCard is not null) Preview.Show(_pressCard.Card);
            _pressCard = null;
            _dragging = false;
        }

        // --- Drop targets ---

        private void Zone_Drop(object sender, DragEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ZoneSlot slot &&
                e.Data.GetData(typeof(BoardCard)) is BoardCard card)
            {
                _state.MoveToSlot(card, slot);
            }
            e.Handled = true;
        }

        private void Pile_Drop(object sender, DragEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag ||
                e.Data.GetData(typeof(BoardCard)) is not BoardCard card)
                return;

            ResetPosition(card);
            switch (tag)
            {
                case "gy": _state.MoveToPile(card, _state.Graveyard); break;
                case "ban": _state.MoveToPile(card, _state.Banished); break;
                case "hand": _state.MoveToPile(card, _state.Hand); break;
                case "deck": _state.MoveToPile(card, _state.Deck); break;
                case "extra": _state.MoveToPile(card, _state.ExtraDeck); break;
            }
            e.Handled = true;
        }

        // --- Pile viewer ---

        private void Pile_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag) return;

            (string? title, System.Collections.IEnumerable? source) = tag switch
            {
                "deck" => ("Deck", _state.Deck),
                "extra" => ("Extra Deck", _state.ExtraDeck),
                "gy" => ("Graveyard", _state.Graveyard),
                "ban" => ("Banished", _state.Banished),
                _ => (null, null),
            };
            if (title is null) return;

            PileViewerTitle.Text = title;
            PileViewerItems.ItemsSource = source;
            PileViewer.IsOpen = true;
        }

        // --- Right-click card actions ---

        private static BoardCard? CardOf(object sender) =>
            (sender as FrameworkElement)?.DataContext as BoardCard;

        private static void ResetPosition(BoardCard card)
        {
            card.FaceDown = false;
            card.Defense = false;
        }

        private void ToAttack_Click(object sender, RoutedEventArgs e)
        {
            if (CardOf(sender) is { } c) { c.FaceDown = false; c.Defense = false; }
        }

        private void ToDefense_Click(object sender, RoutedEventArgs e)
        {
            if (CardOf(sender) is { } c) { c.FaceDown = false; c.Defense = true; }
        }

        private void SetFaceDown_Click(object sender, RoutedEventArgs e)
        {
            if (CardOf(sender) is { } c) { c.FaceDown = true; c.Defense = false; }
        }

        private void Flip_Click(object sender, RoutedEventArgs e)
        {
            if (CardOf(sender) is { } c) c.FaceDown = !c.FaceDown;
        }

        private void ToGrave_Click(object sender, RoutedEventArgs e) => MoveCard(sender, _state.Graveyard);
        private void ToBanish_Click(object sender, RoutedEventArgs e) => MoveCard(sender, _state.Banished);
        private void ToHand_Click(object sender, RoutedEventArgs e) => MoveCard(sender, _state.Hand);
        private void ToDeckTop_Click(object sender, RoutedEventArgs e) => MoveCard(sender, _state.Deck, toTop: true);
        private void ToDeckBottom_Click(object sender, RoutedEventArgs e) => MoveCard(sender, _state.Deck);

        private void MoveCard(object sender, System.Collections.ObjectModel.ObservableCollection<BoardCard> pile, bool toTop = false)
        {
            if (CardOf(sender) is not { } card) return;
            ResetPosition(card);
            _state.MoveToPile(card, pile, toTop);
        }

        // --- Toolbar ---

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            if (DeckPicker.SelectedItem is not string name)
            {
                ResultText.Text = "Pick a deck to load.";
                return;
            }
            try
            {
                await _state.LoadDeckAsync(YdkFile.Load(name));
                UpdateLp();
                ResultText.Text = $"Loaded \"{name}\".";
            }
            catch (Exception ex)
            {
                ResultText.Text = $"Load failed: {ex.Message}";
            }
        }

        private void Draw_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Draw() is null) ResultText.Text = "Deck is empty.";
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _state.Shuffle();
            ResultText.Text = "Shuffled.";
        }

        private void LpMinus1000_Click(object sender, RoutedEventArgs e) => AdjustLp(-1000);
        private void LpMinus500_Click(object sender, RoutedEventArgs e) => AdjustLp(-500);
        private void LpReset_Click(object sender, RoutedEventArgs e) { _state.LifePoints = 8000; UpdateLp(); }

        private void Lp_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (int.TryParse(LpBox.Text, out var lp)) _state.LifePoints = lp;
            UpdateLp();
        }

        private void AdjustLp(int delta) { _state.LifePoints += delta; UpdateLp(); }
        private void UpdateLp() => LpBox.Text = _state.LifePoints.ToString();

        private void Die_Click(object sender, RoutedEventArgs e) => ResultText.Text = $"Rolled a {_state.RollDie()}.";
        private void Coin_Click(object sender, RoutedEventArgs e) => ResultText.Text = $"Coin: {(_state.FlipCoin() ? "Heads" : "Tails")}.";
    }
}
