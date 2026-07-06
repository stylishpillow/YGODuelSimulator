using System.IO;

namespace YGODuelSimulator.Data;

/// <summary>Centralizes where the SQLite database file lives so runtime and
/// design-time (EF CLI) use the exact same location.</summary>
public static class DatabaseConfig
{
    public static string GetDatabasePath() => Path.Combine(AppPaths.BaseDirectory, "cards.db");

    public static string GetConnectionString() => $"Data Source={GetDatabasePath()}";
}
