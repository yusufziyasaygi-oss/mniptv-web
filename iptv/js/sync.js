// Cihazlar arası senkron — Windows/Mac/TV uygulamalarıyla aynı şifreli mn-sync biçimi
import { store, cat, stableId } from './core.js';
import { data, hooks, persist } from './data.js';

const REPO = 'yusufziyasaygi-oss/mn-sync', TX = 'KidZGwwMPF1TRG0HHCghMgwJYiJJARBCWGJQUVhcIgwfEWlDHjQzSkN3agJpCQkyehxFSzQlGVl7BwJQZClQODsMbgZIX1BGWEZ1eVxfPTodBGwwSzgLb0MGUQVA', MASK = 'MN-sync-2026-key';
const KIND = { live: 'LIVE', movies: 'MOVIE', series: 'SERIES' }, SECTION = { LIVE: 'live', MOVIE: 'movies', SERIES: 'series' };
const S = { doc: null, docKey: '', timer: null, running: false, again: false, pending: false, lastRun: 0, lastPush: 0, status: 'Kapalı', lib: null, poll: null, getAcc: () => null, onApplied: () => {}, base: (typeof window !== 'undefined' && window.__MN_SYNC_BASE) || null };

const token = () => { try { const x = atob(TX); let o = ''; for (let i = 0; i < x.length; i++) o += String.fromCharCode(x.charCodeAt(i) ^ MASK.charCodeAt(i % MASK.length)); return o; } catch { return ''; } };
const acct = () => { const s = S.getAcc(); if (!s || s.type !== 'xtream' || !s.username) return null; return { server: String(s.syncServer || s.base || '').replace(/\/+$/, ''), user: String(s.username).trim(), pass: String(s.password || '').trim(), key: s.key }; };
const enabled = () => !!(S.base || token()) && !!acct();
const now = () => Date.now();
export const syncStatus = () => (!acct() ? 'Yalnızca Xtream hesaplarında' : S.status);
export const syncEnabled = enabled;

function entry(it) { return { kind: KIND[it.section], id: String(it.providerId), name: it.name || '', icon: it.artwork || '', ext: it.ext || '', cat: '', num: 0 }; }
function progRec(h) {
  if (!h || !h.position) return null; const it = h.item || {};
  if (h.seriesId) { const p = cat.byId.get(h.seriesId); if (!p?.providerId) return null; return { key: `SERIES:${p.providerId}`, kind: 'SERIES', id: String(p.providerId), title: h.seriesName || p.name || '', subtitle: `S${h.season || 0} B${h.episode || 0} · ${it.name || ''}`, image: h.seriesArtwork || p.artwork || '', ext: it.ext || '', episodeId: String(it.providerId || ''), season: h.season || 0, episode: h.episode || 0, pos: Math.round(h.position * 1000), dur: Math.round((h.duration || 0) * 1000) }; }
  if (it.section === 'movies' && it.providerId) return { key: `MOVIE:${it.providerId}`, kind: 'MOVIE', id: String(it.providerId), title: it.name || '', subtitle: '', image: it.artwork || '', ext: it.ext || '', episodeId: '', season: 0, episode: 0, pos: Math.round(h.position * 1000), dur: Math.round((h.duration || 0) * 1000) };
  return null;
}
function docLoad() {
  const a = acct(); if (!a) return null;
  if (S.doc && S.docKey === a.key) return S.doc;
  let doc = store.load('syncdoc', null); S.docKey = a.key;
  if (!doc || typeof doc !== 'object') {
    doc = { v: 1, fav: {}, prog: {}, ep: {} }; let n = data.favorites.size;
    for (const id of data.favorites) { const it = cat.byId.get(id); if (it?.providerId && KIND[it.section]) doc.fav[`${KIND[it.section]}:${it.providerId}`] = { at: n--, del: false, e: entry(it) }; }
    for (const h of Object.values(data.history)) { const r = progRec(h); if (r) doc.prog[r.key] = { at: Math.round(h.updated || 1), del: false, p: r }; }
  }
  S.doc = doc; return doc;
}
const docSave = () => store.save('syncdoc', S.doc);
function record(section, key, body, at) { if (!enabled()) return; const d = docLoad(); if (!d) return; body.at = at || now(); (d[section] || (d[section] = {}))[key] = body; docSave(); schedule(); }
function onFav(item, added) { if (!item?.providerId || !KIND[item.section]) return; record('fav', `${KIND[item.section]}:${item.providerId}`, { del: !added, e: entry(item) }); }
function onProg(item, pos, dur, finished) {
  if (!enabled() || !item) return;
  if (item.seriesId) {
    const p = cat.byId.get(item.seriesId); if (!p?.providerId) return; const key = `SERIES:${p.providerId}`;
    if (item.providerId) record('ep', String(item.providerId), { pos: Math.round(pos * 1000), w: !!finished });
    if (finished) record('prog', key, { del: true });
    else if (pos > 15) { const r = progRec({ position: pos, duration: dur, seriesId: item.seriesId, seriesName: item.seriesName, seriesArtwork: item.seriesArtwork, season: item.season, episode: item.episode, item }); if (r) record('prog', key, { del: false, p: r }); }
  } else if (item.section === 'movies' && item.providerId) {
    const key = `MOVIE:${item.providerId}`;
    if (finished) record('prog', key, { del: true });
    else if (pos > 15) record('prog', key, { del: false, p: progRec({ position: pos, duration: dur, item }) });
  }
}
function onProgDel(item) {
  if (!item) return; let key = '';
  if (item.section === 'series') { if (item.providerId) key = `SERIES:${item.providerId}`; }
  else if (item.providerId) key = `MOVIE:${item.providerId}`;
  if (key) record('prog', key, { del: true });
}
function schedule() { S.pending = true; clearTimeout(S.timer); S.timer = setTimeout(run, Math.max(6, 45 - (now() - S.lastPush) / 1000) * 1000); }
export function syncFlush() { if (S.pending && enabled()) { clearTimeout(S.timer); run(); } }
export function syncPull(gap = 20) { if (!enabled() || now() - S.lastRun < gap * 1000) return; run(); }
export async function syncNow() { S.lastRun = 0; await run(); return S.status; }
const lib = async () => (S.lib ||= await import('./sync-crypto.js'));
function merge(local, remote) {
  const out = { v: 1, fav: {}, prog: {}, ep: {} };
  for (const sec of ['fav', 'prog', 'ep']) {
    for (const [k, v] of Object.entries(local?.[sec] || {})) out[sec][k] = v;
    for (const [k, v] of Object.entries(remote?.[sec] || {})) if ((v?.at || 0) > (out[sec][k]?.at || 0)) out[sec][k] = v;
  }
  const lim = now() - 90 * 86400000;
  for (const sec of ['fav', 'prog']) for (const [k, v] of Object.entries(out[sec])) if (!v || (v.del && (v.at || 0) < lim)) delete out[sec][k];
  const trim = (o, max) => { const ks = Object.keys(o); if (ks.length > max) ks.sort((a, b) => (o[b].at || 0) - (o[a].at || 0)).slice(max).forEach((k) => delete o[k]); };
  trim(out.prog, 150); trim(out.ep, 3000); return out;
}
const canon = (v) => JSON.stringify(v, (k, x) => (x && typeof x === 'object' && !Array.isArray(x) ? Object.keys(x).sort().reduce((o, kk) => ((o[kk] = x[kk]), o), {}) : x));
const headers = () => ({ Authorization: 'Bearer ' + (S.base ? 'test' : token()), Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' });
const u8b64 = (str) => { const b = new TextEncoder().encode(str); let s = ''; for (let i = 0; i < b.length; i += 0x8000) s += String.fromCharCode.apply(null, b.subarray(i, i + 0x8000)); return btoa(s); };
async function run() {
  if (!enabled()) return;
  if (S.running) { S.again = true; return; }
  S.running = true;
  try { do { S.again = false; await once(); } while (S.again); } finally { S.running = false; }
}
async function once() {
  const a = acct(); if (!a) return; S.lastRun = now(); const had = S.pending; S.pending = false;
  try {
    const L = await lib(); const { id, key } = L.derive(a.server, a.user, a.pass); const base = S.base || 'https://api.github.com';
    for (let attempt = 0; attempt < 3; attempt++) {
      const r = await fetch(`${base}/repos/${REPO}/contents/v1/${id}.json?t=${now()}`, { headers: headers(), cache: 'no-store' });
      let remote = null, sha = null;
      if (r.status !== 404) {
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const o = await r.json(); sha = o.sha || null;
        try { remote = L.unseal(atob(String(o.content || '').replace(/\s/g, '')), key); } catch {}
      }
      const local = docLoad(); const merged = merge(local, remote);
      if (canon(merged) !== canon(local)) { S.doc = merged; docSave(); apply(merged); }
      if (remote && canon(merged) === canon(remote)) { S.status = 'Güncel'; return; }
      const body = { message: 'sync web', content: u8b64(L.seal(merged, key)) }; if (sha) body.sha = sha;
      const p = await fetch(`${base}/repos/${REPO}/contents/v1/${id}.json`, { method: 'PUT', headers: { ...headers(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      if (p.status === 200 || p.status === 201) { S.lastPush = now(); S.status = 'Güncel'; return; }
      if (p.status !== 409 && p.status !== 422) throw new Error('HTTP ' + p.status);
    }
    throw new Error('çakışma');
  } catch (e) {
    console.warn('sync', e); S.status = 'Bağlanamadı';
    if (had) { S.pending = true; clearTimeout(S.timer); S.timer = setTimeout(run, 60000); }
  }
}
function apply(doc) {
  const key = store.key; if (!key) return; let fc = false, hc = false, wc = false;
  const wid = (sec, pid) => stableId(`${key}|${sec}|${pid}`);
  const playing = S.playingId?.() || null;
  for (const [k, v] of Object.entries(doc.fav || {})) {
    const [kind, ...rest] = k.split(':'); const sec = SECTION[kind]; if (!sec || !v) continue; const id = wid(sec, rest.join(':'));
    if (v.del) { if (data.favorites.has(id)) { data.favorites.delete(id); fc = true; } } else if (!data.favorites.has(id)) { data.favorites.add(id); fc = true; }
  }
  for (const [k, v] of Object.entries(doc.prog || {})) {
    if (!v) continue; const [kind, ...rest] = k.split(':'); const pid = rest.join(':');
    if (kind === 'MOVIE') {
      const id = wid('movies', pid); if (playing === id) continue; const cur = data.history[id];
      if (v.del) { if (cur && (cur.updated || 0) < v.at) { delete data.history[id]; hc = true; } continue; }
      const p = v.p; if (!p || (cur && (cur.updated || 0) >= v.at)) continue;
      const item = cat.byId.get(id) || { id, name: p.title, section: 'movies', artwork: p.image || null, providerId: pid, ext: p.ext || 'mp4', group: '' };
      data.history[id] = { id, position: p.pos / 1000, duration: p.dur / 1000, updated: v.at, item: { ...item }, seriesId: null, seriesName: null, seriesArtwork: null, season: null, episode: null, group: item.group || '' }; hc = true;
    } else if (kind === 'SERIES') {
      const sid = wid('series', pid);
      if (v.del) { for (const [hid, h] of Object.entries(data.history)) if (h?.seriesId === sid && (h.updated || 0) < v.at && playing !== hid) { delete data.history[hid]; hc = true; } continue; }
      const p = v.p; if (!p || !p.episodeId) continue; const eid = wid('episode', p.episodeId); if (playing === eid) continue;
      const latest = Object.values(data.history).filter((h) => h?.seriesId === sid).reduce((m, h) => Math.max(m, h.updated || 0), 0); if (latest >= v.at) continue;
      const parent = cat.byId.get(sid);
      const epItem = { id: eid, name: String(p.subtitle || '').split(' · ').slice(1).join(' · ') || `Bölüm ${p.episode}`, section: 'series', artwork: p.image || null, providerId: p.episodeId, ext: p.ext || 'mp4', seriesId: sid, season: p.season, episode: p.episode };
      data.history[eid] = { id: eid, position: p.pos / 1000, duration: p.dur / 1000, updated: v.at, item: epItem, seriesId: sid, seriesName: parent?.name || p.title, seriesArtwork: parent?.artwork || p.image || null, season: p.season, episode: p.episode, group: parent?.group || '' }; hc = true;
    }
  }
  for (const [epid, v] of Object.entries(doc.ep || {})) {
    if (!v) continue; const id = wid('episode', epid);
    if (v.w) { if (!data.watched.has(id)) { data.watched.add(id); wc = true; } } else if (data.watched.has(id) && (v.at || 0) > 1) { data.watched.delete(id); wc = true; }
    if (!v.w && v.pos > 0 && !data.eppos[id]) { data.eppos[id] = v.pos / 1000; persist.eppos(); }
  }
  if (fc) persist.fav(); if (hc) persist.hist(); if (wc) persist.watched();
  if (fc || hc || wc) S.onApplied();
}
export function syncStart({ getAccount, onApplied, playingId }) {
  S.getAcc = getAccount; S.onApplied = onApplied; S.playingId = playingId;
  hooks.fav = onFav; hooks.prog = onProg; hooks.progDel = onProgDel;
  S.doc = null; clearInterval(S.poll);
  if (!enabled()) { S.status = 'Kapalı'; return; }
  syncPull(0);
  S.poll = setInterval(() => { if (!document.hidden) syncPull(55); }, 60000);
}
export function syncStop() { clearInterval(S.poll); clearTimeout(S.timer); S.doc = null; hooks.fav = hooks.prog = hooks.progDel = null; }
