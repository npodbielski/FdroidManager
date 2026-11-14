# App Icons & Screenshots (render fdroid's own images) — COMPLETED

> The manager does not display any app artwork today, even though fdroid already
> generates and serves it. This plan makes the Web UI render the **existing**
> images fdroid produced — there is no new artwork pipeline to build: for icons
> fdroid extracts them from the APK; for screenshots fdroid ingests per-locale
> files that the app's metadata provides (this is exactly how the official
> f-droid.org repo shows screenshots).

---

## 1. Goal

1. Show each app's **icon** in the Web UI app cards, read from fdroid's index
   (`repo/index-v1.json` `apps[].icon`) — no upload, no re-processing.
2. Show **screenshots** when present, read from the index's localized metadata.
3. Decide the fate of the dead `POST …/icon` and `POST …/screenshot` endpoints.

---

## 2. Research — how fdroid stores icons and screenshots (verified vs fdroidserver 2.4.5)

### 2.1 Icons — extracted from the APK (automatic)

`fdroid update` already extracts each APK's launcher icon
(`update.py:extract_apk_icons`, wired from the APK scan):

- `repo/icons/{packageName}.{versionCode}.png` (base density), plus density
  variants under `repo/icons-{120,160,240,320,480,640}/{packageName}.{versionCode}.png`
  (`update.py:get_repo_icons_dir`).
- The index references it as `apps[].icon` = e.g. `org.example.helloworld.2.png` —
  a **basename**, served from `repo/icons/{name}` (the fdroid client prepends
  `icons/`; the v2 index records it as `name: "/icons/…"`).

Verified against the live repo: `/srv/fdroid/repo/repo/index-v1.json` has
`apps[0].icon = "org.example.helloworld.2.png"`, and `repo/icons/` +
`repo/icons-{density}/` contain the matching `org.example.helloworld.2.png` files.
The serving URL is therefore `/fdroid/repo/icons/{icon}` (not `/fdroid/repo/{icon}`).

⇒ **Nothing to build for icons** — we only need to *read* `apps[].icon` and serve
it (the existing `/fdroid/repo/**` static route already serves `image/png`).

### 2.2 Screenshots — per-locale metadata files (the official-repo mechanism)

fdroid supports localized store graphics/screenshots from
`metadata/{packageName}/{locale}/…` — this is the "All About Descriptions,
Graphics and Screenshots" flow (`update.py:insert_localized_app_metadata`):

- **Single graphics** (`GRAPHIC_NAMES`): `icon.png` (→ `iconv2`), `featureGraphic`,
  `promoGraphic`, `tvBanner` — one per locale.
- **Screenshots** (`SCREENSHOT_DIRS`): `phoneScreenshots`, `sevenInchScreenshots`,
  `tenInchScreenshots`, `tvScreenshots`, `wearScreenshots` — any number per locale
  (Triple-T/fastlane-style dirs `phone-screenshots`, `tablet-screenshots`, … are
  also accepted and mapped onto the same keys).

Flow (all inside `fdroid update`):

1. `insert_localized_app_metadata` walks `metadata/{packageName}/{locale}/…` and
   copies graphic/screenshot files into
   `repo/{packageName}/{locale}/{dirname}/{file}` (single graphics get a
   hash-suffixed name, screenshots keep theirs).
2. `ingest_screenshots_from_repo_dir` scans `repo/{packageName}/{locale}/…*Screenshots*`
   and records them on the app.
3. In `index-v1.json`, screenshots surface under
   `apps[].localized.{locale}.{phoneScreenshots,sevenInchScreenshots,…}` (arrays
   of filenames). (fdroid also builds a top-level `apps[].screenshots` map, but
   `make_v1` deliberately drops it — the client reads `localized`.) A hash-named
   `icon`/`featureGraphic`/`promoGraphic`/`tvBanner` appears next to them.

⇒ This is why official f-droid.org apps have screenshots: their metadata ships
`metadata/{pkg}/{locale}/phoneScreenshots/*.png`, fdroid copies + indexes them,
and the client renders them from `localized`.

The exact filename shape is **basename** — confirmed against fdroidserver 2.4.5
(`update.py:ingest_screenshots_from_repo_dir` does
`graphics[screenshotdir].append(os.path.basename(f))`), so
`apps[].localized.{locale}.{…Screenshots}` are arrays of filenames only. To serve
one, the client (and the manager) reconstructs
`repo/{packageName}/{locale}/{dirname}/{filename}`. The *source of truth* is
`apps[].localized`; the serving side is `repo/{packageName}/{locale}/{dirname}/…`.

---

## 3. Final state (implemented — plans/done/app-media.md)

- `FdroidApp` now carries `Icon` (← `apps[].icon`) and `Screenshots`
  (← `apps[].localized.{locale}.{…Screenshots}`, flattened to repo-relative
  paths per locale) — see §4.1.
- `GET /api/apps` exposes `icon`; `GET /api/apps/{packageName}` exposes `icon` +
  `screenshots` (the preferred locale's list, en-US first).
- `wwwroot/js/ui.js`/`ui.css` render the icon on each app card and a screenshot
  gallery in the metadata panel.
- The dead `POST /api/apps/{packageName}/icon` and `…/screenshot` endpoints (and
  their `SaveMedia` helper) are **removed** — fdroid generates the artwork itself.

---

## 4. Design

### 4.1 Read model (`FdroidRepoService`)

Extend the records:

```csharp
public record FdroidApp(
    string PackageName, string Name, string Summary, string Description,
    string License, string? Web, string? Source, string? Tracker,
    string? Icon,                                    // apps[].icon (basename, served under repo/icons/)
    IReadOnlyDictionary<string, string[]>? Screenshots, // locale → repo-relative paths (merged device dirs)
    IReadOnlyList<FdroidVersion> Versions);
```

- `Icon` ← `apps[].icon` (basename; the client/UI prepends `icons/`).
- `Screenshots` ← flatten `apps[].localized.{locale}.{phoneScreenshots,…}` into a
  per-locale list of **repo-relative paths**
  (`{pkg}/{locale}/{dir}/{filename}` — reconstructed from the basenames fdroid
  stores). Device-class buckets are merged; the preferred locale (en-US first)
  is resolved by `FdroidRepoService.PreferredScreenshots` for the detail view.

### 4.2 API

- `GET /api/apps` — add `icon` to each card.
- `GET /api/apps/{packageName}` — add `icon` + `screenshots`.

No new routes; the bytes are served by the existing `/fdroid/repo/**`
(`RepoController.ServeRepo`), which already maps `.png` → `image/png`.

### 4.3 UI (`wwwroot/js/ui.js` + `ui.css`)

- App card: `<img class="app-icon" src="/fdroid/repo/icons/{icon}">` (fallback to
  a gray placeholder when `icon` is null or missing).
- Metadata panel: a small screenshot `<img>` gallery when `screenshots` is
  non-empty — `<img class="screenshot" src="/fdroid/repo/{path}">` using the
  repo-relative path from the API.

### 4.4 Remove the dead endpoints

- Delete `POST /api/apps/{packageName}/icon` and `…/screenshot` (or replace
  them with a documented "fdroid generates app artwork from the APK / localized
  metadata" note). Keeping dead routes that write bytes fdroid will ignore is
  misleading. Decision: **remove**, and note the fdroid-native alternative.

---

## 5. Tests

- **Unit (`FdroidRepoServiceTests`)**: fixture with `apps[].icon` and
  `apps[].localized.{locale}.{phoneScreenshots,…}` → assert `Icon` and
  `Screenshots` populate; absent → null/empty.
- **API (`AppMetadataApiTests` or new)**: `GET /api/apps` / `GET /api/apps/{pkg}`
  include `icon`; `GET /fdroid/repo/icons/{icon}` returns `image/png` (200)
  for a seeded icon file.
- **Manual/real proof**: publish the existing `data/…` APK → `fdroid update` →
  index has `icon`; UI card shows it; screenshots appear after dropping a png
  into `metadata/{pkg}/en-US/phoneScreenshots/` and re-running `fdroid update`.

---

## 6. Implementation checklist

- [x] `FdroidApp`: add `Icon` + `Screenshots`; parse `apps[].icon` and `apps[].localized`.
- [x] `AppsController`: expose `icon` (list + detail) and `screenshots` (detail).
- [x] UI: icon `<img>` on cards + screenshot gallery.
- [x] Remove dead `…/icon` and `…/screenshot` endpoints.
- [x] Tests (unit parser + API fields + static serve).

*Status: COMPLETED — render fdroid's existing icons/screenshots (and retire the dead upload endpoints).*

Verification (all green): `dotnet build FdroidMgr.slnx -c Release` (0 errors),
`dotnet format FdroidMgr.slnx --verify-no-changes --no-restore` (exit 0),
`dotnet test … --filter-class FdroidMgr.Tests.FdroidRepoServiceTests` (10),
`… AppMediaApiTests` (2), full suite 158 passing.
