# Plan: Alpine.js migration, hash-based view state, SSO history hygiene

> **Status: ✅ done.** MR 1 (the Alpine migration) merged as `!18`; MR 2 (hash
> view state + SSO history hygiene) merged as `!19`; the popup-login follow-up
> below is implemented as well. `../../docs/dev/unocss.md` documents the stylesheet layer
> that now sits on this markup.

## Problem

1. The back button exits the app completely. Two separate causes:
   - **No in-app history.** Every "navigation" (app detail panel, statistics, API
     keys, S4 search/sort/paging) is an in-place DOM change in `ui.js` — nothing
     pushes a history entry, so back always jumps out of the app.
   - **SSO pollutes the tab history.** First login leaves
     `[login screen → /api/auth/login → keycloak.example.com/auth → keycloak.example.com/login →
     /signin-oidc → /api/auth/login]`. Going back replays `/signin-oidc` (its
     code is already consumed → IdP error page), lands on Keycloak pages, and
     `/api/auth/login` (the middleware LoginPath) re-challenges the IdP for an
     already-authenticated user.
2. `ui.js` (~1200 lines) is imperative `innerHTML`/DOM manipulation — growing
   harder to extend (the S4 features added a second stateful view).

Note: Alpine solves state/DOM reactivity, **not** history — parts 2 and 3 are
still required for the back-button fix.

## MR 1 — Alpine migration (behavior-preserving rewrite)

- Replace the imperative `wwwroot/js/ui.js` with Alpine components
  (`x-data` per view: login screen, app list, app detail, statistics, API keys).
- **No build step:** vendor `alpine.min.js` as `wwwroot/js/alpine.min.js`
  (single ~20 KB file). Fits the static `wwwroot` pipeline as-is; attribute-based
  (no `unsafe-eval` CSP issues).
- Keep the existing helpers — they are framework-agnostic: `api()`,
  `escapeHtml()`, `timeAgo()`, the 401 → `/api/auth/login` redirect.
- The S4 features (300 ms search debounce, sort, `skip/take` paging with the
  26-take next-page probe, empty state) become Alpine state — same HLinq query
  contract; the two existing UI query tests (`Where_Or_CaseInsensitive_…`,
  `FullUiQuery_WhereAndSortAndPage`) stay green.

Verification: the S4 Lightpanda rig (stub `window.api()`, drive the real page)
plus a back/forward/paging pass; existing API + `e2e-oidc` jobs.

Risk: it is a rewrite. Mitigate by keeping DOM structure/ids stable where
possible and running the full Lightpanda checklist before merging.

## MR 2 — hash-based view state + SSO history hygiene ✅

Shipped: the router lives in `wwwroot/js/ui.js` (`parseHash` / `hashFor` /
`navigate` → `applyRoute`, with one `hashchange` listener per document) and the
OIDC failure path in `OidcAuthExtensions.cs`.

- **Routes.** `#stats`, `#api-keys`, `#app=<pkg>&panel=<tab>` for the app
  detail, and the app-list state as `#q=…&sort=…&page=N`. User-driven changes
  push a history entry, search typing replaces one. The hash is the source of
  truth: `navigate` writes it and `applyRoute` renders it.
- **Detail is the workspace filtered to one package** (`where[x.packageName
  .contains(…)]`), so a deep link reaches an app on any page; a neighbouring
  package adds a card but never hides the linked one.
- **The open panel is route state.** Card tabs call `navigate({app, panel})`
  and the rendered panel is derived from the route, so back/forward rewinds it.
- **Only a changed query refetches.** A list↔detail step reuses the loaded page
  and the scroll position; `#q` / `#sort` / `#page` reload, and the search and
  sort inputs are re-synced from the route (never while the search box has
  focus, which would move the caret).
- **Hash input is validated** — package alphabet, known sorts and panels,
  `page ≥ 1`, sanitized `q` — before it reaches a query, a selector or an API
  path.
- **Failure path.** `Accept: text/html` gets a 302 to `/ui.html`, everything
  else a bare 400 JSON. Both answer as an already-handled response: an
  unhandled one makes the handler rethrow and the 4xx becomes a 500 on a
  started response.
- **Login history.** The sign-in button replaces the current entry, and boot
  rewrites it to `/ui.html` when arriving from a `/signin-oidc` referrer.
- **Bootstrap** is the `init()` method of the root `x-data` component, Alpine's
  lifecycle hook for it. Adding `x-init="init()"` on top gives two entry points
  and the whole boot sequence (OAuth status, identity, repo init status, app
  list) runs twice per page load.

Tests: stale `/signin-oidc` replay → 302 `/ui.html` for a browser, 400 JSON with
no session for anything else; `e2e-oidc` unchanged.

Verified with the Lightpanda rig (real `ui.html` + `ui.js`, only `fetch()`
stubbed): deep links to `#stats` / `#api-keys` / `#app=…&panel=releases`
including an app on page 2, tab switches re-rendering the panel, `hashchange`
rewinding it, `#q=…&sort=…&page=N` restoring both the controls and the exact
HLinq query, search typing replacing the entry (`history.length` stable) while
paging and detail push, malformed hashes (`#app=%3Cscript%3E`, `page=0`,
unknown sort/panel) dropped, and a `/signin-oidc` referrer rewritten to a clean
`/ui.html`. Lightpanda keeps no session-history entries for hash navigations,
so the back-button walk is covered through the `hashchange` path.

## Popup sign-in (follow-up, implemented)

Sign-in runs in a popup: `login()` opens `/api/auth/login?returnUrl=/login-done.html`,
the IdP round trip happens there, and the tab running the app never navigates.
Its history therefore holds no IdP pages at all, and its place in the UI
(search, page, open detail) survives the login instead of reloading.

- **Success.** `AuthenticationProperties.RedirectUri` carries the landing page
  through the protected state, so the popup ends on `wwwroot/login-done.html`:
  it posts `fdroidmgr:signed-in` to the opener and closes itself, showing a
  manual "continue" link if the close is refused. The session cookie is shared
  with the opener, which also polls `/api/auth/me` while the popup is open, so a
  lost message still finishes the login.
- **Failure.** The failure path redirects to the target of the flow that failed,
  so a popup failure lands on the same page and reports "Sign-in did not
  complete" rather than booting a second copy of the app. Non-browser callers
  still get the bare 400.
- **Popup blocked, or a browser that turns popups into tabs.** `window.open`
  returning nothing falls back to the full-page `location.replace` flow, and the
  login screen offers "sign in in this tab" while the popup is pending and after
  a failure.
- **`returnUrl` is site-relative only** (`/login-done.html` and friends); an
  absolute target is dropped, since it would make the sign-in an open redirect.
