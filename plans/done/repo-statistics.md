# Repo download statistics (PLAN)

> **Status: ✅ done** — implemented with the header identity work
> (`DownloadStatsService` + `StatsController`, `#stats` graph in the UI).
>
> Goal: count downloads of the served repo (total, per app, per release) and
> show a daily-downloads graph for the last month in the UI.

## 1. What we're counting

A "download" = a served APK from `GET /fdroid/repo/**`. Index/icon/screenshot
hits are also visible to the server but are **not** APK installs, so by default
they are not counted as downloads (recorded separately or ignored — see §5).

| Metric | Definition |
|---|---|
| downloads, everything | sum of all APK downloads, all apps, all time |
| downloads per app | sum of APK downloads grouped by `packageName` |
| downloads per release | sum grouped by `packageName` + `versionCode` (apk file) |
| downloads per day (last 30d) | APK downloads grouped by calendar day |

## 2. Data model

New table `DownloadCount` (SQLite, `EnsureCreatedAsync` + `EnsureTableAsync` at
startup, mirroring the `ApiKey`/`ApiUser` migration pattern):

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK AUTOINCREMENT | |
| `DayUtc` | TEXT (date only) | `yyyy-MM-dd` day bucket |
| `PackageName` | TEXT NULL | app the APK belongs to; NULL for "other" if we choose to track it |
| `VersionCode` | INTEGER NULL | release; NULL for unknown/other |
| `Count` | INTEGER NOT NULL | running total for the bucket |

- Unique index on `(DayUtc, PackageName, VersionCode)`.
- Each download does one atomic upsert:
  `INSERT … ON CONFLICT(DayUtc,PackageName,VersionCode) DO UPDATE SET Count = Count + 1`
  (single writer SQLite; sufficient at this repo's traffic).

Alternative (only if we later need per-hour or per-country detail): a raw
`DownloadEvent(Id, Utc, PackageName, VersionCode, FileName, UserAgent)` event
log aggregated on read. **Defer** — the counter table is simpler and enough for
the requested metrics.

### 2.1 Mapping a served file → app/release

fdroidserver names APKs `{packageName}_{versionCode}.apk` (canonical, post
`--rename-apks`). Two options:

1. **Authoritative (preferred):** read `repo/index-v1.json`
   (`FdroidRepoService.ReadIndexAsync` already returns `Apps[].Versions[].ApkName`
   + `VersionCode` + `PackageName`) and build an in-memory
   `apkName → (package, versionCode)` map. Invalidate the cache whenever the
   index is regenerated (all the `ReindexAsync`/`Generate*` paths).
2. **Fallback regex** on the filename for files not in the index (e.g. an APK
   served before a fresh index is visible): strip `.apk`, split at the last
   `_n` where `n` is the trailing integer → versionCode; the rest = package.

## 3. Where the count happens

`RepoController.ServeRepo(string? file)` already resolves the file and serves it
via `PhysicalFile`. Make the action async:

- After `File.Exists` is confirmed (and the extension is `.apk`), resolve
  `apkName → (package, versionCode)` and call
  `await _stats.RecordAsync(package, versionCode, ct)` **before** returning the
  file (or after, but before is simpler for a no-buffer PhysicalFile flow).
- Keep it best-effort: log-and-continue if the DB write fails so a stats glitch
  never blocks a download.
- Non-`.apk` files (index.xml/index-v1.json/icons/screenshots) → not counted as
  downloads (optionally record an `Other` bucket in a later phase).

New service: `DownloadStatsService(FdroidDb)` with:
- `Task RecordAsync(string? package, int? versionCode, ct)` — upsert
  `(today, package, versionCode, +1)`.
- `Task<DownloadStatsDto> GetAsync(ct)` — totals + per-app + recent-per-release
  + last-30-days daily series (see §4).

## 4. API

Interactive-cookie-only (same trust level as repo config / API-key management —
statistics are operator-facing, not CI-facing):

- `GET /api/stats/downloads` — `[Authorize(Policy = "InteractiveOnly")]`

Response shape:

```json
{
  "totalDownloads": 12345,
  "apps": [
    { "packageName": "com.example.smart.gatecontrol", "name": "SmartHome",
      "downloads": 9001, "latestVersionCode": 2 }
  ],
  "releases": [
    { "packageName": "com.example.smart.gatecontrol", "versionCode": 2,
      "versionName": "1.4.0", "downloads": 5000 }
  ],
  "daily": [
    { "day": "2026-08-01", "downloads": 12 },
    { "day": "2026-08-02", "downloads": 0 }
  ]
}
```

- `daily` = last 30 calendar days **including zero days** (frontend renders a
  continuous axis without gaps).
- `apps` / `releases` sorted by downloads desc. `name`/`versionName` joined from
  the current index where available (best-effort; unknown → omit).
- Later, if desired, the same endpoint (or a sibling) can be opened to the
  `read` scope without a schema change — just swap the policy.

## 5. UI

New "Statistics" page/section, reachable from a **button on the Apps page**, not
from the avatar dropdown (it is repo activity, not user-profile content).

Entry point: a "📊 Statistics" button at the **top-right of the workspace**,
**above the "＋ Register new app" panel** — in the same header row as the
`<h2>Apps</h2>` heading (right-aligned), so it reads like a workspace toolbar
action next to the existing ↻ refresh control.

```html
<section id="workspace">
  <div class="workspace-toolbar">
    <h2>Apps <button id="refresh-btn">↻</button></h2>
    <button id="stats-btn" class="primary">📊 Statistics</button>
  </div>
  <details id="create-app-panel">+ Register new app …</details>
  <div id="app-list"></div>
</section>
```

The statistics page itself:

- **Total downloads** card (big number).
- **Per-app** table/cards (app name, package, downloads, mini share bar).
- **Per-release** table inside/under each app (versionName/versionCode +
  downloads) — or a "Releases" tab; keep it simple first: expandable per-app.
- **Last-30-days graph**: a bar chart. Prefer a hand-rolled inline **SVG bar
  chart** (no build step, no dependency) — 30 bars, y-scale from max; hover
  shows day + count. (Chart.js vendored only if richer charts are later wanted.)

Wire-up mirrors `showApiKeys()`: `#stats-btn` → `showStats()` (hides workspace,
shows the stats section, calls `GET /api/stats/downloads`, renders). A "← Back
to apps" button returns to the workspace.

## 6. Files that change

- `src/FdroidMgr.Api/Models/Dtos.cs` — `DownloadStatsDto` records.
- `src/FdroidMgr.Api/Data/FdroidDb.cs` — `DbSet<DownloadCount>` + model config +
  `EnsureTableAsync` DDL at startup.
- `src/FdroidMgr.Api/Services/DownloadStatsService.cs` *(new)* — record + aggregate.
- `src/FdroidMgr.Api/Controllers/RepoController.cs` — `ServeRepo` records hits.
- `src/FdroidMgr.Api/Controllers/StatsController.cs` *(new)* — `GET /api/stats/downloads`.
- `src/FdroidMgr.Api/Program.cs` — register service; `EnsureTableAsync` for the
  table + unique index.
- `wwwroot/ui.html` / `js/ui.js` / `css/ui.css` — stats page + SVG chart.
- Tests: `DownloadStatsServiceTests` (upsert + aggregation) and
  `StatsApiTests` (endpoint `200`/`403`-for-api-key, increments on APK download).

## 7. Verification

- `dotnet build` (0 errors), `node --check js/ui.js`, full `dotnet test`.
- Manual: download an APK via the F-Droid client/Basic-auth curl a few times →
  stats page shows the increments; daily graph shows today; API key hitting
  `GET /api/stats/downloads` → `403`, cookie session → `200`.
- Deploy with `podman compose build fdroidmgr && podman compose up -d fdroidmgr`,
  confirm healthy.

## 8. Non-goals / deferred

- Unique visitors, geography, user-agent breakdowns.
- Per-key attribution (we purposefully bust attribution via a shared repo key
  anyway; Basic auth username is ignored server-side today).
- Real-time streaming charts; 1-day granularity is enough.
- Counting index/icon fetches as "downloads" (could add an `Other` bucket later).
