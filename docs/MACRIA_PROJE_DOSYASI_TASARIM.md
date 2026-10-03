# Macria Proje Dosyası (.macria) — Tasarım

- **Durum:** Tasarım kabul edildi (2026-10-03). Uygulama §13'teki aşamalarla; Aşama 1 uygulanıyor.
- **Kapsam:** Dosya Analiz Merkezi → STEP / STP Analizi sonuçlarının kaydedilip yeniden açılması.

## 1. Bağlam

Dosya Analiz Merkezi'ndeki STEP sonuçları yalnız oturumda yaşıyor:

- Macria kapanınca ya da "Tabloyu Temizle" / yeni analiz yapılınca motor çıktısı, kullanıcı kararları ve motor DXF'leri kayboluyor.
- WGRV004423 gibi bir montajın analizi yaklaşık 1 dakika sürüyor; Liste dışı ve sac onayları gibi kararlar elle veriliyor.

Hedef: işi bir dosyaya kaydedip yeniden açabilmek. STEP değişmemişse motor yeniden çalışmasın, kararlar aynen geri gelsin.

## 2. Kodda dayanılan noktalar (bugünkü durum)

- **Satırlar motor sonucundan belirlenimci (deterministik) kuruluyor:**
  - Profil satırı: `GeometryLabStepProfileListItem.Apply(GeometryLabProcessAdapterResult)`.
  - Montaj: `MainWindow.MontajSonucunuDagit` → `MontajParcaSatiri.Olustur(step, analysis, part, dxf, lazerSınırı, kesimDxf)`.
  - Sonuç: saklanan motor çıktısı aynı kodla yeniden oynatılırsa aynı satırlar çıkar; kararlar bunun üzerine uygulanır.
- **Kullanıcı kararları (hepsi `DecisionSource.User`):**
  - Profil satırı: `ConfirmAsProfile(note)`, `ConfirmManualHollowProfile(tür, kesit, note)`, `MoveToReview(note)`, `ExcludeFromList(note)`.
  - Montaj parçası: `ApproveAsSheet()`, `MoveToReview()`.
  - İkisinde de `RestoreAutomaticDecision()` var.
- **Parça kimliği** (`GeometryLabPartTransport`): `localId` (aynı motor çıktısı içinde kararlı), `name`, `productId`, `productName`, `quantity`.
- **Motor çıktısının taşınması:** `GeometryLabProcessAdapter`, `analysis.json`'u okuyup `GeometryLabAnalysisTransport`'a çeviriyor ve geçici klasörü siliyor; **ham JSON saklanmıyor**.
  - Şema kontrolü: `IsSupportedSchemaVersion` (1.0 / 1.1 / 1.2).
  - DXF'ler: `PartDxfDirectory` + `PartDxfPath(part, cutOnly)`.
  - Geçici DXF klasörü: `MotorDxfOturumunuYenile` / `MotorDxfOturumunuTemizle` (`%TEMP%\Macria\MotorDxf\<guid>`).
- **Ayarlar** (`Ayarlar.cs`, `%APPDATA%\Macria` altında `anahtar=değer` dosyası): `LazerAzamiKalinlikMm`, `ParcaSureSiniriSaniye`, `MotorIsParcacigi`, `BukumBilgisiDxf`. Son açılanlar listesi için hazır bir yapı yok.
- **DXF Üret:** `MotorDxfAktarici.Planla` / `Uygula` → `<hedef>\Motor-DXF\`.
- **3B:** `Step3BModelHazirlayici.Hazirla(path)` modeli arka planda bir kez hazırlıyor (tam yolla anahtarlı). Görüntüleyici DLL'deki `ModelCache` süreç boyunca yaşıyor.
- **Güvenli kaydetme örneği:** `DxfEditOturumu` (geçici dosya + değiştirme + yedek).

## 3. Kapsam

- **İçinde:**
  - ZIP + JSON, sürümlü şema.
  - Ham motor çıktısı, motor DXF'leri, kullanıcı kararları.
  - Kaynak STEP yolu + SHA-256.
  - Analiz anındaki ayarlar, DXF Üret geçmişi.
  - Kaydet / Farklı Kaydet / Aç / son açılanlar; kaydedilmemiş değişiklik uyarısı.
  - Bir projede birden çok STEP (bugünkü çoklu seçim gibi).
- **İleride:** 3B model önbelleği, CATIA karşılaştırma anlık görüntüsü, Montaj Gezgini kararları.
- **Dışında:**
  - STEP dosyasının kendisi projeye gömülmez (boyut). İleride isteğe bağlı olabilir.
  - CATIA ana ekranının tarama sonuçları (`SheetRow` vb.) bu dosyaya girmez.

## 4. Dosya biçimi

`.macria` = ZIP (Deflate). JSON'lar UTF-8, BOM'suz, `System.Text.Json`. ZIP içi yollar `/` ile.

```
manifest.json                         biçim kimliği ve sürüm (ilk okunan)
proje.json                            kaynaklar, ayarlar, kararlar, DXF geçmişi
kaynaklar/<kaynakId>/analysis.json    motorun yazdığı JSON, bayt bayt aynı
kaynaklar/<kaynakId>/dxf/part-<n>.dxf, part-<n>-kesim.dxf
--- ileride ---
kaynaklar/<kaynakId>/model/...        3B model önbelleği (Aşama 4)
catia/tarama.json                     CATIA karşılaştırma anlık görüntüsü (Aşama 2)
```

`kaynakId`: kısa, kalıcı kimlik (`k1`, `k2`, ...). Yol değişse de aynı kalır.

### manifest.json

```json
{
  "format": "macria-proje",
  "schemaVersion": "1.0",
  "olusturan": "Macria 1.12.0",
  "olusturulma": "2026-10-04T10:12:00Z",
  "kaydedilme": "2026-10-04T11:40:00Z"
}
```

### proje.json (şema 1.0)

```json
{
  "ayarlar": {
    "lazerAzamiKalinlikMm": 20,
    "bukumBilgisiDxf": true,
    "analiz": { "parcaSureSiniriSaniye": 120, "motorIsParcacigi": 0 }
  },
  "kaynaklar": [{
    "id": "k1",
    "tur": "step",
    "yol": "C:\\...\\WGRV004423 A.stp",
    "goreliYol": "..\\Fixture\\WGRV004423 A.stp",
    "sha256": "…", "boyut": 18771234, "degistirilme": "2026-09-30T08:00:00Z",
    "analiz": {
      "zaman": "2026-10-04T10:13:05Z",
      "durum": "Succeeded",
      "mesaj": null,
      "motorSemaSurumu": "1.2",
      "motor": { "surum": "2026.10.3.1", "commit": "237a30a", "sha256": "…" },
      "sureSn": 64.2,
      "montaj": true
    }
  }],
  "kararlar": [
    { "kaynak": "k1", "parca": { "localId": 63, "ad": "350151", "productId": "350151" },
      "hedef": "montaj", "karar": "SacOnayla", "zaman": "…" },
    { "kaynak": "k1", "parca": { "localId": 12, "ad": "01-Duz-Duz", "productId": "…" },
      "hedef": "profil", "karar": "ListeDisi", "not": "kaynak parçası", "zaman": "…" },
    { "kaynak": "k2", "parca": null, "hedef": "profil", "karar": "ElleProfil",
      "elleProfil": { "tur": "Kare Kutu Profil", "kesit": "40 × 40 × 2 mm" }, "zaman": "…" }
  ],
  "eslenemeyenKararlar": [],
  "dxfAktarimlari": [
    { "zaman": "…", "hedefKlasor": "D:\\Is\\Motor-DXF", "bukumBilgisi": true,
      "dosyalar": [{ "kaynak": "k1", "localId": 7, "yol": "D:\\Is\\Motor-DXF\\55RS100111-1.dxf", "sha256": "…" }] }
  ]
}
```

- **Karar türleri:**
  - Profil satırı: `ProfilOnayla`, `ElleProfil`, `Incelemeye`, `ListeDisi`.
  - Montaj parçası: `SacOnayla`, `Kontrole`.
  - Yalnız kullanıcı kararları yazılır. Otomatik ve CATIA kaynaklı kararlar motor çıktısından yeniden hesaplanır.
- **`parca: null`:** Tek parçalı STEP'in dosya satırı. Montaj olmayan dosyada satır = kaynak.
- **Ayar kayıtlarının anlamı:**
  - `lazerAzamiKalinlikMm` ve `bukumBilgisiDxf` proje ayarıdır; açılınca bu proje için geçerli olur. Gruplama (`SetLaserMaximum`) ve DXF seçimi bunlara bağlıdır.
  - `analiz` bölümü yalnız kayıttır. Yeniden taramada o anki uygulama ayarları kullanılır; kayıttaki süre sınırından farklıysa kullanıcıya bildirilir, çünkü süre sınırı sonucu değiştirebilir.

## 5. Gereken küçük kod değişiklikleri (uygulamada, tasarımda değil)

- **`GeometryLabProcessAdapter.cs`:**
  - Sonuca ham JSON eklenir (`AnalysisJson`).
  - Ayrıştırma + şema kontrolü statik bir yönteme çıkarılır (`SonucuJsondanKur(json, dxfKlasoru)`). Hem canlı analiz hem proje açılışı aynı yolu kullanır.
  - Ham JSON saklandığı için motorun eklediği yeni alanlar kaybolmaz.
- **Yeni `MacriaProje.cs` (WPF'siz):**
  - Model, ZIP okuma/yazma, şema kontrolü, karar eşleme.
  - `GeometryLabAdapter.Tests`'e bağlanır (`<Compile Include … Link>`).
- **Yeni `MainWindow.Proje.cs`:** Komutlar, kirli (kaydedilmemiş) durum, açılış iletişim kutuları.
- **Karar dışa/içe aktarma:**
  - `GeometryLabStepProfileListItem` ve `MontajParcaSatiri` için küçük bir `KararKaydi`.
  - İçe aktarma mevcut yöntemleri çağırır (`ExcludeFromList(note)` vb.); yeni karar mantığı yazılmaz.

## 6. Kaydetme

- **Komutlar:** STEP / STP Analizi araç şeridinde "Proje ▾" menüsü.
  - **Aç** (Ctrl+O).
  - **Kaydet** (Ctrl+S): Yolu yoksa Farklı Kaydet'e düşer.
  - **Varsayılan kayıt yeri:** İlk STEP'in klasörü; ad, STEP'in adı (`WGRV004423 A.stp` → `WGRV004423 A.macria`).
  - **Farklı Kaydet** (Ctrl+Shift+S).
  - **Son açılanlar** (en çok 10).
  - Dosya Analiz Merkezi ana sayfasında ayrıca "Proje Aç" kartı ve son açılanlar listesi.
- **Yazma sırası:**
  1. Satırlardan `proje.json` kurulur.
  2. Her kaynağın ham JSON'u ve DXF'leri geçici DXF oturum klasöründen ZIP'e eklenir.
  3. ZIP, aynı klasörde `<ad>.macria.tmp` olarak yazılır.
  4. Varsa `File.Replace(tmp, hedef, hedef.bak)` ile değiştirilir; `.bak` bir önceki sürüm olarak kalır. Bu, `DxfEditOturumu`'ndaki kalıptır.
- **Kaydedilmemiş değişiklik (kirli durum) tetikleyicileri:**
  - analiz bitmesi,
  - her karar ve "Otomatik Karara Dön",
  - Tabloyu Temizle,
  - DXF Üret,
  - yeniden tarama,
  - kaynak yolunun değişmesi,
  - lazer sınırı / büküm ayarı değişmesi.
  - Başlıkta `Macria — <proje adı>*` gösterilir.
- **"Kaydet / Kaydetme / İptal" sorusu sorulan yerler:**
  - Macria kapanırken (`MainWindow.Closing`),
  - başka proje açılırken,
  - yeni "STEP Seç ve Analiz Et" (bugün listeyi tamamen değiştiriyor),
  - Tabloyu Temizle (mevcut onay sorusuyla birleştirilir).
- **Son açılanlar:** `%APPDATA%\Macria\son-projeler.txt`, satır başına bir yol. Diğer ayar dosyalarıyla aynı biçim. Bulunamayan girdi soluk gösterilir ve listeden kaldırılabilir.

## 7. Açılış

1. **Biçim kontrolü:**
   - `manifest.json` okunur.
   - `format` yanlışsa ya da ana sürüm (`2.x`) desteklenenden büyükse açılmaz: "Bu proje daha yeni bir Macria ile kaydedilmiş."
   - Ara sürüm (`1.y`) desteklenenden büyükse salt-okunur açılır. Kaydetmek, bilinmeyen alanları silebileceği için kapalıdır.
2. **Kaynak kontrolü (her kaynak için):**
   1. `yol`'da dosya var mı? Yoksa proje dosyasının yeni yerine göre `goreliYol` denenir. Bu, klasör birlikte taşındığında işe yarar.
   2. Dosya bulunursa SHA-256 hesaplanır ve kaydedilen değerle karşılaştırılır. Her zaman hash'e bakılır; boyut + tarih yalnız hızlı bir ön eleme olarak kullanılır.
      - WGRV (17,9 MB) için ~0,1 s; 500 MB için birkaç saniye, ilerleme gösterilir.
      - OneDrive yalnız bulutta duran dosyayı bu sırada indirir.
   3. **Motor şema sürümü** artık desteklenmiyorsa kaynak "değişmiş" gibi ele alınır ve yeniden tarama önerilir.
   4. **Motor sürümü:** Kurulu motorun sürümü kaynağın taranmış olduğu sürümden yeniyse, hash sorusuyla aynı pencerede sorulur: "Eski motorla taranmış (2026.10.2.1 → 2026.10.3.1); yeniden taransın mı?" Seçenekler: **Yeniden tara** / **Kayıtlı sonuçla aç**. Sürüm aynı ama exe SHA-256'sı farklıysa da aynı soru sorulur (paketleme hatası olabilir).
      - Motor sürümü `GeometryEngineRuntime/motor-surumu.txt` dosyasından okunur (`2026.10.3.1` + GeometryLab commit'i). Motor JSON'unda sürüm alanı olmadığı için bu dosya her motor paketlemesinde güncellenir (README kuralı). Dosya yoksa sürüm "bilinmiyor" sayılır ve yalnız SHA-256 karşılaştırılır.
3. **Durumlara göre davranış:**
   - **Aynı:** Motor çalışmaz.
     - Ham JSON, `SonucuJsondanKur` ile sonuca çevrilir.
     - DXF'ler yeni bir geçici DXF oturum klasörüne açılır.
     - Satırlar bugünkü kodla kurulur, sonra kararlar `localId` ile birebir uygulanır.
   - **Değişmiş ya da bulunamadı:** Tek bir pencerede kaynak başına seçim istenir; "Tümüne uygula" kutusu vardır.
     - **Yeniden tara** (yalnız dosya bulunduysa): Normal analiz çalışır, kararlar §8'e göre yeniden eşlenir, proje kirli olur.
     - **Yeni yolu göster:** Dosya seçtirilir.
       - Hash aynıysa yalnız yol güncellenir; tarama yok, proje kirli olur.
       - Hash farklıysa "Yeniden tara / Salt-okunur" sorusuna döner.
     - **Sonuçları salt-okunur aç:** Kayıtlı sonuçlar ve kararlar gösterilir.
       - Karar düğmeleri ve Kaydet kapalıdır.
       - Excel'e Aktar ve DXF Üret çalışır, çünkü DXF'ler projenin içindedir.
       - 3B, STEP olmadığı için "Kaynak STEP yok" der (Aşama 4'te önbellekten gösterilebilir).
       - Başlık: `[salt-okunur]`.
4. **Sonra:** Doğrulanan her kaynak için `Step3BModelHazirlayici.Hazirla(path)` çağrılır (bugün analiz sonrası yapılanın aynısı). Konsola özet yazılır: "Proje açıldı: 2 kaynak, 1 tarama gerekmedi, 1 yeniden tarandı, 3 karar eşlenemedi."

## 8. Kararların parçaya eşlenmesi

- **Aynı motor çıktısı** (açılış, tarama yok): `localId` ile birebir. Ad yalnız tutarlılık denetimidir; uyuşmazsa karar eşlenemeyenlere düşer.
- **Yeniden taramadan sonra:** `localId` değişebilir. Sıra şöyledir:
  1. `productId` eşitliği,
  2. `ad` eşitliği.
  - İkisinde de tek aday varsa karar uygulanır. Birden çok aday varsa (aynı adlı parçalar) uygulanmaz.
  - Kararın hedefi artık geçerli değilse uygulanmaz. Örnek: parça artık profil değil ya da DXF'i olmadığı için `CanApproveAsSheet` false.
- **Eşlenemeyen kararlar sessizce silinmez:**
  - `eslenemeyenKararlar`'a taşınır.
  - Bir rapor penceresinde ve konsolda listelenir.
  - Kullanıcı "listeden at" diyene kadar dosyada kalır.

## 9. Ortak 3B model önbelleğiyle ilişkisi

- **Aşama 1–3:** Değişiklik yok. Proje açılışı, analiz sonrası nasıl yapılıyorsa öyle `Hazirla(path)` çağırır. Panellerin ve Büyük Aç'ın paylaşımı aynen sürer.
- **Önbellek anahtarı:** Bugün tam yol. Proje ile yol değişebildiği için anahtarın kaynak SHA-256'sına geçmesi önerilir (`Step3BModelHazirlayici` + görüntüleyicinin `ModelCache`'i). Yeri değişen ama aynı olan dosya yeniden okunmaz.
- **Aşama 4 — projede 3B önbellek:**
  - `kaynaklar/<id>/model/` altına üçgenlenmiş model yazılır. Biçim, OCCT XBF (XCAF + üçgenleme) ya da basit bir mesh biçimi olabilir; GeometryLab'da karar verilecek.
  - Geçerlilik anahtarı: STEP SHA-256 + görüntüleyici DLL sürümü + üçgenleme parametreleri. Uymazsa önbellek yok sayılır ve STEP'ten okunur.
  - Kazanç: WGRV'de 4,4 s'lik hazırlık atlanır; STEP'i olmayan salt-okunur projede de 3B görünür.
  - Gereken GeometryLab API'si: `MacriaGeometryViewer_SaveModel(path)`, `MacriaGeometryViewer_PreloadFromCache(path)`.
  - İsteğe bağlıdır ("3B önbelleği projeye kaydet" ayarı), çünkü proje boyutunu büyütür.

## 10. Montaj Gezgini ile ilişkisi (ileride)

- Gezgini, ürün ağacını gösterecek ve düğüm (alt montaj / parça) bazında "dahil et / etme" kararı alacak. Örnek: kelepçe alt montajını taramadan çıkarmak (Obsidian 99, not 9).
- **Bu kararlar da proje kararıdır.** `proje.json`'a şema `1.1` ile ek bir bölüm gelir:
  `"montajGezgini": { "haricDugumler": [{ "kaynak": "k1", "yol": ["<productId>", "<productId>"], "ad": "Kelepçe" }] }`
- Hariç bırakılan düğüm, satırları **siler değil süzer**. Parça adetleri o düğümdeki örnekler düşülerek yeniden hesaplanır; motor yeniden çalışmaz.
- **Gereken motor çıktısı:** Ağacın düğüm/örnek yolu motor JSON'unda olmalı. Bugünkü 1.2 çıktısında parça listesi ve adet var, ama örnek ağacı yoksa motor şemasının yükseltilmesi gerekir. Eski motor çıktılı projeler Gezgini'nde "yeniden tarama gerekli" der. Bu yüzden her kaynakta `motorSemaSurumu` saklanır.
- **3B:** Gezgini aynı model önbelleğini ve `Step3BPaneli`'ni kullanır; hariç düğümler 3B'de gizlenir. Bunun için görüntüleyicide düğüm gizleme API'si gerekir.
- **Eşleme:** Yeniden taramada `yol` (productId zinciri) ile eşlenir; §8'in kuralları geçerlidir.

## 11. Sürümleme

- `manifest.schemaVersion` = `ana.ara`.
  - **Ara sürüm:** Yalnız alan ekler; eski okuyucu bilinmeyen alanı yok sayar ama dosyayı salt-okunur açar (§7.1).
  - **Ana sürüm:** Uyumsuz değişiklik demektir.
- Okuyucu, eski sürümleri adım adım geçiş fonksiyonlarıyla (`1.0→1.1`) güncel modele çevirir. Kaydetme her zaman güncel sürümde yapılır.
- Her şema sürümü için `GeometryLabAdapter.Tests`'te örnek bir proje dosyası tutulur ve okunması test edilir.
- Motor şeması ayrıdır; kaynak başına `motorSemaSurumu` olarak tutulur ve `IsSupportedSchemaVersion` ile denetlenir.

## 12. Riskler

1. **CATIA karşılaştırması saklanmazsa** (Aşama 1): "CATIA sac unsuru" (`ThreeDScan`) kararları açılışta kaybolur; satırlar "Onay gerekli"ye döner. Kullanıcıya açılışta bildirilir, Aşama 2 çözer. Ara çözüm: önemli olanları kullanıcı "Sac Olarak Onayla" ile karara çevirir.
2. **Yeniden taramada farklı sonuç:** Süre sınırı ya da motor sürümü değişmişse parça sınıfları değişebilir. Kararlar §8 ile korunur; değişen sınıflar rapora yazılır.
3. **Karar eşleme belirsizliği:** Aynı adlı parçalar ve yeniden adlandırılan parçalar. Belirsiz karar uygulanmaz, eşlenemeyenlere düşer.
4. **ZIP güvenliği:** Açarken `..` içeren ve mutlak yollar reddedilir (zip slip). Kayıt sayısı ve açılmış boyut sınırlanır (zip bombası).
5. **Kaydetme sırasında kesinti / OneDrive kilidi:** `.tmp` + `File.Replace` + `.bak`. Hata olursa eski dosya bozulmaz, hata gösterilir.
6. **Büyük proje:** WGRV DXF'leri küçük (KB'lar). 3B önbellek eklenirse onlarca MB olabilir; bu yüzden isteğe bağlı.
7. **Geçici DXF klasörü yaşam döngüsü:** Açılışta DXF'ler yeni oturum klasörüne açılır. Tabloyu Temizle ve proje kapatma bu klasörü siler; veri ZIP'te kalır.
8. **Salt-okunur modun karmaşıklığı:** Bütün karar düğmelerinin tek bir "salt-okunur" bayrağına bağlanması gerekir. En aza indirilir: düğmeler kapanır, Kaydet kapanır, başka bir şey değişmez.
9. **Ham JSON'un tutulması:** Adaptör değişikliği küçük ama test edilmeli. Canlı analiz ve proje açılışı aynı ayrıştırma yolunu kullanmalı.
10. **Yol gizliliği:** Proje dosyası mutlak yollar (kullanıcı adı, klasör yapısı) içerir; paylaşılırken görünür. Kabul edilen bir risk; `goreliYol` taşınabilirliği sağlar.

## 13. Aşamalar

- **Aşama 1 — en küçük çalışan sürüm:**
  - Kaydet / Farklı Kaydet / Aç.
  - `manifest.json` + `proje.json` + ham `analysis.json` + motor DXF'leri.
  - Kararlar `localId` ile.
  - Açılışta hash kontrolü:
    - aynıysa tarama yok;
    - değişmiş/bulunamadıysa basit bir soru: "Yeniden tara" (bulunduysa) / "Salt-okunur aç" / "İptal".
  - Motor sürümü (`motor-surumu.txt` + exe SHA-256); motor yeniyse aynı soruda "Yeniden tara / Kayıtlı sonuçla aç".
  - Varsayılan kayıt yeri: STEP'in yanı, STEP'in adıyla.
  - Kaydedilmemiş değişiklikte kapanış, yeni analiz ve başka proje açma sorusu.
  - **Testler:**
    - Sahte motor JSON'uyla ZIP round-trip ve şema sürümü reddi.
    - Zip slip reddi.
    - montaj-1 ile gerçek round-trip: kaydet → aç → satırlar ve kararlar aynı, motor çalışmadı.
- **Aşama 2:**
  - CATIA karşılaştırma anlık görüntüsü (`catia/tarama.json`) ile `ThreeDScan` kararlarının korunması.
  - Son açılanlar ve ana sayfadaki "Proje Aç" kartı.
  - "Yeni yolu göster" ve `goreliYol`.
  - Yeniden taramada `productId` / ad ile karar eşleme ve eşlenemeyen kararlar raporu.
  - DXF Üret geçmişi.
  - Kaydetmede `.bak`.
- **Aşama 3:**
  - Projeye dosya ekleme (yeni analiz listeyi değiştirmek yerine ekler).
  - `.macria` dosya ilişkilendirmesi ve sürükle-bırak.
- **Aşama 4:** Projede 3B model önbelleği (GeometryLab API'si gerekir); önbellek anahtarının SHA-256'ya geçmesi.
- **Aşama 5:** Montaj Gezgini bölümü (şema 1.1); motor şemasında örnek ağacı gerekiyorsa önce GeometryLab işi.

## 14. Aşama 1 uygulama notları (2026-10-03)

- **Kod:**
  - `MacriaProje.cs`: biçim, ZIP, şema, kaynak denetimi, karar eşleme.
  - `MacriaProjeSatirlari.cs`: motor sonucundan satır kurma ve kararlar.
  - `MainWindow.Proje.cs`: komutlar, kaydedilmemiş değişiklik, açılış.
  - `GeometryLabMotorKimligi` + `GeometryEngineRuntime/motor-surumu.txt`: motor sürümü.
  - `GeometryLabProcessAdapter.SonucuJsondanKur`: canlı analiz ve proje açılışı için ortak ayrıştırma.
- **Tasarımdan farklar:**
  - `goreliYol` ile bulma ve yeniden taramada `productId` / ad eşlemesi Aşama 1'e alındı. İkisi de küçük ve testli.
  - Lazer sınırı: Satırlar projenin değeriyle kurulur; Ayarlar'da değiştirilirse proje bunu izler ve kaydedilmemiş olur. Büküm bilgisi yalnız kayıttır; DXF için Ayarlar kullanılır ve fark konsola yazılır.
  - Eşlenemeyen kararlar şimdilik yalnız konsolda listelenir; rapor penceresi Aşama 2'de.
  - `.bak` henüz yok; kaydetme geçici dosya + `File.Replace` ile yapılıyor.
  - Komutlar yalnız STEP / STP Analizi ekranında: "Proje Aç / Kaydet / Farklı Kaydet" ve Ctrl+O / Ctrl+S / Ctrl+Shift+S. Ana sayfa kartı ve son açılanlar Aşama 2'de.
- **Ölçüm** (`GeometryLabAdapter.Tests` → `RealProjectRoundTripAsync`): analiz → karar → kaydet → aç (hash + satır + karar, motor çalışmadan).
  - montaj-1 (7 parça): analiz 1,0 s, kaydet 0,02 s, aç 0,06 s, dosya 169 KB.
  - WGRV004423 A (195 parça): analiz 59,9 s, kaydet 0,66 s, aç 0,82 s, dosya 7,8 MB.
