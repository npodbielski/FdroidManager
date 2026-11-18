# API reference

Everything the web UI does is available over HTTP under `/api`. The UI is a
client of this API like any other; nothing is available only to it.

- **[Run it locally](development.md)** — how to start the API from source.
- **[Deployment](deployment.md)** — TLS, reverse proxy, OIDC setup.

## Authentication

Two ways, and they are independent:

1. **Interactive session** — sign in through the web UI with OIDC. The browser
   gets a session cookie; it authorises everything the UI does.
2. **API key** — a scoped bearer token for pipelines and scripts. Created in the
   UI under **API keys**, shown once at creation, revocable at any time.

Scopes:

| Scope | Grants |
|---|---|
| `read` | list apps, releases, signing keys, metadata |
| `apps:write` | create/update/delete apps, icons, signing keys (implies `read`) |
| `releases:write` | publish/delete releases, edit changelogs (implies `read`) |

## Endpoints

| Endpoint | Description |
|---|---|
| `GET /auth/login` | start OIDC sign-in (302 → IdP) |
| `POST /auth/logout` | end the session |
| `GET /auth/me` | current user |
| `GET /auth/oauth/status` | whether OIDC is configured |
| `POST /auth/oauth` | one-time OIDC setup (anonymous until configured) |
| `GET /apps` | list apps |
| `POST /apps` | create an app (`generateKey` to also create a keystore) |
| `GET/PUT/DELETE /apps/{packageName}` | read / update / delete one app |
| `GET/POST /apps/{packageName}/releases` | list / publish a release (multipart field `apk`) |
| `GET /apps/{packageName}/releases/next-version` | next `versionName` / `versionCode` |
| `DELETE /apps/{packageName}/releases/{versionCode}` | remove a release |
| `GET/POST/DELETE /apps/{packageName}/releases/{versionCode}/changelog` | changelog sidecar |
| `GET/POST /apps/{packageName}/keys` | list / generate signing keys (optional `?branch=`) |
| `POST /apps/{packageName}/keys/import` | import an existing keystore |
| `GET/POST /apps/{packageName}/metadata` | read / (re)generate the fdroid metadata yml |
| `POST /apps/{packageName}/icon` | upload an icon |
| `GET/POST/DELETE /api-keys` | list / create / revoke API keys (interactive session only) |
| `GET/PUT /repo` | repository config (URL, name, description) |
| `POST /repo/init` | initialise a fresh repository |
| `GET /repo/init/status` | whether the repository has been initialised |
| `POST /repo/index/rebuild` | force index regeneration |
| `GET /repo/status` | public health and counts |
| `GET /stats/downloads` | download statistics |
| `GET /fdroid/repo/**` | the F-Droid repository itself (public, no auth) |

## Queries

List endpoints accept an [HLinq](https://github.com/npodbielski/HamsterWheel.HLinq)
query string — filtering, ordering, paging, projection and counting happen
server-side.

```http
GET /api/api-keys?where[x.name.contains(pipeline)]
GET /api/api-keys?orderBy[x.createdUtc].skip[20].take[10]
GET /api/api-keys?where[x.revokedUtc == null].count[]
```

Clauses are `.`-separated and apply left to right:

| Clause | Effect |
|---|---|
| `where[…]` | keep rows matching the predicate |
| `orderBy[…]` / `orderByDescending[…]` | sort |
| `thenBy[…]` | secondary sort |
| `skip[n]` / `take[n]` | page |
| `count[]` | count what the clauses to its left produced |
| `select[…]` | project fields |

Rules worth knowing:

- **Additive.** With no clause the response is exactly what it always was, so
  clients that do not use queries are unaffected.
- **Capped.** Without `take[]` a query returns at most 1000 records.
- **`count[]` counts the pipeline so far.** `where[…].count[]` gives the real
  total; `take[1].count[]` gives `1`. Ask for the total before any `take[]`.
- **`contains` is case-sensitive.** `contains(pipeline)` matches,
  `contains(PIPELINE)` does not.
- **Only response fields are reachable**, so a query cannot touch stored secrets.
- A malformed query, or one naming a field that the response does not expose, is
  a `400` from HLinq's own `ProblemDetails` shape — never a `500`.
- Build query strings with `URLSearchParams`; `[` and `]` need percent-encoding
  in some clients and proxies.

| Endpoint | Queryable | Backed by |
|---|---|---|
| `GET /api/api-keys` | ✅ | EF Core → SQLite (clauses become SQL) |
| `GET /api/apps` | ✅ | `index-v1.json` + `metadata/*.yml` (in-memory) |
| `GET /api/apps/{pkg}/releases` | ✅ | `index-v1.json` + changelog sidecars (in-memory) |

```http
GET /api/apps?where[x.name.contains(Notes)].orderBy[x.packageName]
GET /api/apps?where[x.license.contains(MIT)].count[]
GET /api/apps?orderByDescending[x.versionCount].thenBy[x.name].skip[10].take[10]
```

The web UI uses these same queries: the filter box sends one OR'd case-insensitive
`where` across name, package and summary, and paging asks for `take[pageSize+1]`
so the extra row signals a next page without a separate count request.

## What a client can reach

`GET /fdroid/repo/**` serves the repository exactly as a phone sees it: the
generated index (`index.xml`, `index-v1.json`, `index-v2.json`, `entry.json`) and
the APKs. It reads from a separate public mirror directory, so the rest of the
tree on disk — `config.yml`, the keystores, the SQLite file — is not reachable
over HTTP at all. What a client does need from it is exposed as data rather than
as files: repository configuration and app metadata through their own endpoints,
signing keys as alias and fingerprint only, never as a password or a keystore.

Layout of those directories is a deployment concern and belongs to
[deployment.md](deployment.md).
