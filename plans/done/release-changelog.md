# Release Changelog Editing — Plan

> Allow the operator to fix/append the "what's new" text of an already-published
> release without touching the APK. The changelog is a manager-owned sidecar —
> for a binary-only repo fdroid does **not** bake it into the signed index — so
> it is the one release mutation that is safe. The APK bytes, canonical name,
> hash and recorded signer stay immutable.

---

## 1. Goal

1. `PATCH /api/apps/{packageName}/releases/{versionCode}` — set or clear the
   per-version changelog, re-reading it from the sidecar. No re-index, no APK
   change.
2. Web UI: an "edit changelog" affordance on each release row in `renderReleases`.

---

## 2. Why this is safe (verified, `plans/done/app-releases.md` §2.3)

- The manager writes `metadata/{pkg}/en-US/changelogs/{versionCode}.txt`
  (`FdroidRepoService.WriteChangelogAsync`) and reads it back for display
  (`ReadChangelogAsync`).
- fdroidserver's `_set_localized_text_entry(..., versionCode=…)` only attaches
  `whatsNew` to `app["Builds"]` entries — i.e. **source-build repos**. For a
  binary-only repo (no `Builds:` in the yml) the changelog is **not** inserted
  into `index-v1.json`; the index is therefore unaffected by changelog edits.
- Consequence: changing the sidecar does not change the signed index, so there is
  no re-index and no invalidation of what clients already cached.

---

## 3. Design

### 3.1 API

```
PATCH /api/apps/{packageName}/releases/{versionCode}   (Authorize)
  body: { "changelog": "…" | null }
```

Behavior:

1. Read the index; require the (versionCode) release to exist → else `404`.
2. `changelog == null` or whitespace → **delete** the sidecar file.
   Otherwise → `WriteChangelogAsync(packageName, versionCode, changelog)`.
3. Return the fresh `ReleaseDto` (same shape as `GET .../releases` items), so
   the caller sees the resulting `changelog` exactly as the list will render it.

Errors: `400` bad body / invalid `versionCode`; `404` release not found; `401`
unauthenticated. (No `502` — nothing shells out to fdroid.)

New DTO: `record UpdateChangelogRequest { string? Changelog; }`.

Route choice rationale: `PATCH .../releases/{versionCode}` (resource-partial-update)
rather than a nested `/changelog`, keeping the release resource self-consistent.
(A nested `…/releases/{versionCode}/changelog` alternative was considered and
rejected as no more discoverable.)

### 3.2 UI (`wwwroot/js/ui.js` — `renderReleases`)

Each release `<li>` gets a small "✎ changelog" button:

- click → inline `<textarea>` pre-filled with `r.changelog` + Save/Cancel;
- Save → `PATCH` → `load()` re-renders the list (which already shows the
  changelog text).

---

## 4. Tests

**API (`AppReleasesApiTests`, in-process + `FakeCommandRunner`):**

- set changelog → `200`, sidecar exists at `metadata/{pkg}/en-US/changelogs/{vc}.txt`;
  subsequent `GET .../releases` returns it.
- update existing changelog → `200`, content replaced.
- `{ "changelog": null }` → `200`, sidecar removed, list shows empty changelog.
- unknown `versionCode` / unknown package → `404`.
- requires auth → `401`.

The read/write sidecar machinery is already unit-tested in `FdroidRepoServiceTests`
(`Changelog_WriteAndRead_RoundTrip`).

---

## 5. Out of scope (explicitly not doing)

- Editing `versionName`/APK/hash/signer — immutable once published (fdroid
  recorded them in the signed index).
- Deleting/replacing the APK — already exists as `DELETE .../releases/{versionCode}`.
- Any "release update" that re-signs or swaps the binary.

---

## 6. Implementation checklist

- [x] `UpdateChangelogRequest` DTO.
- [x] `ReleasesController.PatchChangelog` (`[HttpPatch("{versionCode:int}")]`).
- [x] `renderReleases`: per-release edit-changelog control.
- [x] API tests for set/update/clear/404/401.

*Status: COMPLETED — safe per-release changelog editing (APK and index stay immutable).*
