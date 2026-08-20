# Macria

CATIA / 3DEXPERIENCE montajlarındaki sac parçaları listeleyen ve DXF olarak dışa aktaran WPF masaüstü uygulaması.

## Ne yapar

- COM üzerinden çalışan CATIA / 3DEXPERIENCE örneğine bağlanır
- Aktif montajın ağacını gezip sac parçaları bulur
- Ürün adı, parça adı, kalınlık ve adet bilgisini tabloda gösterir
- Seçili parçayı ya da tüm listeyi CATIA'nın "Save As DXF" panelini sürerek DXF'e aktarır
- Toplu aktarım sırasında her pencerenin üstünde duran bir ilerleme penceresi (PiP) ve acil durdurma sunar

## Gereksinimler

- Windows x64
- .NET 8 (tek dosya self-contained yayınlarda gerekmez)
- Kurulu ve çalışan CATIA V5 ya da 3DEXPERIENCE

## Derleme

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
