# Plan: Add HLinq query support to the API

> **Status: ✅ done — S0–S6 all merged.** S0–S2+S5 as MR `!5`, the S3/S4
> apps-list pilot and UI adoption on `feat/hlinq-apps` / `feat/hlinq-ui`,
> S6 (the releases list) as MR `!29`.
>
> **The spike findings below override the syntax snippets in this plan** — read
> them before writing a query or an endpoint.
>
> **Referenced version: `HamsterWheel.HLinq.AspNet 0.6.0` (stable).** As of
> 2026-09-09 HLinq has merged `alpha` into `master` (`7be6e06`,
> pipeline #5323) and its `package` job published **0.6.0** — the version that
> carries `net10.0` assets — to the project registry, the private feed **and
> nuget.org** (byte-identical: `lib/net10.0/HamsterWheel.HLinq.AspNet.dll` is
> `sha256 80b34b5a…` from both sources). The `alpha` branch is gone.
>
> **Restore needs nuget.org only** (since 2026-09-12). It did not use to: 0.6.0
> depends on `HamsterWheel.CodeShare.NetStandard 0.6.1`, an analyzer-only package
> that hlinq references without `PrivateAssets`/`IncludeAssets="analyzers"`, so
> NuGet records it as a runtime dependency — while that package lived only on the
> internal private feed, a nuget.org-only restore died with `NU1101` on it. It is
> published to nuget.org now (CodeShare master pipeline, through
> `ci/templates`' `.nuget-publish`), so this repo carries **no** feed credential:
> neither R8's secret nor R9's clean-machine caveat applies. The private feed stays
> documented in `README.md` as a fallback. Version rule: decision 7; wiring: S1.

## Why

Every list endpoint returns its whole collection and the web UI filters, sorts
and pages it in JavaScript (`wwwroot/js/ui.js`). That is fine at the current
size and wrong in general: the manager has no way to ask for "the 20 releases
after this one, filtered by package", and every new client re-implements the
same client-side filtering.

**HLinq** (HLinq) is a URL-native LINQ query language:
filtering, ordering, paging, projection and count travel in the query string
and are applied server-side to an `IQueryable`. It is the same library the
HamsterWheel platform is built on, so adopting it here means one query idiom
across both projects.

## What HLinq gives us (verified from the hlinq repo @ `alpha` = 0.6.0)

| Piece | Note |
|---|---|
| `HamsterWheel.HLinq` | core query language + parser |
| `HamsterWheel.HLinq.Abstractions` | extension points (custom filters/translations) |
| `HamsterWheel.HLinq.AspNet` | the ASP.NET Core integration used here |
| `HamsterWheel.HLinq.PgSql` | PostgreSql-only DB functions — **not applicable**, this repo is SQLite |
| `HamsterWheel.HLinq.Client` | typed `HttpClient` query builder — no .NET client consumes this API (the UI is JS) |

Server side is three lines per endpoint:

```csharp
// Program.cs, once
builder.Services.ConfigureHLinq();          // optional Action<HLinqConfiguration> for options

// an endpoint — note the namespace: HLinqQuery<T> is in HamsterWheel.HLinq.Request
using HamsterWheel.HLinq.Request;

[HttpGet]
public IActionResult List(HLinqQuery<AppListItem> query, CancellationToken ct) =>
    Ok(query.ApplyTo(items.AsQueryable(), ct));
```

`ApplyTo` returns `object?`, **not** `IActionResult`: the materialized array —
or an `int` when the query is `count[]` alone — so `Ok(...)` is what serializes
it. It is synchronous (it takes a `CancellationToken` but does not await).

Query-string syntax — **as verified against `0.6.0-alpha-10558`, not the README**
(the keywords are the token keywords in the package: `where`, `orderBy`,
`orderByDescending`, `thenBy`, `thenByDescending`, `skip`, `take`, `count`,
`select`; clauses separated by `.`; comparisons `== != > >= < <= && ||`):

```http
GET /api/api-keys?skip[20].take[10]
GET /api/api-keys?where[x.name.contains(pipeline)].orderBy[x.createdUtc]
GET /api/api-keys?where[x.name.contains(pipeline)].count[]
```

There is **no `order[…]` / `order-[…]`** — the plan's original snippets were
wrong, and the hlinq README is out of date on this.

Safety net that matters for an API: without `take[]` HLinq caps the result at
`IHLinqOptions.HttpDefaultMaxTakeRecords` (**1000** by default, changeable via
`ConfigureHLinq(c => c.HLinqOptions.HttpDefaultMaxTakeRecords = …)`), so an
unmodified `GET /api/apps` cannot drag the whole repo through the serializer.

## Current state (verified in this repo 2026-09-06)

- `src/FdroidMgr.Api` is **net10.0**, EF.Core **10.0.0 + Sqlite**, central
  package management (`Directory.Packages.props`), controllers registered at
  `Program.cs:134` (`AddControllersWithViews()`) and `:135`
  (`AddControllers().ConfigureApiBehaviorOptions(...)` with a custom
  `InvalidModelStateResponseFactory` that produces the `{error}` shape the UI
  renders).
- **No `app.UseExceptionHandler(...)` in the pipeline today** — see risk R3.
- **Package source — `0.6.0-alpha-10558`, from the private NuGet private feed.**
    Facts verified 2026-09-06:

    | Feed | Version | Target framework |
    |---|---|---|
    | **private NuGet private feed** (`groups/private/-/packages/nuget`; published by HLinq, pipeline 4455 from `alpha`, 2026-08-21) | **`0.6.0-alpha-10558`** | **`HamsterWheel.HLinq.AspNet` → `net10.0`**; core `→ netstandard2.0;net10.0` |
    | nuget.org | 0.5.1 (stable) | `net9.0` only |

    So the alpha is the one that actually matches this repo's `net10.0`, and it
    is what the platform repo already runs
    (`HamsterWheel.HLinq{,.Abstractions,.AspNet,.PgSql}` 0.6.0-alpha-10558 in
    six csproj files).
  - **Use the private feed** — one source covers the whole org, where
    the platform repo needs four project sources today:
    ```
    https://api.gitlab.example.com/api/v4/groups/private/-/packages/nuget/index.json
    ```
    ⚠️ **The `/-/` is mandatory.** `groups/private/packages/nuget/index.json` (the
    form GitLab's own docs show) returns `404` here — and that 404 carries
    `x-gitlab-meta` and is byte-identical to a bogus path, so it is
    indistinguishable from "this instance has no private feeds". It is a URL typo,
    not a missing feature; an earlier revision of this plan drew exactly that
    wrong conclusion.
    The feed is **not** anonymously reachable, and **this repo has no
    `nuget.config`** — CI restores from nuget.org only (`.gitlab-ci.yml`
    `before_script: dotnet restore FdroidMgr.slnx`). S1 adds the source; R8
    covers the credential, R9 the clean-machine consequence.

- `AspNetInstaller.ConfigureHLinq` (verified in the hlinq source) does four
  things that interact with our startup, and is **byte-identical between
  `master` (0.5.1) and `alpha` (0.6.0)** for all of them: registers
  `HLinqQueryExceptionHandler`, a singleton `IHLinqOptions`, calls
  **`AddControllers(...)`** to insert `HLinqQueryBinderProvider` at index 0, and
  runs the core `HLinqCore.ConfigureServices`.

- **Restore path proven end to end over plain HTTP (2026-09-06)**, with a real
  a maintainer token — the exact calls `dotnet restore` makes, so S0.1 is a
  formality rather than an unknown:

  | Call | Result |
  |---|---|
  | `GET groups/private/-/packages/nuget/index.json` | **200** — NuGet v3 service index |
  | `GET groups/private/-/packages/nuget/metadata/hamsterwheel.hlinq.aspnet/index.json` | **200** — 134 versions, `0.6.0-alpha-10558` present, `net10.0` |
  | `GET` that entry's `packageContent` | **200** — valid .nupkg, `lib/` has exactly one TFM: **`net10.0`** |
  | `GET …/metadata/<other org packages>/…` | **200** — org-wide from the one source, as intended |

  (The `packageContent` download URL is the *owning project's* path,
  `projects/8/…`, even when discovered through the private feed. Normal — a token
  that reads the private feed reads its members' packages.)

  Details that cost a confusing debugging session to learn, recorded so the next
  reader skips them:
  - **The `/-/` in the group URL** — without it every request 404s (above).
  - **Basic-auth username must be the token's owner.** `gitlab-ci-token:<pat>`
    and a mismatched username fail with `404 Group Not Found` / `401`, which
    looks like a permissions problem, not a typo.
  - The metadata response is **not version-sorted** (134 versions, newest not
    last) — select the pinned version, do not take the tail.
  - Historical note: `0.4.0-alpha-*` of the same package really is `net9.0`, so
    the TFM claim is per-version and must be re-checked on every bump.

### Endpoints, in order of fit

| Endpoint | Data source today | Fit |
|---|---|---|
| `GET /api/keys` (`ApiKeysController.List`) | EF Core `ApiKeys` table, already hand-projects an anonymous DTO (never returns `KeyHash`) | **best pilot** — DB-backed, projection exists |
| `GET /api/apps` (`AppsController.List`) | fdroid index JSON + `metadata/*.yml` merged into an in-memory `List<AppListItem>` | good — HLinq over `AsQueryable()`, no SQL; this is the endpoint the UI actually filters |
| `GET /api/apps/{pkg}/releases` (`ReleasesController.List`) | fdroid index per app + changelog files | **S6, done** — queried; the changelog sidecar is resolved lazily by the DTO, so only the rows in the final response pay a file read |
| `GET /api/stats/downloads` (`StatsController.Downloads`) | `DownloadStatsService.GetAsync` computed DTO | **poor** — no `IQueryable`, skip |
| `/fdroid/repo/**` | static repo files behind `repo:read` | out of scope, must stay byte-identical |

## Design decisions

1. **`HLinqQuery<T>` is parameterised by the DTO, never the entity.** The hlinq
   README is explicit that `T` must be the type the API exposes. This also
   keeps the `ApiKey` secret out of reach of `select[]`.
2. **Additive, not breaking.** No clause in the query string ⇒ today's exact
   payload. The UI keeps calling `GET /api/apps` bare. The one behavior change
   to decide is the implicit 1000-record cap (open question Q2).
3. **In-memory vs DB-backed is a real difference.** `AppListItem` lists get the
   HLinq grammar but no SQL translation (fine at this scale); only `ApiKeys` /
   `DownloadCount` go through EF. HLinq documents PgSql, and its own roadmap
   lists "Add support for other DBs" — so **SQLite translation must be proven
   in S0, not assumed**.
4. **`select[]` is allowed but not advertised in v1** — the UI binds to fixed
   field names; projection is for machine clients.
5. **Thin glue only.** The 80% line-coverage gate is per-assembly: keep the
   HLinq surface at one endpoint parameter + one `ApplyTo` call, no wrapper
   services.
6. **Feature-flag by endpoint, one MR per endpoint** (per `AGENTS.md`: one
   logical change per MR).
7. **Use the 0.6.0 line — never 0.5.1 — pinned to an exact build
   (`0.6.0-alpha-10558`).** This is the version rule for the whole plan:
   - **Why not the "obvious" stable:** nuget.org's only release is **0.5.1**, and
     it is the *wrong* package for a net10.0 app (its `lib/` is `net9.0` only).
     A fresh `dotnet add package HamsterWheel.HLinq.AspNet` picks exactly that
     and still restores successfully — the framework mismatch is silent at
     restore time. Always pin explicitly.
   - **Why not `0.6.0` stable:** it **does not exist yet** — verified on the group
     feed 2026-09-06: `HamsterWheel.HLinq`, `.Abstractions` and `.AspNet` all
     top out at `0.6.0-alpha-10558` (newest `alpha` pipeline: 280, `b7bae44`,
     2026-08-21). Nothing newer than 10558 has been published.
   - **Pin the build, no floating range:** the `alpha` branch publishes a new
     `-alpha-<build>` on every push, so an exact pin is what keeps a green
     pipeline reproducible; upgrades are deliberate edits to
     `Directory.Packages.props` (same policy the platform repo uses — it
     runs `0.6.0-alpha-10558` across six csproj files).
   - **When `0.6.0` stable lands**, bump every HLinq package this repo references
     in one commit (never a mixed `0.6.0` stable + `0.6.0-alpha` set), and
     re-verify the TFM claim (below) rather than assuming.
   - **How to pick "newest 0.6.0-alpha" correctly:** see the checked snippet in
     *Checking for a newer 0.6.0-alpha* below — the `-alpha-<n>` suffix is a
     pipeline counter, **not** an ordinal, so sorting by it picks the wrong build.
   - And confirm the pin actually carries the expected asset before believing it:
     download the `.nupkg` and check `lib/` (this is how the `net10.0` claim in
     this plan was established — and how a *stale* version was caught: `0.4.0-alpha-*`
     of the same package is `net9.0`).

### Checking for a newer 0.6.0-alpha

The build suffix is a pipeline counter. Proof from the sibling
`HamsterWheel.Flows.Abstractions` feed, which currently offers:

```
0.6.0-alpha-13457   0.7.0-alpha-13522   0.5.0-alpha-13685   0.7.0-alpha-13726
```

`0.5.0-alpha-13685` has a **higher** build number than `0.7.0-alpha-13522` — so
"highest build" ≠ "newest version". Order by semver instead. Verified working
2026-09-06; prints `0.6.0-alpha-10558` as newest and `0.6.0 stable exists:
False`:

```sh
curl -s -u "user:$TOK" \
  "https://api.gitlab.example.com/api/v4/groups/private/-/packages/nuget/metadata/hamsterwheel.hlinq.aspnet/index.json" \
  -o /tmp/hlinq-versions.json

python3 - <<'PY'
import json, re
d = json.load(open("/tmp/hlinq-versions.json"))
vs = {i["catalogEntry"]["version"] for a in d["items"] for i in a["items"]}

def key(v):
    m = re.match(r"(\d+)\.(\d+)\.(\d+)(?:-alpha-(\d+))?", v)
    major, minor, patch = map(int, m.groups()[:3])
    build = int(m.group(4)) if m.group(4) else 0
    return (major, minor, patch, m.group(4) is None, build)  # a release outranks its prereleases

print("newest:", *sorted(vs, key=key)[-4:])
print("0.6.0 stable exists:", "0.6.0" in vs)
PY
```

(Use a heredoc: a `python3 -c` one-liner with escaped newlines gets flattened by
the shell and dies with `SyntaxError` — that was the first draft of this block.)

## Spike findings (S0, verified 2026-09-08 against 0.6.0-alpha-10558)

1. **Restore** works from the private feed; `project.assets.json` resolves
   `HamsterWheel.HLinq{,.Abstractions,.AspNet}` to `lib/net10.0` with no `NU1701`
   fallback and no `NU1605` downgrade. A machine with both nuget.org and the
   private feed gets `NU1507` (two sources + CPM, advisory only).
2. **`ConfigureHLinq()` is safe next to the existing MVC setup** — its internal
   `AddControllers(...)` only inserts the binder provider; the custom
   `InvalidModelStateResponseFactory` still owns model-state errors (the
   `{error}` body). Call order does not matter.
3. **No `[FromQuery]` needed**; an empty query string binds to an empty query,
   and `ApplyTo` appends a default `take` (the 1000 cap) — a bare request
   behaves exactly like the pre-HLinq endpoint as long as the endpoint keeps its
   own default ordering, which HLinq's `orderBy[…]` then *replaces*
   (`OrderBy` after `OrderBy`, not `ThenBy`).
4. **SQLite translates the pilot grammar** (R2 resolved for this endpoint) —
   `where[x.name.contains(…)]`, `orderBy[…]`, `orderByDescending[…]`, `skip`,
   `take`, `count[]` all execute against the database. **`contains` is
   case-sensitive** (EF maps it to SQLite `instr()`), unlike an `ILIKE`-style
   translation. Not exercised: `select[]` (decision 4: not advertised in v1)
   and compound `&&`/`||` conditions.
5. **The binder throws; it does not add a model-state error** — a malformed
   query can only be answered from middleware, never from a `try/catch` around
   `ApplyTo` (the exception escapes before the action is invoked).
6. **`ConfigureHLinq()` does register a working handler, but `UseExceptionHandler()`
   still refuses to start without one of its own.** Two separate facts from the
   shipped `Microsoft.AspNetCore.Diagnostics` (`ExceptionHandlerMiddlewareImpl`):
   - the **constructor guard** accepts only `Options.ExceptionHandler`,
     `Options.ExceptionHandlingPath`, or a registered `IProblemDetailsService`
     — it never looks at the injected `IExceptionHandler` collection, so
     `ConfigureHLinq`'s `AddExceptionHandler<HLinqQueryExceptionHandler>()` does
     **not** satisfy it: the argument-free `app.UseExceptionHandler()` throws
     "Either the 'ExceptionHandlingPath' or the 'ExceptionHandler' property must
     be set … or configure … 'service.AddProblemDetails()'".
   - at **handling** time the `IExceptionHandler` collection is consulted
     **before** `Options.ExceptionHandler`.

   Net effect: a rethrow-only `ExceptionHandler` delegate is the cheapest way to
   get past the guard *without* `AddProblemDetails()` (which would also format
   every other unhandled error in the app as ProblemDetails — R3's blast
   radius), and HLinq's own handler then answers malformed queries with
   **`400 {"title": <parse error>, "status": 400}`**, not the app's `{error}`
   shape. That is deliberate: it is the library's (and the platform's) contract,
   and the delegate never touches HLinq errors.

   (An earlier revision of this plan, and the first cut of this MR, claimed the
   opposite — that the app's delegate mapped `HLinqQueryException` to
   `400 {error}`. That branch was dead code: the collection runs first, and the
   test only asserted the status code, so it passed anyway.)

## Steps

### S0 — spike ✅ (done on the feature branch; see *Spike findings* above)
Prove the four unknowns before committing to the shape:
1. `dotnet restore` pulls `HamsterWheel.HLinq.AspNet` **0.6.0-alpha-10558**
   from the private feed into the net10.0 web project with the source added per
   S1 — confirm the CI credential can read the private feed and that restore picks
   the `net10.0` asset (no `NU1701` fallback to `netstandard2.0`, no `NU1605`
   downgrade from the transitive `Microsoft.Extensions.DependencyInjection` /
   `System.Text.Json` pins). The three HTTP calls behind this are already
   verified (see "Restore path proven…"), so this step is mostly about the CI
   credential.
2. `ConfigureHLinq()` placed relative to `Program.cs:134/135` — confirm the
   existing `InvalidModelStateResponseFactory` still wins (the custom `{error}`
   body must not regress; `ApiIntegrationTests` assert it).
3. A bare `HLinqQuery<AppListItem>` parameter binds from the query string with
   **no** `[FromQuery]`, and is absent-means-no-filter.
4. SQLite: one `where[…].order[…].skip[…].take[…]` through EF on `ApiKeys` —
   does it translate to SQL, and does case-insensitive `contains` survive?
   Record the generated SQL in the spike notes.

### S1 — wiring ✅ (this MR)
> **Superseded on 2026-09-12 — do not implement the two bullets below.** They
> describe wiring this MR no longer contains: with `HamsterWheel.CodeShare.NetStandard
> 0.6.1` published to nuget.org, this repo restores from nuget.org alone and adds
> **no** NuGet source anywhere (`.gitlab-ci.yml` and `Containerfile` are untouched).
> Kept as the record of what it took, and as the recipe if a dependency ever goes
> registry-only again — including the trap that `tests`, `image-build` and
> `e2e-oidc` override `before_script`, so a source added only globally is silently
> missing there.

- **NuGet source for the private feed** — the private feed, one source for every
  HamsterWheel package:
  ```sh
  dotnet nuget add source "${CI_API_V4_URL}/groups/private/-/packages/nuget/index.json" \
    --name private-feed --username $CI_REGISTRY_USER --password $HAMSTER_WHEEL_FEED_TOKEN --store-password-in-clear-text
  ```
  (`/-/` required. Mechanically this is the same `dotnet nuget add source` step
  the platform repo already runs per project — that repo could collapse
  its four sources to this one as a separate cleanup.)
  ⚠️ In this repo the source has to be added **everywhere a restore happens**:
  `before_script` has the global `dotnet restore FdroidMgr.slnx`, but `tests`
  **overrides `before_script`** (it installs a JRE first) and `image-build` /
  `e2e-oidc` have their own — a source added only to the global one silently
  leaves those jobs restoring without the feed. `style-check` inherits the
  global one, so it needs it too (`dotnet format` restores the solution).
  Prefer a single `!reference`-shared hidden job (`.nuget-source`) over
  copy-pasting the line into each `before_script`.
- **Group feed, not a per-project source** — one source for the whole org,
    strictly better than what the platform repo does today (four
    `dotnet nuget add source` lines: hlinq/8, flows/18, codeshare/10,
    datamapper/11). Request-level evidence, including the URL-shape trap, is in
    "Current state" above.
  - Where the source already exists: the **dev box** has the private feed +
    credentials in `~/.nuget/NuGet/NuGet.Config` (source `gitlab`,
    `ClearTextPassword` = a maintainer token), so local `dotnet restore` works with
    no extra flags. The **prod box** (the CI host) has **no GitLab NuGet source at all** — its only `NuGet.Config`
    on the box lists nuget.org + one Azure DevOps feed — plus no
    `HamsterWheel.*` package cache and no `gitlab-runner`. So CI is the place
    that must add the source explicitly (S1), and the prod box is not a fallback.
  - A credential really is required: the registry **index** is anonymous
    (`index.json` → **200 unauthenticated**) but **metadata and downloads are
    not** (`.../metadata/<pkg>/index.json` and `.../download/...` → **401**
    anonymously). So `dotnet restore` cannot work without a token — and, just as
    important for debugging: **a `200` on `index.json` proves nothing about a
    credential**. Validate a token with the *metadata* call.
  - If the feed is switched to a **fine-grained PAT**, it needs the
    **Package: Read** permission on the group (or on hlinq specifically, with
    the project feed URL). Without it: `403 insufficient_granular_scope —
    "...requires a fine-grained personal access token with the following group
    permissions: [Package: Read]"`, and `GET /groups/private/packages` answers
    `404 Group Not Found` because the group is not in the token's scope at all.
    The error text names the exact permission to tick — trust it over guesswork.
    Error vocabulary for this feed, all observed:

    | Response | Meaning |
    |---|---|
    | `401 "Token is expired"` | token recognised, expiry passed (a mistyped token gives a generic `401 {"message":"401 Unauthorized"}` instead) |
    | `403 insufficient_granular_scope` | token valid, missing **Package: Read** |
    | `401 {"message":"401 Unauthorized"}` | unknown/malformed token, or a scope the endpoint never honours |
    | `404` on a feed path | wrong URL shape (usually a missing `/-/`), not a disabled feature |
- **Credential (R8)** — pick one in this step:
  - a **project/deploy token scoped to hlinq (project 8) with
    `read_package_registry`**, stored as one masked + protected CI variable
    here — what platform does (`FLOWS_PROJECT_TOKEN`, name is historical);
  - a **fine-grained PAT with `Package: Read` on the HamsterWheel group** —
    the least-privilege option that fits the private feed (a token created without
    that permission is valid but gets `403 insufficient_granular_scope`, and the
    error names the permission to add); recommended for the CI variable;
  - a **legacy PAT with `read_package_registry` / `read_api`** — the shape that
    actually works today (verified against the private feed; the same credential
    type already sits in the dev box's `NuGet.Config`). Also the broadest: it
    grants everything the maintainer can read, so a leaked CI variable is worse
    than a scoped token, and **expiring it silently breaks every pipeline that
    restores** — an expired PAT is a plain `401` from the feed, which reads
    exactly like a missing `nuget add source` line. (This bit us for real while
    writing the plan: one token in hand was expired, another was in daily use —
    so "the UI says it was used today" can be a *different* token.) If this is
    the choice: set an expiry date + a calendar reminder, and never paste the
    token into a repo, an MR description or a chat;
  - or no secret at all: allowlist this project's **`CI_JOB_TOKEN`** in
    hlinq's *Settings → CI/CD → Job token permissions* and use
    `--username gitlab-ci-token --password $CI_JOB_TOKEN`. Verify cross-project
    *package-registry read* via job token actually works on this instance
    before committing to it.
- `Directory.Packages.props`:
  ```xml
  <!-- 0.6.0 line only — a bare `dotnet add package` would pull nuget.org's
       0.5.1 (net9.0-only) and restore without complaint. See decision 7. -->
  <PackageVersion Include="HamsterWheel.HLinq.AspNet" Version="0.6.0-alpha-10558" />
  ```
  (add `HamsterWheel.HLinq` / `.Abstractions` here too if they are ever
  referenced directly, and keep every HLinq pin on the same build)
- `FdroidMgr.Api.csproj`: `PackageReference` (no version — CPM).
- `Program.cs`: `builder.Services.ConfigureHLinq(...)` + (if S0/R3 confirms it)
  `app.UseExceptionHandler(...)` **before** `UseRouting`.
- `ConfigureHLinq` option: set `HttpDefaultMaxTakeRecords` to the agreed value.
- OpenAPI: document the HLinq query parameter on the endpoints that take it
  (`Microsoft.AspNetCore.OpenApi` is already referenced; the param will show up
  as a complex object — a `[FromQuery]`-style `ProducesResponseType`/`WithSummary`
  note or a small `IOperationFilter` describing the grammar is enough; keep it
  in one place).

### S2 — pilot: `GET /api/api-keys` ✅ (this MR; the route is `api/api-keys`, not
`api/keys` — `KeysController` is the app-signing-key surface)
Swap the hand-written `OrderByDescending(k => k.Id).ToListAsync()` for
`query.ApplyTo(...)`, keeping the anonymous projection. Integration test in
`test/FdroidMgr.Tests/ApiKeysControllerTests.cs`-style: filter + order + take
through the public HTTP surface, plus "no query string ⇒ same body as today".

### S3 — `GET /api/apps` ✅
Build the list as today, then `query.ApplyTo(items.AsQueryable(), ct)`. Add a
`count[]` example to the docs (the UI needs totals before it can page).

### S4 — web UI adoption ✅ (`feat/hlinq-ui`)
Done 2026-09-14. The premise ("the in-JS filter/sort") turned out to be
aspirational — the UI never filtered or sorted at all, it rendered the whole
list. So S4 adds the controls, implemented server-side as HLinq query strings:

- **Filter** (`#app-search`): case-insensitive match across name / package /
  summary — one OR'd condition with the
  `StringComparison.InvariantCultureIgnoreCase` overload (legal here because
  the list is LINQ-to-objects). The term is sanitized client-side of HLinq
  metacharacters (and of `& + ? # " %`, which would break the URL itself), so
  a search term can't inject extra clauses. Debounced 300 ms.
- **Sort** (`#app-sort`): Name A–Z / Z–A, Package Z–A; the default is the
  server's package-name order (no clause).
- **Paging**: 25 per page via `skip[N].take[26]` — the extra row signals a next
  page without a second `count[]` request (left-to-right `take[].count[]`
  would count the page, not the total).
- Clause order in every request: `where[…] → orderBy[…] → skip[…] → take[…]`.
  The no-clause path still works (S3 pins it), so the UI diff is independently
  revertable.

Verified two ways: integration tests pin the exact query strings the UI
builds (`Where_Or_CaseInsensitive_MatchesNamePackageOrSummary`,
`FullUiQuery_WhereAndSortAndPage`), and a headless-browser pass drove the real
`ui.js` (with only `api()` stubbed) through load / filter / sort / paging /
empty state.

### S5 — docs ✅ (same MR as S1)
`README.md` + this file: the grammar one-liner, the cap, and per-endpoint
support status.

### S6 — `GET /api/apps/{pkg}/releases` ✅ (MR `!29`)
The last list endpoint that still ignores the query string: `List` orders by
`VersionCode` descending and returns *every* version, and the UI detail panel
renders the whole history. It should filter, sort, page and count like the
apps list — `where[versionName~=…]`, `orderBy[versionCode]`, `skip[N].take[M]`,
`count[]` — with the same additive, DTO-parameterised contract as S3/S4.

One order-of-work constraint makes it more than a copy of S3: build the row
set, apply `where → orderBy → skip → take` to the `IQueryable`, and read the
changelog sidecar **only for the rows that survive paging** — otherwise every
request pays a file read per historical version, which is exactly the cost
paging is supposed to avoid. `select[]` therefore cannot drive the projection;
it stays a response-shape filter.

## Risks

- **R1 — the dependency is an unreleased alpha.** `0.6.0-alpha-10558` has no
  semver promise: an `alpha`-branch push can change behaviour under us, and
  there is no patch line to stay on. Mitigations: exact pin (decision 7), and
  remember the producer is our own HLinq, so a break is fixable
  in-house — but a *silent* break is not: pin, and bump deliberately.
  (In exchange we get the `net10.0` asset; nuget.org's stable 0.5.1 is
  `net9.0`-only.)
- **R2 — SQLite is not HLinq's documented target.** Only PgSql has a dedicated
  package and the roadmap admits other DBs are unsupported. If EF/SQLite can't
  translate a filter, restrict the DB-backed endpoints to
  `order/skip/take/equality` (or run the filter in memory for small tables).
- **R3 — the 400 path needs middleware HLinq assumes we have.**
  `HLinqQueryExceptionHandler` returns a clean 400 `ProblemDetails` for a
  malformed query, but `IExceptionHandler` only runs under
  `app.UseExceptionHandler(...)`, which this app does **not** have. Without it a
  bad HLinq query becomes an unhandled 500; with it, **every** unhandled
  exception in the app starts going through the new handler — so either scope
  it or verify the existing error shape (`{error}` strings the UI shows) is
  unchanged. Decide explicitly in S1; this is the highest-blast-radius item.
- **R4 — `ConfigureHLinq` calls `AddControllers()`.** It must not clobber the
  `ConfigureApiBehaviorOptions` customization at `Program.cs:135`. Verify order
  in S0.2.
- **R5 — implicit 1000-record cap** silently truncates a bare `GET /api/apps`
  once the repo has more apps than that, turning a "nothing there" UI into a
  wrong answer. Either raise the cap above any realistic count or make the UI
  page (S4) — but do not leave it implicit.
- **R6 — `[` and `]` in query strings** must be percent-encoded by some
  clients/proxies; the UI should build queries with `URLSearchParams`.
- **R7 — auth interaction**: HLinq binds before the action, so a
  `select[secret]`-style probe still runs under the existing `read` /
  `apps:write` policies. `T` = DTO (decision 1) is what actually protects the
  API-key hash.
- **R8 — the feed needs a credential, and that credential is a new secret in
  this project.** *(Resolved 2026-09-12: no credential — the registry-only
  dependency is on nuget.org now, so this risk does not apply to this repo.)* A `read_package_registry` token for hlinq is a supply-chain
  surface (any job that can read CI variables can pull the packages the API
  links against). Prefer the job-token allowlist if it works (S1); otherwise
  scope the token to the **group (69) + `read_package_registry` only** (not
  `api`), mask + protect it, and note that **any maintainer who can publish an
  `alpha` to the private feed can publish a package this repo restores** — inherent
  to depending on an internal feed, but worth stating next to the fact that the
  dependency is an alpha (R1).
- **R9 — clean-machine restore stops working anonymously.** *(Resolved
  2026-09-12: `git clone && dotnet restore` works anonymously again — see R8.)*
  With a registry-only
  dependency, `git clone && dotnet restore` on a fresh box (or a contributor
  without the feed) fails with a confusing `NU1101`. The platform papers over
  this with a *global* NuGet config on the dev machine and an empty
  `src/nuget.config` stub. Decision for this repo: document the exact
  `dotnet nuget add source` command in `README.md` (S5) rather than rely on
  machine state.
- **R10 — `0.6.0` parser spins forever on `where[x` / `where[x.` (thread-pool
  DoS).** *(Found 2026-09-14 while writing the S3 tests. Fixed upstream in
  HLinq upstream MR `!15` — merged and shipped as **0.6.1** on 2026-09-14;*
  *pin bumped here in MR `!13`.)* A `where` filter truncated
  right after the entity alias made `HLinqParser.Parse` loop without progress:
  `Condition.Parser` matched `[Entity, ..]` but built `Condition` with zero
  tokens, so the parse loop never advanced and pinned a thread-pool thread per
  crafted request. Both live endpoints (`/api/api-keys` since MR `!5`,
  `/api/apps` with S3) were exposed. 0.6.1's no-progress guard makes the
  sequence fail to parse → 400. The S3 integration test still uses `where[` as
  its malformed sample, and `ApiKeysHlinqApiTests.
  TruncatedWhereAfterEntityAlias_IsBadRequest_NotAHang` pins the 0.6.1 fix
  (asserts 400 under a 10 s timeout, so a regression fails CI fast instead of
  hanging it).

## Non-goals

- `HamsterWheel.HLinq.PgSql` (this is SQLite).
- `HamsterWheel.HLinq.Client` (no .NET client).
- HLinq roadmap features that don't exist yet: grouping, nested selects/counts,
  methods in `select`.
- Converting `/fdroid/repo/**` serving or the OIDC/API-key auth paths.

## Definition of done

Per `AGENTS.md`, for each MR: `dotnet test` green, `dotnet format
FdroidMgr.slnx style --verify-no-changes --severity error` clean, coverage gate
(`ci/coverage-gate.sh`) ≥ 80% lines, feature branch + MR, pipeline green,
**no merge without maintainer approval**.

## Open questions

- **Q1** — **resolved: `0.6.0-alpha-10558` from the group-69 feed** (net10.0
  asset, parity with the platform, one source for the whole org).
  **Still open (maintainer action): the CI credential.** The pipeline and the
  image build both read `HAMSTER_WHEEL_FEED_TOKEN` (+ optional
  `HAMSTER_WHEEL_FEED_USER`) and **fail fast with a clear
  message while it is unset** — no pipeline can go green before the variable
  exists. Intended value: a **fine-grained PAT with Package: Read on the HamsterWheel group**,
  masked + protected. The job-token allowlist was not verified (a job token is
  *per project*; whether it can read a *group* feed on this instance is unknown)
  — if it does work, set `HAMSTER_WHEEL_FEED_USER=gitlab-ci-token` and pass
  `$CI_JOB_TOKEN`, which removes the secret entirely (R8's preference).
- **Q2** — **kept the default 1000 cap** (above any realistic key count, and
  `count[]` reports the real total); it is now documented rather than implicit,
  which is what R5 asks for. The question only becomes live for `/api/apps`
  (R5's real subject): is that endpoint big enough to need paging, or is the
  motivation API uniformity? If uniformity only, **S3 can be dropped and this
  plan stops after S2**.
- **Q3** — **resolved by the spike: neither option works as written, and the
  answer that ships is HLinq's, not ours.** The binder *throws*, so a
  `try/catch` around `ApplyTo` never sees a malformed query; and although
  `ConfigureHLinq` registers the `IExceptionHandler` that answers it, the
  middleware's startup guard ignores that collection, so `UseExceptionHandler()`
  still wants a delegate, a path, or `AddProblemDetails()` (finding 6).
  `Program.cs` therefore passes a **rethrow-only** delegate: enough to get past
  the guard, HLinq's own handler answers the malformed query with
  `400 ProblemDetails`, and every other unhandled error keeps its exact current
  500 behaviour. A global `UseExceptionHandler()` + `AddProblemDetails` (arguably
  a gap on its own) would change every 500 body in the app — if it is ever added,
  that delegate is the thing to delete.
- **Q4** — **not needed for the pilot** (SQLite translated everything S2 uses,
  finding 4). Still open for a future endpoint that wants something
  untranslatable — the leading candidate being case-insensitive `contains` on
  SQLite. Fix upstream in HLinq (its roadmap says "Add support
  for other DBs") or work around it here.
