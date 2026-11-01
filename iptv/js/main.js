// MN IPTV Web — uygulama
import { $, $$, esc, fmtTime, norm, safeImage, sectionLabel, store, account, cat, login, loadCatalog, refreshInBackground, loadDetails, epgGet, epgRequest, stableId, clamp } from './core.js';
import { data, loadData, persist, toggleFav, isFav, favItems, continueList, removeContinue } from './data.js';
import { Player } from './player.js';
import { icon } from './icons.js';
import { syncStart, syncStop, syncStatus, syncEnabled, syncNow, syncFlush, syncPull } from './sync.js';

if (location.hostname === 'mavinokta.pro' && location.protocol === 'https:' && location.pathname.startsWith('/iptv')) {
  location.replace(`http://${location.host}${location.pathname}${location.search}${location.hash}`);
  throw new Error('redirect');
}

const app = $('#app');
const S = { src: null, route: 'home', params: {}, ov: [], skipPop: 0, sheet: null, mode: 'xtream', ui: { sort: 'new', cat: {} }, search: '' };
const APPS = [
  ['macOS', 'DMG', '1yeSmAepFYs56tFeIKL47TXqDmzlGTzSA'], ['Windows', 'EXE', '1Fjbz7yuP1ZqrQ9y-rcz1aHUfoTLrwaM-'],
  ['Android', 'APK', '1rQVpw3qMEZHoT6Lvf1ywjaJgwqQEsDwY'], ['Android TV', 'APK', '12Bne0unAR8b1pfVdwAXGKgxJlEPM8xiv'],
];
const driveUrl = (id) => `https://drive.google.com/uc?export=download&id=${id}`;

// ---------------- yardımcılar ----------------
function toast(title, msg = '') {
  const box = $('#toasts') || document.body.appendChild(Object.assign(document.createElement('div'), { id: 'toasts' }));
  const t = document.createElement('div'); t.className = 'toast'; t.innerHTML = `<b>${esc(title)}</b>${msg ? `<span>${esc(msg)}</span>` : ''}`;
  box.appendChild(t); setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 250); }, 3200);
}
const initials = (n) => (String(n || '?').replace(/[^\p{L}\p{N} ]/gu, '').trim().split(/\s+/).slice(0, 2).map((w) => w[0]).join('') || '?').toUpperCase();
const hue = (n) => { let h = 0; for (const c of String(n)) h = (h * 31 + c.codePointAt(0)) % 360; return h; };
function img(url, alt = '') { const u = safeImage(url); return u ? `<img loading="lazy" decoding="async" referrerpolicy="no-referrer" src="${esc(u)}" alt="${esc(alt)}" onerror="this.remove()">` : ''; }
function art(item, cls = 'poster') {
  return `<div class="${cls}" style="--h:${hue(item.name)}"><span class="ph">${esc(initials(item.name))}</span>${img(item.artwork, item.name)}</div>`;
}
function pushOv(k) { S.ov.push(k); history.pushState({ ov: k }, ''); }
function popOv(k) { const i = S.ov.lastIndexOf(k); if (i < 0) return; const top = i === S.ov.length - 1; S.ov.splice(i, 1); if (top) { S.skipPop++; history.back(); } }
window.addEventListener('popstate', () => {
  if (S.skipPop > 0) { S.skipPop--; return; }
  const k = S.ov.pop();
  if (k === 'player') player.close(true), afterPlayer(); else if (k === 'sheet') hideSheet(); else if (k === 'dialog') hideDialog();
});

const player = new Player({
  getSource: () => S.src,
  onClose: () => { popOv('player'); afterPlayer(); },
  onFav: (it) => { const a = toggleFav(it); toast(a ? 'Favorilere eklendi' : 'Favorilerden çıkarıldı', it.name); },
  isFav: (it) => isFav(it),
});
function afterPlayer() { syncFlush(); if (S.sheet) renderSheet(); else if (['home', 'continue'].includes(S.route)) render(); }

// ---------------- oturum ----------------
async function boot() {
  const src = account.get();
  if (!src) return showLogin();
  await startSession(src);
}
async function startSession(src, err) {
  S.src = src; loadData(src.key);
  app.innerHTML = splash('Hazırlanıyor…', 0);
  try {
    const r = await loadCatalog(src, { progress: (n, t) => { app.innerHTML = splash('İçerikler alınıyor…', n / t, ['Canlı TV kategorileri', 'Canlı kanallar', 'Film kategorileri', 'Filmler', 'Dizi kategorileri', 'Diziler'][n - 1] || ''); } });
    if (r.warnings?.length) toast('Liste kısmen yüklendi', 'Bazı bölümler sağlayıcı tarafından reddedildi.');
    if (!cat.items.length) throw new Error('Kaynakta içerik bulunamadı.');
    startApp();
    if (r.fromCache && r.stale) refreshInBackground(src).then((ok) => { if (ok) { toast('Liste güncellendi'); if (!player.isOpen()) render(); } }).catch(() => {});
  } catch (e) { showLogin(e.message || 'Bağlanılamadı.'); }
}
function splash(text, p, sub = '') {
  return `<div class="splash"><div class="logo">${logo()}</div><strong>MN IPTV</strong><div class="bar"><i style="width:${Math.round(p * 100)}%"></i></div><span>${esc(text)}${sub ? ' · ' + esc(sub) : ''}</span></div>`;
}
const logo = () => '<svg viewBox="0 0 48 48" width="100%" height="100%"><rect width="48" height="48" rx="12" fill="#0b1224"/><path d="M12 35V13h4.500l7.500 12 7.500-12H36v22h-4.500V21.500L24 33l-7.500-11.500V35z" fill="#fff"/><circle cx="37" cy="11" r="4.500" fill="#2f7bff"/></svg>';
function logout() {
  syncFlush(); syncStop(); player.close(true); account.clear(); S.src = null; cat.items = []; history.replaceState(null, '', location.pathname); showLogin();
}

// ---------------- giriş ----------------
function showLogin(error = '') {
  const sv = account.get() || {}; const mode = S.modeSet ? S.mode : (sv.type || S.mode); S.mode = mode;
  const dr = S.draft || {};
  const v = (k) => esc(dr[k] ?? (sv.type === mode ? (sv[k] || '') : ''));
  const srv = esc(dr.server ?? (sv.type === 'xtream' && mode === 'xtream' ? String(sv.base || '').replace(/\/$/, '') : ''));
  app.innerHTML = `<main class="login">
    <form id="lf" class="lcard" autocomplete="on">
      <div class="lbrand"><div class="logo big">${logo()}</div><h1>MN IPTV</h1><p>Hesabını bağla, kaldığın yerden devam et.</p></div>
      <div class="seg"><button type="button" data-mode="xtream" class="${mode === 'xtream' ? 'on' : ''}">Hesap bilgileri</button><button type="button" data-mode="m3u" class="${mode === 'm3u' ? 'on' : ''}">M3U bağlantısı</button></div>
      ${error ? `<div class="lerr" role="alert">${esc(error)}</div>` : ''}
      <label class="fld"><span>Profil adı</span><input name="name" placeholder="Ev IPTV" autocomplete="nickname" value="${v('name')}"></label>
      ${mode === 'xtream' ? `
        <label class="fld"><span>Sunucu adresi</span><input name="server" inputmode="url" placeholder="http://sunucu:port" autocomplete="url" required value="${srv}"></label>
        <div class="two"><label class="fld"><span>Kullanıcı adı</span><input name="username" autocomplete="username" required value="${v('username')}"></label>
        <label class="fld"><span>Şifre</span><input name="password" type="password" autocomplete="current-password" required value="${v('password')}"></label></div>`
      : `<label class="fld"><span>M3U bağlantısı</span><input name="url" inputmode="url" placeholder="http://…/get.php?…" required value="${v('url')}"></label>`}
      <label class="chk"><input type="checkbox" name="remember" ${account.remembered() || !sv.type ? 'checked' : ''}><span>Beni hatırla <small>Bilgiler yalnızca bu tarayıcıda saklanır.</small></span></label>
      <button class="btn primary lg" type="submit">Bağlan</button>
      <p class="fine">MN IPTV içerik sağlamaz; kendi yetkili IPTV hesabını bağlarsın.</p>
    </form>
    <div class="lapps"><span>Uygulamalar</span>${APPS.map(([n, f, id]) => `<a href="${driveUrl(id)}" target="_blank" rel="noopener">${esc(n)} <small>${f}</small></a>`).join('')}</div>
  </main>`;
  $$('[data-mode]').forEach((b) => b.addEventListener('click', () => { S.draft = null; S.mode = b.dataset.mode; S.modeSet = true; showLogin(); }));
  $('#lf').addEventListener('submit', async (e) => {
    e.preventDefault(); const f = new FormData(e.currentTarget); const btn = $('button[type=submit]', e.currentTarget);
    btn.disabled = true; btn.innerHTML = '<span class="spin"></span> Bağlanılıyor…';
    S.draft = { name: f.get('name') || '', server: f.get('server') || '', username: f.get('username') || '', password: f.get('password') || '', url: f.get('url') || '' };
    try {
      const src = await login({ type: mode, name: f.get('name'), server: f.get('server'), username: f.get('username'), password: f.get('password'), url: f.get('url') });
      account.save(src, f.get('remember') === 'on'); S.draft = null;
      await startSession(src);
    } catch (er) { showLogin(er.message); }
  });
}

// ---------------- kabuk ----------------
const NAV = [['home', 'home', 'Ana Sayfa'], ['continue', 'resume', 'İzlemeye Devam'], ['live', 'live', 'Canlı TV'], ['movies', 'film', 'Filmler'], ['series', 'series', 'Diziler'], ['favorites', 'heart', 'Favoriler']];
const TABS = [['home', 'home', 'Ana Sayfa'], ['live', 'live', 'Canlı'], ['movies', 'film', 'Filmler'], ['series', 'series', 'Diziler'], ['more', 'more', 'Daha']];
function startApp() {
  syncStart({ getAccount: () => account.get(), onApplied: () => { if (!player.isOpen()) render(); }, playingId: () => (player.isOpen() ? player.item?.id : null) });
  app.innerHTML = `<div class="shell">
    <aside class="side">
      <a class="brand" href="#/home">${`<span class="logo">${logo()}</span>`}<b>MN IPTV</b></a>
      <nav>${NAV.map(([r, ic, l]) => `<a href="#/${r}" data-r="${r}">${icon(ic, 20)}<span>${l}</span></a>`).join('')}<a href="#/search" data-r="search">${icon('search', 20)}<span>Ara</span></a><a href="#/settings" data-r="settings">${icon('settings', 20)}<span>Ayarlar</span></a></nav>
      <button class="acct" data-act="account"><span class="av">${esc(initials(S.src.name))}</span><span class="at"><small>Hesap</small><b>${esc(S.src.name || 'IPTV')}</b></span>${icon('more', 18)}</button>
    </aside>
    <div class="main">
      <header class="top"><a class="brand m" href="#/home"><span class="logo">${logo()}</span></a>
        <label class="search">${icon('search', 18)}<input id="q" type="search" placeholder="Film, dizi veya kanal ara…" autocomplete="off" enterkeyhint="search"><kbd>/</kbd></label>
        <button class="sync" data-act="syncnow" title="Senkron">${icon('sync', 18)}</button>
        <button class="iconbtn m" data-act="account" aria-label="Hesap">${icon('user', 20)}</button>
      </header>
      <main id="view" tabindex="-1"></main>
    </div>
    <nav class="tabs">${TABS.map(([r, ic, l]) => `<a href="${r === 'more' ? '#/settings' : '#/' + r}" data-r="${r}">${icon(ic, 22)}<span>${l}</span></a>`).join('')}</nav>
  </div>`;
  const q = $('#q'); let t;
  q.addEventListener('input', () => { clearTimeout(t); t = setTimeout(() => { S.search = q.value; if (q.value.trim()) { if (S.route !== 'search') { location.hash = '#/search'; } else render(); } }, 180); });
  q.addEventListener('keydown', (e) => { if (e.key === 'Enter') { S.search = q.value; if (S.route !== 'search') location.hash = '#/search'; else render(); q.blur(); } if (e.key === 'Escape') q.blur(); });
  route();
}
window.addEventListener('hashchange', () => { if (S.src) route(); });
document.addEventListener('keydown', (e) => {
  if (!S.src || player.isOpen()) return;
  if ((e.key === '/' || (e.key === 'k' && (e.ctrlKey || e.metaKey))) && !e.target.matches('input,textarea')) { e.preventDefault(); $('#q')?.focus(); }
});
function route() {
  const h = (location.hash || '#/home').replace(/^#\/?/, ''); const [r, qs] = h.split('?');
  S.route = ['home', 'live', 'movies', 'series', 'continue', 'favorites', 'search', 'settings'].includes(r) ? r : 'home';
  S.params = Object.fromEntries(new URLSearchParams(qs || ''));
  if (S.route === 'search' && S.params.q != null) S.search = S.params.q;
  const q = $('#q'); if (q && S.route === 'search' && q.value !== S.search) q.value = S.search;
  if (q && S.route !== 'search') { q.value = ''; S.search = ''; }
  $$('[data-r]').forEach((a) => a.classList.toggle('on', a.dataset.r === S.route || (a.dataset.r === 'more' && S.route === 'settings')));
  render(true);
}
function render(top) {
  const v = $('#view'); if (!v) return; const y = top ? 0 : window.scrollY;
  const fn = { home: viewHome, live: () => viewBrowse('live'), movies: () => viewBrowse('movies'), series: () => viewBrowse('series'), continue: viewContinue, favorites: viewFavs, search: viewSearch, settings: viewSettings }[S.route];
  v.className = `view v-${S.route}`; v.innerHTML = ''; fn(v);
  if (top) window.scrollTo(0, 0); else window.scrollTo(0, y);
  $('.sync')?.classList.toggle('off', !syncEnabled());
}

// ---------------- kartlar ----------------
function card(item, o = {}) {
  const live = item.section === 'live';
  const fav = isFav(item);
  if (live) return `<button class="card chan" data-open="${item.id}">${art(item, 'logo-box')}<span class="ct"><b>${esc(item.name)}</b><small>${esc(item.group)}</small></span></button>`;
  return `<div class="card ${o.wide ? 'wide' : ''}" data-open="${item.id}" ${o.resume ? 'data-resume="1"' : ''} tabindex="0" role="button" aria-label="${esc(item.name)}">
    <div class="pw">${art(item, 'poster')}
      ${o.pct ? `<i class="prog"><b style="width:${o.pct}%"></b></i>` : ''}
      ${item.rating ? `<span class="rt">${icon('star', 12)} ${item.rating.toFixed(1)}</span>` : ''}
      <button class="fv ${fav ? 'on' : ''}" data-fav="${item.id}" aria-label="Favori" tabindex="-1">${icon('heart', 16)}</button>
      <span class="pp">${icon('play', 22)}</span></div>
    <div class="cm"><b>${esc(item.name)}</b><small>${esc(o.label || item.group || sectionLabel(item.section))}</small></div></div>`;
}
function row(title, items, { route, wide, resume, pcts } = {}) {
  if (!items.length) return '';
  return `<section class="rowsec"><div class="rowh"><h2>${esc(title)}</h2>${route ? `<a href="#/${route}">Tümü ${icon('chevron', 14)}</a>` : ''}</div>
    <div class="scroller">${items.map((it, i) => card(it, { wide, resume, pct: pcts?.[i]?.pct, label: pcts?.[i]?.label })).join('')}</div></section>`;
}
const byAdded = (a, b) => (b.added || 0) - (a.added || 0);

// ---------------- ana sayfa ----------------
function viewHome(v) {
  const cont = continueList(); const favs = favItems();
  const movies = [...cat.bySection.movies].sort(byAdded).slice(0, 24);
  const series = [...cat.bySection.series].sort(byAdded).slice(0, 24);
  const live = cat.bySection.live.slice(0, 18);
  const hero = cont[0]?.item.artwork ? cont[0] : null;
  const heroItem = hero?.item || movies.find((m) => m.artwork) || series.find((m) => m.artwork) || movies[0] || series[0];
  let html = '';
  if (heroItem) {
    const resume = hero ? `<button class="btn primary lg" data-open="${heroItem.id}" data-resume="1">${icon('play', 18)} Devam et</button>` : `<button class="btn primary lg" data-open="${heroItem.id}">${icon('play', 18)} Ayrıntılar</button>`;
    html += `<section class="hero" style="--h:${hue(heroItem.name)}">${safeImage(heroItem.artwork) ? `<div class="hbg" style="background-image:url('${esc(safeImage(heroItem.artwork))}')"></div>` : ''}<div class="hfade"></div>
      <div class="hc"><span class="chip">${hero ? 'Kaldığın yerden' : esc(sectionLabel(heroItem.section))}</span><h1>${esc(heroItem.name)}</h1><p>${hero ? `${esc(hero.label)} · ${fmtTime(hero.h.position)} / ${fmtTime(hero.h.duration)}` : esc(heroItem.group)}</p>
      <div class="ha">${resume}<button class="btn lg" data-fav="${heroItem.id}">${icon('heart', 18)} ${isFav(heroItem) ? 'Favorilerde' : 'Favorilere ekle'}</button></div></div>
      <div class="hp">${art(heroItem, 'poster')}</div></section>`;
  }
  html += row('İzlemeye devam', cont.slice(0, 20).map((c) => c.item), { route: 'continue', wide: true, resume: true, pcts: cont });
  html += row('Favoriler', favs.slice(0, 24), { route: 'favorites' });
  html += row('Yeni filmler', movies, { route: 'movies' });
  html += row('Yeni diziler', series, { route: 'series' });
  html += row('Canlı TV', live, { route: 'live' });
  v.innerHTML = html || empty('Henüz içerik yok', 'Listeni yenilemeyi dene.');
}
const empty = (t, s, extra = '') => `<div class="empty"><div class="ei">${icon('tv', 34)}</div><b>${esc(t)}</b><p>${esc(s)}</p>${extra}</div>`;

// ---------------- liste görünümü ----------------
const SORTS = { new: ['Yeni eklenen', byAdded], az: ['A → Z', (a, b) => a.name.localeCompare(b.name, 'tr')], rate: ['Puan', (a, b) => (b.rating || 0) - (a.rating || 0)], num: ['Kanal no', (a, b) => (a.num || 0) - (b.num || 0)] };
function viewBrowse(section) {
  const st = S.ui[section] || (S.ui[section] = { cat: '', q: '', sort: section === 'live' ? 'num' : 'new', cq: '' });
  const cats = cat.cats[section];
  const title = { live: 'Canlı TV', movies: 'Filmler', series: 'Diziler' }[section];
  const total = cat.bySection[section].length;
  const sorts = section === 'live' ? ['num', 'az'] : ['new', 'az', 'rate'];
  document.title = `${title} · MN IPTV`;
  $('#view').innerHTML = `<div class="browse">
    <aside class="cats" id="cats"><div class="cats-h"><input type="search" id="cq" placeholder="Kategori ara" value="${esc(st.cq)}"><button class="iconbtn m" data-act="catclose" aria-label="Kapat">${icon('close', 18)}</button></div><div class="cats-l" id="cl"></div></aside>
    <section class="lst"><div class="lh"><div class="lt"><h1>${title}</h1><span id="cnt"></span></div>
      <div class="lc"><button class="btn catbtn" data-act="catopen">${icon('list', 16)} <span id="cname"></span></button>
      <label class="lf">${icon('search', 16)}<input id="lq" type="search" placeholder="Bu listede ara" value="${esc(st.q)}"></label>
      <select id="ls" aria-label="Sırala">${sorts.map((s) => `<option value="${s}" ${st.sort === s ? 'selected' : ''}>${SORTS[s][0]}</option>`).join('')}</select></div></div>
      <div id="items" class="${section === 'live' ? 'chans' : 'grid'}"></div><div id="more"></div></section></div>`;
  const drawCats = () => {
    const q = norm(st.cq); const list = cats.filter((c) => !q || norm(c.name).includes(q));
    $('#cl').innerHTML = `<button class="ci ${!st.cat ? 'on' : ''}" data-cat=""><span>Tümü</span><em>${total}</em></button>` + list.map((c) => `<button class="ci ${st.cat === c.name ? 'on' : ''}" data-cat="${esc(c.name)}"><span>${esc(c.name)}</span><em>${c.count}</em></button>`).join('');
  };
  let list = [], shown = 0, io;
  const rowHtml = (it) => section === 'live' ? chanRow(it) : card(it);
  const draw = () => {
    const q = norm(st.q);
    list = cat.bySection[section].filter((i) => (!st.cat || i.group === st.cat) && (!q || i._s.includes(q))).sort(SORTS[st.sort][1]);
    $('#cnt').textContent = `${list.length} ${section === 'live' ? 'kanal' : section === 'movies' ? 'film' : 'dizi'}`;
    $('#cname').textContent = st.cat || 'Tüm kategoriler';
    const box = $('#items'); box.innerHTML = ''; shown = 0; io?.disconnect();
    if (!list.length) { box.innerHTML = empty('Sonuç yok', 'Başka bir kategori ya da arama dene.'); $('#more').innerHTML = ''; return; }
    more(); $('#more').innerHTML = '<div class="sent"></div>';
    io = new IntersectionObserver((es) => { if (es[0].isIntersecting) more(); }, { rootMargin: '900px' }); io.observe($('#more .sent'));
  };
  const more = () => {
    if (shown >= list.length) return; const box = $('#items'); const n = section === 'live' ? 60 : 48;
    const chunk = list.slice(shown, shown + n); shown += chunk.length;
    box.insertAdjacentHTML('beforeend', chunk.map(rowHtml).join(''));
    if (section === 'live') observeEpg(box);
    if (shown >= list.length) io?.disconnect();
  };
  drawCats(); draw();
  S.browseList = () => list;
  $('#cq').addEventListener('input', (e) => { st.cq = e.target.value; drawCats(); });
  let t; $('#lq').addEventListener('input', (e) => { clearTimeout(t); t = setTimeout(() => { st.q = e.target.value; draw(); }, 150); });
  $('#ls').addEventListener('change', (e) => { st.sort = e.target.value; draw(); });
  $('#cl').addEventListener('click', (e) => { const b = e.target.closest('[data-cat]'); if (!b) return; st.cat = b.dataset.cat; drawCats(); draw(); $('#cats').classList.remove('open'); window.scrollTo(0, 0); });
}
function chanRow(it) {
  const e = epgGet(it);
  return `<div class="chan-row" data-open="${it.id}" tabindex="0" role="button">${art(it, 'logo-box')}<span class="cn">${it.num || ''}</span>
    <span class="ci2"><b>${esc(it.name)}</b><small data-epg="${it.id}">${esc(e?.now?.title || it.group)}</small>${e?.now ? `<i class="eb"><u style="width:${clamp((Date.now() - e.now.start) / (e.now.stop - e.now.start) * 100, 0, 100)}%"></u></i>` : ''}</span>
    <button class="fv ${isFav(it) ? 'on' : ''}" data-fav="${it.id}" aria-label="Favori" tabindex="-1">${icon('heart', 18)}</button><span class="go">${icon('play', 18)}</span></div>`;
}
let epgIO;
function observeEpg(box) {
  epgIO ||= new IntersectionObserver((es) => {
    for (const en of es) if (en.isIntersecting) {
      epgIO.unobserve(en.target); const it = cat.byId.get(en.target.dataset.open); if (!it) continue;
      epgRequest(S.src, it, (x) => { const r = $(`.chan-row[data-open="${CSS.escape(x.id)}"]`); const e = epgGet(x); if (r && e?.now) { $('small', r).textContent = e.now.title; if (!$('.eb', r)) $('.ci2', r).insertAdjacentHTML('beforeend', `<i class="eb"><u style="width:${clamp((Date.now() - e.now.start) / (e.now.stop - e.now.start) * 100, 0, 100)}%"></u></i>`); } });
    }
  }, { rootMargin: '150px' });
  $$('.chan-row:not([data-w])', box).forEach((r) => { r.dataset.w = 1; epgIO.observe(r); });
}

// ---------------- diğer görünümler ----------------
function viewContinue(v) {
  const c = continueList();
  v.innerHTML = `<div class="ph1"><h1>İzlemeye devam</h1><span>${c.length} içerik</span></div>` + (c.length ? `<div class="grid wide">${c.map((x) => card(x.item, { wide: true, resume: true, pct: x.pct, label: x.label }) .replace('</div></div>', `</div></div>`)).join('')}</div>` : empty('Yarım kalan içerik yok', 'Film veya dizi izlediğinde kaldığın yer burada görünür.'));
  $$('.grid .card', v).forEach((c2) => c2.insertAdjacentHTML('beforeend', `<button class="rm" data-rm="${c2.dataset.open}" aria-label="Listeden kaldır" title="Listeden kaldır">${icon('close', 14)}</button>`));
}
function viewFavs(v) {
  const f = favItems(); const tab = S.params.t || 'all';
  const cnt = (s) => f.filter((i) => i.section === s).length;
  const list = f.filter((i) => tab === 'all' || i.section === tab);
  v.innerHTML = `<div class="ph1"><h1>Favoriler</h1><span>${f.length} içerik</span></div>
    <div class="tabs2">${[['all', 'Tümü', f.length], ['live', 'Canlı', cnt('live')], ['movies', 'Filmler', cnt('movies')], ['series', 'Diziler', cnt('series')]].map(([k, l, n]) => `<a href="#/favorites?t=${k}" class="${tab === k ? 'on' : ''}">${l} <em>${n}</em></a>`).join('')}</div>` +
    (list.length ? `<div class="${tab === 'live' ? 'chans' : 'grid'}">${list.map((i) => (tab === 'live' ? chanRow(i) : card(i))).join('')}</div>` : empty('Favori yok', 'Bir içeriğin üzerindeki kalbe dokunarak buraya ekle.'));
  observeEpg(v);
}
function viewSearch(v) {
  const q = norm(S.search); const t = S.params.t || 'all';
  if (!q) { v.innerHTML = `<div class="ph1"><h1>Ara</h1></div>` + empty('Ne izlemek istersin?', 'Yukarıdaki arama kutusuna film, dizi ya da kanal adı yaz.'); setTimeout(() => $('#q')?.focus(), 50); return; }
  const all = cat.items.filter((i) => i._s.includes(q));
  const c = (s) => all.filter((i) => i.section === s).length;
  const list = all.filter((i) => t === 'all' || i.section === t).sort((a, b) => (norm(a.name).startsWith(q) ? 0 : 1) - (norm(b.name).startsWith(q) ? 0 : 1)).slice(0, 240);
  v.innerHTML = `<div class="ph1"><h1>“${esc(S.search)}”</h1><span>${all.length} sonuç</span></div>
    <div class="tabs2">${[['all', 'Tümü', all.length], ['live', 'Canlı', c('live')], ['movies', 'Filmler', c('movies')], ['series', 'Diziler', c('series')]].map(([k, l, n]) => `<a href="#/search?t=${k}" class="${t === k ? 'on' : ''}">${l} <em>${n}</em></a>`).join('')}</div>` +
    (list.length ? `<div class="${t === 'live' ? 'chans' : 'grid'}">${list.map((i) => (t === 'live' || (t === 'all' && i.section === 'live') ? chanRow(i) : card(i))).join('')}</div>${all.length > list.length ? `<p class="fine c">İlk ${list.length} sonuç gösteriliyor. Aramayı daraltabilirsin.</p>` : ''}` : empty('Sonuç bulunamadı', 'Yazımı değiştirip tekrar dene.'));
  observeEpg(v);
}
function viewSettings(v) {
  const s = S.src; const exp = s.exp && Number(s.exp) ? new Date(Number(s.exp) * 1000).toLocaleDateString('tr-TR') : '';
  let host = ''; try { host = new URL(s.base || s.url).host; } catch {}
  v.innerHTML = `<div class="ph1"><h1>Ayarlar</h1></div><div class="sgrid">
    <section class="scard"><h3>Hesap</h3><dl><dt>Profil</dt><dd>${esc(s.name)}</dd><dt>Sunucu</dt><dd>${esc(host)}</dd>${s.username ? `<dt>Kullanıcı</dt><dd>${esc(s.username)}</dd>` : ''}${exp ? `<dt>Bitiş</dt><dd>${exp}</dd>` : ''}${s.maxConn ? `<dt>Eşzamanlı bağlantı</dt><dd>${esc(s.maxConn)}</dd>` : ''}<dt>İçerik</dt><dd>${cat.bySection.live.length} kanal · ${cat.bySection.movies.length} film · ${cat.bySection.series.length} dizi</dd></dl>
      <div class="sa"><button class="btn" data-act="refresh">${icon('refresh', 16)} Listeyi yenile</button><button class="btn" data-act="switch">Hesabı değiştir</button><button class="btn danger" data-act="logout">${icon('logout', 16)} Çıkış yap</button></div></section>
    <section class="scard"><h3>Senkron</h3><p>Favoriler ve izlemeye devam kayıtların Windows, Mac ve TV uygulamalarıyla şifreli olarak eşitlenir.</p><div class="st"><span class="dot ${syncStatus() === 'Güncel' ? 'ok' : ''}"></span><b id="sst">${esc(syncStatus())}</b></div><div class="sa"><button class="btn" data-act="syncnow" ${syncEnabled() ? '' : 'disabled'}>${icon('sync', 16)} Şimdi senkronla</button></div></section>
    <section class="scard"><h3>Oynatıcı</h3><label class="sw"><span>Sonraki bölümü otomatik oynat</span><input type="checkbox" id="an" ${data.settings.autoNext ? 'checked' : ''}><i></i></label><p class="fine">Kısayollar: Boşluk oynat · ←/→ 10 sn · ↑/↓ ses · F tam ekran · M sessiz · C liste · N/P sonraki/önceki · 0-9 kanal no</p></section>
    <section class="scard"><h3>Veri</h3><p>Favoriler ve izleme geçmişi bu tarayıcıda saklanır.</p><div class="sa"><button class="btn" data-act="clearhist" data-c="0">Geçmişi temizle</button><button class="btn" data-act="clearfav" data-c="0">Favorileri temizle</button></div></section>
    <section class="scard wide2"><h3>Uygulamaları indir</h3><div class="appgrid">${APPS.map(([n, f, id]) => `<a href="${driveUrl(id)}" target="_blank" rel="noopener"><b>${esc(n)}</b><small>${f}</small>${icon('download', 18)}</a>`).join('')}</div></section></div>`;
  $('#an')?.addEventListener('change', (e) => { data.settings.autoNext = e.target.checked; persist.settings(); });
}

// ---------------- ayrıntı ekranı ----------------
function sheetEl() { return $('#sheet') || document.body.appendChild(Object.assign(document.createElement('div'), { id: 'sheet', className: 'sheet-wrap', hidden: true })); }
async function openSheet(item) {
  const first = !S.sheet;
  S.sheet = { item, d: null, season: null };
  const el = sheetEl(); el.hidden = false; document.body.classList.add('sh-open');
  if (first) pushOv('sheet');
  renderSheet();
  try { S.sheet.d = await loadDetails(S.src, item); } catch (e) { S.sheet.d = { title: item.name, plot: '', episodes: [], err: e.message }; }
  if (S.sheet?.item === item) renderSheet();
}
function hideSheet() { const el = $('#sheet'); if (el) { el.hidden = true; el.innerHTML = ''; } S.sheet = null; document.body.classList.remove('sh-open'); }
function closeSheet() { if (!S.sheet) return; hideSheet(); popOv('sheet'); }
function seriesProgress(item) { return Object.values(data.history).filter((h) => h?.seriesId === item.id).sort((a, b) => (b.updated || 0) - (a.updated || 0))[0] || null; }
function renderSheet() {
  const sh = S.sheet; if (!sh) return; const el = $('#sheet'); const { item, d } = sh; const series = item.section === 'series';
  const poster = d?.poster || item.artwork, back = d?.backdrop || d?.poster || item.artwork;
  const eps = d?.episodes || [];
  const seasons = [...new Set(eps.map((e) => e.season))].sort((a, b) => a - b);
  const last = series ? seriesProgress(item) : data.history[item.id];
  if (sh.season == null && seasons.length) sh.season = last?.season && seasons.includes(last.season) ? last.season : seasons[0];
  const meta = d ? [d.year, d.genre, d.duration, d.rating && Number(d.rating) ? `★ ${Number(d.rating).toFixed(1)}` : ''].filter(Boolean) : [];
  let primary = '';
  if (series) primary = d ? (eps.length ? `<button class="btn primary lg" data-act="playseries">${icon('play', 18)} ${last ? `Devam et · S${last.season} B${last.episode}` : 'İzlemeye başla'}</button>` : '') : '<span class="spin"></span>';
  else primary = `<button class="btn primary lg" data-act="playmovie">${icon('play', 18)} ${last?.position > 5 ? `Devam et · ${fmtTime(last.position)}` : 'İzle'}</button>`;
  const list = eps.filter((e) => e.season === sh.season);
  el.innerHTML = `<div class="sh-back" data-act="closesheet"></div><article class="sh" role="dialog" aria-modal="true" aria-label="${esc(item.name)}">
    <button class="sh-x" data-act="closesheet" aria-label="Kapat">${icon('close', 20)}</button>
    <div class="sh-hero" style="--h:${hue(item.name)}">${safeImage(back) ? `<div class="hbg" style="background-image:url('${esc(safeImage(back))}')"></div>` : ''}<div class="hfade"></div></div>
    <div class="sh-body"><div class="sh-top"><div class="sh-poster">${art({ name: item.name, artwork: poster }, 'poster')}</div>
      <div class="sh-info"><span class="chip">${esc(sectionLabel(item.section))} · ${esc(item.group)}</span><h2>${esc(d?.title || item.name)}</h2>
        <div class="meta">${meta.map((m) => `<span>${esc(m)}</span>`).join('')}</div>
        ${d?.plot ? `<p class="plot">${esc(d.plot)}</p>` : d ? '' : '<div class="skl"></div><div class="skl s"></div>'}
        ${d?.err ? `<p class="fine err">${esc(d.err)}</p>` : ''}
        <div class="sh-act">${primary}<button class="btn lg" data-fav="${item.id}">${icon('heart', 18)} ${isFav(item) ? 'Favorilerde' : 'Favorilere ekle'}</button></div>
        ${d?.cast ? `<p class="fine"><b>Oyuncular:</b> ${esc(d.cast)}</p>` : ''}${d?.director ? `<p class="fine"><b>Yönetmen:</b> ${esc(d.director)}</p>` : ''}</div></div>
      ${series && eps.length ? `<div class="seasons">${seasons.map((s) => `<button class="${s === sh.season ? 'on' : ''}" data-season="${s}">Sezon ${s}</button>`).join('')}</div>
        <div class="eplist">${list.map((e) => { const h = data.history[e.id]; const pct = h?.duration ? clamp(h.position / h.duration * 100, 0, 100) : (data.eppos[e.id] && e.dur ? clamp(data.eppos[e.id] / e.dur * 100, 0, 100) : 0); const w = data.watched.has(e.id);
          return `<button class="ep ${w ? 'w' : ''}" data-ep="${e.id}"><span class="en">${e.episode}</span><span class="eart">${img(e.artwork || poster)}${pct ? `<i class="prog"><b style="width:${pct}%"></b></i>` : ''}</span><span class="et"><b>${esc(e.name)}</b><small>${e.dur ? Math.max(1, Math.round(e.dur / 60)) + ' dk' : ''}${w ? ' · İzlendi' : pct ? ' · Devam ediyor' : ''}</small>${e.plot ? `<em>${esc(e.plot)}</em>` : ''}</span><span class="epp">${w ? icon('check', 18) : icon('play', 18)}</span></button>`; }).join('')}</div>` : series && d ? '<p class="fine">Bölüm bulunamadı.</p>' : ''}</div></article>`;
}
function playMovie(item) { player.open({ item, queue: [item], resume: true, titleOf: (i) => i.name, subtitleOf: () => sectionLabel(item.section) + (item.group ? ' · ' + item.group : '') }); pushOv('player'); }
function playEpisode(parent, eps, ep, resume = true) {
  const queue = eps.map((e) => ({ ...e, seriesId: parent.id, seriesName: parent.name, seriesArtwork: parent.artwork, group: parent.group }));
  player.open({ item: queue.find((x) => x.id === ep.id), queue, resume, titleOf: (i) => `${parent.name}`, subtitleOf: (i) => `S${i.season} · B${i.episode} · ${i.name}`, favItem: () => parent });
  pushOv('player');
}
function playLive(item) {
  const full = (S.route === 'live' && S.browseList ? S.browseList() : cat.bySection.live); let list = full.some((x) => x.id === item.id) ? full : cat.bySection.live;
  if (list.length > 600) { const i = list.findIndex((x) => x.id === item.id); list = list.slice(Math.max(0, i - 300), i + 300); }
  player.open({ item, queue: list, resume: false, titleOf: (i) => i.name, subtitleOf: (i) => (i.num ? `${i.num} · ` : '') + i.group });
  pushOv('player');
}
async function openItem(item, resume) {
  if (item.section === 'live') return playLive(item);
  if (resume) {
    if (item.section === 'movies') return playMovie(item);
    // dizi: kaldığı bölümden devam
    const h = seriesProgress(item);
    try { const d = await loadDetails(S.src, item); const ep = d.episodes.find((e) => e.id === h?.id) || d.episodes[0]; if (ep) return playEpisode(item, d.episodes, ep, true); } catch (e) { toast('Bölümler alınamadı', e.message); }
  }
  openSheet(item);
}

// ---------------- hesap penceresi ----------------
function showDialog() {
  const s = S.src; let host = ''; try { host = new URL(s.base || s.url).host; } catch {}
  const w = $('#dlg') || document.body.appendChild(Object.assign(document.createElement('div'), { id: 'dlg', className: 'dlg-wrap' }));
  w.hidden = false;
  w.innerHTML = `<div class="dlg-back" data-act="closedlg"></div><div class="dlg" role="dialog" aria-modal="true"><h3>Hesap</h3><div class="dacc"><span class="av lg">${esc(initials(s.name))}</span><div><b>${esc(s.name)}</b><small>${esc(host)}</small></div></div>
    <div class="dsync"><span class="dot ${syncStatus() === 'Güncel' ? 'ok' : ''}"></span>Senkron: <b>${esc(syncStatus())}</b></div>
    <div class="dact"><button class="btn primary" data-act="refresh">${icon('refresh', 16)} Listeyi yenile</button><button class="btn" data-act="switch">Hesabı değiştir</button><button class="btn danger" data-act="logout">Çıkış yap</button><button class="btn ghost" data-act="closedlg">Kapat</button></div></div>`;
  pushOv('dialog');
}
function hideDialog() { const w = $('#dlg'); if (w) { w.hidden = true; w.innerHTML = ''; } }
function closeDialog() { if (!$('#dlg') || $('#dlg').hidden) return; hideDialog(); popOv('dialog'); }

// ---------------- olaylar ----------------
document.addEventListener('click', async (e) => {
  const t = e.target;
  const fav = t.closest('[data-fav]'); if (fav) { e.preventDefault(); e.stopPropagation(); const it = cat.byId.get(fav.dataset.fav); if (it) { const a = toggleFav(it); fav.classList.toggle('on', a); if (fav.classList.contains('btn')) fav.innerHTML = `${icon('heart', 18)} ${a ? 'Favorilerde' : 'Favorilere ekle'}`; toast(a ? 'Favorilere eklendi' : 'Favorilerden çıkarıldı', it.name); if (S.route === 'favorites') render(); } return; }
  const rm = t.closest('[data-rm]'); if (rm) { e.stopPropagation(); const it = cat.byId.get(rm.dataset.rm) || { id: rm.dataset.rm }; removeContinue(it); render(); toast('Listeden kaldırıldı'); return; }
  const ep = t.closest('[data-ep]'); if (ep && S.sheet) { const d = S.sheet.d; const e2 = d.episodes.find((x) => x.id === ep.dataset.ep); if (e2) playEpisode(S.sheet.item, d.episodes, e2, true); return; }
  const se = t.closest('[data-season]'); if (se && S.sheet) { S.sheet.season = +se.dataset.season; renderSheet(); return; }
  const op = t.closest('[data-open]'); if (op && !t.closest('[data-act]')) { const it = cat.byId.get(op.dataset.open) || (data.history[op.dataset.open]?.item); if (it) openItem(it, !!op.dataset.resume); return; }
  const a = t.closest('[data-act]'); if (!a) return;
  const act = a.dataset.act;
  if (act === 'account') showDialog();
  else if (act === 'closedlg') closeDialog();
  else if (act === 'closesheet') closeSheet();
  else if (act === 'catopen') $('#cats')?.classList.add('open');
  else if (act === 'catclose') $('#cats')?.classList.remove('open');
  else if (act === 'playmovie') playMovie(S.sheet.item);
  else if (act === 'playseries') { const { item, d } = S.sheet; const h = seriesProgress(item); const ep = d.episodes.find((x) => x.id === h?.id) || d.episodes[0]; if (ep) playEpisode(item, d.episodes, ep, true); }
  else if (act === 'syncnow') { toast('Senkron başlatıldı'); const r = await syncNow(); toast('Senkron', r); if ($('#sst')) $('#sst').textContent = r; }
  else if (act === 'refresh') { closeDialog(); toast('Liste yenileniyor…'); try { const ok = await refreshInBackground(S.src); toast(ok ? 'Liste yenilendi' : 'Liste boş döndü'); render(); } catch (er) { toast('Yenilenemedi', er.message); } }
  else if (act === 'switch') { closeDialog(); logout(); }
  else if (act === 'logout') { closeDialog(); logout(); }
  else if (act === 'clearhist' || act === 'clearfav') { if (a.dataset.c !== '1') { a.dataset.c = '1'; a.textContent = 'Emin misin? Tekrar dokun'; setTimeout(() => { a.dataset.c = '0'; a.textContent = act === 'clearhist' ? 'Geçmişi temizle' : 'Favorileri temizle'; }, 3000); return; } if (act === 'clearhist') { data.history = {}; data.eppos = {}; persist.hist(); persist.eppos(); } else { data.favorites.clear(); persist.fav(); } toast('Temizlendi'); render(); }
});
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && !player.isOpen()) { if ($('#dlg') && !$('#dlg').hidden) closeDialog(); else if (S.sheet) closeSheet(); else if ($('#cats.open')) $('#cats').classList.remove('open'); }
  if ((e.key === 'Enter' || e.key === ' ') && e.target.matches('.card[role=button],.chan-row')) { e.preventDefault(); e.target.click(); }
});
document.addEventListener('visibilitychange', () => { if (document.hidden) syncFlush(); else if (S.src) syncPull(20); });

boot();
