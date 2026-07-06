using Velopack;

namespace YGODuelSimulator
{
    /// <summary>
    /// Explicit application entry point. Velopack must run first: when the
    /// installer or an update invokes the exe with hook arguments, this processes
    /// them (creating shortcuts, applying updates, etc.) and exits before any WPF
    /// UI would load. On a normal launch it returns immediately and the app starts.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
