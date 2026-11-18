# Contributing

How to build and run fdroidmgr from source, and how the project is organised.

For production installation see **[deployment.md](deployment.md)**; for the HTTP
surface see **[api.md](api.md)**.

## Requirements

- .NET 10 SDK (the version is pinned in `global.json`)
- Node 20+ — only for the CSS build and the browser checks
- `fdroidserver` + a JDK — only if you want to exercise index generation locally
- Podman or Docker — only for the container image and the E2E jobs

## Run it locally

```bash
export FDROID_MASTER_KEY=$(openssl rand -hex 32)
export FDROID_CONFIG_FILE=$PWD/tmp/appsettings.json
mkdir -p tmp
dotnet run --project src/FdroidMgr.Api
```

Then open **http://localhost:5000/ui**. With no identity provider configured the
first-run wizard appears; you can point it at any OIDC test server, or skip sign-in
for a quick look by exploring the public endpoints (`/api/repo/status`,
`/fdroid/repo/index.xml`).

The image defaults every path to somewhere under `/data`, so a local run needs the
overrides above (or set them in your IDE's run configuration).

## Project layout

```
src/FdroidMgr.Api/        the whole application (one project)
  Abstractions/           interfaces for everything registered in DI
  Data/                   EF Core context, entities, migrations
  Endpoints/              minimal-API endpoint groups, one folder per area
  Models/                 request/response DTOs
  Services/               implementations behind those interfaces
  wwwroot/                the web UI: ui.html + js/ui.js + css/
test/FdroidMgr.Tests.Unit/      services and units exercised directly
test/FdroidMgr.Tests.Component/ boots the API via WebApplicationFactory,
                                treats it as a black box (public HTTP only)
ci/                       pipeline helpers: coverage gate, browser rig, E2E
docs/                     user/operator docs (this folder)
docs/dev/                 implementation notes for contributors
demo/                     self-contained browser demo (no IdP, no repo)
```

## Conventions that matter

- **Braces are mandatory** on every `if`/`else`/`for`/`foreach`/`while`, even for a
  single statement. `dotnet format style` enforces this and CI fails on it.
- **Everything goes through an interface.** Services are registered as
  `Add{Scoped,Singleton}<IFoo, Foo>()` and injected through `IFoo`; interfaces live
  in `Abstractions/`. Nothing is injected by concrete type and there are no static
  service classes. Plain helpers that belong to one service stay static
  (`FdroidRepoService.Parse`, `KeyService.GetFingerprint`).
- **Logging is source-generated.** A class that logs is `partial` and logs through
  `[LoggerMessage]` partial methods, never `_logger.LogInformation(...)`.
- **Comments explain *why*, not *what*.** Keep them terse; names and code should
  carry the *what*. Doc-comment `<summary>` tags are multiline (tag on its own line).
- Nullable reference types are on everywhere; prefer `var` when the type is obvious;
  file-scoped namespaces.

## Tests

Microsoft.Testing.Platform + xUnit v3 + FluentAssertions.

```bash
dotnet test test/FdroidMgr.Tests.Unit
dotnet test test/FdroidMgr.Tests.Component
```

The component project boots the API with `WebApplicationFactory` and talks to it
over HTTP only; OIDC flows run against an in-process `FakeOidcServer`. Coverage is
merged across both projects and gated at **95 % line coverage** in CI
(`ci/coverage-gate.sh`).

Two gotchas:

- `dotnet test` forwards unrecognised CLI arguments to the test host, so never pass
  restore options (e.g. `--packages`) to it — restore separately.
- Coverage files accumulate: the gate reads *every* `coverage.cobertura.*.xml` in
  the results directory. Clear it between runs or a stale file skews the number.

## Style and formatting

```bash
dotnet format FdroidMgr.slnx style --verify-no-changes --severity error
```

Rules live in `.editorconfig` at `:error` severity. `style-check` in CI runs
exactly this command.

## The web UI

Single page, no framework build step, no npm for runtime code:

- `wwwroot/ui.html` — markup, Alpine.js `x-` attributes
- `wwwroot/js/ui.js` — router and rendering (~1200 lines)
- `wwwroot/css/ui.css` — hand-written `:root` tokens and components
- `wwwroot/css/unocss.css` — **generated**, do not edit by hand

Views are hash routes (`#stats`, `#api-keys`, `#app=<pkg>&panel=<tab>`, plus list
state as `#q=…&sort=…&page=N`) so the back button walks through the app and every
view is deep-linkable.

### CSS

If you change `wwwroot/`, regenerate and verify:

```bash
npm run unocss:build                 # regenerate css/unocss.css, commit it
ci/ui-rig/run.sh                     # browser checks against a stubbed /api
```

CSS itself cannot be judged headlessly (the rig's browser resolves no colours or
borders), so a real browser look is part of reviewing a UI change.
`ci/unocss-parity.mjs` checks declaration parity between the hand-written and
generated sheets — see [dev/unocss.md](dev/unocss.md).

### Browser checks (`ci/ui-rig`)

`ui.js` has no unit harness, so `ci/ui-rig` loads the real `ui.html`/`ui.js` in a
headless browser against a hand-written fake `/api` (`fetch-stub.js`) and asserts
the behaviour that broke in the past. If you change an endpoint's shape, update
`fetch-stub.js` in the same MR — the rig is meant to stay in step with the
controllers, and endpoints it never modelled will answer `404 {"error":"not in rig
stub"}`.

## Demo without infrastructure

`demo/demo.sh` runs the manager against a minimal OIDC provider and a seeded
three-app index — no real IdP or repository needed. See
[`demo/README.md`](../demo/README.md).

## Git workflow

Feature branch → merge request → `master`. Never push to `master`, never merge an
MR without the maintainer's explicit approval. Keep an MR to one logical change.
The full gate list is in [`AGENTS.md`](../AGENTS.md).
