// MN IPTV Web — oynatıcı
import { $, $$, esc, fmtTime, mediaUrl, safeImage, epgGet, epgRequest, clamp } from './core.js';
import { data, recordProgress, resumePosition, persist } from './data.js';
import { icon } from './icons.js';

const HLS_URL = new URL('./hls.min.js', import.meta.url).href;
let hlsLoading = null;
function loadHls() {
  if (window.Hls) return Promise.resolve(window.Hls);
  return (hlsLoading ||= new Promise((res) => {
    const s = document.createElement('script'); s.src = HLS_URL; s.onload = () => res(window.Hls || null); s.onerror = () => res(null); document.head.appendChild(s);
  }));
}

export class Player {
  constructor({ getSource, onClose, onFav, isFav }) {
    this.getSource = getSource; this.onClose = onClose; this.onFav = onFav; this.isFav = isFav;
    this.q = []; this.i = -1; this.item = null; this.hls = null; this.hideT = null; this.numBuf = ''; this.numT = null; this.retry = 0; this.tapT = 0; this.dragging = false;
    this.build();
  }
  build() {
    const r = this.root = document.createElement('div');
    r.className = 'pl'; r.hidden = true; r.tabIndex = -1;
    r.innerHTML = `
      <div class="pl-stage">
        <video playsinline webkit-playsinline preload="auto"></video>
        <div class="pl-shade"></div>
        <div class="pl-top">
          <button class="pl-btn" data-a="close" aria-label="Kapat" title="Kapat (Esc)">${icon('back', 22)}</button>
          <div class="pl-titles"><div class="pl-sub"></div><div class="pl-title"></div></div>
          <button class="pl-btn" data-a="fav" aria-label="Favori" title="Favori">${icon('star', 20)}</button>
          <button class="pl-btn" data-a="side" aria-label="Liste" title="Liste (C)">${icon('list', 20)}</button>
          <button class="pl-btn" data-a="pip" aria-label="Resim içinde resim" title="Resim içinde resim">${icon('pip', 20)}</button>
        </div>
        <div class="pl-center"><div class="pl-spin" hidden></div><div class="pl-flash" hidden></div></div>
        <div class="pl-toast" hidden></div>
        <div class="pl-bottom">
          <div class="pl-seek"><div class="pl-buf"></div><div class="pl-played"></div><input type="range" min="0" max="1000" value="0" step="1" aria-label="İlerleme"><div class="pl-tip" hidden></div></div>
          <div class="pl-row">
            <button class="pl-btn" data-a="prev" aria-label="Önceki" title="Önceki (P)">${icon('prev', 20)}</button>
            <button class="pl-btn pl-play" data-a="play" aria-label="Oynat/Duraklat" title="Oynat/Duraklat (Boşluk)">${icon('play', 24)}</button>
            <button class="pl-btn" data-a="next" aria-label="Sonraki" title="Sonraki (N)">${icon('next', 20)}</button>
            <button class="pl-btn pl-vod" data-a="back10" aria-label="10 sn geri" title="10 sn geri (←)">${icon('back10', 22)}</button>
            <button class="pl-btn pl-vod" data-a="fwd10" aria-label="10 sn ileri" title="10 sn ileri (→)">${icon('fwd10', 22)}</button>
            <div class="pl-vol"><button class="pl-btn" data-a="mute" aria-label="Sessiz" title="Sessiz (M)">${icon('vol', 20)}</button><input type="range" min="0" max="1" step="0.02" value="1" aria-label="Ses"></div>
            <div class="pl-time"><span class="pl-cur">00:00</span><span class="pl-sep"> / </span><span class="pl-dur">00:00</span></div>
            <div class="pl-live" hidden><i></i>CANLI</div>
            <div class="pl-grow"></div>
            <button class="pl-btn" data-a="menu" aria-label="Ayarlar" title="Ses, altyazı, hız, kalite">${icon('gear', 20)}</button>
            <button class="pl-btn" data-a="fs" aria-label="Tam ekran" title="Tam ekran (F)">${icon('full', 20)}</button>
          </div>
        </div>
        <div class="pl-menu" hidden></div>
        <div class="pl-err" hidden><div><strong>Yayın açılamadı</strong><p></p><div class="pl-err-actions"><button class="btn primary" data-a="retry">Yeniden dene</button><button class="btn" data-a="close">Kapat</button></div></div></div>
        <div class="pl-next" hidden><div class="pl-next-body"><span>Sıradaki bölüm <b class="pl-cd"></b></span><strong></strong></div><button class="btn primary" data-a="nextnow">Şimdi oynat</button><button class="btn" data-a="nextcancel">İptal</button></div>
      </div>
      <aside class="pl-side"><div class="pl-side-head"><strong></strong><button class="pl-btn" data-a="side" aria-label="Kapat">${icon('close', 18)}</button></div><div class="pl-side-list"></div></aside>`;
    document.body.appendChild(r);
    const v = this.v = $('video', r);
    this.el = { sub: $('.pl-sub', r), title: $('.pl-title', r), spin: $('.pl-spin', r), flash: $('.pl-flash', r), toast: $('.pl-toast', r), seek: $('.pl-seek input', r), played: $('.pl-played', r), buf: $('.pl-buf', r), tip: $('.pl-tip', r), cur: $('.pl-cur', r), dur: $('.pl-dur', r), live: $('.pl-live', r), menu: $('.pl-menu', r), err: $('.pl-err', r), next: $('.pl-next', r), side: $('.pl-side', r), sideList: $('.pl-side-list', r), vol: $('.pl-vol input', r), play: $('.pl-play', r), fs: $('[data-a=fs]', r), mute: $('[data-a=mute]', r), fav: $('[data-a=fav]', r) };

    r.addEventListener('click', (e) => { const b = e.target.closest('[data-a]'); if (b) { e.stopPropagation(); this.act(b.dataset.a, b); return; } if (!e.target.closest('.pl-menu')) this.closeMenu(); });
    // dokunma/tıklama: tek -> kontrolleri aç/kapat, çift -> ±10 sn / tam ekran
    const stage = $('.pl-stage', r);
    stage.addEventListener('click', (e) => {
      if (e.target.closest('.pl-top,.pl-bottom,.pl-menu,.pl-err,.pl-next,.pl-side')) return;
      const now = Date.now(); const x = e.clientX - stage.getBoundingClientRect().left; const w = stage.clientWidth;
      if (now - this.tapT < 320) {
        clearTimeout(this.tapTimer); this.tapT = 0;
        if (this.isLive() || (x > w * 0.35 && x < w * 0.65)) this.toggleFs();
        else this.skip(x < w / 2 ? -10 : 10);
        return;
      }
      this.tapT = now;
      this.tapTimer = setTimeout(() => { if (matchMedia('(pointer:coarse)').matches) { if (this.controlsOn()) this.hideControls(true); else this.showControls(); } else this.togglePlay(); }, 250);
    });
    stage.addEventListener('mousemove', () => this.showControls());
    stage.addEventListener('touchstart', () => this.showControls(), { passive: true });

    // seek
    const sk = this.el.seek;
    sk.addEventListener('pointerdown', () => { this.dragging = true; });
    sk.addEventListener('input', () => { if (v.duration) { const t = sk.value / 1000 * v.duration; this.el.cur.textContent = fmtTime(t); this.el.played.style.width = sk.value / 10 + '%'; } });
    sk.addEventListener('change', () => { if (v.duration) v.currentTime = sk.value / 1000 * v.duration; this.dragging = false; });
    sk.addEventListener('pointerup', () => { this.dragging = false; });
    sk.addEventListener('pointermove', (e) => { const b = sk.getBoundingClientRect(); const p = clamp((e.clientX - b.left) / b.width, 0, 1); if (v.duration) { this.el.tip.hidden = false; this.el.tip.textContent = fmtTime(p * v.duration); this.el.tip.style.left = p * 100 + '%'; } });
    sk.addEventListener('pointerleave', () => { this.el.tip.hidden = true; });
    this.el.vol.addEventListener('input', () => { v.volume = +this.el.vol.value; v.muted = v.volume === 0; this.syncVol(); try { localStorage.setItem('mniptv:vol', String(v.volume)); } catch {} });
    try { const sv = parseFloat(localStorage.getItem('mniptv:vol')); if (sv >= 0 && sv <= 1) { v.volume = sv; this.el.vol.value = sv; } } catch {}

    v.addEventListener('play', () => { this.syncPlay(); this.showControls(); });
    v.addEventListener('pause', () => { this.syncPlay(); this.showControls(true); this.save(true); });
    v.addEventListener('waiting', () => { clearTimeout(this.spinT); this.spinT = setTimeout(() => (this.el.spin.hidden = false), 350); });
    v.addEventListener('playing', () => { clearTimeout(this.spinT); this.el.spin.hidden = true; this.el.err.hidden = true; this.retry = 0; });
    v.addEventListener('canplay', () => { clearTimeout(this.spinT); this.el.spin.hidden = true; });
    v.addEventListener('timeupdate', () => this.tick());
    v.addEventListener('progress', () => this.buffered());
    v.addEventListener('durationchange', () => this.tick());
    v.addEventListener('volumechange', () => this.syncVol());
    v.addEventListener('ended', () => this.ended());
    v.addEventListener('error', () => { if (v.error && this.item) this.fail(this.nativeMsg(v.error)); });
    document.addEventListener('fullscreenchange', () => this.syncFs());
    document.addEventListener('webkitfullscreenchange', () => this.syncFs());
    v.addEventListener('webkitendfullscreen', () => this.syncFs());
    window.addEventListener('keydown', (e) => this.key(e), true);
    document.addEventListener('visibilitychange', () => { if (document.hidden) this.save(true); });
    window.addEventListener('pagehide', () => this.save(true));
  }

  isLive() { return this.item?.section === 'live'; }
  controlsOn() { return this.root.classList.contains('show'); }
  showControls(keep) {
    this.root.classList.add('show'); clearTimeout(this.hideT);
    if (!keep && !this.v.paused) this.hideT = setTimeout(() => this.hideControls(), 3200);
  }
  hideControls(force) { if (force || (!this.v.paused && this.el.menu.hidden && !this.dragging)) { this.root.classList.remove('show'); this.closeMenu(); } }

  async open(ctx) {
    this.q = ctx.queue || [ctx.item]; this.i = Math.max(0, this.q.findIndex((x) => x.id === ctx.item.id)); this.ctx = ctx;
    this.root.hidden = false; this.root.focus({ preventScroll: true }); document.body.classList.add('pl-open');
    this.root.classList.toggle('has-side', false);
    await this.load(this.q[this.i], ctx.resume !== false);
  }
  isOpen() { return !this.root.hidden; }
  titleOf(it) { return this.ctx?.titleOf ? this.ctx.titleOf(it) : it.name; }
  subOf(it) { return this.ctx?.subtitleOf ? this.ctx.subtitleOf(it) : ''; }

  async load(item, resume = true) {
    this.save(true); this.destroyHls();
    this.item = item; this.retry = 0; this.resumeAt = resume && item.section !== 'live' ? resumePosition(item) : 0;
    const v = this.v, live = this.isLive();
    this.root.classList.toggle('live', live);
    this.root.classList.toggle('multi', this.q.length > 1);
    this.el.title.textContent = this.titleOf(item); this.el.sub.textContent = this.subOf(item);
    this.el.live.hidden = !live; this.el.err.hidden = true; this.el.next.hidden = true; clearInterval(this.cdT);
    this.el.cur.textContent = live ? '' : '00:00'; this.el.dur.textContent = '00:00'; this.el.seek.value = 0; this.el.played.style.width = '0'; this.el.buf.style.width = '0';
    this.el.spin.hidden = false; this.syncFav(); this.renderSide(); this.updateNav(); this.mediaSession();
    const src = this.getSource(); const url = mediaUrl(src, item);
    if (!url) return this.fail('Bu içerik için oynatma adresi bulunamadı.');
    v.removeAttribute('src'); v.load();
    const isHls = /\.m3u8(\?|$)/i.test(url) || String(item.ext).toLowerCase() === 'm3u8';
    const start = () => { if (this.resumeAt > 0) { try { v.currentTime = this.resumeAt; } catch {} } this.resumeAt = 0; v.play().catch(() => { this.el.spin.hidden = true; this.showControls(true); }); };
    if (isHls) {
      const Hls = await loadHls();
      if (this.item !== item) return;
      if (Hls && Hls.isSupported()) {
        const hls = this.hls = new Hls({ enableWorker: true, lowLatencyMode: false, backBufferLength: live ? 30 : 90, maxBufferLength: 40, manifestLoadingMaxRetry: 2, levelLoadingMaxRetry: 3, fragLoadingMaxRetry: 4 });
        hls.loadSource(url); hls.attachMedia(v);
        hls.on(Hls.Events.MANIFEST_PARSED, start);
        hls.on(Hls.Events.ERROR, (_, d) => {
          if (!d.fatal) return;
          if (d.type === Hls.ErrorTypes.NETWORK_ERROR && this.retry++ < 3) { setTimeout(() => this.hls?.startLoad(), 800 * this.retry); return; }
          if (d.type === Hls.ErrorTypes.MEDIA_ERROR && this.retry++ < 3) { this.hls?.recoverMediaError(); return; }
          this.fail(d.type === Hls.ErrorTypes.NETWORK_ERROR ? 'Yayına ulaşılamadı. Sunucu yanıt vermiyor ya da kanal şu an kapalı olabilir.' : 'Yayın biçimi tarayıcıda oynatılamadı.');
        });
        return;
      }
      if (v.canPlayType('application/vnd.apple.mpegurl')) { v.src = url; v.addEventListener('loadedmetadata', start, { once: true }); return; }
      return this.fail(Hls ? 'Bu tarayıcı HLS yayınlarını oynatamıyor.' : 'Oynatıcı bileşeni yüklenemedi. İnternet bağlantını kontrol edip tekrar dene.');
    }
    v.src = url; v.addEventListener('loadedmetadata', start, { once: true });
  }
  destroyHls() { if (this.hls) { try { this.hls.destroy(); } catch {} this.hls = null; } }
  nativeMsg(e) { return ({ 1: 'Oynatma iptal edildi.', 2: 'Ağ hatası: dosya alınamadı.', 3: 'Video çözülemedi (codec desteklenmiyor olabilir).', 4: 'Bu biçim tarayıcıda oynatılamıyor (MKV/AVI veya desteklenmeyen codec olabilir).' }[e.code] || 'Oynatma hatası.'); }
  fail(msg) { this.el.spin.hidden = true; this.el.err.hidden = false; $('p', this.el.err).textContent = msg; this.showControls(true); }

  // --- kuyruk
  updateNav() { const multi = this.q.length > 1; $('[data-a=prev]', this.root).disabled = !(multi && this.i > 0); $('[data-a=next]', this.root).disabled = !(multi && this.i < this.q.length - 1); $('[data-a=side]', this.root).style.display = multi ? '' : 'none'; }
  go(d) { const j = this.i + d; if (j < 0 || j >= this.q.length) return; this.i = j; this.load(this.q[j], true); if (this.isLive()) this.toast(`${this.q[j].num ? this.q[j].num + ' · ' : ''}${this.q[j].name}`); }
  renderSide() {
    if (this.q.length < 2) return;
    $('.pl-side-head strong', this.root).textContent = this.isLive() ? 'Kanallar' : 'Bölümler';
    const list = this.el.sideList; const live = this.isLive();
    if (list.dataset.sig !== `${this.q.length}:${this.q[0]?.id}:${live}`) {
      list.dataset.sig = `${this.q.length}:${this.q[0]?.id}:${live}`;
      const src = this.getSource();
      list.innerHTML = this.q.map((it, k) => {
        const img = safeImage(it.artwork);
        return live
          ? `<button class="sl" data-k="${k}"><span class="sl-logo">${img ? `<img loading="lazy" src="${esc(img)}" alt="" onerror="this.remove()">` : esc((it.name || '?')[0])}</span><span class="sl-t"><b>${esc(it.name)}</b><small data-epg="${esc(it.id)}"></small></span></button>`
          : `<button class="sl ep" data-k="${k}"><span class="sl-n">${it.episode || k + 1}</span><span class="sl-t"><b>${esc(it.name)}</b><small>Sezon ${it.season || 1}</small></span>${data.watched.has(it.id) ? icon('check', 16, 'sl-ok') : ''}</button>`;
      }).join('');
      list.onclick = (e) => { const b = e.target.closest('.sl'); if (!b) return; this.i = +b.dataset.k; this.load(this.q[this.i], true); if (matchMedia('(max-width:900px)').matches) this.root.classList.remove('has-side'); };
      if (live) {
        const io = this.io = new IntersectionObserver((es) => { for (const en of es) if (en.isIntersecting) { const it = this.q[+en.target.dataset.k]; epgRequest(src, it, (x) => this.paintEpg(x)); const e2 = epgGet(it); if (e2) this.paintEpg(it); io.unobserve(en.target); } }, { root: list, rootMargin: '200px' });
        $$('.sl', list).forEach((b) => io.observe(b));
      }
    }
    $$('.sl.active', list).forEach((b) => b.classList.remove('active'));
    const cur = $(`.sl[data-k="${this.i}"]`, list); if (cur) { cur.classList.add('active'); cur.scrollIntoView({ block: 'center' }); }
  }
  paintEpg(it) { const s = $(`[data-epg="${CSS.escape(it.id)}"]`, this.el.sideList); const e = epgGet(it); if (s) s.textContent = e?.now?.title || ''; }

  // --- eylemler
  act(a) {
    const v = this.v;
    switch (a) {
      case 'close': this.close(); break;
      case 'play': this.togglePlay(); break;
      case 'prev': this.go(-1); break;
      case 'next': this.go(1); break;
      case 'nextnow': this.go(1); break;
      case 'nextcancel': this.el.next.hidden = true; clearInterval(this.cdT); break;
      case 'back10': this.skip(-10); break;
      case 'fwd10': this.skip(10); break;
      case 'mute': v.muted = !v.muted; break;
      case 'fs': this.toggleFs(); break;
      case 'pip': this.pip(); break;
      case 'side': this.root.classList.toggle('has-side'); this.showControls(true); break;
      case 'menu': this.toggleMenu(); break;
      case 'fav': if (this.onFav) { this.onFav(this.favTarget()); this.syncFav(); } break;
      case 'retry': this.retry = 0; this.load(this.item, true); break;
    }
  }
  favTarget() { return this.ctx?.favItem ? this.ctx.favItem(this.item) : this.item; }
  syncFav() { const t = this.favTarget(); const on = t && this.isFav(t); this.el.fav.classList.toggle('on', !!on); this.el.fav.innerHTML = icon('star', 20).replace('fill="none"', on ? 'fill="currentColor"' : 'fill="none"'); }
  togglePlay() { const v = this.v; if (v.paused) v.play().catch(() => {}); else v.pause(); this.flash(v.paused ? 'pause' : 'play'); }
  skip(s) { const v = this.v; if (this.isLive() || !Number.isFinite(v.duration)) return; v.currentTime = clamp(v.currentTime + s, 0, v.duration); this.flash(s < 0 ? 'back10' : 'fwd10', s < 0 ? '-10 sn' : '+10 sn'); this.showControls(); }
  flash(ic, text) { const f = this.el.flash; f.innerHTML = icon(ic, 34) + (text ? `<span>${text}</span>` : ''); f.hidden = false; f.classList.remove('go'); void f.offsetWidth; f.classList.add('go'); clearTimeout(this.flT); this.flT = setTimeout(() => (f.hidden = true), 700); }
  toast(t) { const e = this.el.toast; e.textContent = t; e.hidden = false; clearTimeout(this.toT); this.toT = setTimeout(() => (e.hidden = true), 1800); }
  syncPlay() { this.el.play.innerHTML = icon(this.v.paused ? 'play' : 'pause', 24); this.root.classList.toggle('paused', this.v.paused); }
  syncVol() { const v = this.v; this.el.mute.innerHTML = icon(v.muted || v.volume === 0 ? 'mute' : 'vol', 20); this.el.vol.value = v.muted ? 0 : v.volume; }
  fsEl() { return document.fullscreenElement || document.webkitFullscreenElement; }
  toggleFs() {
    const r = this.root, v = this.v;
    if (this.fsEl()) { (document.exitFullscreen || document.webkitExitFullscreen).call(document); return; }
    const req = r.requestFullscreen || r.webkitRequestFullscreen;
    if (req) { try { const p = req.call(r); p?.catch?.(() => this.iosFs()); } catch { this.iosFs(); } } else this.iosFs();
  }
  iosFs() { const v = this.v; if (v.webkitEnterFullscreen) { try { v.webkitEnterFullscreen(); } catch {} } else this.toast('Tam ekran bu tarayıcıda desteklenmiyor'); }
  syncFs() { const on = !!this.fsEl(); this.root.classList.toggle('fs', on); this.el.fs.innerHTML = icon(on ? 'exit' : 'full', 20); }
  async pip() { const v = this.v; try { if (document.pictureInPictureElement) await document.exitPictureInPicture(); else if (v.requestPictureInPicture) await v.requestPictureInPicture(); else if (v.webkitSupportsPresentationMode?.('picture-in-picture')) v.webkitSetPresentationMode(v.webkitPresentationMode === 'picture-in-picture' ? 'inline' : 'picture-in-picture'); else this.toast('Bu tarayıcıda PiP desteklenmiyor'); } catch { this.toast('PiP açılamadı'); } }

  // --- ayar menüsü
  closeMenu() { this.el.menu.hidden = true; }
  toggleMenu() { if (!this.el.menu.hidden) return this.closeMenu(); this.menuView('main'); this.el.menu.hidden = false; this.showControls(true); }
  menuView(view) {
    const m = this.el.menu, v = this.v, h = this.hls; let html = '';
    const row = (a, label, val, on) => `<button class="mi ${on ? 'on' : ''}" data-m="${a}"><span>${label}</span><em>${val ?? ''}</em></button>`;
    if (view === 'main') {
      const speed = v.playbackRate === 1 ? 'Normal' : v.playbackRate + '×';
      const lv = h && h.levels?.length > 1 ? (h.currentLevel === -1 || h.autoLevelEnabled ? 'Otomatik' : (h.levels[h.currentLevel]?.height ? h.levels[h.currentLevel].height + 'p' : '')) : '';
      html = (this.isLive() ? '' : row('speed', 'Oynatma hızı', speed)) + (h && h.levels?.length > 1 ? row('quality', 'Kalite', lv) : '') + row('audio', 'Ses', this.audioLabel()) + row('subs', 'Altyazı', this.subLabel()) + (!this.isLive() ? row('autonext', 'Sonraki bölümü otomatik oynat', data.settings.autoNext ? 'Açık' : 'Kapalı') : '') + `<button class="mi" data-m="shortcuts"><span>Klavye kısayolları</span><em>?</em></button>`;
    } else if (view === 'speed') html = `<div class="mh"><button data-m="main">${icon('back', 16)}</button>Hız</div>` + [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2].map((s) => row('setspeed:' + s, s === 1 ? 'Normal' : s + '×', '', v.playbackRate === s)).join('');
    else if (view === 'quality') html = `<div class="mh"><button data-m="main">${icon('back', 16)}</button>Kalite</div>` + row('setlevel:-1', 'Otomatik', '', h.autoLevelEnabled) + [...h.levels].map((l, i) => ({ l, i })).sort((a, b) => (b.l.height || 0) - (a.l.height || 0)).map(({ l, i }) => row('setlevel:' + i, (l.height ? l.height + 'p' : Math.round(l.bitrate / 1000) + ' kb/s'), '', !h.autoLevelEnabled && h.currentLevel === i)).join('');
    else if (view === 'audio') { const t = this.audioTracks(); html = `<div class="mh"><button data-m="main">${icon('back', 16)}</button>Ses</div>` + (t.length ? t.map((x, i) => row('setaudio:' + i, x.label, '', x.on)).join('') : '<div class="mn">Ek ses parçası yok</div>'); }
    else if (view === 'subs') { const t = this.subTracks(); html = `<div class="mh"><button data-m="main">${icon('back', 16)}</button>Altyazı</div>` + row('setsub:-1', 'Kapalı', '', !t.some((x) => x.on)) + t.map((x, i) => row('setsub:' + i, x.label, '', x.on)).join('') + (t.length ? '' : '<div class="mn">Altyazı bulunamadı</div>'); }
    else if (view === 'shortcuts') html = `<div class="mh"><button data-m="main">${icon('back', 16)}</button>Kısayollar</div><div class="mn sc">Boşluk/K oynat · ←/→ 10 sn · ↑/↓ ses · M sessiz · F tam ekran · C liste · N/P sonraki/önceki · 0-9 kanal no · [ ] hız · Esc kapat</div>`;
    m.innerHTML = html;
    m.onclick = (e) => {
      const b = e.target.closest('[data-m]'); if (!b) return; e.stopPropagation(); const [a, arg] = b.dataset.m.split(':');
      if (['main', 'speed', 'quality', 'audio', 'subs', 'shortcuts'].includes(a)) return this.menuView(a);
      if (a === 'setspeed') v.playbackRate = +arg;
      if (a === 'setlevel') { h.currentLevel = +arg; }
      if (a === 'setaudio') this.setAudio(+arg);
      if (a === 'setsub') this.setSub(+arg);
      if (a === 'autonext') { data.settings.autoNext = !data.settings.autoNext; persist.settings(); return this.menuView('main'); }
      this.closeMenu();
    };
  }
  audioTracks() { const h = this.hls, v = this.v; if (h?.audioTracks?.length > 1) return h.audioTracks.map((t, i) => ({ label: t.name || t.lang || `Ses ${i + 1}`, on: h.audioTrack === i })); if (v.audioTracks?.length > 1) return [...v.audioTracks].map((t, i) => ({ label: t.label || t.language || `Ses ${i + 1}`, on: t.enabled })); return []; }
  setAudio(i) { const h = this.hls, v = this.v; if (h?.audioTracks?.length) h.audioTrack = i; else if (v.audioTracks) [...v.audioTracks].forEach((t, j) => (t.enabled = j === i)); }
  subTracks() { const h = this.hls, v = this.v; if (h?.subtitleTracks?.length) return h.subtitleTracks.map((t, i) => ({ label: t.name || t.lang || `Altyazı ${i + 1}`, on: h.subtitleTrack === i && h.subtitleDisplay })); return [...v.textTracks].filter((t) => t.kind === 'subtitles' || t.kind === 'captions').map((t, i) => ({ label: t.label || t.language || `Altyazı ${i + 1}`, on: t.mode === 'showing' })); }
  setSub(i) { const h = this.hls, v = this.v; if (h?.subtitleTracks?.length) { if (i < 0) h.subtitleDisplay = false; else { h.subtitleTrack = i; h.subtitleDisplay = true; } } else { const ts = [...v.textTracks].filter((t) => t.kind === 'subtitles' || t.kind === 'captions'); ts.forEach((t, j) => (t.mode = j === i ? 'showing' : 'disabled')); } }
  audioLabel() { const t = this.audioTracks(); return t.find((x) => x.on)?.label || (t.length ? '' : 'Varsayılan'); }
  subLabel() { const t = this.subTracks(); return t.find((x) => x.on)?.label || 'Kapalı'; }

  // --- ilerleme
  tick() {
    const v = this.v, live = this.isLive();
    if (live || !Number.isFinite(v.duration) || !v.duration) { this.save(false); return; }
    if (!this.dragging) { this.el.seek.value = Math.round(v.currentTime / v.duration * 1000); this.el.played.style.width = v.currentTime / v.duration * 100 + '%'; this.el.cur.textContent = fmtTime(v.currentTime); }
    this.el.dur.textContent = fmtTime(v.duration); this.save(false);
  }
  buffered() { const v = this.v; if (!v.duration || !v.buffered.length) return; let end = 0; for (let i = 0; i < v.buffered.length; i++) if (v.buffered.start(i) <= v.currentTime + 1) end = Math.max(end, v.buffered.end(i)); this.el.buf.style.width = end / v.duration * 100 + '%'; }
  save(force) { if (this.item && !this.isLive()) recordProgress(this.item, this.v.currentTime, this.v.duration, force); }
  ended() {
    this.save(true);
    if (this.i < this.q.length - 1 && !this.isLive()) {
      if (!data.settings.autoNext) { this.showControls(true); return; }
      const n = this.q[this.i + 1]; $('.pl-next strong', this.root).textContent = this.titleOf(n); this.el.next.hidden = false; let c = 8; $('.pl-cd', this.root).textContent = `(${c})`;
      clearInterval(this.cdT); this.cdT = setInterval(() => { c--; $('.pl-cd', this.root).textContent = `(${c})`; if (c <= 0) { clearInterval(this.cdT); this.go(1); } }, 1000);
    } else this.showControls(true);
  }
  mediaSession() {
    if (!('mediaSession' in navigator) || !this.item) return;
    try {
      navigator.mediaSession.metadata = new MediaMetadata({ title: this.titleOf(this.item), artist: this.subOf(this.item) || 'MN IPTV', artwork: safeImage(this.item.artwork) ? [{ src: safeImage(this.item.artwork), sizes: '512x512' }] : [] });
      navigator.mediaSession.setActionHandler('play', () => this.v.play()); navigator.mediaSession.setActionHandler('pause', () => this.v.pause());
      navigator.mediaSession.setActionHandler('previoustrack', this.i > 0 ? () => this.go(-1) : null); navigator.mediaSession.setActionHandler('nexttrack', this.i < this.q.length - 1 ? () => this.go(1) : null);
      navigator.mediaSession.setActionHandler('seekbackward', this.isLive() ? null : () => this.skip(-10)); navigator.mediaSession.setActionHandler('seekforward', this.isLive() ? null : () => this.skip(10));
    } catch {}
  }

  key(e) {
    if (this.root.hidden || e.target.matches('input[type=text],input[type=search],textarea')) return;
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    const k = e.key, v = this.v; let used = true;
    if (k === ' ' || k === 'k' || k === 'K') this.togglePlay();
    else if (k === 'ArrowRight') this.skip(e.shiftKey ? 30 : 10); else if (k === 'ArrowLeft') this.skip(e.shiftKey ? -30 : -10);
    else if (k === 'ArrowUp') { v.volume = clamp(v.volume + 0.05, 0, 1); v.muted = false; this.toast(`Ses %${Math.round(v.volume * 100)}`); }
    else if (k === 'ArrowDown') { v.volume = clamp(v.volume - 0.05, 0, 1); this.toast(`Ses %${Math.round(v.volume * 100)}`); }
    else if (k === 'm' || k === 'M') v.muted = !v.muted;
    else if (k === 'f' || k === 'F') this.toggleFs();
    else if (k === 'c' || k === 'C') this.act('side');
    else if (k === 'n' || k === 'N' || k === 'PageDown') this.go(1);
    else if (k === 'p' || k === 'P' || k === 'PageUp') this.go(-1);
    else if (k === '[' ) { v.playbackRate = Math.max(0.25, v.playbackRate - 0.25); this.toast(v.playbackRate + '×'); }
    else if (k === ']') { v.playbackRate = Math.min(3, v.playbackRate + 0.25); this.toast(v.playbackRate + '×'); }
    else if (k === '?') this.toggleMenu();
    else if (k === 'Escape') { if (this.fsEl()) return; if (!this.el.menu.hidden) this.closeMenu(); else this.close(); }
    else if (/^[0-9]$/.test(k) && this.isLive()) { this.numBuf += k; this.toast(this.numBuf); clearTimeout(this.numT); this.numT = setTimeout(() => { const n = +this.numBuf; this.numBuf = ''; const j = this.q.findIndex((x) => x.num === n); if (j >= 0) { this.i = j; this.load(this.q[j], false); this.toast(`${n} · ${this.q[j].name}`); } else this.toast('Kanal bulunamadı'); }, 1100); }
    else used = false;
    if (used) { e.preventDefault(); e.stopPropagation(); this.showControls(); }
  }

  close(silent) {
    if (this.root.hidden) return;
    this.save(true); clearInterval(this.cdT); clearTimeout(this.tapTimer);
    if (this.fsEl()) { try { (document.exitFullscreen || document.webkitExitFullscreen).call(document); } catch {} }
    if (document.pictureInPictureElement) document.exitPictureInPicture().catch(() => {});
    const v = this.v; try { v.pause(); } catch {} this.destroyHls(); v.removeAttribute('src'); v.load();
    this.root.hidden = true; this.root.classList.remove('show', 'has-side'); document.body.classList.remove('pl-open'); this.item = null; this.el.sideList.dataset.sig = ''; this.io?.disconnect();
    if (!silent) this.onClose?.();
  }
}
