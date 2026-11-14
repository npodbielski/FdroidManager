// Browser regression checks for the web UI, run by `lightpanda run check.js`
// against the rig built by build.mjs (run.sh does both). Throwing fails the
// script, so every assertion below is a CI gate; the returned list is the
// record of what passed.
//
// Not covered here: Lightpanda opens no popup windows, so the sign-in handshake
// from the popup into the manager (postMessage, window.close, the opener
// reload) is checked only as far as the URL the popup is opened with. The
// server half of that flow is covered by the integration tests.

// run.sh exports LP_UI_RIG_BASE; Lightpanda substitutes it in the string
// arguments of page calls, so the placeholder stays in the URL until goto.
const BASE = "$LP_UI_RIG_BASE";

// Page-side helpers, prepended to every scenario body.
const PRELUDE = `
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function boot() { for (let i = 0; i < 200 && !window.Alpine; i++) await sleep(25); await sleep(600); }
const root = () => Alpine.$data(document.body);
// Views toggle with the .hidden class (:class bindings); x-show hints set display.
const shown = sel => { const el = document.querySelector(sel); return el ? !el.classList.contains("hidden") : "no-node"; };
const disp = sel => { const el = document.querySelector(sel); return el ? getComputedStyle(el).display : "no-node"; };
const val = sel => { const el = document.querySelector(sel); return el ? el.value : "no-node"; };
const txt = sel => { const el = document.querySelector(sel); return el ? el.textContent.trim() : "no-node"; };
const cards = () => Array.from(document.querySelectorAll("#app-list .card[data-pkg]")).map(e => e.dataset.pkg);
const calls = sub => (window.__calls || []).filter(c => c.indexOf(sub) >= 0);
`;

const passed = [];

function assert(name, condition, detail) {
  if (!condition) {
    throw new Error(`FAIL ${name} — got ${detail}`);
  }
  passed.push(name);
}

// Opens a rig URL, waits for the app to boot, runs the body page-side.
async function scenario(path, body) {
  const page = new Page();
  try {
    await page.goto(`${BASE}${path}`);
  } catch (error) {
    // An unexpanded placeholder (LP_UI_RIG_BASE unset) looks like a navigation error.
    throw new Error(`could not open ${path}: ${error} — run the checks through ci/ui-rig/run.sh`);
  }
  return JSON.parse(page.evaluate(`${PRELUDE}\n${body}`));
}

// A double component init (an x-init next to the init() lifecycle method) used
// to double every boot request.
const bootResult = await scenario("/ui.html", `
  await boot();
  return {
    oauth: calls("/api/auth/oauth/status").length, me: calls("/api/auth/me").length,
    init: calls("/api/repo/init/status").length, health: calls("/api/repo/status").length,
    apps: calls("/api/apps").length,
    workspace: shown("#workspace"), login: shown("#login"), initWizard: shown("#init-wizard"),
    cards: cards(), page: txt("#app-page-info"),
  };
`);
assert(
  "boot hits each boot endpoint exactly once",
  bootResult.oauth === 1 && bootResult.me === 1 && bootResult.init === 1 && bootResult.health === 1 && bootResult.apps === 1,
  JSON.stringify(bootResult),
);
assert(
  "a signed-in boot shows the app list",
  bootResult.workspace && !bootResult.login && !bootResult.initWizard && bootResult.cards.length === 13
    && bootResult.page === "Page 1",
  JSON.stringify(bootResult),
);

// A detail is a list of one app, so its route pins the page to 1 and asks for
// the package without a paging clause (#2 was a *list* route losing its page).
const detail = await scenario("/ui.html#app=net.idrobo.fdroidmgr&page=2", `
  await boot();
  return { cards: cards(), page: txt("#app-page-info"), prev: document.querySelector("#app-prev").disabled,
    next: document.querySelector("#app-next").disabled, query: calls("/api/apps?").slice(-1)[0] };
`);
assert(
  "detail deep link filters to its package and pins the page to 1",
  detail.cards.join() === "net.idrobo.fdroidmgr" && detail.page === "Page 1" && detail.prev && detail.next
    && detail.query.indexOf("where[x.packageName.contains(net.idrobo.fdroidmgr)]") >= 0 && detail.query.indexOf("skip[") < 0,
  JSON.stringify(detail),
);

// Search, sort and page are route state and reach the server as one HLinq query.
const searched = await scenario("/ui.html#q=fdroid&sort=name-desc&page=2", `
  await boot();
  return { term: val("#app-search"), sort: val("#app-sort"), page: txt("#app-page-info"),
    cards: cards(), query: decodeURIComponent(calls("/api/apps?").slice(-1)[0]) };
`);
assert(
  "list route restores search and sort into the controls",
  searched.term === "fdroid" && searched.sort === "name-desc" && searched.page === "Page 2",
  JSON.stringify(searched),
);
assert(
  "list route sends search, sort and offset as one query",
  searched.query.includes("StringComparison.InvariantCultureIgnoreCase")
    && searched.query.includes("orderByDescending[x.name]") && searched.query.includes("skip[25].take[26]"),
  JSON.stringify(searched),
);
const firstPage = await scenario("/ui.html#q=fdroid", `
  await boot();
  return { cards: cards(), page: txt("#app-page-info") };
`);
assert(
  "search renders its matches",
  firstPage.cards.join() === "org.fdroid.repo,net.idrobo.fdroidmgr" && firstPage.page === "Page 1",
  JSON.stringify(firstPage),
);

// The open panel is part of the route; without a panel the detail defaults to
// releases.
const panelRelease = await scenario("/ui.html#app=org.fdroid.repo&panel=releases", `
  await boot();
  const card = document.querySelector('#app-list .card[data-pkg="org.fdroid.repo"]');
  const panel = card ? card.querySelector(".panel") : null;
  const heading = panel ? panel.querySelector("h4") : null;
  return { action: panel ? panel.dataset.action : "no-panel", heading: heading ? heading.textContent.trim() : null,
    releases: calls("/api/apps/org.fdroid.repo/releases").length };
`);
assert(
  "panel deep link opens the releases panel",
  panelRelease.action === "releases" && panelRelease.heading === "Releases" && panelRelease.releases >= 1,
  JSON.stringify(panelRelease),
);
const panelDefault = await scenario("/ui.html#app=org.fdroid.repo", `
  await boot();
  const card = document.querySelector('#app-list .card[data-pkg="org.fdroid.repo"]');
  const panel = card ? card.querySelector(".panel") : null;
  return { action: panel ? panel.dataset.action : "no-panel" };
`);
assert("a detail without a panel shows releases", panelDefault.action === "releases", JSON.stringify(panelDefault));

// The health banner has three states; "never built" must not read as an error.
const healthy = await scenario("/ui.html?health=ok", `
  await boot();
  return { html: document.querySelector("#index-health").innerHTML };
`);
assert("healthy index shows up to date", healthy.html.includes("index up to date"), JSON.stringify(healthy));
const broken = await scenario("/ui.html?health=error", `
  await boot();
  return { html: document.querySelector("#index-health").innerHTML, details: !!document.querySelector("#show-error") };
`);
assert(
  "failed index shows the error with its details",
  broken.html.includes("repo init error") && broken.details,
  JSON.stringify(broken),
);
const unchecked = await scenario("/ui.html?health=unknown", `
  await boot();
  return { text: document.querySelector("#index-health").textContent, details: !!document.querySelector("#show-error") };
`);
assert(
  "an unchecked index is not reported as an error",
  unchecked.text.includes("health unknown") && !unchecked.details,
  JSON.stringify(unchecked),
);

// Sign-in: the popup URL, and the two hints that replace the button while
// pending or after a failure. window.open returning null is a blocked popup —
// the app must fall back to signing in in this tab.
const login = await scenario("/ui.html?auth=401", `
  await boot();
  const hidden = [disp("#login-wait"), disp("#login-failed")];
  root().loginState = "pending"; await sleep(150);
  const pending = [disp("#login-wait"), disp("#login-failed")];
  root().loginState = "failed"; await sleep(150);
  const failed = [disp("#login-wait"), disp("#login-failed")];
  root().loginState = ""; await sleep(100);
  let opened = null, replaced = null;
  window.open = (url, name, features) => { opened = { url, name, features }; return null; };
  try { Object.defineProperty(location, "replace", { configurable: true, value: url => { replaced = url; } }); } catch (e) {}
  root().login(); await sleep(200);
  return { login: shown("#login"), workspace: shown("#workspace"), hidden, pending, failed, opened, replaced, error: disp("#login-error") };
`);
assert("a signed-out boot shows the sign-in card, not the list", login.login && !login.workspace, JSON.stringify(login));
assert("login hints stay hidden until a login starts", login.hidden.join() === "none,none", JSON.stringify(login));
assert("a pending login shows the wait hint only", login.pending.join() === "block,none", JSON.stringify(login));
assert("a failed login shows the failure hint only", login.failed.join() === "none,block", JSON.stringify(login));
assert(
  "sign-in opens the provider in a named popup landing on the handshake page",
  !!login.opened && login.opened.url === "/api/auth/login?returnUrl=%2Flogin-done.html" && !!login.opened.name,
  JSON.stringify(login),
);
assert("a blocked popup signs in in this tab", login.replaced === "/api/auth/login", JSON.stringify(login));
assert("a sign-in card with no failed attempt shows no reason", login.error === "none", JSON.stringify(login));

// Why the last attempt failed is known only to the app, and the card has to
// repeat it — the popup can only say that nothing arrived.
const why = await scenario("/ui.html?auth=401&oauth=failed", `
  await boot();
  return { shown: disp("#login-error"), text: txt("#login-error") };
`);
assert(
  "the sign-in card repeats the provider failure it was given",
  why.shown === "block" && why.text.includes("Connection refused (idp:443)"),
  JSON.stringify(why),
);

// The popup's landing page reports the outcome. On success it closes itself,
// and after 300 ms the manual hint replaces the text, so either success string
// is accepted — the failure text never is.
const done = await scenario("/login-done.html", `
  return await new Promise(resolve => {
    const seen = [];
    const timer = setInterval(() => {
      seen.push(document.getElementById("msg").textContent);
      if (seen[seen.length - 1] !== "Finishing sign-in…") { clearInterval(timer); resolve({ text: seen[seen.length - 1] }); }
    }, 20);
    setTimeout(() => { clearInterval(timer); resolve({ text: "never left the initial text: " + seen.slice(-1)[0] }); }, 3000);
  });
`);
assert(
  "the landing page reports a successful sign-in",
  done.text.indexOf("did not complete") < 0 && (done.text.includes("Signed in") || done.text.includes("Close this window")),
  JSON.stringify(done),
);
const doneFailed = await scenario("/login-done.html?auth=401", `
  await boot();
  return { text: txt("#msg"), link: !!document.querySelector('#hint a[href="/ui.html"]') };
`);
assert(
  "the landing page reports a failed sign-in",
  doneFailed.text === "Sign-in did not complete." && doneFailed.link,
  JSON.stringify(doneFailed),
);

// First run: no OAuth provider configured takes the setup wizard, and an
// uninitialized repo takes the init wizard — neither may show the login card.
const wizard = await scenario("/ui.html?oauth=unconfigured", `
  await boot();
  return { wizard: shown("#oauth-wizard"), login: shown("#login") };
`);
assert("unconfigured OAuth shows the setup wizard", wizard.wizard && !wizard.login, JSON.stringify(wizard));
const uninitialized = await scenario("/ui.html?init=uninitialized", `
  await boot();
  return { init: shown("#init-wizard"), workspace: shown("#workspace") };
`);
assert("an uninitialized repo shows the init wizard", uninitialized.init && !uninitialized.workspace, JSON.stringify(uninitialized));

return { checks: passed.length, passed };
