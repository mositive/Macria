# Macria Çalışma Kuralları

## Kaynakların Rolü

- Gerçek davranış için birincil kaynak mevcut Macria kaynak kodudur.
- `MacriaObsidian/Macria` klasörü teknik hafıza ve dokümantasyon kaynağıdır.
- Kaynak kod ile Obsidian çelişirse çelişki açıkça belirtilir; gizlenmez.

## Kod ve Tasarım Ayrımı

- Mevcut kod davranışı, hedef tasarım kuralı ve kullanıcı isteği birbirine karıştırılmaz.
- Kodda uygulanmayan bir kural uygulanmış gibi belgelenmez.
- Tasarım hedefleri Obsidian'da `tasarım kuralı`, `Karar Bekliyor` veya eşdeğer açık etiketlerle tutulur.

## Kod Değişikliği Öncesi

- Etkilenecek dosyalar ve olası riskler önce belirlenir.
- Gereksiz refactor yapılmaz.
- İstenen konu dışındaki dosyalara dokunulmaz.
- CATIA COM, `ReferenceKey`, DXF export, maliyet ölçümü, profil tanıma ve UI otomasyonunda mevcut fallback zincirleri korunur.

## Kod Değişikliği Sonrası

- Build sonucu kontrol edilir.
- Mümkünse ilgili test veya doğrulama adımı çalıştırılır.
- Başarısız veya doğrulanmamış değişiklik tamamlanmış gibi belgelenmez.
- Kullanıcı kabulünden önce değişiklik kalıcı başarı olarak işaretlenmez.

## Obsidian Güncelleme

- Kod değişikliği doğrulanıp kabul edildiğinde yalnızca ilgili Obsidian belgesi güncellenir.
- CATIA bağlantısı: `02 - CATIA Entegrasyonu.md`
- DXF / Sheet Metal: `03 - DXF ve Sheet Metal.md`
- Maliyet / ölçüm: `04 - Maliyet ve Ölçüm.md`
- Profil / geometri: `05 - Geometri Tanıma.md`
- Hata / fallback: `06 - Hatalar ve Çözümler.md`
- Teknik borç / roadmap / karar: `99 - Yapılacaklar.md`
- Genel davranış veya mimari: `00 - Macria Genel Bakış.md` ve gerekirse `01 - Mimari.md`

## Dokümantasyon Sınırı

- Yalnızca gerçekten değişen davranış güncellenir.
- Tüm dokümantasyon yeniden yazılmaz.
- Gerekirse tarihsel davranış `önceki davranış` notuyla korunur.
- Doğrulanmayan bilgi kesin gerçek gibi yazılmaz.

## Tamamlanan İş Akışı

Gerçekten tamamlanan bir geliştirmede:

- İlgili Obsidian belgesindeki mevcut davranış güncellenir.
- `99 - Yapılacaklar.md` içindeki ilgili madde güncellenir.
- Gerekiyorsa `Karar Bekliyor` durumu kaldırılır.
- Gerekiyorsa yeni hata/fallback davranışı `06 - Hatalar ve Çözümler.md` içine eklenir.

## Kritik Alanlar

Aşağıdaki alanlarda ekstra dikkat gösterilir:

- `ReferenceKey`, `_repRefs`, `_costRepRefs`
- CATIA COM / ROT ve `PLMOpenService`
- `InertiaService` / `MeasureService`
- DXF Save As UI otomasyonu ve Bend Information
- Çoklu Body ve gizli Physical Product davranışı
- Profil güven puanı ve yüz sayısı eşikleri
- STEP export
- `Task.Run`, UI thread ve COM ilişkisi

## DXF Edit Modu

- Önizleme, ölçüm ve analiz özellikleri DXF dosyasına yazamaz.
- DXF değişikliği yalnızca açıkça tanımlanmış DXF Edit Modu içinde yapılabilir.
- Edit işlemleri Undo/Redo desteklemeli; kayıt öncesi yedek alınmalı ve kullanıcı onayı olmadan orijinal dosyanın üzerine yazılmamalıdır.

## Belirsizlik

- Kaynak koddan doğrulanamayan teknik bilgi `doğrulanmalı` olarak işaretlenir.
- Tahmin yapılmaz.
- Kullanıcı kararı gereken konularda karar verilmez; `Karar Bekliyor` olarak işaretlenir.

## İşlem Sonu Raporu

Her anlamlı geliştirme sonunda kısa özet verilir:

- Değişen dosyalar
- Değişen davranış
- Build/test sonucu
- Güncellenen Obsidian dosyaları
- Açık kalan riskler
