# Browser rig (`ci/ui-rig`)

`wwwroot/js/ui.js` is ~1200 lines of router and rendering with no unit-test
harness, and the C# integration tests cannot see it. This rig loads the real
`ui.html` / `login-done.html` and the real `ui.js` into a headless browser
against a stubbed `/api`, and asserts the behaviour that broke in the past.

Run it after touching `wwwroot` — the same command the pipeline runs:

    ci/ui-rig/run.sh

## Files

| file | role |
| --- | --- |
| `fetch-stub.js` | fake `/api`, loaded before Alpine; shapes mirror the controllers |
| `build.mjs` | copies `wwwroot` to a build dir and injects the stub (markup is never edited) |
| `serve.mjs` | static server, no dependencies, `no-store` |
| `check.js` | the checks (PandaScript); a failing assertion exits non-zero |
| `run.sh` | build → serve → check; exit status is the checks' status |
| `get-binary.mjs` | downloads the browser binary (the CI image has no curl) |

## Requirements

Node 20+, and the [Lightpanda](https://lightpanda.io) binary — on `PATH`, or
`LIGHTPANDA=/path/to/lightpanda ci/ui-rig/run.sh`. To fetch a copy:

    node ci/ui-rig/get-binary.mjs \
      https://github.com/lightpanda-io/browser/releases/download/nightly/lightpanda-x86_64-linux \
      ~/.local/bin/lightpanda

`PORT` (default 8899) moves the server; `RIG_DIR=/tmp/rig` keeps the built rig
for inspection (`http://127.0.0.1:8899/ui.html?auth=401#stats` style URLs are
handy, the scenario flags are documented at the top of `fetch-stub.js`).

## In the pipeline

The `ui-rig` job (stage `validate`) downloads the binary and runs the checks.
It runs on pipelines whose changes touch `wwwroot` or `ci/ui-rig`, and it is
`allow_failure: true`: the download is the unversioned nightly, so a browser
regression must not block a merge. Pin `LIGHTPANDA_URL` to a release asset and
drop `allow_failure` to make it a real gate.

## Adding a check

Add a `scenario(path, body)` + `assert(name, condition, detail)` pair to
`check.js`. The body runs inside the page with the helpers in `PRELUDE`
(`shown`/`disp` for the `.hidden`-class views and `x-show` hints, `cards`,
`calls` for the recorded requests). Assert on markup and on the requests the
app makes — those are the two things a refactor breaks.

When the API contract changes, update `fetch-stub.js` from the controllers in
the same MR. A stub that has drifted is worse than no stub: the checks would
pass against a fiction.

## What it cannot cover

- **Popups.** Lightpanda opens no popup windows, so the sign-in handshake
  (`postMessage` → `window.close` → opener reload) is checked only up to the URL
  the popup is opened with; the server half is covered by
  `test/FdroidMgr.Tests.Component/Endpoints/Auth/OAuthLoginIntegrationTests.cs`.
- **Real HTTP.** The stub answers with fixed bodies: auth expiry, concurrency,
  and server-side paging/sorting semantics are the integration tests' job.
- **Chrome.** Lightpanda is a separate engine: no canvas, no clipboard
  permissions, and its CSS support is a subset. Visual layout is not a
  regression target here — assert structure and requests, not pixels.
