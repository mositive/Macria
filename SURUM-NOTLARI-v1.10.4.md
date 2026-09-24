# Macria v1.10.4 — Eşsiz Title ve Hiyerarşik Ürün Ağacı

Bu sürüm, kullanıcı tarafından doğru temel kabul edilen **v1.10.1** kaynakları
üzerinden hazırlanmıştır. v1.10.3'ten profil teşhis değişiklikleri alınmamış;
yalnızca son bekleyen arkadaşlar görseli ve okunabilir kahve penceresi yazıları
aktarılmıştır.

## Tarama düzeltmesi

- Parça tekilleştirme anahtarı Physical Product **Reference Title** oldu.
- Aynı Title montajda kaç kez kullanılırsa kullanılsın, sac/profil geometrisi
  yalnızca ilk sağlam occurrence üzerinden bir kez teşhis edilir.
- Diğer occurrence'lar yeni satır veya yeni geometrik teşhis oluşturmaz; yalnızca
  **Adet** değerini artırır.
- Bir yaprak Physical Product içindeki birden fazla Part/CATIAPart representation,
  adedi birden fazla artırmaz.
- Alt occurrence içeren Physical Product düğümleri montaj grubu kabul edilir ve
  düz eşsiz parça listesine alınmaz.
- Konsolda `Parça occurrence / Eşsiz Title / Geometrik teşhis sayısı` ayrı ayrı
  raporlanır.

## Yeni liste görünümleri

- **Ürün Ağacı — Eşsiz Parçalar:** Yalnızca gerçek parçaları, Title başına tek
  satırda gösterir. Montaj grupları bu tabloda görünmez.
- **Hiyerarşik Ürün Ağacı:** CATIA occurrence ağacını ürün grupları ve parçalarıyla
  korur. Grup yanındaki `+ / −` düğmesi alt seviyeleri açar veya kapatır.
- Sac Lazer, eşsiz parça ve hiyerarşik ağaç sekmelerinin altında kendi özetleri
  bulunur. Eşsiz parça sayısı ile montajdaki toplam kullanım artık karıştırılmaz.

## Çoklu Body

- Sac Lazer sekmesine **Çoklu Body Öğelerini Dahil Et** seçeneği eklendi.
- Seçenek kapatıldığında Çoklu Body satırları listeden çıkarılır.
- Seçenek açık olsa bile Çoklu Body parçaları toplu DXF kuyruğuna girmez.
- 🤔 simgesi, renk ve açıklama korunur.

## Yeni Güncelleme İsteği 1

- DXF PiP penceresi, kahve animasyonu, önizleme başlığı, konsol işlem satırları ve
  başarısız parça listesinde `3D Shape...` yerine **Reference Title** gösterilir.
- 3D Shape/representation değeri yalnızca CATIA iç referansı ve teknik teşhis için
  içeride tutulur.

## Evde hızlı kontrol

1. ZIP'i yeni bir klasöre çıkarın ve `Macria.slnx` dosyasını açın.
2. Visual Studio'da **Derle > Çözümü Yeniden Derle** seçin.
3. Konsolun ilk satırında `Macria v1.10.4 Hazır` yazdığını doğrulayın.
4. CATIA olmayan bilgisayarda Debug çalıştırıp `F9` ile son arkadaş gruplu kahve
   animasyonunu test edin.
5. Gerçek CATIA montajında tarama sonunda konsoldaki `Title tekilleştirme` satırını
   kontrol edin. Örneğin 1036 occurrence ve 107 eşsiz parça varsa geometrik teşhis
   sayısı 107 olmalıdır.
