#!/usr/bin/env python3
# Serves build/web on http://127.0.0.1:8765 for testing. Browsers only give the game threads when
# the page is cross-origin isolated, which plain "python3 -m http.server" does not do.
#   python3 tools/serve-web.py build/web
# Static server with the cross-origin isolation headers Godot's threaded web builds need.
import http.server, sys, functools
class H(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()
http.server.ThreadingHTTPServer(("127.0.0.1", 8765), functools.partial(H, directory=sys.argv[1])).serve_forever()
