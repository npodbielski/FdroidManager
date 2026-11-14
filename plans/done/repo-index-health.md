# Repo Index Build Health — Plan

> Make the health of the repo index visible to the operator. Today a failed
> `fdroid update` is only a 502 to whichever caller triggered it (or a log line
> when it runs at startup/background); nothing tells the operator that the
> served index is stale or that the last build failed. A repo that silently
> stops regenerating its signed index is the worst kind of failure, so this is
> operational safety, not a nice-to-have.

---

## 1. Goal

Track the last index build outcome and surface it in the API + Web UI, so the
operator can answer "is the repo index currently healthy?" at a glance:

- `GET /api/repo/status` gains index-health fields.
- The Web UI shows a status banner (index OK / stale / failed) with a one-click
  rebuild.

---

## 2. Current state (verified)

- `RepoIndexService` (constructor `(IRepoCommandRunner, ILogger)`) calls
  `fdroid update --rename-apks` in the repo root.
  - `GenerateAsync` (non-strict): logs success; logs a warning on failure and
    returns normally. Used at app startup (`Program.cs`) and by
    `RepoController` (config update + `/index/rebuild`).
  - `GenerateStrictAsync`: throws `FdroidUpdateException` on failure. Used by
    `AppsController` + `ReleasesController`.
- `GET /api/repo/status` returns only `{ repoName, appCount, updatedUtc }`
  (`appCount` = apps currently in the index).
- `RepoConfig` has no fields tracking index health.

---

## 3. Design

### 3.1 Persistence

Add two columns to `RepoConfig` (via the existing `EnsureColumnAsync` migration):

| Field | Type | Meaning |
|---|---|---|
| `LastIndexBuiltUtc` | `TEXT NULL` | timestamp of the last *successful* `fdroid update` |
| `LastIndexError` | `TEXT NULL` | last failure message (trimmed), cleared on success |

Both live on the single `RepoConfig` row (id = 1), matching the existing
`RepoKeystorePassEnc` / `RepoKeySha256` / `BackupPath` pattern.

### 3.2 Where success/failure is recorded

`RepoIndexService` re-gains a `FdroidDb` dependency (constructor becomes
`(FdroidDb db, IRepoCommandRunner runner, ILogger log)`) so *every* update
records its outcome in one place — startup, rebuild, publish, delete:

- `GenerateAsync`: after a successful run → set `LastIndexBuiltUtc = now`,
  `LastIndexError = null`; on failure → set `LastIndexError` (trimmed stderr)
  and leave `LastIndexBuiltUtc` unchanged.
- `GenerateStrictAsync`: same, then throw (callers keep their 502 behavior).

This means an upload that fails mid-way still leaves a visible `LastIndexError`
rather than disappearing.

### 3.3 API

```
GET /api/repo/status   (AllowAnonymous)
  200 {
    repoName,
    appCount,
    lastIndexBuiltUtc: string|null,   // ISO-8601
    lastIndexError: string|null,
    indexHealthy: bool                // lastIndexBuiltUtc != null && lastIndexError == null
  }
```

No new route; the existing public status endpoint carries the health.

### 3.4 UI (`wwwroot/js/ui.js` + `ui.html`)

- A status line in the workspace header (near the "View Repo" link) fetched from
  `GET /api/repo/status` on load and after every rebuild/publish:
  - `✓ index up to date` when `indexHealthy` (with "rebuilt Xm ago" if desired)
  - `⚠ index build failed: <lastIndexError>` + a **"Rebuild index"** button →
    `POST /api/repo/index/rebuild` → re-fetch status.
- The banner is cheap to render and safe to poll on the existing card ops.

---

## 4. Tests

- **Unit (`RepoIndexServiceGenerateTests`)**: update ctor calls; add tests:
  - success → `db.RepoConfig.LastIndexBuiltUtc` set, `LastIndexError` null;
  - failure (`SimulateUpdateFailure`) → `LastIndexError` set, build time unchanged
    (and `GenerateStrictAsync` still throws `FdroidUpdateException`).
- **API (`ApiIntegrationTests2` / `RepoController`)**: `GET /api/repo/status`
  includes the new fields; after `POST /api/repo/index/rebuild` with a failing
  fake runner → `indexHealthy == false` + non-empty `lastIndexError`.

---

## 5. Migration

`Program.cs` startup: `EnsureColumnAsync("RepoConfig", "LastIndexBuiltUtc", "TEXT NULL")`
and `EnsureColumnAsync("RepoConfig", "LastIndexError", "TEXT NULL")`. Fresh DBs
already get the columns from the model; existing DBs get them added idempotently.

---

## 6. Implementation checklist

- [x] `RepoConfig`: add `LastIndexBuiltUtc` / `LastIndexError`.
- [x] `RepoIndexService`: inject `FdroidDb`, record outcome in both variants.
- [x] `Program.cs`: `EnsureColumnAsync` for the two columns.
- [x] `RepoController.Status`: expose `lastIndexBuiltUtc` / `lastIndexError` / `indexHealthy`.
- [x] UI status banner + rebuild button.
- [x] Tests (unit + API).

*Status: COMPLETED — repo index build health surfaced via status endpoint + UI banner.*
