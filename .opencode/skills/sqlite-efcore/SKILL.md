---
name: sqlite-efcore
description: Schema design, migrations, indexes and FTS5 for PlaySphere's SQLite database.
---

# SQLite + EF Core

## Rules
- All persistence lives in `PlaySphere.Infrastructure.Library` / EF Core; UI and modules depend only on contracts in `PlaySphere.Application.Library` (`IMediaLibraryRepository`, etc.).
- The DB lives at `%LOCALAPPDATA%\LiVi PlaySphere Studio\library.db` (unpackaged per ADR-0001 → `Environment.SpecialFolder.LocalApplicationData`, NOT `Windows.Storage.ApplicationData`).
- EF Core + SQLite packages are pinned at 10.0.11 (match `Microsoft.Extensions.*` 10.0.11).
- Always `Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()` before deleting a temp test DB, or cleanup races the pooled native connection (IOException "file in use").
- SQLite provider CANNOT translate `ORDER BY` over `DateTimeOffset` columns (throws at query time). Use `DateTime` (UTC) for all timestamps.
- Every schema change needs a versioned EF migration (design-time factory) + applies at startup via `MigrateLibraryDatabase()`.

## Schema (§42)
Tables: `media_items`, `playback_history`, `bookmarks`, `chapters`, `playlists`, `playlist_items`, `library_folders`, `transcripts`, `transcript_segments`, `ai_artifacts`, `conversion_jobs`.

## Migrations
- Generate via the design-time factory; migration classes must be self-contained (FKs, indexes).
- Confirm migrations apply on top of the existing on-disk DB (smoke-launch runs `MigrateLibraryDatabase`) — don't assume a fresh DB.
- FKs cascade to both `media_items` and (for items) `playlists`.

## FTS5
- Use FTS5 for full-text search; do not scan rows with `LIKE '%..%'` at scale.
