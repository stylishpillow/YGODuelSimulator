using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace YGODuelSimulator.Services
{
    /// <summary>
    /// Client side of the auto-updater. The release CI publishes a Velopack update feed
    /// (a <c>-full.nupkg</c> + <c>RELEASES</c> manifest) to GitHub Releases on every
    /// version tag; this checks that feed and applies a newer version in place.
    ///
    /// Only meaningful for a real Velopack install — a <c>dotnet run</c> dev build reports
    /// <see cref="IsInstalled"/> false and is left alone.
    /// </summary>
    public static class UpdateService
    {
        // Public repo, so no access token is needed. If the repo ever goes private this
        // must carry a token (and the feed would have to be reachable by the client).
        private const string RepoUrl = "https://github.com/stylishpillow/YGODuelSimulator";

        private static UpdateManager NewManager() => new(new GithubSource(RepoUrl, null, prerelease: false));

        /// <summary>True only for a Velopack-installed build (not a dev <c>dotnet run</c>).</summary>
        public static bool IsInstalled
        {
            get { try { return NewManager().IsInstalled; } catch { return false; } }
        }

        /// <summary>The running version, for display. Uses the Velopack-installed version
        /// when installed, otherwise the assembly version (dev builds).</summary>
        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var mgr = NewManager();
                    if (mgr.IsInstalled && mgr.CurrentVersion is { } v) return v.ToString();
                }
                catch { /* fall through to the assembly version */ }

                var asm = Assembly.GetExecutingAssembly().GetName().Version;
                return asm is null ? "0.0.0" : $"{asm.Major}.{asm.Minor}.{asm.Build}";
            }
        }

        /// <summary>Checks the release feed for a newer version. Returns the manager and the
        /// pending update when one exists, or null when up to date, not installed, or the
        /// feed can't be reached (offline users are never blocked from playing).</summary>
        public static async Task<(UpdateManager mgr, UpdateInfo info)?> CheckAsync()
        {
            try
            {
                var mgr = NewManager();
                if (!mgr.IsInstalled) return null;
                var info = await mgr.CheckForUpdatesAsync();
                return info is null ? null : (mgr, info);
            }
            catch
            {
                return null; // offline / feed unreachable — let the app run
            }
        }
    }
}
