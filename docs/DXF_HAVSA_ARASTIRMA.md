# DXF Havşa / İmbus Delik Araştırması

- **Tarih:** 2026-09-27
- **Kod sürümü:** 1.11.2, branch `enes-yesiloz`
- **Kapsam:** Bu bir araştırmadır. **Hiçbir kod değiştirilmedi.**
- **Kaynak:** Yalnızca kod okundu. CATIA davranışına ilişkin hiçbir iddia çalıştırılarak doğrulanmadı.

**İşaretler**

- **⚠️ Belirsiz / doğrulanmalı:** Koddan kanıtlanamayan; CATIA'da denenmesi gereken.
- **Karar Bekliyor:** Kullanıcı kararı gerekiyor (AGENTS.md).

---

## 0. Sorun ve hedef

**Gözlem (kullanıcıdan):**

- Havşa (countersink) ya da imbus/havşa başlı (counterbore) delikli sac parçada CATIA "Save As DXF":
  - Referans yüz **Top** seçiliyse delik **baş çapında** (büyük) çıkıyor.
  - **Bottom** seçiliyse **geçiş çapında** (küçük) çıkıyor.
- Başı parçanın diğer yüzünde olan bir delik için bu ilişki tersine döner. Bu çıkarım geometriktir; ⚠️ doğrulanmalı.

**Hedef:** Kesim DXF'inde her delik **geçiş (küçük) çapında** olmalı. Lazer delik geçişini keser; havşa ve imbus sonradan işlenir.

---

## 1. Mevcut akışta Top/Bottom seçimi kullanılıyor mu?

### 1.1 Bulgu: kullanılmıyor

Kodda referans yüz seçimine dair hiçbir şey yok.

- **Grep sonucu:** Tüm `.cs` ve `.xaml` dosyalarında `top`, `bottom`, `reference face`, `referans yüz`, `alt/üst yüz`, `mirror`, `ayna`, `flip` arandı. Eşleşmelerin tamamı UI yerleşimi (Margin, Rect.Bottom vb.), Excel kenarlık stili ya da `OcctStandardView.Top/Bottom` (3B görünüm). **Save As DXF paneliyle ilgili tek bir satır yok.**
- **`ExportOneIc` akışı** (`Macria/MainWindow.xaml.cs:4034`), panel açıldıktan sonra yalnızca iki şey yapar:
  1. `BukumBilgisiniKapat()` (`:4187`): Yalnızca "Bend Information" onay kutusuna dokunur ve bunu da yalnızca kutu işaretli görünüyorsa yapar.
  2. `SaveAsBas()` (`:4235`): Yalnızca "Save As" düğmesine tıklar. Konum görüntüden (`SaveAsBulucu`) ya da öğretilmiş koordinattan bulunur.
- **Ayarlar** (`Ayarlar.cs`): Referans yüz için bir ayar alanı yok. Öğretme ekranında (`SettingsWindow`) yalnızca "Konumu Öğret" (Save As) ve "Kutuyu Öğret" (Bend Information) var.

### 1.2 Şu an hangi yüzle export ediliyor?

- **Kod açısından:** Panel açıldığında **CATIA'nın o anki varsayılanı ya da kullanıcının en son seçtiği değer** ne ise o yüz kullanılır. Macria bu değeri ne okuyor ne de değiştiriyor.
- ⚠️ **Belirsiz:**
  - Panel ilk açılışta Top mu Bottom mu gelir?
  - Seçim oturum boyunca veya oturumlar arasında hatırlanır mı?
  - Seçim parçaya mı bağlıdır?
  - Bunlar koddan anlaşılamaz. Toplu export'ta her parça aynı şekilde mi export edildi, bilinmiyor. Kullanıcı bir kez elle Bottom seçtiyse sonraki parçalar da Bottom ile çıkıyor olabilir.

### 1.3 Panelde seçim yapmak neden zor?

`SaveAsBulucu.cs:9-13` ve `BukumBulucu.cs:9-12` yorumlarına göre panel **kendini kendisi çiziyor**:

- UIA ağacı yok.
- Metinli Win32 alt penceresi yok.

Bu yüzden Top/Bottom seçimi de ancak **görüntüden bulunarak ve tıklanarak** yapılabilir. `BukumBulucu`'nun kullandığı model burada da uygulanabilir:

- Seçeneğin yanındaki etiketin görüntüsü öğretilir.
- Seçimin durumu, kutu veya radyo düğmesi pikselinin parlaklık ve doygunluğundan okunur.

⚠️ Panelde bu seçeneğin radyo düğmesi mi, açılır liste mi, yoksa ayrı bir komut mu olduğu kodda görünmüyor. **Ekran görüntüsü gerekli** (§6, deneme D1).

---

## 2. İki DXF'ten "küçük çaplı" birleşik DXF üretmek mümkün mü?

### 2.1 Kısa cevap

**Mümkün**, ancak mevcut DXF altyapısına küçük bir genişletme gerekiyor.

- Okuma, kimlik ve bayt koruyarak güvenli yazma altyapısı hazır.
- Eksik olanlar:
  - CIRCLE yarıçapını değiştirme yeteneği.
  - İki çizimi hizalayan ve eşleştiren birleştirme mantığı.

### 2.2 Yeniden kullanılabilecekler

| Parça | Konum | Nasıl kullanılır |
|---|---|---|
| `DxfOkuyucu.Oku(yol, out hata) : DxfCizim?` | `DxfOkuyucu.cs:136` | İki dosyayı okur. CIRCLE için `DxfEntity.Tip="CIRCLE"`, `Merkez`, `Radius` ve **`KaynakKayit`** (kaynak kaydın bayt aralığı) dolu gelir (`Daire`, `:361`). ARC için `Merkez`, `Radius`, açılar dolu. |
| `DxfCizim.Entityler`, `MinX/MaxX/MinY/MaxY` | `DxfOkuyucu.cs:29` | Eşleştirme ve sınır kutusu. |
| `DxfKaynakBelge.Olustur(byte[], out hata)` | `DxfEditOturumu.cs:63` | Sıkı yapı doğrulaması + kayıt bazlı bayt aralıkları. Birleşik DXF, taban dosyanın baytları korunarak üretilebilir. |
| `DxfKaynakBelge.DuzenlemeleriUygula(...)` | `DxfEditOturumu.cs:406` | Yama motoru: silme, değer yaması, sona ekleme. Dokunulmayan bayt birebir korunur. **Yarıçap yaması için genişletilmesi gerekiyor** (§2.3). |
| `DxfKaynakBelge.DegerIkilisi(kayit, kod)` | `DxfEditOturumu.cs:382` (private) | Bir kayıttaki tek grup kodunun değer bayt aralığını bulur. Kod 40 (yarıçap) için doğrudan kullanılabilir. |
| `DxfEditOturumu.FarkliKaydet(yeniYol)` / `GeciciYaz` / `YedekOlustur` | `DxfEditOturumu.cs:736, 805, 770` | Atomik ve üzerine yazmayan kayıt. Birleşik çıktı için aynı kalıp kullanılmalı. |
| `Yerlesim.Halkalar(DxfCizim)` + `KapaliAlan` | `Yerlesim.cs:422, 481` (private static) | Tüm `Yollar`'ı (LINE, ARC, polyline, spline dahil) uç uca ekleyip kapalı halkalar kurar. **En büyük alanlı halka = dış kontur.** Hizalama için ideal. Şu an `private`; ayrı bir yardımcıya taşınmalı. |
| `DxfKoseGeometrisi.KonturBul` | `DxfKoseGeometrisi.cs:253` | Yalnızca LINE ile çalışır ve düzenlenebilirlik şartları çok sıkı. Hizalama için **uygun değil**; `Halkalar` daha genel. |
| `DxfAdi.Uret` | `Yerlesim.cs:13` | Nihai dosya adı değişmemeli; önizleme ve yerleşim dosyayı bu adla buluyor. |

### 2.3 Eksikler

1. **CIRCLE yarıçap yaması yok.**
   - `DegisiklikOnDogrula` (`DxfEditOturumu.cs:373`) yalnızca LINE kabul ediyor.
   - `DuzenlemeleriUygula` yalnızca 10/20/11/21 grup kodlarını yamalıyor.
   - Önerilen genişletme, dar kapsamlı: "mevcut CIRCLE'ın 40 kodunu yamala". Merkez değişmediği için yalnızca kod 40 değişir. Bu, silme + yeni kayıt eklemekten çok daha az riskli:
     - Handle, layer ve sahiplik (330) aynen kalır.
     - `$HANDSEED` değişmez.
   - Alternatif: Sil + yeni CIRCLE ekle. `YeniKayitOnDogrula` (`:318`) şu an yalnızca LINE/ARC ekliyor; CIRCLE için genişletme ve `AcDbCircle` alt sınıf işaretçisi gerekir. **Önerilmez.**
2. **Hizalama (ayna + öteleme) yok** (§3).
3. **Delik eşleştirme mantığı yok.** Gerekli olan: iki çizimde merkezleri tolerans içinde çakışan CIRCLE çiftlerini bulmak ve küçük yarıçapı seçmek.
4. **Delik CIRCLE değilse ne olacağı tanımsız.**
   - `DxfOkuyucu` LWPOLYLINE, POLYLINE, ELLIPSE ve SPLINE'ı **entity olarak kaydetmiyor**; yalnızca `Yollar`'a ekliyor (`HafifCokgen` → `cizim.Ekle(noktalar)`; `DxfEntity` yok). Bu tiplerle çizilmiş delikler eşleştirilemez ve yamalanamaz.
   - İki yarım ARC ile çizilmiş delikler de ayrıca ele alınmalı.
   - ⚠️ CATIA'nın delikleri CIRCLE olarak mı yazdığı gerçek dosyada görülmeli (D2).
5. **OCS (extrusion) desteği yok.**
   - `DxfOkuyucu` 210/220/230 kodlarını **yok sayıyor**; 10/20'yi dünya koordinatı kabul ediyor.
   - `DxfKaynakBelge.DuzlemDisi` (`DxfEditOturumu.cs:245`) 230≠1 olan kaydı düzenlemeye kapatıyor.
   - ⚠️ CATIA Bottom export'u aynalamayı `230=-1` (ters normal) ile yaparsa iki sonuç doğar:
     - Önizleme ve eşleştirme yanlış koordinatla çalışır (X aynalı görünmez).
     - Yama engellenir.
   - Bu durum kontrol edilmeden birleştirme yazılmamalı (D3).
6. **AGENTS.md kuralı:** "DXF değişikliği yalnızca açıkça tanımlanmış DXF Edit Modu içinde yapılabilir."
   - Otomatik birleştirme, **yeni bir dosya üretir**; mevcut bir dosyayı düzenlemez. Yine de kuralın bu durumu kapsayıp kapsamadığı **Karar Bekliyor**.
   - Önerim:
     - Birleştirme export hattının bir adımı olarak geçici klasördeki iki kaynaktan **yeni** bir nihai dosya üretir.
     - Kullanıcının hiçbir dosyasının üzerine yazmaz.
     - Kaynak TOP/BOTTOM dosyaları isteğe göre saklanır.

---

## 3. Bottom export aynalı mı gelir? Hizalama nasıl yapılır?

### 3.1 Aynalama

⚠️ **Koddan bilinemez.** Geometrik beklenti şöyle:

- Açınım aynı düzlemde, **karşı taraftan** bakılarak çiziliyorsa Bottom çizimi Top'un bir eksene göre aynasıdır. X ya da Y ekseni olabilir; ayrıca bir döndürme de eklenmiş olabilir.
- CATIA ise Bottom'da görünümü kendi yerel eksenine göre yeniden yerleştirebilir. Bu durumda koordinatlar sıfırdan ötelenmiş gelir.

Dolayısıyla tasarım **herhangi bir sabit dönüşüm varsaymamalı**; dönüşümü geometriden bulmalıdır.

### 3.2 Hizalama algoritması (öneri)

Girdi: A (taban, örneğin Top) ve B (diğer, örneğin Bottom) `DxfCizim`'leri.

1. **Dış konturu çıkar.**
   - Her çizimde `Halkalar` ile kapalı halkalar kurulur. En büyük |alan|'a sahip halka dış konturdur.
   - Dış kontur bulunamazsa veya iki alan %0,1'den fazla farklıysa işlem **reddedilir**.
2. **Aday dönüşümleri oluştur.**
   - D4 simetri grubunun 8 elemanı: 0/90/180/270° döndürme × aynalı/aynasız.
   - Her aday için önce B'nin sınır kutusu dönüştürülür. Ölçüleri A'nın sınır kutusuyla (tolerans içinde) uyuşmayan adaylar elenir.
   - Kalan her aday için öteleme = A kutusunun merkezi − dönüştürülmüş B kutusunun merkezi.
   - Pratikte büyük ihtimalle yalnızca "X aynası" ve "Y aynası" kalır; ama genel çözüm daha güvenli.
3. **Aday skoru.**
   - B dış konturunun örnek noktaları dönüştürülür. Her noktanın A dış konturuna (parça-nokta) en yakın uzaklığı hesaplanır. En büyük uzaklık (yaklaşık Hausdorff) skor olarak alınır.
   - Kabul eşiği: örneğin `max(0,01 mm, 1e-5 × kutu köşegeni)`.
4. **Belirsizliği çöz.**
   - Birden fazla aday eşik altındaysa (simetrik dış kontur), **delik merkezlerinin kümesiyle** ikinci bir skor hesaplanır: B'nin tüm CIRCLE merkezleri dönüştürülür ve A'da karşılığı olmayanlar sayılır.
   - Hâlâ birden fazla aday kalıyorsa:
     - Bu adayların hepsinde her deliğin eşleşmesi ve seçilen yarıçapların aynı olması durumunda sonuç aynıdır; herhangi biri kullanılabilir.
     - Aksi halde işlem **reddedilir** ve "manuel kontrol" gerekir.
5. **Eşleştirme.**
   - A'daki her CIRCLE için dönüştürülmüş B'de merkezi tolerans içinde olan **tek** CIRCLE aranır.
   - Kurallar:
     - Birden fazla aday bulunursa → reddet.
     - Hiç aday bulunmazsa → reddet. Delik yalnızca bir çizimde görünüyorsa geometri tutarsızdır.
     - Eşleşmeyen herhangi bir CIRCLE varsa birleştirme durdurulur.
   - Seçim: `r = min(rA, rB)`. `rA > rB` ise A'daki kaydın 40 kodu `rB` ile yamalanır.
6. **Diğer geometri.**
   - Dış kontur, bükülme çizgileri, yazılar ve diğer entity'ler **A'dan aynen** alınır; B'den hiçbir şey kopyalanmaz.
   - Böylece çıktının koordinat sistemi, layer'ları ve handle'ları A ile aynıdır.
   - Taban dosyayı sabit tutmak için A = Top önerilir. Kullanıcının alışık olduğu görünüm muhtemelen Top ⚠️.

### 3.3 Kodda gereken varsayımlar (açıkça yazılmalı)

| # | Varsayım | Kontrol |
|---|---|---|
| V1 | İki export aynı parçanın aynı açınımıdır (aynı kalınlık, aynı K-faktörü) | Dış kontur alanı ve çevresi tolerans içinde eşit olmalı |
| V2 | Dönüşüm rijittir (ölçek 1) | Sınır kutusu ölçüleri eşit olmalı |
| V3 | Delikler tam bir CIRCLE olarak yazılıyor | CIRCLE dışı kapalı küçük halka varsa (iç halka, CIRCLE değil) → reddet veya uyar ⚠️ D2 |
| V4 | Entity'ler varsayılan OCS'dedir (230=1) | 230≠1 bulunursa → reddet (bugün `DuzlemDisi` zaten engelliyor) ⚠️ D3 |
| V5 | Geçiş deliği iki çizimde aynı merkezde ve iki yüzden bakıldığında eş eksenli | Eğik havşa / açılı delik ⚠️ kapsam dışı; merkez sapması toleransı aşarsa reddet |
| V6 | Delik başının bulunduğu yüz, delikten deliğe değişebilir | min(rA, rB) kuralı bunu kendiliğinden çözer |
| V7 | Bükülme bölgesindeki delikler iki yüzde farklı açılır ⚠️ | Merkez toleransı ve eşleşme sayısı kontrolüyle yakalanır; yakalanmazsa reddedilir |
| V8 | Bükülme bilgisi (Bend Information) iki export'ta da aynı durumdadır | Aynı export fonksiyonu kullanılacağı için sağlanır; ancak panel durumu okunamazsa ⚠️ |

---

## 4. Tarama sırasında Hole unsurlarını COM ile okumak

### 4.1 Mevcut durum

- `GetThickness` (`MainWindow.xaml.cs:3132`) iki yoldan Sheet Metal kanıtı arıyor:
  - `TryFindSheetMetalFeatureInPartBody` → `TryFindSheetMetalFeatureInBody` (`:3541`): `MainBody.Shapes` gezilir ve `shape.Name`, `SheetMetalFeatureAliases` sözlüğünde aranır.
  - `Parameters` koleksiyonundaki parametre yollarının segmentleri (`SheetMetalFeatureYolunuCoz`, `:3442`).
- Sözlükte (`:2998`) sac delikleri **yalnızca varlık kanıtı** olarak geçiyor: `Extruded Hole`, `Circular Cutout`, `Sheet Metal Hole`, `Cutout` vb.
- Kod içindeki yoruma göre (`:2996`) genel Part Design `Hole` unsuru **bilerek** sözlükte yok.
- Delik tipi veya çapı **hiçbir yerde okunmuyor.**

### 4.2 Önerilen okuma yolu (GetThickness'e paralel, ayrı fonksiyon)

İmza önerisi:

```csharp
// DoScan icinde, sac olarak kabul edilen her eşsiz parça için bir kez (found döngüsü)
private static HavsaDelikBilgisi DelikleriOku(object partObj, out List<string> teshis)
```

**Yol 1 — Shapes gezintisi (tip + özellik):**

1. `Part.Bodies` içindeki **tüm** Body'ler gezilir. Delik PartBody dışında bir Body'de olabilir; `GetThickness` yalnızca MainBody/PartBody'ye bakıyor.
2. Her `shape` için tip ve ad okunur: `ComProbe.TipAdi(shape)` ve `shape.Name`.
   - "Hole" tipi veya adı `Hole`/`Delik` ile başlayan shape → Part Design Hole adayı.
3. Aday için geç bağlama ile şu özellikler okunur. `SayiOku` (`MainWindow.Maliyet.cs:804`) değer/out ikili yolunu zaten deniyor:
   - `Type`: CATIA V5 Automation'da `CatHoleType` enum'u — simple / tapered / **counterbored** / **countersunk** / counterdrilled ⚠️. Enum sayısal değerleri **doğrulanmalı**; isimler bilgi amaçlı.
   - `Diameter` → `Length` nesnesi → `.Value` (mm) ⚠️
   - `HeadDiameter` → `Length.Value` (counterbore) ⚠️
   - `HeadDepth`, `HeadAngle` (countersink açısı) ⚠️
   - Yukarıdaki üye adları CATIA V5 PartDesign Automation belgelerinden bilinen adlardır. **3DEXPERIENCE'ta aynı adlarla sunulup sunulmadığı ⚠️ Belirsiz.**
   - İlk sürümde her aday için `ComProbe.UyeAdlari(shape)` loglanarak gerçek üye listesi alınmalı. Tarama başına bir kez; `UyeDok` kalıbıyla aynı.
4. Pattern'ler (RectPattern / CircPattern / UserPattern) bir deliği çoğaltır. Adet için pattern'in `ItemToCopy` / instance sayısı gerekir ⚠️. Ancak **B tasarımı yalnızca var/yok bilgisine ihtiyaç duyar**; adet şart değil.

**Yol 2 — Parametre yolları (yedek, dil bağımsızlığa yakın):**

- `Part.Parameters` içinde yolu `…\Hole.1\Diameter`, `…\Hole.1\Head Diameter`, `…\Delik.1\Çap` gibi olan parametreler aranır.
  - Segmentler `Fold()` ile normalize edilip `hole`/`delik`/`havsa`/`counterbore`/`countersink` içeriyor mu diye bakılır.
  - Yaprak adı `diameter`/`cap`/`headdiameter`/`bascapi`/`headangle`/`aci` ise değer `NormalizeLengthMillimeters` ile okunur.
- Parametre adlarının 3DEXPERIENCE yerelleştirmesi ⚠️ Belirsiz. `GetThickness`'taki takma ad sözlüğü yaklaşımı gibi TR/EN listesi tutulmalı.
- Avantaj: Shape'in tip arayüzüne erişilemese bile çalışabilir.

**Yol 3 — Sac özgü unsurlar (⚠️ büyük belirsizlik):**

| Unsur | Havşa/imbus bilgisi taşır mı? | Not |
|---|---|---|
| `Cutout` / `Circular Cutout` (Sheet Metal) | ⚠️ Muhtemelen hayır: kesme sac kalınlığı boyunca düzdür | Bu unsurlar geçiş deliğidir; sorun üretmez |
| `Sheet Metal Hole` | ⚠️ Bilinmiyor. Tipinde countersink/counterbore seçeneği olup olmadığı doğrulanmalı | Varsa aynı `Type`/`HeadDiameter` üyeleri aranmalı |
| `Extruded Hole` (flanşlı delik) | ⚠️ Havşa değil, form unsurudur; DXF'te ayrı davranabilir | Kapsam dışı; ayrıca işaretlenebilir |
| Stamp'ler (`Dowel`, `Circular Stamp`, `Surface Stamp`) | ⚠️ Havşa benzeri görünüm üretebilir (örneğin countersunk stamp) | Adında "countersink/havşa" geçen stamp olup olmadığı D5 ile kontrol edilmeli |
| Part Design `Hole` (sac unsurlarından sonra eklenmiş) | Evet: `Type` ve `HeadDiameter` taşır ⚠️ (3DEXPERIENCE'ta üye adı doğrulanmalı) | Bu durumda "Save As DXF" "son unsur sac değil" uyarısı verir. `OnayIzleyici` bu soruya **otomatik Evet** diyor (`MainWindow.xaml.cs:4487-4491`). Kullanıcı büyük/küçük çap farkının bu yoldan geldiğini fark etmeyebilir. |
| As Result / geçmişsiz parça | Hayır: unsur yoktur | Yalnızca DXF birleştirme (A) çözer |

**Sonuç:** Hole okuma, **ön tespit** için değerli ama **tek başına yeterli değil**. Geçmişsiz parçalar, stamp'ler ve pattern'ler gözden kaçabilir. Nihai garantiyi yalnızca A (çift export + birleştirme) verir.

### 4.3 Maliyet

- Hole okuma, `GetThickness` gibi eşsiz parça başına bir kez yapılır. COM çağrısı sayısı Body × Shape kadardır; tarama süresine eklenir ⚠️ (ölçülmeli).
- Tarama `Task.Run` (MTA) içinde yapılıyor. Mevcut kalıpla aynı olduğu için yeni bir thread riski getirmez.

---

## 5. Önerilen tasarım

### 5.1 Seçenekler

| | A — Çift export + birleştirme | B — Hole okuma ile ön tespit | C — Geçici uyarı |
|---|---|---|---|
| Ne yapar | Havşalı parçayı Top ve Bottom olarak iki kez export eder; delikleri eşleştirip küçük çapı yazar | Taramada havşa/imbus deliği olan parçaları işaretler | DXF'in kontrol edilmesi gerektiğini gösterir |
| Doğruluk | En yüksek (geometriden) | Kısmi (unsur geçmişine bağlı) | Yok; yalnızca farkındalık |
| Süre etkisi | Havşalı parça başına +1 export (≈10 sn+, bkz. §9 P2 genel bakış) | Taramada küçük ek | Yok |
| Risk | Panelde yüz seçme otomasyonu (görüntü öğretme), hizalama belirsizliği | ⚠️ 3DEXPERIENCE üye adları | Yok |
| Bağımlılık | Top/Bottom seçiminin öğretilmesi (D1) | — | — |

**Önerilen sıra: C → B → A.**

- **C** hemen yapılabilir ve güvenlidir.
- **B**, A'nın **yalnızca gereken parçalarda** çalışmasını sağlar. Böylece toplu export süresi iki katına çıkmaz.
- **A** nihai çözümdür.
- A'nın B olmadan "her parçada çift export" modu, ayarlardan açılabilen yavaş ama güvenli bir seçenek olabilir. **Karar Bekliyor.**

### 5.2 C — Geçici uyarı (en küçük değişiklik)

| Dosya | Eklenecek |
|---|---|
| `Macria/MainWindow.xaml.cs` | Tarama sonunda B yoksa bile genel bir bilgi satırı: "Havşa/imbus delikli parçalarda DXF referans yüzünü kontrol edin." B varsa `SheetRow.Not`'a "Havşa/imbus delik — DXF geçiş çapını kontrol edin". Toplu export özetinde bu satırların sayısı. |
| `Macria/FareUyariWindow.xaml` (veya export öncesi onay) | ⚠️ İsteğe bağlı: "Save As DXF panelinde Bottom seçili olmalı" hatırlatması. Ancak Bottom da başı alt yüzdeki deliklerde yanlış çap verir (V6); bu yüzden metin "kontrol edin" olmalı, "Bottom seçin" olmamalı. |
| `SheetRow` | ⚠️ `DxfDurumKodu`'na yeni bir kod eklemek (örneğin "Kontrol") Excel ve durum ikonu eşlemelerini etkiler (`DxfDurumMetni`, `DxfDurumIkonu`). Bunun yerine `Not` kullanmak daha az dokunuş gerektirir. |

### 5.3 B — Hole okuma ile ön tespit

| Dosya | Eklenecek |
|---|---|
| `Macria/HavsaDelikBilgisi.cs` (yeni, saf) | `sealed class HavsaDelikBilgisi { bool HavsaVar; bool ImbusVar; int AdayDelikSayisi; List<(string Ad, string Tip, double? CapMm, double? BasCapMm)> Delikler; List<string> Teshis; }` + parametre yolu sınıflandırıcı `static bool HoleParametresiMi(string yol, out string alan)`. COM içermez, test edilebilir. |
| `Macria/MainWindow.xaml.cs` | `DelikleriOku(object partObj, out List<string> teshis)` (Yol 1 + Yol 2); `DoScan`'deki `found` döngüsünde sac satırları için çağrılır; sonuç `ScanItem`/`SheetRow`'a eklenir. Tarama başına bir kez `UyeDok`/`ComProbe.UyeAdlari` ile Hole üye dökümü. |
| `SheetRow` (`MainWindow.xaml.cs:18`) | `bool HavsaDelikVar`, `string DelikOzeti` (tooltip). |
| `MainWindow.xaml` | Sac Lazer tablosunda küçük bir ikon veya `Not` içeriği (sütun eklemek `ParcaSutunDeposu` anahtarlarını etkiler; **Karar Bekliyor**). |
| `GeometryLabAdapter.Tests` veya `DxfEdit.Tests` | `HoleParametresiMi` ve TR/EN takma ad testleri. |

### 5.4 A — Çift export + birleştirme

**Akış:**

1. `ExportOne` yerine, havşalı satırlar için `ExportOneCiftYuz(repRef, nihaiYol)` çağrılır:
   - `%LOCALAPPDATA%\Macria\DxfYuz\<guid>\top.dxf` ve `bottom.dxf` ayrı ayrı export edilir.
   - Tasarım tercihi: Parça **bir kez açılır** ve panel iki kez çalıştırılır. İki kez açıp kapatmaktan hızlıdır ⚠️. CATIA'nın aynı açık parçada komutu ikinci kez kabul ettiği doğrulanmalı.
2. Her export öncesi `ReferansYuzuSec(Top|Bottom)`: görüntüden bulunan seçeneğe tıklanır, durum tekrar okunarak doğrulanır. **Doğrulanamazsa export yapılmaz.** Bu, `BukumBilgisiniKapat`'taki "yanlış tıklamaktansa hiç tıklama" ilkesinin aynısıdır.
3. `DxfDelikBirlestirici.Birlestir(topBytes, bottomBytes, secenekler, out rapor) : byte[]?`
4. Başarılıysa nihai yola **yeni dosya** olarak yazılır (geçici dosya + `File.Move(…, false)`). Hedef varsa mevcut toplu export davranışı (§9 R5 genel bakış) geçerlidir; **Karar Bekliyor**.
5. Başarısızsa (reddedildiyse):
   - Nihai dosya **üretilmez** ya da Top dosyası "_KONTROL" ekiyle bırakılır. **Karar Bekliyor.**
   - Satır `DxfBasarisiz(rapor)` olur.
6. Geçici klasör temizlenir. Hata ayıklama için "kaynakları sakla" ayarı eklenebilir.

**Dosyalar:**

| Dosya | Eklenecek |
|---|---|
| `Macria/DxfDelikBirlestirici.cs` (yeni, WPF'siz, COM'suz) | `Birlestir(...)`, `DisKonturBul(DxfCizim)`, `HizalamaBul(DxfCizim a, DxfCizim b, out Matrix m, out rapor)`, `DelikEslestir(...)`. `Yerlesim.Halkalar`/`KapaliAlan` buraya ya da ortak `DxfKontur.cs`'e taşınır. `Yerlesim.cs` yalnızca yeni yeri çağırır (davranış değişmez). |
| `Macria/DxfEditOturumu.cs` | `DxfKaynakBelge` içine dar kapsamlı `internal byte[] CemberYaricaplariniUygula(IReadOnlyDictionary<DxfKaynakKayit,double> yeniYaricap)`. Yalnızca `Tip=="CIRCLE"`, `KayitGecerli`, `DuzenlemeEngeli==null`, tek bir 40 kodu, sonlu ve pozitif değer kabul edilir. `DegerIkilisi`'nin yeniden kullanımı. `DuzenlemeleriUygula` ile aynı yama birleştirme kodu (ortak private metoda ayrılabilir). LINE yolu değişmez. |
| `Macria/ReferansYuzBulucu.cs` (yeni) | `BukumBulucu` kalıbı. Öğretme: Top ve Bottom etiketlerinin görüntüsü (`referans-top.png`, `referans-bottom.png`) + seçili durum rengi. Çalışma: `Durum Bul(IntPtr pencere, Yuz yuz)`. `GorselEslesme`'yi kullanır. |
| `Macria/SettingsWindow.xaml(.cs)` | "Top seçeneğini öğret" ve "Bottom seçeneğini öğret" (F8 kalıbı). |
| `Macria/Ayarlar.cs` | `DxfCiftYuz` (0 = kapalı / 1 = yalnız havşalılar / 2 = tümü), `ReferansTopGri/Doygunluk`, `ReferansBottomGri/Doygunluk`, `DxfYuzKaynaklariniSakla`. |
| `Macria/MainWindow.xaml.cs` | `ExportOneIc`'i panel açma + yüz seçme + kaydetme adımlarına ayıran küçük bir yeniden düzenleme (mevcut fallback zinciri korunarak). `ExportOneCiftYuz`, `btnExportAll_Click` ve `mnuExportDxf_Click` içinde yönlendirme. |
| `DxfEdit.Tests/DxfEdit.Tests.csproj` | `<Compile Include="../Macria/DxfDelikBirlestirici.cs" …/>` (+ taşınırsa `DxfKontur.cs`). |
| `DxfEdit.Tests/Program.cs` | §5.5'teki testler. |

### 5.5 Test planı

**`DxfEdit.Tests`'e eklenecek birleştirme testleri** (CATIA gerektirmez; fixture DXF metinleri testte üretilir):

| Test | Beklenen |
|---|---|
| `MergeIdenticalDrawings` | İki özdeş DXF → çıktı baytları A ile birebir aynı; rapor "0 delik değişti". |
| `MergeMirroredX_SmallerInBottom` | B, A'nın X aynası + ötelenmiş hali; bir delik B'de küçük → çıktıda o CIRCLE'ın yalnızca 40 kodu değişmiş; diğer tüm baytlar aynı. |
| `MergeMirroredY` / `MergeRotated180` | Dönüşüm doğru bulunur. |
| `MergeHeadOnOppositeFaces` | Bir delik A'da büyük, diğeri B'de büyük → çıktıda ikisi de küçük. |
| `MergeCounterboreAndCountersinkMixed` | Farklı çaplarda karışık delikler. |
| `MergeRejectsOutlineMismatch` | Dış kontur alanı farklı → `null` + neden. |
| `MergeRejectsUnmatchedHole` | Yalnızca bir çizimde bulunan delik → reddet. |
| `MergeRejectsAmbiguousMatch` | Tolerans içinde iki aday merkez → reddet. |
| `MergeSymmetricOutlineResolvedByHoles` | Simetrik dış kontur; delik düzeni dönüşümü belirler. |
| `MergeSymmetricFullyAmbiguous` | Kontur ve delikler simetrik, sonuç adaydan bağımsız → kabul; bağımlı → reddet. |
| `MergeRejectsNonDefaultOcs` | B'de 230=-1 CIRCLE → reddet (V4). |
| `MergeRejectsPolylineHoles` | Delik LWPOLYLINE ile çizilmiş (CIRCLE değil, kapalı iç halka) → reddet veya uyar. |
| `MergeArcHalvesHole` | ⚠️ Delik iki yarım ARC ise: ilk sürümde reddet (test bunu sabitler). |
| `MergeToleranceBoundaries` | Merkez sapması eşiğin hemen altında ve üstünde. |
| `MergePreservesLayerHandleAndLineEndings` | 8/5/330 kodları ve CR/LF korunur. |
| `MergeOutputReparses` | Çıktı `DxfOkuyucu.Oku` ve `DxfKaynakBelge.Olustur` ile hatasız tekrar okunur. |
| `CircleRadiusPatchGuards` | `CemberYaricaplariniUygula`: negatif/NaN yarıçap, LINE kaydı, yinelenen 40 kodu ve yabancı belge kaydı → `InvalidOperationException`. |
| `CircleRadiusPatchDoesNotAffectLineEdits` | Mevcut LINE yama testleri değişmeden geçer (regresyon). |

**B için:** `HoleParametresiMi` TR/EN yol örnekleri; `NormalizeLengthMillimeters` ile çap (mevcut fonksiyon; birim testi yok, eklenmeli).

**UI testi** (`DxfEdit.UiTests`): Değişiklik yok. Birleştirme OnizlemeWindow'a girmez.

**Çalıştırma:** Mevcut komutlar (§7 genel bakış). Her değişiklikten sonra `DxfEdit.Tests` ve `GeometryLabAdapter.Tests` tam geçmeli.

### 5.6 CATIA'da yapmanız gereken denemeler

Bu denemeler tasarımın ⚠️ işaretli varsayımlarını kapatır. Kod yazılmadan önce yapılması önerilir.

| # | Deneme | Toplanacak kanıt |
|---|---|---|
| D1 | Save As DXF panelini açın. Top/Bottom seçeneğinin **yerini ve biçimini** (radyo / liste / ayrı sekme) görün. Aynı oturumda ikinci bir parça için paneli yeniden açın: seçim **hatırlanıyor mu?** CATIA'yı kapatıp açın ve tekrar bakın. | Panelin ekran görüntüleri (Top seçili ve Bottom seçili halleri); hatırlama davranışı |
| D2 | Havşalı ve imbus delikli test parçasını Top ve Bottom ile ayrı ayrı export edin. İki DXF'i Macria önizlemesinde ve bir metin editöründe açın. | Delikler `CIRCLE` mı (`0\nCIRCLE`), yoksa ARC/LWPOLYLINE mi? Top dosyasında hem büyük hem küçük çember var mı, yoksa yalnızca büyük mü? |
| D3 | Aynı iki dosyada `210/220/230` kodlarını arayın. | `230` değeri `-1` olan entity var mı? (V4) |
| D4 | İki DXF'i üst üste koyun (bir CAD programında). Bottom, Top'un X aynası mı, Y aynası mı, döndürülmüş mü? Koordinat orijini aynı mı? | Dönüşüm türü ve öteleme miktarı |
| D5 | Başı **alt yüzde** olan bir havşa deliği ekleyin (ters yönde Hole). Top ve Bottom'da hangi çap çıkıyor? | V6'nın doğrulanması (min kuralı gerekli mi) |
| D6 | Deliği sırasıyla (a) Part Design `Hole` (counterbored / countersunk), (b) Sheet Metal Hole / Cutout, (c) stamp ile yapın. Her birinde Top/Bottom davranışı ve "son unsur sac değil" uyarısı çıkıyor mu? | Hangi unsur türleri sorunu üretiyor |
| D7 | Macria'da tarama yapın ve konsol dökümünü paylaşın. B'nin ilk sürümü eklendiğinde Hole shape'lerinin `ComProbe.UyeAdlari` listesi loglanacak. Şimdilik CATIA'da Hole unsurunun Properties / Parameters listesindeki parametre adlarını (TR arayüzde) not edin. | `Type`, `Diameter`, `HeadDiameter`, `HeadAngle` gerçek adları ve TR karşılıkları |
| D8 | Bükülme bölgesine yakın veya bükümde kalan havşalı bir delik deneyin. | V7: iki yüzde merkezler çakışıyor mu |
| D9 | Aynı açık parçada Save As DXF'i art arda iki kez çalıştırın (önce Top, sonra Bottom). | Tek açılışla çift export mümkün mü (A süresi) |

---

## 6. Kısa sonuç

1. **Bugün Macria Top/Bottom seçimine hiç dokunmuyor.** Hangi yüzle export edildiği, CATIA panelinin o anki durumuna bağlı ⚠️ (D1).
2. **İki DXF'ten küçük çaplı birleşik DXF üretmek mümkün.** Okuma, bayt korumalı yama ve güvenli yazma hazır. Eksik olanlar: CIRCLE 40 kodu yaması, dış kontur tabanlı hizalama (mevcut `Yerlesim.Halkalar` taşınarak) ve eşleştirme.
3. **Aynalama koddan bilinemez.** Hizalama sabit dönüşüm varsaymamalı; 8 D4 adayını dış kontur ve delik düzeniyle skorlayıp belirsizlikte reddetmeli. OCS (230=-1) ve CIRCLE dışı delikler ilk sürümde reddedilmeli.
4. **Hole okuma mümkün görünüyor ama üye adları 3DEXPERIENCE'ta ⚠️ doğrulanmalı.** B, ön tespit için yararlı; tek başına garanti değil.
5. **Önerilen sıra: C → B → A.** A için önce D1–D4 ve D9 denemeleri yapılmalı.

**Karar Bekliyor:**

- Otomatik birleştirmenin AGENTS.md DXF Edit Modu kuralıyla ilişkisi.
- Reddedilen parçada nihai dosya üretilip üretilmeyeceği.
- Çift export kapsamı: yalnızca havşalılar mı, yoksa tümü mü.
- Tabloya yeni sütun eklenip eklenmeyeceği.

**Obsidian:** Kod değişmediği için güncellenmedi. Uygulamaya geçilirse ilgili notlar `03 - DXF ve Sheet Metal.md` ve `99 - Yapılacaklar.md` olur.
