# App Metadata (fdroid) — Plan

> Publish each app's F-Droid metadata file (`metadata/<packageName>.yml`).
> This is the missing half of "add a new application": the APK file alone gives
> fdroid the *technical* facts (package, versionCode, SDK levels, permissions,
> signer cert), but `fdroid update` only lists an app in the index when a
> matching `metadata/<appId>.yml` exists — otherwise it logs `"{apk} has no
> metadata!"` and drops it (see `update.py:get_apps_with_packages`, which
> intersects `apps` from metadata with the scanned APKs).
>
> The manager already stores everything this file needs in `AppInfo`
> (`Name`, `Summary`, `Description`, `License`, `WebSite`, `SourceCode`,
> `IssueTracker`) and has the per-app signing-key fingerprint
> (`AppKey.FingerprintSha256`). This plan adds an API endpoint + Web UI to
> generate that file, and hooks the same generator into release publishing so
> a newly-published app actually shows up.

---

## 0. Review against current codebase (verified)

This plan was written against an earlier schema. The implementation that
shipped differs as follows — all of it already exists in the codebase:

- **No `AppInfo` table.** The authoritative app fields live in the on-disk
  `metadata/{packageName}.yml` (read back via `FdroidMetadataService`), and
  apps are keyed by **package name**, not a numeric id. `FdroidMetadataService`
  therefore takes an `AppMetadataFields` record, not an `AppInfo` entity.
- **Routes are `…/apps/{packageName}/metadata`**, not `…/apps/{id}/metadata`.
- **Item 4 (publish integration) is superseded:** `ReleaseService.PublishAsync`
  already requires the metadata file to exist (`FdroidMetadataService.ExistsAsync`,
  else 404), and app creation (`POST /api/apps`) writes the yml, so by the time a
  release is published the yml exists and `fdroid update` picks it up. There is no
  `AppInfo` row left to re-read, so the old "publish writes metadata from DB" step
  is meaningless now.
- **Operator-facing field edits** (update Name/Summary/Description/… so fdroid
  clients see the new values): `PUT /api/apps/{packageName}` (backend, done) plus
  the Web UI edit form (implemented in `plans/done/app-metadata-edit-ui.md`).

---

## 1. Goal

1. `POST /api/apps/{id}/metadata` — generate/overwrite `metadata/<packageName>.yml`
   for an app from its DB row (+ signing key), then regenerate the F-Droid index.
2. `GET /api/apps/{id}/metadata` — return the current YAML (or 404 when not yet
   written) so the UI can preview it.
3. A Web UI action on each app card ("fdroid metadata") that previews the file
   and writes it with one click.
4. `ReleaseService.PublishAsync` also calls the generator, so publishing an APK
   automatically creates the metadata and the app appears in the index without
   a separate manual step.

---

## 2. Research — what fdroid needs (verified against fdroidserver 2.4.5)

### 2.1 Discovery & app id

`metadata.read_metadata()` globs `metadata/*.yml`; the **app id is the file
stem** and must be a valid package name (`metadatapath.stem`, checked with
`common.is_valid_package_name`). There is **no `AppId:` key inside the file**.

```python
for metadatapath in metadatafiles:
    appid = metadatapath.stem            # ← metadata/org.example.helloworld.yml → org.example.helloworld
    ...
    apps[app.id] = app
```

`fdroid update` then intersects `apps` (from metadata) with the APKs found in
`repo/*.apk` (`get_apps_with_packages`), so:

- **APK without metadata** → `warn "{apk} has no metadata!"`, dropped from index
  (raw file still served, but not listed by the F-Droid client).
- **metadata without APK** → also not listed (no package to attach).

So for an app to appear, write the metadata file **and** have a published APK in
`repo/` with the canonical name `{packageName}_{versionCode}.apk` (release
publishing already does that part), then run `fdroid update`.

### 2.2 Metadata fields we map (binary repo — no `Builds`)

From `metadata.py:yaml_app_field_order`, the fields relevant to a binary-only
repo (no source build) and their source in our DB:

| Metadata key | AppInfo source | Notes |
|---|---|---|
| `Name` | `Name` | required human name |
| `Summary` | `Summary` | one-line |
| `Description` | `Description` | multi-line → write as a YAML block scalar (`|`) |
| `License` | `License` | SPDX id (`MIT`, `Apache-2.0`, …); present in DB, default `Unknown` |
| `WebSite` | `Website` | optional |
| `SourceCode` | `SourceCode` | optional |
| `IssueTracker` | `IssueTracker` | optional |
| `AllowedAPKSigningKeys` | `AppKey.FingerprintSha256` (all the app's keys) | restrict accepted signers to the keys we generated — recommended, omit when the app has no key yet |

Example the endpoint must produce:

```yaml
Name: Hello World
Summary: A demo app
Description: |
  Longer text shown in the F-Droid client.
License: MIT
WebSite: https://example.com
SourceCode: https://git.example.com/example/helloworld
IssueTracker: https://git.example.com/example/helloworld/-/issues
AllowedAPKSigningKeys:
  - a1b2c3…64-hex-sha256…
```

### 2.3 Where the file goes

- Repo **root** = `Path.GetDirectoryName(RepoConfig.RepoPath)` (the `config.yml`
  parent; `/data/fdroidrepo`), **not** `RepoPath` itself (`/data/fdroidrepo/repo`).
- File = `metadata/{PackageName}.yml`.
- `fdroid update` runs with cwd = repo root (this is already what
  `RepoIndexService.GenerateAsync` does via `ResolveRepoRoot`).

---

## 3. Design

### 3.1 `FdroidMetadataService`

New service, no new NuGet package (hand-rolled YAML emitter — the repo is
deliberately dependency-lean; the field set is small and we control the values):

```csharp
public class FdroidMetadataService
{
    // IConfiguration? no — RepoPath comes from RepoConfig; logger for warnings.
    public async Task<string> WriteAsync(AppInfo app, CancellationToken ct);
    // → renders YAML, writes metadata/{app.PackageName}.yml atomically,
    //   returns the full path.

    public string? GetPath(AppInfo app);           // path (null if RepoConfig.RepoPath empty)
    public async Task<string?> ReadAsync(AppInfo app, CancellationToken ct); // current contents or null

    // pure/unit-testable:
    public static string Render(AppInfo app, IEnumerable<string>? signingKeyFingerprints);
}
```

`Render` details:
- `Description`: if it contains newlines or leading/trailing whitespace → emit as
  `Description: |` block scalar; a single short line → plain `Description: <v>`.
- String escaping for plain scalars: quote when the value contains `: `, `#`,
  leading/trailing space, or starts with a YAML indicator; the service writes a
  small `EscapeYamlScalar` helper (tested).
- Empty optional fields (`Website`/`SourceCode`/`IssueTracker`) are omitted.
- `AllowedAPKSigningKeys` only when the app has at least one key; emit each
  `- <fingerprint>` (lowercased 64-hex). No key → omit the list.
- Stable field order (as in the table above), UTF-8 no BOM, trailing newline.
- Atomic write: temp file in `metadata/` + `File.Move(overwrite: true)`, chmod
  `0644` (repo files are world-readable so the F-Droid client can mirror them).

### 3.2 Controller (AppsController)

```csharp
/// <summary>Generate/overwrite the app's fdroid metadata file and re-index.</summary>
[Authorize]
[HttpPost("{id:int}/metadata")]
public async Task<IActionResult> WriteMetadata(int id, CancellationToken ct)
    // 404 if app missing
    // FdroidMetadataService.WriteAsync(app)
    // RepoIndexService.GenerateAsync(cfg)   (fdroid update)
    // 200 { packageName, path, name, license, signingKeyFingerprints: [...], indexRegenerated: true }

/// <summary>Current metadata YAML (preview), or 404 when not yet written.</summary>
[Authorize]
[HttpGet("{id:int}/metadata")]
public async Task<IActionResult> GetMetadata(int id, CancellationToken ct)
    // 200 text/plain (or JSON { path, yaml }) — decide; text/plain is simplest for a <pre>
```

`POST` is idempotent (re-run just overwrites + re-indexes). Index regeneration
failure → `502` `{ error: "fdroid update failed: …" }` (mirror repo init).

### 3.3 Publish integration (`ReleaseService.PublishAsync`)

After copying the APK and saving the release row, before
`_index.GenerateAsync(cfg, ct)`:

```csharp
await _metadata.WriteAsync(app, ct);   // ensure metadata exists for the new package
```

This is the key correctness change: it turns "publish an APK" into "publish an
APK **and** make it visible", closing the loop the user asked about. The manual
endpoint stays for (re)generating metadata independently of a release.

### 3.4 Restart requirement

None. Metadata files + `fdroid update` are picked up on the next
`fdroid update` run; the endpoint itself triggers it.

---

## 4. API contract

```
POST /api/apps/{id}/metadata                 (Authorize)
GET  /api/apps/{id}/metadata                 (Authorize)
```

| Code | Body |
|---|---|
| `200` | `{ packageName, path, name, license, signingKeyFingerprints: ["64hex"], indexRegenerated: true }` |
| `404` | `{ error: "app not found" }` |
| `502` | `{ error: "fdroid update failed: …" }` |
| `401` | unauthenticated (any endpoint already `[Authorize]` at controller level) |

---

## 5. UI (single-file vanilla JS, `wwwroot/ui.html` + `js/ui.js`)

- Add a **"metadata"** action to each app card (`data-action="metadata"`), in the
  same expansion-panel pattern as releases/git/key (`onCardAction` →
  `renderMetadata(panel, appId)`).
- `renderMetadata`:
  1. `GET /api/apps/{id}/metadata` → show the current YAML in a `<pre>`
     (or "not written yet").
  2. A **"Write metadata file"** button → `POST /api/apps/{id}/metadata` →
     show `✓ written to <path>, index regenerated`, refresh the `<pre>`.
- No new section/route; it lives in the existing per-app cards.

---

## 6. Tests

- **Unit** (`FdroidMetadataServiceTests`):
  - `Render` maps every AppInfo field to the right key; omits empty optionals.
  - Multi-line `Description` → block scalar; single line → plain.
  - `EscapeYamlScalar` quotes values containing `: `, `#`, leading/trailing space.
  - `AllowedAPKSigningKeys` emitted from `FingerprintSha256` list; omitted when none.
  - filename is `metadata/{packageName}.yml`, written under the repo **root**
    (assert via a temp `RepoConfig.RepoPath = tmp/repo` → file at `tmp/metadata/...`).
- **API** (`AppMetadataApiTests`, mirror `RepoInitApiTests` + `FakeCommandRunner`):
  - `POST` → `200`, file exists at `{repoRoot}/metadata/{package}.yml`, content
    matches the app row; `GET` returns the same YAML.
  - unknown id → `404`; requires auth → `401`.
  - idempotent re-`POST` → still `200`, file overwritten.
  - `fdroid update` invoked (Runner log) with cwd = repo root after write.
- **Integration / real proof** (the part that matters):
  - extend `ci/e2e-test.sh` (or add to `OAuthLoginIntegrationTests`–style in-process
    check) so after publishing an APK **and** writing metadata, the regenerated
    `index.xml` actually contains the app `<package>` + `<name>`.

---

## 7. Security / operational notes

- `AllowedAPKSigningKeys` pins each app to the signing key(s) the manager
  generated for it (from `AppKey.FingerprintSha256`). This is a client-visible
  trust hint and stops a mismatched-signed APK from being served as an update.
- Metadata files are plain text, non-secret → `0644` in the repo root; nothing
  encrypted here (unlike `RepoConfig.RepoKeystorePassEnc` / key passwords).
- The endpoint is `[Authorize]` (controller-level) — writes remain operator-only.
- Writing the file is a local disk op; no network. `fdroid update` may fetch
  icons/descriptions if `repo_web_base_url` is set (out of scope).

---

## 8. Implementation checklist

- [x] `FdroidMetadataService` (`Render`, `WriteAsync`, `ReadAsync`) + YAML escape helper.
- [x] `AppsController`: `POST {packageName}/metadata` + `GET {packageName}/metadata`.
- [x] `ReleaseService.PublishAsync`: write metadata before `fdroid update`
      *(superseded — see §0: publish already requires the yml to exist, and the
      yml is written at app creation, so `fdroid update` picks it up; no DB row
      remains to read).*
- [x] DI registration + `RepoIndexService` reuse (no new package).
- [x] UI: `metadata` card action + `renderMetadata` preview/write panel.
- [x] Tests: unit (render/escape/filename), API (200/404/401/idempotent/runner
      cwd), and an e2e assert that an edited app's fields change in the served
      index (`MetadataServedTests`).

---

*Status: COMPLETED — app metadata file generation + UI; field edits propagate to the served fdroid index.*
