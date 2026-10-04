# STEP / STP Analizi — Sekme ve Araç Çubuğu Düzeni (Plan)

- **Durum:** Plan kabul edildi (2026-10-04); uygulama başlamadı. Aşamalar sırayla, her biri ayrı commit ve elle deneme listesiyle uygulanır.
- **Kapsam:** Obsidian `99 - Yapılacaklar.md` notları 4, 12, 16–22 ve (E) "Yeniden analiz et".
- **Kullanıcı kararları (2026-10-04):**
  - Tek parçalı STEP de motorun parça sınıfına göre dağıtılır.
  - Kullanıcının "13" dediği madde 12'dir.

## Bağlam

- **Bugünkü düzen:** Üç sekme var.
  - Profiller: dört filtre kutusu (Kesin / İnceleme gerekli / Liste dışı / Tanımsız) ve kendi düğmeleri.
  - Saclar: Lazer / Şalama alt sekmeleri, iki filtre kutusu ve kendi düğmeleri.
  - Kontrol gerekli: iki filtre kutusu ve kendi düğmeleri.
- **Sağ panel:** Her sekmenin kendi paneli ve kendi 3B görüntüleyicisi var (`profilOnizleme`, `_sacOnizleme3B`, `kontrolOnizleme`).
- **Sorunlar:**
  - Filtre kutuları satırları gizliyor (not 4).
  - Kontrol gerekli'de karar verilemiyor (17).
  - "Liste dışı" yalnız Profiller'de (12, 18).
  - Düğmeler sekmeden sekmeye farklı yerde ve adda (19).
  - Profillerdeki otomatik "Liste dışı" (`Excluded`) yalnız kullanıcı kararı değil. Başarısız analiz, şema hatası, çok gövdeli dosya ve CATIA'nın sac doğruladığı tek parçalı dosya da buraya düşüyor.
  - Tek parçalı STEP her zaman Profiller'de dosya satırı oluyor, sac olsa bile.
- **Hedef:** Beş sekme: Profiller | Saclar | Kontrol gerekli | Tanımsız | Liste dışı. Bunlarla birlikte ortak bir araç çubuğu ve ortak bir sağ panel.

## Motor sınıfından sekmeye (öneri)

Motor parça sınıfları `Sheet` / `Profile` / `ReviewRequired` / `Other`. Bunların gerekçeleri iki farklı türden durumu karıştırıyor:

1. **Motor bir şey buldu ama emin değil** → Kontrol gerekli.
2. **Motor hiçbir şey bulamadı ya da bakamadı** → Tanımsız.

Ayrımı metinden çıkarmak kırılgan olacağı için motor her parçaya kısa bir **sınıf kodu** (`classificationCode`) yazacak (Aşama 1).

| Durum (kod) | Sekme | Durum etiketi |
|---|---|---|
| `Profile` (`HollowSection`, `ProcessedProfile`) | Profiller | Profil |
| `Sheet` + CATIA sac unsuru ya da kullanıcı onayı | Saclar | Sac (yeşil) |
| `Sheet`, onaylanmamış | Saclar | Onay gerekli (amber) |
| Profil tanındı ama üretime uygun değil (bugünkü "İnceleme gerekli") | Kontrol gerekli | İnceleme gerekli |
| `ReviewRequired`: sac ve profil ikisi de tanıdı (`SheetProfileConflict`) | Kontrol gerekli | Sac / profil çakışması |
| `ReviewRequired`: sac tanındı, açınım yok (`FlatPatternFailed`) | Kontrol gerekli | Açınım yok |
| `ReviewRequired`: çok gövdeli (`MultiSolid`) | Kontrol gerekli | Çok gövdeli |
| `Other`: dolu kesit (`SolidBar`), kalınlık > et genişliği (`ThickerThanMaterial`: mil, halka, somun, burç) | Kontrol gerekli | **Diğer** |
| `Other`: hiçbir tanıyıcı tanımadı (`NotRecognized`) | Tanımsız | Tanınmadı |
| `ReviewRequired`: desteklenmeyen yüz tipi (`UnsupportedFaces`, B-spline vb.), sac analizi başarısız (`SheetAnalysisFailed`) | Tanımsız | Tanıyıcı değerlendiremedi |
| `ReviewRequired`: geçersiz geometri (`InvalidGeometry`), süre aşımı (`TimedOut`), solid yok (`NoSolid`) | Tanımsız | Geçersiz geometri / Süre aşıldı / Solid yok |
| Analiz başarısız, iptal edildi, şema okunamadı (dosya satırı) | Tanımsız | Analiz başarısız / İptal |
| Kullanıcı "Liste Dışına Çıkar" | Liste dışı (her sekmeden) | önceki etiket + "Liste dışı" |

- **"Diğer" Kontrol gerekli'de kalır:** Bunlar motorun olumlu olarak tanıdığı ama sac ya da profil olmayan parçalar (mil, halka, somun, burç). Kullanıcının karar vermesi gerekiyor: Liste dışı, ya da gerekirse Sac / Profil.
- **Tanımsız:** Motorun bakamadığı ya da hiçbir kanıt bulamadığı parçalar.
- **Eski motor çıktısı** (kod alanı yok; eski .macria'lar): Macria bilinen Türkçe gerekçe başlangıçlarından aynı kodu çıkarır. Eşleşmeyen `ReviewRequired` → Kontrol gerekli; eşleşmeyen `Other` → Kontrol gerekli / Diğer.

## Model (WPF'siz, testli)

- **Tek parçalı STEP:** Şema 1.2 çıktısında `parts` olan her STEP `MacriaProjeSatirlari.MontajSatirlari` yolundan geçer: sac → Saclar, profil → Profiller. Dosya satırı yalnız şunlarda kalır:
  - eski şema (`parts` yok),
  - analiz başarısız / iptal.

  Sonuç: CATIA'nın sac doğruladığı tek parçalı dosya otomatik "Liste dışı"na düşmez; Saclar'a gider.
- **Sekme kuralı:** Yeni `AnalizSekmesi` enum'u (Profiller, Saclar, KontrolGerekli, Tanimsiz, ListeDisi) ve yukarıdaki tabloyu uygulayan `SekmeKurallari` (her iki satır türü için saf fonksiyon).
- **Liste dışı bayrağı:** "Liste dışı" kategori değil, ayrı bir bayrak olur: `ListeDisi` + not, her iki satır türünde.
  - `ListeDisinaCikar(not)` / `ListeyeGeriAl()`.
  - Geri Al bayrağı kaldırır; satır geldiği sekmeye döner (kategori kararı korunduğu için).
  - Bugünkü `ExcludeFromList` bu bayrağa yönlenir.
  - Otomatik `Excluded` kalkar; tablodaki Tanımsız / Kontrol gerekli karşılıkları kullanılır.
- **Kararlar (satır türüne göre):**
  - **Profil satırı:**
    - Profil Olarak Onayla → `ConfirmAsProfile`; kesitsizse bugünkü elle kutu profil penceresi (`ConfirmManualHollowProfile`).
    - Kontrol Gerekliye Al → bugünkü `MoveToReview`.
    - Liste Dışına Çıkar.
    - Otomatik Karara Dön.
  - **Sac / montaj satırı:**
    - Sac Olarak Onayla → `ApproveAsSheet`, motor DXF'i varsa.
    - Kontrol Gerekliye Al → `MoveToReview`.
    - Liste Dışına Çıkar.
    - Otomatik Karara Dön.
  - **Sınır:** Sac / profil çakışmasındaki bir montaj satırını "Profil Olarak Onayla" yapmak profil verisi gerektirir. Bu, Aşama 2'de görünmez; gerekirse sonraki bir adımda satıra profil temsili eklenir.
- **Karma listeler için ortak arayüz:** Kontrol gerekli / Tanımsız / Liste dışı sekmeleri iki satır türünü birlikte gösterir. Her iki sınıf ortak bir `IAnalizSatiri` arayüzü uygular:
  - kimlik: `Sekme`, `KaynakYolu`, `ParcaGosterimi`, `AdetGosterimi`;
  - görünüm: `TurGosterimi` (Profil / Sac / Diğer / —), `OlcuGosterimi` (kesit ya da kalınlık), `DurumEtiketi`, `KararGosterimi`, `AciklamaGosterimi`;
  - CATIA: `CatiaQuantityDisplay`, `CatiaMatchDisplay`;
  - sağ panel için `Ayrintilar` (etiket / değer listesi).

  `_tumSatirlar` iki koleksiyondan beslenen birleşik liste olur; her sekme bunun süzülmüş bir görünümüdür.
- **.macria şeması 1.1** (ara sürüm; eski Macria salt-okunur açar):
  - `ListeDisi` kararı bayrak olarak her iki hedefte geçerli.
  - Profil hedefinde `Kontrole` (`Incelemeye`'nin yeni adı).
  - **1.0 okuma:**
    - profil `ListeDisi` → bayrak,
    - `Incelemeye` → `Kontrole`,
    - `parca: null` (eski tek parçalı dosya satırı) → kaynağın tek parçasına bağlanır (tek aday varsa).
  - Eşlenemeyen karar bugünkü gibi `eslenemeyenKararlar`'da kalır.

## Arayüz

- **Sekmeler:** `Profiller (n) | Saclar (n) | Kontrol gerekli (n) | Tanımsız (n) | Liste dışı (n)`.
  - Bütün filtre kutuları, Tümünü Göster/Gizle ve `GeometryLabExternalStepFilterState` kalkar.
  - Lazer / Şalama alt sekmeleri Saclar'da kalır.
  - Boş sekmeler de görünür (sayı 0).
- **Tablolar:**
  - Profiller: bugünkü profil sütunları.
  - Saclar: bugünkü sac sütunları; Durum sütunu renkli etiket (Sac yeşil, Onay gerekli amber).
  - Kontrol gerekli / Tanımsız / Liste dışı: ortak sütunlar (Durum etiketi, Parça, Adet, Tür, Ölçü, CATIA Adedi, Eşleşme, Karar, Açıklama).
  - `TabloSutunGenisligi` hepsine uygulanır.
- **Ortak araç çubuğu:** Sekmelerin üstünde, her sekmede aynı yerde ve sırada.
  - `Excel'e Aktar · Üretim Paketi Hazırla · CATIA ile Karşılaştır · Dosyayı Aç · [STEP Olarak Dışa Aktar]`.
  - Yalnız Saclar'da: `DXF Üret · [DXF Düzenle]`.
  - Kararlar: `Profil Olarak Onayla · Sac Olarak Onayla · Kontrol Gerekliye Al · Liste Dışına Çıkar · Otomatik Karara Dön`. Liste dışı sekmesinde bunların yerine `Geri Al`.
  - Köşeli parantezdekiler sonraki aşamalarda gelir.
  - Hangi düğmenin hangi sekmede görüneceği ve seçime göre etkinliği, testli bir saf kural olur (`AnalizAracDurumu`, `Step3BAracDurumu` gibi).
  - Görünürlük: Profil Olarak Onayla Profiller'de görünmez; Sac Olarak Onayla yalnız Saclar (Onay gerekli satırlar) ve Kontrol gerekli'de görünür.
  - "Kesin Profile Aktar" → Profil Olarak Onayla; "İncelemeye Al" → Kontrol Gerekliye Al.
  - Excel her sekmede açık sekmenin satırlarını yazar: Profiller ve Saclar için bugünkü yazıcılar, diğer sekmeler için ortak sütunlarla `RaporModel` / `ExcelYazici`.
- **Ortak sağ panel:** Sekmelerin sağında tek panel.
  - Tek bir `Step3BPaneli` (üç görüntüleyici yerine bir tane; bellek de azalır) açık sekmenin seçimini gösterir.
  - Saclar'da 2B | 3B geçişi.
  - "Seçili Parça" ayrıntıları `Ayrintilar`'dan her sekmede gösterilir.
  - Büyük Aç bu paneli izler; `BuyukOnizlemeSekmesi` mantığı sadeleşir.
- **Geri düğmesi:** "← Dosya Analiz Merkezi" düğmesine `SecondaryButton` stili verilir.

## Aşamalar

**Aşama 1 — Motor sınıf kodu.** GeometryLab, sonra Macria paketi.

- **Yapılacaklar:**
  - `AssemblyPartRecord.classificationCode`; JSON'da `parts[].classificationCode` (şema 1.2'de ek alan).
  - `PopulatePartClassification`'daki her `decide` kendi kodunu yazar.
  - Motor `2026.10.5.1`; 52 dosyalık regresyon: yeni alan dışında fark sıfır.
  - Macria DTO'su alanı okur; eski çıktı için gerekçe metninden kod çıkarılır (testli).
- **Elle deneme:**
  1. montaj-1 ve WGRV analizi: sekmeler ve sınıflar değişmemeli.
  2. Kayıtlı WGRV .macria'sı "eski motor" sorusunu göstermeli; Kayıtlı sonuçla aç çalışmalı.

**Aşama 2 — Sekmeler, model ve ortak araç çubuğu.** Macria; büyük olduğu için alt commit'ler: model / .macria 1.1 / XAML / araç çubuğu.

- **Yapılacaklar:**
  - Beş sekme, bayrak olarak Liste dışı, tek parçalı STEP'in parça yolundan geçmesi, `SekmeKurallari`, `IAnalizSatiri`.
  - Durum etiketleri, ortak araç çubuğu ve kararlar, .macria 1.1 ve 1.0 okuma, geri düğmesi stili.
  - Sağ paneller bu aşamada sekme başına kalır.
- **Testler:**
  - Sınıf tablosu.
  - 1.0 proje dosyasının 1.1'e dağılımı (örnek dosya).
  - 52 STEP'te tek parçalı profillerin dosya yolu ve parça yolunda aynı göründüğü (`RealProjectRoundTrip` benzeri).
  - Araç çubuğu kuralı.
- **Elle deneme:**
  1. montaj-1: Profiller 3 satır (yalnız kesinler); Saclar'da 55RS100111-3 / -9 / -1 ve "Onay gerekli" amber etiket; 55RS100111-2 Kontrol gerekli'de.
  2. WGRV: halka, somun ve burçlar Kontrol gerekli'de "Diğer"; B-spline / desteklenmeyen yüzlü somunlar (nut_iso_4035, M5 Somun) Tanımsız'da.
  3. Hiçbir sekmede filtre kutusu ve Tümünü Göster/Gizle olmamalı; sekme başlıklarında sayılar doğru olmalı.
  4. Her sekmede araç çubuğu aynı yerde; anlamsız karar düğmeleri görünmemeli.
  5. Kontrol gerekli'den bir parçayı Sac Olarak Onayla → Saclar'a geçmeli. "İnceleme gerekli" bir profili Profil Olarak Onayla → Profiller'e geçmeli.
  6. Saclar'dan bir parçayı Liste Dışına Çıkar → Liste dışı'nda görünmeli; Geri Al → Saclar'a dönmeli. Aynısını Kontrol gerekli ve Profiller'den deneyin.
  7. Tek parçalı bir sac STEP'i (ör. `claude-test\55RS100111-3`) analiz edin: Saclar'da görünmeli, DXF önizlemesi açılmalı.
  8. Eski `WGRV004423 A.macria` ve `B-Rep Calisma A.macria`: kararlar yeni sekmelere doğru dağılmalı (Liste dışı ve İncelemeye alınanlar dahil). Kaydet → yeniden aç aynı olmalı.
  9. "← Dosya Analiz Merkezi" düğmesi koyu temada olmalı.

**Aşama 3 — Ortak sağ panel.** Macria.

- **Yapılacaklar:** Tek `Step3BPaneli` + Saclar 2B, ortak "Seçili Parça", Büyük Aç bu paneli izler. `profilOnizleme`, `kontrolOnizleme` ve `_sacOnizleme3B` kalkar; `TumStep3BPanelleri()` tek panele iner.
- **Elle deneme:**
  1. Her sekmede seçilen parça aynı panelde 3B görünmeli; sekme değişince panel o sekmenin seçimini göstermeli.
  2. Saclar'da 2B / 3B geçişi çalışmalı, Büyük Aç seçimi izlemeli.
  3. Seçili Parça ayrıntısı her sekmede dolu olmalı (profil: kesit / boy; sac: kalınlık / büküm / delik; diğer: açıklama).
  4. WGRV'de Görev Yöneticisi'nde bellek öncekinden az olmalı.

**Aşama 4 — DXF Düzenle** (not 21, sonraki aşama).

- **Yapılacaklar:** Saclar'da seçili satırın motor DXF'i mevcut `OnizlemeWindow` (DXF Edit Mode) ile açılır.
  - Kaydedince düzenlenmiş DXF projede ayrı tutulur (`kaynaklar/<id>/dxf-duzenli/`).
  - DXF Üret ve 2B önizleme düzenlenmiş sürümü kullanır; "Otomatik Karara Dön" benzeri bir "Motor DXF'ine dön" seçeneği olur.
  - .macria şeması 1.2.
- **Elle deneme:** DXF'i düzenleyip kaydedin → 2B önizleme düzenlenmiş olmalı → DXF Üret düzenlenmişi yazmalı → proje kaydet / aç sonrası düzenleme korunmalı.

**Aşama 5 — STEP Olarak Dışa Aktar** (not 27, sonraki aşama).

- **Yapılacaklar:**
  - Motora yeni bir komut: `--parca-step <localId> --cikti <dosya>`. Tek parçayı XDE'den yazar; süreç dışında çalışır.
  - Profiller'de seçili ya da bütün profiller `<hedef>\Profil-STEP\<ParçaNo>_<X>Adet.stp` olarak yazılır (ör. `246501_4Adet.stp`).
  - Ad çakışmasında `_2` eki; geçersiz dosya adı karakterleri temizlenir.
- **Elle deneme:** montaj-1 profilleri → 3 dosya, adlar ve adetler doğru olmalı; dosyalar CATIA'da / 3B'de açılmalı.

**Aşama 6 — Yeniden analiz et** (E, sonraki aşama).

- **Yapılacaklar:** Proje araç şeridinde "Yeniden Analiz Et": seçili kaynak(lar)ı ya da hepsini mevcut ayarlarla yeniden tarar. Kararlar .macria'daki yeniden tarama eşlemesiyle (`productId`, sonra ad) korunur; eşlenemeyenler raporlanır. Ayar değişikliği (süre sınırı, iş parçacığı) için de kullanılır.
- **Elle deneme:** WGRV'de birkaç karar verip Yeniden Analiz Et → kararlar korunmalı, konsolda süre ve "N karar uygulandı / M eşlenemedi" görünmeli.

## Kritik dosyalar

- **Macria:**
  - `MainWindow.xaml` (sekmeler, araç çubuğu, sağ panel),
  - `MainWindow.ExternalStepProfiles.cs`, `MainWindow.MontajParcalari.cs`, `MainWindow.ExternalStepPreview.cs`, `MainWindow.Proje.cs`,
  - `GeometryLabStepProfileListItem.cs` (Excluded / filtre durumu, karar yöntemleri), `MontajParcasi.cs`,
  - `MacriaProjeSatirlari.cs`, `MacriaProje.cs` (şema 1.1),
  - yeni `SekmeKurallari.cs`, `AnalizAracDurumu.cs`, `IAnalizSatiri`.
- **GeometryLab:** `AssemblyStructure.cxx` (`classificationCode`), `GeometryEngine.cxx` (JSON), Aşama 5 için `main.cxx`.
- **Testler:** `GeometryLabAdapter.Tests` (kurallar, .macria 1.0 → 1.1, gerçek STEP karşılaştırmaları), GeometryLab `SheetMetalTests` (sınıf kodları).

## Riskler

- **Büyük XAML değişikliği:** Aşama 2'yi alt commit'lere böleceğim; her alt commit derlenip testlenecek.
- **Tek parçalı STEP'in yolu değişiyor:** Profil görünümü değişebilir. 52 dosyalık karşılaştırmayla sıfır fark kanıtlanmadan Aşama 2 bitmeyecek.
- **Eski projelerde gerekçe metninden kod çıkarma:** Eşleşmeyen durumlar güvenli tarafa, Kontrol gerekli'ye düşer.
- **Ortak sağ panel** (Aşama 3) görüntüleyici yaşam döngüsünü değiştiriyor. FreeLibrary sabitlemesi yerinde; kapanışta çökme kaydı yeniden kontrol edilecek.
