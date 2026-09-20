(() => {
  'use strict';

  const D = Object.freeze({
    windows: 'https://drive.google.com/uc?export=download&id=16_iKFD_hVOzAUx-J1EQb7sAYYxPFCXIH',
    ios: 'https://drive.google.com/uc?export=download&id=1swm9hKc3oIfdoISK4zjADpKR5BUGw9aY',
    android: 'https://drive.google.com/uc?export=download&id=1_Lp2ssDhsMVD018BJFwtL4ldF3-Ttkgz',
    androidTV: 'https://drive.google.com/uc?export=download&id=1drBtXJtHnQ6FK_RajX1ZFDN86VlZA5R5',
    macArm: 'https://drive.google.com/uc?export=download&id=1-4hF_BFoX-RSE1lpdVFdmafjffQwf90G',
    macIntel: 'https://drive.google.com/uc?export=download&id=1fHeLJ4Xdetn9P0PcLQNUcNfT0CfXZ2K4',
    sha256: 'https://drive.google.com/uc?export=download&id=1ffdHkK8Hk2p5AffATS4Arnrz39Y6aq8j'
  });

  function card(name, desc, format, icon, href, extraClass='') {
    return `<a class="app-download-card ${extraClass}" href="${href}" target="_blank" rel="noopener">
      <div class="app-download-icon">${icon}</div>
      <div class="app-download-copy"><strong>${name}</strong><span>${desc}</span><small>${format} · İndirmek için tıkla</small></div>
      <div class="app-download-arrow">↓</div>
    </a>`;
  }

  function publicLink(name, format, icon, href) {
    return `<a href="${href}" target="_blank" rel="noopener">
      <b>${icon}</b><span><strong>${name}</strong><small>${format}</small></span>
    </a>`;
  }

  function patchAppGrid(grid) {
    if (!grid || grid.dataset.driveCurrent === '1') return;
    grid.dataset.driveCurrent = '1';
    grid.innerHTML =
      card('macOS · Apple Silicon', 'M1 / M2 / M3 / M4 Mac için', 'APP.TGZ · v1.5.2', '', D.macArm) +
      card('macOS · Intel', 'Intel işlemcili Mac için', 'APP.TGZ · v1.5.2', '', D.macIntel) +
      card('Windows', 'Windows için masaüstü uygulaması', 'EXE', '⊞', D.windows) +
      card('iOS', 'iPhone / iPad Xcode paketi', 'ZIP', '◆', D.ios) +
      card('Android', 'Android telefon ve tabletler için', 'APK', '◆', D.android) +
      card('Android TV', 'Android TV ve Google TV için', 'APK', '▣', D.androidTV);
  }

  function patchPublicLinks(box) {
    if (!box || box.dataset.driveCurrent === '1') return;
    box.dataset.driveCurrent = '1';
    box.innerHTML =
      publicLink('macOS · Apple Silicon', 'v1.5.2', '', D.macArm) +
      publicLink('macOS · Intel', 'v1.5.2', '', D.macIntel) +
      publicLink('Windows', 'EXE', '⊞', D.windows) +
      publicLink('iOS', 'ZIP', '◆', D.ios) +
      publicLink('Android', 'APK', '◆', D.android) +
      publicLink('Android TV', 'APK', '▣', D.androidTV);
  }

  function patchChecksum(root=document) {
    root.querySelectorAll?.('a.checksum-link').forEach(a => {
      a.href = D.sha256;
      a.target = '_blank';
      a.rel = 'noopener';
    });
  }

  function patchAll(root=document) {
    root.querySelectorAll?.('.apps-download-grid').forEach(patchAppGrid);
    root.querySelectorAll?.('.public-download-links').forEach(patchPublicLinks);
    patchChecksum(root);
  }

  function boot() {
    patchAll(document);
    const obs = new MutationObserver(() => patchAll(document));
    obs.observe(document.documentElement, {subtree:true, childList:true});
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, {once:true});
  else boot();
})();
