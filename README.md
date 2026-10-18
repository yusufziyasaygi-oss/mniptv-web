# MN IPTV TV 2.3.0 (Android TV / Google TV)

Xtream Codes tabanlı Android TV uygulaması. Media3 ExoPlayer ile oynatır.

- Giriş: sunucu adresi, kullanıcı adı, şifre
- Canlı TV, Filmler, Diziler; kategoriler, arama, favoriler
- İzlemeye Devam: film başına ve dizi başına tek kart
- Dizi ekranı: sezon/bölüm seçimi, kaldığı yerden devam, otomatik sonraki bölüm
- Kumanda: canlıda ▲▼ kanal, ◀ önceki kanal, OK kanal listesi, ▶ bilgi, rakamlar kanal numarası, uzun OK seçenekler
- Film/dizide ◀▶ 10 sn (basılı tutunca hızlanır), OK oynat/duraklat, uzun OK seçenekler (ses, altyazı, görüntü oranı)

2.3.0: cihazlar arası senkron (TV, Mac, Windows)
- Favoriler, İzlemeye Devam ve bölüm konumları aynı Xtream hesabıyla giriş yapılan tüm cihazlarda eşitlenir
- Veri özel `mn-sync` reposunda, Xtream bilgilerinden türetilen anahtarla AES-256-GCM şifreli durur; şifre/giriş bilgisi gönderilmez
- Biçim: `ci/mnsync.py` (referans), birleştirme öğe başına en yeni zaman damgası

2.2.0 yenilikleri:
- Hızlı açılış: kategori/içerik listeleri diske (gzip) kaydedilir; uygulama son listeyle anında açılır, eskiyse arkada yenilenir, sunucu yanıt vermezse eski liste gösterilir
- Kaldığın yerden açılış: son bölüm ve son kategori hatırlanır
- Canlı TV'de "Son İzlenenler" kategorisi (son 30 kanal)
- Canlı bilgi çubuğunda "Sonra: …" (EPG sıradaki program)
- Seçenekler: uyku zamanlayıcısı (15–120 dk), film/dizide oynatma hızı (0,75x–2x)
- Kategori başlığında içerik sayısı
- Performans: favori/izleme listeleri bellekte tutulur (her tuşta JSON okunmaz), EPG yanıtları 1 dk önbelleklenir, diğer bölümlerin kategorileri arkada önceden yüklenir, afişler 16 bit çözülür (daha az bellek)

Derleme: `mn-iptv-tv` branch'ine push GitHub Actions'ı tetikler, imzasız APK ve log `mn-iptv-tv-out` branch'ine yazılır.
