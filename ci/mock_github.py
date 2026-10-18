"""Tiny stand-in for the GitHub contents API (GET/PUT with sha checks), used by the CI smoke tests."""
import base64
import hashlib
import json
import threading
from urllib.parse import urlparse

FILES = {}
LOCK = threading.Lock()
LOG = []


def _send(h, code, obj):
    body = json.dumps(obj).encode()
    h.send_response(code)
    h.send_header("Content-Type", "application/json")
    h.send_header("Content-Length", str(len(body)))
    h.end_headers()
    h.wfile.write(body)


def handle(h, method):
    """Returns True when the request was a GitHub API call."""
    path = urlparse(h.path).path
    if path == "/__syncreset":
        with LOCK:
            FILES.clear()
            LOG.append("RESET")
        _send(h, 200, {"reset": True})
        return True
    if not path.startswith("/repos/") or "/contents/" not in path:
        return False
    file_path = path.split("/contents/", 1)[1]
    if h.headers.get("Authorization") != "Bearer test":
        _send(h, 401, {"message": "Bad credentials"})
        return True
    with LOCK:
        if method == "GET":
            data = FILES.get(file_path)
            LOG.append(f"GET {file_path} -> {'200' if data else '404'}")
            if data is None:
                _send(h, 404, {"message": "Not Found"})
            else:
                _send(h, 200, {"path": file_path, "sha": hashlib.sha1(data).hexdigest(),
                               "content": base64.encodebytes(data).decode()})
            return True
        length = int(h.headers.get("Content-Length") or 0)
        req = json.loads(h.rfile.read(length) or b"{}")
        old = FILES.get(file_path)
        sha = req.get("sha")
        if old is not None and sha != hashlib.sha1(old).hexdigest():
            LOG.append(f"PUT {file_path} -> 409 (sha {sha})")
            _send(h, 409 if sha else 422, {"message": "sha does not match"})
            return True
        if old is None and sha:
            LOG.append(f"PUT {file_path} -> 422 (sha for new file)")
            _send(h, 422, {"message": "sha given for new file"})
            return True
        data = base64.b64decode(req["content"])
        FILES[file_path] = data
        LOG.append(f"PUT {file_path} -> {'200' if old else '201'} msg={req.get('message')}")
        _send(h, 200 if old else 201, {"content": {"path": file_path, "sha": hashlib.sha1(data).hexdigest()}})
        return True
