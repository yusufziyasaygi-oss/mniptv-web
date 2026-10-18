# MN IPTV Mac 2.3.0

2.3.0: cihazlar arası senkron (TV, Mac, Windows). Favoriler, İzlemeye Devam ve bölüm konumları aynı Xtream hesabındaki tüm cihazlarda eşitlenir; veri AES-256-GCM şifreli (`Sources/Sync.swift`, referans `ci/mnsync.py`).

TV sürümüyle aynı işleyiş; mouse ve klavye ile kullanılır. Oynatma libmpv ile (TS, HLS, MKV, MP4).

Klavye:
- Katalog: oklar gezinme, Enter aç, Esc geri, F favori, ⌘1–6 bölümler, ⌘F arama, ⌘R listeyi yenile
- Canlı: ↑↓ kanal, ← önceki kanal, → bilgi, Enter kanal listesi, rakamlar kanal numarası, Space duraklat
- Film/dizi: ←→ 10 sn (Shift 60 sn), ↑↓ ses, Space/Enter oynat-duraklat, N/P sonraki/önceki bölüm
- Her yerde: F tam ekran, M sessiz, Esc geri / tam ekrandan çık

Derleme: `bash scripts/build.sh` (Homebrew mpv gerekir). GitHub Actions `mn-iptv-mac` branch'inde derler, DMG ve test çıktısını `mn-iptv-mac-out` branch'ine yazar.
