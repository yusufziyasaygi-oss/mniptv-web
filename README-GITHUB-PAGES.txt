MN IPTV WEB REV3.1 - GENERIC PROVIDERS / CACHE BUST

MN IPTV — GitHub Pages HTTP Direct Build — REV3
================================================

Bu sürüm TEK BİR IPTV SAĞLAYICISINA KİLİTLİ DEĞİLDİR.
Kullanıcı kendi Xtream Codes sunucu adresi + kullanıcı adı + şifresini veya kendi M3U bağlantısını girebilir.

ÖNEMLİ
- Siteyi mümkünse http://mavinokta.pro olarak aç.
- GitHub Pages ayarlarında "Enforce HTTPS" KAPALI kalmalı.
- Bir IPTV sağlayıcısı yalnızca HTTP sunuyorsa HTTPS sayfası mixed-content nedeniyle bağlantıyı engelleyebilir.
- Bazı IPTV sağlayıcıları tarayıcı erişimine CORS izni vermez. Böyle bir sağlayıcı native iOS/macOS/Android uygulamasında çalışsa bile web sürümünde çalışmayabilir.
- GitHub deposuna IPTV kullanıcı adı veya şifre yazılmaz. Giriş bilgileri yalnızca o tarayıcı oturumunda tutulur.
- HTTP bağlantısı şifreli değildir; ortak Wi-Fi üzerinde hassas giriş bilgileri kullanma.
- Favoriler, izleme geçmişi, Devam Et ve diğer kişisel kayıtlar cihazdaki tarayıcı depolamasında, IPTV hesabına göre ayrı tutulur.

GİRİŞ SEÇENEKLERİ
1) Xtream Codes
   - Sunucu adresi: sağlayıcının verdiği http:// veya https:// adresi ve gerekiyorsa port
   - Kullanıcı adı
   - Şifre

2) M3U
   - Sağlayıcının verdiği tam M3U URL'si

REV3 (3.2.0)
- orfoz60.top / 2086 sabit sağlayıcı kilidi tamamen kaldırıldı.
- Girilen Xtream sunucu adresi dinamik olarak kullanılır.
- player_api.php, XMLTV/EPG ve canlı/film/dizi medya URL'leri bağlı hesabın kendi sunucusundan oluşturulur.
- http ve https Xtream sunucuları desteklenir.
- Kullanıcı sunucu alanına player_api.php / get.php / xmltv.php adresi yapıştırırsa temel sunucu yolu otomatik ayıklanır.
- Sağlayıcının allowed_output_formats bilgisinde m3u8 varsa canlı yayınlarda m3u8 tercih edilir; yoksa ts kullanılabilir. Tarayıcı codec/container desteği yine sağlayıcıya göre değişebilir.
- Bağlantı başarısızlığında CORS / yanlış adres / port olasılığı daha açık hata mesajıyla gösterilir.
- Önceki hesabın favori/geçmiş anahtarı korunacak şekilde hesap kimliği hostname + kullanıcı adı üzerinden oluşturulur.

REV2
- Canlı TV / Filmler / Diziler sayfalarında bölüm içi arama.
- Kategoriler üstte yatay butonlar yerine kart/listeler halinde gösterilir.
- Kategori seçilince yalnızca o kategorinin içerikleri açılır.

REV1
- Yüzen mini player + büyüt/küçült.
- Mobil navigasyon sadeleştirmeleri.
- Büyük kataloglarda parça parça çizim, arama debounce, EPG Web Worker, lazy poster.

GITHUB PAGES GÜNCELLEME
Mevcut repo kullanılıyorsa bu ZIP'in içindeki dosyaları repo köküne yükle ve aynı adlı dosyaların üzerine yazıp Commit et.
DNS veya CNAME'i değiştirme.


REV3.1: app.js ve styles.css dosya adları değiştirildi; Safari/GitHub Pages eski REV1 dosyasını önbellekten kullanamasın diye asset adları cache-bust edildi.
