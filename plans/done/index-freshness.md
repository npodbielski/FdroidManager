# Plan: index freshness — rebuild after metadata / app / icon edits

> **Status: ✅ done** — merged as MR `!16` (`feat/index-freshness`). Metadata,
> app and icon edits rebuild the index best-effort; `IndexFreshnessApiTests`
> pins the endpoints, and the badge / error dialog / rebuild button were driven
> in a browser, including the refetch after a metadata write.

## Problem

The "✓ index up to date · rebuilt X ago" badge (left of *View Repo*, rendered by
`loadIndexHealth` in `wwwroot/js/ui.js` from `GET /api/repo/status`) shows the
last successful `fdroid update` (`RepoConfig.LastIndexBuiltUtc`). That timestamp
is only written by:

- the startup build (`Program.cs`, `RepoIndexService.GenerateAsync`),
- **release publish** (`AppsController`, `GenerateStrictAsync`),
- the manual "Rebuild index" button,
- repo initialization.

But several content-mutating endpoints **never rebuild the index**:

| Endpoint | Controller method | Stale index content |
|---|---|---|
| `POST /api/apps/{pkg}/metadata` | `WriteMetadata` (AppsController.cs:364) | description |
| `PATCH /api/apps/{pkg}` | `Update` (AppsController.cs:279) | name, summary, … |
| `POST /api/apps/{pkg}/icon` | `UploadIcon` (AppsController.cs:173) | icon URL |

So after editing a description in the UI, clients keep seeing the old one while
the badge still says "✓ up to date". A live report: the badge sat at
"2 h ago" (last publish/startup) while edits since then were invisible to it.

## Design

1. Trigger a **best-effort index rebuild** after `WriteMetadata`, `Update`, and
   `UploadIcon` — the same `RepoIndexService.GenerateAsync` (it records the
   outcome on the `RepoConfig` row). Best-effort, not strict: the edit itself is
   persisted; a rebuild failure surfaces via the badge flipping to the existing
   ⚠ + *details* + *Rebuild index* UI instead of a 500-after-write.
   (Publish keeps `GenerateStrictAsync` — a failed publish *is* a failed
   publish.)
2. Keep the "rebuilt X ago" wording — with #1 it now approximates content
   freshness.
   *Optional extension (decide at implementation):* explicit staleness check
   (index file mtime vs. newest metadata/DB change → "content may be stale"
   hint).

## Changes

- `src/FdroidMgr.Api/Controllers/AppsController.cs` — three call sites, small
  shared helper (fetch `RepoConfig`, call `GenerateAsync`, swallow + rely on the
  recorded outcome).
- No schema change. No frontend change.

## Tests (integration)

- `WriteMetadata` → `GET /api/repo/status` shows a fresh `lastIndexBuiltUtc`
  **and** `index-v1.json` on disk contains the new description.
- `Update` → `index-v1.json` contains the new name.
- `UploadIcon` → `index-v1.json` contains the new icon URL.
- Forced rebuild failure (e.g. read-only repo root) → the edit still succeeds
  and the status shows `lastIndexError`.

## Risk

The best-effort contract must not mask a persistently broken rebuild — the ⚠
badge is the visible backstop (`RecordOutcomeAsync` is already try/catch and
never changes the caller's success contract).
