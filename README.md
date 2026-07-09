# YGODuelSimulator

## Card database

Card data comes from the [YGOPRODeck API](https://ygoprodeck.com/api-guide/). To
respect that API's rules (20 requests/second, and "download once, don't make
every user re-pull"), the full card database ships **with the app** as a
compressed seed instead of being downloaded by each user:

- `cards.db.gz` — a gzip-compressed snapshot of the card database, committed to
  the repo. On first launch, if no local `cards.db` exists yet, the app
  decompresses this seed into place. Users get every card without calling the
  API.
- `cards.db` (and its `-wal`/`-shm` siblings) — the live local database. Git
  **ignores** it; it's regenerated from the seed or via Import.
- `images/` — card images, downloaded lazily and cached on disk (git-ignored).
  Each image is pulled at most once per machine, with throttling, as the API
  requires. Images are **not** bundled; only the metadata database is.

### Refreshing the bundled seed (maintainer)

When a new set comes out and you want to update the card data everyone gets:

1. Click **Import / Refresh Cards** to pull the latest database from YGOPRODeck.
2. Click **Export DB Seed** — this compacts the database (`VACUUM INTO`) and
   writes `cards.db.gz`.
3. Commit the updated `cards.db.gz`.

Because a gzip blob can't be delta-compressed by Git, each refresh adds roughly
the seed's compressed size to history. That's fine for infrequent set updates; if
the seed ever grows large or you update often, move it to a GitHub Release asset
or Git LFS instead.

## Releasing (installer + auto-update)

The app is distributed as a downloadable Windows installer via
[Velopack](https://velopack.io). Publishing is automated: pushing a version tag
triggers `.github/workflows/release.yml`, which builds a self-contained
(no .NET runtime required) 64-bit build and creates a GitHub Release with:

- `YGODuelSimulator-win-Setup.exe` — the installer users download and run.
- a `-full.nupkg` update feed so installed apps can auto-update from Releases.
- `YGODuelSimulator-win-Portable.zip` — a no-install portable build.

**In-app updates are mandatory.** After sign-in, `UpdateService.CheckAsync`
(`Services/UpdateService.cs`) checks the GitHub Releases feed; if a newer version
exists, a blocking `UpdateWindow` requires the user to install it (which downloads
and relaunches the app) before the main shell opens — so everyone converges on the
latest release, which matters for the networked-duel `ProtocolVersion`. The check
is skipped for dev (`dotnet run`) builds and when the feed is unreachable, so
offline users aren't locked out. Settings also shows the current version and a
manual "Check for updates". Tags must stay in the `vMAJOR.MINOR.PATCH` shape.

To cut a release, bump `<Version>` in `YGODuelSimulator.csproj` if you like, then:

```
git tag v0.1.0
git push origin v0.1.0
```

To build the installer locally instead (output lands in `releases/`, git-ignored):

```
dotnet publish YGODuelSimulator.csproj -c Release /p:PublishProfile=win-x64
dotnet tool install -g vpk --version 1.2.0
vpk pack --packId YGODuelSimulator --packTitle "YGO Duel Simulator" \
  --packVersion 0.1.0 --packDir bin/Release/publish/win-x64 \
  --mainExe YGODuelSimulator.exe --outputDir releases
```

**Code signing:** builds are currently unsigned, so Windows SmartScreen shows an
"unknown publisher" prompt on first run (users click *More info → Run anyway*).
Add a signing certificate later to remove it.

**Note on installed data:** Velopack installs per-user under `%LocalAppData%`,
which is writable, so the app's `cards.db`/`images/` (written next to the exe via
`AppPaths`) work there. If the app is ever installed to a read-only location,
that writable data should move to a dedicated per-user folder — a future
hardening step.
