using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YGODuelSimulator.Models.Duel;
using YGODuelSimulator.Services;

namespace YGODuelSimulator.Views.Pages
{
    /// <summary>
    /// Local manual duel playfield, driven the Dueling Book way: click a card to
    /// select it and open an action menu, then (for placement actions) click the
    /// highlighted destination zone. Nothing is rule-enforced.
    /// </summary>
    public partial class DuelRoomPage : Page
    {
        private readonly DuelState _state = new();

        // A placement armed from the action menu: the face/position to apply and
        // which zone kinds are valid destinations. Null when nothing is pending.
        private (bool faceDown, bool defense, ZoneKind[] kinds)? _pending;

        public DuelRoomPage()
        {
            InitializeComponent();
            DataContext = _state;
            DeckPicker.ItemsSource = YdkFile.ListDeckNames();
            UpdateLp();
        }

        // --- Selection & action menu ---

        // Left-click just selects / points at a card (the game is two-player, so a
        // click is "I'm looking at this one") and shows it in the inspector.
        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not BoardCard card) return;

            _state.Selected = card;
            Preview.Show(card.Card);
            e.Handled = true;
        }

        // Right-click selects the card and opens the action menu at the cursor.
        private void Card_RightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not BoardCard card) return;

            _state.Selected = card;
            Preview.Show(card.Card);
            CardActions.IsOpen = true;
            e.Handled = true;
        }

        private void Zone_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ZoneSlot slot) return;

            // Only meaningful while placing a card into a matching, empty zone.
            if (_pending is { } p && slot.IsEmpty && p.kinds.Contains(slot.Kind) &&
                _state.Selected is { } card)
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
            // A click that reached the playmat background (not a card or zone)
            // cancels the current selection / pending placement.
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
            if (_state.Selected is null) return;
            _pending = (faceDown, defense, kinds);
            _state.HighlightTargets(kinds);
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

        // --- Action menu: move to a pile (act now) ---

        private void SelToHand_Click(object sender, RoutedEventArgs e) => MoveSelected(_state.Hand);
        private void SelToGrave_Click(object sender, RoutedEventArgs e) => MoveSelected(_state.Graveyard);
        private void SelToBanish_Click(object sender, RoutedEventArgs e) => MoveSelected(_state.Banished);
        private void SelToDeckTop_Click(object sender, RoutedEventArgs e) => MoveSelected(_state.Deck, toTop: true);
        private void SelToDeckBottom_Click(object sender, RoutedEventArgs e) => MoveSelected(_state.Deck);
        private void SelToExtra_Click(object sender, RoutedEventArgs e) => MoveSelected(_state.ExtraDeck);

        private void MoveSelected(System.Collections.ObjectModel.ObservableCollection<BoardCard> pile, bool toTop = false)
        {
            if (_state.Selected is not { } card) return;
            ResetPosition(card);
            _state.MoveToPile(card, pile, toTop);
            EndPlacement();
            Deselect();
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
            e.Handled = true;
        }

        private static void ResetPosition(BoardCard card)
        {
            card.FaceDown = false;
            card.Defense = false;
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
