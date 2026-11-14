# Browser demo

Run the manager in a browser **without a real identity provider and without a
real F-Droid repository**: `demo.sh` publishes the API, seeds a demo repo,
starts a minimal OIDC provider next to it, and puts both behind one origin.

```
browser ──▶ :8090  (proxy.py — one origin)
              ├── /_idp/*  ──▶ :8084  (idp.py — demo identity provider)
              └── /*        ──▶ :8091  (fdroidmgr, published to work/publish)
```

Sign in at `http://<DEMO_IP>:<DEMO_FRONT_PORT>/ui.html` as **`alice` / `pw`**.

## Start and stop

```sh
demo/demo.sh          # build + seed + start; wipe demo/work on each run
demo/stop-demo.sh
```

Everything runs from `demo/work/` (gitignored): the publish output, the demo
repo, the SQLite database, the data-protection keys, the OIDC config file and
the logs (`work/logs/{app,idp,proxy}.log`).

| environment variable | default | meaning |
|---|---|---|
| `DEMO_IP` | `127.0.0.1` | address the services bind to and the issuer advertises — set a LAN address to browse from another device (phone) |
| `DEMO_FRONT_PORT` | `8090` | the single origin the browser talks to |
| `DEMO_APP_PORT` | `8091` | the manager |
| `DEMO_IDP_PORT` | `8084` | the demo IdP (only reachable through the front) |
| `DEMO_WORK` | `demo/work` | working directory, wiped on start |

Requirements: the dotnet SDK, `python3` with `cryptography` (the IdP signs
RS256 id_tokens). `apksigner` is optional — see "Rebuilding the index" below.

## What the demo is made of

**The demo repo is a hand-written index, not a real repository.**
`seed-repo.py` writes `config.yml` and a three-app `index-v1.json` (F-Droid,
Firefox, Aurora Store) directly — no APKs exist. The API reads that index
(`<repo>/repo/index-v1.json`) and the UI shows the three apps from it. This is
a UI preview: download links point at APKs that are not there.

`demo.sh` seeds it **after the manager starts**, and the seed also stamps the
database's "last successful build". The reason: on every startup the manager
regenerates the index with `fdroid update` whenever the repo is *initialized*
(a `config.yml` or keystore exists) — and with a real fdroidserver that means
the fixture is replaced by an empty index. With no `config.yml` at startup the
rebuild is skipped, and the seed then creates the fixture in the state the
UI expects.

**The demo IdP (`idp.py`) is deliberately cookie-less.** It is a ~200-line
authorization-code + PKCE provider (RS256 id_tokens, one user) that carries
the whole authorize request inside the login form, so it sets **no cookies at
all**. That is what makes it usable over plain HTTP from a LAN address, where
a real IdP's session cookie needs `SameSite=None; Secure` and every browser
refuses it. It auto-approves by default; the actual login form is served
behind `/_idp/authorize?...&form=1`.

**The front (`proxy.py`)** keeps the browser on a single origin: `/_idp/*`
goes to the IdP, everything else to the manager. One origin means one
cookie jar — no second-port policy surprises for the browser.

**OIDC wiring:** the manager's config file starts as `{}`, which leaves its
anonymous bootstrap endpoint open; `demo.sh` POSTs the provider to
`/api/auth/oauth` (same origin, `allowInsecure` because it is plain HTTP).
After that the endpoint closes and the config persists for the session's
life.

## Rebuilding the index

The **Rebuild index** button runs the real `fdroid update --rename-apks`,
which indexes what is *actually* in the repo — so it replaces the fixture
with an empty index. That is the button working, not a demo failure.

`seed-repo.py` checks for `apksigner` on the PATH:

- **present** — it generates a demo keystore (keytool) and writes the
  signing entries into `config.yml`, so the rebuild runs to completion
  (the index just comes back empty);
- **absent** — the config stays minimal and the rebuild reports the missing
  signing key, which at least exercises the error banner and the index error
  dialog.

Either way, running `demo/demo.sh` again restores the fixture.
