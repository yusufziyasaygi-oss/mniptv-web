// Kullanıcı verisi: favoriler, izleme geçmişi, izlendi işaretleri
import { store, cat, clamp } from './core.js';
export const data = { favorites: new Set(), history: {}, watched: new Set(), eppos: {}, settings: { autoNext: true } };
export const hooks = { fav: null, prog: null, progDel: null };
export function loadData(key) {
  store.key = key;
  data.favorites = new Set(store.load('favorites', []));
  data.history = store.load('history', {});
  data.watched = new Set(store.load('watched', []));
  data.eppos = store.load('eppos', {});
  data.settings = { autoNext: true, ...store.gget('settings', {}) };
}
export const persist = {
  fav: () => store.save('favorites', [...data.favorites]),
  hist: () => store.save('history', data.history),
  watched: () => store.save('watched', [...data.watched]),
  eppos: () => store.save('eppos', data.eppos),
  settings: () => store.gset('settings', data.settings),
};
export function toggleFav(item) {
  const add = !data.favorites.has(item.id);
  if (add) data.favorites.add(item.id); else data.favorites.delete(item.id);
  persist.fav(); hooks.fav?.(item, add); return add;
}
export const isFav = (item) => data.favorites.has(item.id);
export function favItems() { return [...data.favorites].map((id) => cat.byId.get(id)).filter(Boolean); }

let lastSave = 0;
export function recordProgress(item, pos, dur, force = false) {
  if (!item || item.section === 'live' || !Number.isFinite(pos) || pos < 1) return;
  const now = Date.now();
  if (!force && now - lastSave < 5000) return;
  lastSave = now;
  dur = Number.isFinite(dur) ? dur : 0;
  const finished = dur > 0 && pos / dur > 0.95;
  hooks.prog?.(item, pos, dur, finished);
  if (item.seriesId) {
    if (finished) { data.watched.add(item.id); persist.watched(); delete data.eppos[item.id]; } else if (pos > 15) data.eppos[item.id] = pos;
    persist.eppos();
  }
  if (finished) delete data.history[item.id];
  else data.history[item.id] = { id: item.id, position: pos, duration: dur, updated: now, item: { ...item }, seriesId: item.seriesId || null, seriesName: item.seriesName || null, seriesArtwork: item.seriesArtwork || null, season: item.season || null, episode: item.episode || null, group: item.group || '' };
  persist.hist();
}
export function resumePosition(item) {
  const h = data.history[item.id];
  const p = h?.position || data.eppos[item.id] || 0;
  return p > 5 ? p : 0;
}
export function continueList() {
  const out = [], seen = new Set();
  const entries = Object.values(data.history).filter((h) => h && h.position >= 5 && !(h.duration > 0 && h.position / h.duration > 0.95)).sort((a, b) => (b.updated || 0) - (a.updated || 0));
  for (const h of entries) {
    if (h.seriesId) {
      if (seen.has(h.seriesId)) continue; seen.add(h.seriesId);
      const parent = cat.byId.get(h.seriesId) || { id: h.seriesId, name: h.seriesName || 'Dizi', section: 'series', artwork: h.seriesArtwork, group: h.group || '', providerId: null };
      out.push({ item: parent, h, pct: h.duration ? clamp(h.position / h.duration * 100, 0, 100) : 0, label: `S${h.season || 0} · B${h.episode || 0}` });
    } else {
      const item = cat.byId.get(h.id) || h.item; if (!item) continue;
      out.push({ item, h, pct: h.duration ? clamp(h.position / h.duration * 100, 0, 100) : 0, label: 'Film' });
    }
  }
  return out;
}
export function removeContinue(item) {
  hooks.progDel?.(item);
  for (const [id, h] of Object.entries(data.history)) if (id === item.id || h?.seriesId === item.id) delete data.history[id];
  persist.hist();
}
