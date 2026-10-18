# MN IPTV — Windows

2.3.0: cihazlar arası senkron (TV, Mac, Windows). Favoriler, İzlemeye Devam ve bölüm konumları aynı Xtream hesabındaki tüm cihazlarda eşitlenir; veri AES-256-GCM şifreli (`src/Core/Sync.cs`, referans `ci/mnsync.py`).

Windows 10/11 (x64) sürümü. Android TV ve Mac sürümleriyle aynı işleyiş; fare ve klavyeyle kullanılır.

- .NET 8 WPF, oynatıcı libmpv (mpv-winbuild).
- Derleme ve kurulum dosyası GitHub Actions'ta (`.github/workflows/build-win.yml`) üretilir; çıktılar `mn-iptv-win-out` dalına yazılır.
- `ci/mock_xtream.py` sahte Xtream sunucusu ile uygulama içi otomatik test (`MN_SMOKE_DIR`) çalışır.

Veriler: `%APPDATA%\MN IPTV\data.json`
