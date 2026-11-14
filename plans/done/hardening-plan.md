# Hardening & cleanup plan (post-review)

> Produced from a full code review after MR !37 merged (`dd534fa`).
> Each **step** is one MR against `master` (feature branch → MR, never direct push).
> Definition of done per MR: AGENTS.md checklist (tests green, format clean,
> coverage ≥95%, pipeline green, no merge without approval).
> Status markers: `[ ]` open, `[x]` done.

Priority order is deliberate: data-corruption/wedge risks first, then
security, then structural cleanups that delete boilerplate, then nits.

---

## Phase A — correctness & stability (data-loss / wedge risks)

### A1. `[x]` Serialize `fdroid update` behind a repo lock

**Problem.** `RepoIndexService.GenerateAsync` / `GenerateStrictAsync` spawn
`fdroid update` against the repo directory. Two concurrent publishes (or a
publish + a manual rebuild + boot-time rebuild) run *two* fdroidserver
processes on the same directory → index corruption, half-written APKs,
`index.jar` mismatch. `ReleaseService.PublishAsync` additionally has a TOCTOU:
`vc <= lastCode` is checked against an index snapshot that another request may
already have invalidated.

**Fix.**
- New `IRepoLock` (Services/RepoLock.cs): `IDisposable Acquire(CancellationToken)`
  over a `SemaphoreSlim(1,1)` (single-instance assumption, documented).
  Register singleton.
- `GenerateAsync` / `GenerateStrictAsync` acquire the lock around the process
  run. `ReleaseService.PublishAsync` acquires it for the whole
  validate-version → drop-apk → `GenerateStrictAsync` critical section
  (this also closes the versionCode TOCTOU).
- Re-entrancy: publish holds the lock and calls GenerateStrict — give
  `RepoLock` an async-local/flag-free design by passing the acquired lock
  token into the generate call (`GenerateAsync(cfg, createMetadata, lockLease, ct)`
  overload; the parameterless path acquires itself).

**Tests.** Two concurrent `GenerateStrictAsync` calls — second waits until the
first fake run completes (FakeCommandRunner handler gated on a `TaskCompletionSource`);
publishing the same versionCode twice concurrently yields exactly one success
and one `ReleaseConflictException`.

### A2. `[x]` Timeout and kill for external processes

**Problem.** `ProcessCommandRunner.RunAsync` never bounds execution: a hung
`fdroid update`/`keytool` wedges a request thread forever, and the boot-time
rebuild (Program.cs scope block) can block startup indefinitely against the
container HEALTHCHECK.

**Fix.**
- `RunAsync`: linked `CancellationTokenSource` with configurable timeout
  (`FDROID_PROCESS_TIMEOUT_SECONDS`, default 900 for fdroid, 60 for
  keytool/apksigner/aapt — pass per-callsite or infer from exe). On timeout:
  `p.Kill(entireProcessTree: true)`, return `CommandResult(124, "", "timed out")`.
- Read stdout/stderr concurrently (`Task.WhenAll`) before waiting for exit —
  current sequential `ReadToEndAsync` can deadlock if the child writes lots of
  stderr after filling stdout's pipe.

**Tests.** FakeProcess stub or `/bin/sleep`-based integration test asserting
kill-on-timeout and `CommandResult.ExitCode == 124`.

### A3. `[x]` SQLite pragmas: WAL + busy_timeout

**Problem.** Default rollback journal + no busy timeout: concurrent
download-stats upserts (every APK download hits `RecordAsync`) and index
rebuild writes will throw `SQLITE_BUSY` under modest load.

**Fix.**
- `EnsureWalAsync` (called from `DatabaseInitializer` after migrations)
  queries `PRAGMA journal_mode` and converts the database to WAL once;
  the mode persists in the DB file, so every later connection —
  including external tools — gets WAL for free. The EF Core SQLite
  provider sets WAL only when it creates a file database, so the
  explicit check is what covers pre-existing rollback-journal DBs.
- `busy_timeout` needs no code: Microsoft.Data.Sqlite maps the command
  timeout (default 30 s) onto `sqlite3_busy_timeout` on every connection.
- Keep DB file colocated with WAL on the same volume (already true: `/data`).

**Tests.** Two contexts updating the same stats bucket concurrently — no
exception, final count == 2.

### A4. `[x]` Boot work off the request-critical path

**Problem.** `Program.cs` runs `DatabaseInitializer` + a potentially long
`fdroid update` *before* `app.Run()`; container healthcheck (`/api/repo/status`,
which parses the full index — heavy) times out on first boot.

**Fix.**
- Move the whole scope block into a `BootInitializer : IHostedService`
  (also satisfies review item: DI-friendly, testable, Program.cs declarative).
- Rebuild-at-boot becomes fire-and-forget *inside* the hosted service with the
  A1 lock (status endpoint reports last outcome — `LastIndexBuiltUtc` already
  exists), so the app starts serving as soon as migrations are applied.
- New trivial `GET /healthz` (anonymous, no IO/DB): returns 200 with
  `{ status: "ok", bootCompleted: bool }`. Point Containerfile HEALTHCHECK at it.

**Tests.** Healthz returns 200 while boot rebuild is still "running" (blocked
fake runner); BootInitializer is exercised via existing TestHost without change.

---

## Phase B — security

### B1. `[x]` Replace hand-rolled AES-CBC with `IPasswordProtector` on AES-GCM

**Problem.** Three duplicated implementations (`KeyService`, `OAuthConfigService`,
`RepoInitService`) encrypt with **AES-CBC and no authentication tag** (malleable
ciphertext, no integrity) using key = single SHA-256 of the master passphrase.

**Fix.**
- New `IPasswordProtector` / `PasswordProtector` (instance, injected):
  `string Protect(string plain)`, `string Unprotect(string blob)`.
- Format: `v2:<b64nonce(12)>:<b64tag(16)>:<b64cipher>` using `AesGcm`.
- Key derivation: `Rfc2898DeriveBytes.Pbkdf2(masterPass, salt, 600_000, SHA256)`;
  salt stored once in RepoConfig (`MasterKeySalt` column, EF migration) —
  generated on first use.
- **Migration path**: blobs without the `v2:` prefix are legacy CBC — decrypt
  with the old codepath (kept private inside PasswordProtector), re-encrypt on
  next write (already happens: keys/config are rewritten whenever edited). Add a
  one-shot boot pass that re-protects all legacy blobs it can read.
- Delete the three CBC copies; `FDROID_MASTER_KEY` short/missing warning stays
  in RepoInitService.

**Tests.** Roundtrip v2; legacy blob (fixture generated by the old algorithm)
decrypts and is transparently re-protected; tampered v2 blob throws; PBKDF2
salt persisted and reused.

### B2. `[x]` Keep keytool/apksigner passphrases out of argv

**Problem.** `keytool -storepass <pw>` / `apksigner --ks-pass pass:<pw>` expose
the decrypted keystore password to any process reading `/proc/<pid>/cmdline`
in the container.

**Fix.**
- `keytool`: `-storepass:env FDROID_KEYPASS` / `-keypass:env FDROID_KEYPASS`,
  value passed via `ProcessStartInfo.Environment` (never argv).
- `apksigner`: `--ks-pass env:FDROID_KS_PASS --key-pass env:FDROID_KEY_PASS`.
- The three leak sites live in `KeyService`'s own `Process` calls (not the
  `IRepoCommandRunner` path), so `RunTool` gained an optional
  `IReadOnlyDictionary<string,string>? passEnv` applied to
  `ProcessStartInfo.Environment` — child-only, the parent env is untouched.

**Tests.** Stub `keytool`/`apksigner` scripts record argv+env: args must
contain `-storepass:env`/`env:FDROID_KS_PASS` and must not contain the
passphrase; the real keytool chained behind the stub still produces a real
keystore fingerprint.

### B3. `[x]` Harden repo file serving

**Problem.** `IsWithin` compares with `OrdinalIgnoreCase` — wrong on Linux and
potentially escapable with case-varying prefixes. Serving is also unrestricted
inside `RepoPath` once `FDROID_REQUIRE_REPO_AUTH` is off.

**Fix.**
- `IsWithin`: `StringComparison.Ordinal` + segment walk rejecting any `..`
  segment after `GetFullPath`; compare on the full normalized root with
  trailing separator.
- Explicit denylist regardless of position: `config.yml`, `*.p12`, `*.jks`,
  `*.db`, dotfiles → 404 (defense in depth in case RepoPath is ever pointed at
  the repo *root* instead of the output dir).
- Content-Type map: unknown extensions → `application/octet-stream`
  (already) — but also set `X-Content-Type-Options: nosniff`.
- `Results.File(..., enableRangeProcessing: true)` — F-Droid clients range-request
  APKs (fixes perf item G3 at the same time).

**Tests.** `GET /fdroid/repo/../config.yml`, `..%2fconfig.yml`, mixed-case
`..%2fCONFIG.yml` → 404; range request on an APK returns 206 with
Content-Range; containment stays exact-case.

### B4. `[x]` Throttle API-key `LastUsedUtc` writes

**Problem.** Every authenticated request executes an `UPDATE` on `ApiKeys`
(write amplification + SQLite contention; compounds A3).

**Fix.** In `ApiKeyService.ValidateAsync`: return the stored `LastUsedUtc` in
the projection and only `ExecuteUpdate` when it is null or older than 60 s
(constant `LastUsedWriteThreshold`).

**Tests.** Two rapid authentications → single update (count via in-memory
interception or FakeDb timestamp check); expired key still returns null without
writing.

**Result.** Landed as `fix/apikey-hardening`: the projections carry `LastUsedUtc`
and the stamp is skipped while it is younger than the threshold; revoked and
expired keys still authenticate to nothing without writing.

### B5. `[x]` Reject unknown scopes from a single source of truth

**Problem.** Scope strings (`read`, `apps:write`, `releases:write`) are
hardcoded in three places: `ApiKeyService.AllScopes`,
`CreateApiKeyRequestValidator`, Program.cs authorization policies. Divergence
= policy bypass or impossible-to-pass validation.

**Fix.** `static class Scopes` (constants + `All` array + `Expand` logic — pure,
no IO) as the one source; `ApiKeyService`, the validator and `AddAuthorization`
all consume it. (`IApiKeyService.AllScopes` becomes a pass-through or goes away.)

**Tests.** Constants equality tests between validator accepts and policies.

**Result.** Landed as `fix/apikey-hardening`. `Scopes` (constants + `All` +
`IsValid` + `Expand`) is the single source: `ApiKeyService`, the validator, the
`AddAuthorization` policies, the endpoint `RequireAuthorization` calls and the
claim type all read it; `IApiKeyService.AllScopes` is gone and
`ScopeClaimsTransformer` no longer needs the service at all. Both key kinds
(`ApiKeys`, `FdroidClientKey`) stamp `LastUsedUtc` only when it is null or older
than `ApiKeyService.LastUsedWriteThreshold` (60 s).

---

## Phase C — structure (delete boilerplate)

### C1. `[x]` Central exception → ProblemDetails mapping

**Problem.** Every mutating endpoint hand-copies
`try/catch (KeyNotFoundException → 404) / ReleaseConflictException → 409 /
ArgumentException → 400 / FdroidUpdateException → 502`. The
`UseExceptionHandler` currently rethrows (no structured 500 body, and it hides
the stack from logging middleware).

**Fix.**
- `app.UseExceptionHandler()` default handler +
  `app.MapProblemDetails` / `AddProblemDetails(...)` conventions mapping:
  `KeyNotFoundException→404`, `ReleaseConflictException→409`,
  `ArgumentException→400`, `FdroidUpdateException→502`,
  `InvalidOperationException→400` (only where current code maps it),
  unknown → 500 `Problem("Unhandled request failure")` with log (never rethrow).
- Delete the per-endpoint try/catches that the filter now covers (Init,
  Publish, Key import, …). Keep catch blocks only where the handler adds
  context (e.g. `duplicatesRemoved` in DeleteRelease).
- Existing body shape compat: tests assert `error` field today — either keep a
  custom `extensions.error` mirror during a deprecation window, or update the
  tests + UI in the same MR (prefer the latter; it's our app).

**Tests.** Each exception type maps to its status + problem body; 500 no longer
rethrows (asserts structured body and logged exception).

**Result.** Landed as `refactor/problem-details`. `AddProblemDetails` +
`app.UseExceptionHandler()`, with one `IExceptionProblemMapper` per exception
(`NotFoundProblem`, `ReleaseConflictProblem`, `RepoAlreadyInitializedProblem`,
`OAuthAlreadyConfiguredProblem`, `FdroidUpdateProblem`, `BadRequestProblem`,
`InvalidOperationProblem`, `UnhandledProblem`) resolved by exact runtime type
and walked up to the closest registered base type. A new mapping is a new class
plus one registration; the dispatcher never changes. Bodies keep a top-level
`error` field next to the standard fields, so the UI and the existing tests did
not have to move. The hand-written
catches in Init, OAuth configure, Publish and Key import are gone; DeleteRelease
keeps its own because it adds `duplicatesRemoved` context. Two follow-ups fell
out of it: `FdroidUpdateException` now derives from
`InvalidOperationException` (so a missed `fdroid` failure can never be reported
as a client error), and `RepoInitService` reports a failed clear of the backup
as `IOException` rather than a 400-worthy `InvalidOperationException`.

### C2. `[x]` Typed response records for anonymous DTOs

**Problem.** Anonymous response objects in endpoints make the OpenAPI document
useless and invite drift with `wwwroot/js/ui.js`.

**Fix.** Introduce response records (`ApiKeyCreatedResponse`, `RepoInitStatus`
(already), `ClientKeyResponse`, `AppCreatedResponse`, …) per endpoint that
returns an anonymous type; wire them in and let `AddOpenApi` emit schemas.

**Tests.** OpenAPI document contains the components (golden-file-ish assertion
on `/openapi/v1.json` in dev) — optional; at minimum existing tests switch to
typed deserialization where convenient.

**Result.** Landed as `refactor/typed-responses`. Every anonymous body in
`Endpoints/` (and `/healthz`) is now a record in `Models/`
(`ApiKeyCreatedResponse`, `ClientKeyStateResponse`/`ClientKeySecretResponse`,
`AppCreatedResponse`/`AppDetailResponse`/`AppKeyResponse`/`AppVersionResponse`/
`AppMetadataResponse`/`AppMetadataWrittenResponse`/`AppUpdatedResponse`/
`AppIconResponse`, `RepoStatusResponse`, `AuthMeResponse`, `StatusResponse`,
`HealthzResponse`, `ErrorResponse` for the plain `{"error":…}` bodies), and the
camelCase field names the UI reads did not move — `ClientKeySecretResponse`
drops `regeneratedUtc` entirely until a regeneration happened, exactly as the
old anonymous reveal body did. Since minimal APIs cannot infer a result union
from `Results.Ok(...)` calls, each endpoint carries `.Produces<T>()` (and
`.Produces<ErrorResponse>(status)`) so `AddOpenApi` emits the components. The
plain `new { error = … }` bodies are deliberately kept: they are what `ui.js`
reads on these local validations (the Problem Details pipeline from C1 covers
exceptions, not them). `OpenApiDocumentTests` asserts the documented
method/path/status → schema mapping for every typed response, and the API key,
app CRUD and F-Droid client key tests read the records instead of a
`JsonDocument`.

### C3. `[x]` Endpoint hygiene: inject loggers, shrink service statics

- Replace `http.RequestServices.GetRequiredService<ILoggerFactory>()` hacks in
  ServeRepoFile / ImportKey / DeleteRelease with an injected `ILogger<X>`
  endpoint parameter (minimal APIs support it) — or move the log call into the
  service and drop `HttpContext` usage.
- `FdroidMetadataService.GetPath` → instance method next to `ResolveRepoRoot`.
- `ReleaseService.SafeUploadName`: add charset allowlist `[A-Za-z0-9._\-]`
  (replace others with `_`) before forcing `.apk`.

**Tests.** Compile-level; behavior tests unchanged.

**Result.** Landed as `cleanup/hygiene`. The `RequestServices` lookups are gone
from ServeRepoFile, ImportKey and DeleteRelease, and the `Logger(http, name)`
helper in `AppsEndpoints` with them: the endpoints take `ILoggerFactory` as a
minimal-API parameter and keep their existing categories (the endpoint holder
classes are `static`, so `ILogger<X>` is not a valid generic argument — CS0718).
`GetPath` is an instance member of `IFdroidMetadataService`, deliberately not
`virtual`: the part-fakes in the tests stub virtual members by default, which
would have silently broken its internal callers. `SafeUploadName` now keeps
only `[A-Za-z0-9._-]` (everything else becomes `_`) before forcing `.apk`, so
an upload name cannot smuggle a path or shell-unsafe character into the repo
directory.

### C4. `[x]` Reduce repeated index parsing per request

**Problem.** List/detail endpoints read + parse `index-v1.json` multiple times
per request (ReadIndexAsync, ReadV2IconsAsync, then again in services).

**Fix.** Scoped `RepoIndexSnapshot` (per-request cache: parsed index + v2 icons
keyed by file mtime) registered scoped; `FdroidRepoService` checks the cache.
Same pattern already exists process-wide in `DownloadStatsIndexCache`.

**Tests.** Repeated `ReadIndexAsync` within one scope touches IO once
(FakeIOService call counter).

**Result.** Landed as `cleanup/hygiene`. `IRepoIndexSnapshot` +
`RepoIndexSnapshot`, registered scoped, memoize the parsed `index-v1.json` and
the `index-v2.json` icons for one request, keyed by path and mtime — a rebuild
in the middle of a request (publish, metadata write) bumps the mtime and is
therefore still seen. `FdroidRepoService` consults it in `ReadIndexAsync` and
`ReadV2IconsAsync`, so endpoints that read the index repeatedly now pay for one
disk parse. Tests substitute `IOService` and assert `OpenRead` is called once
per mtime, then twice after the stamp moves.

---

## Phase D — small items

### D1. `[x]` Background writer for download stats

`ServeRepoFile` performs DB write before streaming. Move `RecordAsync` to a
`Channel<(pkg,vc)>` drained by a hosted `DownloadStatsWriter` (single consumer
→ also removes write contention). Stats endpoint unaffected (reads DB).
Bounded channel; on full, drop with counter log (downloads must never fail
because stats are slow).

**Result.** Landed as `cleanup/hygiene`. `IDownloadStatsQueue`
(`DownloadStatsQueue`, singleton) wraps a bounded `Channel` of 1024 hits;
`ServeRepoFile` resolves the package/version from the index and enqueues
instead of writing, and the new hosted `DownloadStatsWriter` is the single
consumer — it opens a scope per hit and keeps the SQLite writes serialised. A
full queue is refused and counted in `Dropped` (`FullMode.Wait`, because with
`DropWrite` `TryWrite` reports success for an item it silently discarded).
The download can no longer fail or wait on the stats write.

### D2. `[x]` Log the swallowed fingerprint error

`ReleaseService.TryGetSignerFingerprint` bare `catch` → log debug with exception
(consistent with the repo's "log instead of swallow" rule).

**Result.** Landed as `cleanup/hygiene`: the catch now logs
`SignerFingerprintFailed` (debug, with the exception and the APK path) before
reporting the APK as unsigned. A test makes the inspector throw and asserts the
publish still signs the APK and emits that line.

### D3. `[x]` Containerfile pinning & hygiene

- Pin `pip3 install "fdroidserver==2.3.2"` (or current stable) — reproducible images.
- Pin apt packages to installed security updates only; no change otherwise.
- HEALTHCHECK → `/healthz` (with A4).

**Result.** Landed as `chore/image-pinning`. `fdroidserver` installs from its
PyPI URL pinned to 2.4.5 with a `sha256` fragment (the version pip resolved
before the pin, so behaviour does not move), as overridable `ARG`s. The runtime
base moved off Ubuntu to `debian:trixie-slim`: Microsoft publishes no stable
Debian image for .NET 10, so the shared frameworks are copied out of the
official aspnet image and the apt layer rewrites the Debian sources down to the
`trixie` + `trixie-security` pockets — no feature updates, CVE fixes only —
behind a `grep` that fails the build if a base-image change makes the rewrite a
no-op. trixie ships no openjdk-17 (the image uses 21) and no ICU, so `libicu76`
is installed explicitly rather than relied on from the base. HEALTHCHECK already
hit `/healthz` since A4, so nothing to do there. Verified locally: full image
build, boot, `/healthz` 200, `fdroid --version` 2.4.5, aapt/apksigner/keytool
present, the OIDC login E2E against the mock IdP, and `POST /api/repo/init`
running `fdroid init`/`update` to a signed index.

### D4. `[x]` Repo hygiene

- `.gitignore`: `fdroidmgr.db`, `test-results/` (both currently present in the
  worktree).
- `plans/hardening-plan.md` status updates as steps merge; move to
  `plans/done/` when all items close.

**Result.** Already satisfied: `.gitignore` covers `*.db` (so `fdroidmgr.db`
and its WAL siblings) and `test-results/`, and neither is tracked. Nothing to
change.

### D6. `[x]` Raise the coverage gate to 95%

**Problem.** `ci/coverage-gate.sh` floors line coverage at 80 %; the actual
line-rate has been stable at ~90.6 % for a long time, so the gate no longer
pulls its weight and new dead spots slip in under it.

**Fix.** Two moves, in this order:

1. **Exclude the untestable by design** (otherwise 95 % is a lie):
   `-p:ExcludeByFile='**/Data/Migrations/*.cs'` and coverlet attributes for
   generated/wiring code (`Program.cs` top-level wiring stays covered by the
   `WebApplicationFactory` tests, so no exclusion needed there). EF migration
   files are ~360 lines of generated DDL that no unit test executes; leaving
   them in the denominator caps what honest tests can reach.
2. **Close the real gaps** the gate table still shows: `StampBaselineAsync`
   (58 missed — reachable via a legacy-DB fixture: create tables the pre-migration
   way, then run `InitializeAsync`), `EnsureColumnAsync` (42 — same fixture),
   `ProcessCommandRunner.RunAsync` real-path (32 — an `/bin/echo`-style test
   already exists; extend it to cover the timeout kill and stderr interleaving),
   `KeyService.SignApkAsync` real path (26 — stub `apksigner` like the B2 tests
   do), `FdroidRepoService` (78 — index parsing edge branches),
   `DynamicOpenIdConnectHandler` (22 — the live-provider reconcile branches the
   integration tests half-cover).

**Gate move.** Bump `GATE` in `ci/coverage-gate.sh` to `0.95` in the same MR
that lands the exclusions + the first batch of new tests (measure first: the
exclusion alone moves the rate ~+7 points; the test batch must bring the rest).
If one stubborn class keeps the run under the bar, a second interim gate at
0.93 with the remaining gaps listed as follow-ups is acceptable — but the end
state is 95 % with migrations excluded, not suppressions.

**Tests.** The MR *is* tests; the gate script itself is covered by the CI job
failing on a deliberately-lowered local run before it passes.

**Result.** Landed as `test/coverage-95` without the migration exclusion: the
test batch alone moved the measured line-rate 91.63 % → 95.38 % with the
generated migration DDL still counted in the denominator, so the exclusion
turned out to be unnecessary. Dropping it later would only inflate the number,
so the migrations stay in.

## Suggested MR sequence & sizes

| # | Step | Branch (suggested) | Size |
|---|------|--------------------|------|
| 1 | A1 repo lock | `fix/repo-index-lock` | S–M |
| 2 | A2 process timeout + concurrent reads | `fix/process-timeout` | S |
| 3 | A3 SQLite WAL/busy_timeout | `fix/sqlite-pragmas` | S |
| 4 | A4 boot hosted service + /healthz | `refactor/boot-hostedservice` | M |
| 5 | B1 PwdProtector (GCM+PBKDF2+migration) | `security/password-protector` | M |
| 6 | B2 passphrases via env | `security/argv-passphrase` | S |
| 7 | B3 file serving hardening + ranges | `security/repo-file-serving` | S |
| 8 | B4 LastUsed throttle + B5 scopes source | `fix/apikey-hardening` | S |
| 9 | C1 problem-details mapping | `refactor/problem-details` | M |
| 10 | C2 typed responses | `refactor/typed-responses` | M |
| 11 | C3+C4+D1+D2 endpoint/service hygiene | `cleanup/hygiene` | M |
| 12 | D3+D4 image + repo hygiene | `chore/image-pinning` | S |
| 13 | D6 coverage gate 80% → 95% | `test/coverage-gate-95` | M |

Steps 1–2 and 6–7 are independent and can proceed in parallel MRs.
B1 is the only step requiring a data migration — sequence it alone (no
other MR touching those services open at the same time).

## Non-goals (explicit)

- HTTPS/HSTS stays at the reverse proxy (per initial plan, "HTTPS lives in SWAG");
  `UseForwardedHeaders` consideration documented in deployment docs instead.
- Rate limiting: single-family homelab scale; revisit if exposed publicly.
- Multi-instance scale-out: repo lock design is in-process by assumption;
  documented, out of scope.
