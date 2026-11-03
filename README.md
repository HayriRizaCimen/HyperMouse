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
  - Toplam 18 birim ve uçtan uca test (`18 Passed, 0 Failed`).

---

### Aşama 3: Profiller ve Aktif Pencere Takibi (M3 - Sırada)
- [ ] Olay tabanlı (`SetWinEventHook`) aktif pencere ve işlem adı takibi (`chrome.exe`, `code.exe` vb.).
- [ ] Uygulamaya özel profiller (örn. Tarayıcıda Yan Tuş: Sekme Değiştir, Kod Editöründe: Hata Ayıkla).
- [ ] Evrensel profil (Global Fallback Profile).
- [ ] Profil konfigürasyonlarının JSON olarak kaydedilmesi ve yüklenmesi (`profiles.json`).

### Aşama 4: Modern Windows 11 Arayüzü & Sistem Tepsisi (M4)
- [ ] Wpf.Ui tabanlı Fluent Design ana penceresi (Mica efekti, Koyu/Açık tema).
- [ ] Tuş atama ve ritim düzenleme arayüzü (Tek tık, çift tık, uzun basma seçicileri).
- [ ] Sistem Tepsisi (System Tray / NotifyIcon) entegrasyonu (Arka planda sessiz çalışma).
- [ ] "Tuş Öğrenme" (Listen Mode) diyaloğu: Kullanıcı fareye bastığında hangi tuş olduğunu görsel olarak gösterir.
- [ ] Global duraklatma / acil durum kısayolu (`Ctrl + Alt + Shift + M`).

### Aşama 5: Sistem Entegrasyonu ve Yayınlama (M5)
- [ ] Windows ile otomatik başlatma seçeneği (`HKCU\Run`).
- [ ] Tek örnek koruması (`Named Mutex`).
- [ ] Profil içe/dışa aktarma (Import/Export).
- [ ] Tek dosya (Single-File Self-Contained) `.exe` derleme ve MSIX paketleme.

---

## 🛠️ Nasıl Derlenir ve Test Edilir?

### Gereksinimler
* Windows 10/11
* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Derleme
```powershell
dotnet build
```

### Testleri Çalıştırma
```powershell
dotnet test
```

---

## 📄 Lisans
Bu proje [MIT Lisansı](LICENSE) ile lisanslanmıştır.
