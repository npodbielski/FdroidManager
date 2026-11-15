# Deployment

> How to run **fdroidmgr** in a real environment. This guide assumes you found
> the project (by searching for "fdroid manager" or similar) and know nothing
> about the machine it will run on. It describes a clean, from-scratch install
> and then how to point the manager at a repo you may already have.

---

## 1. What fdroidmgr is (60-second version)

fdroidmgr is a self-hosted web UI + HTTP API wrapped around an F-Droid
repository. It does the things `fdroid update` cannot do on its own:

- sign APKs and generate per-app signing keys,
- edit the human-authored metadata (`metadata/<package>.yml`),
- publish/delete releases and write changelogs,
- serve the repository (`/fdroid/repo/**`) and the management UI (`/ui`),
- gate every write behind interactive OIDC sign-in (Keycloak/Authentik/Zitadel).

It is **not** a fork or rewrite of fdroidserver. It runs the real
`fdroid update` command inside the container and treats fdroid's own files
(`metadata/*.yml`, `repo/index-v1.json`, `repo/*.apk`) as the source of truth.

---

## 2. What you need

- A machine that can run **containers** (podman or docker both work), or the
  **.NET 10 SDK** to run without containers.
- A **domain + TLS** reverse proxy in front (recommended; F-Droid warns about
  plain-HTTP repos and the OIDC login needs a stable public URL).
- An **OIDC identity provider** (Keycloak, Authentik, Zitadel, …) with a client
  registered for this app.
- An **existing F-Droid repository** to adopt, *or* an empty directory where the
  manager can bootstrap a brand-new one.
- Nothing else: **Android build-tools** (`aapt`, `apksigner`) are **bundled
  inside the image**, so the manager can parse and verify APKs out of the box.

---

## 3. Architecture: how it fits around the repository

The container has a few well-known paths. You bind your host directories onto
them:

| Container path | Purpose | Host path you choose |
|---|---|---|
| `/data` | SQLite DB, DP key ring, `/data/keys` (app signing keys) | e.g. `/srv/fdroid-mgr/data` |
| `/config` | `appsettings.json` (OIDC provider settings) | e.g. `/srv/fdroid-mgr/config` |
| `/data/fdroidrepo` | the fdroid repo **root** (config.yml + keystore.p12 + metadata/ + repo/) | your existing repo dir, or a fresh empty dir |
| `/data/fdroidrepo/public` | the *public mirror* — the only tree `/fdroid/repo/**` serves | (none — on the `/data` mount) |
| `/opt/android-sdk` | Android build-tools (aapt/apksigner) — **baked into the image** | (none — no mount needed) |

The app listens on **8080** inside the container.

There are two key concepts:

- **`FDROID_REPO_ROOT` / `/data/fdroidrepo`** — the working directory where
  `fdroid update` runs. It must contain `config.yml` and `metadata/`.
- **`FDROID_REPO_PATH` / `/data/fdroidrepo/repo`** — the *output* directory
  (the signed `index-v1.json`, `index.xml`, and the APKs). This is what the
  manager serves under `/fdroid/repo/**`.
- **`FDROID_REPO_PUBLIC_PATH` / `/data/fdroidrepo/public`** — the *public mirror*
  directory, the default in the image and in the shipped compose. Every successful
  `fdroid update` copies the output files into it (minus anything the denylist
  hides) and `/fdroid/repo/**` serves **only** from there, so the working tree —
  `config.yml`, keystores, `metadata/`, the SQLite file — is never reachable from
  the HTTP surface at all. It sits on the `/data` volume; the manager owns its
  contents and removes files that disappear from the output directory. Set it
  **empty** to serve the output directory directly (the pre-mirror behaviour that
  existing installs may rely on) — the denylist still applies then. Upgrading an
  install that had it off is safe: the index rebuild at startup stages the mirror
  before the first client request.

### Adopt an existing repo vs. create a new one

- **Existing repo** — just mount the whole existing repo tree read-write at
  `/data/fdroidrepo` and set `FDROID_REPO_PATH=/data/fdroidrepo/repo`. The
  manager reuses your existing `keystore.p12` (the repo signing key clients
  already trust) and `config.yml`. **Do not** run "initialize repository" — that
  is only for empty directories.
- **Fresh repo** — mount an empty directory at `/data/fdroidrepo`, then use the
  "Initialize repository" step (UI or `POST /api/repo/init`) after signing in.
  The manager runs `fdroid init` and generates a new repo signing key.

---

## 4. Build or pull the image

The image is fully self-contained (ASP.NET runtime + JDK for `keytool` +
Python + fdroidserver + Android build-tools + curl + git).

Pull the prebuilt image from Docker Hub:

```bash
podman pull docker.io/npodbielski/fdroidmgr:latest
# or: docker pull npodbielski/fdroidmgr:latest
```

Or build it yourself from this repository:

```bash
podman build -f Containerfile -t fdroidmgr:latest .
# or: docker build -f Containerfile -t fdroidmgr:latest .
```

---

## 5. Configuration

Configuration is passed as environment variables. The required ones and the
commonly used optional ones are listed below.

### 5.1 The master key (required, stable)

`FDROID_MASTER_KEY` encrypts every app signing-key password at rest. Generate
it once and keep it forever:

```bash
openssl rand -hex 32
```

> ⚠️ **Do not rotate this key after first use.** Changing it makes all stored
> encrypted secrets (signing-key passwords) undecryptable. Back
> it up with your data.

### 5.2 Environment variables

| Variable | Required | Default | Meaning |
|---|---|---|---|
| `FDROID_MASTER_KEY` | yes | — | ≥32 hex/random chars; encrypts secrets at rest |
| `FDROID_REPO_URL` | yes* | `http://localhost:5000/fdroid/repo` | Canonical public repo URL shown to clients |
| `FDROID_REPO_NAME` | no | `My F-Droid Repository` | Repo name shown in the F-Droid client |
| `FDROID_PUBLIC_URL` | recommended | — | Public base URL (e.g. `https://apps.example.com`); used for the OIDC redirect and to derive the repo URL. Set this when behind a TLS reverse proxy. |
| `FDROID_REPO_ROOT` | yes (container) | `/data/fdroidrepo` | Working dir for `fdroid update` (holds `config.yml` + `metadata/`) |
| `FDROID_REPO_PATH` | yes (container) | `<cwd>/repo` | Output dir (index + APKs); the source that staging copies from |
| `FDROID_REPO_PUBLIC_PATH` | no | `/data/fdroidrepo/public` (image/compose) | Public mirror dir served at `/fdroid/repo/**`; when empty/unset the output dir is served directly |
| `FDROID_KEY_PATH` | no | `<cwd>/keys` | Where per-app signing keystores live |
| `FDROID_DB` | no | `Data Source=fdroidmgr.db` | SQLite connection string |
| `FDROID_CONFIG_FILE` | no | `/config/appsettings.json` | Where the OIDC wizard writes its settings |
| `FDROID_DATAPROTECTION_PATH` | no | ephemeral | Where the ASP.NET key ring persists (survives restarts) |
| `FDROID_REQUIRE_REPO_AUTH` | no | `false` | When `true`, `/fdroid/repo/**` (index + APKs) requires an API key or a session |
| `FDROID_PROCESS_TIMEOUT_SECONDS` | no | `900` (`fdroid`), `60` (other tools) | Kill-and-`exit 124` timeout for external processes (single value overrides both) |
| `AAPT` / `APKSIGNER` | no | from `ANDROID_HOME` | Explicit paths to the Android tools |
| `ANDROID_HOME` | no | `/opt/android-sdk` | SDK root for aapt/apksigner (bundled in the image) |

\* `FDROID_REPO_URL` can be left on the default during setup; set it to the real
public URL before handing the repo to clients.

### 5.3 Example `.env`

```ini
FDROID_MASTER_KEY=<openssl rand -hex 32 output>
FDROID_REPO_URL=https://apps.example.com/fdroid/repo
FDROID_REPO_NAME=My F-Droid Repository
FDROID_PUBLIC_URL=https://apps.example.com
FDROID_REQUIRE_REPO_AUTH=false
```

---

## 6. OIDC (identity provider) setup

### 6.1 Create a client in your IdP

- Grant type: **authorization code + PKCE**.
- Client type: **public** (no client secret). The manager always uses PKCE
  and never sends a `client_secret`.
- Redirect/callback URI: **`https://apps.example.com/signin-oidc`** (the
  `FDROID_PUBLIC_URL` plus `/signin-oidc`).
- Scopes: `openid profile email` (add more as needed).

### 6.2 Provide the settings (two options)

**Option A — write the file yourself** at `<config-mount>/appsettings.json`:

```json
{
  "Auth": {
    "OAuthIssuer": "https://id.example.com/realms/fdroid",
    "OAuthClientId": "fdroidmgr",
    "OAuthScopes": "openid profile email"
  }
}
```

The OIDC client is a public PKCE client: there is no client secret.

**Option B — leave `/config` empty** and use the built-in first-run wizard: open
`https://apps.example.com/ui`, fill in the issuer / client id / scopes, and
save — the settings take effect immediately, **no restart needed**
(the OpenIdConnect handler reads the provider config live on every request).

---

## 7. Run it

The repository ships a ready `docker-compose.yml` and a tracked `.env.example`.
Quick start:

```bash
cp .env.example .env      # edit: set FDROID_MASTER_KEY, and FDROID_REPO_HOST if adopting
                          # an existing repo (keep the default ./fdroidrepo for a fresh install)
docker compose up -d      # pulls the prebuilt image from Docker Hub
```

### 7.1 docker/podman compose

The shipped compose is equivalent to the explicit example below (host paths are
`.env`-driven here for clarity):

```yaml
services:
  fdroidmgr:
    image: ${FDROID_IMAGE:-docker.io/npodbielski/fdroidmgr:latest}   # pulled from Docker Hub
    # build:
    #   context: .
    #   dockerfile: Containerfile
    container_name: fdroidmgr
    restart: unless-stopped
    ports:
      - "8082:8080"          # host port is arbitrary; pick what your proxy forwards to
    volumes:
      - ./data:/data                            # DB + keys + key ring
      - ./config:/config                        # appsettings.json
      - /srv/fdroid/repo:/data/fdroidrepo       # existing (or empty) repo root
      # aapt/apksigner are baked into the image — no SDK mount
    environment:
      FDROID_CONFIG_FILE: /config/appsettings.json
      FDROID_MASTER_KEY: ${FDROID_MASTER_KEY:?set FDROID_MASTER_KEY}
      FDROID_REPO_URL: ${FDROID_REPO_URL:-https://apps.example.com/fdroid/repo}
      FDROID_REPO_NAME: ${FDROID_REPO_NAME:-My F-Droid Repository}
      FDROID_PUBLIC_URL: ${FDROID_PUBLIC_URL:-https://apps.example.com}
      FDROID_REPO_ROOT: /data/fdroidrepo
      FDROID_REPO_PATH: /data/fdroidrepo/repo
      FDROID_REPO_PUBLIC_PATH: ${FDROID_REPO_PUBLIC_PATH:-/data/fdroidrepo/public}
      FDROID_KEY_PATH: /data/keys
      FDROID_DB: "Data Source=/data/fdroidmgr.db"
      FDROID_DATAPROTECTION_PATH: /data/dp-keys
      FDROID_REQUIRE_REPO_AUTH: ${FDROID_REQUIRE_REPO_AUTH:-false}
```

Start it:

```bash
docker compose up -d
# or: podman compose up -d
```

### 7.2 Plain `podman run` (no compose)

```bash
podman run -d --name fdroidmgr -p 8082:8080 \
  -v /srv/fdroid-mgr/data:/data \
  -v /srv/fdroid-mgr/config:/config \
  -v /srv/fdroid/repo:/data/fdroidrepo \
  -e FDROID_CONFIG_FILE=/config/appsettings.json \
  -e FDROID_MASTER_KEY=<master-key> \
  -e FDROID_REPO_URL=https://apps.example.com/fdroid/repo \
  -e FDROID_REPO_NAME="My F-Droid Repository" \
  -e FDROID_PUBLIC_URL=https://apps.example.com \
  -e FDROID_REPO_ROOT=/data/fdroidrepo \
  -e FDROID_REPO_PATH=/data/fdroidrepo/repo \
  -e FDROID_REPO_PUBLIC_PATH=/data/fdroidrepo/public \
  -e FDROID_KEY_PATH=/data/keys \
  -e FDROID_DB="Data Source=/data/fdroidmgr.db" \
  -e FDROID_DATAPROTECTION_PATH=/data/dp-keys \
  -e FDROID_REQUIRE_REPO_AUTH=false \
  fdroidmgr:latest
```

### 7.3 Local run (no container)

```bash
export FDROID_MASTER_KEY=$(openssl rand -hex 32)
export FDROID_REPO_ROOT=/srv/fdroid/repo          # existing repo root, or a fresh dir
export FDROID_REPO_PATH=/srv/fdroid/repo/repo
export FDROID_KEY_PATH=/srv/fdroid-mgr/keys
export FDROID_CONFIG_FILE=/srv/fdroid-mgr/config/appsettings.json
dotnet run --project src/FdroidMgr.Api
```

---

## 8. Reverse proxy (recommended)

If the app is behind nginx (or any TLS terminator), forward these locations to
the container's 8080 (here `127.0.0.1:8082` is the host port):

```nginx
location /            { proxy_pass http://127.0.0.1:8082; }   # /ui, /api, /signin-oidc, /fdroid
```

A more explicit split:

```nginx
location /ui          { proxy_pass http://127.0.0.1:8082; }
location /api         { proxy_pass http://127.0.0.1:8082; }
location /signin-oidc { proxy_pass http://127.0.0.1:8082; }
location /fdroid      { proxy_pass http://127.0.0.1:8082; }
```

Notes:

- Keep `proxy_set_header Host $host;` and forward `X-Forwarded-*` so redirects
  and `FDROID_PUBLIC_URL` derivation stay correct.
- The F-Droid client talks to the repo over plain HTTP (possibly with Basic auth).
  If `FDROID_REQUIRE_REPO_AUTH=true`, users add the repo as
  `https://user:apikey@apps.example.com/fdroid/repo` — the Basic credentials are
  an API key created in the manager UI, not a password.

---

## 9. First-run steps

1. Open `https://apps.example.com/ui`.
2. If OIDC is unconfigured you see only the wizard — fill it in and save
   (takes effect immediately, no restart).
3. Sign in with your IdP.
4. **Existing repo?** → nothing else to do; the dashboard should already list
   the apps/releases found in `repo/index-v1.json`. Skip to step 6.
5. **Fresh empty repo?** → the UI shows "Initialize repository": pick a name and
   (optionally) a key alias, submit. The manager runs `fdroid init` + a first
   `fdroid update` and generates the repo signing key.
6. Register your first app (package name, name, summary, license, **generate
   key**), then upload an APK from the app's **Releases** tab.

> The APK's `versionCode` must be strictly greater than the previous release —
> the manager enforces this automatically. `GET
> /api/apps/{package}/releases/next-version` suggests the next
> `versionName`/`versionCode` for your build pipeline to stamp into the APK.

---

## 10. Verify it works

```bash
# liveness (public, no DB/IO; bootCompleted=false only while the
# boot-time index rebuild is still running)
curl http://localhost:8080/healthz

# repo status (public, reports whether the index is healthy)
curl https://apps.example.com/api/repo/status

# private repo: this returns 401 without credentials when
# FDROID_REQUIRE_REPO_AUTH=true, 200 with them
curl -u "user:fdmgr_<apikey>" https://apps.example.com/fdroid/repo/index.xml

# the index-v1.json the manager itself reads
curl -u "user:fdmgr_<apikey>" https://apps.example.com/fdroid/repo/index-v1.json
```

---

## 11. Backups

Back these up (in order of importance):

1. The repo signing key + config — `<repo-root>/keystore.p12` and
   `<repo-root>/config.yml` (the trust anchor clients already have).
2. `<data>/keys` — per-app signing keystores (Android requires the same key for
   every update of an app).
3. `FDROID_MASTER_KEY` — decrypts all of the above.
4. `<data>/fdroidmgr.db` — repo config + API keys.
5. `<data>/dp-keys` — OIDC session key ring (optional; losing it only forces a
   re-login).

---

## 12. Upgrading

1. Rebuild/pull the new image.
2. `podman compose up -d` (or recreate the container) with the **same** mounts
   and the **same** `FDROID_MASTER_KEY`.
3. The app runs idempotent schema migrations on boot; no manual DB steps.

---

## 13. Troubleshooting / common footguns

- **"Initialize repository" on an existing repo** — don't. It is for empty
  directories only; running it on a real repo backs it up and generates a *new*
  repo signing key, which orphans every client that trusted the old index.
- **Rotating `FDROID_MASTER_KEY`** — breaks decryption of the OIDC client
  secret and all signing-key passwords. Treat it like a password vault master
  password: set once, back up, never change.
- **APK parse fails / no version info** — `aapt` isn't found. The image bundles
  build-tools 35.0.0; if you mounted your own SDK over `/opt/android-sdk` it
  must contain `build-tools/35.0.0/aapt` (or set `AAPT`/`APKSIGNER` to point
  at the binaries).
- **"Sign in" loops back or 500s** — the callback URI in the IdP doesn't match
  `FDROID_PUBLIC_URL` + `/signin-oidc`, or the issuer is unreachable (the
  discovery fetch fails). OIDC settings themselves are live — no restart is
  involved.
- **F-Droid client can't add the repo (401)** — you set
  `FDROID_REQUIRE_REPO_AUTH=true` but added the repo without credentials. Use
  the `https://user:apikey@host/fdroid/repo` form.
- **`fdroid update` fails inside the container** — check that the mounted repo
  root is writable and contains a valid `config.yml` (existing repo) or that you
  ran initialize (fresh repo).
