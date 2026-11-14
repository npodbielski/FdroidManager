# App Metadata Edit (Web UI) — Plan

> Add an "edit" affordance to the per-app metadata panel so the operator can
> change the rich metadata fields from the Web UI after creating an app. The
> API already supports this (`PUT /api/apps/{packageName}`); this plan is UI-only.

---

## 1. Goal

Let the operator edit an app's Name / Summary / Description / License /
Website / SourceCode / IssueTracker from the Web UI, mirroring the already
implemented `PUT /api/apps/{packageName}` endpoint.

No new backend work: `AppsController.Update` + `FdroidMetadataService.WriteAsync`
+ `RepoIndexService.GenerateStrictAsync` already do write-yml-then-re-index, so
fdroid clients see the new values on their next index refresh.

---

## 2. Current state (verified)

- API: `PUT /api/apps/{packageName}` accepts a *partial* `UpdateAppRequest`
  `{ name?, summary?, description?, license?, website?, sourceCode?, issueTracker? }`.
  Merge semantics: omitted/`null` = keep current value; `""` = clear; value = set.
  Returns `200 { packageName, name, license }`. `404` unknown app, `400` empty name,
  `502` on `fdroid update` failure. (Implemented in `AppsController.Update`.)
- UI: `renderMetadata(panel, pkg)` in `wwwroot/js/ui.js` today only **previews**
  the yml (`GET .../metadata`) and offers a "Write metadata file" button
  (`POST .../metadata`). There is no form to change any field.

---

## 3. Design (UI only)

Extend `renderMetadata` in `wwwroot/js/ui.js`:

1. Keep the existing `<pre>` preview + "Write metadata file" regenerate button.
2. Add an **"Edit fields"** section (`<details>` or a small toggle) with a form:
   - `name` (required)
   - `summary`, `license`
   - `description` (`<textarea>`)
   - `website`, `sourceCode`, `issueTracker`
3. Populate the form with the current values. Source of truth for the initial
   values is the existing `GET /api/apps/{packageName}` response (`app.name`,
   `app.summary`, `app.license`, `app.web`, `app.source`, `app.tracker`,
   `app.description`) — which reads them from the index (or falls back to the yml).
4. On submit → `PUT /api/apps/{packageName}` with the JSON body, then:
   - refresh the panel preview (`GET .../metadata`),
   - call `loadApps()` so the app card header/version line reflects the new
     name/summary/license,
   - surface `4xx`/`502` errors inline (the shared `api()` helper already
     throws on non-2xx with the server's `{ error }` message).

Form-to-JSON: send only the fields the user can edit; empty `website`/`sourceCode`/
`issueTracker` are sent as `""` so they *clear* (matching the API's merge contract).

---

## 4. API contract

No change. Reuse:

```
PUT /api/apps/{packageName}   (Authorize)
  body: UpdateAppRequest (all fields optional, partial/merge)
  200  { packageName, name, license }
  400  { error: "name is required" }
  404  { error: "app not found" }
  502  { error: "fdroid update failed: …" }
```

---

## 5. Tests

- No JS test harness exists in this repo (§note: UI is plain vanilla JS, tested
  manually / via the API integration tests).
- The backend path is already covered by `AppMetadataApiTests.UpdateApp_EditsMetadataFields`
  and `ApiIntegrationTests2.AppCrud_CreateDuplicateUpdateDelete_KeyedByPackageName`.
- Manual check: create app → edit name/summary → card header updates → the
  `metadata/{pkg}.yml` file on disk shows the new fields; rebuild index →
  `repo/index-v1.json` `apps[].name` reflects the change.

---

## 6. Implementation checklist

- [x] `renderMetadata`: add edit form populated from the served metadata yml
      (`parseMetadataFields`, a JS mirror of `FdroidMetadataService.ParseAppMetadata`;
      chosen over `GET /api/apps/{packageName}` because that 404s for unpublished
      apps, while the yml is the source of truth and always exists after create).
- [x] submit → `PUT /api/apps/{packageName}` → refresh preview + `loadApps()`.
- [x] inline error display for 400/404/502.
- [x] auto verification (replaces manual-only): `MetadataServedTests` proves the
      edited fields reach the served `repo/index-v1.json` + `repo/index.xml`.

*Status: COMPLETED — UI edit form for app metadata (API already implemented).*
