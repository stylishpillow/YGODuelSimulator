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
