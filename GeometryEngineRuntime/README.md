# GeometryEngine Runtime Paketi

Bu klasör Macria'nın ayrı process olarak çalıştırdığı `Macria.GeometryEngine.exe`
ve onun app-local native çalışma dosyalarının tek kaynak kontrollü paketidir.

Güncelleme kuralı: GeometryLab'ın doğrulanmış x64 Release çıktısından yalnız
`Macria.GeometryEngine.exe`, gerçek bağımlılık kapanışındaki DLL'ler (izole
çalıştırmada gerekli olduğu doğrulanan `FreeImage.dll` dahil) ve MSVC runtime
DLL'leri alınır. Test EXE'leri, `.pdb`, `.lib`, `.exp` ve CMake ara
dosyaları buraya konmaz. `Macria.csproj` bu klasörü Debug, Release ve publish
çıktılarında `GeometryEngine\` altına kopyalar.

Analiz motoru (`Macria.GeometryEngine.exe`) her zaman ayrı bir child process
olarak başlatılır. 3B STEP görüntüleyici ise farklıdır: `OcctViewerNative`,
`Macria.GeometryViewer.dll`'i ve OCCT DLL'lerini `LoadLibraryEx` ile Macria
işlemine yükler; görüntüleyicideki native bir çökme Macria'yı da kapatır.

## Sürüm kaydı

| Tarih | Değişen | Kaynak | Not |
|---|---|---|---|
| 2026-09-28 | `Macria.GeometryEngine.exe` | GeometryLab `1b9ea13`, x64 Release | JSON şema 1.2: sac tanıma, delikler, çok bükümlü açınım, montaj parçaları (`parts`), `--dxf-klasor`. Diğer 38 dosya (OCCT DLL'leri, viewer) bayt bayt aynı kaldı. Yeni exe'nin DLL bağımlılıkları (TKCDF, TKLCAF, TKXCAF dahil) bu klasörde mevcut. Önceki paket: `MACRIA-RUNTIME-ASAMA2-BEFORE-20260928-231623` (ZIP SHA-256 `B9095F0E…019651`). |
| 2026-09-29 | `Macria.GeometryEngine.exe` | GeometryLab `f385952`, x64 Release | İşlenmiş/markalanmış kutu profil parça sınıfında Profil; temel stok solid başına (`baseStockProfiles`). Diğer 38 dosya aynı. Önceki paket: `MACRIA-RUNTIME-ISLENMIS-PROFIL-BEFORE-20260929-234720` (ZIP SHA-256 `94CA7909…81EEBAC`). |
