(() => {
  'use strict';

  const DOWNLOADS = Object.freeze({
    macOS: 'https://drive.google.com/uc?export=download&id=1yeSmAepFYs56tFeIKL47TXqDmzlGTzSA',
    windows: 'https://drive.google.com/uc?export=download&id=1Fjbz7yuP1ZqrQ9y-rcz1aHUfoTLrwaM-',
    android: 'https://drive.google.com/uc?export=download&id=1rQVpw3qMEZHoT6Lvf1ywjaJgwqQEsDwY',
    androidTV: 'https://drive.google.com/uc?export=download&id=1eVEuN2zR1y9LH32Begozq334fvFu40X6',
    sha256: 'https://drive.google.com/uc?export=download&id=1oa0v6nvx_onjdgL2Wbwk722MTrrk6C03'
  });

  const oldIds = Object.freeze({
    '1yeSmAepFYs56tFeIKL47TXqDmzlGTzSA': DOWNLOADS.macOS,
    '1Fjbz7yuP1ZqrQ9y-rcz1aHUfoTLrwaM-': DOWNLOADS.windows,
    '1rQVpw3qMEZHoT6Lvf1ywjaJgwqQEsDwY': DOWNLOADS.android,
    '12Bne0unAR8b1pfVdwAXGKgxJlEPM8xiv': DOWNLOADS.androidTV,
    '1eVEuN2zR1y9LH32Begozq334fvFu40X6': DOWNLOADS.androidTV,
    '1cYRhqEJqC6Uzyb4T_QHZrZsHcvzPRnKa': DOWNLOADS.sha256,
    '1oa0v6nvx_onjdgL2Wbwk722MTrrk6C03': DOWNLOADS.sha256
  });

  function normalizeText(el) {
    return (el.textContent || '').replace(/\s+/g, ' ').trim().toLocaleLowerCase('tr-TR');
  }

  function desiredLink(a) {
    const href = a.getAttribute('href') || '';
    for (const [id, url] of Object.entries(oldIds)) {
      if (href.includes(id)) return url;
    }

    const t = normalizeText(a);
    if (t.includes('android tv') || t.includes('google tv')) return DOWNLOADS.androidTV;
    if (t.includes('windows') || t.includes('exe')) return DOWNLOADS.windows;
    if (t.includes('android') && !t.includes('tv')) return DOWNLOADS.android;
    if (t.includes('macos') || t.includes('mac için') || t.includes('dmg')) return DOWNLOADS.macOS;
    if (t.includes('sha-256') || t.includes('sha256')) return DOWNLOADS.sha256;
    return null;
  }

  function patchDownloads(root = document) {
    root.querySelectorAll?.('a').forEach(a => {
      const target = desiredLink(a);
      if (!target) return;
      if (a.href !== target) a.href = target;
      a.target = '_blank';
      a.rel = 'noopener';
    });
  }

  function boot() {
    patchDownloads(document);
    const observer = new MutationObserver(mutations => {
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (node.nodeType !== 1) continue;
          patchDownloads(node);
          if (node.matches?.('a')) {
            const target = desiredLink(node);
            if (target) {
              node.href = target;
              node.target = '_blank';
              node.rel = 'noopener';
            }
          }
        }
      }
    });
    observer.observe(document.documentElement, {childList: true, subtree: true});
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, {once: true});
  } else {
    boot();
  }
})();
