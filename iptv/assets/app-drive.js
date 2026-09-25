(() => {
  'use strict';

  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
  const app = $('#app');
  const toastRegion = $('#toastRegion');
  const modalLayer = $('#modalLayer');
  const modal = $('#modal');
  const contextLayer = $('#contextLayer');
  const contextMenu = $('#contextMenu');

  const playerEls = {
    layer: $('#playerLayer'), player: $('#player'), video: $('#video'), controls: $('#playerControls'),
    title: $('#playerTitle'), eyebrow: $('#playerEyebrow'), close: $('#playerClose'), play: $('#playPauseButton'),
    rewind: $('#rewindButton'), forward: $('#forwardButton'), prev: $('#prevEpisodeButton'), next: $('#nextEpisodeButton'),
    seek: $('#seekBar'), current: $('#currentTime'), duration: $('#durationTime'), live: $('#liveBadge'),
    volume: $('#volumeBar'), mute: $('#muteButton'), fullscreen: $('#fullscreenButton'), pip: $('#pipButton'),
    audio: $('#audioButton'), subtitle: $('#subtitleButton'), loading: $('#playerLoading'), error: $('#playerError'),
    errorText: $('#playerErrorText'), retry: $('#retryPlayerButton'), trackMenu: $('#trackMenu'), nextUp: $('#nextUp'),
    nextUpTitle: $('#nextUpTitle'), nextUpButton: $('#nextUpButton')
  };

  if (location.hostname === 'mavinokta.pro' && location.protocol === 'https:' && location.pathname.startsWith('/iptv')) {
    location.replace(`http://${location.host}${location.pathname}${location.search}${location.hash}`);
    return;
  }

  const state = {
    connected: false,
    offlineMode: false,
    source: null,
    sourceKey: '',
    catalog: [],
    itemById: new Map(),
    categories: { live: [], movies: [], series: [] },
    favorites: new Set(),
    history: {},
    downloads: {},
    settings: { autoPiP: true },
    route: 'home',
    query: '',
    category: 'all',
    loading: false,
    epg: new Map(),
    detailsCache: new Map(),
    seriesEpisodes: new Map(),
    currentItem: null,
    currentEpisodes: [],
    currentEpisodeIndex: -1,
    hls: null,
    controlsTimer: null,
    progressTimer: 0,
    seekDragging: false,
    downloadJobs: new Map(),
    sourceMode: 'xtream',
    longPress: null,
    pageLimit: window.innerWidth <= 700 ? 40 : 72,
    searchDebounce: null,
    epgLoaded: false,
    epgLoading: false,
    epgSchedule: null,
  };

  const storage = {
    key(name) { return `mniptv:${state.sourceKey || 'global'}:${name}`; },
    load(name, fallback) {
      try { const v = localStorage.getItem(this.key(name)); return v ? JSON.parse(v) : fallback; } catch { return fallback; }
    },
    save(name, value) {
      try { localStorage.setItem(this.key(name), JSON.stringify(value)); } catch { toast('Depolama hatası', 'Tarayıcı yerel veriyi kaydedemedi.'); }
    },
    setGlobal(name, value) { try { localStorage.setItem(`mniptv:global:${name}`, JSON.stringify(value)); } catch {} },
    getGlobal(name, fallback = null) { try { const v = localStorage.getItem(`mniptv:global:${name}`); return v ? JSON.parse(v) : fallback; } catch { return fallback; } },
  };

  function esc(v = '') { return String(v).replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c])); }
  function safeImage(url) {
    if (!url) return '';
    try {
      const u = new URL(url, location.href);
      return ['http:', 'https:'].includes(u.protocol) ? providerProxyUrl(u.toString()) : '';
    } catch { return ''; }
  }
  function clamp(n, a, b) { return Math.min(b, Math.max(a, n)); }
  function fmtTime(seconds) {
    if (!Number.isFinite(seconds) || seconds < 0) return '00:00';
    const s = Math.floor(seconds % 60), m = Math.floor((seconds / 60) % 60), h = Math.floor(seconds / 3600);
    return h > 0 ? `${h}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}` : `${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
  }
  function fmtBytes(bytes) {
    if (!Number.isFinite(bytes) || bytes <= 0) return '—';
    const units = ['B','KB','MB','GB','TB']; let i=0, n=bytes;
    while (n >= 1024 && i < units.length-1) { n/=1024; i++; }
    return `${n >= 10 || i === 0 ? n.toFixed(0) : n.toFixed(1)} ${units[i]}`;
  }
  function normalized(text) { return String(text || '').toLocaleLowerCase('tr-TR').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/ı/g,'i'); }
  function sectionLabel(section) { return section === 'live' ? 'Canlı TV' : section === 'movies' ? 'Filmler' : 'Diziler'; }
  function iconFor(section) { return section === 'live' ? '▣' : section === 'movies' ? '◆' : '▤'; }
  function isOnline() { return navigator.onLine !== false; }
  function pageStep() { return window.innerWidth <= 700 ? 40 : 72; }
  function resetPageLimit() { state.pageLimit = pageStep(); }

  const STATIC_GATEWAY_VERSION = '3.2.1-web-rev31';
  const PRIVATE_SESSION_KEY = 'mniptv:provider-session:v3';
  try { sessionStorage.removeItem('mniptv:provider-session:v2'); } catch {}

  function appError(message, status = 400, code = 'request_failed') {
    const e = new Error(message); e.status = status; e.code = code; return e;
  }

  function stableId(value) {
    // Deterministic FNV-1a based ID. It keeps favorites/history stable without storing credentials in the ID.
    let h = 0x811c9dc5;
    for (const ch of String(value || '')) {
      h ^= ch.codePointAt(0);
      h = Math.imul(h, 0x01000193) >>> 0;
    }
    return `mn${h.toString(16).padStart(8,'0')}`;
  }

  function privateSource() {
    try { return JSON.parse(sessionStorage.getItem(PRIVATE_SESSION_KEY) || 'null'); } catch { return null; }
  }
  function savePrivateSource(source) {
    if (source) sessionStorage.setItem(PRIVATE_SESSION_KEY, JSON.stringify(source));
    else sessionStorage.removeItem(PRIVATE_SESSION_KEY);
  }
  function publicSource(source) {
    if (!source) return null;
    let server = 'Bağlı IPTV kaynağı';
    try {
      if (source.type === 'xtream' && source.base) server = new URL(source.base).toString().replace(/\/$/, '');
      else if (source.type === 'm3u' && source.url) server = new URL(source.url).origin;
    } catch {}
    return { type:source.type, name:source.name || 'IPTV', key:source.key, server };
  }

  function normalizeProviderServer(raw) {
    let value = String(raw || '').trim();
    if (!value) throw appError('Sunucu adresi gerekli.', 400, 'missing_server');
    if (!/^[a-z][a-z0-9+.-]*:\/\//i.test(value)) value = `http://${value}`;
    let u;
    try { u = new URL(value); }
    catch { throw appError('Sunucu adresi geçersiz.', 400, 'invalid_server'); }
    if (!['http:', 'https:'].includes(u.protocol) || !u.hostname) {
      throw appError('Sunucu adresi HTTP veya HTTPS olmalı.', 400, 'invalid_server');
    }
    u.username = '';
    u.password = '';
    u.search = '';
    u.hash = '';
    u.pathname = u.pathname.replace(/(?:player_api|xmltv|get)\.php\/?$/i, '');
    if (!u.pathname.endsWith('/')) u.pathname += '/';
    return u.toString();
  }

  function providerProxyUrl(remoteUrl) {
    const u = remoteUrl instanceof URL ? new URL(remoteUrl.toString()) : new URL(String(remoteUrl || ''));
    if (!['http:', 'https:'].includes(u.protocol)) {
      throw appError('Desteklenmeyen bağlantı türü.', 400, 'unsupported_remote');
    }
    if (location.protocol === 'https:' && u.protocol === 'http:') {
      throw appError('Bu IPTV kaynağı HTTP kullanıyor. Tarayıcı engelini aşmak için siteyi http://mavinokta.pro adresinden aç.', 400, 'mixed_content');
    }
    return u.toString();
  }

  function providerApiUrl(source, action = '', params = {}) {
    const u = new URL('player_api.php', source.base);
    u.searchParams.set('username', source.username);
    u.searchParams.set('password', source.password);
    if (action) u.searchParams.set('action', action);
    for (const [k,v] of Object.entries(params)) u.searchParams.set(k, String(v));
    return providerProxyUrl(u.toString());
  }

  function providerXmltvUrl(source) {
    const u = new URL('xmltv.php', source.base);
    u.searchParams.set('username', source.username);
    u.searchParams.set('password', source.password);
    return providerProxyUrl(u.toString());
  }

  async function providerFetch(url, kind = 'json') {
    let response;
    try {
      response = await fetch(url, { method:'GET', cache:'no-store', credentials:'omit', redirect:'follow', headers:{'Accept':kind==='json'?'application/json,text/plain,*/*':'*/*'} });
    } catch {
      throw appError('IPTV sunucusuna tarayıcıdan ulaşılamadı. Adres/port yanlış olabilir veya sağlayıcı web bağlantısını (CORS) engelliyor olabilir.', 502, 'upstream_unreachable');
    }
    if (!response.ok) {
      const status = response.status;
      if (status === 403) throw appError('IPTV sunucusu bağlantıyı reddetti (HTTP 403).', 502, 'upstream_forbidden');
      throw appError(`IPTV sunucusu HTTP ${status} döndürdü.`, 502, 'upstream_http');
    }
    if (kind === 'text') return response.text();
    const text = await response.text();
    try { return JSON.parse(text); } catch { throw appError('IPTV sunucusu geçerli veri döndürmedi.', 502, 'invalid_json'); }
  }

  function validateAccount(root) {
    const info = root && typeof root === 'object' ? root.user_info : null;
    if (!info || typeof info !== 'object') throw appError('Sunucu Xtream API yanıtı vermedi.', 400, 'not_xtream');
    if (String(info.auth ?? '') !== '1') throw appError('Giriş bilgileri doğrulanamadı.', 401, 'unauthorized');
    const status = String(info.status || '').toLowerCase();
    if (['banned','disabled','expired'].includes(status)) throw appError('Bu IPTV hesabı aktif değil.', 401, 'inactive_account');
    const exp = Number(info.exp_date || 0);
    if (exp > 0 && exp * 1000 < Date.now()) throw appError('IPTV hesabının süresi dolmuş.', 401, 'expired');
  }

  async function browserSession(method, payload = null) {
    if (method === 'GET') {
      const source = privateSource();
      return { ok:true, connected:!!source, ...(source ? {source:publicSource(source)} : {}) };
    }
    if (method === 'DELETE') {
      savePrivateSource(null); return {ok:true};
    }
    if (method !== 'POST') throw appError('Desteklenmeyen istek.', 405, 'method_not_allowed');

    const data = payload || {};
    const type = data.type || 'xtream';
    const name = String(data.name || '').trim() || 'IPTV';

    if (type === 'xtream') {
      const base = normalizeProviderServer(data.server || '');
      const username = String(data.username || '').trim();
      const password = String(data.password || '');
      if (!username || !password) throw appError('Kullanıcı adı ve şifre gerekli.', 400, 'missing_credentials');
      const temp = {type:'xtream',name,base,username,password};
      const root = await providerFetch(providerApiUrl(temp), 'json');
      validateAccount(root);
      const formats = Array.isArray(root?.user_info?.allowed_output_formats)
        ? root.user_info.allowed_output_formats.map(v=>String(v).toLowerCase())
        : [];
      const liveExt = formats.includes('m3u8') ? 'm3u8' : formats.includes('ts') ? 'ts' : 'm3u8';
      const accountHost = new URL(base).hostname.toLowerCase();
      const source = {...temp,liveExt,key:stableId(`${accountHost}|${username}`)};
      try { source.epgUrl = providerXmltvUrl(source); } catch {}
      savePrivateSource(source);
      return {ok:true,source:publicSource(source)};
    }

    if (type === 'm3u') {
      let remote; try { remote = new URL(String(data.url || '').trim()); } catch { throw appError('M3U bağlantısı geçersiz.',400,'invalid_m3u_url'); }
      const fetchUrl = providerProxyUrl(remote.toString());
      const text = await providerFetch(fetchUrl,'text');
      if (!text.replace(/^\uFEFF/, '').trimStart().slice(0,256).startsWith('#EXTM3U')) throw appError('Bağlantı geçerli bir M3U listesi değil.',400,'invalid_m3u');
      const source = {type:'m3u',name,url:remote.toString(),key:stableId(remote.toString())};
      savePrivateSource(source);
      return {ok:true,source:publicSource(source)};
    }
    throw appError('Desteklenmeyen kaynak türü.',400,'invalid_source');
  }

  function metaValue(key, line) {
    const q = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const m = String(line).match(new RegExp('(?:^|\\s)' + q + '\\s*=\\s*(?:"([^"]*)"|\\\'([^\\\']*)\\\')', 'i'));
    return m ? String(m[1] || m[2] || '').trim() : '';
  }
  function classifyM3U(url, group, name, meta) {
    for (const key of ['stream-type','content-type','tvg-type','type']) {
      const v = metaValue(key, meta).toLowerCase();
      if (['live','tv'].includes(v)) return 'live';
      if (['movie','movies','vod','film'].includes(v)) return 'movies';
      if (['series','episode','show'].includes(v)) return 'series';
    }
    const path = new URL(url).pathname.toLowerCase().split('/').filter(Boolean);
    if (path.includes('live')) return 'live';
    if (path.includes('movie')) return 'movies';
    if (path.includes('series')) return 'series';
    const hay = `${group} ${name}`.toLocaleLowerCase('tr-TR');
    if (/\b(dizi|series|sezon|season|episode|bölüm|bolum)\b/u.test(hay) || /\bS\d{1,2}\s*E\d{1,3}\b/i.test(name)) return 'series';
    const ext = new URL(url).pathname.split('.').pop().toLowerCase();
    return ['mp4','mkv','avi','mov','m4v','webm'].includes(ext) ? 'movies' : 'live';
  }
  function parseM3UCatalog(text, source) {
    const lines = text.replace(/^\uFEFF/, '').split(/\r\n|\r|\n/);
    let epgUrl = '';
    if (lines[0]) for (const key of ['url-tvg','x-tvg-url','tvg-url']) { const v=metaValue(key,lines[0]); if(v){ epgUrl=v; break; } }
    const items=[]; let name='',group='Diğer',meta='';
    for (const raw of lines) {
      const line=raw.trim();
      if (line.startsWith('#EXTINF:')) {
        meta=line; group=metaValue('group-title',line)||'Diğer'; let quoted=false,sep=-1;
        for(let i=0;i<line.length;i++){if(line[i]==='"')quoted=!quoted;else if(line[i]===','&&!quoted){sep=i;break;}}
        name=sep>=0?line.slice(sep+1).trim():metaValue('tvg-name',line);
      } else if (line.startsWith('#EXTGRP:')) group=line.slice(8).trim()||'Diğer';
      else if (line && !line.startsWith('#')) {
        let remote; try { remote=new URL(line); providerProxyUrl(remote.toString()); } catch { name='';group='Diğer';meta='';continue; }
        const section=classifyM3U(remote.toString(),group,name,meta); const path=remote.pathname; const ext=(path.includes('.')?path.split('.').pop():'')||(section==='live'?'m3u8':'mp4');
        items.push({id:stableId(`${source.key}|${remote}`),name:name||`İçerik ${items.length+1}`,group,section,artwork:metaValue('tvg-logo',meta)||null,providerId:null,tvgId:metaValue('tvg-id',meta)||null,added:null,ext:section==='live'?'m3u8':ext,directUrl:remote.toString(),seriesContainer:false});
        name='';group='Diğer';meta='';
      }
    }
    return {items,epgUrl};
  }

  async function browserCatalog() {
    const source = privateSource();
    if (!source) throw appError('Oturum bulunamadı. Kaynağı yeniden bağla.',401,'no_session');
    if (source.type === 'm3u') {
      const text = await providerFetch(providerProxyUrl(source.url),'text');
      const parsed = parseM3UCatalog(text,source); source.epgUrl=parsed.epgUrl||source.epgUrl; savePrivateSource(source);
      return {ok:true,items:parsed.items,sourceKey:source.key,epg:!!source.epgUrl};
    }
    const order=['get_live_streams','get_live_categories','get_vod_streams','get_vod_categories','get_series','get_series_categories'];
    const responses={},warnings=[];
    for(let i=0;i<order.length;i++){
      const action=order[i];
      try { const data=await providerFetch(providerApiUrl(source,action),'json'); responses[action]=Array.isArray(data)?data:[]; }
      catch(e){ responses[action]=[]; warnings.push(`${action}: ${e.message}`); }
      if(i<order.length-1) await new Promise(r=>setTimeout(r,160));
    }
    const items=[];
    for(const [section,catKey,contentKey] of [['live','get_live_categories','get_live_streams'],['movies','get_vod_categories','get_vod_streams'],['series','get_series_categories','get_series']]){
      const names=new Map();
      for(const row of responses[catKey]||[]) if(row&&typeof row==='object') names.set(String(row.category_id??''),String(row.category_name||'').trim()||'Diğer');
      for(const row of responses[contentKey]||[]){
        if(!row||typeof row!=='object')continue;
        const pid=String(row[section==='series'?'series_id':'stream_id']??'').trim(); if(!pid)continue;
        const category=String(row.category_id??''); const art=String(row[section==='series'?'cover':'stream_icon']||'').trim()||null; const addedRaw=String(row[section==='series'?'last_modified':'added']??'').trim();
        items.push({id:stableId(`${source.key}|${section}|${pid}`),name:String(row.name||pid),group:names.get(category)||(category?`Kategori ${category}`:'Diğer'),section,artwork:art,providerId:pid,tvgId:String(row.epg_channel_id||'').trim()||null,added:addedRaw?Number(addedRaw)||null:null,ext:section==='live'?(source.liveExt||'m3u8'):(String(row.container_extension||'').trim()||'mp4'),seriesContainer:section==='series'});
      }
    }
    if(!items.length && warnings.length) throw appError(warnings[0].replace(/^[^:]+:\s*/,''),502,'catalog_failed');
    return {ok:true,items,sourceKey:source.key,epg:true,warnings};
  }

  async function browserDetails(section, providerId) {
    const source=privateSource();
    if(!source) throw appError('Oturum bulunamadı. Kaynağı yeniden bağla.',401,'no_session');
    if(source.type!=='xtream') throw appError('Bu kaynak ayrıntılı dizi bilgisini desteklemiyor.',400,'unsupported_source');
    const isSeries=section==='series';
    if(!['movies','series'].includes(section)||!providerId) throw appError('Geçersiz içerik.',400,'invalid_item');
    const root=await providerFetch(providerApiUrl(source,isSeries?'get_series_info':'get_vod_info',{[isSeries?'series_id':'vod_id']:providerId}),'json');
    const info=root&&root.info&&typeof root.info==='object'?root.info:{}; const back=info.backdrop_path;
    const detail={title:String(info.name||info.title||''),plot:String(info.plot||info.description||''),year:String(info.releasedate||info.releaseDate||''),duration:String(info.duration||''),genre:String(info.genre||''),cast:String(info.cast||''),director:String(info.director||''),rating:String(info.rating||''),poster:String(info.movie_image||info.cover||'').trim()||null,backdrop:Array.isArray(back)?(back[0]||null):(typeof back==='string'&&back?back:null),episodes:[]};
    if(isSeries){
      const episodesRoot=root.episodes&&typeof root.episodes==='object'?root.episodes:{};
      for(const [seasonKey,rows] of Object.entries(episodesRoot)){
        if(!Array.isArray(rows))continue;
        for(const row of rows){if(!row||typeof row!=='object')continue;const id=String(row.id??'').trim();if(!id)continue;const epiInfo=row.info&&typeof row.info==='object'?row.info:{};const season=Number(String(row.season??seasonKey))||0,episode=Number(String(row.episode_num??'0'))||0;detail.episodes.push({id:stableId(`${source.key}|episode|${id}`),name:String(row.title||`S${season} E${episode}`),group:'',section:'series',artwork:String(epiInfo.movie_image||'').trim()||detail.poster,providerId:id,tvgId:null,added:null,ext:String(row.container_extension||'').trim()||'mp4',seriesContainer:false,seriesId:stableId(`${source.key}|series|${providerId}`),season,episode});}
      }
      detail.episodes.sort((a,b)=>a.season-b.season||a.episode-b.episode);
    }
    return {ok:true,details:detail};
  }

  async function api(path, options = {}) {
    const u = new URL(path, location.origin);
    const route = (u.pathname.split('/').pop() || '').replace(/\.php$/i,'').toLowerCase();
    const method = String(options.method || 'GET').toUpperCase();
    let payload=null; if(options.body){try{payload=JSON.parse(options.body);}catch{payload={};}}
    if(route==='health') return {ok:true,service:'MN IPTV Static CDN Gateway',runtime:'static-cdn',version:STATIC_GATEWAY_VERSION};
    if(route==='session') return browserSession(method,payload);
    if(route==='catalog') return browserCatalog();
    if(route==='details') return browserDetails(u.searchParams.get('section')||'',u.searchParams.get('id')||'');
    throw appError('API yolu bulunamadı.',404,'not_found');
  }

  function toast(title, message = '') {
    const el = document.createElement('div'); el.className = 'toast';
    el.innerHTML = `<strong>${esc(title)}</strong>${message ? `<span>${esc(message)}</span>` : ''}`;
    toastRegion.appendChild(el);
    setTimeout(() => { el.style.opacity='0'; el.style.transform='translateY(8px)'; setTimeout(()=>el.remove(),220); }, 3400);
  }

  function hydrateLocalState() {
    state.favorites = new Set(storage.load('favorites', []));
    state.history = storage.load('history', {});
    state.downloads = storage.load('downloads', {});
    state.settings = { autoPiP: true, ...storage.load('settings', {}) };
  }
  function persistFavorites() { storage.save('favorites', [...state.favorites]); }
  function persistHistory() { storage.save('history', state.history); }
  function persistDownloads() { storage.save('downloads', state.downloads); }
  function persistSettings() { storage.save('settings', state.settings); }

  function saveSourceMeta() {
    if (!state.sourceKey || !state.source) return;
    storage.setGlobal('lastSourceKey', state.sourceKey);
    storage.setGlobal(`sourceMeta:${state.sourceKey}`, state.source);
  }

  function setSourceKey(key, source) {
    state.sourceKey = key || '';
    state.source = source || null;
    hydrateLocalState();
    saveSourceMeta();
  }

  function availableOfflineSource() {
    const key = storage.getGlobal('lastSourceKey', '');
    if (!key) return null;
    const raw = localStorage.getItem(`mniptv:${key}:downloads`);
    if (!raw) return null;
    try {
      const downloads = JSON.parse(raw);
      if (!downloads || !Object.keys(downloads).length) return null;
      return { key, meta: storage.getGlobal(`sourceMeta:${key}`, {name:'MN IPTV',server:'Çevrimdışı'}) };
    } catch { return null; }
  }

  async function init() {
    registerServiceWorker();
    bindGlobalEvents();
    const offline = availableOfflineSource();
    try {
      const session = await api('session.php');
      if (session.connected) {
        state.connected = true; state.source = session.source;
        await loadCatalog(session.source);
        return;
      }
    } catch (e) {
      if (e.status !== 401 && isOnline()) console.warn(e);
    }
    if (!isOnline() && offline) {
      enterOfflineMode(offline);
    } else {
      renderLogin(offline);
    }
  }

  async function registerServiceWorker() {
    // GitHub Pages HTTP-direct build: service workers/PWA require HTTPS, so intentionally disabled.
    return;
  }

  function enterOfflineMode(info) {
    state.connected = false; state.offlineMode = true;
    setSourceKey(info.key, info.meta);
    state.catalog = Object.values(state.downloads).map(d => d.item).filter(Boolean);
    indexCatalog();
    state.route = 'downloads';
    renderApp();
  }

  async function loadCatalog(source, forceFresh = false) {
    state.loading = true;
    renderLoadingShell(source);

    const cacheKey = source?.key ? `mniptv:global:catalogCache:${source.key}` : '';
    if (!forceFresh && cacheKey) {
      try {
        const cached = JSON.parse(localStorage.getItem(cacheKey) || 'null');
        if (cached?.items?.length && Date.now() - Number(cached.savedAt || 0) < 6 * 60 * 60 * 1000) {
          state.offlineMode = false; state.connected = true;
          setSourceKey(source.key, source);
          state.catalog = cached.items;
          indexCatalog();
          state.loading = false;
          renderApp();
          if (cached.epg && state.route==='live') scheduleEPG(700);
          return;
        }
      } catch {}
    }

    try {
      const result = await api('catalog.php');
      state.offlineMode = false; state.connected = true;
      setSourceKey(result.sourceKey, source);
      state.catalog = Array.isArray(result.items) ? result.items : [];
      indexCatalog();
      if (result.sourceKey && state.catalog.length) {
        try { localStorage.setItem(`mniptv:global:catalogCache:${result.sourceKey}`, JSON.stringify({items:state.catalog,epg:!!result.epg,savedAt:Date.now()})); } catch {}
      }
      state.loading = false;
      renderApp();
      if (Array.isArray(result.warnings) && result.warnings.length) toast('Katalog kısmen yüklendi', 'Sağlayıcının reddettiği bölümler otomatik atlandı.');
      if (result.epg && state.route==='live') scheduleEPG(700);
    } catch (e) {
      state.loading = false;
      const offline = availableOfflineSource();
      if (offline) { toast('Sunucuya ulaşılamadı', 'İndirilen içerikler çevrimdışı açıldı.'); enterOfflineMode(offline); }
      else renderLogin(null, e.message);
    }
  }

  function indexCatalog() {
    state.itemById = new Map();
    state.categories = {live:[],movies:[],series:[]};
    const counts = {live:new Map(),movies:new Map(),series:new Map()};
    for (const item of state.catalog) {
      state.itemById.set(item.id,item);
      Object.defineProperty(item,'_search',{value:normalized(`${item.name || ''} ${item.group || ''}`),writable:true,configurable:true,enumerable:false});
      if (counts[item.section]) {
        const group=item.group||'Diğer';
        counts[item.section].set(group,(counts[item.section].get(group)||0)+1);
      }
    }
    for (const section of Object.keys(state.categories)) {
      state.categories[section] = [...counts[section].entries()].sort((a,b)=>a[0].localeCompare(b[0],'tr')).map(([name,count])=>({name,count}));
    }
    state.epgLoaded=false; state.epgLoading=false; state.epg.clear();
    resetPageLimit();
  }

  function renderLogin(offlineInfo = null, error = '') {
    app.innerHTML = `
      <main class="login-page">
        <section class="login-visual">
          <div class="login-grid-art"></div>
          <div class="login-copy">
            <div class="brand-row"><div class="brand-mark"><span>M</span><i></i></div><strong>MN IPTV</strong></div>
            <h1>Yayınların.<br>Senin ekranın.</h1>
            <p>Canlı TV, film ve dizilerini tek yerde izle. Kaldığın yerden devam et, bölümler arasında geç ve içeriklerini doğrudan kendi IPTV kaynağından izle.</p>
            <div class="feature-pills"><span>Canlı TV</span><span>Film & Dizi</span><span>İzlemeye Devam Et</span><span>Picture in Picture</span><span>Doğrudan Bağlantı</span></div>
          </div>
        </section>
        <section class="login-panel">
          <form id="loginForm" class="login-card">
            <h2>Kaynağını bağla</h2>
            <p>MN IPTV içerik sağlamaz. Kendi yetkili IPTV hesabını veya M3U listenı bağlayabilirsin.</p>
            <div class="segmented">
              <button type="button" class="${state.sourceMode==='xtream'?'active':''}" data-login-mode="xtream">Hesap bilgileri</button>
              <button type="button" class="${state.sourceMode==='m3u'?'active':''}" data-login-mode="m3u">M3U bağlantısı</button>
            </div>
            ${error ? `<div class="form-error">${esc(error)}</div>` : ''}
            <div class="form-field"><label>Profil adı</label><input name="name" autocomplete="nickname" placeholder="Örn. Ev IPTV"></div>
            ${state.sourceMode==='xtream' ? `
              <div class="form-field"><label>Sunucu adresi</label><input name="server" inputmode="url" autocomplete="url" placeholder="http://sunucu-adresi:port" required></div>
              <div class="form-field"><label>Kullanıcı adı</label><input name="username" autocomplete="username" required></div>
              <div class="form-field"><label>Şifre</label><input name="password" type="password" autocomplete="current-password" required></div>
            ` : `
              <div class="form-field"><label>M3U bağlantısı</label><input name="url" inputmode="url" autocomplete="url" placeholder="https://…/get.php?…" required></div>
            `}
            <div class="form-help">Giriş bilgileri yalnızca bu tarayıcı oturumunda tutulur. Kişisel izleme geçmişin, favorilerin ve indirme kayıtların bu cihazda saklanır.</div>
            <button class="primary-button login-submit" type="submit">Bağlan</button>
            ${offlineInfo ? `<button id="offlineEntry" class="secondary-button offline-entry" type="button">İndirilenleri çevrimdışı aç</button>` : ''}
          </form>
          <section class="public-download-card" aria-label="MN IPTV uygulamalarını indir">
            <div class="public-download-head"><div><strong>MN IPTV uygulamasını indir</strong><span>Platformunu seç ve doğrudan indir.</span></div></div>
            <div class="public-download-links">
              <a href="https://drive.google.com/uc?export=download&id=1yeSmAepFYs56tFeIKL47TXqDmzlGTzSA" target="_blank" rel="noopener"><b></b><span><strong>macOS</strong><small>DMG</small></span></a>
              <a href="https://drive.google.com/uc?export=download&id=1Fjbz7yuP1ZqrQ9y-rcz1aHUfoTLrwaM-" target="_blank" rel="noopener"><b>⊞</b><span><strong>Windows</strong><small>EXE</small></span></a>
              <a href="https://drive.google.com/uc?export=download&id=1rQVpw3qMEZHoT6Lvf1ywjaJgwqQEsDwY" target="_blank" rel="noopener"><b>◆</b><span><strong>Android</strong><small>APK</small></span></a>
              <a href="https://drive.google.com/uc?export=download&id=12Bne0unAR8b1pfVdwAXGKgxJlEPM8xiv" target="_blank" rel="noopener"><b>▣</b><span><strong>Android TV</strong><small>APK</small></span></a>
            </div>
          </section>
        </section>
      </main>`;
    $$('[data-login-mode]').forEach(btn=>btn.addEventListener('click',()=>{state.sourceMode=btn.dataset.loginMode;renderLogin(offlineInfo);}));
    $('#loginForm')?.addEventListener('submit', async e => {
      e.preventDefault(); const form = new FormData(e.currentTarget); const submit = $('.login-submit', e.currentTarget);
      submit.disabled = true; submit.textContent = 'Bağlanıyor…';
      const payload = state.sourceMode==='xtream'
        ? {type:'xtream',name:form.get('name'),server:form.get('server'),username:form.get('username'),password:form.get('password')}
        : {type:'m3u',name:form.get('name'),url:form.get('url')};
      try { const res=await api('session.php',{method:'POST',body:JSON.stringify(payload)}); state.connected=true; await loadCatalog(res.source,true); }
      catch(err){ renderLogin(offlineInfo,err.message); }
    });
    $('#offlineEntry')?.addEventListener('click',()=>enterOfflineMode(offlineInfo));
  }

  function renderLoadingShell(source) {
    app.innerHTML = `
      <div class="layout"><aside class="sidebar"><div class="sidebar-brand"><div class="brand-mark"><span>M</span><i></i></div><strong>MN IPTV</strong></div></aside>
      <main class="main"><div class="boot-screen" style="min-height:80vh"><div class="boot-spinner"></div><strong>İçerikler alınıyor…</strong><span>Katalog hazırlanıyor</span></div></main></div>`;
  }

  const navItems = [
    ['home','⌂','Ana Sayfa'],['live','▣','Canlı TV'],['movies','◆','Filmler'],['series','▤','Diziler'],
    ['continue','↻','Devam Et'],['favorites','★','Favoriler'],['downloads','↓','İndirilenler'],['search','⌕','Arama'],['apps','⇩','Uygulamaları İndir'],['settings','⚙','Ayarlar']
  ];
  const mobilePrimaryNav = new Set(['home','live','movies','series','search']);

  function routeInfo() {
    const map = {
      home:['Ana Sayfa','Ne izlemek istersin?'],live:['Canlı TV','Kanallar ve yayın akışı'],movies:['Filmler','Film arşivin'],series:['Diziler','Diziler ve bölümler'],
      continue:['İzlemeye Devam Et','Kaldığın yerden devam et'],favorites:['Favoriler','Kaydettiğin içerikler'],downloads:['İndirilenler','İnternet olmadan izlemeye hazır'],search:['Arama','Tüm içeriklerde ara'],apps:['Uygulamaları İndir','MN IPTV’yi cihazına kur'],settings:['Ayarlar','MN IPTV tercihleri']
    }; return map[state.route] || map.home;
  }

  function renderApp() {
    const [title,sub] = routeInfo();
    app.innerHTML = `
      <div class="layout">
        <aside class="sidebar">
          <div class="sidebar-brand"><div class="brand-mark"><span>M</span><i></i></div><strong>MN IPTV</strong></div>
          <nav class="nav-list">${navItems.map(([id,icon,label])=>`<button class="nav-button ${state.route===id?'active':''} ${mobilePrimaryNav.has(id)?'mobile-primary':'mobile-secondary'}" data-route="${id}"><span class="nav-icon">${icon}</span><span class="nav-label">${label}</span></button>`).join('')}</nav>
          <div class="sidebar-footer"><div class="source-pill"><small>${state.offlineMode?'Çevrimdışı':'Bağlı kaynak'}</small><strong>${esc(state.source?.name||'MN IPTV')}</strong></div></div>
        </aside>
        <main class="main">
          <header class="topbar">
            <div class="mobile-brand"><div class="brand-mark"><span>M</span><i></i></div></div>
            <div class="page-heading"><h1>${esc(title)}</h1><p>${esc(sub)}</p></div>
            ${['live','movies','series','search'].includes(state.route) ? `<div class="search-box"><input id="globalSearch" value="${esc(state.query)}" placeholder="${state.route==='live'?'Kanal ara…':state.route==='movies'?'Film ara…':state.route==='series'?'Dizi ara…':'Film, dizi veya kanal ara…'}" autocomplete="off"><span>⌕</span></div>` : ''}
            <div class="top-actions">${state.offlineMode?'<span class="chip active">Çevrimdışı</span>':''}<button id="mobileMoreButton" class="mobile-more-button ${mobilePrimaryNav.has(state.route)?'':'active'}" type="button" aria-label="Diğer bölümler" title="Diğer">•••</button></div>
          </header>
          <div id="pageContent" class="content"></div>
        </main>
      </div>`;
    $$('[data-route]').forEach(b=>b.addEventListener('click',()=>navigate(b.dataset.route)));
    $('#mobileMoreButton')?.addEventListener('click',openMobileNav);
    $('#globalSearch')?.addEventListener('input',e=>{
      state.query=e.target.value; resetPageLimit(); clearTimeout(state.searchDebounce);
      state.searchDebounce=setTimeout(renderPage,140);
    });
    renderPage();
  }

  function navigate(route) {
    state.route = route; state.category='all'; if(route!=='search') state.query='';
    resetPageLimit();
    history.replaceState(null,'',`#${route}`); renderApp();
    if(route==='live') scheduleEPG(500);
  }

  function openMobileNav(){
    const extra=navItems.filter(([id])=>!mobilePrimaryNav.has(id));
    contextLayer.hidden=false;
    contextMenu.innerHTML=extra.map(([id,icon,label])=>`<button type="button" data-mobile-route="${id}"><span style="display:inline-block;width:28px">${icon}</span>${esc(label)}</button>`).join('');
    $$('[data-mobile-route]',contextMenu).forEach(b=>b.addEventListener('click',()=>{const route=b.dataset.mobileRoute;closeContext();navigate(route);}));
  }

  function renderPage() {
    const root = $('#pageContent'); if (!root) return;
    switch(state.route) {
      case 'home': root.innerHTML = renderHome(); break;
      case 'live': root.innerHTML = renderCatalogPage('live'); break;
      case 'movies': root.innerHTML = renderCatalogPage('movies'); break;
      case 'series': root.innerHTML = renderCatalogPage('series'); break;
      case 'continue': root.innerHTML = renderContinue(); break;
      case 'favorites': root.innerHTML = renderFavorites(); break;
      case 'downloads': root.innerHTML = renderDownloads(); refreshStorageMeter(); break;
      case 'search': root.innerHTML = renderSearch(); break;
      case 'apps': root.innerHTML = renderApps(); break;
      case 'settings': root.innerHTML = renderSettings(); refreshStorageMeter(); bindSettings(); break;
      default: root.innerHTML = renderHome();
    }
    bindCards(root); bindPageActions(root);
  }

  function renderHome() {
    const cont = continueItems().slice(0,12), fav = state.catalog.filter(i=>state.favorites.has(i.id)).slice(0,12);
    const downloaded = Object.values(state.downloads).sort((a,b)=>(b.savedAt||0)-(a.savedAt||0)).slice(0,10).map(d=>d.item).filter(Boolean);
    const recentMovies = state.catalog.filter(i=>i.section==='movies').sort((a,b)=>(b.added||0)-(a.added||0)).slice(0,12);
    const hero = cont[0] || recentMovies[0] || state.catalog.find(i=>i.section==='series') || state.catalog[0];
    return `${hero ? renderHero(hero) : ''}
      ${cont.length ? sectionBlock('İzlemeye Devam Et','Kaldığın yerden devam et',cont,'continue') : ''}
      ${downloaded.length ? sectionBlock('İndirilenler','İnternet olmadan izlemeye hazır',downloaded,'downloads') : ''}
      ${fav.length ? sectionBlock('Favorilerin','Kaydettiğin içerikler',fav,'favorites') : ''}
      ${recentMovies.length ? sectionBlock('Yeni Eklenen Filmler','Arşivdeki son eklemeler',recentMovies,'movies') : ''}
      ${sectionBlock('Canlı TV','Kanallara hızlı erişim',state.catalog.filter(i=>i.section==='live').slice(0,8),'live',true)}
    `;
  }

  function renderHero(item) {
    const art = safeImage(item.artwork || item.backdrop);
    const h = progressFor(item);
    return `<section class="hero">
      ${art?`<div class="hero-art" style="background-image:url('${esc(art)}')"></div>`:''}
      <div class="hero-copy"><div class="hero-kicker">${esc(sectionLabel(item.section))}</div><h2>${esc(item.name)}</h2>
      <p>${item.section==='live'?'Canlı yayını hemen aç.':h?`Kaldığın yer: ${fmtTime(h.position)} · Tek dokunuşla devam et.`:'İçerik ayrıntılarını aç veya hemen izlemeye başla.'}</p>
      <div class="hero-actions"><button class="primary-button" data-action="open-item" data-id="${esc(item.id)}">${h?'▶ Devam Et':'▶ İzle'}</button><button class="secondary-button" data-action="toggle-favorite" data-id="${esc(item.id)}">${state.favorites.has(item.id)?'★ Favorilerde':'☆ Favoriye Ekle'}</button></div>
      </div></section>`;
  }

  function sectionBlock(title, subtitle, items, route, live=false) {
    if (!items.length) return '';
    return `<section class="section-block"><div class="section-head"><div><h2>${esc(title)}</h2><p>${esc(subtitle)}</p></div><button data-route-inline="${route}">Tümünü Gör</button></div><div class="media-grid ${live?'live-grid':''}">${items.map(renderCard).join('')}</div></section>`;
  }

  function renderCatalogPage(section) {
    const cats = state.categories[section] || [];
    const q = normalized(state.query);
    const browsingCategory = state.category !== 'all';

    // Kategori ekranı artık üstte yatay chip olarak değil, içerik kartlarıyla aynı
    // akışta bir ızgara olarak görünür. Arama yazılınca doğrudan o bölümdeki
    // içerikler aranır; kategori seçilmişse arama yalnızca o kategori içinde kalır.
    if (!browsingCategory && !q) {
      if (!cats.length) return emptyState('Kategori bulunamadı','Bu bölümde gösterilecek kategori yok.');
      return `<div class="catalog-intro"><strong>${esc(sectionLabel(section))} kategorileri</strong><span>Bir kategori seç; içerikler burada listelensin.</span></div>
        <div class="category-grid">${cats.map(c=>renderCategoryCard(c,section)).join('')}</div>`;
    }

    const allItems = filteredCatalog(section);
    const items = allItems.slice(0,state.pageLimit);
    const more = allItems.length>items.length ? `<div class="load-more-wrap"><button class="secondary-button load-more-button" data-load-more> Daha Fazla Göster (${allItems.length-items.length}) </button></div>` : '';
    const heading = browsingCategory ? state.category : `“${state.query.trim()}” arama sonuçları`;
    const sub = browsingCategory
      ? `${allItems.length} içerik${q ? ' · kategori içinde arama' : ''}`
      : `${allItems.length} sonuç · tüm ${sectionLabel(section).toLocaleLowerCase('tr-TR')} içinde`;

    return `<div class="catalog-selection-bar">
        <button class="secondary-button compact category-back-button" data-category-back>← Kategoriler</button>
        <div><strong>${esc(heading)}</strong><span>${esc(sub)}</span></div>
      </div>
      ${items.length ? `<div class="media-grid ${section==='live'?'live-grid':''}">${items.map(renderCard).join('')}</div>${more}` : emptyState('İçerik bulunamadı',q?'Aramana uyan içerik yok.':'Bu kategoride gösterilecek içerik yok.')}`;
  }

  function renderCategoryCard(category, section) {
    const icon = section==='live' ? '▣' : section==='movies' ? '◆' : '▤';
    return `<button class="category-card" type="button" data-category="${esc(category.name)}" aria-label="${esc(category.name)}">
      <span class="category-card-icon">${icon}</span>
      <span class="category-card-copy"><strong>${esc(category.name)}</strong><small>${category.count} içerik</small></span>
      <span class="category-card-arrow">›</span>
    </button>`;
  }

  function filteredCatalog(section) {
    const q=normalized(state.query);
    return state.catalog.filter(i=>i.section===section && (state.category==='all'||i.group===state.category) && (!q || (i._search||normalized(`${i.name} ${i.group}`)).includes(q)));
  }

  function renderContinue() {
    const items=continueItems();
    return items.length ? `<div class="media-grid">${items.map(renderCard).join('')}</div>` : emptyState('Henüz yarım kalan içerik yok','Film veya dizi bölümü izlediğinde kaldığın yer burada görünür.');
  }

  function continueItems() {
    const candidates=[]; const seriesSeen=new Set();
    for(const [id,h] of Object.entries(state.history)){
      if(!h || !h.position || h.position<5 || (h.duration>0 && h.position/h.duration>0.96)) continue;
      if(h.seriesId){
        if(seriesSeen.has(h.seriesId)) continue; seriesSeen.add(h.seriesId);
        const all=Object.values(state.history).filter(x=>x?.seriesId===h.seriesId && x.position>0).sort((a,b)=>(b.updated||0)-(a.updated||0));
        const latest=all[0]; const parent=state.itemById.get(h.seriesId) || latest?.series || {id:h.seriesId,name:latest?.seriesName||'Dizi',section:'series',artwork:latest?.seriesArtwork||latest?.artwork,group:latest?.group||'',seriesContainer:true};
        candidates.push({...parent,_continueUpdated:latest?.updated||0,_resumeEpisode:latest});
      } else {
        const item=state.itemById.get(id) || h.item; if(item) candidates.push({...item,_continueUpdated:h.updated||0});
      }
    }
    return candidates.sort((a,b)=>(b._continueUpdated||0)-(a._continueUpdated||0));
  }

  function renderFavorites() {
    const items=state.catalog.filter(i=>state.favorites.has(i.id));
    return items.length?`<div class="media-grid">${items.map(renderCard).join('')}</div>`:emptyState('Favorilerin boş','Bir içerikte yıldız simgesine dokunarak buraya ekleyebilirsin.');
  }

  function renderSearch() {
    const q=normalized(state.query); if(!q) return emptyState('Ne izlemek istiyorsun?','Yukarıdaki arama alanına film, dizi, bölüm veya kanal adı yaz.');
    const all=state.catalog.filter(i=>(i._search||normalized(`${i.name} ${i.group}`)).includes(q));
    const items=all.slice(0,state.pageLimit);
    const more=all.length>items.length?`<div class="load-more-wrap"><button class="secondary-button load-more-button" data-load-more>Daha Fazla Göster (${all.length-items.length})</button></div>`:'';
    return items.length?`<div class="media-grid">${items.map(renderCard).join('')}</div>${more}`:emptyState('Sonuç bulunamadı','Arama kelimelerini değiştirip tekrar dene.');
  }

  function renderDownloads() {
    const list=Object.values(state.downloads).sort((a,b)=>(b.savedAt||0)-(a.savedAt||0));
    const jobs=[...state.downloadJobs.values()];
    if(!list.length&&!jobs.length) return `${storagePanel()}${emptyState('Henüz indirme yok','Desteklenen film ve dizi bölümlerini ayrıntı ekranındaki İndir düğmesiyle çevrimdışı izlemeye hazırlayabilirsin.')}`;
    return `${storagePanel()}<section class="section-block"><div class="section-head"><div><h2>İndirilen İçerikler</h2><p>Cihazda çevrimdışı kullanıma hazır</p></div></div>
      ${jobs.map(j=>downloadRow(j.item,{downloading:true,progress:j.progress,total:j.total})).join('')}${list.map(d=>downloadRow(d.item,d)).join('')}</section>`;
  }

  function storagePanel(){return `<div class="settings-card" style="margin-bottom:22px"><h3>Çevrimdışı depolama</h3><p>İndirilen dosyalar tarayıcının bu site için ayırdığı alanda tutulur. iOS ve bazı tarayıcılar depolama baskısı olduğunda önbelleği temizleyebilir.</p><div class="storage-meter"><i id="storageFill" style="width:0%"></i></div><div id="storageCaption" class="storage-caption">Depolama hesaplanıyor…</div></div>`;}

  function downloadRow(item,meta={}){
    const img=safeImage(item.artwork); return `<div class="download-row" data-item-id="${esc(item.id)}"><div class="download-thumb">${img?`<img src="${esc(img)}" alt="">`:'↓'}</div><div class="download-info"><strong>${esc(item.name)}</strong><small>${meta.downloading?'İndiriliyor…':`${sectionLabel(item.section)}${meta.size?` · ${fmtBytes(meta.size)}`:''}`}</small>${meta.downloading?`<div class="download-progress"><i style="width:${meta.total?clamp(meta.progress/meta.total*100,0,100):20}%"></i></div>`:''}</div><div>${meta.downloading?'<span class="chip active">İndiriliyor</span>':`<button class="secondary-button" data-action="play-downloaded" data-id="${esc(item.id)}">▶</button> <button class="danger-button" data-action="delete-download" data-id="${esc(item.id)}">Sil</button>`}</div></div>`;
  }

  function renderApps(){
    const apps=[
      {name:'macOS',file:'https://drive.google.com/uc?export=download&id=1yeSmAepFYs56tFeIKL47TXqDmzlGTzSA',format:'DMG',icon:'',desc:'Mac için masaüstü uygulaması'},
      {name:'Windows',file:'https://drive.google.com/uc?export=download&id=1Fjbz7yuP1ZqrQ9y-rcz1aHUfoTLrwaM-',format:'EXE',icon:'⊞',desc:'Windows için masaüstü uygulaması'},
      {name:'Android',file:'https://drive.google.com/uc?export=download&id=1rQVpw3qMEZHoT6Lvf1ywjaJgwqQEsDwY',format:'APK',icon:'◆',desc:'Android telefon ve tabletler için'},
      {name:'Android TV',file:'https://drive.google.com/uc?export=download&id=12Bne0unAR8b1pfVdwAXGKgxJlEPM8xiv',format:'APK',icon:'▣',desc:'Android TV ve Google TV için'}
    ];
    return `<section class="apps-download-hero"><div><span class="eyebrow">MN IPTV</span><h2>Cihazına kur</h2><p>Platformunu seç. Kurulum dosyası Google Drive üzerinden indirilir.</p></div></section>
      <div class="apps-download-grid">${apps.map(a=>`<a class="app-download-card" href="${a.file}" target="_blank" rel="noopener"><div class="app-download-icon">${a.icon}</div><div class="app-download-copy"><strong>${a.name}</strong><span>${a.desc}</span><small>${a.format} · İndirmek için tıkla</small></div><div class="app-download-arrow">↓</div></a>`).join('')}</div>
      <section class="settings-card apps-download-note"><h3>Kurulum dosyaları</h3><p>Bu sayfadaki bağlantılar MN IPTV dağıtım dosyalarını Google Drive üzerinden indirir. Web sürümünü kullanmaya devam etmek istersen herhangi bir şey indirmen gerekmez.</p><a class="checksum-link" href="https://drive.google.com/uc?export=download&id=1cYRhqEJqC6Uzyb4T_QHZrZsHcvzPRnKa" target="_blank" rel="noopener">SHA-256 doğrulama dosyasını indir</a></section>`;
  }

  function renderSettings(){
    return `<div class="settings-grid">
      <section class="settings-card"><h3>Oynatıcı</h3><p>Web oynatıcının davranışını ayarla.</p><div class="setting-row"><div><strong>Uygulamadan çıkarken PiP dene</strong><div class="storage-caption">Tarayıcı izin verirse video yüzen pencerede devam eder.</div></div><label class="switch"><input id="autoPipSetting" type="checkbox" ${state.settings.autoPiP?'checked':''}><span></span></label></div></section>
      <section class="settings-card"><h3>Kaynak</h3><p>${esc(state.source?.name||'MN IPTV')} · ${esc(state.source?.server||'Bağlı IPTV kaynağı')}</p><button id="logoutButton" class="danger-button">Kaynağı Kapat</button></section>
      <section class="settings-card"><h3>Yerel Veriler</h3><p>Favoriler ve izleme geçmişi yalnızca bu tarayıcıda tutulur.</p><div style="display:flex;gap:8px;flex-wrap:wrap"><button id="clearHistory" class="secondary-button">Geçmişi temizle</button><button id="clearFavorites" class="secondary-button">Favorileri temizle</button></div></section>
      <section class="settings-card"><h3>Depolama</h3><p>İndirilen video dosyalarının kullandığı tarayıcı alanı.</p><div class="storage-meter"><i id="storageFill" style="width:0%"></i></div><div id="storageCaption" class="storage-caption">Hesaplanıyor…</div><button id="clearDownloads" class="danger-button" style="margin-top:15px">Tüm indirmeleri sil</button></section>
      <section class="settings-card" style="grid-column:1/-1"><h3>Web sürümü notu</h3><p>Tarayıcıların desteklediği codec ve depolama davranışları native iOS/Android uygulamalarından farklıdır. Direkt MP4/WebM dosyaları çevrimdışı indirmeye uygundur; HLS (.m3u8) yayınları çevrimdışı indirilmez. DRM korumalı içerikler kaynak sağlayıcının web iznine tabidir.</p></section>
    </div>`;
  }

  function emptyState(title,text){return `<div class="empty-state"><div><div class="empty-icon">◌</div><strong>${esc(title)}</strong><p>${esc(text)}</p></div></div>`;}

  function progressFor(item){
    if(item._resumeEpisode) return item._resumeEpisode;
    return state.history[item.id] || null;
  }

  function renderCard(item){
    const live=item.section==='live', img=safeImage(item.artwork), h=progressFor(item), downloaded=!!state.downloads[item.id];
    const epg=live?state.epg.get(item.id):null;
    const subtitle=live?(epg?.now?.title||item.group):(item._resumeEpisode?`S${item._resumeEpisode.season||0} E${item._resumeEpisode.episode||0} · ${fmtTime(item._resumeEpisode.position)}`:item.group);
    const pct=h&&h.duration>0?clamp(h.position/h.duration*100,0,100):0;
    return `<article class="media-card ${live?'live-card':''}" tabindex="0" data-card-id="${esc(item.id)}">
      <div class="poster">${img?`<img loading="lazy" decoding="async" fetchpriority="low" src="${esc(img)}" alt="${esc(item.name)}" onerror="this.remove()">`:`<div class="poster-fallback">${esc(item.name)}</div>`}${live?'<span class="card-badge">CANLI</span>':''}${downloaded?'<span class="card-badge downloaded-badge">↓ İNDİRİLDİ</span>':''}<div class="card-play"><span>▶</span></div></div>
      <div class="card-meta"><div class="card-title">${esc(item.name)}</div><div class="card-subtitle">${esc(subtitle||sectionLabel(item.section))}</div>${pct>0?`<div class="progress-track"><i style="width:${pct}%"></i></div>`:''}</div>
    </article>`;
  }

  function bindCards(root){
    $$('[data-card-id]',root).forEach(card=>{
      const id=card.dataset.cardId;
      card.addEventListener('click',()=>openItemById(id));
      card.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();openItemById(id);}});
      card.addEventListener('contextmenu',e=>{e.preventDefault();openCardContext(id);});
      card.addEventListener('pointerdown',e=>startLongPress(e,id));
      card.addEventListener('pointerup',cancelLongPress);card.addEventListener('pointercancel',cancelLongPress);card.addEventListener('pointermove',e=>{if(state.longPress&&Math.hypot(e.clientX-state.longPress.x,e.clientY-state.longPress.y)>12)cancelLongPress();});
    });
  }

  function startLongPress(e,id){if(e.pointerType==='mouse')return;cancelLongPress();state.longPress={x:e.clientX,y:e.clientY,t:setTimeout(()=>{state.longPress=null;openCardContext(id);},560)};}
  function cancelLongPress(){if(state.longPress?.t)clearTimeout(state.longPress.t);state.longPress=null;}

  function bindPageActions(root){
    $$('[data-route-inline]',root).forEach(b=>b.addEventListener('click',()=>navigate(b.dataset.routeInline)));
    $$('[data-category]',root).forEach(b=>b.addEventListener('click',()=>{state.category=b.dataset.category;state.query='';const input=$('#globalSearch');if(input)input.value='';resetPageLimit();renderPage();}));
    $$('[data-category-back]',root).forEach(b=>b.addEventListener('click',()=>{state.category='all';state.query='';const input=$('#globalSearch');if(input)input.value='';resetPageLimit();renderPage();}));
    $$('[data-load-more]',root).forEach(b=>b.addEventListener('click',()=>{state.pageLimit+=pageStep();renderPage();}));
    $$('[data-action]',root).forEach(b=>b.addEventListener('click',async e=>{e.stopPropagation();await handleAction(b.dataset.action,b.dataset.id);}));
  }

  async function handleAction(action,id){
    const item=resolveItem(id); if(action==='toggle-favorite'&&item){toggleFavorite(item);renderPage();return;}
    if(action==='open-item'&&item){openItem(item);return;}
    if(action==='play-downloaded'){const d=state.downloads[id];if(d?.item)startPlayback(d.item,{resume:true});return;}
    if(action==='delete-download'){await deleteDownload(id);renderPage();return;}
  }

  function resolveItem(id){
    return state.itemById.get(id) || state.downloads[id]?.item || state.history[id]?.item || continueItems().find(i=>i.id===id) || null;
  }

  function toggleFavorite(item){
    if(state.favorites.has(item.id))state.favorites.delete(item.id);else state.favorites.add(item.id);persistFavorites();toast(state.favorites.has(item.id)?'Favorilere eklendi':'Favorilerden çıkarıldı',item.name);
  }

  function openCardContext(id){
    const item=resolveItem(id);if(!item)return;
    const canRemoveContinue=state.route==='continue'||!!progressFor(item);
    contextMenu.innerHTML=`<button data-context="open">${item.seriesContainer?'Bilgiyi Aç':'▶ Oynat'}</button><button data-context="favorite">${state.favorites.has(item.id)?'★ Favorilerden çıkar':'☆ Favoriye ekle'}</button>${canRemoveContinue?'<button class="danger" data-context="remove-continue">İzlemeye Devam Et listesinden kaldır</button>':''}${state.downloads[item.id]?'<button class="danger" data-context="delete-download">İndirmeyi sil</button>':''}`;
    contextLayer.hidden=false;
    $$('[data-context]',contextMenu).forEach(b=>b.addEventListener('click',async()=>{closeContext();const a=b.dataset.context;if(a==='open')openItem(item);if(a==='favorite'){toggleFavorite(item);renderPage();}if(a==='remove-continue'){removeContinue(item);renderPage();}if(a==='delete-download'){await deleteDownload(item.id);renderPage();}}));
  }
  function closeContext(){contextLayer.hidden=true;contextMenu.innerHTML='';}
  function removeContinue(item){
    if(item.seriesContainer||item._resumeEpisode){for(const [id,h] of Object.entries(state.history))if(id===item.id||h?.seriesId===item.id)delete state.history[id];}
    else delete state.history[item.id];
    persistHistory();toast('Listeden kaldırıldı',item.name);
  }

  async function openItemById(id){const item=resolveItem(id);if(item)openItem(item);}
  async function openItem(item){
    if(item.section==='live'&&!item.seriesContainer){startPlayback(item,{resume:false});return;}
    if(item.seriesContainer||item.section==='series'&&item.providerId&&!item.seriesId){openDetails(item);return;}
    if(item.section==='movies'&&item.providerId){openDetails(item);return;}
    startPlayback(item,{resume:true});
  }

  async function openDetails(item){
    modalLayer.hidden=false;modal.innerHTML=detailSkeleton(item);
    let details=state.detailsCache.get(item.id);
    if(!details&&state.connected&&!state.offlineMode&&item.providerId){
      try{const res=await api(`details.php?section=${encodeURIComponent(item.section)}&id=${encodeURIComponent(item.providerId)}`);details=res.details;state.detailsCache.set(item.id,details);if(details.episodes?.length)state.seriesEpisodes.set(item.id,details.episodes);}
      catch(e){details={title:item.name,plot:'',poster:item.artwork,episodes:[]};toast('Ayrıntılar alınamadı',e.message);}
    }
    details=details||{title:item.name,plot:'',poster:item.artwork,episodes:[]};
    modal.innerHTML=renderDetails(item,details);bindDetailActions(item,details);
  }
  function detailSkeleton(item){return `<button class="icon-button glass modal-close" data-modal-close>✕</button><div class="detail-hero skeleton"><div class="detail-copy"><div class="detail-poster"></div><div><h2 class="detail-title">${esc(item.name)}</h2><p>Yükleniyor…</p></div></div></div>`;}

  function renderDetails(item,d){
    const poster=safeImage(d.poster||item.artwork),back=safeImage(d.backdrop||d.poster||item.artwork),fav=state.favorites.has(item.id);
    const resume=item.seriesContainer?latestSeriesProgress(item.id):state.history[item.id];
    const meta=[d.year,d.genre,d.duration,d.rating?`★ ${d.rating}`:''].filter(Boolean);
    const episodes=Array.isArray(d.episodes)?d.episodes:[];
    return `<button class="icon-button glass modal-close" data-modal-close aria-label="Kapat">✕</button><div class="detail-hero">${back?`<div class="detail-backdrop" style="background-image:url('${esc(back)}')"></div>`:''}<div class="detail-copy"><div class="detail-poster">${poster?`<img src="${esc(poster)}" alt="">`:''}</div><div><h2 id="modalTitle" class="detail-title">${esc(d.title||item.name)}</h2><div class="detail-meta">${meta.map(x=>`<span>${esc(x)}</span>`).join('')}</div>${d.plot?`<div class="detail-plot">${esc(d.plot)}</div>`:''}<div class="detail-actions">
      ${item.seriesContainer?(resume?`<button class="primary-button" data-detail="resume-series">▶ Devam Et · S${resume.season||0} E${resume.episode||0}</button>`:''):`<button class="primary-button" data-detail="play">${resume?.position>5?'▶ Devam Et':'▶ İzle'}</button>`}
      <button class="secondary-button" data-detail="favorite">${fav?'★ Favorilerde':'☆ Favoriye Ekle'}</button>${!item.seriesContainer&&canDownload(item)?`<button class="secondary-button" data-detail="download">${state.downloads[item.id]?'✓ İndirildi':'↓ İndir'}</button>`:''}
      </div></div></div></div><div class="detail-body">${episodes.length?renderEpisodes(item,episodes):''}${d.cast?`<p class="storage-caption"><strong>Oyuncular:</strong> ${esc(d.cast)}</p>`:''}${d.director?`<p class="storage-caption"><strong>Yönetmen:</strong> ${esc(d.director)}</p>`:''}</div>`;
  }

  function latestSeriesProgress(seriesId){return Object.values(state.history).filter(h=>h?.seriesId===seriesId&&h.position>0).sort((a,b)=>(b.updated||0)-(a.updated||0))[0]||null;}
  function renderEpisodes(parent,episodes){
    const groups=new Map();episodes.forEach(ep=>{const s=ep.season||0;if(!groups.has(s))groups.set(s,[]);groups.get(s).push(ep);});
    return [...groups.entries()].sort((a,b)=>a[0]-b[0]).map(([season,eps])=>`<h3 class="season-title">Sezon ${season}</h3><div class="episode-list">${eps.map(ep=>{const img=safeImage(ep.artwork||parent.artwork),h=state.history[ep.id],pct=h?.duration?clamp(h.position/h.duration*100,0,100):0;return `<div class="episode-row" data-episode-id="${esc(ep.id)}"><div class="episode-art">${img?`<img src="${esc(img)}" alt="">`:''}</div><div class="episode-info"><strong>${esc(ep.name)}</strong><small>S${ep.season||0} E${ep.episode||0}${h?.position?` · ${fmtTime(h.position)} kaldı`:''}</small>${pct?`<div class="progress-track"><i style="width:${pct}%"></i></div>`:''}</div><div class="episode-actions"><button class="secondary-button" data-episode-play="${esc(ep.id)}">▶</button>${canDownload(ep)?`<button class="secondary-button" data-episode-download="${esc(ep.id)}">${state.downloads[ep.id]?'✓':'↓ İndir'}</button>`:''}</div></div>`;}).join('')}</div>`).join('');
  }

  function bindDetailActions(parent,details){
    $('[data-modal-close]',modal)?.addEventListener('click',closeModal);
    $('[data-detail="favorite"]',modal)?.addEventListener('click',()=>{toggleFavorite(parent);modal.innerHTML=renderDetails(parent,details);bindDetailActions(parent,details);});
    $('[data-detail="play"]',modal)?.addEventListener('click',()=>{closeModal();startPlayback(parent,{resume:true});});
    $('[data-detail="download"]',modal)?.addEventListener('click',()=>downloadItem(parent));
    $('[data-detail="resume-series"]',modal)?.addEventListener('click',()=>{const h=latestSeriesProgress(parent.id);if(!h)return;const ep=(details.episodes||[]).find(e=>e.id===h.item?.id||e.id===h.id)||h.item;if(ep){closeModal();prepareSeriesPlayback(parent,details.episodes,ep,true);}});
    $$('[data-episode-play]',modal).forEach(b=>b.addEventListener('click',e=>{e.stopPropagation();const ep=details.episodes.find(x=>x.id===b.dataset.episodePlay);if(ep){closeModal();prepareSeriesPlayback(parent,details.episodes,ep,true);}}));
    $$('[data-episode-download]',modal).forEach(b=>b.addEventListener('click',e=>{e.stopPropagation();const ep=details.episodes.find(x=>x.id===b.dataset.episodeDownload);if(ep)downloadItem(enrichEpisode(parent,ep));}));
    $$('[data-episode-id]',modal).forEach(r=>r.addEventListener('click',()=>{const ep=details.episodes.find(x=>x.id===r.dataset.episodeId);if(ep){closeModal();prepareSeriesPlayback(parent,details.episodes,ep,true);}}));
  }
  function closeModal(){modalLayer.hidden=true;modal.innerHTML='';}
  function enrichEpisode(parent,ep){return {...ep,seriesId:parent.id,seriesName:parent.name,seriesArtwork:parent.artwork,group:parent.group};}
  function prepareSeriesPlayback(parent,episodes,episode,resume){state.seriesEpisodes.set(parent.id,episodes);startPlayback(enrichEpisode(parent,episode),{resume,episodes,parent});}

  function canDownload(item){return item.section!=='live'&&!String(item.ext||'').toLowerCase().includes('m3u8');}
  function mediaUrl(item){
    const saved=state.downloads[item.id];if(saved?.mediaUrl)return saved.mediaUrl;
    if(item.directUrl){try{return providerProxyUrl(item.directUrl);}catch{return item.directUrl;}}
    const source=privateSource();
    if(!source||source.type!=='xtream')return '';
    const route=item.section==='live'?'live':item.section==='movies'?'movie':item.section==='series'?'series':'';
    if(!route||!item.providerId)return '';
    let ext=String(item.ext||(item.section==='live'?(source.liveExt||'m3u8'):'mp4')).replace(/[^a-zA-Z0-9]/g,'');
    const rel=`${route}/${encodeURIComponent(source.username)}/${encodeURIComponent(source.password)}/${encodeURIComponent(item.providerId)}.${ext||'mp4'}`;
    return providerProxyUrl(new URL(rel, source.base).toString());
  }

  async function downloadItem(rawItem){
    const item={...rawItem};if(state.downloads[item.id]){toast('Zaten indirilmiş',item.name);return;}
    if(!canDownload(item)){toast('Bu içerik indirilemiyor','Web sürümünde HLS/canlı yayın çevrimdışı indirilmiyor.');return;}
    if(!('caches'in window)||!('ReadableStream'in window)){toast('Tarayıcı desteklemiyor','Bu tarayıcı çevrimdışı video önbelleğini desteklemiyor.');return;}
    if(state.downloadJobs.has(item.id))return;
    const url=mediaUrl(item),job={item,progress:0,total:0};state.downloadJobs.set(item.id,job);if(state.route==='downloads')renderPage();toast('İndirme başladı',item.name);
    const controller=new AbortController();job.controller=controller;
    try{
      const response=await fetch(url,{credentials:'omit',signal:controller.signal});if(!response.ok||!response.body)throw new Error('Dosya alınamadı.');
      const total=Number(response.headers.get('content-length')||0);job.total=total;
      if(navigator.storage?.estimate&&total){const est=await navigator.storage.estimate();const free=(est.quota||0)-(est.usage||0);if(free>0&&total>free*.92)throw new Error('Cihazda yeterli tarayıcı depolama alanı yok.');}
      const [cacheStream,progressStream]=response.body.tee();
      const headers=new Headers(response.headers);const cacheResponse=new Response(cacheStream,{status:response.status,statusText:response.statusText,headers});
      const cache=await caches.open('mniptv-downloads-v1');
      const cachePromise=cache.put(new Request(url,{credentials:'omit'}),cacheResponse);
      const reader=progressStream.getReader();let received=0,lastPaint=0;
      while(true){const {done,value}=await reader.read();if(done)break;received+=value.byteLength;job.progress=received;const now=performance.now();if(state.route==='downloads'&&now-lastPaint>350){lastPaint=now;renderPage();}}
      await cachePromise;
      state.downloads[item.id]={item,mediaUrl:url,size:total||received,savedAt:Date.now()};persistDownloads();state.downloadJobs.delete(item.id);toast('İndirme tamamlandı',item.name);if(state.route==='downloads')renderPage();else if(!modalLayer.hidden){/* detail button refreshes next open */}
    }catch(e){state.downloadJobs.delete(item.id);toast('İndirme tamamlanamadı',e.message||'Bilinmeyen hata');if(state.route==='downloads')renderPage();}
  }

  async function deleteDownload(id){
    const d=state.downloads[id];if(!d)return;try{const cache=await caches.open('mniptv-downloads-v1');await cache.delete(d.mediaUrl);}catch{}delete state.downloads[id];persistDownloads();toast('İndirme silindi',d.item?.name||'İçerik');
  }
  async function clearAllDownloads(){for(const id of Object.keys(state.downloads))await deleteDownload(id);try{await caches.delete('mniptv-downloads-v1');}catch{}state.downloads={};persistDownloads();renderPage();}

  async function refreshStorageMeter(){
    if(!navigator.storage?.estimate)return;try{const {usage=0,quota=0}=await navigator.storage.estimate();const pct=quota?clamp(usage/quota*100,0,100):0;$$('#storageFill').forEach(e=>e.style.width=`${pct}%`);$$('#storageCaption').forEach(e=>e.textContent=quota?`${fmtBytes(usage)} / ${fmtBytes(quota)} tarayıcı alanı kullanılıyor`:`${fmtBytes(usage)} kullanılıyor`);}catch{}
  }

  function bindSettings(){
    $('#autoPipSetting')?.addEventListener('change',e=>{state.settings.autoPiP=e.target.checked;persistSettings();});
    $('#clearHistory')?.addEventListener('click',()=>{state.history={};persistHistory();toast('İzleme geçmişi temizlendi');renderPage();});
    $('#clearFavorites')?.addEventListener('click',()=>{state.favorites.clear();persistFavorites();toast('Favoriler temizlendi');renderPage();});
    $('#clearDownloads')?.addEventListener('click',clearAllDownloads);
    $('#logoutButton')?.addEventListener('click',async()=>{try{await api('session.php',{method:'DELETE'});}catch{}state.connected=false;state.catalog=[];state.itemById.clear();renderLogin(availableOfflineSource());});
  }

  function scheduleEPG(delay=600){
    if(state.epgLoaded||state.epgLoading||state.offlineMode)return;
    clearTimeout(state.epgSchedule);
    state.epgSchedule=setTimeout(()=>{if(state.route==='live')loadEPG();},delay);
  }

  function parseEPGWorker(buffer,wantedIds,now){
    return new Promise((resolve,reject)=>{
      const code=`self.onmessage=e=>{
        const {buffer,wanted,now}=e.data;
        const text=new TextDecoder().decode(buffer);
        const wantedSet=new Set(wanted);
        const out={};
        const parseTime=raw=>{const m=String(raw||'').match(/^(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})?\s*([+-])(\d{2})(\d{2})?/);if(!m)return 0;let t=Date.UTC(+m[1],+m[2]-1,+m[3],+m[4],+m[5],+(m[6]||0));const off=((+m[8])*60+(+m[9]||0))*60000;return m[7]==='+'?t-off:t+off;};
        const decode=s=>String(s||'').replace(/<!\[CDATA\[([\s\S]*?)\]\]>/g,'$1').replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&#39;|&apos;/g,"'").replace(/&#(\d+);/g,(_,n)=>String.fromCodePoint(+n));
        const re=/<programme\b([^>]*)>([\s\S]*?)<\/programme>/gi;let m;
        while((m=re.exec(text))){const attrs=m[1],body=m[2];const ch=(attrs.match(/\bchannel=["']([^"']+)["']/i)||[])[1];if(!ch||!wantedSet.has(ch))continue;const start=parseTime((attrs.match(/\bstart=["']([^"']+)["']/i)||[])[1]);const stop=parseTime((attrs.match(/\bstop=["']([^"']+)["']/i)||[])[1]);if(!start||!stop||stop<now-60000||start>now+8*3600000)continue;const title=decode((body.match(/<title\b[^>]*>([\s\S]*?)<\/title>/i)||[])[1]||'Program').trim();(out[ch]||(out[ch]=[])).push({start,stop,title});}
        const result={};for(const ch of wanted){const a=(out[ch]||[]).sort((x,y)=>x.start-y.start);const cur=a.find(p=>p.start<=now&&p.stop>now)||null;const next=a.find(p=>p.start>(cur?.start||now))||null;if(cur||next)result[ch]={now:cur,next};}
        self.postMessage(result);
      };`;
      const url=URL.createObjectURL(new Blob([code],{type:'text/javascript'}));
      const worker=new Worker(url);
      worker.onmessage=e=>{URL.revokeObjectURL(url);worker.terminate();resolve(e.data||{});};
      worker.onerror=e=>{URL.revokeObjectURL(url);worker.terminate();reject(e);};
      worker.postMessage({buffer,wanted:wantedIds,now},[buffer]);
    });
  }

  async function loadEPG(){
    if(state.epgLoaded||state.epgLoading||state.offlineMode||!state.catalog.some(i=>i.section==='live'&&i.tvgId))return;
    state.epgLoading=true;
    try{
      const source=privateSource();if(!source)return;let epgUrl='';
      if(source.type==='xtream'){try{epgUrl=providerXmltvUrl(source);}catch{return;}}
      else if(source.epgUrl){try{epgUrl=providerProxyUrl(new URL(source.epgUrl, source.url).toString());}catch{return;}}
      if(!epgUrl)return;const res=await fetch(epgUrl,{cache:'no-store',credentials:'omit'});if(!res.ok)return;const buffer=await res.arrayBuffer();if(buffer.byteLength>64*1024*1024)return;
      const wanted=new Map();for(const i of state.catalog){if(i.section==='live'&&i.tvgId){if(!wanted.has(i.tvgId))wanted.set(i.tvgId,[]);wanted.get(i.tvgId).push(i.id);}}
      const parsed=await parseEPGWorker(buffer,[...wanted.keys()],Date.now());
      state.epg.clear();for(const [tvg,ids] of wanted){const data=parsed[tvg];if(!data)continue;for(const id of ids)state.epg.set(id,data);}
      state.epgLoaded=true;
      if(state.route==='live')renderPage();
    }catch(e){console.warn('EPG',e);}finally{state.epgLoading=false;}
  }
  function parseXmltvTime(raw){if(!raw)return 0;const m=String(raw).match(/^(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})?\s*([+-])(\d{2})(\d{2})?/);if(!m)return 0;let t=Date.UTC(+m[1],+m[2]-1,+m[3],+m[4],+m[5],+(m[6]||0));const off=((+m[8])*60+(+m[9]||0))*60000;return m[7]==='+'?t-off:t+off;}

  function recordProgress(force=false){
    const item=state.currentItem,v=playerEls.video;if(!item||item.section==='live'||!Number.isFinite(v.currentTime)||v.currentTime<1)return;
    const now=Date.now();if(!force&&now-state.progressTimer<4000)return;state.progressTimer=now;const duration=Number.isFinite(v.duration)?v.duration:0;
    if(duration>0&&v.currentTime/duration>0.965){delete state.history[item.id];}
    else state.history[item.id]={id:item.id,position:v.currentTime,duration,updated:now,item:{...item},seriesId:item.seriesId||null,seriesName:item.seriesName||null,seriesArtwork:item.seriesArtwork||null,season:item.season||null,episode:item.episode||null,group:item.group||''};
    persistHistory();
  }

  async function startPlayback(item,opts={}){
    closeModal();closeContext();stopPlayback(false);state.currentItem={...item};
    if(opts.episodes){state.currentEpisodes=opts.episodes.map(ep=>enrichEpisode(opts.parent||{id:item.seriesId,name:item.seriesName,artwork:item.seriesArtwork,group:item.group},ep));}
    else if(item.seriesId&&state.seriesEpisodes.has(item.seriesId)){state.currentEpisodes=state.seriesEpisodes.get(item.seriesId).map(ep=>({...ep,seriesId:item.seriesId,seriesName:item.seriesName,seriesArtwork:item.seriesArtwork,group:item.group}));}
    else state.currentEpisodes=[];
    state.currentEpisodeIndex=state.currentEpisodes.findIndex(e=>e.id===item.id);
    playerEls.layer.hidden=false;playerEls.title.textContent=item.name;playerEls.eyebrow.textContent=item.seriesId?`S${item.season||0} E${item.episode||0}`:sectionLabel(item.section);
    playerEls.loading.hidden=false;playerEls.error.hidden=true;playerEls.nextUp.hidden=true;playerEls.trackMenu.hidden=true;
    playerEls.live.hidden=item.section!=='live';playerEls.seek.disabled=item.section==='live';playerEls.current.textContent=item.section==='live'?'CANLI':'00:00';playerEls.duration.textContent=item.section==='live'?'':'00:00';
    updateEpisodeButtons();showControls(true);
    const url=mediaUrl(item);const isHls=String(item.ext||'').toLowerCase()==='m3u8';
    const v=playerEls.video;v.controls=true;v.pause();v.removeAttribute('src');v.load();destroyHls();
    const onReady=async()=>{playerEls.loading.hidden=true;const h=state.history[item.id];if(opts.resume!==false&&h?.position>3&&item.section!=='live'){try{v.currentTime=Math.min(h.position,Math.max(0,(v.duration||h.duration||h.position+10)-2));}catch{}}try{await v.play();}catch{}updatePlayButton();scheduleControlsHide();};
    try{
      if(isHls&&window.Hls?.isSupported()){
        const hls=new window.Hls({enableWorker:true,lowLatencyMode:item.section==='live',backBufferLength:60});state.hls=hls;hls.loadSource(url);hls.attachMedia(v);hls.on(window.Hls.Events.MANIFEST_PARSED,onReady);hls.on(window.Hls.Events.AUDIO_TRACKS_UPDATED,()=>updateTrackButtons());hls.on(window.Hls.Events.SUBTITLE_TRACKS_UPDATED,()=>updateTrackButtons());hls.on(window.Hls.Events.ERROR,(evt,data)=>{if(data.fatal){if(data.type===window.Hls.ErrorTypes.NETWORK_ERROR){hls.startLoad();}else showPlayerError('Yayın HLS oynatıcısında açılamadı.');}});
      } else if(isHls&&v.canPlayType('application/vnd.apple.mpegurl')){v.src=url;v.addEventListener('loadedmetadata',onReady,{once:true});}
      else {v.src=url;v.addEventListener('loadedmetadata',onReady,{once:true});}
      v.load();
    }catch(e){showPlayerError(e.message||'Yayın başlatılamadı.');}
  }

  function destroyHls(){if(state.hls){try{state.hls.destroy();}catch{}state.hls=null;}}
  function nativeFullscreenElement(){
    return document.fullscreenElement || document.webkitFullscreenElement || null;
  }
  function videoIsNativeFullscreen(){
    const v=playerEls.video;
    return !!v.webkitDisplayingFullscreen || v.webkitPresentationMode==='fullscreen';
  }
  function isPlayerFullscreen(){
    return !!nativeFullscreenElement() || videoIsNativeFullscreen();
  }
  function syncFullscreenButton(){
    const active=isPlayerFullscreen();
    const glyph=$('#fullscreenGlyph');
    const label=$('#fullscreenLabel');
    if(glyph) glyph.textContent=active?'↙':'⛶';
    if(label) label.textContent=active?'Tam ekrandan çık':'Tam ekran';
    playerEls.fullscreen.setAttribute('aria-label',active?'Tam ekrandan çık':'Tam ekrana geç');
    playerEls.fullscreen.title=active?'Tam ekrandan çık':'Tam ekran';
    document.body.classList.toggle('native-player-fullscreen',!!nativeFullscreenElement()||videoIsNativeFullscreen());
  }
  function setPlayerExpanded(expanded){
    playerEls.layer.classList.toggle('expanded',!!expanded);
    document.body.classList.toggle('player-expanded',!!expanded);
    syncFullscreenButton();
    showControls(true);
  }
  function stopPlayback(hide=true){if(state.currentItem)recordProgress(true);clearTimeout(state.controlsTimer);destroyHls();const v=playerEls.video;try{v.pause();v.removeAttribute('src');v.load();}catch{}state.currentItem=null;state.currentEpisodes=[];state.currentEpisodeIndex=-1;if(hide){playerEls.layer.hidden=true;setPlayerExpanded(false);}playerEls.trackMenu.hidden=true;}
  function showPlayerError(message){playerEls.loading.hidden=true;playerEls.error.hidden=false;playerEls.errorText.textContent=message;showControls(true);}
  function updatePlayButton(){playerEls.play.textContent=playerEls.video.paused?'▶':'❚❚';}
  function updateEpisodeButtons(){const i=state.currentEpisodeIndex,n=state.currentEpisodes.length;playerEls.prev.disabled=!(i>0);playerEls.next.disabled=!(i>=0&&i<n-1);playerEls.prev.style.display=n?'grid':'none';playerEls.next.style.display=n?'grid':'none';}
  function playAdjacent(delta){const i=state.currentEpisodeIndex+delta;if(i<0||i>=state.currentEpisodes.length)return;recordProgress(true);startPlayback(state.currentEpisodes[i],{resume:true});}

  function showControls(persistent=false){playerEls.controls.classList.add('visible');clearTimeout(state.controlsTimer);if(!persistent)scheduleControlsHide();}
  function scheduleControlsHide(){clearTimeout(state.controlsTimer);if(playerEls.video.paused)return;state.controlsTimer=setTimeout(()=>{if(playerEls.trackMenu.hidden&&!state.seekDragging)playerEls.controls.classList.remove('visible');},3000);}
  function toggleControls(){if(playerEls.controls.classList.contains('visible')){if(!playerEls.video.paused){playerEls.controls.classList.remove('visible');clearTimeout(state.controlsTimer);}}else showControls();}

  function bindPlayer(){
    const v=playerEls.video;
    v.controls=true;
    playerEls.close.addEventListener('click',()=>{stopPlayback(true);renderPage();});
    playerEls.play.addEventListener('click',()=>{v.paused?v.play():v.pause();showControls(true);});
    playerEls.rewind.addEventListener('click',()=>{if(state.currentItem?.section!=='live')v.currentTime=Math.max(0,v.currentTime-10);showControls();});
    playerEls.forward.addEventListener('click',()=>{if(state.currentItem?.section!=='live'&&Number.isFinite(v.duration))v.currentTime=Math.min(v.duration,v.currentTime+10);showControls();});
    playerEls.prev.addEventListener('click',()=>playAdjacent(-1));playerEls.next.addEventListener('click',()=>playAdjacent(1));
    playerEls.seek.addEventListener('pointerdown',()=>{state.seekDragging=true;showControls(true);});
    playerEls.seek.addEventListener('pointerup',()=>{state.seekDragging=false;scheduleControlsHide();});
    playerEls.seek.addEventListener('input',()=>{if(Number.isFinite(v.duration)&&v.duration>0){const t=(+playerEls.seek.value/1000)*v.duration;playerEls.current.textContent=fmtTime(t);}});
    playerEls.seek.addEventListener('change',()=>{if(Number.isFinite(v.duration)&&v.duration>0)v.currentTime=(+playerEls.seek.value/1000)*v.duration;});
    playerEls.volume.addEventListener('input',()=>{v.volume=+playerEls.volume.value;v.muted=false;updateMute();});
    playerEls.mute.addEventListener('click',()=>{v.muted=!v.muted;updateMute();});
    playerEls.pip.addEventListener('click',togglePiP);
    $('#playerBackdrop')?.addEventListener('click',()=>setPlayerExpanded(false));
    playerEls.audio.addEventListener('click',()=>openTrackMenu('audio'));playerEls.subtitle.addEventListener('click',()=>openTrackMenu('subtitle'));
    playerEls.retry.addEventListener('click',()=>{const item=state.currentItem;if(item)startPlayback(item,{resume:true});});
    playerEls.nextUpButton.addEventListener('click',()=>playAdjacent(1));
    v.addEventListener('play',()=>{updatePlayButton();scheduleControlsHide();});v.addEventListener('pause',()=>{updatePlayButton();showControls(true);recordProgress(true);});
    v.addEventListener('waiting',()=>playerEls.loading.hidden=false);v.addEventListener('playing',()=>{playerEls.loading.hidden=true;playerEls.error.hidden=true;});
    v.addEventListener('timeupdate',()=>{if(state.currentItem?.section!=='live'){if(Number.isFinite(v.duration)&&v.duration>0){playerEls.seek.value=String(Math.round(v.currentTime/v.duration*1000));playerEls.current.textContent=fmtTime(v.currentTime);playerEls.duration.textContent=fmtTime(v.duration);}recordProgress(false);}});
    v.addEventListener('durationchange',()=>{if(Number.isFinite(v.duration))playerEls.duration.textContent=fmtTime(v.duration);});
    v.addEventListener('ended',()=>{recordProgress(true);if(state.currentEpisodeIndex>=0&&state.currentEpisodeIndex<state.currentEpisodes.length-1){const n=state.currentEpisodes[state.currentEpisodeIndex+1];playerEls.nextUpTitle.textContent=n.name;playerEls.nextUp.hidden=false;showControls(true);}else{showControls(true);}});
    v.addEventListener('error',()=>{if(v.error)showPlayerError(`Tarayıcı bu yayını oynatamadı (kod ${v.error.code}). Kaynak codec/container biçimi web ile uyumlu olmayabilir.`);});
    playerEls.player.addEventListener('click',e=>{if(e.target.closest('button,input,.track-menu,.next-up'))return;toggleControls();});
    playerEls.controls.addEventListener('pointermove',()=>showControls());
    const onNativeFullscreenChange=()=>{
      const active=!!nativeFullscreenElement()||videoIsNativeFullscreen();
      // Gerçek fullscreen ile sayfa-içi 'expanded' modu birbirine karışmasın.
      setPlayerExpanded(false);
      v.controls=true;
      document.body.classList.toggle('native-player-fullscreen',active);
      syncFullscreenButton();
      showControls(true);
    };
    document.addEventListener('fullscreenchange',onNativeFullscreenChange);
    document.addEventListener('webkitfullscreenchange',onNativeFullscreenChange);
    document.addEventListener('fullscreenerror',()=>handleFullscreenFailure());
    document.addEventListener('webkitfullscreenerror',()=>handleFullscreenFailure());
    v.addEventListener('webkitbeginfullscreen',onNativeFullscreenChange);
    v.addEventListener('webkitendfullscreen',()=>{v.controls=true;onNativeFullscreenChange();});
    syncFullscreenButton();
  }
  function updateMute(){playerEls.mute.textContent=playerEls.video.muted||playerEls.video.volume===0?'🔇':'🔊';}
  function isIOSLike(){
    const ua=navigator.userAgent||'';
    const platform=navigator.platform||'';
    return /iPad|iPhone|iPod/i.test(ua) || (platform==='MacIntel' && navigator.maxTouchPoints>1);
  }

  function handleFullscreenFailure(error){
    console.warn('Gerçek tam ekran açılamadı.',error||'fullscreenerror');
    playerEls.video.controls=true;
    setPlayerExpanded(false);
    document.body.classList.remove('native-player-fullscreen');
    syncFullscreenButton();
    toast('Tam ekran açılamadı','Tarayıcı gerçek tam ekran isteğini reddetti. Sayfa yalnızca büyütülmeyecek.');
  }

  function enterNativeFullscreenFromGesture(){
    const v=playerEls.video;

    // Kullanıcı özellikle gerçek fullscreen istiyor. Bu yüzden CSS ile sayfa-içi
    // büyütme hiçbir zaman fullscreen fallback'i değildir.
    setPlayerExpanded(false);

    // iPhone/iPad/WebKit: native video fullscreen en güvenilir yol.
    if(isIOSLike()){
      try{
        v.controls=true;
        if(typeof v.webkitEnterFullscreen==='function'){
          v.webkitEnterFullscreen();
          return;
        }
        if(typeof v.webkitEnterFullScreen==='function'){
          v.webkitEnterFullScreen();
          return;
        }
        if(typeof v.webkitSetPresentationMode==='function'){
          v.webkitSetPresentationMode('fullscreen');
          return;
        }
      }catch(e){
        handleFullscreenFailure(e);
        return;
      }
    }

    // Masaüstü Safari / Firefox / Chromium: fullscreen'i doğrudan VIDEO
    // elemanından iste. Böylece browser chrome'u gizleyen native yol kullanılır.
    // Native controls fullscreen sırasında açık tutulur; çıkınca tekrar kapanır.
    try{
      v.controls=true;
      if(typeof v.requestFullscreen==='function'){
        const p=v.requestFullscreen();
        if(p&&typeof p.catch==='function') p.catch(handleFullscreenFailure);
        return;
      }
      if(typeof v.webkitRequestFullscreen==='function'){
        v.webkitRequestFullscreen();
        return;
      }
      if(typeof v.webkitEnterFullscreen==='function'){
        v.webkitEnterFullscreen();
        return;
      }
      if(typeof v.webkitEnterFullScreen==='function'){
        v.webkitEnterFullScreen();
        return;
      }
    }catch(e){
      handleFullscreenFailure(e);
      return;
    }

    handleFullscreenFailure(new Error('Fullscreen API desteklenmiyor.'));
  }

  function exitNativeFullscreen(){
    const v=playerEls.video;
    try{
      if(document.fullscreenElement&&document.exitFullscreen){
        const p=document.exitFullscreen();
        if(p&&typeof p.catch==='function') p.catch(()=>{});
        return;
      }
      if(document.webkitFullscreenElement&&document.webkitExitFullscreen){
        document.webkitExitFullscreen();
        return;
      }
      if(videoIsNativeFullscreen()&&typeof v.webkitExitFullscreen==='function'){
        v.webkitExitFullscreen();
        return;
      }
      if(v.webkitPresentationMode==='fullscreen'&&typeof v.webkitSetPresentationMode==='function'){
        v.webkitSetPresentationMode('inline');
        return;
      }
    }catch(e){
      console.warn('Tam ekrandan çıkış hatası',e);
    }
    v.controls=false;
    document.body.classList.remove('native-player-fullscreen');
    syncFullscreenButton();
  }

  function toggleFullscreen(){
    if(nativeFullscreenElement()||videoIsNativeFullscreen()){
      exitNativeFullscreen();
      return;
    }
    enterNativeFullscreenFromGesture();
  }
  async function togglePiP(){const v=playerEls.video;try{if(document.pictureInPictureElement)await document.exitPictureInPicture();else if(v.requestPictureInPicture)await v.requestPictureInPicture();else if(v.webkitSupportsPresentationMode){v.webkitSetPresentationMode(v.webkitPresentationMode==='picture-in-picture'?'inline':'picture-in-picture');}else toast('PiP desteklenmiyor','Bu tarayıcı Picture in Picture özelliğini sunmuyor.');}catch{toast('PiP açılamadı','Tarayıcı önce oynatıcıya dokunmanı isteyebilir.');}}

  function updateTrackButtons(){const h=state.hls;playerEls.audio.style.opacity=h?.audioTracks?.length>1?'1':'.55';playerEls.subtitle.style.opacity=h?.subtitleTracks?.length?'1':'.55';}
  function openTrackMenu(type){
    const h=state.hls,buttons=[];
    if(type==='audio'){
      if(h?.audioTracks?.length){h.audioTracks.forEach((t,i)=>buttons.push({label:t.name||t.lang||`Ses ${i+1}`,active:h.audioTrack===i,run:()=>{h.audioTrack=i;}}));}
      else if(playerEls.video.audioTracks?.length){for(let i=0;i<playerEls.video.audioTracks.length;i++){const t=playerEls.video.audioTracks[i];buttons.push({label:t.label||t.language||`Ses ${i+1}`,active:t.enabled,run:()=>{for(let j=0;j<playerEls.video.audioTracks.length;j++)playerEls.video.audioTracks[j].enabled=j===i;}});}}
    }else{
      buttons.push({label:'Kapalı',active:h?h.subtitleTrack<0:![...playerEls.video.textTracks].some(t=>t.mode==='showing'),run:()=>{if(h)h.subtitleTrack=-1;for(const t of playerEls.video.textTracks)t.mode='disabled';}});
      if(h?.subtitleTracks?.length){h.subtitleTracks.forEach((t,i)=>buttons.push({label:t.name||t.lang||`Altyazı ${i+1}`,active:h.subtitleTrack===i,run:()=>{h.subtitleTrack=i;}}));}
      else {for(let i=0;i<playerEls.video.textTracks.length;i++){const t=playerEls.video.textTracks[i];buttons.push({label:t.label||t.language||`Altyazı ${i+1}`,active:t.mode==='showing',run:()=>{for(let j=0;j<playerEls.video.textTracks.length;j++)playerEls.video.textTracks[j].mode=j===i?'showing':'disabled';}});}}
    }
    if(!buttons.length){toast(type==='audio'?'Ek ses parçası yok':'Altyazı bulunamadı');return;}
    playerEls.trackMenu.innerHTML=buttons.map((b,i)=>`<button data-track="${i}" class="${b.active?'active':''}"><span>${esc(b.label)}</span>${b.active?'✓':''}</button>`).join('');playerEls.trackMenu.hidden=false;showControls(true);
    $$('[data-track]',playerEls.trackMenu).forEach(el=>el.addEventListener('click',()=>{buttons[+el.dataset.track].run();playerEls.trackMenu.hidden=true;scheduleControlsHide();}));
  }

  function bindGlobalEvents(){
    bindPlayer();
    $('#modalBackdrop')?.addEventListener('click',closeModal);$('#contextBackdrop')?.addEventListener('click',closeContext);
    document.addEventListener('keydown',e=>{if(e.key==='Escape'){if(nativeFullscreenElement()||videoIsNativeFullscreen())return;if(!contextLayer.hidden)closeContext();else if(!modalLayer.hidden)closeModal();else if(!playerEls.layer.hidden){if(playerEls.layer.classList.contains('expanded'))setPlayerExpanded(false);else stopPlayback(true);}}if(!playerEls.layer.hidden&&e.code==='Space'&&!['INPUT','BUTTON'].includes(document.activeElement?.tagName)){e.preventDefault();playerEls.video.paused?playerEls.video.play():playerEls.video.pause();}});
    document.addEventListener('visibilitychange',async()=>{if(document.hidden&&state.settings.autoPiP&&!playerEls.layer.hidden&&!playerEls.video.paused){try{if(!document.pictureInPictureElement&&playerEls.video.requestPictureInPicture)await playerEls.video.requestPictureInPicture();}catch{}}});
    window.addEventListener('beforeunload',()=>recordProgress(true));
    window.addEventListener('online',()=>toast('İnternet bağlantısı geri geldi'));window.addEventListener('offline',()=>toast('Çevrimdışısın','İndirilen içerikler oynatılabilir.'));
  }

  // Initial route from hash, while keeping mobile nav predictable.
  const initial=(location.hash||'#home').slice(1);if(navItems.some(x=>x[0]===initial))state.route=initial;
  init();
})();
