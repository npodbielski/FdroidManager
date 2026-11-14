# OIDC Login (Keycloak) — Plan

> Replace the local username/password login with an interactive **OpenID Connect
> (OIDC) sign-in** against an external identity provider (Keycloak, Authentik,
> Zitadel, …). The operator configures the provider once via a small first-run
> wizard; the API persists the settings to `appsettings.json` on a dedicated
> `/config` mount; the "Sign in" button redirects the user to the provider's
> login screen (authorization-code flow) immediately — no container restart
> (plans/done/no-restart-plan.md).
>
> This is **not** future work — interactive OIDC redirect login *is* the goal.
> The local username/password login is dropped.

---

## 1. Goal

1. One-time form: **identity server address**, **client id**, **client secret**,
   **required scopes** (optional).
2. Persist those settings to `appsettings.json` (mounted at `/config`).
3. `GET /api/auth/login` redirects the browser to Keycloak's login screen
   (as soon as the settings are saved — no restart); on success Keycloak
   redirects back to `/signin-oidc`; the app establishes a session cookie and
   the UI shows the authenticated user.
4. Local username/password login is removed.

---

## 2. What is mounted where (and where config should live)

Current `docker-compose.yml` mounts:

| Container path | Host path | Contents |
|---|---|---|
| `/data` | `./data` | app data: SQLite DB (`/data/fdroidmgr.db`) + signing keys (`/data/keys`) |
| `/data/fdroidrepo` | `/srv/fdroid/repo` | the F-Droid repo itself (separate mount) |
| `/opt/android-sdk` | `/srv/fdroid-mgr/android-sdk:ro` | Android SDK (apksigner/aapt) |

So **`/data` is app data (DB + keys), not the fdroid repo**. Per the request we
add a **separate `/config` mount** so operator-editable configuration stays out
of the app-data dir:

```yaml
volumes:
  - ./data:/data
  - ./config:/config                              # NEW — appsettings.json lives here
  - /srv/fdroid-mgr/android-sdk:/opt/android-sdk:ro
  - /srv/fdroid/repo:/data/fdroidrepo
```

`appsettings.json` (containing the OAuth/Keycloak settings) is read from
`/config/appsettings.json`. A git-tracked `config/appsettings.sample.json`
documents the shape; the real `config/appsettings.json` is **gitignored**
(it holds the client secret).

---

## 3. Research — current auth state

### 3.1 How the app authenticates today

- `Program.cs` picks a mode from `builder.Configuration["Auth:OAuthIssuer"]`:
  - empty → **local JWT** (current): `AuthController.Login` verifies a BCrypt
    password and `TokenService.Issue` mints a 12 h token with `FDROID_JWT_KEY`.
  - set → an *unwired* JwtBearer branch sets `Authority`/`Audience` (validate-only,
    no interactive login, no client-id/secret/scopes).
- `TokenService` documents the same two modes but only implements local issuing.
- `AuthController` has `POST /api/auth/login` (username/password) and
  `GET /api/auth/me`.

### 3.2 Packages

- Present: `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.0),
  `System.IdentityModel.Tokens.Jwt`, `BCrypt.Net-Next`.
- **To add** for interactive OIDC:
  - `Microsoft.AspNetCore.Authentication.OpenIdConnect` **10.0.0** — the OIDC
    handler (authorization-code + token exchange + redirect).
  - `Microsoft.AspNetCore.Authentication.Cookies` **10.0.0** — session cookie
    that carries the sign-in state after the id_token is validated (reference
    explicitly for parity with JwtBearer; it is otherwise part of the shared
    framework).
- `BCrypt.Net-Next` + `System.IdentityModel.Tokens.Jwt` + `TokenService.Issue`
  become dead code once local login is dropped (remove in the same change).

### 3.3 appsettings.json / config-file loading timing

`.NET` loads `appsettings.json` at the very start of
`WebApplication.CreateBuilder` with `reloadOnChange: true` — so the file is the
source of truth for auth **and** edits (wizard saves, hand edits) are picked up
without a container restart. The OpenIdConnect handler is registered
unconditionally and resolves the provider (issuer, client, secret, scopes) from
the live configuration on every request (`DynamicOpenIdConnectHandler`);
until an issuer + client id are present it is inert and sign-in bounces back
to the setup wizard (plans/done/no-restart-plan.md).

---

## 4. Configuration contract

| Form field | Config key (in `/config/appsettings.json` → `Auth` section) | Required | Notes |
|---|---|---|---|
| Identity server address | `Auth:OAuthIssuer` | yes | Keycloak realm base, e.g. `https://id.example.com/realms/fdroid` |
| Client ID | `Auth:OAuthClientId` | yes | OIDC public/confidential client registered in Keycloak |
| Client secret | `Auth:OAuthClientSecret` | yes (for code flow) | stored AES-encrypted under `FDROID_MASTER_KEY` |
| Required scopes | `Auth:OAuthScopes` | optional | space/comma list, e.g. `openid profile email` |

`appsettings.json` shape (draft):

```json
{
  "Auth": {
    "OAuthIssuer": "https://id.example.com/realms/fdroid",
    "OAuthClientId": "fdroidmgr",
    "OAuthClientSecret": "AES-iv:ct",          // encrypted, never plaintext
    "OAuthScopes": "openid profile email"
  }
}
```

---

## 5. Design

### 5.1 OIDC wiring (Program.cs, when `Auth:OAuthIssuer` is set)

```csharp
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = ".FdroidMgr.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.LoginPath = "/api/auth/login";
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = cfg["Auth:OAuthIssuer"];
        o.ClientId = cfg["Auth:OAuthClientId"];
        o.ClientSecret = Decrypt(cfg["Auth:OAuthClientSecret"]);
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;                       // confidential client still benefits from PKCE
        o.SaveTokens = true;
        o.CallbackPath = "/signin-oidc";
        o.SignedOutCallbackPath = "/signout-oidc";
        o.GetClaimsFromUserInfoEndpoint = true;

        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
        foreach (var s in (cfg["Auth:OAuthScopes"] ?? "")
                 .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            if (!o.Scope.Contains(s)) o.Scope.Add(s);

        o.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = "roles",
        };
    });
```

### 5.2 Login / logout endpoints (replace local login)

```csharp
[AllowAnonymous]
[HttpGet("login")]
public IActionResult Login(string? returnUrl)
    => Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/ui" },
                 OpenIdConnectDefaults.AuthenticationScheme);

[Authorize]
[HttpPost("logout")]
public IActionResult Logout()
    => SignOut(CookieAuthenticationDefaults.AuthenticationScheme,
               OpenIdConnectDefaults.AuthenticationScheme);
```

- Browser hits `/api/auth/login` → 302 to Keycloak login → Keycloak 302 back to
  `/signin-oidc` → OIDC handler exchanges code for tokens → sets the session
  cookie → redirect to the UI.
- `GET /api/auth/me` stays `[Authorize]` and now reads `User.Identity.Name`
  (preferred_username) + claims from the cookie.

### 5.3 One-time setup endpoints

```
GET  /api/auth/oauth/status   (anonymous) → { configured: bool, authority?, configuredAt? }
POST /api/auth/oauth          (ANONYMOUS ONLY while unconfigured) → 201 | 409 | 400
```

Why `POST` is anonymous-when-unconfigured: with local login dropped there is no
credential to gate the *first* setup. The controller enforces the one-time rule
in code — it is rejected with `409` as soon as OAuth is configured (and after
configured, a `[Authorize]` rest call can still be added for `force:true` reuse).

```json
POST /api/auth/oauth
{
  "authority": "https://id.example.com/realms/fdroid",
  "clientId": "fdroidmgr",
  "clientSecret": "…",
  "scopes": ["openid", "profile", "email"]
}
```

| Code | Body |
|---|---|
| `201` | `{ authority, clientId, scopes, restartRequired: false }` |
| `400` | `{ error: "authority is required / must be an https URL", ... }`, missing clientId, bad scopes |
| `409` | `{ error: "oauth already configured", authority }` |

`POST /api/auth/oauth` writes the `Auth:OAuth*` keys into `/config/appsettings.json`
atomically (temp file + `File.Move(overwrite)`), AES-encrypting `OAuthClientSecret`
under `FDROID_MASTER_KEY`, then returns `restartRequired: false` — the provider
goes live as soon as the file watcher reloads the config (a few ms).

### 5.4 No restart required

The OIDC provider is **not** wired at startup anymore:
`DynamicOpenIdConnectHandler` reads issuer / client / secret / scopes from the
live configuration on every request and (re)creates the discovery
`ConfigurationManager` when the issuer changes. Saving via the wizard (or
editing `/config/appsettings.json` by hand) takes effect immediately — the UI
reloads and the "Sign in" button is live (plans/done/no-restart-plan.md).

### 5.5 Bootstrap order (fresh deployment)

1. First boot: `/config/appsettings.json` absent/empty → `oauth/status` returns
   `configured:false` → UI shows **only** the OAuth wizard.
2. Operator saves provider → `201 restartRequired` → restart container.
3. UI now shows **"Sign in"** → `/api/auth/login` → Keycloak login → back to UI
   with a session cookie.
4. Authenticated now → repo-init wizard runs (unchanged, `[Authorize]`) → normal
   dashboard.

---

## 6. Authorization semantics (post-switch)

- `[Authorize]` = has a valid OIDC session cookie (Keycloak login succeeded).
- External claims have no local `admin` role; default: **any authenticated user
  is an operator**. Optional tightening: require a specific claim/scope via an
  `AddAuthorization` policy (`Policy = "RepoAdmin"`) on sensitive endpoints
  (repo init, OAuth re-config, app delete).
- The UI's `api()` helper: on `401` do a full-page `window.location =
  /api/auth/login` instead of showing the removed local login form.

---

## 7. What gets removed (drop internal login)

- `AuthController.Login` (username/password) → replaced by the `GET login`
  challenge endpoint.
- `TokenService` local issuing/validation (`Issue`, `GetSigningKey`,
  `GetValidationParameters`) → delete.
- `UserSeeder` + `Users` DbSet + `Auth:JwtKey` / `FDROID_JWT_KEY` usage → delete.
- `BCrypt.Net-Next` + `System.IdentityModel.Tokens.Jwt` package references →
  remove (dead once local login is gone). `FDROID_ADMIN_USER`/`FDROID_ADMIN_PASS`
  env vars → no longer consumed.

> `FDROID_MASTER_KEY` stays — it now encrypts the OIDC client secret (and still
> the repo-keystore password).

---

## 8. Security notes

- Client secret is AES-encrypted **before** it is written to
  `config/appsettings.json`, and is returned/logged **never**.
- `config/appsettings.json` is chmod `0600`; the `./config` host dir is
  operator-owned and gitignored (only `appsettings.sample.json` is tracked).
- `authority` must be `https` (allow `http` only on an explicit "allow
  insecure" flag for LAN dev).
- Use `ResponseType = code` + PKCE; cookie is `HttpOnly` + Lax (tighten to
  `SecurePolicy.Always` when serving TLS).
- The anonymous `POST /api/auth/oauth` is a one-shot bootstrap window; it is
  guarded to `409` the moment OAuth is configured.

---

## 9. Implementation checklist

- [ ] `Directory.Packages.props` + csproj: add
      `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.0 and
      `Microsoft.AspNetCore.Authentication.Cookies` 10.0.0; remove
      `BCrypt.Net-Next` + `System.IdentityModel.Tokens.Jwt`.
- [ ] `docker-compose.yml` + `Containerfile`: add `./config:/config` mount and
      `FDROID_CONFIG_FILE=/config/appsettings.json`; keep `/data` as-is.
- [ ] Add `config/appsettings.sample.json`; gitignore `config/appsettings.json`.
- [ ] `Program.cs`: `AddJsonFile(FDROID_CONFIG_FILE, optional:true,
      reloadOnChange:true)` at the very top; wire Cookie + OpenIdConnect per §5.1;
      remove the local-JWT branch + `UserSeeder`.
- [ ] Models: `OAuthConfigRequest`, `OAuthConfigStatus`; remove `User`/`Users`.
- [ ] `OAuthConfigService`: validate → encrypt secret → atomic rewrite of
      `/config/appsettings.json` → return `restartRequired`.
- [ ] `AuthController`: `GET login` (challenge), `POST logout`, `GET me`,
      `GET oauth/status`, `POST oauth` (anonymous-until-configured, 409 repeat).
- [ ] Remove `TokenService` local methods and the `UserSeeder` startup call.
- [ ] UI: replace the username/password form with "Sign in" (redirect to
      `/api/auth/login`); add the OAuth first-run wizard (authority, client id,
      client secret, scopes); `api()` 401 → redirect instead of local login.
- [ ] Tests (see §10 for the tiny-IdP recipe):
  - unit: `OAuthConfigService` (validation, secret encryption, atomic write,
    409-once); scope-splitting.
  - API: `GET oauth/status` anonymous; `POST oauth` 400/201/409; secret not in
    response/logs; local `POST /api/auth/login` removed (404).
  - integration (full redirect flow, pure HttpClient — no browser): boot the
    API against a tiny OIDC provider (oidc-server-mock), then drive login →
    redirect → callback → session cookie → assert `GET /api/auth/me` returns the
    mock user.
- [ ] E2E (later): real Keycloak in a sidecar → save config → restart → login
      redirects to Keycloak → callback establishes cookie → `GET /api/auth/me`
      returns the Keycloak username.

---

## 10. Simple test recipe — start a tiny IdP and connect to it

We don't need Keycloak to test the login. A small containerized OIDC provider
(`oidc-server-mock`) is enough, and **plain `HttpClient` drives the whole flow** —
the mock's login is just a static HTML form, so no browser/Playwright is required.

### 10.1 The IdP

```bash
# oidc-server-mock: configures clients/users/scopes from a single JSON file.
podman run -d --rm -p 8084:80 \
  -v ./ci/oidc/oidc-server-mock.json:/tmp/config.json:ro \
  soluto/oidc-server-mock:latest
```

`ci/oidc/oidc-server-mock.json` (shape — verified during implementation):

```json
{
  "Clients": [{
    "ClientId": "fdroidmgr",
    "ClientSecrets": ["test-secret"],
    "AllowedGrantTypes": ["authorization_code"],
    "RedirectUris": ["http://localhost:8082/signin-oidc"],
    "AllowedScopes": ["openid", "profile", "email"],
    "AlwaysIncludeUserClaimsInIdToken": true
  }],
  "Users": [{ "SubjectId": "1", "Username": "alice", "Password": "pw", "Claims": [{ "Type": "preferred_username", "Value": "alice" }] }],
  "ApiScopes": ["openid", "profile", "email"]
}
```

### 10.2 Point the app at it

Either pre-write `/config/appsettings.json` (fast unit/integration path), or
exercise the first-run wizard and let the API write it:

```json
{ "Auth": { "OAuthIssuer": "http://localhost:8084", "OAuthClientId": "fdroidmgr",
             "OAuthClientSecret": "…encrypted…", "OAuthScopes": "openid profile email" } }
```

### 10.3 Drive the flow (pure HttpClient, no browser)

The authorization-code flow is just four HTTP hops we drive manually with one
cookie jar (it must be shared across steps — the challenge response sets the
nonce/state cookies, and the callback sends them back):

```csharp
var jar = new CookieContainer();
using var handler = new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = false };
using var http = new HttpClient(handler);

// 1. Challenge → 302 to the IdP's /authorize URL
var challenge = await http.GetAsync("http://localhost:8082/api/auth/login");
var authorize  = challenge.Headers.Location;                    // IdP login page

// 2. Load the mock login page (GET; nonce/state cookies land in `jar`)
var loginPage = await http.GetAsync(authorize);                 // 200 HTML form

// 3. Submit the mock's static username/password form
var body = new FormUrlEncodedContent(new[] {
    new KeyValuePair<string,string>("Username", "alice"),
    new KeyValuePair<string,string>("Password", "pw"),
});
var loginResp = await http.PostAsync(new Uri(authorize, "account/login"), body);
var callback = loginResp.Headers.Location;                      // http://app/signin-oidc?code=…&state=…

// 4. GET the callback → the middleware exchanges `code` server-side → sets the cookie
var cb = await http.GetAsync(callback);                         // Set-Cookie: .FdroidMgr.Auth

// 5. Authenticated request
var me = await http.GetAsync("http://localhost:8082/api/auth/me");  // 200 { username: "alice" }
```

> The login form's action/field names are mock-specific (`oidc-server-mock` can
> even be configured to auto-login and skip step 3, returning the 302 with `code`
> directly from step 2). No JavaScript is involved, so `HttpClient` suffices.

### 10.4 What this proves

- the correct `authorize` URL is issued (issuer, client_id, redirect_uri, scope, code_challenge/PKCE),
- token exchange + `id_token` validation succeed,
- the session cookie is set and `[Authorize]` + `/api/auth/me` work,
- a bad scope/token is rejected (negative case).

Add these same steps to the `e2e-repo-init`-style CI job (run API + oidc-server-mock
as two `--network=host` podman containers, drive with `HttpClient`), so the login
path is guarded against regressions without a real Keycloak.

---

*Status: COMPLETED — interactive OIDC (Keycloak) + `/config` mount + local
login removal. See commits: OIDC wiring (Program.cs/Cookie+OpenIdConnect),
one-time wizard (OAuthConfigService/AuthController), vanilla-JS first-run
wizard, and the unit/API/integration tests (in-process fake IdP) plus the
`e2e-oidc` CI job driving the real oidc-server-mock.*
