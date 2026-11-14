# fdroidmgr — Initial Plan & Progress Log

> Living document: what we set out to build, what is done, and what is in flight.
> Last updated: 2026-08-26 (feat/web-ui branch)

---

## 1. Goal

Run a **private F-Droid repository** on this machine with a management front-end:

- .NET 10 API + Web UI to manage apps, signing keys, releases, repo metadata
- Serve the F-Droid repo itself (index.xml + APKs) without nginx
- OAuth2/JWT auth (F-Droid has no private-repo auth story)
- SQLite storage, containerized (rootless podman), GitLab CI with tests
  and an **80% coverage gate**

## 2. What is DONE (verified)

### 2.1 Working F-Droid repo (before the project)
- `/srv/fdroid/repo` — classic fdroidserver repo, two sample apps published
  (Hello World `org.example.helloworld`, Second App `org.example.secondapp`), both
  installable from the F-Droid client.
- Root cause of the earlier "invalid metadata" bug: hand-built APKs lacked
  `<uses-sdk>` → minSdk/targetSdk defaulted to **3**; fixed by rebuilding with
  `minSdk 21 / targetSdk 34` + real DEX (verified in client).
- Scripts: `add-apk.sh`, `build-simple-apk.sh`, `run-repo.sh`, nginx serving on
  port 8081 at `/fdroid/repo`.

### 2.2 fdroidmgr project (`/srv/fdroid-mgr`, git repo)

#### API (net10.0, EF Core + SQLite)
- **App CRUD**, releases (multipart APK upload), icons/screenshots, metadata
- **Key automation**: `keytool` RSA-4096 PKCS12 per app (+per-branch), AES-encrypted
  key passwords under `FDROID_MASTER_KEY`, signer-fingerprint verification
- **Git watcher**: clone URL + branch/tag patterns (`v*`) → `PendingRelease` candidates
  - Detection works (unit-tested against a real local git repo)
  - Approve/reject flow wired through UI:
    - API: `GET /api/apps/{id}/git/pending`, `POST .../approve`, `POST .../reject`
    - UI: pending releases list in git watch panel with approve/reject buttons
- **Repo index generation**: delegates to `fdroid update` (fdroidserver)
  - Installed fdroidserver in container (Python3 + pip)
  - Mounts fdroid repo at `/data/repo` inside container
  - RepoIndexService calls `fdroid update` directly
  - All signing, manifest generation, JAR signing handled by proven fdroidserver tools
- **Auto-signing**: unsigned APKs are automatically signed with the app's key on upload
  - ReleaseService detects unsigned APKs and signs them via `KeyService.SignApkAsync`
  - Pre-signed APKs are verified against the app's registered key
- **Static file serving**: `/fdroid/repo/**` served from the API (no nginx needed)
  - RepoIndexService generates index.html with QR code for mobile setup
  - All standard F-Droid repo files served: index.xml, index-v1/v2.json, entry.json,
    index.jar, index-v1.jar, entry.jar, index.html, index.css, index.png
  - Density-specific icon directories (icons-120/160/240/320/480/640)
- **JWT auth** (local mode) + external OIDC option

#### Web UI
- Single-file vanilla JS (`wwwroot/ui.html` + `ui.js` + `ui.css`) — no npm
- Features:
  - Login/authentication
  - App management (create, delete, view releases)
  - Git watch configuration (clone URL, branches, tag patterns)
  - Pending release approval/rejection
  - Signing key management (view, regenerate)
  - Link to F-Droid repo index.html with QR code

#### Tests
- **47 passing** (xunit v3 MTP, NSubstitute, FluentAssertions, coverlet.MTP)
- Local coverage **87.5% line** (CI shows 81.9–87.5% depending on run)
- Gate: `ci/coverage-gate.sh` @ **0.80**
- Tests verify:
  - RepoIndexService generates all required F-Droid repo files
  - Density-specific icon directories are created
  - JAR files contain proper MANIFEST.MF with SHA-256 digests

#### Container
- `Containerfile` + `docker-compose.yml`; smoke-tested in podman
- Installs: .NET 10 SDK, git, JDK 17 (for keytool/apksigner), Python3, fdroidserver
- Mounts:
  - `./data:/data` — app data (SQLite DB, signing keys, repo files)
  - `/srv/fdroid-mgr/android-sdk:/opt/android-sdk:ro` — Android SDK
  - `/srv/fdroid/repo/repo:/data/repo:ro` — F-Droid repo files
- Status/index/login/app-create/key-gen all work
- Repo accessible at http://localhost:8082/fdroid/repo/

#### CI pipeline (`.gitlab-ci.yml`, hca-style)
- Stages: build → test(+gate) → validate
- **image-build job**: runs on `docker-privileged` runner
  - Uses `quay.io/buildah/stable` image with `STORAGE_DRIVER=vfs`
  - Builds OCI image and pushes to GitLab registry
  - Saves image as CI artifact for validation
- **E2E test script**: `ci/e2e-test.sh` tests full loop
  - Create app → gen key → publish APK → verify in repo
  - Can be run manually against a running instance

## 3. What is IN FLIGHT / BLOCKED

### 3.1 Coverage variance
- **CI coverage 81.9% vs local 87.5%** — variance from Release vs Debug builds
  in keytool/integration paths; both above the 80% gate, so not urgent.

### 3.2 External OIDC mode
- ~~Not tested end-to-end~~ — DONE: interactive OIDC/Keycloak login implemented
  (`plans/done/oauth-login.md`), incl. a first-run wizard, unit/API tests, an
  in-process fake-IdP redirect-flow test and an `e2e-oidc` CI job against
  oidc-server-mock.

### 3.3 Registry push
- the published image blocked on image-build job running on
  capable runner. Deploy token created and set as `DEPLOY_REGISTRY_USER` /
  `DEPLOY_REGISTRY_PASSWORD` project variables; local `podman login` with it
  → **Login Succeeded** (credentials work).

## 4. Environment / secrets to preserve (CRITICAL)

- `/srv/fdroid-mgr/keys/` — app signing keystores (losing them = apps can never
  be updated). Includes `app-release.keystore` + `README.txt` (demo password).
- `FDROID_MASTER_KEY` — encrypts key passwords at rest (set in compose/CI).
- Deploy token `gitlab+deploy-token-10` (write_registry) —
  `DEPLOY_REGISTRY_USER`/`DEPLOY_REGISTRY_PASSWORD` in project CI variables.
- `/srv/fdroid/repo/keystore.p12` — repo index signing key (separate from
  app keys).

## 5. Run / deploy quick reference

```bash
# local dev
export FDROID_ADMIN_PASS=... FDROID_JWT_KEY=... FDROID_MASTER_KEY=...
dotnet run --project src/FdroidMgr.Api          # → http://localhost:5000/ui

# container (rootless podman)
cd /srv/fdroid-mgr/mgr
podman compose up -d --build                     # → http://localhost:8082/ui

# Access points
# Web UI:      http://localhost:8082/ui
# API:         http://localhost:8082/api
# F-Droid repo: http://localhost:8082/fdroid/repo
# Repo status: http://localhost:8082/api/repo/status

# E2E test
./ci/e2e-test.sh [BASE_URL]
```

## 6. Next steps (ordered)

1. ~~**image-build is green**~~ — DONE: job runs on docker-privileged runner with buildah.
2. ~~**Wire Git-watch "approve → publish"**~~ — DONE: API endpoints + UI for pending releases.
3. ~~**E2E script**~~ — DONE: `ci/e2e-test.sh` tests full loop.
4. ~~**Simplify repo generation**~~ — DONE: use fdroidserver directly instead of reimplementing in C#.
5. ~~**Real OIDC login**~~ — DONE: interactive OIDC wizard + sign-in (`plans/done/oauth-login.md`).
6. ~~**App metadata file generation**~~ — DONE (`plans/done/app-metadata.md` +
   `plans/done/app-metadata-edit-ui.md`): `POST/GET /api/apps/{id}/metadata` +
   per-app metadata UI, so a published APK actually appears in `fdroid
   update`'s index.
7. ~~**Optional: HTTPS behind a reverse proxy**~~ — DONE at the infra layer,
   not in this repo: SWAG (Let's Encrypt nginx) on the host fronts
   the service. Moving the proxy onto the machine the service runs on is
   tracked in a separate project; the app keeps serving plain HTTP behind it.

---

*Status legend: DONE = verified working · BLOCKED = needs infra/decision · IN FLIGHT = active work*
