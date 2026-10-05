# Çalışma Özeti: 24.09.2026 – 05.10.2026

**128 commit** (Macria 86, geometri motoru 42), 25.09.2026 – 05.10.2026. Bu özetin kendi commit'i sayıya dahil değil.
Kaynak: iki deponun git geçmişi ve Obsidian notları (99 - Yapılacaklar, 05 - Geometri Tanıma, 06 - Hatalar ve Çözümler).

## Geometri motoru (sac / profil tanıma, açınım, DXF)

- **Sac tanıma ve açınım CATIA'sız yapılıyor.** Motor STEP dosyasından şunları kendisi çıkarıyor: sacın kalınlığı, bükümleri, delik türleri (havşa, imbus, düz, dişli) ve açınımı. Açınımı DXF olarak yazıyor. İlk sürüm bükümsüz ve tek bükümlü sacları açıyordu; şimdi çok bükümlü saclar da açılıyor. Kabul testinde 9 parçanın 9'unda motorun DXF'i CATIA'nın DXF'iyle 0,01 mm içinde aynı çıktı.
- **Montaj dosyası parça parça okunuyor.** Her parçanın adı, adedi, türü (sac / profil / diğer) ve kendi DXF'i çıkıyor. Örnek montajda 7 parçanın adları ve adetleri doğru geldi.
- **Sınıflandırma hataları düzeltildi.**
  - Halka, somun ve burç artık sac sayılmıyor; WGRV montajında 4 parça düzeldi.
  - Dolu mil artık profil sayılmıyor; profil yalnız içi boş kesit.
- **CATIA'nın yuvarlak köşeleri artık açılıyor.** CATIA bu köşeleri "serbest eğri" olarak yazıyor ve motor bu parçalarda takılıyordu. Şimdi bu eğriler yay ve doğruya çevriliyor. WGRV'de elle sac onaylanan 11 parçanın DXF'i artık çıkıyor.
- **İşlenmiş plakalar sac olarak tanınıyor.** Cepli, basamaklı, havşalı tabla ve plakalarda kalınlık ham levhadan alınıyor ve DXF'e yalnız boydan boya kesimler giriyor. Örnek: 243441 artık sac ve kalınlığı 18 yerine doğru değer olan 20 mm. Beş plakada "işleme var" görünüyor.
- **Gravür ve kabartma yazılar işleme sayılıyor.** Bunlar açınıma kesim olarak girmiyor. Parçada "İşleme: var (gravür)" ya da "var (kabartma)" yazıyor; WGRV'de 3 parçada bulundu.
- **DXF birim hatası düzeltildi.** 342563 CATIA'da 25,4 kat büyük açılıyordu (Ø30 yerine Ø762). Şimdi doğru ölçüde açılıyor (29,4 × 30 mm).
- **DXF katmanlı yazılıyor.** Kesim ve büküm çizgileri ayrı katmanlarda; büküm çizgisi kesik çizgiyle çiziliyor.
- **Açınım ölçüsü yanında yeni ölçü.** Açınımı çevreleyen en küçük dikdörtgen de hesaplanıyor. Örnek: 325275'te 219,6 × 90,2 yerine 76,5 × 223,1.
- **20 mm levhalar doğru grupta.** Yuvarlama artığı yüzünden Şalama/Kütük'e düşüyorlardı; şimdi Lazer'de kalıyorlar.

## Hız ve kararlılık

- **Büyük montaj analiz edilebiliyor.** WGRV004423 (195 parça) içindeki tek bir bozuk parça yüzünden hiç analiz edilemiyordu. Artık bozuk parça "Kontrol gerekli"ye düşüyor, kalan parçalar analiz ediliyor.
- **Analiz süresi kısaldı.** WGRV analizi 25 dakikanın üstündeydi; önce 165 saniyeye, işi işlemci çekirdeklerine bölünce **59 saniyeye** indi.
- **3B önizleme hızlandı.** Dosya artık bir kez okunup bütün panellerde ortak kullanılıyor. WGRV'de dört panelin açılışı 194 s'den 8,4 s'ye, bellek kullanımı 938 MB'tan 742 MB'a indi.
- **Kapanışta çökme çözüldü.** Macria kapanırken çöküyor ve arkada silinemeyen bir süreç bırakıyordu. 10 çökme kaydının hepsinde aynı neden bulundu ve giderildi.
- **Uzun analizde kontrol kullanıcıda.** İlerleme çubuğu ("142 / 195 parça"), İptal düğmesi ve parça başına süre sınırı (120 s) var. Takılan parça bütün montajı durdurmuyor.
- **Değişiklikler düzenli test ediliyor.** Motorun her değişikliğinde 52 STEP dosyalık karşılaştırma çalışıyor. Otomatik kontrol sayısı: motor 583, Macria 850. Testler ekranda pencere açmıyor.

## Arayüz (sekmeler, 3B, tablolar)

- **Dosya Analiz Merkezi beş sekmeye ayrıldı:** Profiller, Saclar, Kontrol gerekli, Tanımsız ve Liste dışı.
  - Satırları gizleyen filtre kutuları kalktı.
  - Her sekmede aynı araç çubuğu ve aynı karar düğmeleri var: Sac/Profil Olarak Onayla, Kontrol Gerekliye Al, Liste Dışına Çıkar, Geri Al.
- **Saclar sekmesi:**
  - Lazer ve Şalama/Kütük alt sekmeleri.
  - Onay durumu renkli etiketle gösteriliyor.
  - "DXF Üret" onaylı sacların DXF'ini tek seferde yazıyor.
  - Açınım (2B) ile 3B arasında geçiş yapılabiliyor.
- **Tek 3B panel.** Üç ayrı 3B görüntüleyicinin yerini tek ortak panel aldı. "Büyük Aç" seçili parçayı izliyor.
- **Tablolar:**
  - Her sekmede sütun seçme ve sıralama, arama, sütun genişliğinin hatırlanması.
  - Excel'e görünen sütunlar aktarılıyor.
  - Motorun gerekçesi ile kullanıcının kararı ayrı sütunlarda.
- **Kalınlık elle düzeltilebiliyor.** Satır vurgulanıyor. Daha önce yazılmış DXF yeni adla yeniden adlandırılıyor. Parça Lazer ile Şalama arasında yer değiştirirse uyarı çıkıyor.
- **Ayarlar ve Hakkında pencereleri yenilendi.** Ayarlar penceresi artık küçültülebiliyor ve kaydırılabiliyor.
- **Renklendirme 2.0 gerçek CATIA'da doğrulandı:** 18 parçanın 18'i renklendi ve Automatic'e geri döndü, hata yok.

## Proje dosyası (.macria)

- **Analiz tek dosyada saklanıyor.** Sonuçlar, motor DXF'leri ve kullanıcı kararları tek bir proje dosyasına kaydediliyor. Kaydet, Farklı Kaydet ve Aç var. Kapanışta kaydedilmemiş değişiklik soruluyor.
- **Değişmemiş dosya yeniden analiz edilmiyor.** Proje açılırken STEP dosyası değişmemişse (parmak izi kontrolü, WGRV'de ~0,1 s) analiz tekrarlanmıyor.
- **Eski analizler fark ediliyor.** Projeyi eski bir motor sürümü analiz etmişse açılışta yeniden analiz öneriliyor.
- **Eski projeler açılmaya devam ediyor.** Eski biçimdeki projeler yeni sekmelere doğru dağılıyor. Elle düzeltilen kalınlık ve yazılan DXF yolları da projede saklanıyor.

## Lisans ve dağıtım

- **Üçüncü taraf lisansları denetlendi.** Motorla birlikte dağıtılan her kütüphanenin (OCCT, FFmpeg, FreeImage vb.) lisans metni pakete eklendi: motor klasöründe 13, Macria'da 5 dosya.
  - `THIRD_PARTY_NOTICES.txt` eklendi.
  - Hakkında penceresine "Üçüncü taraf yazılımlar" bölümü eklendi.
  - Bir test eksik lisans dosyasını yakalıyor.
- **Motor paketi kendini doğruluyor.** Motorun sürümü pakette bir dosyada yazılı. Derleme, motor dosyalarını içerik karşılaştırmasıyla kopyalıyor; bayat dosya kalmıyor.
- **Sürüm:** Macria 1.11.1'den 1.11.2'ye çıktı. Motor sürümü 2026.10.5.4.

## Açık kalanlar

- **Baskı Kolu ve Mafsal'ın açınımı çıkmıyor.** Uç bükümlü kıskaç parçalarında açınım için motorda yeni bir adım (G3) gerekiyor.
- **Henüz başlanmayanlar:**
  - "Yeniden analiz et".
  - "Sac/Profil olarak dene": motor profil ölçülerini kendisi alsın. Örnek: 236948, köşeleri yuvarlak kısa kare boru.
- **Planlı ama yapılmayanlar:** Saclar'da DXF Düzenle; profilleri parça başına STEP olarak dışa aktarma.
- **DXF birim düzeltmesi lazer programında denenmedi.** CATIA'da kabul edildi.
- **Tanınmayan sac unsurları:** Stamp ve kıvrık kenar (hem). 426252'nin neden sac sayılmadığı da incelenecek.
- **Lisans:** Kaynak kodu paylaşım yeri hâlâ yer tutucu. OCCT'nin FFmpeg ve OpenVR olmadan yeniden derlenmesi değerlendirilecek; paket yaklaşık 20 MB küçülür.
- **Bugünkü dört iş elle denenmedi:** açınım ölçüsü hücresi, en küçük dikdörtgen sütunu, gravür ve sütun genişlikleri.
- **CATIA'ya bağlı davranışlar iş yerinde doğrulanmalı.** CATIA ile karşılaştırma ve gerçek DXF yazımı geliştirme makinesinde doğrulanamıyor.
