using System.IO;
using YGODuelSimulator.Models;

namespace YGODuelSimulator.Services;

/// <summary>
/// Reads and writes decks as <c>.ydk</c> files (the YGOPro/EDOPro/DuelingBook
/// format) in a stable per-user folder, and lists/deletes saved decks. A
/// <c>.ydk</c> file has a <c>#main</c> and <c>#extra</c> section plus a
/// <c>!side</c> section, with one card passcode per line, repeated once per copy.
/// The folder lives under the user's Documents so decks survive app updates and
/// are easy to back up or share.
/// </summary>
public static class YdkFile
{
    public static string DecksDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "YGO Duel Simulator", "Decks");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string PathFor(string name) => Path.Combine(DecksDirectory, name + ".ydk");

    public static IReadOnlyList<string> ListDeckNames() =>
        Directory.EnumerateFiles(DecksDirectory, "*.ydk")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;

    public static bool Exists(string name) => File.Exists(PathFor(name));

    public static Deck Load(string name) => ImportFrom(PathFor(name), name);

    public static void Save(Deck deck) => ExportTo(deck, PathFor(deck.Name));

    public static void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Parses a .ydk file at an arbitrary path into a <see cref="Deck"/>.</summary>
    public static Deck ImportFrom(string path, string? name = null)
    {
        var deck = new Deck { Name = name ?? Path.GetFileNameWithoutExtension(path) };
        var zone = DeckZone.Main;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#main", StringComparison.OrdinalIgnoreCase)) { zone = DeckZone.Main; continue; }
            if (line.StartsWith("#extra", StringComparison.OrdinalIgnoreCase)) { zone = DeckZone.Extra; continue; }
            if (line.StartsWith("!side", StringComparison.OrdinalIgnoreCase)) { zone = DeckZone.Side; continue; }
            if (line[0] is '#' or '!') continue; // comment / unknown directive
            if (long.TryParse(line, out var id)) deck[zone].Add(id);
        }
        return deck;
    }

    /// <summary>Writes a <see cref="Deck"/> as a .ydk file at an arbitrary path.</summary>
    public static void ExportTo(Deck deck, string path)
    {
        using var w = new StreamWriter(path, append: false);
        w.WriteLine("#created by YGO Duel Simulator");
        w.WriteLine("#main");
        foreach (var id in deck.Main) w.WriteLine(id);
        w.WriteLine("#extra");
        foreach (var id in deck.Extra) w.WriteLine(id);
        w.WriteLine("!side");
        foreach (var id in deck.Side) w.WriteLine(id);
    }
}
