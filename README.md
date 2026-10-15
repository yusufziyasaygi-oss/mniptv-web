# MN IPTV — Windows

Windows 10/11 (x64) sürümü. Android TV ve Mac sürümleriyle aynı işleyiş; fare ve klavyeyle kullanılır.

- .NET 8 WPF, oynatıcı libmpv (mpv-winbuild).
- Derleme ve kurulum dosyası GitHub Actions'ta (`.github/workflows/build-win.yml`) üretilir; çıktılar `mn-iptv-win-out` dalına yazılır.
- `ci/mock_xtream.py` sahte Xtream sunucusu ile uygulama içi otomatik test (`MN_SMOKE_DIR`) çalışır.

Veriler: `%APPDATA%\MN IPTV\data.json`
