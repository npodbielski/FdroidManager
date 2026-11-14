# Backup & Restore Strategy

> The host paths below use `/srv/fdroid-mgr` and `/srv/fdroid/repo` as in
> [the deployment guide](deployment.md) — substitute your own bind-mount
> targets. The container-internal paths (`/config`, `/data`) are fixed.

## What to Backup
The following directories contain critical data:
- `/srv/fdroid-mgr/config/` — SSO/OIDC settings (`appsettings.json`)
- `/srv/fdroid-mgr/data/` — Application database (`fdroidmgr.db` and its
  WAL sidecar files `fdroidmgr.db-wal`/`fdroidmgr.db-shm`), signing keys
  (`keys/`), data protection keys (`dp-keys/`)
- `/srv/fdroid/repo/` — F-Droid repository data (config.yml, keystore, APKs, index)

The database runs in WAL journal mode. Always back up the whole `data/`
directory (as below) so the `-wal` sidecar travels with the main DB file;
backing up `fdroidmgr.db` alone can miss recent committed transactions.

## Backup Methods

### Full Backup (Recommended)
```bash
# Stop the container
podman stop fdroidmgr

# Create backup
tar czf /backup/fdroid-full-$(date +%Y%m%d).tar.gz -C /srv/fdroid-mgr .

# Restart the container
podman start fdroidmgr
```

### Incremental Backup
```bash
# Backup only critical files
tar czf /backup/fdroid-critical-$(date +%Y%m%d).tar.gz \
  -C /srv/fdroid-mgr config/ data/keys/ data/dp-keys/
```

### Automated Backup (Cron)
```bash
# Add to crontab (crontab -e)
0 2 * * * tar czf /backup/fdroid-full-$(date +\%Y\%m\%d).tar.gz -C /srv/fdroid-mgr . && podman restart fdroidmgr
```

## Restore Procedure

### Full Restore
1. Stop the container: `podman stop fdroidmgr && podman rm fdroidmgr`
2. Extract backup: `tar xzf /backup/fdroid-full-YYYYMMDD.tar.gz -C /srv/fdroid-mgr`
3. Verify file permissions (the container must be able to write the mounts)
4. Restart the container: `podman run ... fdroidmgr:latest`

### Critical Files Restore
1. Stop the container: `podman stop fdroidmgr`
2. Extract backup: `tar xzf /backup/fdroid-critical-YYYYMMDD.tar.gz -C /srv/fdroid-mgr`
3. Restart the container: `podman start fdroidmgr`

## Verification
After restore, verify:
1. SSO settings: `cat /srv/fdroid-mgr/config/appsettings.json`
2. Database: `podman exec fdroidmgr ls -la /data/fdroidmgr.db`
3. Signing keys: `podman exec fdroidmgr ls -la /data/keys/`
4. API status: `wget -qO- http://localhost:8082/api/repo/status`

## Important Notes
- **Signing keys are critical**: Losing them means you cannot update existing apps
- **Master key is critical**: Losing it means you cannot decrypt stored secrets
- **Database is critical**: Losing it means you lose all app/release metadata
- **Repository data is recoverable**: Can be regenerated from APKs and metadata
