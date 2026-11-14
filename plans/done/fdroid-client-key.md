# Plan: dedicated "F-Droid Client API Key" (always re-displayable)

> **Status: ✅ done** — merged as MR `!17` (`feat/fdroid-client-key`). The
> singleton key, its `InteractiveOnly` API and the API-keys page card (masked
> URL · reveal · regenerate) are in `master`; `FdroidClientKeyApiTests` pins
> the surface, and the card reveal / regenerate flow was driven in a browser.

## Problem

Regular API keys store the secret as **SHA-256 only**
(`ApiKeyService.Hash`, + a 10-char `KeyPrefix`). Re-display is cryptographically
impossible — so every new F-Droid client enrollment forces generating a new key
(the client needs the `user:key` Basic-auth repo URL).

## Design: a special singleton key, purpose `fdroid-client`

A dedicated key type — not "any read-only key is re-displayable":

- **One special key** (singleton row), purpose `fdroid-client`, scope pinned to
  `read`.
- Secret stored **encrypted at rest** (AES-GCM under `FDROID_MASTER_KEY` — the
  same mechanism that already protects the OIDC client secret and the
  signing-key password).
- **Lifecycle:** create once → always revealable → **regenerate** on demand
  (new secret; every client gets the new URL; the old one dies immediately).
- Tracking: `lastUsedUtc` (the repo-auth path already sees the key) and
  `lastRevealedUtc`.
- **Regular API keys are untouched** — SHA-256, one-time display, as today.

## API (all `InteractiveOnly` — SSO session only, so no key can reveal the key)

| Endpoint | Behavior |
|---|---|
| `GET /api/fdroid-client-key` | user + prefix + full repo URL (key masked) |
| `GET /api/fdroid-client-key/secret` | plaintext + records `lastRevealedUtc` |
| `POST /api/fdroid-client-key/regenerate` | new secret (old invalidated) |

## UI

A dedicated **F-Droid Client Access** card:

- full repo URL (`https://user:key@…/fdroid/repo`),
- masked key with an explicit "show" click,
- copy button,
- "Regenerate" with a confirm warning (all clients need the new key).

## Security assessment

- The key grants **repo-content read only** (APKs + index) under
  `FDROID_REQUIRE_REPO_AUTH`. That content is client-consumable by design — the
  auth is privacy (keep the repo off public crawlers / unenrolled devices), not
  secrecy. No API writes, no signing, no user data: `/fdroid/repo/**` is
  content-only; API endpoints need a session or higher scopes.
- **Devices already hold the plaintext** — the F-Droid client stores the
  Basic-auth URL locally. UI re-display is a small step up from that reality,
  not a category change.
- Exposure surface: the user's SSO session (reveal is session-only, masked by
  default) and the `FDROID_MASTER_KEY`'s existing blast radius (compose env +
  data volume leak already recovers the signing key and OIDC secret).
- Rotation is one click instead of forced per-client churn; `lastUsedUtc` makes
  a stale key suddenly in use visible.

## Schema

`ApiKeys` (or a dedicated table — decide at implementation; a dedicated
`FdroidClientKey` table keeps the regular-key model untouched): `SecretCipher`
(TEXT, NULL for legacy/regular keys), `LastUsedUtc`, `LastRevealedUtc`.

## Tests (integration)

- Create → reveal → regenerate → **old secret rejected** and **new accepted**
  for `/fdroid/repo/**` auth.
- Reveal is `403` for key-authenticated callers (`InteractiveOnly`).
- Regular keys' behavior untouched (one-time display, prefix-only listing —
  existing `ApiKeysHlinqApiTests` stay green).

## Alternatives (rejected)

- One-click "create + auto-revoke old" — still per-client friction.
- mTLS / signed URLs — poor F-Droid client support.
