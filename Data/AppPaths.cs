using System.IO;
using System.Runtime.CompilerServices;

namespace YGODuelSimulator.Data;

/// <summary>
/// Resolves the base directory used for local storage (the SQLite database and
/// the cached card images). During development this is the project folder, so
/// everything sits next to the code; a deployed build falls back to the app's
/// output directory.
/// </summary>
public static class AppPaths
{
    public static string BaseDirectory => GetProjectDirectory() ?? AppContext.BaseDirectory;

    /// <summary>
    /// Resolves the project root from this source file's compile-time path.
    /// This file lives at &lt;project&gt;/Data/AppPaths.cs, so the project root
    /// is two directory levels up.
    /// </summary>
    private static string? GetProjectDirectory([CallerFilePath] string sourceFilePath = "")
    {
        var dataDir = Path.GetDirectoryName(sourceFilePath);   // <project>/Data
        var projectDir = Path.GetDirectoryName(dataDir);       // <project>
        return projectDir is not null && Directory.Exists(projectDir) ? projectDir : null;
    }
}
