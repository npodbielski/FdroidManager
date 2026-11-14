#!/usr/bin/env python3
"""Seed the demo repository: config.yml plus a hand-written index.

The demo's apps are preview entries, not real releases: no APKs exist, the
index-v1.json is written directly, and the UI shows the three apps from it.
That is what "Rebuild index" would then replace — with an empty index —
because it indexes what is really in the repo (see README.md).

When `apksigner` is on the PATH a demo keystore is generated and the config
gets the signing entries, so the Rebuild button runs successfully instead of
failing; without it the config stays minimal and the rebuild reports the
missing signing key.

Usage: seed-repo.py <work-dir> <demo-ip> <front-port>
"""
import base64
import json
import shutil
import secrets
import sqlite3
import subprocess
import sys
from datetime import datetime, timezone

WORK, DEMO_IP, FRONT_PORT = sys.argv[1], sys.argv[2], sys.argv[3]
REPO = f"{WORK}/repo"

# (package, name, summary, license, versionCode, versionName)
APPS = [
    ("org.fdroid.fdroid", "F-Droid", "The free and open source app store", "GPLv3+", 1021160, "1.21.1"),
    ("org.mozilla.firefox", "Firefox", "Fast, private & open", "MPL 2.0", 1020450, "128.4.0"),
    ("com.aurora.store", "Aurora Store", "An unofficial F-Droid client", "GPLv3", 105, "4.5.0"),
]


def sign_config(lines: list[str]) -> None:
    password = base64.b64encode(secrets.token_bytes(24)).decode()
    subprocess.run([
        "keytool", "-genkeypair", "-alias", "demo", "-keyalg", "RSA", "-keysize", "2048",
        "-validity", "3650", "-keystore", f"{REPO}/main.jks", "-storetype", "PKCS12",
        "-dname", "CN=fdroidmgr-demo, OU=demo", "-storepass", password, "-keypass", password,
    ], check=True, capture_output=True)
    lines += [
        "keystore: main.jks",
        "repo_keyalias: demo",
        "keydname: CN=fdroidmgr-demo, OU=demo",
        f"keystorepass: {password}",
        f"keypass: {password}",
        f"apksigner: {shutil.which('apksigner')}",
    ]


def patch_db() -> None:
    """Mark the hand-written index as the last successful build.

    The manager regenerates the index at startup (fdroid update) whenever the
    repo is initialized — this seed therefore runs after startup, so the DB
    still says "never built" and the UI shows an error banner. Writing the
    fixture does not touch the database; this closes the loop. (SQLite WAL:
    a single UPDATE from another process is safe while the app runs.)
    """
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f")
    with sqlite3.connect(f"{WORK}/data/fdroidmgr.db") as db:
        db.execute("UPDATE RepoConfig SET LastIndexBuiltUtc = ?, LastIndexError = NULL", (stamp,))


def main() -> None:
    lines = [
        "# demo repo config (presence marks the repo as initialized)",
        f"repo_location: {REPO}",
    ]
    if shutil.which("apksigner"):
        sign_config(lines)
    with open(f"{REPO}/config.yml", "w") as f:
        f.write("\n".join(lines) + "\n")

    index = {
        "repo": {
            "name": "Demo repo",
            "address": f"http://{DEMO_IP}:{FRONT_PORT}/fdroid/repo",
            "timestamp": 1760000000,
            "version": 1,
        },
        "apps": [
            {
                "packageName": p, "name": n, "summary": s,
                "description": f"{n} — demo entry generated for the UI preview.",
                "license": lic, "webSite": "https://example.org",
                "sourceCode": "https://example.org/src", "localized": {},
            }
            for p, n, s, lic, _, _ in APPS
        ],
        "packages": {
            p: [{
                "packageName": p, "versionName": vn, "versionCode": vc,
                "apkName": f"{p}_{vc}.apk", "hash": "0" * 64,
                "size": 5_000_000 + vc, "sig": ["1.2.3"],
                "minSdkVersion": "24", "targetSdkVersion": "35",
            }]
            for p, _, _, _, vc, vn in APPS
        },
    }
    # The API reads <root>/repo/index-v1.json; a copy at the root keeps the
    # fixture next to the config where fdroid's own tooling looks.
    data = json.dumps(index, indent=1)
    for path in (f"{REPO}/repo/index-v1.json", f"{REPO}/index-v1.json"):
        with open(path, "w") as f:
            f.write(data)
    patch_db()
    print(f"seeded {REPO}: config.yml + index with {len(APPS)} preview apps")


if __name__ == "__main__":
    main()
