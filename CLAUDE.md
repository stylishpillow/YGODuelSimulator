# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows desktop Yu-Gi-Oh! deck builder and manual duel simulator. WPF on
**.NET 10** (`net10.0-windows`), C# with nullable + implicit usings enabled, using
[WPF-UI](https://github.com/lepoco/wpfui) for the Fluent shell and EF Core + SQLite
for the local card database. There is no test project.

## Commands

```powershell
# Build
dotnet build YGODuelSimulator.csproj

# Run
dotnet run --project YGODuelSimulator.csproj

# EF Core migrations (design-time context comes from AppDbContextFactory)
dotnet ef migrations add <Name>      # new migration -> Data/Migrations/
dotnet ef database update            # apply to the local cards.db

# Self-contained publish (feeds the installer; see README for the vpk step)
dotnet publish YGODuelSimulator.csproj -c Release /p:PublishProfile=win-x64
```

Migrations are also applied automatically at runtime on `MainWindow_Loaded`, so a
normal launch keeps the schema current; you only run `database update` manually
when working outside the app.

## Architecture

**Entry point is custom, not WPF's.** `Program.Main` (set via `<StartupObject>`)
runs `VelopackApp.Build().Run()` *before* any WPF UI so installer/update hooks are
handled first. Because of this, `App.xaml` is demoted from `ApplicationDefinition`
to a compiled `Page` in the `.csproj` (otherwise WPF would generate a second
`Main`). Don't reintroduce an `ApplicationDefinition` or a generated entry point.

**Where local data lives is resolved by `AppPaths.BaseDirectory`.** In a dev build
it returns the *project source folder* (derived from `[CallerFilePath]` of
`Data/AppPaths.cs`), so `cards.db`, `images/`, and `cards.db.gz` all sit in the
repo root while developing. A deployed build falls back to the app's output
directory. Everything that touches disk (database, image cache, seed) goes through
`AppPaths`, `DatabaseConfig`, and `DatabaseSeeder` — use them rather than
hardcoding paths.

**Card database is seeded, not downloaded per user.** `cards.db.gz` (a
gzip snapshot committed to the repo) is unpacked into `cards.db` by
`DatabaseSeeder.EnsureSeeded()` on first launch when no database exists. This
honors the YGOPRODeck API rule of "download the whole DB once, cache locally." The
maintainer refresh loop is: **Import / Refresh Cards** (`CardImportService`,
full-replace bulk import) → **Export DB Seed** (`DatabaseSeeder.ExportSeedAsync`,
`VACUUM INTO` + gzip) → commit the updated `cards.db.gz`. See README for release
details.

**EF model.** `AppDbContext` owns `Card` and its cascade-delete children
(`CardImage`, `CardSet`, `CardPrice`, `CardLinkMarker`, `CardFormat`). `Card.Id` is
the real YGOPRODeck **passcode**, not a generated key (`ValueGeneratedNever`) —
imports and deck files rely on this. API JSON is shaped by the DTOs in
`Api/CardApiDtos.cs` and mapped to entities in `CardImportService.MapCard`.

**No dependency injection.** Pages have parameterless constructors and are created
on demand by `SimplePageProvider` (via `Activator.CreateInstance`) for WPF-UI's
`NavigationView`. Services (`CardImageService`, `CardImportService`, `DuelState`,
etc.) are `new`'d directly where needed.

**UI layers.** `MainWindow` is the Fluent shell hosting the nav rail; screens are
`Page`s under `Views/Pages/` (Home, CardDatabase, DeckConstructor, DuelRoom,
Profile, Settings). Reusable pieces live in `Views/Controls/` (`CardBrowser`,
`CardPreview`, `CardThumbnail`, `DeckSlot`).

**Images.** `CardImageService` lazily downloads each card image at most once and
caches it under `images/<size>/<apiImageId>.jpg`, with a concurrency cap and
minimum spacing between fetches to stay within API limits. `ImageLoading` turns
cached files into frozen `BitmapImage`s (safe across threads). Never hotlink the
image server; always go through this cache.

**Decks** are plain `.ydk` files (YGOPro/EDOPro/DuelingBook format: `#main` /
`#extra` / `!side`, one passcode per line per copy), read/written by `YdkFile`
under `Documents/YGO Duel Simulator/Decks`. `Deck` is a simple model of passcode
lists, **not** an EF entity.

**Duel room** (`Views/Pages/DuelRoomPage` + `Services/DuelState` +
`Models/Duel/`) is a *manual, two-player* simulator — no rules are enforced; the
turn/phase tracker is a guide, not a referee. Structure:
- `PlayerBoard` is one player's half: the piles (Hand/Deck/Extra/GY/Banished), the
  fixed `ZoneSlot`s (main/extra monster, spell-trap, field), LP, and per-board
  `LoadDeckAsync`/`Draw`/`Shuffle`. Each `ZoneSlot` knows its `Owner` board.
- `DuelState` coordinates two `PlayerBoard`s (`Player`, `Opponent`) plus the shared
  `Selected` card, turn/phase state (`TurnNumber`/`ActiveSide`/`Phase`), and the
  move/highlight operations that can cross between boards (`RemoveFromCurrent` and
  `FindBoard` scan both). Placement highlights the *owning* board's zones.
- Interaction is Dueling-Book style, two menus at the cursor: **left-click** opens
  the `ViewMenu` popup (table-talk: inspect / declare effect / point — the last two
  set `DuelState.Announcement` + `BoardCard.IsPointed`, auto-cleared by a
  `DispatcherTimer`); **right-click** opens the `CardActions` popup (play actions).
  Placement actions arm `_pending`, close the pile viewer, and highlight valid empty
  zones (`ZoneSlot.IsTarget`); a left-click on a highlighted zone drops the card.
  Tokens (`BoardCard.IsToken`) and per-card `Counters` are manual helpers.
- The page is hosted in the nav `ScrollViewer` (unbounded height), so `Page_Loaded`
  binds `RootGrid.MaxHeight` to that ScrollViewer's `ViewportHeight` — otherwise the
  field `Viewbox` renders at full natural size and overflows the top.

`BoardCard`/`ZoneSlot` expose `Visibility`/label helpers for the XAML templates;
because an empty zone's `ContentControl` still realizes the card template against a
null data context, visibility bindings in the board card template need
`FallbackValue=Collapsed` to keep empty slots blank.
