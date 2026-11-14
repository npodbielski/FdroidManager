// Fake /api for the browser rig: ui.html loaded from a static server with no
// API process. Loaded before Alpine/ui.js by build.mjs. Shapes mirror the
// controllers (AppsController.List → AppListItem[], ReleasesController,
// RepoController.Status, AuthController), so a markup or contract change shows
// up here as a failing check rather than a silently empty card.
//
// Scenario flags come from the page URL query (the app's own state lives in the
// hash, so both combine):
//   ?auth=401            session expired / signed out
//   ?health=ok|error|unknown   repo index health banner
//   ?oauth=unconfigured  first-run OAuth wizard
//   ?init=uninitialized  init wizard
// Every request path is recorded on window.__calls, which is how the checks
// assert what the app actually asked the server for.
(() => {
  const flags = new URL(location.href).searchParams;
  const authStatus = Number(flags.get("auth") ?? 200);
  const health = flags.get("health") ?? "ok";

  const PKGS = [
    "org.fdroid.repo", "com.aurora.store", "net.idrobo.fdroidmgr",
    "org.telegram.messenger", "io.github.muntashirakon.AppManager",
    "org.briarproject.briar.android", "com.fsck.k9", "org.mozilla.firefox",
    "eu.siacs.conversations", "org.linphone", "net.gsantner.markor",
    "com.duckduckgo.mobile.android", "app.grapheneos.pdfviewer",
  ];
  const APPS = PKGS.map((packageName, i) => ({
    packageName,
    name: packageName.split(".").pop(),
    summary: `summary of ${packageName}`,
    license: "MIT",
    icon: null,
    versionCount: 2 + (i % 3),
    latestVersion: `1.${i}`,
    latestVersionCode: 100 + i,
  }));

  // AppsController.List takes an HLinq query string:
  //   where[…contains(term)…][.orderBy[x.name]][.skip[N].take[M]]
  // The detail route asks for one package and sends no paging clause.
  const apps = (query) => {
    const clause = /where\[([\s\S]*?)\](?:\.|$)/.exec(query);
    const skip = /skip\[(\d+)\]/.exec(query);
    const take = /take\[(\d+)\]/.exec(query);
    const order = /(orderBy|orderByDescending)\[x\.(\w+)\]/.exec(query);
    let items = APPS;
    if (clause) {
      const term = /contains\(([^,)]+)/.exec(clause[1])?.[1] ?? "";
      // The search clause is the OR over name/package/summary; the detail one
      // is a bare packageName.contains(pkg) and is matched literally.
      const fuzzy = clause[1].includes("StringComparison");
      const needle = fuzzy ? term.toLowerCase() : term;
      items = APPS.filter((a) => (fuzzy
        ? [a.name, a.packageName, a.summary].some((f) => f.toLowerCase().includes(needle))
        : a.packageName.includes(needle)));
    }
    if (order) {
      const key = order[2] === "name" ? "name" : "packageName";
      items = [...items].sort((a, b) => a[key].localeCompare(b[key]));
      if (order[1] === "orderByDescending") { items.reverse(); }
    }
    const from = skip ? Number(skip[1]) : 0;
    return items.slice(from, take ? from + Number(take[1]) : undefined);
  };

  const releases = (pkg) => [1, 2].map((n) => ({
    packageName: pkg, versionCode: 100 + n, versionName: `1.${n}`,
    apkFileName: `${pkg}_${100 + n}.apk`, sha256: "0".repeat(64), sizeBytes: 1000 + n,
    signerSha256: null, minSdk: 24, targetSdk: 35, permissions: [], changelog: null,
  }));

  const routes = {
    "GET /api/auth/oauth/status": () => (flags.get("oauth") === "unconfigured"
      ? { configured: false }
      : flags.get("oauth") === "failed"
        ? {
            configured: true,
            restartRequired: false,
            lastSignInError: "IDX20803: Unable to obtain configuration — Connection refused (idp:443)",
          }
        : { configured: true, restartRequired: false }),
    "GET /api/auth/me": () => ({ username: "rig@example.com", roles: ["admin"], scopes: [], avatar: null }),
    "POST /api/auth/logout": () => null,
    "GET /api/repo/init/status": () => (flags.get("init") === "uninitialized"
      ? { initialized: false, repoPath: "/repo", name: null, suggestedRepoUrl: "http://localhost:8080/repo" }
      : { initialized: true, repoPath: "/repo", name: "Rig repo", suggestedRepoUrl: "http://localhost:8080/repo" }),
    "GET /api/repo/status": () => ({
      repoName: "Rig repo",
      appCount: APPS.length,
      updatedUtc: "2026-09-18T10:00:00Z",
      lastIndexBuiltUtc: health === "ok" ? "2026-09-18T09:55:00Z" : health === "error" ? "2026-09-18T08:00:00Z" : null,
      lastIndexError: health === "error" ? "fdroid index failed: exit 1" : null,
      indexHealthy: health === "ok",
    }),
    "GET /api/apps": (query) => apps(query),
    "GET /api/stats/downloads": () => ({ totalDownloads: 42, daily: [], apps: [], releases: [] }),
  };

  window.__calls = [];
  window.fetch = (url, init = {}) => {
    const u = new URL(url, location.href);
    const method = (init.method || "GET").toUpperCase();
    window.__calls.push(`${method === "GET" ? "" : method + " "}${u.pathname}${u.search}`);
    const respond = (status, body) => Promise.resolve(new Response(body === null ? "" : JSON.stringify(body), {
      status, headers: { "content-type": "application/json" },
    }));
    if (u.pathname === "/api/auth/me" && authStatus !== 200) { return respond(authStatus, {}); }
    if (method === "GET") {
      const release = /^\/api\/apps\/([^/]+)\/releases(\/next-version)?$/.exec(u.pathname);
      if (release) {
        return release[2]
          ? respond(200, { packageName: release[1], previousVersionName: "1.2", previousVersionCode: 102, versionName: "1.3", versionCode: 103 })
          : respond(200, releases(release[1]));
      }
    }
    const handler = routes[`${method} ${u.pathname}`];
    // The app fetches icons and metadata too; a 404 there is the honest answer
    // for a rig that does not model them.
    return handler ? respond(200, handler(u.search.slice(1), init)) : respond(404, { error: "not in rig stub" });
  };
})();
