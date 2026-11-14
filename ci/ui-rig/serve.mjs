#!/usr/bin/env node
// Static server for the rig: one directory, no dependencies, no caching (a
// rebuilt rig must not be served from the previous build's cache headers).
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, join, resolve, sep } from "node:path";

const [dirArg, portArg] = process.argv.slice(2);
const root = resolve(dirArg ?? ".");
const port = Number(portArg ?? 8899);

const types = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".ico": "image/x-icon",
  ".woff2": "font/woff2",
};

createServer(async (req, res) => {
  const relative = new URL(req.url, "http://localhost").pathname.replace(/^\/+/, "");
  const target = resolve(root, relative);
  if (target !== root && !target.startsWith(root + sep)) {
    res.writeHead(403).end();
    return;
  }
  try {
    const file = (await stat(target)).isDirectory() ? join(target, "index.html") : target;
    const info = await stat(file);
    res.writeHead(200, {
      "content-type": types[extname(file).toLowerCase()] ?? "application/octet-stream",
      "content-length": info.size,
      "cache-control": "no-store",
    });
    createReadStream(file).pipe(res);
  } catch {
    res.writeHead(404, { "content-type": "text/plain" }).end("not found");
  }
}).listen(port, "127.0.0.1", () => console.log(`rig serving http://127.0.0.1:${port} from ${root}`));
