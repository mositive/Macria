# Macria v1.10.4 — Kutu Profil / STEP Deneme Rehberi

Bu sürümün amacı, kutu profil olabilecek parçaları kaybetmeden listelemek,
seçili parçayı ayrıntılı incelemek ve CATIA'daki mevcut geometrisini STEP olarak
kaydetmektir. Yay profili düzleştirme bu sürümün kapsamında değildir.

## 1. Visual Studio'da açma

1. ZIP'i boş bir klasöre çıkarın.
2. Klasördeki `Macria.slnx` dosyasını Visual Studio ile açın.
3. Üst araç çubuğunda **Debug** ve **Any CPU** seçili olabilir; proje kendi
   ayarında x64 hedeflenmiştir.
4. **Derle > Çözümü Yeniden Derle** seçeneğini çalıştırın.
5. Yeşil **Macria** düğmesine veya `F5` tuşuna basın.
6. Konsolun ilk satırında `Macria v1.10.4 Hazır` yazdığını doğrulayın.

## 2. CATIA olmayan bilgisayarda arayüz testi

Macria penceresi odaktayken `F10` tuşuna basın. Kutu Profil sekmesinde üç sahte
satır görünür:

- kuvvetli profil adayı,
- geçmişsiz/yay profil adayı,
- karar verilemeyen katı parça.

Bu test yalnızca yeni sekmeyi, renklendirmeyi, filtreyi, seçim düğmelerini ve
tooltip açıklamalarını doğrular. Sahte satırların gerçek CATIA referansı olmadığı
için STEP üretmez. `F10` yalnızca Debug derlemesinde bulunur.

## 3. İş bilgisayarında gerçek profil teşhisi

1. CATIA/3DEXPERIENCE'ta profil parçalarını içeren Physical Product montajını açın.
2. Macria'da **CATIA'yı Tara** düğmesine basın.
3. **Kutu Profil (Deneysel)** sekmesine geçin.
4. Bilinen bir profil satırını seçip **Profil Teşhisi** düğmesine basın.
5. Şu alanları kontrol edin: Durum, Sınır Ölçüleri, Tahmini Et, Atalet Oranı,
   Yüz, Güven ve Not.
6. Masaüstünde oluşan `macria_profil_teshis_...txt` dosyasını açın.
7. Parça doğruysa **Seçiliyi STEP Kaydet** düğmesine basın ve `.stp` yolunu seçin.
8. STEP'i tekrar CATIA'da açıp geometriyi gözle doğrulayın.

Macria teşhis veya STEP sırasında açtığı parçayı işlem sonunda kapatır; kaynak
CATIA parçasını kaydetmez ya da değiştirmez.

## 4. Özellikle denenmesi gereken parçalar

| Deneme | Beklenen davranış |
|---|---|
| Düz, tek Body kutu profil | Güçlü/orta profil kanıtı ve çalışan STEP |
| Sweep/Süpürme ile yapılmış yay profil | Unsur kanıtı görülür; STEP eğri mevcut geometri olur |
| As Result, geçmişsiz yay profil | İlk listede görünür; geometri kanıtlarıyla puanlanır |
| Uçları açılı kesilmiş profil | Listede kalır; STEP açılı uçları korur |
| Kabuk/Shell ile boşaltılmış profil | Shell/Kabuk kanıtı rapora girer |
| Çoklu Body parça | Çoklu Body uyarısı verir; kullanıcı kontrolü ister |
| Dolu mil veya işlenmiş blok | Düşük güven / Karar verilemedi beklenir |

## 5. Sorun çıkarsa paylaşılacak bilgiler

- Macria konsolunun özellikle `STEP yolu — ...` satırları,
- Masaüstündeki profil teşhis `.txt` raporu,
- CATIA sürümü ve arayüz dili,
- parçanın ağaç görünümü,
- STEP kaydetme penceresi açıldıysa ekran görüntüsü.

Bu bilgiler, bir sonraki sürümde gerçek kesit doğrulaması ve yay profil
düzleştirme algoritmasını CATIA kurulumunuza göre sağlamlaştırmak için kullanılacaktır.

## 6. v1.10.1 STEP düzeltmesi

- 3DEXPERIENCE'ta bulunmayan `Save As STEP` ve `Save As` StartCommand
  çağrıları kaldırıldı.
- Genel `Export` komutu açılıyor ve STEP türü açılan dışa aktarma
  penceresinden seçiliyor.
- V5 uyumluluğu için `ExportData` hem uzantısız hedef yol hem de tam dosya
  yolu ile deneniyor.
- DXF paneline öğretilen Save As koordinatı STEP işleminde kullanılmıyor.
