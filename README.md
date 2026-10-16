# MN Player — macOS

Yerel macOS oynatıcı (LibVLC). Kaynak: Drive hafıza modülü 01 (MN Player 2.2.2, 13 Ekim düzeltmeleri dahil).
`MNPlayer.swift` ve `FinderService.swift` kayıttaki SHA-256 ile birebir aynıdır.

- `desktop/build.sh` — uygulamayı, `MN Preview.app` ve `MN Finder.app` yardımcılarını derler (VLC.app gerekir).
- Finder önizleme: `MN Finder.app` bir Servis ("MN Player Önizleme") sağlar; kurulum Finder'da bu servise Control+M atar.
- CI: `.github/workflows/build-player.yml`, çıktılar `mn-player-mac-out` dalında.

Not: Asıl `MNPlayer.icns` kayıtta yoktu; `assets/icon-1024.png` geçici ikondur.
