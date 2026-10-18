"""CI helper: read or modify the synced document on the mock GitHub API like another device would.
usage: sync_tool.py BASE SERVER USER PASS read|inject OUT_JSON"""
import base64
import json
import sys
import time
import urllib.request

import mnsync

base, server, user, pw, cmd, out = sys.argv[1:7]
sid, key = mnsync.derive(server, user, pw)
url = f"{base}/repos/yusufziyasaygi-oss/mn-sync/contents/v1/{sid}.json"
H = {"Authorization": "Bearer test", "Accept": "application/vnd.github+json"}


def get():
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=H)) as r:
            o = json.load(r)
            return mnsync.unseal(base64.b64decode(o["content"]).decode(), key), o["sha"]
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return None, None
        raise


doc, sha = get()
result = {"found": doc is not None}
if doc is not None:
    result["fav"] = sorted(k for k, v in doc.get("fav", {}).items() if not v.get("del"))
    result["prog"] = {k: v["p"].get("pos") for k, v in doc.get("prog", {}).items() if not v.get("del")}
    result["ep"] = doc.get("ep", {})
if cmd == "inject":
    now = int(time.time() * 1000)
    doc = doc or {"v": 1, "fav": {}, "prog": {}, "ep": {}}
    doc.setdefault("fav", {})["SERIES:3002"] = {"at": now, "del": False, "e": {
        "kind": "SERIES", "id": "3002", "name": "Dizi 2", "icon": "", "ext": "", "cat": "20", "num": 0}}
    doc.setdefault("prog", {})["MOVIE:2005"] = {"at": now, "del": False, "p": {
        "key": "MOVIE:2005", "kind": "MOVIE", "id": "2005", "title": "Film 5", "subtitle": "", "image": "",
        "ext": "mp4", "episodeId": "", "season": 0, "episode": 0, "pos": 42000, "dur": 60000}}
    doc.setdefault("ep", {})["4103"] = {"at": now, "pos": 0, "w": True}
    body = {"message": "sync test-device", "content": base64.b64encode(mnsync.seal(doc, key).encode()).decode()}
    if sha:
        body["sha"] = sha
    req = urllib.request.Request(url, data=json.dumps(body).encode(), headers=H, method="PUT")
    with urllib.request.urlopen(req) as r:
        result["inject_status"] = r.status
json.dump(result, open(out, "w"), indent=1, ensure_ascii=False)
print(json.dumps(result, ensure_ascii=False))
