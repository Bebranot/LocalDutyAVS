#!/usr/bin/env python3
"""Standalone reimplementation of RobustToolbox's "Manual ACZ" manifest+download
protocol, meant to run OFF the game server host (e.g. on a VPS with a real
uplink) so new-player content downloads don't traverse the game host's weak
home connection.

Protocol reverse-engineered from RobustToolbox/Robust.Server/ServerStatus/
StatusHost.Acz.cs and StatusHost.Acz.Sources.cs (manifest format, hashing,
download framing) plus RobustToolbox/Tools/download_manifest_file.py (wire
format confirmation). This does not need to produce byte-identical output to
the engine's own automatic ACZ generator -- once server_config.toml points the
launcher at THIS service via build.manifest_url/build.manifest_download_url,
the engine's own generator is never consulted, so this service only needs to
be internally consistent and speak the exact wire protocol the launcher
expects.

v1 deliberately skips per-file zstd compression (PreCompressed=0 always) to
minimize the amount of protocol surface that has to be exactly right before
the first real end-to-end test. Bandwidth savings from compression are a
secondary concern once the big win (moving bytes off the weak host link) is
proven to work at all.
"""

import argparse
import hashlib
import http.server
import socketserver
import struct
import sys
import threading
import zipfile


class Manifest:
    """zipfile.ZipFile is not safe for concurrent reads from multiple threads --
    it shares one underlying file handle/seek position. With ThreadingHTTPServer,
    two players downloading at once could interleave reads and get corrupt data
    or a mid-response exception (seen live as "An error occurred while sending
    the request" in the launcher). Each thread gets its own ZipFile instead."""

    def __init__(self, zip_path: str):
        self.zip_path = zip_path
        self._local = threading.local()
        self.entries: list[tuple[str, str]] = []  # (path, name_in_zip) in manifest order
        self._hash_to_name: dict[str, str] = {}  # content hash -> first zip name seen with that hash
        self._build()

    @property
    def _zip(self) -> zipfile.ZipFile:
        zf = getattr(self._local, "zip", None)
        if zf is None:
            zf = zipfile.ZipFile(self.zip_path, "r")
            self._local.zip = zf
        return zf

    def _build(self):
        infos = self._zip.infolist()
        hashed: list[tuple[str, str, str]] = []  # (path, hash_hex, zip_name)

        for info in infos:
            if info.is_dir():
                continue
            data = self._zip.read(info.filename)
            digest = hashlib.blake2b(data, digest_size=32).hexdigest().upper()

            if digest not in self._hash_to_name:
                self._hash_to_name[digest] = info.filename

            hashed.append((info.filename, digest, info.filename))

        # Manifest lines must be sorted by path (ordinal), matching StatusHost.Acz.Sources.cs.
        hashed.sort(key=lambda t: t[0])
        self.entries = [(path, digest) for path, digest, _ in hashed]

        lines = ["Robust Content Manifest 1\n"]
        for path, digest in self.entries:
            lines.append(f"{digest} {path}\n")
        self.manifest_text = "".join(lines).encode("utf-8")
        self.manifest_hash = hashlib.blake2b(self.manifest_text, digest_size=32).hexdigest().upper()

        print(f"Built manifest: {len(self.entries)} files, "
              f"{len(self._hash_to_name)} unique by content, "
              f"manifest_hash={self.manifest_hash}", file=sys.stderr)

    def read_by_index(self, index: int) -> bytes:
        path, digest = self.entries[index]
        canonical_name = self._hash_to_name[digest]
        return self._zip.read(canonical_name)


class Handler(http.server.BaseHTTPRequestHandler):
    manifest: Manifest = None  # type: ignore[assignment]

    def log_message(self, fmt, *args):
        sys.stderr.write(f"{self.address_string()} - {fmt % args}\n")

    def do_GET(self):
        if self.path != "/manifest.txt":
            self.send_error(404)
            return

        data = self.manifest.manifest_text
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        self.send_header("Pragma", "no-cache")
        self.end_headers()
        self.wfile.write(data)

    def do_OPTIONS(self):
        if self.path != "/download":
            self.send_error(404)
            return

        self.send_response(204)
        self.send_header("X-Robust-Download-Min-Protocol", "1")
        self.send_header("X-Robust-Download-Max-Protocol", "1")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_POST(self):
        if self.path != "/download":
            self.send_error(404)
            return

        content_type = self.headers.get("Content-Type")
        if content_type and content_type != "application/octet-stream":
            self.send_error(400, "Must specify application/octet-stream Content-Type")
            return

        proto = self.headers.get("X-Robust-Download-Protocol")
        if proto != "1":
            self.send_error(400, "Expected X-Robust-Download-Protocol: 1")
            return

        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        if length % 4 != 0:
            self.send_error(400, "Body length must be a multiple of 4")
            return

        indices = [i for (i,) in struct.iter_unpack("<I", body)]

        manifest_len = len(self.manifest.entries)
        for idx in indices:
            if idx < 0 or idx >= manifest_len:
                self.send_error(400, "Out of bounds manifest index")
                return

        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        self.send_header("Pragma", "no-cache")
        self.end_headers()

        # Stream header: flags=0 (PreCompressed not set -- see module docstring).
        self.wfile.write(struct.pack("<i", 0))

        for idx in indices:
            data = self.manifest.read_by_index(idx)
            self.wfile.write(struct.pack("<I", len(data)))
            self.wfile.write(data)


class ThreadingHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True
    # socketserver's default backlog (5) is far too small for a public game server
    # with soft_max_players=67 -- under any real connect burst, the OS refuses/drops
    # new SYNs once the accept queue fills, which is exactly the connection-establish
    # failure a real player hit ("не получен нужный отклик... разорвано соединение").
    # Each in-flight full download also ties up a thread for a long time (a fresh
    # client with zero cache pulls ~1.7GB in one request), so queued connects need
    # real headroom while those finish.
    request_queue_size = 128


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("zip_path", help="Path to SS14.Client.zip built by Content.Packaging")
    parser.add_argument("--port", type=int, default=8420)
    parser.add_argument("--bind", default="0.0.0.0")
    args = parser.parse_args()

    manifest = Manifest(args.zip_path)
    Handler.manifest = manifest

    server = ThreadingHTTPServer((args.bind, args.port), Handler)
    print(f"Serving manifest+download protocol on {args.bind}:{args.port}", file=sys.stderr)
    print(f"manifest_hash for build.manifest_hash cvar: {manifest.manifest_hash}", file=sys.stderr)
    server.serve_forever()


if __name__ == "__main__":
    main()
