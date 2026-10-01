# Sac Tanıma, Delik Tanıma ve Açınım — Tasarım (GeometryEngine + Macria)

- **Tarih:** 2026-09-27
- **Dayandığı belgeler:** `SAC_STEP_DXF_DEVIR.md`, `DXF_HAVSA_ARASTIRMA.md`, `DXF_REFERENCE_SIDE_ARASTIRMA.md`, `tools/prototip/delik_tanima.py`
- **Durum:** Yalnızca tasarım. **Hiçbir kaynak kod değiştirilmedi.** Ne Macria'da ne GeometryLab'da.
- **İşaretler:**
  - ⚠️ Doğrulanmamış veya belirsiz
  - **Karar Bekliyor:** Kullanıcı kararı gerekiyor (AGENTS.md)

---

## 0. Özet

1. **Motor kaynağı bulundu:** `C:\Users\enesy\OneDrive\Belgeler\Macria\Macria.GeometryLab\` (C++ / OCCT 8.0.1, CMake). Git deposu değil; GeometryLab'ın kendi `AGENTS.md`'si manifest ve SHA-256 tabanlı checkpoint istiyor.
2. **Mevcut motor bu sac parçaları tanımıyor:** İki test STEP'inde profil sonucu "Unknown / InsufficientEvidence". Ancak sac tanıma için gereken altyapı büyük ölçüde hazır: yüz ve kenar kimlikleri, AAG komşuluğu, Convex/Concave/Smooth dihedral sınıflaması, katı içi nokta testi, OCCT kesit alma.
3. **Şema uyumluluğu (2026-10-01 güncellemesi):** Macria artık `schemaVersion` olarak "1.0", "1.1" ve "1.2"yi kabul ediyor (`GeometryLabProcessAdapter.cs:74`, `SupportedSchemaVersions`). Runtime'daki motor "1.2" yazıyor ve montaj-1 analizi kabul ediliyor. *Önceki davranış:* Macria 1.11.2 yalnızca tam "1.0" kabul ediyordu; §5.1'deki sıralı geçiş planı o duruma göre yazıldı.
4. **Prototip havşa ve imbus örneklerinde doğru sonuç veriyor** (§8). Ancak 22 dosyalık gerçek derlemde dört türde hata üretiyor ve bu hatalar tasarım kurallarını doğrudan belirliyor:
   - Dış köşe yuvarlatması "delik" sanılıyor.
   - Kıvrılmış (rolled) sac "imbus" sanılıyor.
   - Kapalı kutu profiller "sac" sanılıyor.
   - DXF'te olmayan küçük bir "havşa" buluyor.
5. **Büküm payı modeli belli** (devir notu §5, 2026-09-27 güncellemesi):
   - `K = log10(clamp(20R/T, 1, 100))/4`, `BA = (R + K·T)·θ`; R = iç yarıçap.
   - Motorda varsayılan; Macria ayarından değiştirilebilir.
   - 55RS100111-12'de CATIA ile birebir doğrulandı (BA = 11,0117534 mm).
   - Açınımın 90° dışı ve çok bükümlü parçalarda doğrulanması kaldı (§3.3.1–3.3.3).
6. **Önerilen iş bölümü:**
   - **GeometryEngine:** Sac, delik ve açınım geometrisini **JSON olarak** üretir.
   - **Macria:** DXF'i yazar. GeometryLab `AGENTS.md`'si DXF işlemeyi motor tarafında korunan alan sayıyor.

---

## 1. GeometryEngine kaynağı

### 1.1 Konum ve durum

| Öğe | Değer |
|---|---|
| Kök | `…\Belgeler\Macria\Macria.GeometryLab\` |
| Native motor | `src\Macria.GeometryEngine\` (CMake 3.25, `project(MacriaGeometryEngine VERSION 0.1.0)`) |
| OCCT | `find_package(OpenCASCADE 8.0.1 REQUIRED …)`, third-party kökü `MACRIA_OCCT_THIRDPARTY_ROOT` |
| Hedefler | `MacriaGeometryEngineCore` (static lib), `MacriaGeometryEngine` (exe), `MacriaGeometryViewer` (dll), `MacriaGeometryFixtureGenerator`, 4 test exe'si (`Aag`, `Dihedral`, `Profile`, `ViewerInput`) |
| Derleme klasörü | `src\Macria.GeometryEngine\build-vs18-x64\` (mevcut) |
| .NET tarafı | `src\Macria.GeometryLab\` (WPF deneme uygulaması, `Contracts\GeometryDtos.cs`, `GeometryAnalysisValidator.cs`), `tests\Macria.GeometryLab.Tests\` |
| Tarihler | Motor kaynağı 2026-09-19, viewer 2026-09-24 |
| Git | **Yok** |
| Yedek kopya | `Desktop\Macria-ChatGPT-Inceleme\Macria-GeometryLab-KaynakKod.zip` (19.09, viewer'sız) |

**Kaynak ile ikili dosyanın uyumu:** Macria'nın `GeometryEngineRuntime\Macria.GeometryEngine.exe` dosyasının ürettiği JSON anahtarları, kaynaktaki `SerializeJson` ile birebir aynı. Buna `baseStockProfile` ve `modificationAnalysis` da dahil. Yani ikili dosya bu kaynaktan (ya da çok yakın bir sürümden) derlenmiş görünüyor. ⚠️ Birebir aynı olduğu SHA-256 veya yeniden derlemeyle doğrulanmadı.

**Yan bulgu:** CMake, `avcodec`/`avformat`/`openvr`/`FreeImage` DLL'lerini OCCT 8.0.1'in resmî third-party paketinden kopyalıyor. `MACRIA_PROJECT_OVERVIEW.md` §9'da "gerekli mi?" diye sorulan DLL'lerin kaynağı bu.

### 1.2 GeometryLab `AGENTS.md`'nin bu işe etkisi

- **Algoritma kuralları:** Algoritmaya dosya adı, sabit eksen, nominal ölçü ya da test parçasına özel değer yazılmaz. Belirsizlikte değer uydurulmaz; hesaplanamayan değer 0 değil `null` kalır.
- **JSON uyumu:** Alanlar silinmez ve yeniden adlandırılmaz; genişletmeler geriye uyumlu yapılır.
- **Korunan alanlar:** Macria ana uygulaması, **DXF işleme/dışa aktarma**, OCCT bağımlılıkları ve WPF arayüzü. Kullanıcı açıkça istemedikçe dokunulmaz.
- **Checkpoint:** Her aşamada `<AŞAMA>-BEFORE` / `-COMPLETE` klasörleri ve ZIP'leri oluşturulur, SHA-256 manifesti tutulur.
- **Gerçek STEP testleri:** Yollar parametre veya ortam değişkeniyle verilir. Yol verildiği halde dosya yoksa test başarısız sayılır.

---

## 2. Mevcut modüller ve yeniden kullanılabilecekler

### 2.1 Analiz akışı

`AnalyzeStep`, `GeometryEngine.cxx:1042` → aşağıdaki sırayla:

1. STEP okuma (`ReadStream`, `SetSystemLengthUnit(1.0)` → mm)
2. `BRepCheck_Analyzer`: Şekil geçerli değilse sonuç **Invalid** olur; onarım (healing) yapılmaz.
3. `PopulateTopology`
4. `PopulateProfileGeometryAnalysis`
5. `PopulateProfileRecognition`
6. `PopulateBaseStockProfile`
7. `PopulateEndCutAnalysis`
8. `PopulateLengthCandidates`
9. `PopulateLengthSummary`
10. `SerializeJson`: `"schemaVersion":"1.0"` sabit (`:1121`)

### 2.2 Doğrudan yeniden kullanılabilecekler

| Fonksiyon / yapı | Konum | Sac/delik için kullanımı |
|---|---|---|
| `PopulateTopology` | `GeometryEngine.cxx:843` | Yüz, kenar ve solid ID haritaları (`TopExp::MapShapes`, 1 tabanlı). `FaceRecord` (tip, alan, yönelim, düzlem normali, komşular, kenarlar). `EdgeIncidenceRecord` (Manifold/Free/**Seam**/NonManifold/CrossSolid). Sac analizi aynı ID uzayını kullanmalı: aynı `MapShapes` sırası. |
| `AnalyzeAdjacencyGeometry` + `ClassifyLocalJoin` | `:667`, `:608` | Her ortak kenar için **Convex / Concave / Smooth** kararı (katı sınıflayıcıyla, 3 ölçekte tutarlılık). **Smooth** = teğet geçiş, yani büküm ile flanş arasındaki kenar. İmbus omzundaki **Concave** kenarlar gerçek veride görüldü (§8.1). |
| `EvaluateOrientedNormal` | `:530` | Yüz yönelimine göre dışa bakan normal. **Deliği dış yuvarlatmadan ayırmak** için gerekli (§3.2). |
| `IsPointInsideSolid` | `:584` | Malzeme tarafı testi (bir silindirin "içi" mi "dışı" mı malzeme). |
| `CollectDirectionEvidence` / `MergeDirections` | `ProfileGeometryAnalysis.cxx:149, 236` | Düzlem normalleri, silindir eksenleri, doğrusal kenar yönleri. Sac için baskın normal ve büküm eksenleri çıkarılabilir. |
| `ProjectionRange` | `ProfileGeometryAnalysis.cxx:302` | Bir yön boyunca katı kapsamı. Delik ekseni boyunca "boydan boya geçiyor mu?" kontrolü. |
| `BuildSection` / `AnalyzeProfileSection` | `:456`, `:1346` | OCCT kesiti + kontur/segment kaydı. Kalınlığı bağımsız ikinci bir kanıtla doğrulamak için kullanılabilir. |
| `MeasureWallThickness` | `:668` | Et kalınlığı ölçüm kalıbı (kesit konturları arası). Sac kalınlığı için referans alınabilir. |
| JSON yardımcıları | `GeometryEngine.cxx:124–470` | `Quote`, `AppendVector`, `AppendNullableDouble`, `AppendArray`, `EscapeJson`. Yeni bloklar aynı üslupla yazılmalı. |
| Test altyapısı | `tests\FixtureGenerator.cxx`, `Run-RealStepAagTest.ps1`, `Run-RealCutAcceptanceTest.ps1` | Sentetik parça üretimi ve env-var ile gerçek STEP kabul testi kalıbı. |

### 2.3 Eksikler

- **`FaceRecord` dönel yüz geometrisini tutmuyor.** Silindir veya koni için eksen, yarıçap ve yarım açı yok (`GeometryEngine.hxx:46`). Sac ve delik analizi bunları kendi içinde `BRepAdaptor_Surface` ile okuyabilir. JSON'a da isteğe bağlı `surfaceGeometry` olarak eklenmesi önerilir (§5).
- **Mevcut profil hattı sac parçada boşa çalışıyor.** Eksen adayları ve adaptif kesitler hesaplanıyor, sonuç Unknown çıkıyor. Performans etkisi büyük sac derlemlerinde ölçülmeli ⚠️.
- **"Diğer" yüz tipleri (BSpline / tor vb.)** derlemde `-10`, `-11`, `-13 MARKALAMA` parçalarında görülüyor (§8.2). Bu yüzlerin büküm mü, yazı/markalama mı olduğu bilinmiyor ⚠️.

---

## 3. Yeni analiz tasarımı (motor)

### 3.0 "Sayısal eşiksiz" ilkesinin anlamı

Bu tasarımda iki tür sayı birbirinden ayrılır:

| Tür | Durum | Örnek |
|---|---|---|
| **Alan (domain) eşiği** | **Kullanılmaz** | "t 0,3–30 mm arasında olmalı", "delik Ø ≤ X", "R < 5t ise…", "derinlik farkı < 0,05 mm" |
| **Ölçüm hassasiyeti** | Kullanılır; motorun mevcut sabitleriyle aynı mantıkta | `Precision::Confusion()` (1e-7), açısal hassasiyet, `MeasurementToleranceMm = 1e-4` ya da model boyutuna göre göreli tolerans. Bu toleranslar "aynı sayı mı?" sorusunu yanıtlar; "üretimde makul mü?" sorusunu yanıtlamaz. |
| **Genel standart tablolar (ISO/DIN)** | **İzinlidir** (kullanıcı onayı, 2026-09-28) | ISO 261/724 metrik diş çekirdek çapı D1 = D − 1,082532·P (dişli delik tanıma). Tablo parçaya özel değil, herkese açık bir standarttır; ayrı, birim testli bir fonksiyonda tutulur ve kaynağı kodda yazılır. |

**Prototip bu ilkeye uymuyor**, ilk aşamada ayıklanmalı:

- `0.3 < t < 30` (`sac_kalinligi`)
- `abs(derinlik - t) > 0.05` ve `abs((b.r - a.r) - t) < 0.05`
- `TOL = 1e-3`, `< 1e-3` eksen sapması

### 3.1 Sac tanıma — A/B/kenar yüzü sınıflaması

**Girdi:** Her solid (çoklu solid birbirine karıştırılmaz). **Yeni dosya:** `src\SheetMetalRecognition.cxx`, `PopulateSheetMetal(shape, result)`.

1. **Ofset çiftlerini bul.** Her yüz çifti (F, G) için "G, F'nin sabit `d` ofseti mi?" diye bakılır:
   - **Düzlem–düzlem:** Normaller zıt ve paralel. `d` = düzlemler arası uzaklık. F'nin UV örnek noktaları `−d·n` yönünde ötelendiğinde G'nin üzerinde olmalı; `GeomAPI_ProjectPointOnSurf` mesafesi ölçüm toleransı içinde kalmalı. Böylece yalnızca sonsuz düzlemlerin değil, gerçek yüz parçalarının karşılıklı olduğu kanıtlanır.
   - **Silindir–silindir:** Eş eksenli, `|rF − rG| = d`, biri içbükey diğeri dışbükey (§3.2).
   - **Koni–koni / tor–tor:** v1'de desteklenmez; `Unsupported` kanıtı üretilir ⚠️.
2. **Kalınlık t:** Tüm ofset çiftlerinin `d` değerleri alan ağırlıklı gruplanır. **Tek bir değer** (ölçüm toleransı içinde) çıkmalı. Birden fazla güçlü değer varsa → `Ambiguous` (GeometryLab kuralı: belirsizlik zorla çözülmez). Kalınlık yalnızca modelden okunur.
3. **Kabuklar (A/B):**
   - Ofset çiftlerinin yüzleri iki kümeye ayrılır. Aynı kümedeki yüzler birbirine **Smooth** (teğet) kenarlarla bağlıdır.
   - A kümesinin her yüzünün B'de bir ofset eşi olmalı; B için de aynısı.
   - Hangi kabuğun "A" olduğu: En büyük düzlem yüzünü içeren kabuk A kabul edilir. Bu yalnızca bir adlandırmadır; üretim anlamı yoktur. CATIA "Top" ile ilişkisi ⚠️ bilinmiyor (devir notunda tek örnek).
4. **Kenar yüzleri:** Kalan her yüz şu koşulları sağlamalı:
   - Sınır kenarları ya A kabuğunda, ya B kabuğunda, ya da iki kabuk arasında yerel normal yönünde **uzunluğu t olan** geçiş kenarlarından oluşuyor.
   - Kenar yüzü, bir A kenarını onun B'deki ofset kenarına bağlayan düzenli (ruled) bir yüzey.
   - Delik duvarları ve havşa konileri de kenar yüzüdür (delik tanıma §3.2 bunları ayrıca yorumlar).
5. **Açık kabuk koşulu (tüp ve kapalı profil ayrımı):**
   - A kabuğunun yüz-komşuluk grafiği (Smooth kenarlar üzerinden) **döngüsüz** olmalı. Tam 360°'lik silindir yüzü (motorun `Seam` kenarı) kabul edilmez.
   - Kutu profilin yan yüzleri ve köşe yarıçapları kabukta bir döngü oluşturur → `NotSheet` (`ClosedSection`). Bu parçalar profil hattına bırakılır.
   - **Gerekçe:** Prototip kutu profilleri (01–03, 6, 13*) "sac + 4 büküm" diye buluyor (§8.2).
   - ⚠️ Kaynaklı veya kapalı dikişli (seam weld) profiller STEP'te ayrı solid mi, tek solid mi geliyor? Doğrulanmalı.
6. **Karar:**
   - Tüm yüzler A/B/kenar kümelerine oturur, t tekildir ve kabuk açıktır → `Recognized`.
   - Aksi halde `NotSheet`, `Ambiguous` veya `Unsupported`; ilk karşılanmayan koşul `rejectionReason` alanına yazılır.
   - Hesaplanamayan değer `null` kalır.
7. **Büküm kayıtları:**
   - Her içbükey/dışbükey silindir çifti (A/B'de eş eksenli) bir büküm sayılır: `innerRadius = min(r)`, `outerRadius = max(r)`.
   - Açı, silindir yüzünün eksen etrafındaki açısal kapsamından (UV aralığı) alınır. Yön (UP/DOWN) referans kabuğa göre belirlenir ⚠️. CATIA DXF'inde "UP"/"DOWN" etiketleri var; eşleşme kuralı gerçek parçalarla kanıtlanmalı.

**Profil tanımayla çakışma:** Bir solid hem profil (Definite) hem sac olarak tanınırsa hangisi öncelikli olacak? **Karar Bekliyor.** Öneri: `ClosedSection` kuralı zaten kapalı profilleri sacdan dışlar. Açık U/C profiller (lama sac bükümü) gerçekten iki anlamlı da olabilir; ikisi birden raporlanmalı, Macria kullanıcıya göstermeli.

### 3.2 Delik tanıma (prototip kuralları, geometrik hale getirilmiş)

**Yeni dosya:** `src\HoleFeatureRecognition.cxx`, `PopulateHoleFeatures(shape, result)`. Yalnızca sac `Recognized` olan solidlerde çalışır; öteki solidlerde `NotStarted`.

1. **Aday yüzler:**
   - Kenar yüzü kümesindeki **içbükey** dönel yüzler (silindir, koni).
   - **İçbükeylik** = malzemenin eksenden uzakta olması. Yüzeyin yönlü normali (`EvaluateOrientedNormal`) eksene doğru bakıyorsa yüz içbükeydir. İkinci kanıt: eksen üzerindeki bir nokta `IsPointInsideSolid` ile **dışarıda** çıkmalı.
   - **Neden gerekli:** Prototip dış köşe yuvarlatmalarını (dışbükey) "Ø20 delik" diye buluyor (parça 4, §8.2).
2. **Eş eksenli gruplama:** Eksenler paralel (açısal hassasiyet) ve eksenler arası dik uzaklık ölçüm toleransı içinde.
3. **Boydan boya geçme:**
   - Grubun eksen boyunca kapsamı, deliğin bulunduğu yerde A ve B kabuklarının arasını **tam olarak** kaplamalı. Uçlar, A ve B kabuklarındaki iç döngü (inner wire) kenarlarına bağlanmalı.
   - Bu koşul hem büküm silindirlerini (zaten kabuk yüzleri) hem de kör delik ve kademe gibi yarım işlemleri eler.
   - Karşılaştırma ölçüm toleransıyla yapılır; `0.05` gibi bir sabit kullanılmaz.
4. **Sınıflama:**

   | Grup içeriği | Tip |
   |---|---|
   | Tek silindir | `Through` (düz delik) |
   | Silindir + koni | `Countersink` (havşa); `headAngleDeg = 2 × yarım açı` |
   | Farklı yarıçaplı iki silindir + aralarında düzlemsel halka (Concave kenarlarla bağlı) | `Counterbore` (imbus); `headDepthMm` = büyük silindirin eksen kapsamı |
   | Silindir + koni + silindir, çoklu kademe | `Counterdrill` / `Unknown` ⚠️ (v1'de ayrıntı yok, raporlanır) |

5. **Ölçüler:**
   - `throughDiameterMm` = grubun eksen boyunca **en dar** çapı.
   - `headDiameterMm` = en geniş çap.
   - `openingSide` = en geniş çapın bulunduğu kabuk (A/B).
   - Merkez = eksenin A kabuğu düzlemiyle kesişimi.
6. **Büküm üzerindeki delikler:** Delik ekseni yerel kabuk normaline paralel değilse `onBend = true` ve `status = Unsupported` ⚠️. Bu durum açınımda ayrı ele alınmalı.
7. **Kesim DXF'i için kural:** Açınıma her delik **`throughDiameterMm`** ile yazılır; havşa ve imbus başı kesim çizgisi değildir. Bu kural devir notundaki hedeftir.
8. **İncelenmesi gerekenler:**
   - ⚠️ Parça 2 ve 3'te prototip "havşa 6,647/8,647" buluyor; CATIA DXF'inde böyle bir çember yok (§8.2). Bu koninin ne olduğu (pah, markalama, büküm bölgesi?) gerçek parçada incelenmeli.
   - İnceleme bitene kadar bu tip gruplar `Unknown` ile işaretlenmeli, "delik" diye sayılmamalı.

### 3.3 Açınım

**Yeni dosya:** `src\SheetMetalUnfold.cxx`, `PopulateFlatPattern(shape, result, options)`. Yalnızca sac `Recognized` olan ve tüm bükümleri silindirik olan solidlerde çalışır.

1. **Kök yüz:** A kabuğunun en büyük düzlem yüzü (yalnızca başlangıç seçimi; sonucu etkilememeli ⚠️, testte kanıtlanmalı).
2. **Gezinme:** A kabuğu grafiğinde genişlik öncelikli (BFS). Her büküm için büküm payı hesaplanır ve sonraki flanş yerine yerleştirilir (aşağıdaki adımlar).

#### 3.3.1 K-faktörü ve büküm payı (varsayılan, doğrulandı)

Kaynak: `SAC_STEP_DXF_DEVIR.md` §5 (güncel sürüm).

```text
K  = log10( clamp(20·R / T, 1, 100) ) / 4        // K ∈ [0, 0,5]
BA = (R + K·T) · θ                                // θ radyan
```

CATIA'daki özgün yazımı: `K = log(min(100, max(20*R, T) / T)) / log(100) / 2`. İki yazım matematiksel olarak aynıdır: `max(20R, T)/T = max(20R/T, 1)`; `log(x)/log(100)/2 = log10(x)/4`.

**Girdiler (hepsi B-Rep'ten, motor tarafından; tablo yok):**

- **R:** Bükümün **iç** yarıçapı = büküm silindir çiftinin küçük yarıçapı (`bends[].innerRadiusMm`). Dış yarıçap `R + T` ile tutarlı olmalı. Bu, §3.1 adım 7'nin ofset çifti kanıtıdır; tutarsızsa büküm `Ambiguous` olur ve açınım yapılmaz.
- **T:** §3.1 adım 2'de bulunan tekil sac kalınlığı.
- **θ:** Silindir yüzünün eksen etrafındaki açısal kapsamı (radyan). İç ve dış silindirin kapsamları aynı olmalı; değilse `Ambiguous`.

**Doğrulama:** 55RS100111-12 (R = 4, T = 20, θ = 90°):

- K = log10(4)/4 = 0,150515
- BA = (4 + 0,150515·20)·π/2 = **11,0117534 mm**
- CATIA DXF'inden ölçülen değerle fark ~1e-9 mm, **birebir aynı**.
- Enes'e göre formül **başka parçalarda da aynı**.

**Varsayılan davranış ve ayar:**

- Motor bu formülü **varsayılan** olarak kullanır. Macria'dan hiçbir seçenek gelmezse de uygulanır.
- İleride farklı bir şablon çıkarsa diye Macria ayarlarından değiştirilebilir kalır. Önerilen motor argümanı:
  - `--k-factor-formula catia-log` (varsayılan)
  - `--k-factor-formula constant:<k>` (sabit K, 0 ≤ k ≤ 0,5 dışı reddedilir)
- Geçersiz argüman → motor `InvalidArguments` ile çıkar. Sessizce varsayılana dönmez.
- ⚠️ Ek formül türleri (tablo vb.) ihtiyaç doğana kadar eklenmez.

**Formüldeki sabitler (20, 1, 100, 4) "alan eşiği" değildir.** Bunlar CATIA'nın tanımlı büküm payı modelinin parçasıdır. Tanıma kararını (sac mı, delik mi) etkilemezler; yalnızca açınım ölçüsünü hesaplarlar. §3.0 ilkesiyle çelişmez.

**Çıktıya yazılacaklar:** Her büküm için `kFactor`, `allowanceMm`, `innerRadiusMm`, `angleDegrees` ve kullanılan `kFactorFormula` (§5.2). Böylece Macria ve testler hesabı tekrar üretip karşılaştırabilir.

**θ tanımı — parça 9 ile çözüldü** (§8.3):

- θ, büküm silindirinin eksen etrafındaki **açısal kapsamıdır**; yani bükülen (swept) açı. Silindirin UV aralığından okunur.
- CATIA DXF etiketindeki açı **θ'nın kendisidir**. "180° − etiket" ya da iç açı tanımları uymuyor.
- 90° dışı açılar (180°, 223,603°) ve 4 bükümlü birikim, CATIA DXF'iyle ~1e-9 mm farkla tutuyor.

**Hâlâ doğrulanması gerekenler:**

- **Rolled sac:** Parça 8'de kıvrılmış bölge için aynı formül mü geçerli? ⚠️
- **İki boyutlu açınım:** Parça 9 tek yönlü bir zincir. Farklı eksenli bükümleri olan parçalarda (ör. parça 4) doğrulanmadı ⚠️.
- **UP/DOWN yönü:** Hangi kurala göre belirlendiği ⚠️.

#### 3.3.2 Flanşları yerleştirme

- Sonraki flanş, büküm ekseni etrafında `−θ` döndürülür. Ardından büküm bölgesi yerine düzlemde `BA` uzunluğunda bir şerit eklenecek şekilde ötelenir (`gp_Trsf` birikimli).
- Referans kabuk kenarındaki uzunluklar, flanşın düz kısmı ile büküm şeridinin (BA) toplamı olarak kurulur.
- ⚠️ Açınımın A kabuğu kenarından mı, tarafsız eksenden mi ölçüldüğü CATIA DXF'iyle karşılaştırılarak kesinleştirilmeli. 55RS100111-12'de BA'nın birebir tutması, büküm şeridi uzunluğunun doğrudan BA olduğunu gösteriyor.
3. **Çıktı geometrisi** (referans kabuk üzerinde, 2B):
   - Dış döngü ve iç döngüler.
   - Çizgi ve yaylar (`Line` / `Arc`; B-spline kenar → `Unsupported` ⚠️).
   - Delikler (merkez + geçiş çapı).
   - Büküm çizgileri: **Her büküm için tek çizgi, BA şeridinin tam ortasında** (parça 9 ile doğrulandı, §8.3). Çizgi parça genişliği boyunca uzanır. CATIA'da çizgi rengi 1, kontur rengi 18; ikisi de layer "0".
   - Büküm etiketleri. CATIA biçimi: `UP 90deg  R 8` (iki boşluk). Açı θ'nın kendisi, 3 ondalığa kadar yazılıyor, sondaki sıfırlar atılıyor; ondalık ayırıcı yerel ayara bağlı (`223,603deg`). Etiket, büküm çizgisinin orta noktasında, yükseklik 2,5 olan bir TEXT. R = iç yarıçap.
4. **Kendi kendini kesme kontrolü:** Açınım konturu kendini keserse (flanş çakışması) sonuç `Invalid`; değer uydurulmaz.
5. **Rolled (kıvrılmış) sac:** A kabuğu tek bir açık silindir yüzüdür (parça 8). Açınım = yay uzunluğu × yükseklik dikdörtgeni. CATIA DXF'i de 4 LINE veriyor. Aday: yay uzunluğu = `(R + K·T)·θ`, yani §3.3.1'in tüm yüzeye uygulanması; R = iç yarıçap. ⚠️ Parça 8 DXF genişliğiyle doğrulanmalı.

#### 3.3.3 Açınım uygulama sırası

| Adım | İçerik | Kabul ölçütü |
|---|---|---|
| U1 | Tek büküm, 90° (L braket; gerçek parça 12, 3) | BA formülü ve DXF kontur ölçüleri CATIA ile tutar (12 için BA = 11,0117534) |
| U2 | Çok büküm, 90° (parça 2, 4) | Birikimli dönüşüm; tüm kontur ve delik merkezleri CATIA ile tutar |
| U3 | 90° dışı açılar (parça 9; sentetik S7) | ~~θ tanımı~~ çözüldü (§8.3). Motorun ürettiği açınım parça 9 DXF'iyle tutar: toplam boy 392,149316; büküm çizgileri 18,93376 / 42,33440 / 276,03160 / 371,05146 mm |
| U4 | Rolled (parça 8; sentetik S8) | Dikdörtgen genişliği CATIA ile tutar |
| U5 | Büküm üzerinde delik, B-spline kenar, tor büküm | v1'de `Unsupported`; sonraki aşama |

Bir adım kabul görmeden sonrakine geçilmez. Her adım GeometryLab `AGENTS.md` checkpoint'iyle kapatılır.

---

## 4. DXF üretimi — nerede?

**Öneri: DXF'i Macria yazsın.** Gerekçeler:

- GeometryLab `AGENTS.md` DXF'i korunan alan sayıyor.
- Macria'da DXF okuma, düzenleme ve güvenli kayıt altyapısı zaten var.
- **Karar Bekliyor.**

- **Yeni `Macria\DxfYazici.cs`** (saf sınıf, test edilebilir):
  - Girdi: motorun `flatPattern` bloğu.
  - Çıktı: ASCII DXF. Önerilen sürüm AC1015; CATIA çıktısıyla aynı: LINE, ARC, CIRCLE, TEXT.
  - `$HANDSEED` ve handle'lar tutarlı yazılmalı ki çıktı `DxfKaynakBelge.Olustur` ile açılıp DXF Edit Modunda düzenlenebilsin.
- **Dosya adı:** `DxfAdi.Uret(title, t, adet)`. Title ve adet CATIA eşleşmesinden gelir; eşleşme yoksa STEP dosya adı ve adet 1 kullanılır. **Karar Bekliyor.**
- **"Doğrulanmamış" etiketi:** Devir notu gereği, üretilen DXF bir CATIA referansıyla karşılaştırılıp tutana kadar satırda "doğrulanmamış" işareti taşımalı.
- **AGENTS.md (Macria) DXF kuralı:** Otomatik üretim yeni dosya yaratır, mevcut dosyanın üzerine yazmaz. Kural ile ilişkisi `DXF_HAVSA_ARASTIRMA.md` §2.3-6'daki gibi **Karar Bekliyor**.

---

## 5. JSON schema 1.1

### 5.1 Sürüm geçişi (zorunlu sıra)

**Bugünkü durum (2026-10-01):**

- **Macria** `"1.0"`, `"1.1"` ve `"1.2"`yi kabul ediyor (`GeometryLabProcessAdapter.cs:74`, `SupportedSchemaVersions`). Bu, aşağıdaki S1 seçeneğinin kodda uygulanmış hâlidir.
- Runtime'daki motor `"1.2"` yazıyor; montaj-1 analizi Macria tarafından kabul ediliyor.
- ⚠️ **GeometryLab .NET testinin** (`tests\Macria.GeometryLab.Tests\Program.cs:317`) hâlâ yalnızca `"1.0"` bekleyip beklemediği doğrulanmalı.

*Önceki davranış:* Macria 1.11.2 yalnızca tam `"1.0"` kabul ediyordu (`GeometryLabProcessAdapter.SupportedSchemaVersion`, `GeometryLabStepProfileListItem.Apply`). Aşağıdaki seçenekler o duruma göre yazıldı.

**Seçenekler (tarihsel; S1 uygulandı):**

| Seçenek | Açıklama | Risk |
|---|---|---|
| **S1 (önerilen)** | Önce Macria "1.0 ve 1.1"i kabul eden sürümle yayınlanır; sonra motor "1.1" yazar. Macria'da sabit bir kümeye döner: `SupportedSchemaVersions = {"1.0","1.1"}`. | Sıra bozulursa eski Macria yeni motorla çalışmaz; paket her zaman birlikte gönderildiği için düşük risk |
| S2 | `schemaVersion` "1.0" kalır, yeni alanlar yalnızca eklenir | Sürüm numarası içeriği anlatmaz; GeometryLab `AGENTS.md`'deki "geriye uyumlu genişletme" ilkesine uyar ama tüketici yeni alanın varlığını tahmin etmek zorunda kalır |

### 5.2 Eklenecek alanlar (tamamı isteğe bağlı; 1.0 alanları aynen kalır)

```jsonc
{
  "schemaVersion": "1.1",
  // mevcut 1.0 alanları aynen…
  "faces": [ { /* 1.0 alanları */
      "surfaceGeometry": {                       // yeni, isteğe bağlı; düzlem dışı yüzler için
        "axisOrigin": {"x":0,"y":0,"z":0}, "axisDirection": {"x":0,"y":0,"z":1},
        "radiusMm": 8.0, "semiAngleDegrees": null,
        "materialSide": "Outside|Inside|Unknown" // içbükey/dışbükey kanıtı
      } } ],

  "sheetMetal": { /* tek solidli parçalar için kolaylık; sheetMetalAnalyses[0] ile aynı */ },
  "sheetMetalAnalyses": [ {
      "solidId": 1,
      "status": "Recognized|NotSheet|Ambiguous|Unsupported|NotStarted",
      "rejectionReason": null,
      "thicknessMm": 20.0,
      "thicknessEvidence": { "offsetPairCount": 5, "maximumDeviationMm": 1e-9 },
      "closedSection": false,
      "skins": { "aFaceIds": [..], "bFaceIds": [..], "edgeFaceIds": [..], "unclassifiedFaceIds": [] },
      "bends": [ { "localId": 1, "innerFaceId": 7, "outerFaceId": 9,
                   "innerRadiusMm": 4.0, "outerRadiusMm": 24.0, "angleDegrees": 90.0,
                   "direction": "Up|Down|Unknown",
                   "axisOrigin": {..}, "axisDirection": {..} } ],
      "flatPattern": {
        "status": "Succeeded|Unsupported|Invalid|NotStarted",
        "referenceSkin": "A",
        "kFactorFormula": "catia-log",           // varsayılan; "constant:<k>" Macria ayarından gelirse
        "bendAllowances": [ { "bendId": 1, "innerRadiusMm": 4.0, "thicknessMm": 20.0,
                              "angleDegrees": 90.0, "kFactor": 0.150515, "allowanceMm": 11.0117534 } ],
        "loops": [ { "role": "Outer|Inner", "segments": [
            { "type": "Line", "start": {"x":..,"y":..}, "end": {..} },
            { "type": "Arc", "center": {..}, "radiusMm": .., "startAngleDegrees": .., "endAngleDegrees": .. } ] } ],
        "holes": [ { "holeFeatureId": 1, "center": {"x":101.153,"y":-146.218}, "diameterMm": 16.0 } ],
        "bendLines": [ { "bendId": 1, "start": {..}, "end": {..}, "label": "UP 90deg  R 4" } ],
        "boundingBox": { "widthMm": .., "heightMm": .. },
        "evidence": [], "rejectionReason": null
      },
      "evidence": [ { "code": "..", "description": "..", "status": "..", "numericValue": null, "unit": "" } ]
  } ],

  "holeFeatures": [ {
      "localId": 1, "solidId": 1,
      "type": "Through|Countersink|Counterbore|Counterdrill|Unknown",
      "status": "Recognized|Unsupported|Ambiguous",
      "throughDiameterMm": 16.0, "headDiameterMm": 40.0,
      "headDepthMm": null, "headAngleDegrees": 90.0,
      "axisOrigin": {..}, "axisDirection": {..},
      "openingSide": "A|B|Unknown", "onBend": false,
      "faceIds": [11, 12, 13], "reason": null
  } ]
}
```

- **Birimler:** mm ve derece. Hesaplanamayan değer `null`.
- **ID'ler:** `faces[].id` ile aynı uzayda.
- **Açıklama/kanıt:** `evidence[]` biçimi mevcut `RecognitionEvidenceRecord` ile aynı.
- **⚠️ Alan adları öneridir.** Mevcut `profileRecognition` / `profileRecognitions` ikilisine paralel tutulmuştur.

### 5.3 C++ tarafı değişiklik haritası (motor)

| Dosya | Değişiklik |
|---|---|
| `include\GeometryEngine.hxx` | Yeni kayıtlar: `SurfaceGeometryRecord`, `SheetMetalAnalysisRecord`, `BendRecord`, `FlatPatternRecord` (+ Loop/Segment/Hole/BendLine), `HoleFeatureRecord`. `AnalysisResult`'a yeni vektörler. `FaceRecord`'a `std::optional<SurfaceGeometryRecord>`. |
| `src\SheetMetalRecognition.cxx` (yeni) | §3.1 |
| `src\HoleFeatureRecognition.cxx` (yeni) | §3.2 |
| `src\SheetMetalUnfold.cxx` (yeni) | §3.3 |
| `src\GeometryEngine.cxx` | `PopulateTopology` içinde `surfaceGeometry` doldurulması. `AnalyzeStep`'e üç çağrı: profil hattından sonra, mevcut sırayı bozmadan. `SerializeJson`'a yeni bloklar ve `"1.1"`. |
| `src\main.cxx` | `--k-factor-formula catia-log\|constant:<k>` argümanı. Verilmezse `catia-log` (§3.3.1); geçersizse `InvalidArguments`. Mevcut `--input`/`--output` sözleşmesi değişmez. |
| `src\SheetMetalUnfold.cxx` | K ve BA hesabı tek bir saf fonksiyonda (`ComputeCatiaLogKFactor(R, T)`, `ComputeBendAllowance(R, T, θ, K)`), birim testlenebilir |
| `CMakeLists.txt` | Yeni `.cxx` dosyaları `MacriaGeometryEngineCore`'a; yeni test exe'si (`MacriaGeometrySheetMetalTests`) |
| `src\Macria.GeometryLab\Contracts\GeometryDtos.cs`, `GeometryAnalysisValidator.cs` | DTO ve validator genişletmesi ⚠️ (içerik bu çalışmada okunmadı) |
| `tests\Macria.GeometryLab.Tests\Program.cs:317` | Şema beklentisi `"1.1"` |

---

## 6. Macria tarafı değişiklikleri

| Dosya | Değişiklik |
|---|---|
| `GeometryLabTransportDtos.cs` | Yeni record'lar: `GeometryLabSheetMetalTransport`, `…BendTransport`, `…FlatPatternTransport` (+ loop/segment/hole/bendLine), `GeometryLabHoleFeatureTransport`, `GeometryLabSurfaceGeometryTransport`. `GeometryLabAnalysisTransport`'a `SheetMetal`, `SheetMetalAnalyses` (varsayılan boş dizi), `HoleFeatures` (varsayılan boş dizi). 1.0 JSON'u eksiksiz okunmaya devam eder. |
| `GeometryLabProcessAdapter.cs` | `SupportedSchemaVersions` kümesi (§5.1-S1). Mevcut `SupportedSchemaVersion` sabiti testler ve geriye uyum için korunur. `GeometryLabProcessAdapterOptions`'a `KFactorFormula` (varsayılan `null` → argüman gönderilmez, motor `catia-log` kullanır); doluysa `--k-factor-formula` eklenir. |
| `Ayarlar.cs` + `SettingsWindow` | Yeni ayar `KFaktorFormulu` (varsayılan `catia-log`; alternatif sabit K). `ayarlar.txt`'ye yazılır. Kullanıcı değiştirmedikçe motora argüman gönderilmez. Ayar ekranında formül ve 55RS100111-12 doğrulama notu gösterilebilir. |
| `GeometryLabStepProfileListItem.cs` | Şema kontrolü kümeye bağlanır. Yeni gösterim özellikleri: `SheetStatusDisplay` ("Sac (geometrik)" / "—"), `ThicknessDisplay`, `HoleSummaryDisplay` (ör. "1 havşa, 1 imbus, 2 düz"), `HasCountersinkOrCounterbore`, `FlatPatternAvailable`. Sac parçaların hangi `ResultGroup`'a düşeceği **Karar Bekliyor**. Bugün CATIA eşleşmesiyle sac doğrulanırsa satır `Excluded` oluyor (`:310-316`). Öneri: yeni `GeometryLabExternalStepResultGroup.SheetMetal` + filtre kutusu. |
| `MainWindow.xaml` (`gridExternalStepProfil`) | Mevcut sütunlar: Durum, STEP Dosyası, Parça Türü, Kesit, Boy, CATIA Adedi, Eşleşme, Topoloji, Açılı Kesim, İşlem Durumu, Açıklama. **Yeni:** "Sac (geometrik)", "Kalınlık", "Havşa/İmbus". **Yeni düğme:** "DXF Üret" (seçili ve açınımı olan satırlar). **Yeni filtre:** "Saclar". |
| `MainWindow.ExternalStepProfiles.cs` | "DXF Üret" akışı: klasör seç → `DxfYazici` → `DxfAdi` → çakışmada sor (üzerine yazma yok) → satıra yol + "doğrulanmamış" işareti. Excel raporuna yeni sütunlar (`ExternalStepExcelRaporuHazirla`). |
| `DxfYazici.cs` (yeni) | §4 |
| `DxfAcinimKarsilastirici.cs` (yeni, saf) | Motor açınımı ile CATIA DXF'ini karşılaştırır (§7.3). Hem testte hem isteğe bağlı UI doğrulamasında kullanılır. |
| `ProductionPackage*` | Üretilen DXF'lerin üretim paketine alınması ⚠️. Bugün STEP satırları paket için yalnızca Definite/Processed profil ise uygun (`MainWindow.ProductionPackage.cs:30`). |
| `DxfEdit.Tests.csproj`, `GeometryLabAdapter.Tests.csproj` | Yeni dosyaların `<Compile Include>` bağlantıları |

**Korunacak davranış:** CATIA "Save As DXF" yolu (`ExportOneIc`) ve fallback zinciri **değişmez**. Motor yolu ayrı bir üretim yöntemidir. İkisinin sonuçlarının karşılaştırılması doğrulama aracıdır.

---

## 7. Test planı

### 7.1 Motor — sentetik (CTest, `FixtureGenerator` ile)

Her senaryo **en az üç farklı kalınlıkta** (ör. 0,5 / 3 / 20 mm) ve farklı R/T oranlarında üretilir. Böylece sonucun sabit bir eşiğe bağlı olmadığı kanıtlanır.

| # | Fixture | Beklenen |
|---|---|---|
| S1 | Düz levha | `Recognized`, t doğru, büküm 0, delik 0 |
| S2 | Levha + düz delik | 1× `Through`, çap doğru |
| S3 | Levha + havşa (A'dan) / (B'den) | `Countersink`, geçiş/kafa çapı, açı; `openingSide` A / B |
| S4 | Levha + imbus (A'dan) / (B'den) | `Counterbore`, `headDepthMm` |
| S5 | Köşeleri yuvarlatılmış levha | **Delik 0** (dışbükey köşeler elenir; parça 4 regresyonu) |
| S6 | L braket (1 büküm, 90°) | 1 büküm, iç/dış R, açınım genişliği = `a + b + BA` |
| S7 | Z / U braket, 90° dışı açı (ör. 223,6°, parça 9 gibi) | Bükümler + BA doğru |
| S8 | Açık rolled silindir segmenti | `Recognized`, delik 0, açınım dikdörtgen (parça 8 regresyonu) |
| S9 | Kapalı kutu profil / boru | `NotSheet` + `ClosedSection` (01–03, 13* regresyonu); profil sonucu değişmez |
| S10 | Büküm üzerinde delik | `onBend=true`, `Unsupported` |
| S11 | İki solid | Her solid ayrı kayıt; karışma yok |
| S12 | Farklı kalınlıklı iki bölge (tek solid) | `Ambiguous` |
| S13 | Kör delik / cep | Delik sayılmaz |
| S14 | K/BA saf fonksiyon testi | `K(R=4, T=20) = 0,150515` (log10(4)/4); `BA(4, 20, 90°) = 11,0117534`; sınırlar: `20R/T ≤ 1` → K = 0 (ör. R = T/40), `20R/T ≥ 100` → K = 0,5 (ör. R = 10T); `constant:<k>` seçeneği; geçersiz argüman → `InvalidArguments` |
| S15 | L braket, farklı R/T oranları | Açınım genişliği = `a + b + BA`; BA, S14 fonksiyonuyla aynı |

**Regresyon:** Mevcut `Aag`, `Dihedral`, `Profile` ve `ViewerInput` testleri ile gerçek STEP kabul betikleri değişmeden geçmeli (GeometryLab `AGENTS.md`).

### 7.2 Motor — gerçek STEP kabul (env var ile)

**Veri kaynakları:**

- **Test klasörü:** `C:\Users\enesy\OneDrive\Desktop\Macria-4A-Gercek-Test\`
  - `step\55RS100111-12 A.stp` (havşa)
  - `step\55RS100111-12 A_(1).stp` (havşa + imbus)
- **Geniş derlem:** `deneme-2\*.stp` (22 STEP)
- **CATIA DXF referansları:** Adımda belirtilen `step\` klasöründe değil, **`deneme-2\sac-yeni\`** altında:
  - `55RS100111-12 havsa Buyuk.dxf` (Top, Ø40) ve `havsa Kucuk.dxf` (Bottom, Ø16)
  - `55RS100111-12 A.dxf` (Ø16)
  - `55RS100111-12_20mm_2adet.dxf` (Macria export'u, Ø40 → Top ile alınmış)
  - Parça 1, 2, 3, 4, 8, 9, 10 DXF'leri

**Beklenen sonuçlar:** Kaynak = CATIA DXF'i. DXF'i olmayan parçalar ⚠️; Enes'in onayı gerekiyor.

| STEP | Sac? | t | Büküm | Delik (geçiş çapı) | Kaynak |
|---|---|---|---|---|---|
| `-12 A` / `-12_Rep` | Evet | 20 | 1 (UP 90° R4) | 1 havşa Ø16 (kafa Ø40) | DXF + devir notu |
| `-12 A_(1)` | Evet | 20 | 1 | havşa Ø16/Ø40 + imbus Ø16/Ø34 | Prototip + devir notu ⚠️ (DXF yok) |
| `-1` | Evet | 13 | 0 | Ø10 | DXF |
| `-2` | Evet | 8 | 2 (UP 90° R8) | Ø10 | DXF |
| `-3` | Evet | 8 | 1 (DOWN 90° R6) | Ø10, Ø13 | DXF |
| `-4` | Evet | 1,2 | 3 (UP, UP, DOWN 90° R4) | Ø16, 2× Ø6 | DXF |
| `-8` | Evet (rolled) | 3 | — ⚠️ | yok | DXF (4 LINE) |
| `-9` | Evet | 6 | 4 (90, 90, 223,603, 180°; R4) | yok | DXF |
| `-10` / `-10_1` | ⚠️ | 10 ? | ⚠️ | Ø58,66 | DXF (`-10 A.dxf`); hangi STEP'e karşılık geldiği ⚠️ |
| 01–03, `-6`, `-13*` | **Hayır** (kapalı profil) ⚠️ | — | — | — | Adlar ve yüz yapısı; doğrulanmalı |
| `-5`, `-7`, `-11`, `-14` | ⚠️ | ⚠️ | ⚠️ | ⚠️ | Referans yok |

### 7.3 Açınım ↔ CATIA DXF karşılaştırması

`DxfAcinimKarsilastirici`, `DxfOkuyucu` ile okuyup şunları karşılaştırır:

1. **Dış kontur:** Sınır kutusu en/boy, çevre uzunluğu, kapalı alan.
2. **Delikler:** Sayı ve çap. Motor geçiş çapı **Bottom/"Küçük"** DXF'iyle karşılaştırılır. Top DXF'iyle karşılaştırmada havşalı deliklerin kafa çapını vermesi beklenir; bu da ayrıca kontrol edilir.
3. **Delik merkezleri:** Rijit hizalamadan sonra karşılaştırılır. Hizalama yöntemi `DXF_HAVSA_ARASTIRMA.md` §3.2: D4 adayları + dış kontur.
4. **Büküm çizgileri:** Sayı, uzunluk ve etiket metni (`UP 90deg  R 4`).
5. **Büküm payı:** Motorun `bendAllowances[].allowanceMm` değeri ile CATIA DXF'inden ölçülen büküm şeridi uzunluğu karşılaştırılır. Parça 12 için beklenen değer **11,0117534 mm** ve doğrulanmış kabul değeridir. Çok bükümlü (2, 4, 9) ve 90° dışı (9) parçalar §3.3.3 U2–U3'te aynı yöntemle eklenir.

**Kabul toleransı:** Test ölçütüdür, algoritma eşiği değildir. Öneri: 1e-3 mm. **Karar Bekliyor.**

**Testlerin yeri:** `GeometryLabAdapter.Tests`, env var'lı gerçek dosya testleri. Yol yoksa `SKIPPED`; yol verildiği halde dosya yoksa `FAIL`.

### 7.4 Macria birim testleri

- **`GeometryLabAdapter.Tests`:**
  - 1.0 JSON'u değişmeden okunur (mevcut testler).
  - 1.1 JSON'u yeni alanlarla okunur; eksik alanlar boş/null gelir; `"1.2"` → `UnsupportedSchema`.
  - Liste satırı gösterimleri (Sac / Kalınlık / Havşa-İmbus özeti).
  - `ResultGroup` kararı.
  - Excel sütunları.
- **`DxfEdit.Tests`:**
  - `DxfYazici` çıktısı `DxfOkuyucu.Oku` ve `DxfKaynakBelge.Olustur` ile hatasız okunur (sürüm, handle, `$HANDSEED`).
  - Delikler CIRCLE olarak yazılır, çap = geçiş çapı.
  - Büküm etiketleri TEXT olarak yazılır.
  - Satır sonu ve kültür bağımsızlığı: ondalık nokta her zaman `.`; etiketteki yerel virgül ayrıca **Karar Bekliyor**.
  - `DxfAcinimKarsilastirici` sentetik çiftlerle test edilir.

### 7.5 Enes'in yapması gerekenler

| # | İş |
|---|---|
| E1 | 01–03, `-5`, `-6`, `-7`, `-10`, `-11`, `-13*`, `-14` için "sac mı, profil mi, diğer mi?" etiketi ve varsa CATIA DXF'i (hem Top hem Bottom) |
| E2 | Parça 2 ve 3'teki koni yüzünün ne olduğu (CATIA ağacında hangi unsur) |
| E3 | `-12 A_(1)` için CATIA Top/Bottom DXF'leri |
| E4 | ~~K formülü ve θ tanımı~~ **Tamamlandı.** Formül aynı (Enes); θ = bükülen açı = etiketteki açı (parça 9 STEP + DXF'ten çözüldü, §8.3). CATIA ölçüsüne gerek kalmadı. |
| E5 | ~~Büküm çizgisinin konumu~~ **Tamamlandı:** BA şeridinin ortası (parça 9, §8.3) |
| E6 | Kararlar: §3.1 profil/sac önceliği, §4 DXF'i kimin yazacağı, §5.1 sürüm geçişi, §6 `ResultGroup`, K formülünün parametre biçimi, test toleransı |

---

## 8. Prototip çalıştırma sonuçları

**Ortam:**

- Python 3.12.10
- `pip install cadquery-ocp` → **cadquery-ocp 8.0.1.0.0** (motorla aynı OCCT sürümü)
- Kurulumla birlikte gelen bağımlılıklar: `vtk 9.6.2`, `numpy 2.5.3`, `matplotlib 3.11.2` vb. Kullanıcının genel Python ortamına kuruldu.
- Komut: `python tools\prototip\delik_tanima.py <stp>`

### 8.1 İstenen iki STEP (`…\Macria-4A-Gercek-Test\step\`)

**`55RS100111-12 A.stp`**

```json
{
  "yuzSayisi": 18,
  "yuzTipleri": { "Düzlem": 12, "Silindir": 4, "Koni": 2 },
  "sacKalinligi": 20.0,
  "bukumler": [
    { "icYaricap": 4.0, "disYaricap": 24.0, "eksen": [-0.0, 1.0, 0.0], "merkez": [-4.0, -228.906, 210.264] }
  ],
  "delikler": [
    { "tip": "Havşa (Countersink)", "gecisCapi": 16.0, "kafaCapi": 40.0, "havsaAcisi": 90.0,
      "eksen": [-1.0, 0.0, -0.0], "acildigiYuzNoktasi": [20.0, -235.962, 345.47],
      "acildigiYuzNormali": [1.0, -0.0, 0.0] }
  ]
}
```

**`55RS100111-12 A_(1).stp`**

```json
{
  "yuzSayisi": 23,
  "yuzTipleri": { "Koni": 2, "Silindir": 8, "Düzlem": 13 },
  "sacKalinligi": 20.0,
  "bukumler": [
    { "icYaricap": 4.0, "disYaricap": 24.0, "eksen": [-0.0, 1.0, 0.0], "merkez": [-4.0, -228.906, 210.264] }
  ],
  "delikler": [
    { "tip": "İmbus yuvası (Counterbore)", "gecisCapi": 16.0, "kafaCapi": 34.0, "havsaAcisi": null,
      "eksen": [-1.0, 0.0, -0.0], "acildigiYuzNoktasi": [20.0, -158.47, 242.405],
      "acildigiYuzNormali": [1.0, -0.0, 0.0] },
    { "tip": "Havşa (Countersink)", "gecisCapi": 16.0, "kafaCapi": 40.0, "havsaAcisi": 90.0,
      "eksen": [1.0, 0.0, 0.0], "acildigiYuzNoktasi": [20.0, -235.962, 345.47],
      "acildigiYuzNormali": [1.0, 0.0, 0.0] }
  ]
}
```

**Yorum:**

- Değerler devir notuyla birebir aynı: Ø16/Ø40 havşa (90°), Ø16/Ø34 imbus, t=20, R4/R24, açılış x=20 yüzü.
- İkinci dosyada havşanın `eksen` işareti ilk dosyanın tersi. Prototip eksen yönünü kanonikleştirmiyor. `acildigiYuzNormali` doğru olduğu için sonuç etkilenmiyor, ama motorda eksen yönü kanonik olmalı.
- **Mevcut motorun** aynı iki dosyadaki çıktısı (`GeometryEngineRuntime\Macria.GeometryEngine.exe`, schema 1.0):

  | Dosya | Yüz | Komşuluk | Profil sonucu |
  |---|---|---|---|
  | A | 18 (12 Plane, 4 Cylinder, 2 Cone) | 38 (26 Convex, 12 Smooth) | `Unknown` / `InsufficientEvidence` / "No reliable profile axis exists for this solid." |
  | A_(1) | 23 (13 Plane, 8 Cylinder, 2 Cone) | 50 (32 Convex, 16 Smooth, **2 Concave**) | Aynı |

  A_(1)'deki 2 Concave kenar, imbus omzu (§3.2-4 kanıtı).

### 8.2 Derlemde ek çalıştırma (bilgi amaçlı, `deneme-2\*.stp`)

Prototipin sınırlarını görmek için 22 STEP'te de çalıştırıldı ve CATIA DXF'leriyle karşılaştırıldı.

| STEP | Prototip: t / büküm / delikler | CATIA DXF (çember çapı, etiket) | Değerlendirme |
|---|---|---|---|
| `-1_Rep` | 13 / 0 / Düz Ø10 | Ø10 | ✓ |
| `-2_Rep` | 8 / 2 / **Havşa 6,647/8,647**, Düz Ø10 | Ø10; 2× "UP 90deg R 8" | ✗ Fazladan "havşa" (§3.2-8) |
| `-3_Rep` | 8 / 1 / **Havşa 6,647/8,647**, Ø10, Ø13 | Ø10, Ø13; "DOWN 90deg R 6" | ✗ Aynı fazlalık |
| `-4_Rep` | 1,2 / 3 / **2× Ø20**, 2× Ø6, Ø16 | 2× Ø6, Ø16; ARC r10 ×2 (köşe); 3 etiket | ✗ Dış köşe yuvarlatmaları delik sanıldı (§3.2-1) |
| `-8_Rep` | **None** / 0 / **İmbus 94,354/100,354** | Çember yok, 4 LINE | ✗ Rolled sac: kalınlık düzlemden aranıyor, kabuk silindirleri "imbus" sanıldı (§3.1-1, §3.3-5) |
| `-9_Rep` | 6 / 4 / yok | Çember yok; UP/DOWN 90°, 223,603°, 180° R4 | ✓ (büküm sayısı) |
| `-10_1_Rep` | 10 / 0 / Düz Ø58,66 | `-10 A.dxf`: Ø58,66 | ✓ ⚠️ (eşleşme varsayımı) |
| `-10_Rep` | 3 / 2 / yok (8 "Diğer" yüz) | — | ⚠️ |
| `-11_Rep` | 3 / 2 / yok (8 "Diğer") | — | ⚠️ |
| `-12_Rep` | 20 / 1 / Havşa Ø16/Ø40 | Ø16 (A, Küçük), Ø40 (Büyük, Macria export'u) | ✓ |
| 01, 02, 03 (`Duz-Duz`, `Duz-45`, `45-45`) | 2 / 4 / yok | — | ✗ Kapalı profil "sac + 4 büküm" (§3.1-5) ⚠️ |
| `-6_Rep` | 2 / 4 / yok | — | ✗ ⚠️ Aynı örüntü |
| `-13 A` / `A_duz` / `A_15deg` / `A_tek-delik` | 3 / 4–7 / yok | — | ✗ ⚠️ Profil örüntüsü; `A_tek-delik`'te delik bulunamadı (profil üzerindeki delik kabuk silindiri içinde kalıyor olabilir) |
| `-13 A MARKALAMA` | 3 / 4 / yok (101 düzlem, 41 "Diğer") | — | ⚠️ Markalama yüzleri |
| `-5_Rep` | None / 0 / Düz Ø118,287 | — | ⚠️ Olasılıkla dış silindir (dışbükey) |
| `-7_Rep` | None / 0 / İmbus 101,317/107,317 | — | ⚠️ Olasılıkla rolled veya boru |
| `-14` | None / 0 / İmbus 22/28 | — | ⚠️ Olasılıkla burç/boru |

**Sonuç:**

- Prototipin havşa ve imbus çekirdek kuralı doğru (parça 12).
- Genel kullanım için §3.1–3.2'deki şu eklemeler **zorunlu**:
  - İçbükeylik kontrolü
  - Kabuk tabanlı kalınlık (silindir çiftleri dahil)
  - Açık kabuk koşulu
  - Eşiklerin kaldırılması

### 8.3 Parça 9 ile θ ve büküm çizgisi tanımının çözülmesi (2026-09-27)

**Veri:**

- STEP: `deneme-2\55RS100111-9_Rep.stp`
- DXF: `deneme-2\sac-yeni\55RS100111-9_6mm_2adet.dxf`. ⚠️ Top ile mi alındığı dosyadan kanıtlanamıyor. Delik olmadığı ve konturu Top/Bottom değiştirmediği için sonucu etkilemez.
- OCCT (cadquery-ocp 8.0.1) ile tam hassasiyette okundu. Kod repoya eklenmedi; yalnızca geçici betik kullanıldı.

**STEP'ten:**

- T = 6 (iç silindirler R = 4, dış silindirler R = 10; 4 büküm).
- Tüm bükümler aynı eksen yönünde. Parça genişliği 151,5155 mm; açınım tek yönlü bir zincir.

| Sıra | Büküm (DXF etiketi) | İç/dış silindir kapsamı (STEP) | Önceki düz flanş boyu (STEP) |
|---|---|---|---|
| — | (üst uç flanşı) | — | L0 = 10,000000000 |
| 1 | `UP 180deg  R 4` | 180,000000000° / 180,000000000° | — |
| 2 | `UP 90deg  R 4` | 90° / 90° | L1 = 10,000000000 |
| 3 | `DOWN 90deg  R 4` | 90° / 90° | L2 = 224,763436282 |
| 4 | `UP 223,603deg  R 4` | 223,602818976° / 223,602818972° | L3 = 79,455131019 |
| — | (eğik uç flanşı) | — | L4 = 10,000000003 |

**DXF'ten** (üst kenardan uzaklık):

- Toplam açınım boyu **392,149315968**
- Büküm çizgileri 18,933759760 / 42,334399400 / 276,031595443 / 371,051461155

**Hesap:**

- K = log10(20·4/6)/4 = 0,281234684; R + K·T = 5,687408105.
- "Büküm çizgisi şeridin ortasında" varsayımıyla her BA sırayla çözüldü: `BAᵢ = 2·(dᵢ − dᵢ₋₁ − BAᵢ₋₁/2 − Lᵢ₋₁)`.
- Ardından `θᵢ = BAᵢ / (R + K·T)`.

| Büküm | BA (DXF'ten) | θ (çözülen) | STEP kapsamı | 180° − etiket | BA formül(kapsam) − BA(DXF) |
|---|---|---|---|---|---|
| UP 180 | 17,8675195 | **180,0000000°** | 180,0000000° | 0° | −5,7e−14 |
| UP 90 | 8,9337598 | **90,0000000°** | 90,0000000° | 90° | −3,6e−15 |
| DOWN 90 | 8,9337598 | **90,0000000°** | 90,0000000° | 90° | 3,9e−11 |
| UP 223,603 | 22,1957096 | **223,6028190°** | 223,6028190° | −43,603° | −1,4e−09 |

**Bağımsız kontroller:**

1. **Kapanış:** Son çizgiden alt kenara kalan boy = BA₄/2 + L4. Artık **−2,8e−9 mm**. "Çizgi şeridin ortasında" varsayımı doğrulandı.
2. **Toplam boy:** ΣL + Σ(R + K·T)·θ(kapsam) = 392,149315972. DXF ile fark **−4,1e−9 mm**.
3. **Alternatif θ = |180° − kapsam|:** Toplam 356,414 çıkıyor, DXF'ten **35,7 mm** sapıyor. **Reddedildi.**
4. **"Çizgi şeridin başında" varsayımı:** Ardışık farklar yalnızca flanş boylarıyla açıklanamıyor (8,93 / 13,40 / 8,93 / 15,56 mm artık). **Reddedildi.**

**Sonuç:**

- θ = büküm silindirinin açısal kapsamı = bükülen açı. CATIA etiketindeki açı doğrudan θ'dır.
- 90°'de tanımlar çakıştığı için ayırt edici kanıtı 180° ve 223,603° bükümleri verdi.
- `BA = (R + K·T)·θ` formülü 90° dışı açılarda ve 4 bükümlü birikimde de CATIA ile birebir tutuyor.
- ⚠️ Bu tek bir parça ve tek yönlü bir zincir. Farklı eksenli çok bükümlü parçalar (ör. parça 4) ve rolled sac (parça 8) ayrıca doğrulanmalı (§3.3.3 U2, U4).

---

## 9. AGENTS.md uyumu ve bu çalışmanın yan etkileri

**Kod değişikliği yok.**

- Macria: Değişmedi. Git durumu yalnızca yeni `.md` dosyalarını gösteriyor.
- GeometryLab: Değişmedi.
- Obsidian: Güncellenmedi (yalnızca tasarım; kabul edilmiş değişiklik yok).

**Ortam değişikliği (kullanıcı isteğiyle):** `cadquery-ocp` ve bağımlılıkları kullanıcı Python'una kuruldu:

- Kaldırmak için: `python -m pip uninstall cadquery-ocp cadquery-ocp-proxy vtk`
- ⚠️ `numpy`, `matplotlib` vb. başka araçlar da kullanıyor olabilir; ayrı değerlendirilmeli.

**Okuma amaçlı çalıştırmalar:**

- Mevcut motor iki STEP üzerinde çalıştırıldı. JSON çıktıları yalnızca geçici oturum klasörüne yazıldı; test klasörüne yazılmadı.
- Prototip `deneme-2` STEP'lerini yalnızca okudu.

**Uygulama aşamasında:**

- GeometryLab `AGENTS.md` checkpoint'leri (`<AŞAMA>-BEFORE` / `-COMPLETE`, SHA-256 manifesti), Macria koruma manifesti kontrolü ve Macria `AGENTS.md` build/test ve Obsidian adımları geçerli.
- İlgili Obsidian notları: `03 - DXF ve Sheet Metal.md`, `05 - Geometri Tanıma.md`, `99 - Yapılacaklar.md`.

**Karar Bekliyor (toplu):**

1. Profil ve sac önceliği (§3.1)
2. DXF'i kimin yazacağı (§4)
3. Otomatik DXF üretimi ile Macria DXF Edit kuralı (§4)
4. Schema geçişi S1/S2 (§5.1)
5. Sac satırlarının `ResultGroup`'u (§6)
6. ~~K formülünün motora iletilme biçimi~~. **Karara bağlandı (2026-09-27):** CATIA log formülü motorda varsayılan, Macria ayarından değiştirilebilir (§3.3.1). Açık kalan tek ayrıntı: argüman adının onayı (`--k-factor-formula`).
7. DXF dosya adı ve adet kaynağı (§4)
8. Test kabul toleransı (§7.3)
9. Etiketlerde ondalık ayırıcı (§7.4)

## 10. Sonraki oturum

İlk dilim (U1 + delik tanıma) tamamlandı. Sonraki oturum bu sırayla ilerler:

1. **UP/DOWN etiketi, motorun kendi çizdiği yüze göre.** Etiket, motorun DXF'i çizerken baktığı yüze göre belirlenecek: flanş bakana doğru kalkıyorsa `UP`, uzaklaşıyorsa `DOWN`. Böylece etiket, CATIA'nın Top/Bottom seçiminden bağımsız olarak motorun kendi çizimiyle tutarlı olur. Test: parça 12 ve parça 3 (parça 3'te şu an `UP` yazıyoruz, CATIA `DOWN` yazıyor).
2. **Dişli delikler DXF'e yazılmayacak.** Dişli delik (ör. parça 3'teki 6,647/8,647 çiftine uyan M8) tanınınca DXF'e daire yazılmaz. JSON'daki kaydında bunu belirten bir uyarı bulunur.
3. **Blok/sac ayrımı Macria entegrasyonunda.** Levha gibi tanınan kalın bloklar (ör. 10×20×30 kutu, 10 mm "levha") için "sac mı, blok mu?" kararı motorda değil, Macria entegrasyonunda verilecek.
4. **Ardından çok bükümlü parçalar.** Yukarıdakiler bitince çok bükümlü parçalara geçilecek. Bugünkü durum: 3, 4, 5 ve 9 sac olarak tanınıyor ama açınımları `Unsupported`; 2, 10, 11 ve 9_rev ise hiç tanınmıyor (`NotSheet`).

## 11. Macria entegrasyonu (Aşama 1–4)

Aşama 1 (motor: montaj STEP'i, parça sınıfı, `--dxf-klasor`) GeometryLab'da uygulandı. Aşama 2–4 henüz kodlanmadı; bu bölüm kararları ve notları tutar.

**Kararlar (2026-09-28):**

- **Arayüz:** B, yani sekmeler (Profiller / Saclar / Kontrol gerekli). Mevcut profil ekranı değişmeden kalır.
- **Sınıf:** Motor yalnız geometriye bakar. Sac/profil çakışmasını Macria, CATIA taramasındaki `SheetMetalConfirmed` bayrağıyla çözer.
- **DXF:** Motor DXF'i geçici klasöre `part-<id>.dxf` olarak yazar. Macria dosyayı `DxfAdi` ile adlandırıp `Motor-DXF\` alt klasörüne, CATIA DXF'leriyle aynı adla taşır. Var olan dosyanın üzerine sormadan yazmaz.
- **Zaman aşımı:** Adaptör zaman aşımı bir ayar olur.
- **Aynalı çizim:** Motorun çizdiği yüz CATIA'nınkinden farklı olabilir. Şimdilik operatör notu yeterli.

**Şirket kuralı (2026-09-28), motorda uygulandı:**

- Profil yalnız içi boş kesitlerdir (kutu, boru).
- Dolu kesitler (SolidRectangular/Square/CircularBar) profil sayılmaz. Parça sac olarak tanındıysa bükümlü ya da bükümsüz fark etmeksizin sınıfı Sac olur. Sac olmayan dolu çubuk Diğer olur.
- Motorun yorumu (onay bekliyor): Uzun dolu bir milin iki uç yüzü de sac kabuğu gibi eşleşir; o zaman mil boyu "kalınlık" olarak ölçülür. Bu yüzden sac iddiası ancak kalınlık açınımın en dar ölçüsünü aşmıyorsa geçerlidir. Tanım gereği levha, kalınlığından daha geniştir. Örnek: Ø20 × 500 mil Diğer çıkar, Ø118 × 50 disk (parça 5) Sac kalır.

**Aşama 3–4'e not, Lazer / Şalama-Kütük (şirket kuralı, henüz kod yok):**

- Macria ayarı: **Lazer azami kalınlık**, varsayılan 20 mm.
- Kalınlığı bu değere eşit ya da küçük olan Sac parçalar **Lazer** grubuna, büyük olanlar **Şalama/Kütük** grubuna girer.
- **İki grubun da DXF'i üretilir.** Grup yalnız listeleme, filtre ve rapor için kullanılır.
- Karşılaştırmada motorun ölçtüğü kalınlık kullanılır. 20 mm'lik bir parça Lazer grubuna girer (eşitlik dahil).
- Ayarın varsayılan değeri kod içine sabit yazılmaz, kullanıcı ayarından gelir.
- Etkilenen aşamalar:
  - Aşama 3: DTO'da grup alanı yoktur; grubu Macria hesaplar.
  - Aşama 4: Saclar sekmesinde Grup sütunu ve filtre olur; "DXF üret" iki grubu da kapsar.
