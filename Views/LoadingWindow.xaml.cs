using System.Windows;
using System.Windows.Threading;

namespace YGODuelSimulator.Views
{
    /// <summary>A small splash shown during the (UI-thread-bound) construction of the
    /// main shell after sign-in. It runs on its own dispatcher thread — see
    /// <see cref="ShowOnBackgroundThread"/> — so its progress bar and rotating
    /// Yu-Gi-Oh! quips keep animating while the main thread is busy building the
    /// window and rendering its first frame.</summary>
    public partial class LoadingWindow : Window
    {
        // Light, on-brand table-talk shown one at a time while the shell loads.
        private static readonly string[] Messages =
        [
            "It's time to d-d-d-duel!",
            "Shuffling the Deck of the Gods…",
            "Believing in the Heart of the Cards…",
            "Activating Pot of Greed… (what does it even do?)",
            "Sending Exodia's arms to the Graveyard…",
            "Reviving the Blue-Eyes White Dragon…",
            "Waiting out your opponent's 20-minute turn…",
            "Setting one card face-down and ending your turn…",
            "Drawing the exact card you needed…",
            "You activated my Trap Card!",
            "Paying 1000 Life Points as tribute…",
            "Summoning in Attack Mode against all advice…",
            "Rolling a die and praying for a six…",
            "Consulting Kaiba's briefcase…",
            "Negating your opponent's Trap… again…",
            "Polymerization! Fusing two monsters…",
            "Solving the Millennium Puzzle…",
        ];

        // Keep the splash up at least this long so it never just flashes on a warm start.
        private static readonly TimeSpan MinVisible = TimeSpan.FromMilliseconds(1200);

        private readonly Random _rng = new();
        private readonly DispatcherTimer _cycle;
        private readonly DateTime _shown = DateTime.UtcNow;
        private int _last = -1;

        public LoadingWindow()
        {
            InitializeComponent();

            MessageText.Text = NextMessage();
            _cycle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            _cycle.Tick += (_, _) => MessageText.Text = NextMessage();
            _cycle.Start();
        }

        private string NextMessage()
        {
            // Pick a different line than the one currently showing.
            int i = _rng.Next(Messages.Length);
            if (i == _last && Messages.Length > 1) i = (i + 1) % Messages.Length;
            _last = i;
            return Messages[i];
        }

        /// <summary>Spins up a dedicated STA dispatcher thread, shows the splash on it,
        /// and returns once it's visible. Dismiss it from any thread with
        /// <see cref="Dismiss"/>.</summary>
        public static LoadingWindow ShowOnBackgroundThread()
        {
            LoadingWindow? window = null;
            using var ready = new ManualResetEventSlim();

            var thread = new Thread(() =>
            {
                window = new LoadingWindow();
                window.Loaded += (_, _) => ready.Set();
                window.Show();
                Dispatcher.Run(); // pump this thread's message loop until it's shut down
            })
            {
                IsBackground = true,
                Name = "SplashScreen",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            ready.Wait();
            return window!;
        }

        /// <summary>Closes the splash (honouring a short minimum visible time) and stops
        /// its dispatcher thread. Safe to call from the main UI thread.</summary>
        public void Dismiss() => Dispatcher.Invoke(RequestClose);

        private void RequestClose()
        {
            var remaining = MinVisible - (DateTime.UtcNow - _shown);
            if (remaining > TimeSpan.Zero)
            {
                var wait = new DispatcherTimer { Interval = remaining };
                wait.Tick += (_, _) => { wait.Stop(); CloseNow(); };
                wait.Start();
            }
            else
            {
                CloseNow();
            }
        }

        private void CloseNow()
        {
            _cycle.Stop();
            Close();
            Dispatcher.InvokeShutdown();
        }
    }
}
