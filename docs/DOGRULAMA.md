# Doğrulanan sürüm

3 Ekim 2026 — yerel Mac ve Codex tarayıcı önizlemesi.

- Swift masaüstü uygulaması başarıyla derlendi; yerel imza doğrulandı.
- Web JavaScript dosyalarının sözdizimi kontrolü geçti.
- Tarayıcıda demo video oynatma, duraklatma ve klavye ile %50 noktasına gitme doğrulandı.
- Hız menüsünden 1.5× seçimi arayüzde doğrulandı.
- Kalite menüsünde 1080p → 720p geçişi sonrası gerçek medya boyutu 1280×720 olarak doğrulandı.
- Türkçe altyazı track’i `showing` durumuna geçti; CC açık durumu doğrulandı.
- macOS uygulaması açıldı, yerel demo oynatıldı.
- macOS dosya seçicisiyle uygulama paketinin dışındaki demo-720.mp4 seçildi; listeye eklendi ve oynatma başladı.
- Mac tam ekranı görsel olarak doğrulandı; Esc ile normal görünüme dönüldü.
- Dar tarayıcı görünümünde kontroller ve sayfa düzeni kontrol edildi.

Test sırasında bulunan menü katman sırası ve WKWebView yerel dosya CORS sorunları düzeltildi.

PiP, uyku zamanlayıcısının tüm süreleri, farklı codec’ler, tüm tarayıcı/telefon kombinasyonları ve gerçek web sitesindeki entegrasyon henüz kapsamlı test edilmedi. YouTube özelliklerinin tamamıyla eşitlik sağlanmadı. Canlı siteye kurulum yapılmadı.

## Sade arayüz güncellemesi

Kırmızı vurgu rengi #3b8bff mavisine çevrildi. Başlıklar, sloganlar, marka alanları, tanıtım altlığı, sürekli açık dosya listesi ve başlangıç demosu kaldırıldı. Uygulama ve web önizlemesi boş, siyah oynatıcıyla açıldı; ana ekran yalnızca simgeler ve süre kontrollerini içeriyor. Merkez düğmesinin boş durumda Mac dosya seçicisini açtığı doğrulandı. Güncel ekran görüntüsü onizleme.png dosyasındadır.

## 2.0 medya motoru ve kurulum doğrulaması

VLC 3.0.24 motoru ile 20 video/ses örneği başarıyla çözüldü: 3GP/H.263, ASF/MSMPEG4, AVI/MPEG4, FLV/FLV1, MKV/AV1, MKV/H.264, MKV/HEVC, MOV/ProRes, MP4/H.264, MPG/MPEG1, TS/MPEG2, VOB/MPEG2, WebM/VP8, WMV/WMV2, AIFF, FLAC, MP3, Opus, WAV ve WMA. Test çıktısı CODEC-TESTLERI.txt dosyasındadır.

Son motor değişikliğinden sonra MP4/H.264, MKV/HEVC ve MOV/ProRes tekrar geçti. Uzun HEVC/MKV dosyasının gerçek görüntüsü Cocoa penceresinde doğrulandı. Finder’da MKV dosyasına çift tıklayınca /Applications/MN Player.app dosyayı açtı. Kurulu uygulamanın kod imzası ve gömülü motor imzaları doğrulandı.

Bunlar örnek dosya testleridir; her dosya, her codec profili, DRM, hasarlı içerik veya tüm ağ kaynakları için garanti değildir. OGV/Theora örneği yerel FFmpeg’de encoder bulunmadığı için üretilmedi; bu biçim için test başarısı iddia edilmiyor.

Son kurulum: macOS 13 ve üzeri için Apple Silicon derlemesi. /Applications/MN Player.app kuruldu. 38/38 video içerik türü eşleştirmesi başarılı; hata yok. Sonuç VARSAYILAN-AYARLAR.json dosyasındadır. Finder üzerinden MKV ve WMV’nin varsayılan uygulamada açılması doğrulandı.

## 2.1 pencere ve Finder önizleme doğrulaması

- 360×640 MP4 oynatıldığında masaüstü penceresi 9:16 oranına geçti. Görüntü pencereyi yan boşluksuz doldurdu; dar görünüm kontrolleri iki satırda kontrol edildi.
- Finder'da portrait.mp4 seçiliyken Control + M ile kurulu uygulamanın ayrı MN Önizleme penceresi açıldı.
- Duraklatılmış önizlemede sağ okla süre 27 saniyeden 32 saniyeye, sol okla yeniden 27 saniyeye geçti: her hareket 5 saniye.
- Aşağı ok portrait.mp4 → rotated.mp4; yukarı ok rotated.mp4 → portrait.mp4 geçişini gerçekleştirdi. İki dosya aynı klasörde bulunuyordu.
- 320×180 kaydedilmiş fakat 90 derece döndürme bilgisi olan MP4 dikey olarak görüntülendi; pencere 9:16 oldu. Döndürme bilgisi test dosyasında ffprobe ile ayrıca doğrulandı.
- Gerçek yatay MP4 önizlemesinde pencere yeniden 16:9 oldu.
- Uygulama ve gömülü önizleme yardımcısının yerel kod imzaları kurulum sonrasında doğrulandı.

Quick Look davranışı MN Player'ın ayrı önizleme penceresi üzerinden sağlanır; Apple'ın yerleşik Quick Look uzantısı değildir. Finder'da gezinirken seçili dosya değişmez; önizleme listesi ada göre sıralanır.

Esc ile önizlemenin kapandığı ve Finder'a dönüldüğü doğrulandı. Dikey, yatay ve 90 derece döndürme bilgili MP4 örnekleri kurulu 2.1 motoruyla --verify-media testinden tekrar geçti. Önizleme ekranı onizleme-finder.png dosyasındadır.

## 2.2 — Otomatik kurulum ve yalnız önizleme

- Apple Silicon / macOS 13+ için MNPlayer-2.2-AppleSilicon.pkg oluşturuldu. Paket Uygulamalar klasörünü hedefler ve kullanıcı ayarlarını oturum açmış kullanıcı olarak çalıştırır. Yönetici hesabının tercihlerine yanlışlıkla yazmamak için root/loginwindow oturumlarında ayarlar ilk açılışa bırakılır.
- Kurucunun çağırdığı --configure-install işlemi bu Mac'te başarıyla çalıştırıldı: Finder Control + M doğrulandı, iki uygulama/servis kaydı başarılı ve 38/38 video türü eşleştirmesi başarılı. KURULUM-SONUCU.json sonuçları içerir.
- Finder servisi ana uygulamadan ayrılarak penceresiz MN Finder yardımcısına taşındı. Servis varsayılan olarak etkin olacak NSRequiredContext bildirimi içerir.
- Ana uygulama kapalıyken Control + M denemesinde yalnız MN Finder ve MN Önizleme süreçleri başladı; MNPlayer süreci başlamadı.
- Ana uygulama açıkken Control + M tekrar denendi; macOS, ana MN Player uygulamasını hidden=true olarak bildirdi. Önizleme görünür kaldı.
- Esc önizlemeyi kapattı ve Finder'a döndü.
- Son PKG arşivi açılıp uygulama ve her iki yardımcının 2.2.0 sürümü, servis bildirimi ve postinstall dosyasının kaynakla eşitliği doğrulandı. Paket içinden çıkarılan uygulamanın kod imzaları doğrulandı ve dikey H.264, döndürme bilgili H.264, HEVC/MKV örnekleri oynatma motoru testinden geçti. KURULUM-MEDYA-TESTI.txt sonuçları içerir.
- macOS installer -showChoicesXML paketi başarıyla okudu.

Tam yönetici kurulum akışı yeni/temiz başka bir Mac'te denenmedi. Bu Mac'te kurulu uygulama doğrudan güncellendi ve aynı otomatik ayar işlemi çalıştırıldı. Paket Apple Developer ID ile imzalanmamıştır ve noter onayı yoktur; dağıtımda macOS güvenlik onayı gerekebilir. Intel ve Windows paketi üretilmedi.
