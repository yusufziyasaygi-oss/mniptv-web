"""Reference implementation of the MN IPTV sync format (used by CI to check the apps).

Remote file: repos/<owner>/<repo>/contents/v1/<id>.json on GitHub, content =
  {"v":1,"n":base64(nonce 12 B),"c":base64(AES-256-GCM ciphertext||tag)}
id  = sha256_hex("mn-sync-id-v1|" + server.lower() + "|" + user + "|" + password)
key = sha256("mn-sync-key-v1|" + server.lower() + "|" + user + "|" + password)
Plain document:
  {"v":1,
   "fav":  {key: {"at":ms,"del":bool,"e":{kind,id,name,icon,ext,cat,num}}},
   "prog": {key: {"at":ms,"del":bool,"p":{key,kind,id,title,subtitle,image,ext,episodeId,season,episode,pos,dur}}},
   "ep":   {episodeId: {"at":ms,"pos":ms,"w":bool}}}
Merge: per item the larger "at" wins (equal: keep local). Positions/durations are milliseconds.
"""
import base64
import hashlib
import json
import os

from cryptography.hazmat.primitives.ciphers.aead import AESGCM


def derive(server, user, password):
    s = server.lower()
    sid = hashlib.sha256(f"mn-sync-id-v1|{s}|{user}|{password}".encode()).hexdigest()
    key = hashlib.sha256(f"mn-sync-key-v1|{s}|{user}|{password}".encode()).digest()
    return sid, key


def seal(doc, key):
    nonce = os.urandom(12)
    ct = AESGCM(key).encrypt(nonce, json.dumps(doc, separators=(",", ":"), ensure_ascii=False).encode(), None)
    return json.dumps({"v": 1, "n": base64.b64encode(nonce).decode(), "c": base64.b64encode(ct).decode()})


def unseal(text, key):
    o = json.loads(text)
    pt = AESGCM(key).decrypt(base64.b64decode(o["n"]), base64.b64decode(o["c"]), None)
    return json.loads(pt.decode())
