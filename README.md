# Macria

CATIA / 3DEXPERIENCE montajlarındaki sac parçaları listeleyen, tüm ürün ağacını gösteren, DXF dışa aktarımı ve deneysel kutu profil teşhisi sunan WPF masaüstü uygulaması.

## Ne yapar

- COM üzerinden çalışan CATIA / 3DEXPERIENCE örneğine bağlanır
- Aktif montajın occurrence ağacını gezip sac parçaları bulur; her Reference Title'ın geometrisini yalnızca bir kez teşhis eder
- "Sac Lazer Parça" sekmesinde yalnızca doğrulanmış Sheet Metal parçaları gösterir
- "Ürün Ağacı — Eşsiz Parçalar" sekmesinde montaj gruplarını ayıklayıp her Title'ı tek satırda, bütün occurrence'ların toplamını Adet sütununda gösterir
- "Hiyerarşik Ürün Ağacı" sekmesinde CATIA montaj kırılımını korur; ürün grupları + / − ile açılıp kapatılır
- Physical Product Reference alanlarından Parça Kodu (Title), PLM Kimliği (Name), Tanım (Description) ve Revizyon bilgisini okur
- Model kalınlığı, geçici Ham Sac kalınlığı ve adet bilgisini tabloda gösterir
- Ham Sac değerlerini her CATIA taramasında model kalınlığından yeniden başlatır; önceki taramayı hatırlamaz
- Sac Lazer ve Ürün Ağacı sütunları ortak ayar penceresinden gösterilip gizlenebilir ve sıralanabilir
- Sac Lazer ve Ürün Ağacı listeleri ayrı ayrı Excel çalışma kitabına aktarılabilir
- Gizlenmiş Physical Product öğeleri iki sekmede soluk renkle ve notla gösterilir; her sekmede ayrı ayrı işlem dışında bırakılabilir
- Durum sütununda DXF sonucu yeşil tik; başarısızlık ve Çoklu Body ise WPF'nin emoji fontuna bağlı kalmayan renkli 😔/🤔 görsel ikonlarıyla gösterilir
- Gizlenmiş Physical Product satırları Durum sütununda renkli 🕵️ görseliyle ayırt edilir
- Tarama sonunda bütün parça sekmelerinde eşsiz parça sayısı ile montajdaki toplam kullanım adedi ayrı ayrı özetlenir
- Çoklu Body öğeleri ayrı seçenekle Sac Lazer listesinden çıkarılabilir; görünür olsalar da güvenlik gereği toplu DXF işlemine alınmaz
- Çoklu Body içeren parçalar CATIA feature ağacı okunamasa bile Sheet Metal Thickness bilgisiyle uyarılı listelenir ve yanlış açınım riskine karşı otomatik olarak DXF dışında bırakılır
- Seçili parçayı ya da tüm listeyi CATIA'nın "Save As DXF" panelini sürerek DXF'e aktarır
- Toplu aktarım sırasında sağ altta açınımın oluşturulmasını canlandıran ilerleme penceresi (PiP) ve acil durdurma sunar
- Toplu DXF sırasında PiP'in üstünde, odak çalmayan ve Enes karakterini sekiz karelik doğal yürüyüş döngüsüyle canlandıran işlem penceresi gösterir; karakter gri ayaklı beyaz ofis masasından sağda bekleyen arkadaş grubuna yürür, bilgisayarın beyaz ekranında yalnızca TURAN aracı ve hemen altında mavi BMC logosu görünür
- Animasyon penceresi v1.9.11'e göre genişlik, yükseklik ve bütün içeriğiyle bir yüzde 10 daha küçültülmüştür; kahve makinesi ile bağlı bekleme ve buhar animasyonu kaldırılmıştır
- Son arkadaş grubu ve mavi varil, yalnızca teras arka planı kaldırılmış şeffaf bir grup olarak Enes ile aynı ölçekte aynı sahnede bekler; Enes yanlarına ulaşınca yürüyüş başa sarar
- İşlem tamamlandığında fotoğraf veya tam ekran görsel açılmaz; Enes arkadaşlarının yanında beklerken genel başarı/kontrol özeti gösterilir
- Karakter tasarımında topuz saç, sarı çerçeveli gözlük, sakal, beyaz tişört, kolye ve bileklik ayrıntıları `DesignReferences/Enes-Kahve-Penceresi-Referans.png` görseline göre korunur
- İstenirse "Bend Information" kutusunun görüntüsü bir kez öğretilir ve DXF export öncesinde işaretliyse otomatik kaldırılır
- Tarama, DXF, Ham Sac güncelleme, Excel ve ağırlık/maliyet işlemlerinin süreleri konsola yazılır
- "Kutu Profil (Deneysel)" sekmesi sac olmayan bütün katıları ilk taramada aday olarak listeler; böylece unsur geçmişi silinmiş **As Result** profiller gözden kaçmaz
- Seçili profil adayı CATIA'da açılarak Body/feature kanıtı, yüz sayısı, hacim-alan oranından yaklaşık cidar, ana atalet oranı ve bulunabiliyorsa sınır ölçüleri incelenir
- Teşhis sonucu kesin imalat kararı yerine güven puanı ve gerekçeleriyle gösterilir; ayrıntılı COM/geometri raporu Masaüstüne `.txt` olarak yazılır
- Seçili adayın CATIA'daki **mevcut geometrisi** `.stp/.step` olarak kaydedilebilir; bu ilk deneysel sürüm yay profili düzleştirmez ve CATIA kaynağını değiştirmez

## Gereksinimler

- Windows x64
- .NET 8 (tek dosya self-contained yayınlarda gerekmez)
- Kurulu ve çalışan CATIA V5 ya da 3DEXPERIENCE

## Derleme

Yeni sürümü eski kaynak klasörünün üzerine çıkarmayın. ZIP'i boş bir klasöre
çıkarın, `Macria.slnx` dosyasını açın ve Visual Studio'da **Derle > Çözümü Yeniden
Derle** komutunu kullanın. Açılan uygulamanın konsolundaki ilk satırda `Macria
v1.10.4 Hazır` yazmalıdır; başka bir sürüm görünüyorsa eski EXE çalışıyordur.

CATIA bulunmayan geliştirme bilgisayarında projeyi **Debug** olarak çalıştırıp
Macria penceresi odaktayken `F9` tuşuna basın. PiP, ofis-ekip animasyonu, 12 öğelik
sahte ilerleme ve `11 başarılı / 1 kontrol bekliyor` sonuç ekranı gerçek DXF
almadan gösterilir. Bu test kısayolu Release derlemesine dahil edilmez.

Yine Debug derlemesinde `F10`, CATIA olmadan Kutu Profil sekmesine üç örnek
satır yükler. Bu yalnızca tablo, renkler, seçim ve açıklamaları sınar; gerçek profil
teşhisi ve STEP için çalışan CATIA gerekir.

## Kutu Profil deneysel akışı

1. Montaj açıkken **CATIA'yı Tara** düğmesine basın.
2. **Kutu Profil (Deneysel)** sekmesine geçin.
3. Bilinen bir profil satırını seçip **Profil Teşhisi** düğmesine basın.
4. Durum, güven, ölçüm alanları ve Masaüstündeki teşhis raporunu kontrol edin.
5. Uygunsa **Seçiliyi STEP Kaydet** ile mevcut geometriyi dışa aktarın.

STEP işlemi önce CATIA'nın doğrudan `ExportData` yolunu, bu kullanılamazsa
3DEXPERIENCE kaydetme arayüzünü dener. Kurulumun STEP dönüştürücüsü/lisansı ve
yerelleştirilmiş komut davranışı sonucu etkileyebilir; başarısız adım konsola yazılır.

```
dotnet build Macria/Macria.csproj -c Release
```

## Taşınabilir sürüm

Yönetici hakkı olmayan makineler için tek dosya, kurulum gerektirmeyen exe:

```
dotnet publish Macria/Macria.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Çıktı `Macria/bin/Release/net8.0-windows/win-x64/publish/Macria.exe`. Yanındaki `.pdb`
yalnızca hata ayıklama sembolüdür, kopyalanması gerekmez.

## COM bağlantısı hakkında

`CatiaConnect` sırayla `CATIA.Application` ProgID'sini, `CATIA.Application.1` sürümlü
ProgID'sini, sabit CLSID `{87FD6F40-E252-11D5-8040-0010B5FA1031}` değerini ve son çare
olarak ROT taramasını dener. Kurumsal makinelerde CATIA'nın COM kaydı HKLM'e
yazılamadığı için ProgID hiç oluşmayabilir; `GetActiveObject` yalnızca CLSID istediğinden
bağlantı yine de kurulabilir. Bağlantı kurulamazsa uygulama konsoluna HRESULT'lu bir
teşhis bloğu basılır.
