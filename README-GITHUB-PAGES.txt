MN IPTV — GitHub Pages HTTP Direct Build
========================================

Bu paket Netlify proxy kullanmaz. Tarayıcı IPTV sunucusuna doğrudan bağlanır:
http://orfoz60.top:2086

ÖNEMLİ
- Siteyi http://mavinokta.pro olarak aç.
- GitHub Pages ayarlarında "Enforce HTTPS" KAPALI kalmalı.
- GitHub deposuna IPTV kullanıcı adı veya şifre yazma. Bu paket hiçbir kullanıcı adı/şifre içermez.
- HTTP bağlantısı şifreli değildir. Ortak Wi-Fi üzerinde kullanma.
- HTTP nedeniyle PWA/Service Worker ve güvenli-context gerektiren çevrimdışı indirme özellikleri devre dışıdır.
- Favoriler, izleme geçmişi, Devam Et, dizi resume, önceki/sonraki bölüm ve player özellikleri tarayıcı yerel depolamasıyla çalışır.

GITHUB PAGES KURULUMU
1) GitHub'da yeni PUBLIC repository oluştur. Örn: mniptv-web
2) Bu ZIP'in İÇİNDEKİ dosyaların tamamını repo köküne yükle ve Commit et.
3) Repository > Settings > Pages
4) Build and deployment > Source: Deploy from a branch
5) Branch: main / (root) > Save
6) Custom domain alanına: mavinokta.pro yaz > Save
7) "Enforce HTTPS" seçeneğini İŞARETLEME / kapalı bırak.

DNS — mavinokta.pro kök alan adı
Mevcut Netlify A/ALIAS kayıtlarını kaldır ve şu 4 A kaydını ekle:
@  A  185.199.108.153
@  A  185.199.109.153
@  A  185.199.110.153
@  A  185.199.111.153

İstersen www için:
www  CNAME  KULLANICI-ADIN.github.io

DNS oturduktan sonra mutlaka:
http://mavinokta.pro
ile aç. https:// değil.

GİRİŞ
Sunucu adresi: http://orfoz60.top:2086
Kullanıcı adı ve şifre: kendi IPTV hesabın.


REV1 (3.1.0)
- Yayınlar varsayılan olarak yüzen mini player'da açılır.
- Oynatıcıdaki büyüt düğmesiyle tam ekran benzeri büyük moda geçilir, tekrar küçültülebilir.
- Mobil navigasyon 5 ana sekmeye indirildi; diğer bölümler sağ üstteki ••• menüsünde.
- Büyük kataloglar parça parça çizilir (Daha Fazla Göster), arama debounce kullanır.
- EPG işleme ana thread yerine Web Worker'da yapılır; mobilde ağır blur efektleri azaltıldı.
- Posterler lazy/async decode edilir.

REV2:
- Canlı TV / Filmler / Diziler sayfalarında bölüm içi arama.
- Kategoriler üstte yatay butonlar yerine kart/listeler halinde gösterilir.
- Kategori seçilince yalnızca o kategorinin içerikleri açılır.
