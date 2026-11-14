#!/usr/bin/env python3
"""Minimal OIDC provider for looking at the manager in a browser.

Authorization code + PKCE, RS256 id_tokens, one user. Deliberately stateless:
the login form carries the authorize request, so no cookies are set at all —
which is what makes it usable over plain HTTP from a LAN address, where a real
IdP's `SameSite=None` session cookie is refused by every browser.

Usage: idp.py <public-issuer> <port> [user:password]
"""
import base64
import hashlib
import sys
import http.server
import json
import os
import secrets
import time
import urllib.parse
import urllib.request
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa, padding

ISSUER = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://10.0.0.104:8084"
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 8084
USER, PASSWORD = (sys.argv[3].split(":", 1) + ["pw"])[:2] if len(sys.argv) > 3 else ("alice", "pw")
CLIENT_ID, CLIENT_SECRET = os.environ.get("DEMO_CLIENT", "fdroidmgr"), os.environ.get("DEMO_SECRET", "test-secret")
SUBJECT = "demo-user-1"

KEY = rsa.generate_private_key(public_exponent=65537, key_size=2048)
PUB = KEY.public_key().public_numbers()
CODES: dict[str, dict] = {}


def b64u(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def jwk() -> dict:
    return {"kty": "RSA", "use": "sig", "alg": "RS256", "kid": "demo",
            "n": b64u(PUB.n.to_bytes((PUB.n.bit_length() + 7) // 8, "big")),
            "e": b64u(PUB.e.to_bytes((PUB.e.bit_length() + 7) // 8, "big"))}


def id_token(nonce: str | None, aud: str) -> str:
    now = int(time.time())
    claims = {"iss": ISSUER, "aud": aud, "sub": SUBJECT, "iat": now, "exp": now + 300,
              "auth_time": now, "preferred_username": USER, "email": f"{USER}@example.com",
              "name": USER.title(), "role": "admin"}
    if nonce:
        claims["nonce"] = nonce
    head = b64u(json.dumps({"alg": "RS256", "typ": "JWT", "kid": "demo"}).encode())
    body = b64u(json.dumps(claims).encode())
    sig = KEY.sign(f"{head}.{body}".encode(), padding.PKCS1v15(), hashes.SHA256())
    return f"{head}.{body}.{b64u(sig)}"


LOGIN = """<!doctype html><meta charset=utf-8><title>Sign in — demo IdP</title>
<style>body{{font:16px system-ui;margin:3rem auto;max-width:26rem}}input,button{{font:inherit;
padding:.5rem .6rem;width:100%;box-sizing:border-box;margin:.4rem 0}}button{{cursor:pointer}}</style>
<h2>Demo identity provider</h2><p>Sign in as <b>{user}</b> / <b>{pw}</b>.</p>
<form method="post" action="">{hidden}
<label>Username <input name="username" value="{user}" autocomplete="username"></label>
<label>Password <input name="password" type="password" value="{pw}" autocomplete="current-password"></label>
<button type="submit">Sign in</button></form>"""


class Handler(http.server.BaseHTTPRequestHandler):
    # HTTP/1.0 semantics: no keep-alive, so a client can never reuse a
    # connection that outlived a previous provider on this address.
    protocol_version = "HTTP/1.0"
    disable_nagle_algorithm = True

    def log_message(self, fmt, *args):
        print(f"[{time.strftime('%H:%M:%S')}] {fmt % args}", flush=True)

    def send(self, status: int, body: bytes, ctype: str, extra: dict | None = None):
        stamp = time.strftime("%H:%M:%S")
        ua = (self.headers.get("user-agent") or "")[:30]
        print(f"{stamp} -> {status} {self.command} {self.path[:70]} len={len(body)} ua={ua}", flush=True)
        self.send_response(status)
        self.send_header("connection", "close")
        self.send_header("content-type", ctype)
        self.send_header("content-length", str(len(body)))
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def redirect(self, location: str):
        self.send(302, b"", "text/plain", {"location": location})

    def approve(self, params: dict):
        """Issue a code for the one demo user and hand the browser back to the client."""
        code = secrets.token_hex(16)
        CODES[code] = {"redirect": params.get("redirect_uri", ""), "nonce": params.get("nonce", ""),
                       "challenge": params.get("code_challenge", ""), "access": secrets.token_hex(16)}
        if len(CODES) > 500:
            CODES.pop(next(iter(CODES)))
        back = urllib.parse.urlparse(params.get("redirect_uri", "about:blank"))
        return self.redirect(f"{back.scheme}://{back.netloc}{back.path}?" + urllib.parse.urlencode(
            {"code": code, "state": params.get("state", ""), "session_state": secrets.token_hex(8)}))

    def form(self) -> dict:
        length = int(self.headers.get("content-length") or 0)
        raw = self.rfile.read(length).decode() if length else ""
        return {k: v[0] for k, v in urllib.parse.parse_qs(raw, keep_blank_values=True).items()}

    def do_GET(self):  # noqa: N802
        path, _, query = self.path.partition("?")
        q = urllib.parse.parse_qs(query)
        get = lambda k: q.get(k, [""])[0]  # noqa: E731
        if path == "/.well-known/openid-configuration":
            return self.send(200, json.dumps({
                "issuer": ISSUER, "jwks_uri": f"{ISSUER}/jwks",
                "authorization_endpoint": f"{ISSUER}/authorize", "token_endpoint": f"{ISSUER}/token",
                "userinfo_endpoint": f"{ISSUER}/userinfo", "response_types_supported": ["code"],
                "grant_types_supported": ["authorization_code"], "subject_types_supported": ["public"],
                "id_token_signing_alg_values_supported": ["RS256"],
                "scopes_supported": ["openid", "profile", "email"],
                "claim_types_supported": ["normal"],
                "code_challenge_methods_supported": ["S256"],
            }).encode(), "application/json")
        if path == "/jwks":
            return self.send(200, json.dumps({"keys": [jwk()]}).encode(), "application/json")
        if path == "/userinfo":
            return self.send(200, json.dumps({
                "sub": SUBJECT, "preferred_username": USER, "email": f"{USER}@example.com",
                "name": USER.title(), "email_verified": True,
            }).encode(), "application/json")
        if path == "/authorize":
            # Signed in as the demo user without a form: a page served by this
            # process would be one more thing the browser has to render, and the
            # login form is still there behind ?form=1.
            if q.get("form", [""])[0] != "1":
                return self.approve({k: v[0] for k, v in q.items()})
            hidden = f'<input type="hidden" name="raw" value="{urllib.parse.quote(query, safe="")}">'
            return self.send(200, LOGIN.format(user=USER, pw=PASSWORD, hidden=hidden).encode(), "text/html; charset=utf-8")
        self.send(404, b"not found", "text/plain")

    def do_HEAD(self):  # noqa: N802
        self.send(200, b"", "text/plain")

    def do_POST(self):  # noqa: N802
        path, _, query = self.path.partition("?")
        fields = self.form()
        if path == "/authorize":
            params = {k: v for k, v in urllib.parse.parse_qsl(fields.get("raw", ""), keep_blank_values=True)}
            if fields.get("username") != USER or fields.get("password") != PASSWORD:
                return self.send(401, b"bad credentials", "text/plain")
            return self.approve(params)
        if path == "/token":
            verifier = fields.get("code_verifier", "")
            entry = CODES.pop(fields.get("code", ""), None)
            auth = self.headers.get("authorization", "")
            sent_secret = fields.get("client_secret", "")
            if auth.lower().startswith("basic "):
                sent_secret = base64.b64decode(auth[6:]).decode().split(":", 1)[1]
            ok = (entry is not None
                  and fields.get("client_id", CLIENT_ID) == CLIENT_ID
                  and sent_secret == CLIENT_SECRET
                  and entry["redirect"] == fields.get("redirect_uri", entry["redirect"])
                  and b64u(hashlib.sha256(verifier.encode()).digest()) == entry["challenge"])
            if not ok:
                return self.send(400, json.dumps({"error": "invalid_grant"}).encode(), "application/json")
            CODES[entry["access"]] = {"access": True}
            return self.send(200, json.dumps({
                "access_token": entry["access"], "token_type": "Bearer", "expires_in": 300,
                "scope": "openid profile email", "id_token": id_token(entry.get("nonce"), CLIENT_ID),
            }).encode(), "application/json")
        self.send(404, b"not found", "text/plain")


if __name__ == "__main__":
    print(f"demo IdP on {ISSUER} (user {USER})")
    http.server.ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
