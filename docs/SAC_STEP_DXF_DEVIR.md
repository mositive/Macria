# Devir Notu: STEP'ten Sac Tanıma, Delik Tanıma ve DXF Üretimi

- **Tarih:** 2026-09-27
- **Hazırlayan:** Claude (Cowork) — Enes ile yapılan çalışmanın özeti
- **İlgili dosyalar:** `DXF_HAVSA_ARASTIRMA.md`, `DXF_REFERENCE_SIDE_ARASTIRMA.md`, `tools/prototip/delik_tanima.py`

## 1. Sorun

CATIA "Save as DXF" paneli **Reference side** seçeneğine göre farklı çap üretiyor:

- Top ile havşa/imbus deliği kafa çapıyla (büyük) çıkıyor.
- Bottom ile geçiş çapıyla (küçük) çıkıyor.

Lazer kesim DXF'inde geçiş çapı olmalı.

## 2. Doğrulanmış bulgular

**DXF karşılaştırması** (`55RS100111-12`):

- Top → Ø40, Bottom → Ø16.
- Kontur, delik merkezi (101,153; −146,218) ve büküm çizgisi ("UP 90deg R 4") iki dosyada birebir aynı.
- Aynalama yok, extrusion (0,0,1), delikler CIRCLE, format AC1015.

**Panel:**

- Her açılışta Top geliyor, son seçimi hatırlamıyor.
- Seçim ayar dosyalarında tutulmuyor; yalnızca panelde tıklanarak değişiyor.
- Macria bu seçeneğe hiç dokunmuyor.

**STEP analizi** (`55RS100111-12 A.stp` ve `A_1.stp`, OCCT prototipi):

- Kalınlık 20 mm; iç R4 / dış R24 büküm. Deneme parçası, üretim parçası değil.
- Havşa 90°: Ø16 / Ø40, x = 20 yüzünden açılıyor (bükümün dış tarafı).
- İmbus (A_1): Ø16 / Ø34, x = 20 yüzünden açılıyor.
- DXF → 3B eşleşmesi: Top export'u parçaya x = 20 yüzünden bakıyor. Bu parçada **Top = bükümün dış tarafı** ⚠️ (tek örnek; genellenmemeli).
- CATIA açınımında büküm payı 11,01 mm (bu örnekte K ≈ 0,15).

## 3. Kararlar (Enes)

- Tespit **GeometryEngine ile B-Rep'ten** yapılacak; CATIA COM Hole unsuru kullanılmayacak.
- Kalınlık veya radyüs için **sayısal kural yazılmayacak**. Tanıma tamamen geometrik olacak.
- **Hedef:** Macria'nın 3B tanıma motoruna entegre etmek. Bütün STEP'ler taransın, saclar tespit edilsin ve DXF'e çevrilsin.

## 4. Önerilen tasarım

1. **Sac tanıma (motor):**
   - Her yüz A yüzü, B yüzü veya kenar yüzü olarak sınıflanır.
   - A/B sabit `t` ofsetinde olmalı: düzlemler paralel ve aralarında `t`; bükümler aynı eksende ve yarıçap farkı `t`.
   - Kenar yüzleri tam `t` yüksekliğinde olmalı.
   - Bütün yüzler bu gruplara oturursa parça sac sayılır. Kalınlık modelden okunur.
2. **Delik tanıma (motor):**
   - Aynı eksendeki dönel yüzler gruplanır.
   - Grup sacı boydan boya geçmiyorsa büküm silindiridir ve elenir.
   - Koni varsa havşa; farklı çaplı silindirler varsa imbus; tek silindir varsa düz delik.
   - En büyük çapın bulunduğu uç, açıldığı taraftır.
3. **Açınım (motor):** Düz yüzler tek bir düzleme açılır; bükümler büküm payıyla araya eklenir.
4. **DXF:**
   - Dış kontur
   - Delikler **her zaman geçiş çapıyla**
   - Büküm çizgileri ve etiketleri (CATIA biçimine benzer)
5. **JSON (schema 1.1, geriye uyumlu):**
   - `sheetMetal{ isSheet, thicknessMm, bends[], ... }`
   - `holeFeatures[]{ type, throughDiameterMm, headDiameterMm, headDepthMm | headAngleDeg, axis, center, openingSide }`
6. **Macria:** Dosya Analiz Merkezi'ndeki haricî STEP listesine "Sac (geometrik)", "Kalınlık" ve "Havşa/İmbus" sütunları; seçili satırlar için "DXF üret" düğmesi.

## 5. K-faktörü (CATIA formülü, doğrulandı)

CATIA'daki sac parametrelerinde kullanılan formül (Enes paylaştı):

```
K = log( min(100, max(20*R, T) / T) ) / log(100) / 2
```

Sadeleştirilmiş hali: `K = log10( clamp(20R/T, 1, 100) ) / 4`. K, 0 (R ≤ T/20) ile 0,5 (R ≥ 5T) arasında değişir.

- **Büküm payı:** `BA = (R + K·T) · θ` (θ radyan cinsinden büküm açısı)
- **Doğrulama (55RS100111-12):** R = 4, T = 20 → K = 0,150515; BA = 11,0117534. CATIA DXF'inden ölçülen BA = 11,0117534 (fark ~1e-9 mm). **Birebir tutuyor.**
- R ve T STEP geometrisinden okunduğu için motor K'yı her büküm için kendisi hesaplayabilir; ayrı bir tablo gerekmez. Not: R, büküm silindirlerinden **iç** yarıçaptır.
- ✅ Enes kontrol etti: **başka parçalarda da formül aynı.** Motorda varsayılan olarak bu formül kullanılabilir. İleride farklı bir şablon çıkarsa diye Macria ayarlarından değiştirilebilir bırakılması önerilir.
- ⚠️ 90° dışındaki açılar ve birden çok bükümlü parçalar ayrıca doğrulanmalı.

## 6. Riskler

- **Büküm payı:** Formül bilindiği için risk büyük ölçüde azaldı. Yine de motorun ürettiği DXF, ilk parçalarda CATIA DXF'iyle otomatik karşılaştırılıp tutana kadar "doğrulanmamış" işaretlenmeli.
- **Motor kaynak kodu Macria reposunda yok;** `GeometryEngineRuntime/` altında yalnızca derlenmiş dosyalar var.
- **AGENTS.md** kuralları geçerli: fallback zincirleri korunmalı, CATIA doğrulaması iş makinesinde yapılmalı, kabulden sonra Obsidian notları güncellenmeli.

## 7. Test verisi

Konum: `C:\Users\enesy\OneDrive\Desktop\Macria-4A-Gercek-Test\step\`

- `55RS100111-12 A.stp`: havşa
- `55RS100111-12 A_(1).stp`: havşa + imbus
- `55RS100111-12 havsa Buyuk.dxf` / `havsa Kucuk.dxf`: CATIA Top/Bottom çıktıları
