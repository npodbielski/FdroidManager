
const ICON_PLACEHOLDER = "/img/icon-placeholder.svg";

const APPS_PAGE_SIZE = 25;
const RELEASES_PAGE_SIZE = 10;

async function api(path, opts = {}) {
  const headers = { ...(opts.headers || {}) };
  if (opts.body && !(opts.body instanceof FormData)) headers["Content-Type"] = "application/json";

  const res = await fetch(path, { ...opts, headers });
  if (res.status === 401) {
    window.location = "/api/auth/login";
    throw new Error("unauthorized");
  }
  if (!res.ok) {
    let msg = res.statusText;
    try { const j = await res.json(); msg = j.error || msg; } catch {  }
    throw new Error(msg);
  }
  return res.status === 204 ? null : res.json();
}

function escapeHtml(s) {
  return String(s ?? "").replace(/[&<>"']/g, c => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  }[c]));
}

function timeAgo(iso) {
  if (!iso) return "";
  const t = new Date(iso).getTime();
  if (Number.isNaN(t)) return "";
  const min = Math.max(0, Math.round((Date.now() - t) / 60000));
  if (min < 1) return "just now";
  if (min < 60) return `${min}m ago`;
  const h = Math.floor(min / 60);
  const d = Math.floor(h / 24);
  if (d > 0) return `${d}d ago`;
  return `${h}h ago`;
}

function formatMb(bytes) {
  const n = Number(bytes);
  if (!Number.isFinite(n)) return "—";
  return (n / (1024 * 1024)).toFixed(1) + " MB";
}

function sanitizeSearchTerm(t) {
  return t.replace(/[()\[\],|&=<>+?#"%]/g, " ").replace(/\s+/g, " ").trim();
}

async function oauthStatus() {
  try {
    const r = await fetch("/api/auth/oauth/status");
    if (r.ok) return await r.json();
  } catch {  }
  return null;
}

async function isRepoInitialized() {
  try {
    const r = await fetch("/api/repo/init/status");
    if (r.ok) return !!(await r.json()).initialized;
  } catch {  }
  return true;
}

async function copyText(text, btnId) {
  const btn = document.getElementById(btnId);
  try {
    await navigator.clipboard.writeText(text);
    if (btn) btn.textContent = "Copied ✓";
  } catch {
    if (btn) btn.textContent = "Copy failed";
  }
}

function makeQrSvg(text) {
  if (typeof qrcode !== "function" || !text) return "";
  try {
    const qr = qrcode(0, "M");
    qr.addData(text);
    qr.make();
    return qr.createSvgTag({ cellSize: 6, margin: 6, scalable: true, alt: "F-Droid repo QR code" });
  } catch {
    return "";
  }
}

const WORKSPACE_HASH = "#workspace";

const LOGIN_POPUP_RETURN = "/login-done.html";
const LOGIN_POPUP_FEATURES = "width=480,height=640,scrollbars=yes";
const APP_SORTS = ["name-asc", "name-desc", "pkg-desc"];
const REL_SORTS = ["code-desc", "code-asc"];
const DETAIL_PANELS = ["releases", "metadata", "key", "icon"];

const PACKAGE_RE = /^[A-Za-z][A-Za-z0-9_.\-]*$/;

function parseHash(hash) {
  const route = { view: "workspace", app: "", panel: "", q: "", sort: "", page: 1, relq: "", relsort: "", relpage: 1 };
  let raw = "";
  try { raw = decodeURIComponent((hash || "").replace(/^#/, "")); } catch { return route; }
  if (raw === "stats" || raw === "api-keys") { route.view = raw; return route; }
  for (const pair of raw.split("&")) {
    const eq = pair.indexOf("=");
    if (eq <= 0) continue;
    const key = pair.slice(0, eq);
    const val = pair.slice(eq + 1);
    if (key === "q") route.q = sanitizeSearchTerm(val);
    else if (key === "sort" && APP_SORTS.includes(val)) route.sort = val;
    else if (key === "page" && /^[1-9][0-9]*$/.test(val)) route.page = Number(val);
    else if (key === "relq") route.relq = sanitizeSearchTerm(val);
    else if (key === "relsort" && REL_SORTS.includes(val)) route.relsort = val;
    else if (key === "relpage" && /^[1-9][0-9]*$/.test(val)) route.relpage = Number(val);
    else if (key === "app" && PACKAGE_RE.test(val)) route.app = val;
    else if (key === "panel" && DETAIL_PANELS.includes(val)) route.panel = val;
  }

  if (route.app) {
    if (!route.panel) { route.panel = "releases"; }
    route.page = 1;
  }
  return route;
}

function hashFor(route) {
  if (route.view === "stats") return "#stats";
  if (route.view === "api-keys") return "#api-keys";
  if (route.app) {

    const parts = [
      `app=${encodeURIComponent(route.app)}`,
      `panel=${encodeURIComponent(route.panel || "releases")}`,
    ];
    if (route.relq) parts.push(`relq=${encodeURIComponent(route.relq)}`);
    if (route.relsort && route.relsort !== "code-desc") parts.push(`relsort=${route.relsort}`);
    if (route.relpage > 1) parts.push(`relpage=${route.relpage}`);
    return "#" + parts.join("&");
  }
  const parts = [];
  if (route.q) parts.push(`q=${encodeURIComponent(route.q)}`);
  if (route.sort) parts.push(`sort=${route.sort}`);
  if (route.page > 1) parts.push(`page=${route.page}`);
  return parts.length ? "#" + parts.join("&") : WORKSPACE_HASH;
}

function locationHash() { return location.hash || WORKSPACE_HASH; }

function appRoot() { return Alpine.$data(document.body); }

async function loadIndexHealth() {
  const el = document.getElementById("index-health");
  if (!el) return;
  try {
    const r = await fetch("/api/repo/status");
    if (!r.ok) throw new Error("status unavailable");
    const s = await r.json();
    if (s.indexHealthy) {
      const ago = timeAgo(s.lastIndexBuiltUtc);

      const built = s.lastIndexBuiltUtc ? new Date(s.lastIndexBuiltUtc) : null;
      const full = built && !Number.isNaN(built.getTime()) ? ` title="rebuilt ${escapeHtml(built.toLocaleString())}"` : "";
      el.innerHTML = `<span class="ok"${full}>✓ index up to date${ago ? " · rebuilt " + escapeHtml(ago) : ""}</span>`;
    } else if (s.lastIndexError) {
      el.innerHTML = `<span class="err">⚠ repo init error</span>
        <button id="show-error" class="btn-link">details</button>
        <button id="rebuild-index" class="btn-link">Rebuild index</button>`;
      document.getElementById("show-error").onclick = () => showHealthError(s.lastIndexError);
      document.getElementById("rebuild-index").onclick = async () => {
        el.textContent = "Rebuilding…";
        try {
          await api("/api/repo/index/rebuild", { method: "POST" });
        } catch {  }
        await loadIndexHealth();
      };
    } else {
      el.innerHTML = `<span class="muted">index health unknown (no build recorded yet)</span>`;
    }
  } catch {
    el.textContent = "index health unknown";
  }
}

function showHealthError(message) {
  const dlg = document.getElementById("health-dialog");
  const body = document.getElementById("health-dialog-body");
  if (!dlg || !body) return;
  body.textContent = message || "";
  dlg.showModal();
}

function showChangelog(title, text) {
  const dlg = document.getElementById("changelog-dialog");
  const body = document.getElementById("changelog-dialog-body");
  if (!dlg || !body) return;
  const head = document.getElementById("changelog-dialog-title");
  if (head) head.textContent = title || "Changelog";
  body.textContent = text || "(no changelog)";
  dlg.showModal();
}

async function renderReleases(panel, pkg) {

  const route = appRoot().route;
  const relq = route.relq || "";
  const relsort = route.relsort || "";
  const page = route.relpage || 1;
  panel.innerHTML = `
    <h4>Releases</h4>
    <details class="summary-toggle">
      <summary>＋ Add release</summary>
      <form class="form-grid">
        <p class="col-full hint">Next version (auto): <strong class="next-version">…</strong></p>
        <label class="col-full">Changelog
          <textarea name="changelog" rows="4" placeholder="What changed in this release?"></textarea>
        </label>
        <div class="col-full">
          <label class="dropzone">
            <input type="file" name="apk" required accept=".apk" />
            <span class="dz-icon">📦</span>
            <span class="dz-title">drop here to upload</span>
            <span class="dz-file">or click to choose an .apk</span>
          </label>
        </div>
        <p class="col-full msg err hidden" id="publish-status"></p>
        <div class="col-full"><button type="submit" class="btn-primary">Publish release</button></div>
      </form>
    </details>
    <div class="toolbar-search">
      <input id="rel-search" type="search" placeholder="Filter by version name…" value="${escapeHtml(relq)}" />
      <select id="rel-sort" aria-label="Sort releases">
        <option value="code-desc"${relsort === "code-asc" ? "" : " selected"}>Newest first</option>
        <option value="code-asc"${relsort === "code-asc" ? " selected" : ""}>Oldest first</option>
      </select>
    </div>
    <table class="table-releases">
      <thead>
        <tr><th>Version</th><th>Code</th><th>Size</th><th>Changelog</th><th></th></tr>
      </thead>
      <tbody id="rel-list"></tbody>
    </table>
    <div id="rel-pager" class="pager hidden">
      <button id="rel-prev" class="btn-quiet" type="button">‹ Prev</button>
      <span id="rel-page-info" class="hint"></span>
      <button id="rel-next" class="btn-quiet" type="button">Next ›</button>
    </div>`;

  const listEl = panel.querySelector("#rel-list");
  const detailsEl = panel.querySelector(".summary-toggle");

  const versionHint = panel.querySelector(".next-version");
  api(`/api/apps/${pkg}/releases/next-version`)
    .then(nv => { if (nv && nv.versionName) versionHint.textContent = nv.versionName; })
    .catch(() => { versionHint.textContent = "—"; });

  const dropzone = panel.querySelector(".dropzone");
  const apkInput = dropzone.querySelector("input[type=file]");
  const dzFile = dropzone.querySelector(".dz-file");
  const updateDropzone = () => {
    const f = apkInput.files && apkInput.files[0];
    if (f) {
      dzFile.textContent = "Selected: " + f.name;
      dropzone.classList.add("dropzone-file");
    } else {
      dzFile.textContent = "or click to choose an .apk";
      dropzone.classList.remove("dropzone-file");
    }
  };
  apkInput.addEventListener("change", updateDropzone);
  for (const evt of ["dragenter", "dragover"]) {
    dropzone.addEventListener(evt, (e) => { e.preventDefault(); e.stopPropagation(); dropzone.classList.add("dragover"); });
  }
  for (const evt of ["dragleave", "drop"]) {
    dropzone.addEventListener(evt, (e) => { e.preventDefault(); e.stopPropagation(); dropzone.classList.remove("dragover"); });
  }
  dropzone.addEventListener("drop", (e) => {
    if (e.dataTransfer.files && e.dataTransfer.files.length) {
      apkInput.files = e.dataTransfer.files;
      updateDropzone();
    }
  });

  const relQuery = () => {
    const parts = [];
    if (relq) parts.push(`where[x.versionName.contains(${relq}, StringComparison.InvariantCultureIgnoreCase)]`);
    parts.push(relsort === "code-asc" ? "orderBy[x.versionCode]" : "orderByDescending[x.versionCode]");
    parts.push(`skip[${(page - 1) * RELEASES_PAGE_SIZE}].take[${RELEASES_PAGE_SIZE + 1}]`);
    return parts.join(".");
  };

  const load = async () => {
    const fetched = await api(`/api/apps/${pkg}/releases?${relQuery()}`);
    const hasMore = fetched.length > RELEASES_PAGE_SIZE;
    const rels = (hasMore ? fetched.slice(0, RELEASES_PAGE_SIZE) : fetched);

    if (page === 1 && !relq) detailsEl.open = rels.length === 0;
    panel.querySelector("#rel-pager").classList.toggle("hidden", !(rels.length > 0 || page > 1));
    panel.querySelector("#rel-page-info").textContent = "Page " + page;
    panel.querySelector("#rel-prev").disabled = page <= 1;
    panel.querySelector("#rel-next").disabled = !hasMore;
    if (rels.length === 0) {
      listEl.innerHTML = `<tr><td colspan="5" class="hint">${relq ? `No versions match “${escapeHtml(relq)}”.` : "No releases yet."}</td></tr>`;
    } else {
      listEl.innerHTML = rels.map(r => `<tr>
        <td>v${escapeHtml(r.versionName)}</td>
        <td>${r.versionCode}</td>
        <td>${formatMb(r.sizeBytes)}</td>
        <td>${r.changelog
          ? `<button class="btn-link" data-chlog="${r.versionCode}">view</button>`
          : '<span class="hint">—</span>'}
          <button class="btn-link" data-chlog-edit="${r.versionCode}">✎</button></td>
        <td><button class="btn-link btn-danger" data-rid="${r.versionCode}">✕</button></td>
      </tr>`).join("");
    }
    for (const d of listEl.querySelectorAll("[data-rid]")) {
      d.onclick = async () => {
        if (!confirm("Delete release?")) return;
        await api(`/api/apps/${pkg}/releases/${d.dataset.rid}`, { method: "DELETE" });
        load();
      };
    }
    for (const b of listEl.querySelectorAll("[data-chlog]")) {
      const rel = rels.find(r => r.versionCode === Number(b.dataset.chlog));
      if (!rel) continue;
      b.onclick = () => showChangelog(`v${rel.versionName} (code ${rel.versionCode})`, rel.changelog || "");
    }
    for (const b of listEl.querySelectorAll("[data-chlog-edit]")) {
      const rel = rels.find(r => r.versionCode === Number(b.dataset.chlogEdit));
      if (!rel) continue;
      b.onclick = () => editChangelog(pkg, rel, b.closest("tr"), load);
    }
  };

  panel.querySelector("#rel-prev").onclick = () => { if (page > 1) appRoot().navigate({ relpage: page - 1 }); };
  panel.querySelector("#rel-next").onclick = () => { if (!panel.querySelector("#rel-next").disabled) appRoot().navigate({ relpage: page + 1 }); };
  const relSearch = panel.querySelector("#rel-search");
  let relSearchTimer;
  relSearch.oninput = () => {
    clearTimeout(relSearchTimer);
    relSearchTimer = setTimeout(() => appRoot().navigate({ relq: sanitizeSearchTerm(relSearch.value), relpage: 1 }, { replace: true }), 300);
  };
  panel.querySelector("#rel-sort").onchange = (ev) => appRoot().navigate({ relsort: ev.target.value, relpage: 1 });

  const form = panel.querySelector("form");
  const status = panel.querySelector("#publish-status");
  const submitBtn = form.querySelector("button[type=submit]");
  form.onsubmit = async (ev) => {
    ev.preventDefault();
    status.classList.add("hidden");
    try {
      const fd = new FormData(ev.target);
      submitBtn.disabled = true;
      submitBtn.textContent = "Publishing…";
      await api(`/api/apps/${pkg}/releases`, { method: "POST", body: fd });
      ev.target.reset();
      updateDropzone();
      await load();
      loadIndexHealth();
    } catch (e) {
      status.textContent = e.message;
      status.classList.remove("hidden");
    } finally {
      submitBtn.disabled = false;
      submitBtn.textContent = "Publish release";
    }
  };
  await load();
}

function editChangelog(pkg, rel, tr, reload) {
  tr.innerHTML = `<td colspan="5"><form class="form-grid">
    <label class="col-full">Changelog for v${escapeHtml(rel.versionName)} (code ${rel.versionCode})
      <textarea name="changelog" rows="3">${escapeHtml(rel.changelog || "")}</textarea>
    </label>
    <div class="col-full">
      <button type="submit" class="btn-primary">Save</button>
      <button type="button" class="btn-link" data-cancel>Cancel</button>
    </div>
  </form></td>`;
  const form = tr.querySelector("form");
  tr.querySelector("[data-cancel]").onclick = () => reload();
  form.onsubmit = async (ev) => {
    ev.preventDefault();
    const text = new FormData(ev.target).get("changelog");
    await api(`/api/apps/${pkg}/releases/${rel.versionCode}`, {
      method: "PATCH",
      body: JSON.stringify({ changelog: (text || "").trim() ? text : null }),
    });
    await reload();
  };
}

async function renderIcon(panel, pkg) {
  panel.innerHTML = `
    <h4>App icon</h4>
    <p class="hint">Upload a PNG/JPEG. It is stored in the repo's metadata and indexed so the
    F-Droid client and this manager show it (preferred over the APK's launcher icon).</p>
    <form class="form-grid">
      <div class="col-full">
        <label class="dropzone">
          <input type="file" name="icon" required accept=".png,.jpg,.jpeg,image/png,image/jpeg" />
          <span class="dz-icon">🖼</span>
          <span class="dz-title">drop icon here to upload</span>
          <span class="dz-file">or click to choose a .png / .jpg</span>
        </label>
      </div>
      <p class="col-full msg hidden" id="icon-status"></p>
      <div class="col-full"><button type="submit" class="btn-primary">Upload icon</button></div>
    </form>`;

  const form = panel.querySelector("form");
  const status = panel.querySelector("#icon-status");
  const submitBtn = form.querySelector("button[type=submit]");

  const dropzone = panel.querySelector(".dropzone");
  const input = dropzone.querySelector("input[type=file]");
  const dzFile = dropzone.querySelector(".dz-file");
  const updateDropzone = () => {
    const f = input.files && input.files[0];
    if (f) {
      dzFile.textContent = "Selected: " + f.name;
      dropzone.classList.add("dropzone-file");
    } else {
      dzFile.textContent = "or click to choose a .png / .jpg";
      dropzone.classList.remove("dropzone-file");
    }
  };
  input.addEventListener("change", updateDropzone);
  for (const evt of ["dragenter", "dragover"]) {
    dropzone.addEventListener(evt, (e) => { e.preventDefault(); e.stopPropagation(); dropzone.classList.add("dragover"); });
  }
  for (const evt of ["dragleave", "drop"]) {
    dropzone.addEventListener(evt, (e) => { e.preventDefault(); e.stopPropagation(); dropzone.classList.remove("dragover"); });
  }
  dropzone.addEventListener("drop", (e) => {
    if (e.dataTransfer.files && e.dataTransfer.files.length) {
      input.files = e.dataTransfer.files;
      updateDropzone();
    }
  });

  form.onsubmit = async (ev) => {
    ev.preventDefault();
    status.className = "full msg hidden";
    const fd = new FormData(form);
    try {
      submitBtn.disabled = true;
      submitBtn.textContent = "Uploading…";
      await api(`/api/apps/${pkg}/icon`, { method: "POST", body: fd });
      status.textContent = "✓ icon uploaded and index regenerated";
      status.className = "full msg ok";
      updateDropzone();
      loadApps();
      loadIndexHealth();
    } catch (e) {
      status.textContent = e.message;
      status.className = "full msg err";
    } finally {
      submitBtn.disabled = false;
      submitBtn.textContent = "Upload icon";
    }
  };
}

async function renderKey(panel, pkg) {
  panel.innerHTML = `<h4>Signing key</h4>
    <p id="key-info">loading…</p>
    <button class="btn-link" id="gen-key">(re)generate key</button>
    <form class="form-grid" id="key-import" style="margin-top:0.6rem">
      <label>Keystore file <input type="file" name="keystore" required accept=".keystore,.jks,.pfx,.p12" /></label>
      <label>Store password <input type="password" name="storePassword" required /></label>
      <label>Key password <input type="password" name="keyPassword" placeholder="defaults to store password" /></label>
      <label>Alias <input name="alias" placeholder="release" /></label>
      <div class="col-full"><button type="submit" class="btn-primary">Import existing key</button></div>
    </form>`;
  const info = panel.querySelector("#key-info");

  const load = async () => {
    try {
      const app = await api(`/api/apps/${pkg}`);
      const keys = app.keys || [];
      info.innerHTML = keys.length === 0
        ? "No key yet."
        : keys.map(k => `Alias <code>${escapeHtml(k.alias)}</code> · branch <code>${escapeHtml(k.branch || "(default)")}</code> · fp <code>${escapeHtml(k.fingerprintSha256.slice(0, 16))}…</code>`).join("<br/>");
    } catch (e) { info.textContent = e.message; }
  };
  await load();

  panel.querySelector("#gen-key").onclick = async () => {
    if (!confirm("Generate a new key? If this app already has published releases this is refused — import the original key instead.")) return;
    info.textContent = "Generating…";
    try {
      const r = await api(`/api/apps/${pkg}/keys`, { method: "POST" });
      info.innerHTML = `Generated (alias <code>${escapeHtml(r.alias)}</code>) — fp <code>${escapeHtml(r.fingerprintSha256.slice(0, 16))}…</code>`;
    } catch (e) {

      info.innerHTML = `<span class="hint">${escapeHtml(e.message)}</span>`;
    }
  };

  panel.querySelector("#key-import").onsubmit = async (ev) => {
    ev.preventDefault();
    info.textContent = "Importing…";
    const fd = new FormData(ev.target);
    try {
      const r = await api(`/api/apps/${pkg}/keys/import`, { method: "POST", body: fd });
      info.innerHTML = `Imported (alias <code>${escapeHtml(r.alias)}</code>) — fp <code>${escapeHtml(r.fingerprintSha256.slice(0, 16))}…</code>`;
      ev.target.reset();
    } catch (e) {
      info.innerHTML = `<span class="hint">${escapeHtml(e.message)}</span>`;
    }
  };
}

async function renderMetadata(panel, pkg) {
  panel.innerHTML = `
    <h4>F-Droid metadata</h4>
    <div id="meta-media"></div>
    <p class="hint" id="meta-status"></p>
    <pre id="meta-yaml" class="pre-block"></pre>
    <div class="col-full" style="margin-top:0.6rem">
      <button id="meta-write" class="btn-primary">Write metadata file</button>
    </div>
    <details class="col-full" style="margin-top:0.6rem">
      <summary>Edit fields</summary>
      <form id="meta-edit" class="form-grid">
        <label>Name <input name="name" required maxlength="50" /></label>
        <label>Summary <input name="summary" maxlength="80" /></label>
        <label>License <input name="license" /></label>
        <label>Website <input name="website" /></label>
        <label>Source code <input name="sourceCode" /></label>
        <label>Issue tracker <input name="issueTracker" /></label>
        <label class="col-full">Description <textarea name="description" rows="3" maxlength="4000"></textarea></label>
        <div class="col-full"><button type="submit" class="btn-primary">Save changes</button></div>
      </form>
    </details>`;

  const pre = panel.querySelector("#meta-yaml");
  const status = panel.querySelector("#meta-status");
  const media = panel.querySelector("#meta-media");
  const form = panel.querySelector("#meta-edit");

  api(`/api/apps/${pkg}`).then((app) => {
    const shots = app.screenshots || [];
    media.innerHTML = shots.length === 0
      ? ""
      : `<div class="flex flex-wrap gap-2 mt-2">${shots.map((s) =>
          `<img class="screenshot" src="/fdroid/repo/${escapeHtml(s)}" alt="screenshot" loading="lazy"
            onerror="this.remove()">`).join("")}</div>`;
  }).catch(() => { media.innerHTML = ""; });

  const fillEditForm = (fields) => {
    form.elements.name.value = fields.name || "";
    form.elements.summary.value = fields.summary || "";
    form.elements.license.value = fields.license || "MIT";
    form.elements.description.value = fields.description || "";
    form.elements.website.value = fields.website || "";
    form.elements.sourceCode.value = fields.sourceCode || "";
    form.elements.issueTracker.value = fields.issueTracker || "";
  };

  const load = async () => {
    try {
      const res = await fetch(`/api/apps/${pkg}/metadata`);
      if (res.status === 401) { window.location = "/api/auth/login"; return; }
      if (res.status === 404) {
        pre.textContent = "(not written yet — publish an APK or click “Write metadata file”)";
        status.textContent = "";
        return;
      }
      if (!res.ok) {
        let msg = res.statusText;
        try { const j = await res.json(); msg = j.error || msg; } catch {  }
        throw new Error(msg);
      }
      const r = await res.json();
      pre.textContent = r.yaml || "";
      status.textContent = r.path ? "Current file: " + r.path : "";
      fillEditForm(parseMetadataFields(r.yaml));
    } catch (e) {
      status.textContent = "Error: " + e.message;
    }
  };
  await load();

  panel.querySelector("#meta-write").onclick = async () => {
    status.textContent = "Writing…";
    try {
      const r = await api(`/api/apps/${pkg}/metadata`, { method: "POST" });
      status.textContent = `✓ written to ${r.path}, index regenerated`;
      await load();
      loadIndexHealth();
    } catch (e) {
      status.textContent = "Write failed: " + e.message;
    }
  };

  form.onsubmit = async (ev) => {
    ev.preventDefault();
    status.textContent = "Saving…";
    try {

      await api(`/api/apps/${pkg}`, {
        method: "PUT",
        body: JSON.stringify({
          name: form.elements.name.value.trim(),
          summary: form.elements.summary.value.trim(),
          description: form.elements.description.value,
          license: form.elements.license.value.trim(),
          website: form.elements.website.value.trim(),
          sourceCode: form.elements.sourceCode.value.trim(),
          issueTracker: form.elements.issueTracker.value.trim(),
        }),
      });
      status.textContent = "✓ saved, index regenerated";
      await load();
      loadApps();
      loadIndexHealth();
    } catch (e) {
      status.textContent = "Save failed: " + e.message;
    }
  };
}

function parseMetadataFields(yaml) {
  const fields = { name: "", summary: "", license: "MIT", description: "", website: "", sourceCode: "", issueTracker: "" };
  const lines = (yaml || "").split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (line.trimEnd() === "Description: |") {
      const parts = [];
      while (i + 1 < lines.length && lines[i + 1].startsWith("  ")) { i++; parts.push(lines[i].slice(2)); }
      fields.description = parts.join("\n");
      continue;
    }
    const m = line.match(/^([A-Za-z]+):\s?(.*)$/);
    if (!m) continue;
    const val = unescapeYamlScalar(m[2]);
    if (m[1] === "Name") fields.name = val;
    else if (m[1] === "Summary") fields.summary = val;
    else if (m[1] === "Description") fields.description = val;
    else if (m[1] === "License") fields.license = val;
    else if (m[1] === "WebSite") fields.website = val;
    else if (m[1] === "SourceCode") fields.sourceCode = val;
    else if (m[1] === "IssueTracker") fields.issueTracker = val;
  }
  return fields;
}

function unescapeYamlScalar(v) {
  if (v.length >= 2 && v.startsWith('"') && v.endsWith('"')) {
    return v.slice(1, -1).replace(/\\(.)/g, (m, c) => c === "n" ? "\n" : c);
  }
  return v;
}

function loadApps() {
  const w = Alpine.$data(document.getElementById("workspace"));
  if (w) w.load();
}

function renderDailyChart(daily) {
  if (!daily || daily.length === 0) return '<p class="hint">No data yet.</p>';
  const max = Math.max(1, ...daily.map(d => d.downloads));
  const bars = daily.map((d, i) => {
    const h = Math.round((d.downloads / max) * 100);
    const dayLabel = (d.day || "").slice(5);
    return `<div class="chart-col" title="${escapeHtml(d.day)} — ${d.downloads.toLocaleString()}">
      <div class="chart-bar" style="height:${h}%"><span class="chart-count">${d.downloads.toLocaleString()}</span></div>
      <div class="chart-day">${escapeHtml(dayLabel)}</div>
    </div>`;
  }).join("");
  return `<div class="chart-bars">${bars}</div>`;
}

function renderAppStat(a, maxApp) {
  const name = a.name || a.packageName;
  const share = renderShareBar(a.downloads, maxApp);
  return `<div class="stat-row" title="${escapeHtml(a.packageName)}">
    <div class="stat-row-head"><strong>${escapeHtml(name)}</strong>
      <span class="stat-num">${a.downloads.toLocaleString()}</span></div>
    <div class="stat-row-sub">${escapeHtml(a.packageName)}
      ${a.latestVersionCode ? `· latest v${a.latestVersionCode}` : ""}</div>
    ${share}
  </div>`;
}

function renderReleaseStat(r) {
  return `<div class="stat-row">
    <div class="stat-row-head">
      <strong>${escapeHtml(r.packageName)} <code>v${r.versionCode}</code></strong>
      <span class="stat-num">${r.downloads.toLocaleString()}</span>
    </div>
    ${r.versionName ? `<div class="stat-row-sub">${escapeHtml(r.versionName)}</div>` : ""}
  </div>`;
}

function renderShareBar(downloads, maxDownloads) {
  const pct = maxDownloads > 0 ? Math.round((downloads / maxDownloads) * 100) : 0;
  return `<div class="share-bar"><div class="share-fill" style="width:${Math.max(0, Math.min(100, pct))}%"></div></div>`;
}

function renderApiKeyCard(k) {
  const revoked = !!k.revokedUtc;
  const expired = !revoked && k.expiresUtc && new Date(k.expiresUtc).getTime() <= Date.now();
  const scopes = (k.scopes || []).map(s => `<code>${escapeHtml(s)}</code>`).join(" ");
  const state = revoked
    ? '<span class="err">revoked</span>'
    : expired
      ? '<span class="err">expired</span>'
      : '<span class="ok">active</span>';

  return `<div class="card col gap-[0.4rem]">
    <div class="row">
      <strong>${escapeHtml(k.name)}</strong>
      <span class="text-muted text-[0.85rem]"><code>${escapeHtml(k.keyPrefix || "")}…</code></span>
      ${state}
    </div>
    <div class="text-[0.82rem] text-muted flex items-center gap-2 flex-wrap">${scopes}</div>
    <div class="text-[0.82rem] text-muted flex items-center gap-2 flex-wrap">
      created ${escapeHtml(timeAgo(k.createdUtc) || "just now")}
      ${k.lastUsedUtc ? "· used " + escapeHtml(timeAgo(k.lastUsedUtc)) : "· never used"}
      ${revoked ? "" : `<button class="btn-link btn-danger" data-revoke-id="${k.id}">revoke</button>`}
    </div>
  </div>`;
}

function renderFdroidKeyMasked(body, k) {
  body.innerHTML = `
    <div class="row">
      <code class="secret-block text-[0.78rem]">${escapeHtml(k.repoBasicAuthUrl)}</code>
      <button type="button" class="btn-primary" id="fdroid-key-copy-url">Copy URL</button>
    </div>
    <div class="row">
      <span class="text-muted text-[0.85rem]"><code>${escapeHtml(k.keyPrefix)}…</code></span>
      <button type="button" class="btn-primary" id="fdroid-key-show">Show key</button>
      <button type="button" class="btn-danger" id="fdroid-key-regenerate">Regenerate</button>
    </div>
    <div class="text-[0.82rem] text-muted hint">
      created ${escapeHtml(timeAgo(k.createdUtc) || "just now")}
      ${k.lastUsedUtc ? "· used " + escapeHtml(timeAgo(k.lastUsedUtc)) : "· never used"}
      ${k.lastRevealedUtc ? "· revealed " + escapeHtml(timeAgo(k.lastRevealedUtc)) : ""}
      ${k.regeneratedUtc ? "· regenerated " + escapeHtml(timeAgo(k.regeneratedUtc)) : ""}
    </div>`;
  document.getElementById("fdroid-key-copy-url").onclick = () => copyText(k.repoBasicAuthUrl, "fdroid-key-copy-url");
  document.getElementById("fdroid-key-show").onclick = showFdroidKey;
  document.getElementById("fdroid-key-regenerate").onclick = regenerateFdroidKey;
}

async function showFdroidKey() {
  const body = document.getElementById("fdroid-key-body");
  try {
    const r = await api("/api/fdroid-client-key/secret");
    renderFdroidKeySecret(body, r);
  } catch (e) {
    body.innerHTML = `<p class="msg err">${escapeHtml(e.message)}</p>`;
  }
}

async function regenerateFdroidKey() {
  if (!confirm("Regenerate the F-Droid client key?\nEvery enrolled client stops working until it gets the new key/URL.")) return;
  const body = document.getElementById("fdroid-key-body");
  try {
    const r = await api("/api/fdroid-client-key/regenerate", { method: "POST" });
    renderFdroidKeySecret(body, r);
  } catch (e) {
    body.innerHTML = `<p class="msg err">${escapeHtml(e.message)}</p>`;
  }
}

function renderFdroidKeySecret(body, r) {
  body.innerHTML = `
    <div class="row">
      <code class="secret-block">${escapeHtml(r.key)}</code>
      <button type="button" class="btn-primary" id="fdroid-key-copy">Copy key</button>
    </div>
    <div class="row">
      <code class="secret-block text-[0.78rem]">${escapeHtml(r.repoBasicAuthUrl)}</code>
      <button type="button" class="btn-primary" id="fdroid-key-copy-url">Copy URL</button>
    </div>
    <button type="button" class="btn-quiet" id="fdroid-key-hide">Hide</button>`;
  document.getElementById("fdroid-key-copy").onclick = () => copyText(r.key, "fdroid-key-copy");
  document.getElementById("fdroid-key-copy-url").onclick = () => copyText(r.repoBasicAuthUrl, "fdroid-key-copy-url");
  document.getElementById("fdroid-key-hide").onclick = loadFdroidClientKey;
}

async function loadFdroidClientKey() {
  const body = document.getElementById("fdroid-key-body");
  try {
    const k = await api("/api/fdroid-client-key");
    renderFdroidKeyMasked(body, k);
  } catch (e) {
    body.innerHTML = `<p class="msg err">${escapeHtml(e.message)}</p>`;
  }
}

function fdroidApp() {
  return {
    view: "login",
    route: parseHash(""),
    routeSeq: 0,
    authed: false,
    loginState: "",
    signInError: "",

    init() { this.boot(); },
    user: "",
    avatar: "",
    userMenuVisible: false,
    theme: "",
    oauthStatusMsg: "",
    initStatusMsg: "",
    initRepoUrlMsg: "",

    applyTheme(t) {
      this.theme = t;
      document.documentElement.dataset.theme = t;
      localStorage.setItem("fdroidmgr-theme", t);
      const btn = document.getElementById("theme-toggle");
      if (btn) {
        btn.textContent = t === "dark" ? "☀️" : "🌙";
        btn.title = t === "dark" ? "Switch to light theme" : "Switch to dark theme";
      }
    },
    toggleTheme() {
      this.applyTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark");
    },

    renderUser(name, avatarUrl) {
      this.user = name || "";
      this.avatar = avatarUrl || "";

      const img = document.getElementById("user-avatar");
      const fallback = document.getElementById("user-avatar-fallback");
      const initials = this.user.trim()[0];
      if (fallback) fallback.textContent = initials ? initials.toUpperCase() : "👤";

      if (!img) return;
      img.onerror = null;
      if (this.avatar) {
        img.hidden = false;
        if (fallback) fallback.hidden = true;
        img.onerror = () => {
          img.hidden = true;
          img.onerror = null;
          if (fallback) fallback.hidden = false;
        };
        img.src = this.avatar;
      } else {
        img.hidden = true;
        if (fallback) fallback.hidden = false;
      }
    },

    viewData(id) { return Alpine.$data(document.getElementById(id)); },

    withView(id, fn, tries = 0, seq = this.routeSeq) {
      const v = this.viewData(id);
      if (v) { fn(v); return; }
      if (seq !== this.routeSeq) { return; }
      if (tries < 50) { setTimeout(() => this.withView(id, fn, tries + 1, seq), 10); }
    },

    navigate(next, { replace = false } = {}) {
      const target = hashFor({ ...this.route, ...next });
      if (target !== locationHash()) {
        if (replace) history.replaceState(history.state, "", target);
        else location.hash = target;
      }
      this.applyRoute(parseHash(target));
    },

    applyHash() {
      if (!this.authed) return;
      const h = locationHash();
      if (h === hashFor(this.route)) return;
      this.applyRoute(parseHash(h));
    },

    applyRoute(r) {

      const seq = this.routeSeq = (this.routeSeq || 0) + 1;
      this.route = r;

      const section = r.view === "stats" ? "stats-page" : r.view === "api-keys" ? "api-keys-page" : "workspace";
      const entered = this.view !== section;
      this.setView(section, true);
      if (r.view === "stats") {
        this.withView("stats-page", v => v.load(), 0, seq);
      } else if (r.view === "api-keys") {
        this.withView("api-keys-page", v => v.load(), 0, seq);
      } else {

        if (entered) { loadIndexHealth(); }
        this.withView("workspace", v => v.applyRoute(r), 0, seq);
      }
    },

    showLogin() { this.setView("login", false); },
    showOAuthWizard() { this.setView("oauth-wizard", false); },
    showInitWizard() { this.setView("init-wizard", false); this.initRepoUrl(); },
    showWorkspace(name) {
      if (name) { this.user = name; }
      this.navigate({ view: "workspace", app: "", panel: "" });
    },
    showApiKeys() { this.navigate({ view: "api-keys" }); },
    showStats() { this.navigate({ view: "stats" }); },
    setView(id, authenticated) {
      this.view = id;
      this.userMenuVisible = !!authenticated;
    },

    login() {
      const popup = window.open(
        "/api/auth/login?returnUrl=" + encodeURIComponent(LOGIN_POPUP_RETURN),
        "fdroidmgr-login", LOGIN_POPUP_FEATURES);

      if (!popup) { this.loginInThisTab(); return; }
      this.loginState = "pending";
      this.watchLoginPopup(popup);
    },

    loginInThisTab() { location.replace("/api/auth/login"); },

    async refreshSignInError() {
      const s = await oauthStatus();
      if (s && s.lastSignInError) { this.signInError = s.lastSignInError; }
    },

    watchLoginPopup(popup) {
      let timer = 0;
      const stop = () => {
        clearInterval(timer);
        window.removeEventListener("message", onMessage);
      };
      const finish = () => {
        stop();
        try { popup.close(); } catch {  }
        location.reload();
      };
      const fail = () => { stop(); this.loginState = "failed"; this.refreshSignInError(); };

      const giveUp = () => { clearInterval(timer); this.loginState = "failed"; this.refreshSignInError(); };
      const onMessage = (e) => {
        if (e.source !== popup || e.origin !== location.origin) return;
        if (e.data && e.data.type === "fdroidmgr:signed-in") { finish(); }
      };

      window.addEventListener("message", onMessage);
      let polls = 0;
      timer = setInterval(async () => {
        if (popup.closed) { fail(); return; }
        if (++polls > 450) { giveUp(); return; }
        try { if ((await fetch("/api/auth/me", { cache: "no-store" })).ok) { finish(); } } catch {  }
      }, 700);
    },

    async signOut() {
      try {

        await fetch("/api/auth/logout", { method: "POST", redirect: "follow" });
      } catch {  }
      window.location.assign("/");
    },

    async submitOauth(ev) {
      ev.preventDefault();
      const fd = new FormData(ev.target);
      this.oauthStatusMsg = "Saving…";
      try {
        const r = await fetch("/api/auth/oauth", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            authority: fd.get("authority"),
            clientId: fd.get("clientId"),
            scopes: (fd.get("scopes") || "").trim().split(/[\s,]+/).filter(Boolean),
            allowInsecure: fd.get("allowInsecure") === "on",
          }),
        });
        if (!r.ok) {
          let msg = r.statusText;
          try { const j = await r.json(); msg = j.error || msg; } catch {  }
          throw new Error(msg);
        }
        this.oauthStatusMsg = "✓ Saved — SSO is active. Reloading…";
        setTimeout(() => window.location.reload(), 400);
      } catch (e) {
        this.oauthStatusMsg = "Save failed: " + e.message;
      }
    },

    async submitInit(ev) {
      ev.preventDefault();
      const fd = new FormData(ev.target);
      this.initStatusMsg = "Initializing… (this can take a minute)";
      try {
        await api("/api/repo/init", {
          method: "POST",
          body: JSON.stringify({
            repoName: fd.get("repoName"),
            repoDescription: fd.get("repoDescription"),
            keyAlias: (fd.get("keyAlias") || "").trim() || null,
          }),
        });
        this.initStatusMsg = "Repository initialized! Reloading…";
        window.location.reload();
      } catch (e) {
        this.initStatusMsg = "Init failed: " + e.message;
      }
    },

    async initRepoUrl() {
      this.initRepoUrlMsg = "";
      try {
        const r = await fetch("/api/repo/init/status");
        if (r.ok) {
          const s = await r.json();
          if (s.suggestedRepoUrl) {
            this.initRepoUrlMsg = "Repo URL (from your public sign-in host): " + s.suggestedRepoUrl;
          }
        }
      } catch {  }
    },

    async boot() {
      this.applyTheme(localStorage.getItem("fdroidmgr-theme")
        || (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"));
      this.dropCallbackFromHistory();

      const oauth = await oauthStatus();
      this.signInError = oauth && oauth.lastSignInError ? oauth.lastSignInError : "";
      if (!oauth || !oauth.configured) {
        if (oauth) this.showOAuthWizard(); else this.showLogin();
        return;
      }

      try {
        const r = await fetch("/api/auth/me");
        if (r.ok) {
          const me = await r.json();
          this.renderUser(me.username, me.avatar);
          if (await isRepoInitialized()) {
            this.authed = true;
            this.applyRoute(parseHash(location.hash));
          } else {
            this.showInitWizard();
          }
          return;
        }
      } catch {  }
      this.showLogin();
    },

    dropCallbackFromHistory() {
      if ((document.referrer || "").indexOf("/signin-oidc") < 0) return;
      history.replaceState(history.state, "", "/ui.html");
    },
  };
}

function workspaceView() {
  return {
    q: "",
    sort: "",
    page: 1,

    relq: "",
    relsort: "",
    relpage: 1,

    onlyPkg: "",
    loaded: false,
    apps: [],
    hasMore: false,
    loading: false,
    error: "",

    appViewQuery() {
      const parts = [];
      if (this.onlyPkg) {
        return `where[x.packageName.contains(${this.onlyPkg})]`;
      }
      if (this.q) {

        const t = this.q;
        parts.push(`where[(x.name.contains(${t}, StringComparison.InvariantCultureIgnoreCase) || x.packageName.contains(${t}, StringComparison.InvariantCultureIgnoreCase) || x.summary.contains(${t}, StringComparison.InvariantCultureIgnoreCase))]`);
      }
      const sort = {
        "name-asc": "orderBy[x.name]",
        "name-desc": "orderByDescending[x.name]",
        "pkg-desc": "orderByDescending[x.packageName]",
      }[this.sort];
      if (sort) parts.push(sort);

      parts.push(`skip[${(this.page - 1) * APPS_PAGE_SIZE}].take[${APPS_PAGE_SIZE + 1}]`);
      return parts.join(".");
    },

    get emptyMsg() {
      return this.q !== "" ? `No apps match “${this.q}”.` : "No apps yet — register one above.";
    },

    async load() {
      this.loading = true;
      try {
        const fetched = await api(`/api/apps?${this.appViewQuery()}`);
        this.error = "";
        this.hasMore = fetched.length > APPS_PAGE_SIZE;
        const apps = (this.hasMore ? fetched.slice(0, APPS_PAGE_SIZE) : fetched);
        this.apps = apps.map(a => ({ ...a, panel: this.panelFor(a), iconBroken: false }));
      } catch (e) {
        this.error = e.message;
        this.apps = [];
      } finally {
        this.loading = false;
        this.loaded = true;

        const panel = document.getElementById("create-app-panel");
        if (panel && this.q === "" && this.onlyPkg === "") { panel.open = this.apps.length === 0; }
      }
    },

    panelFor(a) {
      const r = appRoot()?.route;
      if (!r || r.view !== "workspace" || r.app !== a.packageName) { return undefined; }
      return r.panel || "releases";
    },

    applyRoute(r) {
      const listChanged = !this.loaded
        || this.q !== r.q || this.sort !== r.sort || this.page !== r.page || this.onlyPkg !== r.app;
      const relChanged = this.relq !== r.relq || this.relsort !== r.relsort || this.relpage !== r.relpage;
      this.q = r.q;
      this.sort = r.sort;
      this.page = r.page;
      this.onlyPkg = r.app;
      this.relq = r.relq;
      this.relsort = r.relsort;
      this.relpage = r.relpage;
      this.syncControls();
      if (!listChanged && !relChanged) {
        for (const a of this.apps) {
          a.panel = r.app && r.app === a.packageName ? (r.panel || "releases") : undefined;
        }
        this.revealDetail();
        return;
      }
      if (listChanged) {
        this.load().then(() => this.revealDetail());
        return;
      }

      this.$nextTick(() => {
        const el = document.querySelector("#app-list .panel.card");
        if (el && r.app) { this.openPanel(r.panel || "releases", r.app, el); }
      });
    },

    syncControls() {
      const search = document.getElementById("app-search");

      if (search && document.activeElement !== search) { search.value = this.q; }
      const sort = document.getElementById("app-sort");
      if (sort) { sort.value = this.sort; }
    },

    revealDetail() {
      if (!this.onlyPkg) { return; }
      this.$nextTick(() => {
        const card = document.querySelector(`#app-list [data-pkg="${this.onlyPkg}"]`);
        if (card) { card.scrollIntoView({ block: "nearest" }); }
      });
    },

    onSearchInput(ev) {
      appRoot().navigate({ q: sanitizeSearchTerm(ev.target.value), page: 1, app: "", panel: "" }, { replace: true });
    },
    onSortChange(ev) {
      appRoot().navigate({ sort: ev.target.value, page: 1 });
    },

    prevPage() { if (this.page > 1) { appRoot().navigate({ page: this.page - 1, app: "", panel: "" }); } },
    nextPage() { if (this.hasMore) { appRoot().navigate({ page: this.page + 1, app: "", panel: "" }); } },

    iconSrc(app) {
      if (!app.icon || app.iconBroken) { return ICON_PLACEHOLDER; }
      return `/fdroid/repo/${app.icon}`;
    },

    async cardAction(action, pkg) {

      if (action === "delete") {
        if (confirm("Delete this app and all its releases?")) {
          await api(`/api/apps/${pkg}`, { method: "DELETE" });
          this.load();
        }
        return;
      }
      const r = appRoot().route;
      if (r.app === pkg && r.panel === action) { appRoot().navigate({ app: "", panel: "" }); }
      else if (r.app && r.app !== pkg) {

        appRoot().navigate({ app: pkg, panel: action, relq: "", relsort: "", relpage: 1 });
      }
      else { appRoot().navigate({ app: pkg, panel: action }); }
    },

    async openPanel(action, pkg, el) {
      if (action === "releases") await renderReleases(el, pkg);
      else if (action === "metadata") await renderMetadata(el, pkg);
      else if (action === "key") await renderKey(el, pkg);
      else if (action === "icon") await renderIcon(el, pkg);
    },

    async submitCreate(ev) {
      ev.preventDefault();
      const fd = new FormData(ev.target);
      await api("/api/apps", { method: "POST", body: JSON.stringify({
        packageName: fd.get("packageName"), name: fd.get("name"), summary: fd.get("summary"),
        license: fd.get("license"), website: fd.get("website"), sourceCode: fd.get("sourceCode"),
        description: fd.get("description"), generateKey: fd.get("generateKey") === "on",
      })});
      ev.target.reset();
      this.load();
    },
  };
}

function statsView() {
  return {
    data: null,
    error: "",

    get total() { return this.data ? (this.data.totalDownloads || 0).toLocaleString() : "—"; },
    get chart() { return this.data ? renderDailyChart(this.data.daily || []) : ""; },
    get maxApp() { return Math.max(1, ...((this.data && this.data.apps) || []).map(a => a.downloads)); },
    get appRows() { return (this.data && this.data.apps) || []; },
    get releaseRows() { return (this.data && this.data.releases) || []; },
    get appsEmpty() {
      if (this.error) { return "Failed to load statistics: " + this.error; }
      return this.appRows.length ? "" : "No downloads yet.";
    },
    get releasesEmpty() { return this.releaseRows.length ? "" : "No downloads yet."; },

    async load() {
      try {
        this.data = await api("/api/stats/downloads");
        this.error = "";
      } catch (e) {
        this.error = e.message;
        this.data = null;
      }
    },
  };
}

function apiKeysView() {
  return {
    keys: [],
    error: "",

    async load() {
      try {
        this.keys = await api("/api/api-keys");
        this.error = "";
      } catch (e) {
        this.error = e.message;
        this.keys = [];
      }

      this.$nextTick(() => this.wireRevoke());
      loadFdroidClientKey();
    },

    wireRevoke() {
      const list = document.getElementById("api-key-list");
      for (const el of list.querySelectorAll("[data-revoke-id]")) {
        el.onclick = onRevokeApiKey;
      }
    },

    async submitCreate(ev) {
      ev.preventDefault();
      const fd = new FormData(ev.target);
      const scopes = fd.getAll("scope");
      if (scopes.length === 0) {
        revealApiKeyError("Select at least one scope.");
        return;
      }
      const reveal = document.getElementById("api-key-reveal");
      reveal.classList.add("hidden");
      try {
        const body = {
          name: fd.get("name"),
          scopes,
          expiresDays: (fd.get("expiresDays") || "").trim() ? Number(fd.get("expiresDays")) : null,
        };
        const r = await api("/api/api-keys", { method: "POST", body: JSON.stringify(body) });
        revealSecret(reveal, r);
        ev.target.reset();
        this.load();
      } catch (e) {
        revealApiKeyError(e.message);
      }
    },
  };
}

async function onRevokeApiKey(e) {
  const id = e.currentTarget.dataset.revokeId;
  if (!confirm("Revoke this API key? It will stop working immediately.")) return;
  try {
    await api(`/api/api-keys/${id}`, { method: "DELETE" });
    Alpine.$data(document.getElementById("api-keys-page")).load();
  } catch (err) {
    document.getElementById("api-key-list").innerHTML = `<p class="msg err">${escapeHtml(err.message)}</p>`;
  }
}

function revealApiKeyError(message) {
  const reveal = document.getElementById("api-key-reveal");
  reveal.classList.remove("hidden");
  reveal.innerHTML = `<p class="msg err">${escapeHtml(message)}</p>`;
}

function revealSecret(reveal, r) {
  reveal.classList.remove("hidden");
  const url = r.repoBasicAuthUrl || "";
  const qrSvg = makeQrSvg(url);
  reveal.innerHTML = `
    <h3>API key created</h3>
    <p class="hint">Copy the secret or scan the QR code with the F-Droid client. The secret is shown once and cannot be recovered.</p>
    <div class="row">
      <code class="secret-block">${escapeHtml(r.key)}</code>
      <button type="button" class="btn-primary" id="api-key-copy">Copy secret</button>
    </div>
    ${url ? `
    <div class="flex items-start gap-4 mt-[0.9rem] flex-wrap">
      <div class="qr-frame" aria-label="F-Droid repo QR code">${qrSvg}</div>
      <div class="flex-1 min-w-200px">
        <p class="hint">Scan to add the repo (Basic auth embedded)</p>
        <code class="url-line">${escapeHtml(url)}</code>
        <button type="button" class="btn-link" id="api-key-copy-url">Copy URL</button>
      </div>
    </div>` : ""}`;
  document.getElementById("api-key-copy").onclick = () => copyText(r.key, "api-key-copy");
  const copyUrl = document.getElementById("api-key-copy-url");
  if (copyUrl) copyUrl.onclick = () => copyText(url, "api-key-copy-url");
}

function initDialogs() {
  const healthDialog = document.getElementById("health-dialog");
  if (healthDialog) {
    document.getElementById("health-dialog-close").onclick = () => healthDialog.close();
    healthDialog.addEventListener("click", (e) => { if (e.target === healthDialog) healthDialog.close(); });
  }
  const changelogDialog = document.getElementById("changelog-dialog");
  if (changelogDialog) {
    document.getElementById("changelog-dialog-close").onclick = () => changelogDialog.close();
    changelogDialog.addEventListener("click", (e) => { if (e.target === changelogDialog) changelogDialog.close(); });
  }
}

window.addEventListener("hashchange", () => {
  const root = appRoot();
  if (root) { root.applyHash(); }
});

initDialogs();

