using System.IO;
using System.Runtime.CompilerServices;

namespace YGODuelSimulator.Data;

/// <summary>Centralizes where the SQLite database file lives so runtime and
/// design-time (EF CLI) use the exact same location.</summary>
public static class DatabaseConfig
{
    public static string GetDatabasePath()
    {
        // During development the DB lives in the project folder next to the code.
        // If that folder isn't present (e.g. a deployed build on another machine),
        // fall back to the app's output directory.
        var dir = GetProjectDirectory() ?? AppContext.BaseDirectory;
        return Path.Combine(dir, "cards.db");
    }

    public static string GetConnectionString() => $"Data Source={GetDatabasePath()}";

    /// <summary>
    /// Resolves the project root from this source file's compile-time path.
    /// This file lives at &lt;project&gt;/Data/DatabaseConfig.cs, so the project
    /// root is two directory levels up.
    /// </summary>
    private static string? GetProjectDirectory([CallerFilePath] string sourceFilePath = "")
    {
        var dataDir = Path.GetDirectoryName(sourceFilePath);       // <project>/Data
        var projectDir = Path.GetDirectoryName(dataDir);           // <project>
        return projectDir is not null && Directory.Exists(projectDir) ? projectDir : null;
    }
}
