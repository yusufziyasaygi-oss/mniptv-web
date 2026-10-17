"""Minimal Xtream Codes mock used only by the CI smoke test.

Deliberately mixes value types (numbers as strings, "", null, [] instead of {})
the way real panels do, so the app's lenient parsing is exercised.
"""
import base64
import json
import os
import re
import select
import socket
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

ROOT = os.path.dirname(os.path.abspath(__file__))
MEDIA = os.path.join(ROOT, "media")
HOST = os.environ.get("MOCK_HOST", "http://10.0.2.2:8080")


# Slow-panel mode (switched on by the smoke test): like many real panels, only one list download
# per account at a time, and the "Spor" live category takes 10 s to answer.
SLOW = {"on": False}
LIST_LOCK = threading.Lock()


def client_gone(conn):
    try:
        r, _, _ = select.select([conn], [], [], 0)
        return bool(r) and conn.recv(1, socket.MSG_PEEK) == b""
    except OSError:
        return True


def b64(s):
    return base64.b64encode(s.encode("utf-8")).decode("ascii")


def live_streams(category):
    out = []
    for i in range(1, 31):
        cat = "1" if i <= 15 else "2"
        if category and category != cat:
            continue
        out.append({
            "num": str(i) if i % 2 else i,
            "name": f"Kanal {i} HD" if cat == "1" else f"Spor {i}",
            "stream_type": "live",
            "stream_id": 1000 + i,
            "stream_icon": None if i == 3 else ("" if i == 4 else f"{HOST}/logo.png"),
            "epg_channel_id": f"k{i}",
            "category_id": int(cat) if i % 3 == 0 else cat,
        })
    return out


def vod_streams(category):
    out = []
    for i in range(1, 21):
        cat = "10" if i <= 10 else "11"
        if category and category != cat:
            continue
        item = {
            "num": i,
            "name": f"Film {i}",
            "stream_id": 2000 + i,
            "stream_icon": f"{HOST}/poster.png",
            "category_id": cat,
            "rating": "",
        }
        if i != 2:
            item["container_extension"] = "mp4"
        out.append(item)
    return out


def series_list(category):
    out = []
    for i in range(1, 9):
        cat = "20"
        if category and category != cat:
            continue
        out.append({"num": i, "name": f"Dizi {i}", "series_id": 3000 + i, "cover": f"{HOST}/poster.png", "category_id": cat})
    return out


def series_info(series_id):
    if series_id == "3008":
        return {"info": [], "episodes": []}
    episodes = {}
    for season in (1, 2):
        episodes[str(season)] = [
            {
                "id": str(4000 + season * 100 + e),
                "episode_num": e,
                "title": f"Bölüm {e}",
                "container_extension": "mp4",
                "season": season,
                "info": {"duration_secs": 60, "movie_image": f"{HOST}/poster.png"} if e % 2 else [],
            }
            for e in range(1, 6)
        ]
    return {"info": {"name": f"Dizi {int(series_id) - 3000}", "plot": "Test dizisi.", "cover": f"{HOST}/poster.png"},
            "episodes": episodes}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        print("%s %s" % (self.address_string(), fmt % args), flush=True)

    def send_json(self, obj):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def send_file(self, path, ctype):
        size = os.path.getsize(path)
        start, end = 0, size - 1
        rng = self.headers.get("Range")
        m = re.match(r"bytes=(\d*)-(\d*)", rng or "")
        if m and (m.group(1) or m.group(2)):
            if m.group(1):
                start = int(m.group(1))
                if m.group(2):
                    end = min(int(m.group(2)), size - 1)
            else:
                start = max(0, size - int(m.group(2)))
            self.send_response(206)
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        else:
            self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        self.end_headers()
        with open(path, "rb") as f:
            f.seek(start)
            remaining = end - start + 1
            while remaining > 0:
                chunk = f.read(min(65536, remaining))
                if not chunk:
                    break
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    return
                remaining -= len(chunk)

    def do_GET(self):
        url = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(url.query).items()}
        path = url.path
        if path == "/__slow":
            SLOW["on"] = q.get("on") == "1"
            return self.send_json({"slow": SLOW["on"]})
        if path == "/player_api.php":
            if q.get("username") != "u" or q.get("password") != "p":
                return self.send_json({"user_info": {"auth": 0}})
            action = q.get("action")
            cat = q.get("category_id")
            if SLOW["on"] and action in ("get_live_streams", "get_vod_streams", "get_series"):
                t0 = time.time()
                while not LIST_LOCK.acquire(timeout=0.1):
                    if client_gone(self.connection):
                        print(f"SLOW gave-up-waiting {action} cat={cat}", flush=True)
                        return
                t1 = time.time()
                print(f"SLOW {t1:.1f} start {action} cat={cat} waited={t1 - t0:.1f}s peer={self.client_address}", flush=True)
                try:
                    if action == "get_live_streams" and str(cat) == "2":
                        for _ in range(100):
                            time.sleep(0.1)
                            if client_gone(self.connection):
                                print(f"SLOW {time.time():.1f} aborted-by-client {action} cat={cat} after={time.time() - t1:.1f}s peer={self.client_address}", flush=True)
                                return
                    return self.send_json(live_streams(cat) if action == "get_live_streams"
                                          else vod_streams(cat) if action == "get_vod_streams" else series_list(cat))
                finally:
                    LIST_LOCK.release()
            if action is None:
                return self.send_json({"user_info": {"auth": 1, "status": "Active", "username": "u"},
                                       "server_info": {"url": "10.0.2.2", "port": "8080"}})
            if action == "get_live_categories":
                return self.send_json([{"category_id": "1", "category_name": "Ulusal"},
                                       {"category_id": 2, "category_name": "Spor"}])
            if action == "get_live_streams":
                return self.send_json(live_streams(cat))
            if action == "get_vod_categories":
                return self.send_json([{"category_id": "10", "category_name": "Aksiyon"},
                                       {"category_id": "11", "category_name": "Komedi"}])
            if action == "get_vod_streams":
                return self.send_json(vod_streams(cat))
            if action == "get_series_categories":
                return self.send_json([{"category_id": "20", "category_name": "Yerli Diziler"}])
            if action == "get_series":
                return self.send_json(series_list(cat))
            if action == "get_series_info":
                return self.send_json(series_info(q.get("series_id", "")))
            if action == "get_short_epg":
                now = int(time.time())
                return self.send_json({"epg_listings": [
                    {"title": b64("Akşam Haberleri"), "start_timestamp": str(now - 600), "stop_timestamp": str(now + 1800)},
                    {"title": b64("Gece Filmi"), "start_timestamp": str(now + 1800), "stop_timestamp": str(now + 7200)},
                ]})
            return self.send_json([])
        if path == "/logo.png":
            return self.send_file(os.path.join(MEDIA, "logo.png"), "image/png")
        if path == "/poster.png":
            return self.send_file(os.path.join(MEDIA, "poster.png"), "image/png")
        if path.startswith("/live/") and path.endswith(".ts"):
            return self.send_file(os.path.join(MEDIA, "live.ts"), "video/mp2t")
        if path.startswith("/movie/") or path.startswith("/series/"):
            return self.send_file(os.path.join(MEDIA, "movie.mp4"), "video/mp4")
        self.send_response(404)
        self.send_header("Content-Length", "0")
        self.end_headers()


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
