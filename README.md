# fdroidmgr — F-Droid Repository Manager

A self-hosted management API + Web UI around a private **F-Droid repository**.
It serves the repo itself (no nginx needed), creates app signing keys, publishes
releases, and can watch Git repositories for new tags/branches — all behind
interactive OIDC (Keycloak/Authentik/Zitadel) authentication.

```
┌────────────────┐  HTTP/JSON ───▶  ┌───────────────────────────────────────────────┐
│    Web UI      │                  │           .NET 10 API (fdroidmgr)             │
│  (no NPM,      │                  │  ├─ Apps / Releases / Keys CRUD               │
│   vanilla JS)  │                  │  ├─ Repo index generator (index.xml,          │
└────────────────┘                  │  │    index-v1/v2.json, entry.json)           │
┌────────────────┐    ─────────▶    │  └─ /fdroid/repo/** static serving            │
│  F-Droid app   │                  │                                               │
└────────────────┘                  └───────────────────────────────────────────────┘
```

## Features

- **App management** — create apps (package, name, license, description,
  website, source, tracker), upload icons/screenshots, edit metadata.
- **Key automation** — a signing keystore is generated per app (keytool, RSA
  4096, PKCS12). Key passwords are encrypted at rest with the master key
  (`FDROID_MASTER_KEY`); fingerprints are stored so releases are verified
  against the right signer.
- **Release publishing** — upload an APK; the API signs unsigned uploads with
  the app key, validates signer + monotonic versionCode (read from the fdroid
  index), drops it into the repo, writes the changelog sidecar, and delegates
  renaming + index generation + index signing to `fdroid update --rename-apks`.
  fdroid's own files (`metadata/*.yml`, `repo/index-v1.json`, `repo/*.apk`)
  are the source of truth for apps and releases.
- **OIDC sign-in (Keycloak / Authentik / Zitadel)** — interactive authorization-code
  + PKCE flow with a session cookie. The provider is configured once via a
  first-run wizard (settings persisted to `/config/appsettings.json`, client
  secret AES-encrypted under `FDROID_MASTER_KEY`). Saved settings take effect
  immediately — **no container restart is needed**, the "Sign in" button is
  live as soon as you save. F-Droid itself has no private-repo auth story; this
  API gates every write.
- **API keys** — scoped keys for pipelines/tools that need to call the API
  without an interactive login.
- **Static repo serving** — `/fdroid/repo/**` serves index.xml, index-v1/v2
  json, entry.json and APKs directly from the API (middleware-based, no nginx).

## Stack

- .NET 10 ASP.NET Core (minimal API + controllers)
- SQLite via EF Core (`/data/fdroidmgr.db`)
- Static single-page Web UI (`wwwroot/ui.html` + `ui.js` + `ui.css`) — Alpine.js
  vendored as one file, no npm build, easy to edit. Views are hash routes
  (`#stats`, `#api-keys`, `#app=<pkg>&panel=<tab>`, and list state as
  `#q=…&sort=…&page=N`), so the back button walks through the app instead of
  leaving it and every view is deep-linkable
- Tests: xunit v3 (MTP), NSubstitute, FluentAssertions, coverlet.MTP
  (**≥95% line coverage enforced in CI**, `ci/coverage-gate.sh`)

## Run

### Container (podman/docker) — drop-in

```bash
cp .env.example .env   # edit: set FDROID_MASTER_KEY (openssl rand -hex 32),
                       # and, if you have one, FDROID_REPO_HOST=<existing repo root>
docker compose up -d   # pulls the prebuilt image from Docker Hub
```

Then open **http://localhost:8082/ui**. On first boot the OAuth wizard asks for
your identity server address, client id and client secret (`config/appsettings.json`
is written on the `/config` mount, gitignored — a sample lives in
`config/appsettings.sample.json`). Saved settings take effect immediately:
sign in with your IdP, no restart needed.

For a complete walkthrough — TLS reverse proxy, OIDC setup, adopting an
**existing** repo vs. **fresh init**, and backups — see
**[docs/deployment.md](docs/deployment.md)**.

### Local (dotnet 10 SDK)

```bash
export FDROID_MASTER_KEY=$(head -c64 /dev/urandom | base64)
export FDROID_CONFIG_FILE=/config/appsettings.json   # persists the wizard output
dotnet run --project src/FdroidMgr.Api
```

## Adding your first app

1. Sign in at `/ui` (OIDC — if this is a fresh deployment, the first-run wizard
   opens; configure your IdP there, the settings apply immediately).
2. **Register app** → package name e.g. `org.example.myapp`, tick *generate
   key*.
3. **Keys** tab → a keystore was created (`/data/keys/<package>/release.keystore`).
   The password decrypt happens only at sign time (master key).
4. Build & sign your APK with that keystore, then **Releases** tab → upload.

> ⚠️ Android requires *the same* key for every update of an app. Back up
> `/data/keys` and `FDROID_MASTER_KEY` — losing them means the app can never
> be updated again.

## Browser demo

`demo/demo.sh` runs the manager against a minimal cookie-less OIDC provider
and a hand-written three-app index — no real IdP or repository needed. See
[`demo/README.md`](demo/README.md).

## API surface (all under `/api`)

| Endpoint | Description |
|---|---|
| `GET /auth/login` | start OIDC sign-in (302 → IdP) |
| `POST /auth/logout` | sign out of the session cookie |
| `GET /auth/me` | current user |
| `GET /auth/oauth/status` | whether OIDC is configured |
| `POST /auth/oauth` | one-time OIDC setup (anonymous until configured) |
| `GET/POST/PUT/DELETE /apps` | app CRUD (keyed by `packageName`) |
| `GET/POST /apps/{packageName}/releases` | list / publish release (multipart `apk`) |
| `DELETE /apps/{packageName}/releases/{versionCode}` | remove a release |
| `GET/POST /apps/{packageName}/keys` | list / generate signing keys (optional `?branch=`) |
| `GET/POST/DELETE /api-keys` | list / create / revoke API keys (interactive session only) |
| `GET/POST /apps/{packageName}/metadata` | preview / (re)generate the fdroid metadata yml |
| `POST /apps/{packageName}/icon` | upload icon |
| `GET/PUT /repo` | repo config (URL, name, description) |
| `POST /repo/index/rebuild` | force index regeneration |
| `GET /repo/status` | public health + counts |
| `GET /fdroid/repo/**` | public F-Droid repo files (no auth) |

## Querying with HLinq

List endpoints can filter, order, page, project and count **server-side** via an
[HLinq](https://github.com/npodbielski/HamsterWheel.HLinq) query string — a
URL-native LINQ query language, delivered as the `HamsterWheel.HLinq.AspNet`
NuGet package.

```http
GET /api/api-keys?where[x.name.contains(pipeline)]
GET /api/api-keys?orderBy[x.createdUtc].skip[20].take[10]
GET /api/api-keys?where[x.revokedUtc == null].count[]
```

- Clauses are `.`-separated: `where[...]`, `orderBy[...]`, `orderByDescending[...]`,
  `thenBy[...]`, `skip[n]`, `take[n]`, `count[]`, `select[...]`.
- **Additive:** with no clause the response is exactly what it always was
  (so the web UI is unaffected).
- **Capped:** without `take[]` a query returns at most `HttpDefaultMaxTakeRecords`
  (**1000**) records.
- **`count[]` counts what the clauses to its left produced.** Clauses apply left
  to right, so `where[…].count[]` is the real total but `take[1].count[]` is `1`
  (it counts the page). Ask for a total before any `take[]`.
- **Case-sensitive `contains`** on all three list endpoints, for different
  reasons: on `/api/api-keys` SQLite backs it with `instr()`, on `/api/apps`
  and the releases list it is plain `string.Contains` — `contains(pipeline)`
  matches, `contains(PIPELINE)` does not.
- Fields are the **response DTO's** fields, so `where`/`select` can never reach
  stored secrets (the key hash is not part of the projection).
- A malformed query, or one naming a field the DTO does not expose, is a
  `400 {"title": …, "status": 400}` — HLinq's `ProblemDetails`, not the app's
  `{error}` shape — never a 500.
- Build query strings with `URLSearchParams` — `[`/`]` must be percent-encoded
  by some clients and proxies.

| Endpoint | HLinq | Storage |
|---|---|---|
| `GET /api/api-keys` | ✅ | EF Core → SQLite (clauses translated to SQL) |
| `GET /api/apps` | ✅ | `index-v1.json` + `metadata/*.yml` (in-memory LINQ) |
| `GET /api/apps/{pkg}/releases` | ✅ | `index-v1.json` + changelog sidecars (in-memory LINQ) |

```http
GET /api/apps?where[x.name.contains(Notes)].orderBy[x.packageName]
GET /api/apps?where[x.license.contains(MIT)].count[]
GET /api/apps?orderByDescending[x.versionCount].thenBy[x.name].skip[10].take[10]
```
The web UI's app list uses exactly these — the filter box sends one OR'd
case-insensitive `where` across name / package / summary, and paging is
`skip[…].take[…+1]` — the extra row signals a next page without a `count[]`
request (`wwwroot/js/ui.js`).

## Notes

- **HTTP between phone↔server**: F-Droid accepts plain HTTP repos (shows a
  warning). For production put it behind TLS.
- **aapt/apksigner**: baked into the container image (Android build-tools
  35.0.0), so no host SDK mount is needed. `AAPT`/`APKSIGNER` env can still
  point at alternative binaries.
- **Auth is interactive OIDC (authorization-code + PKCE)**. The provider settings
  (`Auth:OAuthIssuer`, `Auth:OAuthClientId`, `Auth:OAuthClientSecret`, `Auth:OAuthScopes`)
  are read from `/config/appsettings.json`; the client secret is stored
  AES-encrypted under `FDROID_MASTER_KEY`.

## Documentation

- **[Deployment guide](docs/deployment.md)** — from-scratch install, reverse
  proxy, OIDC, adopting an existing repo, fresh init.
- **[Backup & restore](docs/backup-restore.md)** — what to back up and how to
  restore it (signing keys and the master key are unrecoverable if lost).
- **[Keycloak setup](docs/keycloak-setup.md)** — realm/client walkthrough for
  the OIDC first-run wizard.

## License

[MIT](LICENSE)
