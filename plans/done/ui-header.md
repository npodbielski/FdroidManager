# Header redesign — avatar, user menu & index status (PLAN)

> **Status: ✅ done** — the identity chip (avatar + fallback), its dropdown
> (API keys / sign out), the theme toggle and index health live in the header.
>
> Goal: put the operator's identity at the top-right corner (with their Keycloak
> avatar), move the theme/view-repo/index-health controls around it, and tuck
> "API keys" into a dropdown that opens from the avatar/user name.

## 1. Current state

`wwwroot/ui.html` header:

```html
<header>
  <h1>F-Droid Repository Manager</h1>
  <div class="header-actions">
    <button id="theme-toggle" class="ghost">🌙</button>
    <div id="authbar">
      <span id="userinfo"></span>
    </div>
  </div>
</header>
```

When signed in, `showWorkspace()` in `js/ui.js` populates `#authbar` with, in
DOM order:

1. `#repo-link` — "📦 View Repo" → `/fdroid/repo/index.html`
2. `#apikeys-link` — "🔑 API keys" → `showApiKeys()`
3. `#index-health` — index build status ("✓ index up to date · rebuilt 2h ago")

and sets `#userinfo` to `"👤 " + name` (a generic emoji, no avatar, no dropdown).

## 2. Goal

Right side of the header, left → right:

```
[h1 ...]  index-health   View Repo   [theme]   (avatar) username ▾
```

- Username + Keycloak avatar at the far right, top corner.
- Theme toggle immediately **left** of the avatar.
- "View Repo" button left of the theme toggle, with a small spacer.
- Index build status ("index up to date · rebuilt 2h ago") left of View Repo.
- Clicking the avatar **or** username opens a dropdown containing at least
  "API keys" (and, while we're there, "Sign out" — the `POST /api/auth/logout`
  endpoint already exists but is not wired into the UI today).

## 3. Avatar sourcing (Keycloak)

- `GET /api/auth/me` currently returns `{ username, roles, scopes }`.
- Add an `avatar` field: `User.FindFirst("picture")?.Value` (the OIDC `picture`
  claim, surfaced by the `profile` scope / userinfo endpoint).
- **Keycloak caveat:** Keycloak does not emit a `picture` claim by default. Two
  options (document both, pick one):
  1. Add a Keycloak protocol mapper (userinfo) that maps the user's avatar URL
     to `picture`. Keycloak account-avatar (WebAuthn avatar) is not a plain
     attribute; easiest is a mapper returning
     `${REALM_URL}/realms/<realm>/users/${user.id}/avatar/thumb.jpg` if the
     realm has the account avatar feature enabled.
  2. Store an avatar URL in the user profile attribute and map that attribute
     to `picture`.
- **Frontend fallback:** render the `<img>` with `onerror` → swap to a generic
  initials/placeholder avatar (keep the current 👤 as a last resort). Never
  block on the avatar failing.

> Out of scope: editing the avatar; non-Keycloak IdPs (Authentik/Zitadel already
> emit `picture` in userinfo, so the same `picture`-claim logic covers them).

## 4. UI restructure

### 4.1 `ui.html`

Replace the header block with a static skeleton (no more building links in JS):

```html
<header>
  <h1>F-Droid Repository Manager</h1>
  <div class="header-actions">
    <span id="index-health"></span>
    <a id="repo-link" class="ghost-btn" href="/fdroid/repo/index.html"
       target="_blank" rel="noopener">📦 View Repo</a>
    <button id="theme-toggle" class="ghost" aria-label="Toggle dark theme">🌙</button>
    <div class="user-menu" id="user-menu">
      <button id="user-menu-toggle" class="user-chip" aria-haspopup="menu" aria-expanded="false">
        <img id="user-avatar" alt="" hidden />
        <span id="user-name"></span>
        <span class="caret">▾</span>
      </button>
      <div id="user-menu-dropdown" class="user-menu-dropdown hidden" role="menu">
        <button id="menu-api-keys" role="menuitem">🔑 API keys</button>
        <button id="menu-signout" role="menuitem">Sign out</button>
      </div>
    </div>
  </div>
</header>
```

### 4.2 `js/ui.js`

- **`api()`** already redirects to `/api/auth/login` on 401; unchanged.
- **`me`** fetch at boot: also read `me.avatar`; call
  `setUser(name, avatarUrl)` → set `#user-name`, set `#user-avatar.src` (or hide
  + show a placeholder on error).
- **`showWorkspace(name, me)`** (or a new `renderHeader(me)`) stops inserting
  `repo-link` / `apikeys-link` / `index-health` / `userinfo` imperatively — those
  become static elements; only `#index-health` text + user identity are set.
- **Dropdown:** toggle `.hidden` on `#user-menu-dropdown`; close on outside
  click and `Escape`. `#menu-api-keys` → `showApiKeys()`; `#menu-signout` →
  `POST /api/auth/logout` then `window.location = "/"`.
- **`showApiKeys()` / `showLogin()` / `showOAuthWizard()` / `showInitWizard()`**
  update to hide the new user-menu when not signed in (and show it only in the
  workspace / api-keys page).
- **Index status** text stays driven by `loadIndexHealth()`; only its placement
  (a stationary element) changes.

### 4.3 `css/ui.css`

- `.header-actions { display:flex; align-items:center; gap:1rem; }` (already
  flexible; add a distinct `.spacer` / gap class as needed).
- `.user-chip` — transparent icon+name button (always-on-dark header), avatar
  `28–32px` circle with border, hover ring.
- `.user-menu` — `position:relative`; `.user-menu-dropdown` —
  `position:absolute; right:0; top:100%` card, shadows, z-index above content.
- `.ghost-btn` — same look as `.ghost` but as an `<a>` (View Repo).

## 5. Backend changes

- **`AuthController.Me()`**: add `avatar` (from the `picture` claim) to the
  response. No other server changes.
- Optional (nice-to-have, same PR): nothing else — avatar is fully client-side.

## 6. Files that change

- `src/FdroidMgr.Api/Controllers/AuthController.cs` — `avatar` in `/me`.
- `src/FdroidMgr.Api/wwwroot/ui.html` — static header + dropdown skeleton.
- `src/FdroidMgr.Api/wwwroot/js/ui.js` — header/dropdown/menu wiring.
- `src/FdroidMgr.Api/wwwroot/css/ui.css` — user-chip, dropdown, spacer.
- Tests: `AuthController`/`OAuthApiTests` or a quick integration assertion that
  `/me` includes `avatar` when the IdP emits `picture` (FakeOidcServer's
  `/userinfo` today returns `sub`, `preferred_username`, `name`, `email` — add
  `picture` there and assert it round-trips).

## 7. Verification

- `dotnet build` (0 errors), `node --check js/ui.js`.
- Manual: sign in → avatar+name at top-right; theme / View Repo / index status
  order correct; click avatar → dropdown with "API keys" + "Sign out"; API keys
  navigates and back works; avatar falls back gracefully when `picture` missing.
- Keycloak (after mapper): avatar is the real account avatar.

## 8. Out of scope

- Collapsing the `<h1>` / responsive mobile layout beyond the existing flex-wrap.
- Per-user settings/theme server sync (theme stays in localStorage).
