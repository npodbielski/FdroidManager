# API keys as an alternative to OIDC login

> Status: IMPLEMENTED.
>
> Goal: let pipelines/tools call the manager API with a scoped API key instead
> of an interactive OIDC login.

## 1. Goal & non-goals

**Goal:** allow pipelines/tools to authenticate with an API key.

**Non-goals:** replace OIDC; add full RBAC/roles; multi-tenant isolation.
Keep it minimal but safe.

## 2. Authentication scheme (how a key authenticates)

Add a second auth handler alongside the existing cookie+OIDC flow:

- **Header:** `Authorization: Bearer fdmgr_<secret>` (standard). Also accept
  `X-API-Key` as a convenience alias.
- **Key format:** `fdmgr_` + 32 random bytes base64url (≈ 43 chars). The
  `fdmgr_` prefix is for secret-scanning + fast rejection.
- **Storage:** store only `SHA-256(key)` hex, plus a `KeyPrefix` (first ~10
  chars) for display. Constant-time comparison. Plaintext is returned **once**
  at creation.
- **"Smart" default scheme:** register a
  `PolicyScheme("SmartCookieOrApiKey")` whose `ForwardDefaultSelector` chooses:
  - `ApiKey` handler when the request has an `Authorization: Bearer` header,
  - `Cookie` handler otherwise.
  This keeps browser SSO exactly as today, but lets tools sign in via the header.
- **Critical: 401, never a 302.** Today an unauthenticated request is challenged
  by OIDC → `302 → /api/auth/login`. API clients must instead get
  `401 {"error":"unauthorized"}`. The `ApiKey` handler's `Challenge`/`Forbid`
  returns `401` JSON; the cookie handler keeps its SSO redirect. So a bad/missing
  key for a tool → clean 401, while a browser still gets the SSO redirect.

## 3. Scopes

| Scope | Grants |
|---|---|
| `read` | all `GET` endpoints (apps, releases, metadata, keys, `auth/me`) |
| `apps:write` | create/update/delete an app, icon upload, metadata write, signing-key generate/import |
| `releases:write` | publish/delete a release, edit changelog |

Rules:

- A key carries a **set** of scopes (e.g. `["read","releases:write"]`).
- `apps:write` and `releases:write` **imply `read`** (a CI step that publishes
  also needs to list apps/read the index).
- **Admin-only endpoints are NOT given to API keys** by default:
  `POST /api/repo/init`, `GET/PUT /api/repo`, `POST /api/repo/index/rebuild`,
  OAuth config, and API-key management themselves stay **interactive-cookie-only**.
  If a pipeline must rebuild the index later, add an `admin` scope then.

### Endpoint → scope mapping

| Endpoint | Scope |
|---|---|
| `GET /api/apps`, `GET /api/apps/{pkg}`, `GET …/releases`, `GET …/keys`, `GET …/metadata`, `GET /api/auth/me` | `read` |
| `POST /api/apps`, `PUT/DELETE /api/apps/{pkg}`, `POST …/{pkg}/metadata`, `POST …/{pkg}/icon`, `POST …/keys`, `POST …/keys/import` | `apps:write` |
| `POST/DELETE …/releases`, `PATCH …/releases/{vc}` | `releases:write` |
| `POST /api/repo/init`, `GET/PUT /api/repo`, `POST /api/repo/index/rebuild`, `/api/auth/oauth` | cookie interactive only (optionally `admin` scope later) |

Implementation: replace bare `[Authorize]` with `[Authorize(Policy = "…")]`, and
stamp interactive (cookie) sessions with **all** scopes via an
`IClaimsTransformation`, so one uniform mechanism (`scope` claim) drives everything.

## 4. Data model (new tables)

**`ApiUser`**

- `Id`, `Subject` (OIDC `sub`; every user comes from the IdP — service accounts
  are out of scope), `Username`, `CreatedUtc`

**`ApiKey`**

- `Id`, `UserId` (FK), `Name` ("CI pipeline"), `KeyHash`, `KeyPrefix`,
  `Scopes` (comma-joined), `CreatedUtc`, `LastUsedUtc`, `ExpiresUtc`, `RevokedUtc`

DB migration: `EnsureCreatedAsync` won't add new tables to an **existing** DB,
so add a small `EnsureTableAsync` (`CREATE TABLE IF NOT EXISTS …`) helper
mirroring the existing `EnsureColumnAsync`, and call it at startup.

## 5. API-key management endpoints (new `ApiKeysController`)

Cookie-auth only (an API key can't mint more keys):

- `GET /api/api-keys` — the current user's keys (id, name, prefix, scopes,
  created, last-used, revoked). **Never** returns the secret.
- `POST /api/api-keys` — `{ name, scopes[], expiresDays? }` → `{ id, key }`
  (secret shown once).
- `DELETE /api/api-keys/{id}` — revoke own key.

Each OAuth user's keys are keyed to their `sub`, so "each user has their own
key" holds automatically.

## 6. Service users — OUT OF SCOPE for now

System users not connected to OAuth (service accounts that authenticate only
via API key) are **out of scope for this change**. Defer them to a separate
future plan if a need materializes; the core API-key design (subject-bound keys
managed per OIDC user) already covers the pipeline/tool use case with a key
created under the existing login.

## 7. UI

- New **"API keys"** page (link in the authbar when signed in): list, create
  (name + scope checkboxes), reveal-once secret with a copy button, revoke.
- Service accounts / system users are out of scope (see §6).

## 8. Files that would change

- `src/FdroidMgr.Api/Services/ApiKeyAuthenticationHandler.cs` *(new)* — bearer → DB lookup → claims.
- `src/FdroidMgr.Api/Services/ApiKeyService.cs` *(new)* — generate/hash/validate/scopes.
- `src/FdroidMgr.Api/Services/ScopeClaimsTransformer.cs` *(new)* — stamp full scopes onto cookie sessions.
- `src/FdroidMgr.Api/Models/ApiUser.cs`, `Models/ApiKey.cs`, `Models/Dtos.cs` *(add records)*.
- `src/FdroidMgr.Api/Data/FdroidDb.cs` — DbSets + `EnsureTableAsync`.
- `src/FdroidMgr.Api/Program.cs` — register `.AddScheme<...>("ApiKey")`, policy scheme, `.AddPolicy(...)` for each scope, DB init.
- `src/FdroidMgr.Api/Controllers/ApiKeysController.cs` *(new)* + scope attributes on `AppsController`, `ReleasesController`, `KeysController`, `AuthController`.
- `wwwroot/js/ui.js` + `ui.html` — API keys page.
- Tests: `ApiKeyAuthTests.cs` *(new)* — valid/invalid/revoked/expired key, `401`-not-`302`, per-scope enforcement, hash-only storage; update `ApiIntegrationTests` if needed.

## 9. Verification & rollout

- `dotnet build` (0 errors), `dotnet test` (existing tests + new).
- Manual: create a key in the UI → `curl -H "Authorization: Bearer fdmgr_…"`
  against `GET /api/apps` (200), `POST /api/apps` (200), `POST …/releases`
  (403 without `releases:write` / 200 with), and confirm a wrong key returns
  `401` JSON (not a 302).
- Rebuild + redeploy `podman compose build fdroidmgr && podman compose up -d fdroidmgr`,
  confirm healthy.
