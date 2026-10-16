# MN Player — Mavi Nokta Production

## Kurulum

Başka bir Apple Silicon Mac'e kurmak için `MNPlayer-2.2-AppleSilicon.pkg` dosyasını açıp macOS kurulum adımlarını tamamla. macOS 13 ve üzeri gerekir. Yönetici yetkisiyle uygulama Uygulamalar klasörüne kurulur; oturum açmış kullanıcı için Control + M ve varsayılan video oynatıcısı otomatik ayarlanır. Başka kullanıcı hesapları ilk uygulama açılışında kendi ayarlarını tamamlar. Ayrı VLC kurulumu, terminal veya Klavye Ayarları gerekmez. Kurulum sonucu kullanıcı hesabındaki `~/Library/Application Support/MNPlayer/installation.json` dosyasına yazılır.

Paket yerel olarak hazırlanmıştır; Apple Developer ID ile imzalanmamış ve noter onayı almamıştır. Başka bilgisayarda macOS güvenlik onayı isteyebilir. Intel Mac ve Windows bu paketin hedefi değildir. Yeni/temiz bir Mac'teki kurulum henüz denenmedi; mevcut Mac'te otomatik ayar işlemi ve paket içeriği doğrulandı.

## Masaüstü — sürüm 2.2

Bu bilgisayara `/Applications/MN Player.app` olarak kurulmuştur. Apple Silicon Mac üzerinde çalışır. VLC 3.0.24 motoru ve codec eklentileri uygulamanın içinde gelir; ayrı VLC kurulumu veya açık bir VLC penceresi gerekmez.

Klasör simgesi veya **⌘O** ile video/ses aç. Dosyaları pencereye sürükleyebilirsin. Finder’dan çift tıklama ve “Birlikte Aç” desteklenir. **⌘L** ağ bağlantısı, **⌘S** altyazı açar. Gömülü veya harici altyazıları ve ses kanallarını menüden seçebilirsin. Oynatma hızı, döngü, otomatik sonraki video, uyku zamanlayıcısı, tam ekran, üstte kalan mini pencere ve son oynatma konumunu hatırlama bulunur.

MP4, MOV, M4V, MKV, AVI, WebM, WMV, ASF, FLV/F4V, MPEG, TS/MTS/M2TS, VOB, 3GP, MXF, DV, RealMedia ve motorun okuyabildiği diğer biçimler açılabilir. H.264, HEVC, AV1 ve ProRes dahil birçok codec desteklenir. Ses tarafında MP3, AAC, WAV, FLAC, Opus, AIFF, WMA ve diğer VLC biçimleri kullanılabilir. Dosya uzantısı tek başına oynatma garantisi vermez; şifreli/DRM, bozuk veya bilinmeyen codec’li dosyalar kapsam dışındadır.

Uygulamanın **MN Player → Varsayılan video oynatıcı yap** menüsü kayıtlı video türlerini eşleştirir. **Önceki varsayılanları geri yükle** ilk değişiklikten önceki uygulamaları geri getirir. Ses dosyaları açılabilir, ancak varsayılan ses uygulaması bu işlemle değiştirilmez. Eşleştirme sonucu `~/Library/Application Support/MNPlayer/defaults.json` içinde saklanır. Liste ve medya içeriği sunucuya yüklenmez; ağ kaynağı açarsan o kaynağa bağlanılır.

Masaüstü arayüzü Cocoa ile oluşturuldu; web oynatıcısındaki kalite kaynağı geçişi ve tarayıcı PiP işlevi burada birebir kopyalanmamıştır. Mini mod üstte duran küçük bir uygulama penceresidir. Oynatma listesi oturum boyunca korunur; pencere kapandığında oturum biter. Ses, hız ve oynatma konumları yerel tercihlerde saklanır.

## Web sitesi

Bağımlılıksız `web/player.js` dosyası yeterlidir. Diğer arayüz dosyalarını yüklemek zorunda değilsin. Web component kendi stillerini içerir; sitenin stilleriyle karışmaz.

```html
<script src="/player/player.js"></script>
<mn-player
  src="/videos/film.mp4"
  poster="/videos/kapak.jpg"
  title="Mavi Nokta Production">
</mn-player>