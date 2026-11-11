# 🖱️ HyperMouse

> Windows için hafif, yüksek performanslı ve akıllı fare tuşu yeniden eşleme (mouse remapping) & jest motoru.  
> Herhangi bir farenin tuşlarına (Sol, Sağ, Orta, Yan Tuşlar, Tekerlek) tıklama ritimlerine (**tek tık, çift tık, uzun basma, kombinasyon, katman**) göre özel klavye kısayolları, multimedya kontrolleri ve makrolar atayın.

---

## 🚀 Proje Durumu ve Yol Haritası (Roadmap)

### Aşama 0: Altyapı ve Çözüm Mimarisi (M0)
- [x] `.NET 9` ve `WPF` tabanlı çoklu proje mimarisinin kurulması (`HyperMouse.sln`).
- [x] Temiz kod mimarisi: Çekirdek motor (`HyperMouse.Core`) ve testler (`HyperMouse.Tests`) arayüzden izole edildi.
- [x] `WPF-UI 4.3.0` (Fluent Design / Windows 11) paketinin projeye entegrasyonu.
- [x] Git konfigürasyonu (`.gitignore`).

### Aşama 1: Çekirdek Donanım Kancası ve Güvenlik (M1)
- [x] **Bağımsız Thread'li Low-Level Kanca (`ThreadedMouseHook`)**:
  - `WH_MOUSE_LL` Win32 kancası, UI donmalarından etkilenmeyen bağımsız STA thread'inde `GetMessageW` mesaj pompası ile çalışır.
  - Olaylar callback içinde geciktirilmez; `Channel<RawMouseEvent>` kuyruğuna aktarılır (Windows'un 300ms kanca zaman aşımı riski önlendi).
  - GC'nin kanca delegate'ini toplamasını engelleyen `GCHandle` sabitlemesi.
- [x] **Döngü Engelleme (Anti-Loop / Injection Filter)**:
  - Simüle edilen tüm girdilere özel imza (`dwExtraInfo = 0x4D4D4150` - "MMAP") eklendi. Kendi ürettiğimiz girdiler filtrelenerek sonsuz `SendInput` döngüleri engellendi.
- [x] **Girdi Simülatörü (`Win32InputSimulator`)**:
  - Modifikatörlü klavye kısayolları (`Ctrl + C`, `Win + Tab` vb.).
  - Unicode metin simülasyonu (Text Snippet Expansion).
  - Multimedya tuşları (Ses aç/kıs/sustur, oynat/durdur).
  - Fare tuşu simülasyonu ve tekerlek hareketi.
- [x] **Tuş Durumu Takibi (`ButtonStateTracker`)**:
  - DOWN olayı bastırılmışsa (suppress), ilgili UP olayının da bastırılması garanti edilir (sistemde tuş takılı kalmaz).
  - Acil durum durdurmasında (`ReleaseAllHeld`) basılı kalan tuşların işletim sistemine temizlenmesi.

### Aşama 2: Ritim ve Jest Durum Motoru (M2)
- [x] **Sıfır Gecikme (Zero-Latency Passthrough)**:
  - Eşleme yapılmamış tuşlar (örneğin normal Sol Tık) kesinlikle arabelleğe alınmaz; işletim sistemine 0ms gecikmeyle aktarılır.
- [x] **Tıklama Ritimleri (Cadence Engine)**:
  - [x] **Tek Tık (Single Click)**: Çift tık yapılandırılmamışsa tuş bırakıldığı an tetiklenir.
  - [x] **Çift Tık (Double Click) & Üçlü Tık (Triple Click)**: Eşik süresi içinde (varsayılan 280ms) gelen tıklamaları doğru sayarak çalıştırır.
  - [x] **Uzun Basma (Long Press / Hold)**: Tuş basılı tutulduğunda (varsayılan 320ms) tetiklenir; tuş bırakıldığında tek tıkın yanlışlıkla çalışması engellenir.
  - [x] **Bas-Çek (Press & Hold)**: Tuş basıldığı sürece basılı kalır, bırakıldığında serbest bırakılır (Push-to-talk vb. için).
  - [x] **Kombinasyon (Chord)**: Bir tuş basılıyken tekerlek döndürüldüğünde veya başka tuşa basıldığında tetiklenir (örn: *Yan Tuş + Tekerlek* ile Ses Kontrolü).
  - [x] **Katman Değiştirici (Layer / Shift Tuşu)**: Belirli bir tuş basılı tutulurken farenin tüm diğer tuşları 2. katman ayarlarına geçer; bırakıldığında ana katmana geri döner.
- [x] **Eylem Sistemi (`IAction`)**:
  - `HotkeyAction`, `MediaKeyAction`, `MouseRemapAction`, `TextSnippetAction`, `LaunchAppAction`.
- [x] **Ana Orkestrasyon Motoru (`HyperMouseEngine`)**:
  - Donanım kancası, durum makinesi, zaman aşımı kontrol döngüsü ve asenkron eylem yürütücüsünü tek noktadan yönetir.
- [x] **Birim ve Entegrasyon Testleri**:
  - `ITimeProvider` soyutlaması ile deterministik sanal zaman testleri.
  - Toplam 30 birim ve uçtan uca test (`30 Passed, 0 Failed`).

---

### Aşama 3: Modern Windows 11 Arayüzü & Sistem Tepsisi (M3)
- [x] Wpf.Ui tabanlı Fluent Design ana pencere: Mica, yan menü, algılanan fare kartı. Tema seçimi: Sistem / Koyu / Açık. Vurgu renkli dolgulu slider'lar.
- [x] Kural düzenleyici: tuş, ritim (tek, çift, üçlü tık, uzun basma, Bas-Çek, Chord) ve eylem seçimi.
- [x] Sürükle-bırak sıralama: kart ilk/son sıraya kadar taşınabilir, uzun listede kenara yaklaşınca otomatik kaydırma.
- [x] **Çakışma koruması:** Aynı tuş ve ritme ikinci eylem atanamaz. Editör canlı uyarır, Kaydet kilitlenir. Yüklemede yinelenen kurallar temizlenir (`RuleConflictChecker`).
- [x] Sistem tepsisi (`NotifyIcon`) & özel ikon: Kapatınca tepsiye küçülür (Göster, Duraklat/Başlat, Çıkış menülü) ve küçük boyutta okunaklı minimal özel fare ikonu (`Assets/HyperMouse.ico`) EXE, pencere başlığı, görev çubuğu ve tepsi simgesinde kullanılır.
- [x] Canlı test & etkileşimli 3D fare görseli (`Mouse3DVisual`): Basılan tuş 3D modelde animasyonla parlar/çöker; 360° sürükleyerek döndürme, hazır kamera açıları ve hover düğme bilgisi sunar. Olay kaydı isteğe bağlı açılır; sade görünüm varsayılan, Hook/RawInput/HID ayrıntıları "Ayrıntılı" ile görünür (hepsi `diag.log`'a yazılır).
- [x] **DPI tuşu öğrenme** (Tuş Haritası ekranında): 3 saniyelik sayaçla standart dışı sinyal öğretilir (Consumer Control, HID Keyboard, Vendor HID raporu, raw button bits).
- [x] Fare modeli tanıma: HID product string ve PnP aygıt ağacı kullanılır. USB, Bluetooth LE (`VID&02xxxx`) ve I2C yolları desteklenir. Takma ad verilebilir ve tak-çıkar takip edilir.
- [x] Global duraklatma / acil durum kısayolu (`Ctrl + Alt + Shift + M`, `RegisterHotKey`).

### Aşama 4: Sistem Entegrasyonu ve Yayınlama (M4)
- [x] Windows ile otomatik başlatma (`HKCU\...\Run`, `--tray` ile gizli açılır).
- [x] Tek örnek koruması (`Named Mutex`). İkinci açılış çalışan pencereyi öne getirir.
- [x] Profil içe/dışa aktarma (Import/Export, doğrulama ve çakışma temizliği ile).
- [x] Tek dosya (Single-File, Self-Contained) `.exe` derleme (`SingleFile` publish profili).
- [ ] MSIX paketleme (imza sertifikası gerektirir).

### Aşama 5: Profiller ve Aktif Pencere Takibi (M5 - Sırada)
- [ ] Olay tabanlı (`SetWinEventHook`) aktif pencere ve işlem adı takibi (`chrome.exe`, `code.exe` vb.).
- [ ] Uygulamaya özel profiller (örn. tarayıcıda Yan Tuş sekme değiştirir, kod editöründe hata ayıklar).
- [ ] Evrensel profil (Global Fallback Profile).
- [x] Profil konfigürasyonunun JSON olarak kaydedilmesi ve yüklenmesi (`profiles.json`).

---

## 🛠️ Nasıl Derlenir ve Test Edilir?

### Gereksinimler
* Windows 10/11
* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Derleme
```powershell
dotnet build
```

### Çalıştırma
```powershell
dotnet run --project HyperMouse.csproj
```

### Testleri Çalıştırma
```powershell
dotnet test
```

### Tek dosya .exe (kurulum gerektirmez)
```powershell
dotnet publish HyperMouse.csproj -p:PublishProfile=SingleFile
# Çıktı: publish\HyperMouse.exe
```

---

## 📄 Lisans
Bu proje [MIT Lisansı](LICENSE) ile lisanslanmıştır.
