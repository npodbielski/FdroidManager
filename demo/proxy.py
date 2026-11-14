#!/usr/bin/env python3
"""Single-origin front for the browser demo.

One browser origin (http://10.0.0.104:8090) instead of two ports: /_idp/* goes
to the demo identity provider, everything else to the manager. Keeps the demo
working for clients that treat the second port differently (proxies, policies).

Usage: proxy.py <listen-port> <idp-port> <app-base-url>
"""
import sys
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """Redirects belong to the browser: the OIDC dance walks them one hop."""

    def redirect_request(self, *args, **kwargs):
        return None
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8090
IDP = f"http://127.0.0.1:{sys.argv[2] if len(sys.argv) > 2 else 8084}"
APP = (sys.argv[3] if len(sys.argv) > 3 else "http://10.0.0.104:8091").rstrip("/")
# Hopping headers, plus the two this server always writes itself.
HOP = {"connection", "keep-alive", "proxy-authenticate", "proxy-authorization", "te", "trailer", "transfer-encoding",
       "upgrade", "content-length", "host", "server", "date"}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"  # close after each response, see connection header

    def log_message(self, *args):
        pass

    def relay(self):
        path = self.path
        target = f"{IDP}{path[len('/_idp'):]}" if path.startswith("/_idp") else f"{APP}{path}"
        length = int(self.headers.get("content-length") or 0)
        body = self.rfile.read(length) if length else None
        req = urllib.request.Request(target, data=body, method=self.command)
        for k, v in self.headers.items():
            if k.lower() not in HOP:
                req.add_header(k, v)
        req.add_header("host", self.headers.get("host", ""))
        req.add_header("accept-encoding", "identity")
        opener = urllib.request.build_opener(NoRedirect)
        try:
            with opener.open(req, timeout=60) as res:
                data, status, headers = res.read(), res.status, res.headers.items()
        except urllib.error.HTTPError as e:
            data, status, headers = e.read(), e.code, e.headers.items()
        except OSError as e:
            msg = f"upstream failed: {e}".encode()
            return self.respond(502, msg, [("content-type", "text/plain")])
        out = [(k, v) for k, v in headers if k.lower() not in HOP]
        self.respond(status, data, out)

    def respond(self, status, body, headers):
        self.close_connection = True
        self.send_response(status)
        self.send_header("connection", "close")
        for k, v in headers:
            self.send_header(k, v)
        self.send_header("content-length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    do_GET = do_POST = do_PUT = do_DELETE = do_PATCH = relay


if __name__ == "__main__":
    print(f"front on :{PORT} — /_idp → {IDP}, /* → {APP}")
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
