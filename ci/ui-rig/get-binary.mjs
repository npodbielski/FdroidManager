#!/usr/bin/env node
// Downloads the Lightpanda binary and marks it executable. Node-only because
// the CI image has neither curl nor wget. Usage: node get-binary.mjs <url> <dest>
import { chmodSync } from "node:fs";
import { createWriteStream } from "node:fs";
import { rename } from "node:fs/promises";
import { Readable } from "node:stream";
import { pipeline } from "node:stream/promises";

const [url, dest] = process.argv.slice(2);
if (!url || !dest) {
  console.error("usage: node get-binary.mjs <url> <dest>");
  process.exit(2);
}

const response = await fetch(url, { redirect: "follow" });
if (!response.ok || !response.body) {
  console.error(`GET ${url} -> HTTP ${response.status}`);
  process.exit(1);
}
// Written aside, then moved: a failed download never leaves an executable stub.
await pipeline(Readable.fromWeb(response.body), createWriteStream(`${dest}.part`));
await rename(`${dest}.part`, dest);
chmodSync(dest, 0o755);
console.log(`${dest} ready`);
