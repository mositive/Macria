# Macria Kod İnceleme Raporu

- **Tarih:** 2026-09-26
- **Kapsam:** `Macria/` kaynak kodu (yaklaşık 35.000 satır C#), test projeleri, `GeometryEngineRuntime/`, csproj ve publish ayarları
- **Branch:** `enes-yesiloz`. Commitlenmemiş değişiklikler de dahil: `PreviewCore.cs`, `MainWindow.DxfDwgFiles.cs` ve diğerleri.
- **Yöntem:** Yalnızca kod okundu. İnceleme sırasında hiçbir dosya değiştirilmedi; build ve test çalıştırılmadı.
- **Sınır:** CATIA'ya bağlı davranışların hiçbiri CATIA olmayan makinede doğrulanamaz. Emin olunamayan bulgular **(doğrulanmalı)**, kullanıcı kararı gerektirenler **Karar Bekliyor** diye işaretlidir.

Satır numaraları bu tarihteki çalışma kopyasına aittir; kod değiştikçe kayabilir.

---

## 1. Genel mimari

- **Uygulamanın kendisi `MainWindow` partial sınıfı.** Ana dosya `MainWindow.xaml.cs` yaklaşık 5.000 satır, yanında yaklaşık 25 partial dosya var. Ayrı bir servis katmanı yok. Tüm özellikler aynı alanları paylaşıyor: `_catia`, `_repRefs`, `_costRepRefs`, `_stopRequested`, `_pip`.
- **Akış:** CATIA'ya bağlan (`CatiaConnect`) → tara (`DoScan`) → `ReferenceKey` üzerinden sonuçlar `_repRefs`'e yazılır → aynı referanslarla DXF export, maliyet ölçümü, profil teşhisi ve STEP export çalışır.
  - Tarama `Task.Run` içinde (MTA thread'inde) yapılıyor.
  - Bunun dışındaki tüm COM çağrıları UI thread'inde.
- **DXF export:** CATIA'daki "Save As DXF" paneli görsel eşleşmeyle bulunuyor (`SaveAsBulucu`, `GorselEslesme`), gerçek fare tıklaması ve klavye olaylarıyla sürülüyor.
- **DXF okuma ve düzenleme:**
  - `DxfOkuyucu`: saf parser.
  - `OnizlemeWindow`: çizim ve kullanıcı etkileşimi.
  - `DxfKoseGeometrisi`: köşe işlemleri (birleştirme, pah, radius) için geometri planı.
  - `DxfEditOturumu`: Undo/Redo ve güvenli kayıt.
- **GeometryEngine:** Ayrı bir süreç olarak çalışıyor (`GeometryLabProcessAdapter`). Ancak OCCT viewer DLL'i Macria sürecinin içine yükleniyor (bkz. Ö11).
- **Raporlama:** Tek bir rapor modeli (`RaporModel`), Excel ve PDF'e ayrı yazıcılarla (`ExcelYazici`, `PdfYazici`) dönüştürülüyor.

---

## 2. 🔴 Kritik

### K1. Onay izleyicisi, sistemdeki hangi uygulamaya ait olursa olsun her Evet/Hayır kutusunda "Evet"e basıyor

**Konum:** [Macria/MainWindow.xaml.cs:4520](Macria/MainWindow.xaml.cs#L4520) (`TryConfirmDialog`), [Macria/MainWindow.xaml.cs:4590](Macria/MainWindow.xaml.cs#L4590) (`OnayIzleyiciBaslat`)

**Sorun:**
- Her export, STEP ve GeometryLab işlemi boyunca her 400 ms'de `EnumWindows` ile tüm pencereler taranıyor. Pencerenin CATIA'ya ait olup olmadığına bakılmıyor.
- Sonuç olarak Explorer'ın "silinsin mi?", Outlook'un "kaydedilsin mi?" gibi kutuları, hatta CATIA'nın "değişiklikler kaydedilsin mi?" sorusu da onaylanabilir. Sonuncusu "kaynak parçayı değiştirmez" ilkesine aykırı.

**Düzeltme:** Pencereyi `GetWindowThreadProcessId` ile CATIA süreç kimliğine göre filtrele (bu fonksiyon projede zaten tanımlı). İsteğe bağlı olarak onaylanabilecek pencere başlıkları için bir izin listesi ekle.

### K2. Kaydetme penceresi bulunamasa da dosya yolu yapıştırılıp Enter'a basılıyor

**Konum:** [Macria/MainWindow.xaml.cs:4106-4112](Macria/MainWindow.xaml.cs#L4106) (`ExportOneIc`)

**Sorun:**
- Üç deneme sonunda `hSave == IntPtr.Zero` kalırsa kod durmuyor. `ForceForeground(0)`, ardından Ctrl+V ve Enter çalışıyor.
- Yani o an odakta hangi pencere varsa (CATIA'nın komut satırı, başka bir uygulama) dosya yolu oraya yazılıp Enter'a basılıyor.

**Düzeltme:** Döngüden sonra `if (hSave == IntPtr.Zero)` kontrolü ekle: hatayı logla, açılan parçayı kapat, `false` döndür.

### K3. Kaydetme penceresi tespiti fazla gevşek

**Konum:** [Macria/MainWindow.xaml.cs:4931-4937](Macria/MainWindow.xaml.cs#L4931) (`WaitForSaveDialog`)

**Sorun:**
- Tüm süreçlerde, `id=1` kontrolüne sahip her `#32770` penceresi kaydetme penceresi kabul ediliyor. Sıradan bir MessageBox'ın OK düğmesinin kimliği de 1 (IDOK).
- CATIA'dan gelen bir uyarı kutusu Save As penceresi sanılabilir. K2 ile birleşince dosya yolu o uyarı kutusuna yazılıp Enter'a basılır.

**Düzeltme:** CATIA süreç kimliğiyle filtrele. Ayrıca pencerede dosya adı kutusunun (Edit/ComboBoxEx32) varlığını şart koş.

---

## 3. 🟠 Önemli

### 3.1 Hatalar ve kaynak yönetimi

#### Ö1. Açılan parça penceresi her durumda kapatılmıyor; kapatırken de yanlış pencereyi hedefleyebiliyor

**Konum:**
- [Macria/MainWindow.xaml.cs:4034](Macria/MainWindow.xaml.cs#L4034) (`ExportOneIc`): durdurma yolu [4099](Macria/MainWindow.xaml.cs#L4099), kapatma [4122](Macria/MainWindow.xaml.cs#L4122)
- [Macria/MainWindow.Maliyet.cs:731](Macria/MainWindow.Maliyet.cs#L731)

**Sorun:**
- `ExportOneIc`'te durdurma yolunda ve exception durumunda `ActiveWindow.Close()` hiç çağrılmıyor. Açılan parça CATIA'da açık kalıyor.
- Maliyet'in catch bloğu, parça hiç açılmamış olsa da `ActiveWindow.Close()` çağırıyor. Bu durumda **montaj penceresi kapanabilir**.

**Düzeltme:** [Macria/MainWindow.Profil.cs:610-617](Macria/MainWindow.Profil.cs#L610) doğru kalıbı zaten kullanıyor: bir `pencereAcildi` bayrağı tutup kapatmayı `try/finally` içinde yapıyor. Aynı kalıbı bu iki yere uygula.

#### Ö2. Uzun CATIA işlemleri arasında ortak bir kilit yok

**Konum:** [Macria/MainWindow.xaml.cs:2273](Macria/MainWindow.xaml.cs#L2273) (`SetExporting`), [Macria/MainWindow.Maliyet.cs:534](Macria/MainWindow.Maliyet.cs#L534) (`btnCostScan_Click`)

**Sorun:**
- Export yalnızca `_exporting` bayrağına, maliyet yalnızca `_maliyetCalisiyor` bayrağına bakıyor.
- Maliyet ölçümü sürerken DXF görünümüne geçip "Tümünü Export" başlatılabiliyor. `btnBack` export sırasında da devre dışı bırakılmıyor.
- İki akış aynı anda parça açıp `ActiveWindow.Close()` çağırabilir. Ayrıca `_stopRequested` ve `_pip` her iki akışta ortak.

**Düzeltme:** Tek bir "CATIA meşgul" koruması ekle ve bütün uzun işlemler bunu kontrol etsin.

#### Ö3. Toplu export, hedef klasördeki aynı isimli DXF dosyalarını sormadan siliyor

**Konum:** [Macria/MainWindow.xaml.cs:4019](Macria/MainWindow.xaml.cs#L4019)

**Sorun:** Tek parça export'ta SaveFileDialog üzerine yazma onayı soruyor. Toplu export'ta hiçbir uyarı yok.

**Düzeltme:** Export başlamadan çakışan dosyaları listele ve kullanıcıya bir kez sor: üzerine yaz, atla ya da yeniden adlandır.

#### Ö4. `WaitForFile` dosya oluşur oluşmaz başarı döndürüyor

**Konum:** [Macria/MainWindow.xaml.cs:4116](Macria/MainWindow.xaml.cs#L4116)

**Sorun:** CATIA dosyayı hâlâ yazıyor olabilir. Buna rağmen yarım dosya "başarılı" sayılıyor ve hemen önizleme için parse ediliyor.

**Düzeltme:** Dosya boyutunun sabitlenmesini ve dosyanın exclusive açılabilmesini bekle; ek olarak dosyanın sonunda `EOF` satırı olup olmadığını kontrol et.

#### Ö5. Genel bir exception yakalayıcı yok

**Konum:** [Macria/App.xaml.cs](Macria/App.xaml.cs)

**Sorun:**
- `DispatcherUnhandledException`, `AppDomain.UnhandledException` ve `TaskScheduler.UnobservedTaskException` tanımlı değil.
- 17 `async void` handler'ın çoğu try/catch ile korunuyor. Buna rağmen korumasız bir yerde çıkan hata uygulamayı iz bırakmadan kapatır.

**Düzeltme:** Bu üç olayı yakalayıp hatayı loglayan bir handler ekle. Ucuz ve etkili bir güvenlik ağı.

#### Ö6. 131 boş `catch { }` bloğu var

**Dağılım:** `MainWindow.xaml.cs` 51, `MainWindow.Profil.cs` 20, `MainWindow.PartBodyAutomaticAbTesti.cs` 6, `CatiaColorTargetService.cs` 6, `CatiaConnect.cs` 5, diğer dosyalar daha az.

**Sorun:** COM tarafında bir kısmı bilinçli fallback. Ama kritik yollarda (açma, kapatma, export) sahada teşhisi zorlaştırıyor.

**Düzeltme:** En azından export ve kapatma yollarındaki catch bloklarına tek satırlık bir log ekle.

### 3.2 Performans

#### Ö7. CATIA COM çağrıları UI thread'inde yapılıyor

**Sorun:**
- Maliyet ölçümü, profil teşhisi ve export'taki `PLMOpenInNewWindow`, ölçüm ve yüz sayma çağrıları senkron çalışıyor. Bu çağrılar sürerken pencere "Yanıt vermiyor" durumuna düşebilir.
- Ayrıca `_repRefs` içindeki nesneler MTA thread'inde (`Task.Run`) oluşturuluyor ama STA'daki UI thread'inden kullanılıyor. Bu, her çağrıda apartment'lar arası marshaling ek yükü demek **(doğrulanmalı)**.

**Düzeltme:** Tüm CATIA COM çağrılarını yürüten tek bir STA iş thread'i. Bu büyük bir mimari değişiklik, bu yüzden **Karar Bekliyor**.

#### Ö8. Snap noktaları hazırlanırken tüm çizgi çiftleri karşılaştırılıyor (O(n²))

**Konum:** [Macria/OnizlemeWindow.xaml.cs:1511-1519](Macria/OnizlemeWindow.xaml.cs#L1511)

**Sorun:**
- Bu hesap pencere açılışında ([151](Macria/OnizlemeWindow.xaml.cs#L151)) ve **her edit işleminden sonra** ([720](Macria/OnizlemeWindow.xaml.cs#L720)) tekrar çalışıyor.
- 10.000 çizgili bir DXF'te yaklaşık 50 milyon kesişim testi demek.
- Köşe adayı hesabında zaten bir üst sınır var (`EnCokMouseKoseCizgisi`, [337](Macria/OnizlemeWindow.xaml.cs#L337)); snap hesabında yok.

**Düzeltme:** Aynı üst sınırı snap'e de uygula ya da hesabı yalnızca Mesafe modu açıldığında yap. Daha kalıcı çözüm bir grid index.

#### Ö9. DXF, liste seçimi değiştiğinde UI thread'inde senkron parse ediliyor

**Konum:** [Macria/MainWindow.xaml.cs:1475](Macria/MainWindow.xaml.cs#L1475), [Macria/MainWindow.DxfDwgFiles.cs:213](Macria/MainWindow.DxfDwgFiles.cs#L213)

**Sorun:** Büyük bir DXF seçildiğinde UI donuyor. `DxfOkuyucu` UI'a bağımlı olmayan saf bir sınıf, arka planda rahatça çalışabilir.

**Düzeltme:** Parse'ı `Task.Run` ile yap. Kullanıcı başka bir satıra geçerse eski sonucu iptal et ya da yok say.

#### Ö10. Toplu export'ta her parça için yaklaşık 10 saniyelik sabit bekleme var

**Konum:** [Macria/MainWindow.xaml.cs:4044](Macria/MainWindow.xaml.cs#L4044) ve devamı (`ExportOneIc`, `btnExportAll_Click`)

**Sorun:** Beklemeler: 2500 + 600 + `PanelBekleme` (3000) + 600 + 400 + 1000 + 1500 + 800 ms. 100 parçalık bir işte yalnızca beklemeler yaklaşık 17 dakika tutuyor.

**Düzeltme:** Sabit beklemeler yerine koşul kontrolü (polling). Mevcut süreler üst sınır olarak korunur; böylece AGENTS.md'deki fallback zinciri bozulmaz. Mutlaka CATIA olan makinede test edilmeli.

### 3.3 Dağıtım ve dokümantasyon

#### Ö11. OCCT viewer DLL'i Macria sürecine yükleniyor; dokümantasyon tersini söylüyor

**Konum:** [Macria/OcctViewerNative.cs:84](Macria/OcctViewerNative.cs#L84), [Macria/Macria.csproj:25](Macria/Macria.csproj#L25), [GeometryEngineRuntime/README.md](GeometryEngineRuntime/README.md)

**Sorun:**
- `OcctViewerNative.cs`, `Macria.GeometryViewer.dll`'i `LoadLibraryEx` ile yüklüyor.
- Buna karşılık csproj yorumu "never loaded into Macria" diyor. `GeometryEngineRuntime/README.md` de "Macria işlemine native DLL yüklemez" diyor.
- Risk: Native bir çökme Macria'yı da kapatır. OCCT'yi `FreeLibrary` ile boşaltmak da sorun çıkarabilir **(doğrulanmalı)**.

**Düzeltme:** Dokümantasyonu düzelt. Viewer'ın süreç içinde kalıp kalmayacağı **Karar Bekliyor**.

#### Ö12. "Taşınabilir tek dosya" artık tek dosya değil

**Konum:** [Macria/Macria.csproj](Macria/Macria.csproj) (`ExcludeFromSingleFile`), [README.md](README.md) ("Taşınabilir sürüm" bölümü)

**Sorun:** `GeometryEngine\` klasörü `ExcludeFromSingleFile` ile tek dosyanın dışında tutuluyor. README ise yalnızca `Macria.exe`'nin yeterli olduğunu söylüyor. Kullanıcı sadece exe'yi kopyalarsa GeometryLab ve harici STEP özellikleri çalışmaz.

**Düzeltme:** README'yi güncelle ve publish klasörünü bütün olarak dağıt (ör. zip).

### 3.4 Kod kalitesi

#### Ö13. Yaklaşık 5.000 satırlık ölü kod var

**Sorun:**
- Aşağıdaki 11 teşhis ve A/B test partial dosyasındaki 15 `_Click` handler'a ne XAML'den ne de koddan ulaşılabiliyor.
- Bunlar HEAD'deki XAML'de de bağlı değil. `obj/.../MainWindow.g.cs` içinde hâlâ görünmeleri eski bir build'den kalma.

| Dosya | Ulaşılamayan handler |
|---|---|
| `MainWindow.NativeResetAbTesti.cs` | `NativeResetAbTesti_Click`, `NativeResetSonrasiOku_Click` |
| `MainWindow.PartBodyAutomaticAbTesti.cs` | `PartBodyAutomaticAbTesti_Click` |
| `MainWindow.ResetPropertyKontrolluTeshisi.cs` | `ResetPropertyKontrolluTeshisi_Click` |
| `MainWindow.FaceSelectionContextRoundTripTeshisi.cs` | `FaceSelectionContextRoundTripTeshisi_Click` |
| `MainWindow.SelectedElementYetenekTeshisi.cs` | `SelectedElementYetenekTeshisi_Click` |
| `MainWindow.RenkEnvanteriTesti.cs` | `RenkEnvanteriTesti_Click` |
| `MainWindow.RenkHedefiTesti.cs` | `RenkTestOccurrence_Click`, `RenkTestPartBody_Click`, `RenkTestProduct_Click` |
| `MainWindow.RenkDurumuTeshisi.cs` | `RenkDurumuTeshisi_Click` |
| `MainWindow.TekReferansPartBodyRenkTesti.cs` | `TekReferansPartBodyRenkTesti_Click`, `TekReferansPartBodyRenginiGeriYukle_Click` |
| `MainWindow.PartBodyHedefKesfi.cs` | `PartBodyHedefKesfi_Click` |
| `MainWindow.TopluPartBodyHedefCozumleme.cs` | `TopluPartBodyHedefCozumleme_Click` |

**Düzeltme:** Önce bu dosyalardaki yardımcı metotların canlı kodda (ör. `Renklendirme2`) kullanılıp kullanılmadığını kontrol et. Kullanılmıyorsa ayrı bir dala taşı ya da sil.

#### Ö14. "Parçayı aç, işlem yap, kapat" akışı 5 yerde kopyalanmış

**Konum:** `PLMOpenInNewWindow` 5 kez, `ActiveWindow.Close` 6 kez geçiyor:
- [Macria/MainWindow.xaml.cs:4042](Macria/MainWindow.xaml.cs#L4042) (`ExportOneIc`)
- [Macria/MainWindow.Maliyet.cs:666](Macria/MainWindow.Maliyet.cs#L666) (`ParcayiOlc`)
- [Macria/MainWindow.Profil.cs:454](Macria/MainWindow.Profil.cs#L454) ve [1276](Macria/MainWindow.Profil.cs#L1276)
- [Macria/MainWindow.GeometryLab.cs:47](Macria/MainWindow.GeometryLab.cs#L47)

**Sorun:** Bekleme, kapatma ve hata yolu kopyalar arasında farklılaşmış. Ö1'deki hatalar bu farklılaşmanın sonucu.

**Düzeltme:** Tek bir yardımcı metot, ör. `ParcaAcikkenCalistir(repRef, işlem)`, içinde try/finally ve bayrakla. Bu Ö1'i de tek yerden çözer. Büyük bir refactor gerektirmez.

`MainWindow.xaml.cs` (4.989 satır) ve `MainWindow.Profil.cs` (1.633 satır) çok büyük. Ancak AGENTS.md gereksiz refactor'ı yasakladığı için yalnızca Ö14 kapsamında sınırlı bir sadeleştirme öneriliyor.

---

## 4. 🟡 Küçük

- **Süre logu birden fazla kez yazılıyor:** Tek parça DXF'te hem try/catch içinde hem `finally`'de yazıldığı için süre en az iki kez loglanıyor. [3961](Macria/MainWindow.xaml.cs#L3961), [3989](Macria/MainWindow.xaml.cs#L3989), [3994](Macria/MainWindow.xaml.cs#L3994)
- **Pano ezilir:** `SendText` kullanıcının panosunu siliyor ve eski içeriği geri koymuyor. [4972](Macria/MainWindow.xaml.cs#L4972)
- **Yanıltıcı yorum ve log:** `SetExporting` yorumu ve log mesajı "fiziksel girdi kilitli" diyor. Oysa `BlockInput` veya bir hook yok; yalnızca Macria'nın kendi kontrolleri devre dışı bırakılıyor.
- **UI thread'inde `Thread.Sleep`:** `MainWindow.xaml.cs`'te 5, `MainWindow.Profil.cs`'te 1 kez.
- **Kaynak ve büyüme:** `_onayCts` (CancellationTokenSource) dispose edilmiyor. Log koleksiyonu da sınırsız büyüyor ([398](Macria/MainWindow.xaml.cs#L398)).
- **Ayarlar atomik yazılmıyor:** [Macria/Ayarlar.cs](Macria/Ayarlar.cs) içindeki `Kaydet` dosyayı doğrudan `File.WriteAllLines` ile yazıyor ve hatayı `catch { }` ile yutuyor. Yazma yarıda kesilirse öğretilmiş Save As konumu dahil tüm ayarlar sessizce kaybolur. **Düzeltme:** Geçici dosyaya yaz, sonra `File.Replace`.
- **Teşhis dökümleri Masaüstüne yazılıyor:** [MainWindow.xaml.cs:4433](Macria/MainWindow.xaml.cs#L4433), [MainWindow.Maliyet.cs:879](Macria/MainWindow.Maliyet.cs#L879), [MainWindow.Profil.cs:1095](Macria/MainWindow.Profil.cs#L1095). Masaüstü genelde OneDrive ile senkronize, yani parça ve PLM bilgisi buluta gidebilir. **Düzeltme:** `%LocalAppData%\Macria\Logs` klasörünü kullan.
- **Blok iç içeliğinde döngü kontrolü yok:** Derinlik sınırı 8, ama kendine referans veren bloklar için ayrı bir kontrol yok. Kendini birçok kez INSERT eden bir blok üstel büyüme yaratır. [DxfOkuyucu.cs:310](Macria/DxfOkuyucu.cs#L310). **Düzeltme:** Blok adı yığınında ziyaret kontrolü ve bir entity sayısı üst sınırı.
- **`GeometryEngineRuntime` içeriği (doğrulanmalı):**
  - ffmpeg (`avcodec-57`, `avformat-57`, `avutil-55`, `swscale-4`) ve `openvr_api.dll` gerçekten gerekli mi?
  - OCCT ve FFmpeg lisans (LGPL) bildirimleri pakete dahil değil.
  - Csproj'daki `**\*` deseni `README.md`'yi de publish çıktısına kopyalıyor.
- **Sürüm tutarsızlığı:** README v1.10.4, csproj 1.11.2, Obsidian notları v1.10.4 diyor.
- **Kullanılmayan dosya:** `Macria/Assets/coffee-walk-sheet.png` repoda duruyor ama csproj'da yok.
- **Dış ağ erişimi:** `KurServisi` döviz kurları için `api.frankfurter.app` ve `open.er-api.com` adreslerine istek atıyor. Kurumsal proxy arkasında bu istekler başarısız olabilir. Bilgi amaçlı not.

---

## 5. Olumlu tespitler

- Kodda sabit mutlak yol yok; her yer `Environment.SpecialFolder` kullanıyor.
- Debug kısayolları (F9, F10) `#if DEBUG` ile korunuyor.
- `GeometryLabProcessAdapter`'da timeout, kill tree, dispose ve asenkron stdout/stderr okuma düzgün.
- Motor konumu doğru sırayla çözülüyor: paketlenmiş motor, `MACRIA_GEOMETRY_ENGINE_PATH` ortam değişkeninin önüne geçiyor.
- DXF edit tarafı HashSet/Dictionary kullanıyor ve kapsamlı regresyon testleriyle korunuyor.
- Çizim StreamGeometry ve Freeze ile yapılıyor. Köşe adayı hesabında bir üst sınır var.
- Timer ve olay abonelikleri düzgün temizleniyor. `.Result` veya `.Wait()` ile senkron bekleme yok.

---

## 6. Önerilen düzeltme sırası

1. **K1 ve K3:** Onay ve kaydetme penceresi aramasına CATIA süreç filtresi ekle. İki fonksiyonla sınırlı bir değişiklik ve en yüksek riski kapatıyor.
2. **K2 ve Ö1:** `hSave` kontrolünü ekle, parça kapatmayı bayrak ve `try/finally` ile yap (Profil'deki kalıp). Bu adımı Ö14'teki ortak yardımcı metotla birlikte yapmak mantıklı.
3. **Ö2:** Tek bir "CATIA meşgul" koruması.
4. **Ö3 ve Ö4:** Toplu export'ta üzerine yazma onayı ve dosyanın tamamen yazılmasını bekleme.
5. **Ö5 ve Ö6:** Genel exception handler ve kritik catch bloklarına log.
6. **Performans:** Önce Ö8 (snap üst sınırı) ve Ö9 (arka planda DXF parse), bunlar düşük riskli. Ardından Ö10 (sabit beklemeleri polling'e çevirmek); bunu mutlaka CATIA olan makinede test et.
7. **Temizlik:** Ö13 (ölü teşhis kodu) ve dokümantasyon düzeltmeleri (Ö11, Ö12, sürüm).

**Karar Bekliyor:**
- **Ö7:** CATIA COM çağrıları için ayrı bir STA thread kurulsun mu?
- **Ö11:** OCCT viewer süreç içinde mi kalsın, ayrı bir sürece mi taşınsın?

AGENTS.md gereği her düzeltmeden sonra build kontrol edilmeli ve CATIA gerektiren doğrulama iş makinesinde yapılmalı. Kullanıcı kabulünden sonra ilgili Obsidian notları güncellenmeli: `02 - CATIA Entegrasyonu`, `03 - DXF ve Sheet Metal`, `06 - Hatalar ve Çözümler`, `99 - Yapılacaklar`.
