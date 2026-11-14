# Signing Key Import + Generation Guard — Plan

> Adopt a **pre-existing** fdroid repo's apps by importing their signing
> keystores, and stop the manager from generating a fresh key for an app that
> already has published releases (a fresh key can never sign those apps'
> updates — Android/fdroid reject a signer change). In that case the operator
> must import the original key instead.

---

## 1. Goal

1. `POST /api/apps/{packageName}/keys/import` — register an existing signing
   keystore so the manager can sign the app's future updates (and match the
   signer for pre-signed uploads).
2. Guard `POST /api/apps/{packageName}/keys` — **refuse to generate** a key for
   an app that already has published releases. The error points at the import
   endpoint.
3. Both paths validate against the signer fdroid already recorded in the index,
   so a mismatched key is rejected at registration time, not at publish time.

---

## 2. Background (verified)

- Android requires every update of an app to be signed with the **same** key;
  fdroid records that signer in `repo/index-v1.json` per version
  (`packages[pkg][].signer`, surfaced as `FdroidApp.Versions[].SignerSha256`).
- `ReleaseService` already enforces this at upload: the upload's signer must
  equal the app's key fingerprint (`GetAvailableKeyAsync`), and fdroid itself
  records the signer into the index — a different key means the client refuses
  the update.
- `KeyService.GenerateKeyAsync` today creates a fresh RSA-4096 PKCS12 keystore,
  **overwrites** an existing store file blindly, and has no notion of "the app
  already has releases".
- The index signer is already parsed (`FdroidRepoService`); reading "does this
  app have releases, and with what signer" is a pure read, no new infra.

---

## 3. Design

### 3.1 Common helper — "what is this app locked to?"

```
FdroidIndex? index = await _repo.ReadIndexAsync(cfg, ct);
var app = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);
bool hasReleases = app is not null && app.Versions.Count > 0;
string? recordedSigner = app?.Versions
    .Select(v => v.SignerSha256).FirstOrDefault(s => s != null);
```

`KeysController` gains a `FdroidRepoService` dependency for this read.

### 3.2 `POST /api/apps/{packageName}/keys/import` (new)

Multipart form fields:

| field | required | notes |
|---|---|---|
| `keystore` | yes | the `.keystore`/`.pfx`/`.jks` file |
| `storePassword` | yes | keystore store password |
| `keyPassword` | optional | defaults to `storePassword` |
| `alias` | optional | keytool alias, default `release` |
| `branch` | optional | query param, default null (per-branch isolation) |

Behavior:

1. `404` when the app has no `metadata/{pending}` → "app not found".
2. Save the upload to a temp file; read its certificate fingerprint with
   `keytool -list` (`KeyService.GetFingerprint`). Fail `400` if the keystore is
   unreadable with the given passwords / alias.
3. If the app **has releases** and `recordedSigner` is known:
   - require `importedFingerprint == recordedSigner` (case-insensitive);
   - else `409 { error: "imported key (fp …) does not match existing releases (signed with fp …)" }`.
4. Copy the keystore into the key store root
   (`{KeyPath}/{packageName}/release.keystore`, or per-branch dir), AES-encrypt
   the key password under `FDROID_MASTER_KEY`, and insert an `AppKey` row
   (PackageName, Alias, Branch, StorePath, FingerprintSha256, PasswordEncrypted).
   Reuse `KeyService`'s existing encrypt/fingerprint helpers; add an
   `ImportKeyAsync(packageName, stream, storePassword, keyPassword, alias, branch, ct)`.
5. Return `200 { id, packageName, alias, branch, fingerprintSha256, storePath }`
   (same shape as the generate endpoint).

### 3.3 `POST /api/apps/{packageName}/keys` — generation guard (change)

Add before generating:

```
if (app has releases from the index)
    → 409 { error: "app already has releases — use /keys/import instead" }
```

Otherwise generate exactly as today (fresh keystore + `AppKey` row).

This replaces today's blind "delete existing store file and mint a new key"
footgun for apps that already published something.

### 3.4 Error summary

| Code | Meaning |
|---|---|
| `200` | imported / generated ok |
| `400` | unreadable keystore / bad alias / missing fields |
| `404` | app not found (no metadata yml) |
| `409` | import: fingerprint ≠ recorded signer; generate: app already has releases |
| `401` | unauthenticated |

---

## 4. Tests

**`KeysController`/`KeyService` (in-process API + real keytool):**

- import on an app with **no** releases → `200`, `AppKey` row exists, fingerprint
  recorded, keystore file copied.
- import on an app **with** releases + matching fingerprint → `200`.
- import on an app **with** releases + non-matching fingerprint → `409`, and
  **no** `AppKey` row is written (fail atomically).
- import with wrong password → `400`.
- generate on an app **with** releases → `409` (error mentions `/keys/import`);
  **no** keystore is overwritten.
- generate on an app **without** releases → `200` (unchanged behavior).
- unknown app → `404`; requires auth → `401`.

---

## 5. Notes / out of scope

- **Repo-level signing key** is deliberately out of scope: `fdroid update`
  re-signs the index itself from `config.yml` + `keystore.p12` already in a
  mounted repo — the manager does not need it for a pre-existing repo.
- If an app has releases but fdroid recorded *no* signer (e.g. an unsigned APK
  that slipped through), the import guard has nothing to compare against and
  accepts (logged warning).
- The UI "key" panel's "(re)generate key" button should surface the new 409
  rather than implying a successful replacement (follow-up UI tweak).

---

## 6. Implementation checklist

- [x] `KeyService.ImportKeyAsync` (parse fingerprint, persist keystore + encrypted password).
- [x] `KeysController`: `POST .../keys/import` endpoint + `FdroidRepoService` injection.
- [x] Guard `POST .../keys` (refuse when releases exist → "use /keys/import instead").
- [x] Tests (import success/mismatch/wrong-password + generate guard).
- [x] UI: surface the 409 on the key panel (and later an import form).

*Status: COMPLETED — import existing signing keys + refuse generation once an app has releases.*
