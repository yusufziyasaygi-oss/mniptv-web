// MN IPTV Web — çekirdek: yardımcılar, depolama, IPTV sağlayıcı erişimi, katalog
export const $ = (s, r = document) => r.querySelector(s);
export const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
export const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export const clamp = (n, a, b) => Math.min(b, Math.max(a, n));
export const norm = (s) => String(s || '').toLocaleLowerCase('tr-TR').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/ı/g, 'i');
export function fmtTime(sec) {
  sec = Math.max(0, Math.floor(sec || 0));
  const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
  return h ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}
export function stableId(value) {
  let h = 0x811c9dc5;
  for (const ch of String(value || '')) { h ^= ch.codePointAt(0); h = Math.imul(h, 0x01000193) >>> 0; }
  return `mn${h.toString(16).padStart(8, '0')}`;
}
export const sectionLabel = (s) => ({ live: 'Canlı TV', movies: 'Film', series: 'Dizi' }[s] || '');
export function safeImage(u) {
  u = String(u || '').trim();
  if (!u) return '';
  if (/^https?:\/\//i.test(u)) return u;
  return '';
}
export function appError(message, code = 'error') { const e = new Error(message); e.code = code; return e; }

// ---------- yerel depolama (kaynağa göre) ----------
export const store = {
  key: '',
  k(name) { return `mniptv:${this.key || 'global'}:${name}`; },
  load(name, fallback) { try { const v = localStorage.getItem(this.k(name)); return v ? JSON.parse(v) : fallback; } catch { return fallback; } },
  save(name, value) { try { localStorage.setItem(this.k(name), JSON.stringify(value)); return true; } catch { return false; } },
  gget(name, fallback = null) { try { const v = localStorage.getItem(`mniptv:global:${name}`); return v ? JSON.parse(v) : fallback; } catch { return fallback; } },
  gset(name, value) { try { localStorage.setItem(`mniptv:global:${name}`, JSON.stringify(value)); } catch {} },
};

// ---------- IndexedDB (katalog önbelleği) ----------
let dbp = null;
function db() {
  if (!dbp) dbp = new Promise((res, rej) => {
    if (!('indexedDB' in window)) return rej(new Error('no idb'));
    const r = indexedDB.open('mniptv-web', 1);
    r.onupgradeneeded = () => r.result.createObjectStore('kv');
    r.onsuccess = () => res(r.result);
    r.onerror = () => rej(r.error);
  });
  return dbp;
}
export async function idbGet(key) { try { const d = await db(); return await new Promise((res, rej) => { const q = d.transaction('kv').objectStore('kv').get(key); q.onsuccess = () => res(q.result); q.onerror = () => rej(q.error); }); } catch { return undefined; } }
export async function idbSet(key, val) { try { const d = await db(); await new Promise((res, rej) => { const t = d.transaction('kv', 'readwrite'); t.objectStore('kv').put(val, key); t.oncomplete = res; t.onerror = () => rej(t.error); }); } catch {} }
export async function idbDel(key) { try { const d = await db(); await new Promise((res) => { const t = d.transaction('kv', 'readwrite'); t.objectStore('kv').delete(key); t.oncomplete = res; t.onerror = res; }); } catch {} }

// ---------- hesap / oturum ----------
const SESSION_KEY = 'mniptv:provider-session:v3';
const REMEMBER_FLAG = 'mniptv:remember:v1';
export const account = {
  get() {
    try { const v = sessionStorage.getItem(SESSION_KEY); if (v) return JSON.parse(v); } catch {}
    try { if (localStorage.getItem(REMEMBER_FLAG) === '1') { const v = localStorage.getItem(SESSION_KEY); if (v) return JSON.parse(v); } } catch {}
    return null;
  },
  save(src, remember) {
    try {
      sessionStorage.setItem(SESSION_KEY, JSON.stringify(src));
      if (remember) { localStorage.setItem(REMEMBER_FLAG, '1'); localStorage.setItem(SESSION_KEY, JSON.stringify(src)); }
      else { localStorage.removeItem(REMEMBER_FLAG); localStorage.removeItem(SESSION_KEY); }
    } catch {}
  },
  clear() { try { sessionStorage.removeItem(SESSION_KEY); localStorage.removeItem(REMEMBER_FLAG); localStorage.removeItem(SESSION_KEY); } catch {} },
  remembered() { try { return localStorage.getItem(REMEMBER_FLAG) === '1'; } catch { return false; } },
};

export function appsNormalizeServer(input) { // uygulamalardaki Account.Normalize ile aynı (senkron kimliği için)
  let s = String(input || '').trim();
  if (!s) return s;
  if (!/^https?:\/\//i.test(s)) s = 'http://' + s;
  for (const m of ['/player_api.php', '/get.php']) { const i = s.indexOf(m); if (i >= 0) s = s.slice(0, i); }
  while (s.endsWith('/')) s = s.slice(0, -1);
  return s;
}
function normalizeBase(raw) {
  let v = String(raw || '').trim();
  if (!v) throw appError('Sunucu adresi gerekli.', 'server');
  if (!/^[a-z][a-z0-9+.-]*:\/\//i.test(v)) v = `http://${v}`;
  let u; try { u = new URL(v); } catch { throw appError('Sunucu adresi geçersiz.', 'server'); }
  if (!['http:', 'https:'].includes(u.protocol) || !u.hostname) throw appError('Sunucu adresi HTTP veya HTTPS olmalı.', 'server');
  u.username = ''; u.password = ''; u.search = ''; u.hash = '';
  u.pathname = u.pathname.replace(/(?:player_api|xmltv|get)\.php\/?$/i, '');
  if (!u.pathname.endsWith('/')) u.pathname += '/';
  return u.toString();
}

async function http(url, { kind = 'json', timeout = 45000, signal } = {}) {
  if (location.protocol === 'https:' && /^http:/i.test(url)) throw appError('Bu IPTV kaynağı HTTP kullanıyor. Siteyi http://mavinokta.pro/iptv adresinden aç.', 'mixed');
  const ctl = new AbortController();
  const t = setTimeout(() => ctl.abort(), timeout);
  if (signal) signal.addEventListener('abort', () => ctl.abort(), { once: true });
  let r;
  try { r = await fetch(url, { cache: 'no-store', credentials: 'omit', signal: ctl.signal }); }
  catch (e) {
    clearTimeout(t);
    if (ctl.signal.aborted && !signal?.aborted) throw appError('Sunucu zamanında yanıt vermedi.', 'timeout');
    throw appError('IPTV sunucusuna tarayıcıdan ulaşılamadı. Adres/port yanlış olabilir ya da sağlayıcı web erişimini (CORS) engelliyor.', 'unreachable');
  }
  clearTimeout(t);
  if (!r.ok) throw appError(r.status === 403 ? 'IPTV sunucusu bağlantıyı reddetti (HTTP 403).' : `IPTV sunucusu HTTP ${r.status} döndürdü.`, 'http');
  if (kind === 'text') return r.text();
  const text = await r.text();
  try { return JSON.parse(text); } catch { throw appError('Sunucu geçerli bir yanıt vermedi. Kullanıcı adı/şifre kontrol edilmeli.', 'badjson'); }
}

export function apiUrl(src, action = '', params = {}) {
  const u = new URL('player_api.php', src.base);
  u.searchParams.set('username', src.username); u.searchParams.set('password', src.password);
  if (action) u.searchParams.set('action', action);
  for (const [k, v] of Object.entries(params)) u.searchParams.set(k, String(v));
  return u.toString();
}

export async function login(data) {
  const type = data.type || 'xtream';
  const name = String(data.name || '').trim() || 'IPTV';
  if (type === 'm3u') {
    let u; try { u = new URL(String(data.url || '').trim()); } catch { throw appError('M3U bağlantısı geçersiz.', 'm3u'); }
    const text = await http(u.toString(), { kind: 'text', timeout: 90000 });
    if (!text.replace(/^\uFEFF/, '').trimStart().startsWith('#EXTM3U')) throw appError('Bağlantı geçerli bir M3U listesi değil.', 'm3u');
    return { type: 'm3u', name, url: u.toString(), key: stableId(u.toString()) };
  }
  const base = normalizeBase(data.server);
  const username = String(data.username || '').trim(), password = String(data.password || '').trim();
  if (!username || !password) throw appError('Kullanıcı adı ve şifre gerekli.', 'cred');
  const tmp = { type: 'xtream', name, base, username, password, syncServer: appsNormalizeServer(data.server) };
  const root = await http(apiUrl(tmp));
  const ui = root?.user_info;
  if (!ui || ui.auth === 0 || ui.auth === '0') throw appError('Kullanıcı adı veya şifre hatalı.', 'auth');
  if (ui.status && String(ui.status).toLowerCase() !== 'active') throw appError(`Hesap durumu: ${ui.status}`, 'status');
  const fmts = Array.isArray(ui.allowed_output_formats) ? ui.allowed_output_formats.map((v) => String(v).toLowerCase()) : [];
  const liveExt = fmts.includes('m3u8') ? 'm3u8' : fmts.includes('ts') ? 'ts' : 'm3u8';
  const host = new URL(base).hostname.toLowerCase();
  return { ...tmp, liveExt, key: stableId(`${host}|${username}`), exp: ui.exp_date || null, maxConn: ui.max_connections || null };
}

// ---------- katalog ----------
export const cat = { items: [], byId: new Map(), cats: { live: [], movies: [], series: [] }, bySection: { live: [], movies: [], series: [] } };

function index(items, order) {
  cat.items = items; cat.byId = new Map(); cat.bySection = { live: [], movies: [], series: [] };
  const counts = { live: new Map(), movies: new Map(), series: new Map() };
  for (const it of items) {
    cat.byId.set(it.id, it);
    it._s = norm(`${it.name} ${it.group}`);
    cat.bySection[it.section]?.push(it);
    const m = counts[it.section]; if (m) m.set(it.group, (m.get(it.group) || 0) + 1);
  }
  for (const sec of Object.keys(cat.cats)) {
    const names = order?.[sec]?.length ? order[sec].filter((n) => counts[sec].has(n)).concat([...counts[sec].keys()].filter((n) => !order[sec].includes(n))) : [...counts[sec].keys()].sort((a, b) => a.localeCompare(b, 'tr'));
    cat.cats[sec] = names.map((name) => ({ name, count: counts[sec].get(name) }));
  }
}

function metaValue(key, line) {
  const q = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const m = String(line).match(new RegExp('(?:^|\\s)' + q + '\\s*=\\s*(?:"([^"]*)"|\'([^\']*)\')', 'i'));
  return m ? String(m[1] || m[2] || '').trim() : '';
}
function classifyM3U(url, group, name, meta) {
  for (const key of ['stream-type', 'content-type', 'tvg-type', 'type']) {
    const v = metaValue(key, meta).toLowerCase();
    if (['live', 'tv'].includes(v)) return 'live';
    if (['movie', 'movies', 'vod', 'film'].includes(v)) return 'movies';
    if (['series', 'episode', 'show'].includes(v)) return 'series';
  }
  const path = new URL(url).pathname.toLowerCase().split('/').filter(Boolean);
  if (path.includes('live')) return 'live'; if (path.includes('movie')) return 'movies'; if (path.includes('series')) return 'series';
  const hay = `${group} ${name}`.toLocaleLowerCase('tr-TR');
  if (/\b(dizi|series|sezon|season|episode|bölüm|bolum)\b/u.test(hay) || /\bS\d{1,2}\s*E\d{1,3}\b/i.test(name)) return 'series';
  const ext = new URL(url).pathname.split('.').pop().toLowerCase();
  return ['mp4', 'mkv', 'avi', 'mov', 'm4v', 'webm'].includes(ext) ? 'movies' : 'live';
}
function parseM3U(text, src) {
  const lines = text.replace(/^\uFEFF/, '').split(/\r\n|\r|\n/);
  const items = []; let name = '', group = 'Diğer', meta = '', n = 0;
  for (const raw of lines) {
    const line = raw.trim();
    if (line.startsWith('#EXTINF:')) {
      meta = line; group = metaValue('group-title', line) || 'Diğer';
      let q = false, sep = -1;
      for (let i = 0; i < line.length; i++) { if (line[i] === '"') q = !q; else if (line[i] === ',' && !q) { sep = i; break; } }
      name = sep >= 0 ? line.slice(sep + 1).trim() : metaValue('tvg-name', line);
    } else if (line.startsWith('#EXTGRP:')) group = line.slice(8).trim() || 'Diğer';
    else if (line && !line.startsWith('#')) {
      let u; try { u = new URL(line); } catch { name = ''; group = 'Diğer'; meta = ''; continue; }
      const section = classifyM3U(u.toString(), group, name, meta);
      const ext = u.pathname.includes('.') ? u.pathname.split('.').pop() : '';
      items.push({ id: stableId(`${src.key}|${u}`), name: name || `İçerik ${items.length + 1}`, group, section, artwork: metaValue('tvg-logo', meta) || null, providerId: null, tvgId: metaValue('tvg-id', meta) || null, added: null, ext: section === 'live' ? 'm3u8' : (ext || 'mp4'), directUrl: u.toString(), num: section === 'live' ? ++n : 0 });
      name = ''; group = 'Diğer'; meta = '';
    }
  }
  return items;
}

async function fetchXtream(src, progress) {
  const steps = [['live', 'get_live_categories', 'get_live_streams'], ['movies', 'get_vod_categories', 'get_vod_streams'], ['series', 'get_series_categories', 'get_series']];
  const items = [], order = {}; const warnings = [];
  let done = 0;
  for (const [section, ck, sk] of steps) {
    const names = new Map(); order[section] = [];
    let cats = [], rows = [];
    try { cats = await http(apiUrl(src, ck)); if (!Array.isArray(cats)) cats = []; } catch (e) { warnings.push(e.message); }
    progress?.(++done, 6);
    await new Promise((r) => setTimeout(r, 120));
    try { rows = await http(apiUrl(src, sk), { timeout: 120000 }); if (!Array.isArray(rows)) rows = []; } catch (e) { warnings.push(e.message); }
    progress?.(++done, 6);
    await new Promise((r) => setTimeout(r, 120));
    for (const c of cats) if (c && typeof c === 'object') { const nm = String(c.category_name || '').trim() || 'Diğer'; names.set(String(c.category_id ?? ''), nm); if (!order[section].includes(nm)) order[section].push(nm); }
    for (const row of rows) {
      if (!row || typeof row !== 'object') continue;
      const pid = String(row[section === 'series' ? 'series_id' : 'stream_id'] ?? '').trim(); if (!pid) continue;
      const c = String(row.category_id ?? '');
      const art = String(row[section === 'series' ? 'cover' : 'stream_icon'] || '').trim() || null;
      const addedRaw = String(row[section === 'series' ? 'last_modified' : 'added'] ?? '').trim();
      items.push({
        id: stableId(`${src.key}|${section}|${pid}`), name: String(row.name || pid).trim(), group: names.get(c) || (c ? `Kategori ${c}` : 'Diğer'), section,
        artwork: art, providerId: pid, tvgId: String(row.epg_channel_id || '').trim() || null, added: addedRaw ? Number(addedRaw) || null : null,
        ext: section === 'live' ? (src.liveExt || 'm3u8') : (String(row.container_extension || '').trim() || 'mp4'),
        num: Number(row.num) || 0, rating: Number(row.rating_5based || row.rating || 0) || 0,
      });
    }
  }
  if (!items.length && warnings.length) throw appError(warnings[0], 'catalog');
  return { items, order, warnings };
}

export async function loadCatalog(src, { progress, force = false } = {}) {
  const ck = `catalog:${src.key}`;
  const cached = await idbGet(ck);
  const fresh = cached?.items?.length && Date.now() - (cached.savedAt || 0) < 6 * 3600 * 1000;
  if (cached?.items?.length && !force) {
    index(cached.items, cached.order);
    return { fromCache: true, stale: !fresh, warnings: [] };
  }
  let res;
  if (src.type === 'm3u') res = { items: parseM3U(await http(src.url, { kind: 'text', timeout: 120000 }), src), order: null, warnings: [] };
  else res = await fetchXtream(src, progress);
  index(res.items, res.order);
  await idbSet(ck, { items: res.items, order: res.order, savedAt: Date.now() });
  return { fromCache: false, stale: false, warnings: res.warnings };
}
export async function refreshInBackground(src) {
  let res;
  if (src.type === 'm3u') res = { items: parseM3U(await http(src.url, { kind: 'text', timeout: 120000 }), src), order: null };
  else res = await fetchXtream(src);
  if (!res.items.length) return false;
  index(res.items, res.order);
  await idbSet(`catalog:${src.key}`, { items: res.items, order: res.order, savedAt: Date.now() });
  return true;
}

export async function loadDetails(src, item) {
  if (src.type !== 'xtream' || !item.providerId) return { title: item.name, plot: '', episodes: [] };
  const isSeries = item.section === 'series';
  const root = await http(apiUrl(src, isSeries ? 'get_series_info' : 'get_vod_info', { [isSeries ? 'series_id' : 'vod_id']: item.providerId }));
  const info = root?.info && typeof root.info === 'object' ? root.info : {};
  const back = info.backdrop_path;
  const d = {
    title: String(info.name || info.title || '').trim(), plot: String(info.plot || info.description || '').trim(), year: String(info.releasedate || info.releaseDate || info.year || '').slice(0, 4),
    duration: String(info.duration || '').trim(), genre: String(info.genre || '').trim(), cast: String(info.cast || info.actors || '').trim(), director: String(info.director || '').trim(),
    rating: String(info.rating || info.rating_5based || '').trim(), poster: safeImage(info.movie_image || info.cover) || null, backdrop: safeImage(Array.isArray(back) ? back[0] : back) || null, episodes: [],
  };
  if (isSeries) {
    const eroot = root.episodes && typeof root.episodes === 'object' ? root.episodes : {};
    const list = Array.isArray(eroot) ? { 1: eroot } : eroot;
    for (const [sk, rows] of Object.entries(list)) {
      if (!Array.isArray(rows)) continue;
      for (const row of rows) {
        if (!row || typeof row !== 'object') continue;
        const eid = String(row.id ?? '').trim(); if (!eid) continue;
        const ei = row.info && typeof row.info === 'object' ? row.info : {};
        const season = Number(String(row.season ?? sk)) || 0, episode = Number(String(row.episode_num ?? '0')) || 0;
        d.episodes.push({
          id: stableId(`${src.key}|episode|${eid}`), name: String(row.title || `Bölüm ${episode}`).trim(), section: 'series', artwork: safeImage(ei.movie_image) || d.poster,
          providerId: eid, ext: String(row.container_extension || '').trim() || 'mp4', seriesId: item.id, season, episode, plot: String(ei.plot || '').trim(), dur: Number(ei.duration_secs) || 0,
        });
      }
    }
    d.episodes.sort((a, b) => a.season - b.season || a.episode - b.episode);
  }
  return d;
}

export function mediaUrl(src, item) {
  if (item.directUrl) return item.directUrl;
  if (!src || src.type !== 'xtream' || !item.providerId) return '';
  const route = item.section === 'live' ? 'live' : item.section === 'movies' ? 'movie' : 'series';
  const ext = String(item.ext || (item.section === 'live' ? src.liveExt || 'm3u8' : 'mp4')).replace(/[^a-zA-Z0-9]/g, '') || 'mp4';
  return new URL(`${route}/${encodeURIComponent(src.username)}/${encodeURIComponent(src.password)}/${encodeURIComponent(item.providerId)}.${ext}`, src.base).toString();
}

// ---------- canlı yayın programı (kısa EPG) ----------
const epgCache = new Map(); const epgQueue = []; let epgActive = 0;
export function epgGet(item) { const e = epgCache.get(item.id); return e && e.until > Date.now() ? e : null; }
export function epgRequest(src, item, cb) {
  if (src?.type !== 'xtream' || !item.providerId || epgGet(item) || epgCache.get(item.id)?.pending) return;
  epgCache.set(item.id, { pending: true, until: 0 });
  epgQueue.push({ src, item, cb }); pumpEpg();
}
function b64u(s) { try { return decodeURIComponent(escape(atob(s))); } catch { try { return atob(s); } catch { return s; } } }
async function pumpEpg() {
  while (epgActive < 3 && epgQueue.length) {
    const { src, item, cb } = epgQueue.shift(); epgActive++;
    (async () => {
      try {
        const r = await http(apiUrl(src, 'get_short_epg', { stream_id: item.providerId, limit: 3 }), { timeout: 12000 });
        const now = Date.now();
        const list = (r?.epg_listings || []).map((p) => ({ title: b64u(p.title || ''), start: Number(p.start_timestamp) * 1000, stop: Number(p.stop_timestamp) * 1000 })).filter((p) => p.title && p.stop).sort((a, b) => a.start - b.start);
        const cur = list.find((p) => p.start <= now && p.stop > now) || null;
        const next = list.find((p) => p.start > now) || null;
        epgCache.set(item.id, { now: cur, next, until: Math.min(cur ? cur.stop : now + 600000, now + 1800000) });
      } catch { epgCache.set(item.id, { now: null, next: null, until: Date.now() + 300000 }); }
      epgActive--; cb?.(item); pumpEpg();
    })();
  }
}
