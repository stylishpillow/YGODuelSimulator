# YGO Duel Simulator

A Windows desktop **deck builder and manual duel simulator** for the Yu-Gi-Oh! Trading
Card Game. Build decks from the full card pool and play two-player manual duels — either
hot-seat on one PC or online against a friend.

> **Fan project.** Not affiliated with, endorsed by, or sponsored by Konami. *Yu-Gi-Oh!*
> and all related names, logos, and card content are trademarks of their respective
> owners. This is a free, non-commercial tool for personal use.

## Features

- **Deck Constructor** — build, save, and load decks in the standard `.ydk` format
  (compatible with YGOPro / EDOPro / DuelingBook).
- **Card Database** — browse and search the full card pool with images.
- **Duel Room** — a manual two-player playmat: hand, deck, Extra Deck, Graveyard,
  Banished pile, monster/spell/trap/field zones, tokens, counters, life points, dice,
  and coin flips. It's a *manual* simulator — a turn/phase tracker guides play, but no
  rules are enforced, so you're free to play any interaction.
- **Online & LAN play** — duel a friend over the internet or your local network.
- **Automatic updates** — the app keeps itself current with each new release.

## Installation

Download the latest **`YGODuelSimulator-win-Setup.exe`** from the
[Releases](https://github.com/stylishpillow/YGODuelSimulator/releases) page and run it.
A no-install **Portable** ZIP is also available.

The app installs per-user (no administrator rights required) and updates itself
automatically when a new version is published. Because the app isn't code-signed yet,
Windows SmartScreen may warn you on first run — choose **More info → Run anyway**.

## Card data

Card information and images come from the [YGOPRODeck API](https://ygoprodeck.com/api-guide/).
In line with their usage guidelines, the full card database ships **with the app** as a
compressed snapshot rather than being downloaded by every user, and card images are
fetched once and cached locally the first time you view them.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
# Build and run
dotnet build YGODuelSimulator.csproj
dotnet run --project YGODuelSimulator.csproj
```

The app is built with WPF on .NET 10, using [WPF-UI](https://github.com/lepoco/wpfui)
for the Fluent interface and EF Core + SQLite for the local card database.

## Releases

Releases are produced automatically by GitHub Actions when a version tag is pushed, and
packaged with [Velopack](https://velopack.io) to produce the installer and the
auto-update feed:

```
git tag vX.Y.Z
git push origin vX.Y.Z
```

## Contributing

Issues and pull requests are welcome. Please keep changes focused and match the existing
code style.
