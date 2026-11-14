#!/usr/bin/env node
// Assembles the browser rig: a verbatim copy of wwwroot with the fetch stub
// loaded before the page scripts, so ui.js runs against fake data with no API
// process. The markup itself is never edited — only <script> tags are inserted
// — so the rig loads exactly the shipped ui.html / login-done.html.
import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const outDir = resolve(process.argv[2] ?? join(here, "ui-rig"));
const wwwroot = resolve(process.argv[3] ?? join(here, "..", "..", "src", "FdroidMgr.Api", "wwwroot"));

// Each page gets the stub before the scripts that call fetch. Anchors are the
// shipped markup: no match means the page changed and the rig must be updated.
const pages = [
  { file: "ui.html", anchor: '<script defer src="/js/alpine.min.js"></script>' },
  { file: "login-done.html", anchor: "<script>" },
];

await rm(outDir, { recursive: true, force: true });
await mkdir(outDir, { recursive: true });
await cp(wwwroot, outDir, { recursive: true });
await cp(join(here, "fetch-stub.js"), join(outDir, "js", "fetch-stub.js"));

// A fresh URL per build: a rebuilt rig is never read from a stale HTTP cache.
const stamp = Date.now().toString(36);
for (const { file, anchor } of pages) {
  const path = join(outDir, file);
  const markup = await readFile(path, "utf8");
  if (!markup.includes(anchor)) {
    throw new Error(`${file}: anchor not found (${anchor}); update ci/ui-rig/build.mjs`);
  }
  const stub = `<script src="/js/fetch-stub.js?v=${stamp}"></script>`;
  const busted = markup
    .replace(anchor, `${stub}\n    ${anchor}`)
    .replaceAll('src="/js/ui.js"', `src="/js/ui.js?v=${stamp}"`)
    .replaceAll('src="/js/alpine.min.js"', `src="/js/alpine.min.js?v=${stamp}"`);
  await writeFile(path, busted);
}

console.log(`rig built in ${outDir} (stamp ${stamp})`);
