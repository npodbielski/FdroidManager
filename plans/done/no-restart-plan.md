# SSO Configuration Takes Effect Without Container Restart

## Current Setup
- **SSO settings** → saved to `/srv/fdroid-mgr/config/appsettings.json`
- **Config file** → mounted into container at `/config/appsettings.json`
- **Container** → reads config from mounted file (not environment variables)

## Why No Restart Is Needed (implemented)
1. **SSO config is file-based** — settings are written to `appsettings.json` by the web UI
2. **File is watched** — `Program.cs` loads it with `reloadOnChange: true`, and seeds
   an empty `{}` file at boot when absent so the file watcher always attaches to a
   real file (the wizard's first write creates the file)
3. **The OIDC handler is live** — the scheme is registered unconditionally and
   `DynamicOpenIdConnectHandler` resolves the provider (issuer, client id, secret,
   scopes) from the live `IConfiguration` on **every request**:
   - it (re)creates the discovery `ConfigurationManager` when the issuer appears
     or changes, decrypts the client secret per request, and reconciles scopes
   - until an issuer + client id are present it is inert: sign-in bounces back to
     `/ui` (the setup wizard), stray `/signin-oidc` hits fall through, sign-out is
     a local no-op
   - the framework's eager options validation (which requires the provider at
     startup) is removed for this scheme

## Procedure After SSO Setup
1. Configure SSO via web UI at `http://localhost:8082/ui`
2. Save settings — written to `/srv/fdroid-mgr/config/appsettings.json`
3. **No container restart required** — settings take effect immediately (a few ms
   of file-watcher latency); the UI reloads and the "Sign in" button is live
4. Verify: `cat /srv/fdroid-mgr/config/appsettings.json`

## If Container Restart Is Needed (e.g., server reboot)
```bash
# On the host:
podman restart fdroidmgr
# SSO settings persist in /srv/fdroid-mgr/config/appsettings.json
```

## Key Files
| File | Location | Purpose |
|---|---|---|
| `appsettings.json` | `/srv/fdroid-mgr/config/` | SSO/OIDC settings (persists across restarts) |
| `fdroidmgr.db` | `/srv/fdroid-mgr/data/` | Application database (persists across restarts) |
| `keys/` | `/srv/fdroid-mgr/data/` | App signing keys (back this up!) |

## Backup Recommendation
```bash
# Back up SSO config and keys:
tar czf fdroid-backup.tar.gz -C /srv/fdroid-mgr config/ data/keys/
```

## Verification
- `OidcConfig_SavedAtRuntime_GoesLive_WithoutRestart`
  (`test/FdroidMgr.Tests/OAuthLoginIntegrationTests.cs`) boots the app with **no**
  Auth settings, saves the provider via `POST /api/auth/oauth`, and completes the
  full OIDC login flow against the same running app instance — no restart.

## Summary
- **SSO settings** → file-based, no env vars, no restart needed
- **Container restart** → only needed for code updates or env var changes
- **Data persistence** → all important data in `/srv/fdroid-mgr/` volumes
