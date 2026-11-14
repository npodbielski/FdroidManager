# Agent instructions

Standard rules for AI coding agents (and humans) working in this repository.

## Git workflow

- **Never merge a merge request without explicit approval** from the maintainer.
- **Never push directly to `master`.** All changes go through merge requests:
  open a feature branch, push it, and open an MR targeting `master`.
- Keep MRs focused; a single logical change per MR. Reference the MR in the
  branch name (e.g. `fix/review-<commit>-comments`).

Flow: `feature branch` → MR → `master`.

## Where docs live

- `plans/` — agent plans (open). Finished plans move to `plans/done/`.
  Index: `PLANS.md`. New plan documents go here, not in `docs/`.
- `docs/` — user/operator documentation only (deployment, backup, IdP setup).
  Things a real user of the service would read.
- `docs/dev/` — technical implementation notes useful to agents and
  contributors but not to users (e.g. the UnoCSS stylesheet vocabulary).

## Pipeline

Stages: `build` → `test` → `validate` → `deploy` → `release`.

- `build` — builds `FdroidMgr.slnx` (Release).
- `style-check` — hard gate: `dotnet format FdroidMgr.slnx style --verify-no-changes --severity error`
  (rules at `:error` severity from `.editorconfig` — in particular `csharp_prefer_braces`,
  no brace-less single-statement control statements).
- `tests` — unit tests with coverlet + a 95% line-coverage gate (`ci/coverage-gate.sh`).
- `format` — whitespace/whitespace-normalization check (advisory).
- `image-build` — builds the OCI image with buildah (docker-privileged runner) and saves a
  docker-archive artifact.
- `e2e-oidc` — OIDC login E2E against a mock IdP (docker-privileged runner).
- `unocss-check` — regenerates `wwwroot/css/unocss.css` and fails if the
  committed file is stale (`npm run unocss:build`; advisory).
- `ui-rig` — browser checks for `wwwroot` in a headless browser against a stubbed
  `/api` (`ci/ui-rig/run.sh`, see `ci/ui-rig/README.md`). Runs when the UI changes;
  advisory while the browser binary is an unversioned nightly.
- `dockerhub-push` — on `master` only: publishes the image to Docker Hub.
- `release` — on `master` only: the shared `.release` job from
  `ci/templates` (`/release.yml`) cuts `v<VersionPrefix>` (from `Version.props`)
  as a tag + GitLab release, then mirrors that release to the public GitHub
  repo as one synthetic commit per release. Required project CI variables:
  `RELEASE_TOKEN` (tag/release writes) and the `GITHUB_DEPLOY_KEY` *file*
  variable (deploy key of the GitHub repo, write allowed); `GITHUB_MIRROR_URL`
  and `GITHUB_MIRROR_BRANCH` are set in `.gitlab-ci.yml`. Without the deploy
  key the job fails on purpose rather than shipping a release nobody published.

## Definition of done (before pushing)

From the repository root:

1. `dotnet test test/FdroidMgr.Tests.Unit && dotnet test test/FdroidMgr.Tests.Component` — all tests green.
2. `dotnet format FdroidMgr.slnx style --verify-no-changes --severity error` — clean.
3. `dotnet restore FdroidMgr.slnx && dotnet test test/FdroidMgr.Tests.Unit
   --no-restore -c Release --coverlet --coverlet-output-format cobertura
   --results-directory ./test-results && dotnet test
   test/FdroidMgr.Tests.Component --no-restore -c Release --coverlet
   --coverlet-output-format cobertura --results-directory ./test-results
   && bash ci/coverage-gate.sh ./test-results` — line coverage not below the
   95% gate, merged across both test projects by the gate script.
4. If `src/FdroidMgr.Api/wwwroot` changed: `ci/ui-rig/run.sh` — the browser checks
   pass (update `ci/ui-rig/fetch-stub.js` from the controllers in the same MR).
   If its classes changed: `npm run unocss:build` and commit the generated CSS.
   If a class moved out of `ui.css`: `node ci/unocss-parity.mjs --old <before>
   --new src/FdroidMgr.Api/wwwroot/css/ui.css --new src/FdroidMgr.Api/wwwroot/css/unocss.css
   --markup src/FdroidMgr.Api/wwwroot/ui.html,src/FdroidMgr.Api/wwwroot/js/ui.js
   --all --audit --classes '<old>=<new>'` — declaration parity, the orphan
   check, and the audit (every markup token must have a rule).
   CSS itself cannot be verified headlessly (see `docs/dev/unocss.md`), so a
   browser look is part of the review.
5. Push the branch and monitor the pipeline via the GitLab API until it is green.
   Do not merge — wait for approval.

## Code style

- Braces are required on every single-statement control statement
  (if/else/for/foreach/while/do). The `style-check` job enforces this; the IDE
  adds braces (never removes them).
- Prefer `var` when the type is apparent; file-scoped namespaces; nullable
  reference types enabled in every project.
- Every service registered in the container is registered as
  `Add{Scoped,Singleton}<IFoo, Foo>()` and injected through `IFoo` (interfaces
  live in `Abstractions/`); nothing is injected via its concrete type and there
  are no static service classes. Plain helpers that belong to a service
  (`FdroidRepoService.Parse`, `KeyService.GetFingerprint`, ...) stay static.
- Keep comments terse — explain *why*, not *what*; the code and names should
  carry the *what*. Avoid inline doc-comment walls and `// see §…` pointers.
- Doc-comment summaries are multiline: `<summary>` on its own line, the text on
  the lines between, `</summary>` closing on its own line — never
  `/// <summary>text</summary>` on one line.

## Test conventions

- Microsoft.Testing.Platform + xUnit v3 + FluentAssertions.
- Tests live in `test/FdroidMgr.Tests.Unit` (services and units exercised
  directly) and `test/FdroidMgr.Tests.Component` (part of the solution): the
  latter boots the API via `WebApplicationFactory` and treats it as a black box
  — public HTTP surface only, no internals.
- OIDC flows are exercised end-to-end against the in-process `FakeOidcServer`.

## Known quirks

- `dotnet test` (Microsoft.Testing.Platform): extra CLI args are forwarded to the
  test host — never pass restore options (e.g. `--packages`) to it; restore separately.
- The OIDC config file (`FDROID_CONFIG_FILE`, default `/config/appsettings.json`)
  is watched with `reloadOnChange` and seeded with `{}` at boot when absent; the
  OpenIdConnect handler resolves the provider from it live on every request
  (no container restart — `plans/done/no-restart-plan.md`).
