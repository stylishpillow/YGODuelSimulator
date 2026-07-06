using System.IO;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;

namespace YGODuelSimulator.Data;

/// <summary>
/// Ships a prebuilt card database with the app so first-time users get every
/// card without ever calling the YGOPRODeck API. A gzip-compressed snapshot
/// (<c>cards.db.gz</c>) is committed to the repo and copied next to the build; on
/// first launch, when no local database exists yet, it is decompressed into
/// place. This keeps us squarely within the API rules: the ~13k-card database is
/// downloaded once by the maintainer, not re-pulled by every user.
///
/// Maintainer workflow to refresh the snapshot: Import / Refresh Cards, then
/// Export DB Seed, then commit the updated <c>cards.db.gz</c>.
/// </summary>
public static class DatabaseSeeder
{
    private const string SeedFileName = "cards.db.gz";

    public static string GetSeedPath() => Path.Combine(AppPaths.BaseDirectory, SeedFileName);

    /// <summary>
    /// If there is no database yet but a bundled seed exists, decompress the seed
    /// into the database location. Never overwrites an existing database.
    /// Returns true if a seed was applied.
    /// </summary>
    public static bool EnsureSeeded()
    {
        var dbPath = DatabaseConfig.GetDatabasePath();
        if (File.Exists(dbPath)) return false;

        var seedPath = GetSeedPath();
        if (!File.Exists(seedPath)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // Decompress to a temp file first, then move into place so an interrupted
        // launch never leaves a half-written database behind.
        var tmp = dbPath + ".seed.tmp";
        try
        {
            using (var source = File.OpenRead(seedPath))
            using (var gzip = new GZipStream(source, CompressionMode.Decompress))
            using (var dest = File.Create(tmp))
            {
                gzip.CopyTo(dest);
            }
            File.Move(tmp, dbPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
        return true;
    }

    /// <summary>
    /// Writes a compressed snapshot of the current database to
    /// <see cref="GetSeedPath"/>, ready to be committed. Uses SQLite's
    /// <c>VACUUM INTO</c> to first produce a compacted, fully self-contained copy
    /// (folding in any WAL pages), so the snapshot is both consistent and as small
    /// as possible, then gzips it at maximum compression.
    /// </summary>
    public static async Task ExportSeedAsync(CancellationToken cancellationToken = default)
    {
        var dbPath = DatabaseConfig.GetDatabasePath();
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("There is no database to export yet. Import cards first.", dbPath);

        var seedPath = GetSeedPath();
        var vacuumTmp = seedPath + ".vacuum.tmp";
        var gzTmp = seedPath + ".tmp";
        if (File.Exists(vacuumTmp)) File.Delete(vacuumTmp);

        try
        {
            await using (var db = new AppDbContext())
            {
                // VACUUM INTO takes a string literal, not a bound parameter. The path
                // is app-internal (derived from AppPaths, not user input) and single
                // quotes are escaped, so the interpolation warning doesn't apply here.
                var escaped = vacuumTmp.Replace("'", "''");
#pragma warning disable EF1002
                await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escaped}'", cancellationToken);
#pragma warning restore EF1002
            }

            using (var source = File.OpenRead(vacuumTmp))
            using (var dest = File.Create(gzTmp))
            using (var gzip = new GZipStream(dest, CompressionLevel.SmallestSize))
            {
                await source.CopyToAsync(gzip, cancellationToken);
            }
            File.Move(gzTmp, seedPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(vacuumTmp)) File.Delete(vacuumTmp);
            if (File.Exists(gzTmp)) File.Delete(gzTmp);
        }
    }
}
