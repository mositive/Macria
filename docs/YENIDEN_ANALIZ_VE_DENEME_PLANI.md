# Yeniden Analiz Et ve Sac/Profil Olarak Dene — Plan

Durum: plan kabul edildi (2026-10-05). Durma noktaları: Aşama 1–3 birlikte, sonra 4 ve 5 birlikte, en son 6; her aşama ayrı commit(ler), elle deneme listesiyle durulur.

Bağlı notlar (Obsidian `99 - Yapılacaklar.md`): 29 ve 42 (236948), 41 (Baskı Kolu açınımı), Sekme Düzeni Aşama 6 (Yeniden analiz et).

## Bağlam (koddan ve motor çıktısından)

- **Motor her zaman bütün STEP'i analiz eder.** `AnalyzeStep` STEP'i okur ve parçaları XDE ile toplar (`CollectAssemblyParts`). Bütün geçerli parçaların tek bir analiz şeklini kurar (`BuildAnalysisShape`) ve bütün aşamaları onun üzerinde çalıştırır. Parça seçme yolu yok.
- **Parça sırası sabittir.** Parça `localId`'leri ürün ağacı gezinme sırasıyla verilir, yani aynı STEP için her çalıştırmada aynıdır. Bu yüzden seçili parça analizi aynı kimlikleri kullanabilir.
- **236948 (WGRV004423, 4 adet):** Sınıf `Other` / `NotRecognized`, kanıt var, bu yüzden Kontrol gerekli'de.
  - Parça 100 × 100 × 4 mm kare boru, boyu da 100 mm (x ekseninde −50 … +50).
  - Profil tanıyıcının gerekçesi: "Multiple reliable profile axes prevent a unique section classification."
  - Üç eksen adayı var. Yalnız boru ekseninde (x) kesit tutarlı: 37 kesit, dış kontur 100 × 100, iç kontur 92 × 92, et 4 mm, iç köşe R4. Öbür iki eksende kesit alanı %19 değişiyor ("Inconsistent").
  - Tanıyıcı "birden çok güvenilir eksen" görünce vazgeçiyor; boy ve uç kesimine hiç geçmiyor.
- **1201_Baskı Kolu, _Duplicate_1 ve 1201_Mafsal (her biri 2 adet):** Sınıf `ReviewRequired` / `FlatPatternFailed`.
  - Sac tanınıyor: t = 2 mm, 3'er büküm.
  - Açınım "Büküm F… iki flanş arasında değil" gerekçesiyle duruyor. Uçta biten bükümler şunlar:
    - Baskı Kolu: R2,5, 90°;
    - Mafsal: R0,7366 90° ve R5 90°.
  - Bu bükümlerin K-faktörü ve büküm payı da hesaplanmamış (`null`).
- **İşleme bilgisinin bugün göründüğü yerler:**
  - Montaj satırının "Seçili Parça" ayrıntısı (her sekmede).
  - Saclar tablosundaki "İşleme" sütunu.
- **İşleme bilgisinin görünmediği yerler:**
  - Kontrol gerekli / Tanımsız / Liste dışı ortak sütunları.
  - Profil satırlarının ayrıntısı.
  - Sac olmayan parçada "; DXF'te yok" eki yanıltıcı. Örnek: M5 Baskı, Other, işleme "Pocket".

## Kavramlar

| Komut | Motor çağrısı | Kurallar | Süre sınırı |
|---|---|---|---|
| Yeniden Analiz Et | `--parcalar <id,…>` | otomatik ile aynı | yok (`--parca-sure-siniri 0`) |
| Profil Olarak Dene | `--parcalar <id,…> --deneme profil` | yalnız profil tanıyıcı, gevşetilmiş | yok |
| Sac Olarak Dene | `--parcalar <id,…> --deneme sac` | yalnız sac tanıyıcı, gevşetilmiş | yok |

- **Görünürlük:** Komutlar Kontrol gerekli ve Tanımsız sekmelerinin araç çubuğunda görünür (`AnalizAracDurumu`). Profil satırları da (İnceleme gerekli) dahil, seçili satır varsa etkindir.
- **Çoklu seçim:** Her kaynak STEP için tek bir motor çağrısı yapılır. İlerleme mevcut analiz şeridinde görünür: "Profil olarak dene: 2 / 3 parça". İptal motor sürecini kapatır; tamamlanmamış parçalar eski hâlinde kalır.
- **Sonuç tanınırsa:** Satır ilgili sekmeye taşınır (Profiller / Saclar).
- **Sonuç tanınmazsa:** Satır yerinde kalır ve motor gerekçesi yenilenir. Örnek: "Profil olarak denendi: kesit tutarsız (…)".
- **Karar:** Deneme bir kullanıcı kararıdır. .macria'da saklanır, "Otomatik Karara Dön" geri alır.

## Aşama 1 — İşleme bilgisi her yerde (yalnız Macria, küçük)

- **Yapılacaklar:**
  - `IAnalizSatiri.IslemeGosterimi`, ortak sütunlara "İşleme": "var (gravür)", "var (cep)", "—".
  - Profil satırına parçanın `machiningKinds` / `machiningPresent` değerleri (montaj parçası varsa).
  - Profil satırının "Seçili Parça" ayrıntısına "İşleme" satırı.
  - "; DXF'te yok" eki yalnız sac satırında (DXF'i olan ya da olabilecek).
  - Excel her sekmede bu sütunu yazar. Sütunlar düzeni yeni sütunu varsayılan yerine koyar (`AnalizSutunDuzeni.Birlestir`).
- **Testler:** Satır gösterimleri (gravür, kabartma, cep, birden çok tür, işlemesiz). Karma sekme Excel sütunu.
- **Elle deneme:**
  1. WGRV: Kontrol gerekli'de 1201_Baskı Kolu "İşleme: var (gravür)", M5 Baskı "var (cep)" göstermeli.
  2. Tanımsız ve Liste dışı'nda İşleme sütunu görünmeli; Sütunlar'dan gizlenebilmeli.
  3. Bir profil satırında Seçili Parça ayrıntısında "İşleme" satırı olmalı.
  4. Kontrol gerekli Excel'inde İşleme sütunu olmalı.

## Aşama 2 — Motor: seçili parça analizi ve deneme altyapısı (GeometryLab, sonra paket)

- **Yapılacaklar:**
  - **CLI `--parcalar <localId,…>`:** STEP yine okunur ve bütün parçalar toplanır (kimlikler aynı kalsın diye). Analiz şekli yalnız seçili parçalardan kurulur; geçerlilik denetimi de yalnız onlara yapılır.
    - JSON'da yalnız seçili parçalar yer alır, `localId` / `productId` / adlar tam analizdekiyle aynıdır.
    - Yeni üst alanlar (şema 1.2'ye ek): `analysisMode` (`Automatic` / `Parts` / `TrialProfile` / `TrialSheet`) ve `selectedPartIds`.
  - **CLI `--deneme profil|sac`:** `AnalysisOptions.mode`. `--parcalar` olmadan verilirse çıkış kodu 2 ve "deneme yalnız seçili parçalarda" hatası; böylece bütün montaj gevşetilmiş kurallarla taranamaz. Bu aşamada deneme modları yalnız iskelet: kurallar otomatik ile aynı.
  - **Bilinmeyen kimlik:** `--parcalar` STEP'te olmayan bir kimlik içerirse uyarı yazılır ve kalanlar analiz edilir. Hiçbiri yoksa hata verilir.
  - **Sürüm:** Motor `2026.10.6.x`, `motor-surumu.txt`.
- **Testler (`SheetMetalTests`):**
  - Sentetik montajda `--parcalar` ile tek parçanın sonucu tam analizdeki parçayla aynı.
  - `--deneme` `--parcalar` olmadan reddedilir.
- **Regresyon:** "Güvenceler ve regresyon" bölümündeki R1 ve R2.
- **Elle deneme (komut satırı):**
  1. Paketlenmiş exe ile `WGRV004423 A.stp --parcalar 191 --parca-sure-siniri 0` çalıştırılır. Çıktıda tek parça (236948) olmalı; sınıf ve gerekçe tam analizle aynı olmalı.
  2. Konsola yazılan süre not edilir: tam analiz 59 s'ye karşı tek parça.
- **Doğrulanmalı:** STEP okumanın WGRV'deki payı (tahmin: birkaç saniye). Tek parçalı yeniden analizin hızı buna bağlı; bu aşamada ölçülecek.

## Aşama 3 — Macria: Yeniden Analiz Et ve .macria 1.3

- **Yapılacaklar:**
  - **Komut:** Kontrol gerekli ve Tanımsız'da "Yeniden Analiz Et" (`AnalizAracDurumu` kuralı + test).
    - Seçili satırlar kaynak STEP'e göre gruplanır; her grup için `--parcalar … --parca-sure-siniri 0` çalıştırılır.
    - İlerleme ve İptal mevcut STEP analizi şeridinde görünür.
    - Konsolda "Yeniden analiz: 3 parça, 2 sekme değiştirdi, 41 s" yazar.
  - **Birleştirme:** Seçili parça çıktısı ana analizin üzerine parça bazında bindirilir. `MacriaProjeSatirlari.Kur` parçanın satırını ek çıktıdan kurar; satır kimliği (`kaynak` + `localId`) değişmez, mevcut kararlar (Liste dışı, kalınlık, DXF yolu) satırda kalır.
  - **.macria şeması 1.3:**
    - Ek çıktılar `kaynaklar/<id>/ek/<n>.json` dosyasına (DXF'leriyle) yazılır.
    - Yeni karar türü `Dene` eklenir. Alanlar: `Mod` (`yeniden` / `profil` / `sac`), `EkCikti` (dosya adı) ve parça kimliği.
    - 1.2 dosyaları aynen açılır. 1.3 dosyasını eski Macria salt-okunur açar (mevcut kural).
  - **Yeniden tarama:** STEP değiştiğinde ya da motor yenilendiğinde `Dene` kararları bugünkü eşleme ile yeni parçalara bağlanır (önce `localId`, sonra `productId` / ad). Tam analizden sonra her kaynakta tek çağrıyla yeniden uygulanır: "Denemeler yeniden uygulanıyor: 2 / 3". Eşlenemeyen deneme `eslenemeyenKararlar`'da kalır ve raporlanır.
  - **Otomatik Karara Dön:** `Dene` kararını ve ek çıktısını kaldırır; satır otomatik sonuca döner.
- **Testler:**
  - Bindirme: aynı kimlik, kararların korunması.
  - .macria 1.3 kaydet / aç, 1.2'yi okuma, eşleme ve eşlenemeyen deneme.
  - Ana analiz çağrısının argümanlarında `--parcalar` ve `--deneme` olmadığı (adaptör testi).
- **Elle deneme:**
  1. WGRV'de Tanımsız'dan bir parça ve Kontrol gerekli'den iki parça seçip Yeniden Analiz Et. İlerleme ve süre görünmeli; sonuç tam analizle aynı olmalı (süre aşımı olan yoksa sekme değişmez).
  2. Analiz sürerken İptal: Macria donmamalı, satırlar eski hâlinde kalmalı.
  3. Projeyi kaydet, kapat, aç: yeniden analiz sonucu korunmalı.
  4. Otomatik Karara Dön: satır ilk sonuca dönmeli.
  5. Eski bir 1.2 projesi açılıp kaydedilebilmeli.

## Aşama 4 — Profil Olarak Dene (236948)

- **Önce teşhis:** 236948'de "birden çok eksen" kuralı geçildikten sonra boy, uç kesimi ve sınıf aşamalarında takılan başka kural var mı? Motor deneme modunda adım adım çalıştırılır. Bulunan her kural aşağıdaki listeye eklenir ve raporlanır.
- **Gevşetilmiş kurallar (yalnız `TrialProfile`):**
  1. **Eksen seçimi:** Birden çok güvenilir eksen varsa, kesiti tutarlı (`Consistent`) ve içi boş (dış + iç kontur) olan tek eksen seçilir. Böyle birden çok eksen varsa yine "belirsiz" denir; seçim yapılmaz.
  2. **Kısa boy:** Boyun kesitin büyük ölçüsünden kısa ya da ona eşit olması kabul edilir. Otomatikte bu durum profil iddiasını zayıflatıyorsa yalnız denemede gevşetilir.
  3. **Sac kuralları uygulanmaz:** Sac tanıyıcının sonucu sınıflandırmaya katılmaz, yani sac / profil çakışması olmaz. Sac kaydı JSON'da bilgi olarak kalabilir.
- **Ölçüleri motor alır:** Kesit (dış ve iç ölçü, köşe yarıçapları), et kalınlığı, boy ve iki uç kesimi motorun mevcut ölçüm yoluyla, seçilen eksende ölçülür. Kullanıcıdan sayı istenmez; elle kutu profil penceresi bu yolda açılmaz.
- **Motor gerekçesi:** "Profil olarak denendi (gevşetilmiş: tek tutarlı içi boş eksen seçildi; boy kesitten kısa)". Tanınmazsa nedeni yazılır: "Profil olarak denendi: içi boş tutarlı kesit bulunamadı".
- **Macria:**
  - Tanınan parça Profiller'e geçer. Satır `ResultForPart` ile ek çıktıdan kurulur.
  - Durum / Karar sütununda "Profil (deneme)" görünür. Excel ve ayrıntı aynı metni yazar.
- **Testler:**
  - GeometryLab: sentetik kısa kare boru (100 × 100 × 4, boy 100). Otomatikte tanınmamalı (bugünkü davranış), `--deneme profil` ile □100 × 100 × 4, boy 100 çıkmalı.
  - Sentetik dolu kare çubuk: denemede de profil olmamalı (içi boş değil).
  - Macria: deneme satırının sekmesi ve gösterimi.
- **Elle deneme:**
  1. WGRV Kontrol gerekli'de 236948'i seçip Profil Olarak Dene. Profiller'e geçmeli; kesit 100 × 100 × 4, boy 100 mm, adet 4, uç kesimleri düz olmalı. Değerleri CATIA'daki ölçüyle karşılaştırın.
  2. Dolu bir mili (ör. Kontrol gerekli'deki "Diğer") Profil Olarak Dene. Tanınmamalı, yeni gerekçe görünmeli, satır yerinde kalmalı.
  3. Kaydet / aç: 236948 Profiller'de kalmalı.
  4. Otomatik Karara Dön: 236948 Kontrol gerekli'ye dönmeli.

## Aşama 5 — Sac Olarak Dene (kalınlık her zaman, açınım olursa)

- **Gevşetilmiş kurallar (yalnız `TrialSheet`):**
  1. **"Levha değil" kuralları uygulanmaz:** Dış ölçü kuralı (`ThickerThanOutline`) ve et genişliği kuralı (`ThickerThanMaterial`) uygulanmaz. Ölçülen değerler gerekçede bilgi olarak yazılır.
  2. **Profil tanıyıcı sınıflandırmaya katılmaz.**
  3. **Kalınlık her zaman yazılır:** Kabuk eşleşmesi ya da açınım başarısız olsa bile, ölçülen ofset çiftlerinden kalınlık yazılır (`thicknessMm`). Bugün kapalı kesitte de ölçülüyor: 236948'de 4 mm.
  4. **Sınıflandırılamayan yüzler:** Kabuklar arasında kalmayan yüzler en çok %X alanla sınırlı kalırsa işleme sayılır. X Aşama 5'in başında WGRV ve 52 dosyadaki örneklere bakılarak önerilecek; **Karar Bekliyor**.
- **Açınım çıkmazsa:** Parça yine Sac olur. "Açınım yok – CATIA'dan" ve kalınlık görünür; DXF Üret bu parçayı atlar (bugünkü açınımsız sac davranışı).
- **Motor gerekçesi:** "Sac olarak denendi (gevşetilmiş: …)". Tanınmazsa gerekçe yazılır: "Sac olarak denendi: karşılıklı kabuk bulunamadı".
- **Macria:** Tanınan parça Saclar'a, kalınlığına göre Lazer ya da Şalama/Kütük grubuna geçer. Durum "Sac (deneme)" olur. Onay durumu: aşağıdaki karar noktası 1.
- **Testler:**
  - Sentetik halka (otomatikte Diğer): denemede Sac, kalınlık var.
  - Kapalı kesit kare boru: denemede "Sac, açınım yok – CATIA'dan", kalınlık 4.
  - Macria satır ve sekme testleri.
- **Elle deneme:**
  1. 1201_Baskı Kolu'nu Sac Olarak Dene. Bu aşamada açınım yine çıkmayabilir (G3 Aşama 6'da). Saclar'da "açınım yok – CATIA'dan" ve kalınlık 2 mm görünmeli.
  2. WGRV'de bir halkayı (ör. 257300_Duplicate_10) Sac Olarak Dene. Saclar'a geçmeli, kalınlık 30 mm, Şalama/Kütük grubunda olmalı; gerekçede et genişliği bilgisi görünmeli.
  3. DXF Üret açınımsız deneme sacını atlamalı ve raporda bunu söylemeli.
  4. Kaydet / aç / Otomatik Karara Dön: Aşama 4'teki gibi.

## Aşama 6 — Uçta biten büküm (G3): Baskı Kolu ve Mafsal

- **Değerlendirme:** Uçta biten büküm "gevşetilmiş" bir varsayım değil; açınımın eksik bir geometri durumu.
  - Büküm bir flanşa bağlı, öbür ucu serbest. Açınımda bu büküm, nötr eksen yarıçapında açılmış bir şerittir: boyu büküm payı (BA), eni büküm boyu.
  - Bitiş yüzü büküm eksenini içeren düzlemse (radyal kesim), şerit tam bir dikdörtgendir.
  - Bitiş yüzü eğikse, silindir üzerindeki kenar `(r_n·θ, z)` dönüşümüyle düzleme açılır.
  - İkisi de kesin geometri; tahmin yok. K-faktörü ve BA mevcut CATIA formülüyle (`catia-log`) hesaplanabilir; bugün yalnız iki flanş arasındaki bükümler için hesaplanıyor.
  - **Sonuç:** Sac denemesi modunda ele alınabilir. Kesin geometri olduğu için ileride otomatik açınıma alınması da uygun; o zaman Baskı Kolu ve Mafsal denemesiz açılır (karar noktası 2).
- **Önce teşhis:** Baskı Kolu ×2 ve Mafsal'da uçtaki bükümlerin bitiş yüzü türü (radyal düzlem / eğik / başka). 52 dosyada uçta biten büküm içeren başka parçalar listelenir.
- **Yapılacaklar (yalnız `TrialSheet`):**
  - `Unfold`'da uçta biten büküm, şerit olarak ebeveyn flanşa eklenir.
  - Büküm çizgisi ve UP/DOWN etiketi DXF'e mevcut kuralla yazılır.
  - Uçtaki bükümlere K-faktörü / BA hesaplanır.
- **Testler:**
  - Sentetik: tek flanş + uçta 90° büküm (radyal bitiş); otomatikte `FlatPatternFailed`, denemede açınım (şerit boyu = BA).
  - Eğik bitişli örnek.
  - CATIA DXF kabul testi (`SheetDxfAcceptance`): mevcut 9 / 9 değişmemeli.
- **Kabul:** Baskı Kolu ve Mafsal için CATIA'nın DXF'i gerekiyor. Kullanıcı iş yerinde CATIA'dan "Save As DXF" ile alıp `claude-test` altına koyarsa, motor DXF'i 0,01 mm toleransla karşılaştırılır. CATIA DXF'i yoksa yalnız ölçü kontrolü yapılır ve "doğrulanmadı" denir.
- **Elle deneme:**
  1. 1201_Baskı Kolu'nu Sac Olarak Dene. Saclar'a geçmeli, açınım çıkmalı, 2B önizlemede uçtaki büküm şeridi ve büküm çizgisi görünmeli; gravür DXF'te olmamalı.
  2. 1201_Mafsal: aynısı.
  3. Açınım ölçüsünü CATIA'nın açınımıyla karşılaştırın (CATIA'da Unfold ya da DXF).
  4. DXF Üret ile yazılan DXF CATIA'da doğru ölçüde açılmalı.

## Güvenceler (gevşetilmiş kurallar otomatik taramaya sızmasın)

1. **Mod açıkça verilir:** `AnalysisOptions.mode` varsayılanı `Automatic`. Deneme kodu yalnız `mode == TrialProfile / TrialSheet` dalında çalışır.
2. **Otomatik kod değişmez:** Gevşetilmiş kurallar otomatik fonksiyonların eşiklerini değiştirmez; ayrı fonksiyonlarda durur (ör. `SelectTrialProfileAxis`, `ClassifyTrialSheet`). Otomatik fonksiyonun içinde mod sorgusu yalnız "dalı seç" düzeyindedir. Gözden geçirmede otomatik fonksiyonların gövde farkı sıfır olmalı (Aşama 6'da `Unfold` hariç; orada şerit yalnız deneme dalında eklenir).
3. **CLI kilidi:** `--deneme` `--parcalar` olmadan kabul edilmez.
4. **Çıktı etiketli:** JSON `analysisMode` taşır. Macria, `Automatic` olmayan bir çıktıyı kaynağın ana analizi olarak kabul etmez (açılışta ve analizde denetim + test).
5. **Ana analiz çağrısı:** Macria'nın ana analiz çağrısı `--parcalar` / `--deneme` geçmez (adaptör argüman testi).
6. **Deneme sonucu yalnız kararla kullanılır:** Satır, ancak bir `Dene` kararı varsa ek çıktıdan kurulur. Otomatik Karara Dön kararı ve ek çıktıyı siler.
7. **Kullanıcı görür:** Deneme satırı her yerde etiketli: Durum "… (deneme)", motor gerekçesinde hangi kuralların gevşetildiği, Excel'de aynı metin.
8. **Motor testleri:** Her gevşetilmiş kural için iki test: otomatikte eski sonuç, denemede yeni sonuç.

## Regresyon planı

- **R1 — 52 dosya, otomatik:** Her motor aşamasında (2, 4, 5, 6) 52 dosyalık `regresyon.py calistir` + `karsilastir` (normalize JSON + DXF). Beklenen fark: yalnız yeni üst alanlar (`analysisMode: "Automatic"`, `selectedPartIds: null`). Başka bir fark varsa aşama bitmez; önce/sonra listesi raporlanır.
- **R2 — Seçili parça eşdeğerliği (Aşama 2):**
  - `--parcalar <hepsi>` çıktısının parça kayıtları tam analizle aynı olmalı (52 dosya).
  - Tek tek: montaj-1'in 7 parçası ve WGRV'den 20 parça (her sınıftan; süre aşımı yok). `--parcalar <id>` ile parça sınıfı, gerekçe, kalınlık, açınım, DXF tam analizdekiyle aynı olmalı.
- **R3 — Deneme kapsamı (Aşama 4–6):**
  - WGRV'de Kontrol gerekli + Tanımsız'daki bütün parçalar `--deneme profil` ve `--deneme sac` ile çalıştırılır. Sonuçlar tablolanır: hangi parça neye dönüşüyor, hangi kural gevşetildi.
  - Beklenmeyen dönüşüm raporlanır. Örnek: bir somunun profil olması.
  - Bu tablo elle denemeden önce kullanıcıya verilir.
- **R4 — CATIA DXF kabul testi:** 9 / 9 korunur (Aşama 6'da yeni örneklerle).
- **R5 — Macria testleri:** `GeometryLabAdapter.Tests` (gerçek motorlu durumlar dahil), `DxfEdit.Tests`. Eski projeler (`WGRV004423 A.macria`, `B-Rep Calisma A.macria`) açılır, satır dağılımı aynı kalır.

## Karar noktaları (kullanıcı kararları, 2026-10-05)

1. Sac denemesiyle tanınan parça **onaylı** sayılır, "(deneme)" etiketiyle.
2. G3 kabulden sonra **ayrı bir adımda** otomatik taramaya da alınır.
3. "Kesiti tutarlı içi boş tek eksen" kuralı şimdilik **yalnız denemede** kalır; WGRV deneme tablosundan sonra yeniden karar verilir.
4. İşleme alanı sınırı Aşama 5'in başında örneklerle önerilecek.

Planı yazarken sunulan seçenekler:

1. **Sac denemesinin onay durumu:** Kullanıcı "Sac olarak dene" dedi ve motor tanıdıysa satır Saclar'da hangi durumda olmalı?
   - Önerim: onaylı ("Sac (deneme)", DXF Üret'e girer), çünkü deneme zaten kullanıcının kararı.
   - Alternatif: "Onay gerekli" (amber), ayrıca Sac Olarak Onayla gerekir.
2. **G3'ün otomatik taramaya alınması:** Uçta biten büküm açınımı kesin geometri. Aşama 6 kabul edilip CATIA DXF'iyle doğrulandıktan sonra otomatik açınıma alınabilir. Önerim: evet; ayrı bir adım olarak, 52 dosya regresyonuyla.
3. **Tek tutarlı içi boş eksen kuralı:** 236948'deki eksen seçimi de kesin bir kural sayılabilir. Önerim: önce yalnız denemede kalsın; R3 tablosunda yanlış seçim görülmezse otomatiğe alınması ayrıca değerlendirilsin.
4. **Sac denemesinde işleme sayılacak alan sınırı (%X):** Aşama 5 başında örneklerle önerilecek.

## Kritik dosyalar

- **GeometryLab:**
  - `main.cxx` (`--parcalar`, `--deneme`),
  - `GeometryEngine.cxx` (`AnalyzeStep`, JSON `analysisMode`),
  - `AssemblyStructure.cxx` (seçili parçalar, `PopulatePartClassification` deneme dalları),
  - `ProfileRecognition.cxx` / `ProfileGeometryAnalysis.cxx` (eksen seçimi),
  - `SheetMetalRecognition.cxx`, `SheetMetalUnfold.cxx` (G3),
  - `include/GeometryEngine.hxx` (`AnalysisOptions.mode`).
- **Macria:**
  - `GeometryLabProcessAdapter.cs` (argümanlar, `analysisMode` denetimi),
  - `MacriaProje.cs` (şema 1.3, `Dene`, ek çıktılar),
  - `MacriaProjeSatirlari.cs` (bindirme),
  - `MainWindow.AnalizSekmeleri.cs` / `AnalizAracDurumu.cs` (komutlar),
  - `MontajParcasi.cs`, `GeometryLabStepProfileListItem.cs` (İşleme, deneme gösterimi),
  - `MainWindow.Proje.cs` (yeniden taramada denemeleri uygulama).
- **Testler:** GeometryLab `SheetMetalTests`, `SheetDxfAcceptance`; Macria `GeometryLabAdapter.Tests`.

## Riskler

- **Tek parçalı analiz yine bütün STEP'i okur.** Büyük dosyada her deneme birkaç saniye okuma süresi ekler. Çoklu seçim tek çağrıda toplandığı için kabul edilebilir; Aşama 2'de ölçülecek.
- **Ana analiz ve ek çıktı birlikte saklanır.** .macria büyür (her ek çıktı ayrı JSON). WGRV tam JSON'u büyük; ek çıktı yalnız seçili parçaları içerir.
- **Yeniden taramada parça eşlenemeyebilir.** STEP değişince bir parçanın `productId`'si değişebilir; o deneme eşlenemez ve raporlanır, sessizce kaybolmaz.
- **Gevşetilmiş profil kuralları yanlış ekseni seçebilir.** "Tek tutarlı içi boş eksen" şartı ve R3 tablosu bunun için.
- **G3'ün eğik bitiş yüzü durumu daha karmaşık.** Teşhiste yalnız radyal bitiş çıkarsa ilk sürüm onunla sınırlı tutulur; eğik bitiş "desteklenmiyor" gerekçesiyle raporlanır.
