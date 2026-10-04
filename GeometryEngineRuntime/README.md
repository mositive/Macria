# GeometryEngine Runtime Paketi

Bu klasör Macria'nın ayrı process olarak çalıştırdığı `Macria.GeometryEngine.exe`
ve onun app-local native çalışma dosyalarının tek kaynak kontrollü paketidir.

Güncelleme kuralı: GeometryLab'ın doğrulanmış x64 Release çıktısından yalnız
`Macria.GeometryEngine.exe`, gerçek bağımlılık kapanışındaki DLL'ler (izole
çalıştırmada gerekli olduğu doğrulanan `FreeImage.dll` dahil) ve MSVC runtime
DLL'leri alınır. Test EXE'leri, `.pdb`, `.lib`, `.exp` ve CMake ara
dosyaları buraya konmaz. `Macria.csproj` bu klasörü Debug, Release ve publish
çıktılarında `GeometryEngine\` altına kopyalar.

Motor sürümü: `Macria.GeometryEngine.exe` her değiştiğinde `motor-surumu.txt`
güncellenir. İlk satır sıralanabilir sürümdür (`yıl.ay.gün.n`, ör. `2026.10.3.1`;
aynı gün ikinci paket `.2`), ikinci satır GeometryLab commit'idir. Macria bunu
`.macria` projelerine yazar; açılışta kurulu motor daha yeniyse "eski motorla
taranmış, yeniden taransın mı?" diye sorar. Motor JSON'unda sürüm alanı yoktur.

Analiz motoru (`Macria.GeometryEngine.exe`) her zaman ayrı bir child process
olarak başlatılır. 3B STEP görüntüleyici ise farklıdır: `OcctViewerNative`,
`Macria.GeometryViewer.dll`'i ve OCCT DLL'lerini `LoadLibraryEx` ile Macria
işlemine yükler; görüntüleyicideki native bir çökme Macria'yı da kapatır.

## Sürüm kaydı

| Tarih | Değişen | Kaynak | Not |
|---|---|---|---|
| 2026-09-28 | `Macria.GeometryEngine.exe` | GeometryLab `1b9ea13`, x64 Release | JSON şema 1.2: sac tanıma, delikler, çok bükümlü açınım, montaj parçaları (`parts`), `--dxf-klasor`. Diğer 38 dosya (OCCT DLL'leri, viewer) bayt bayt aynı kaldı. Yeni exe'nin DLL bağımlılıkları (TKCDF, TKLCAF, TKXCAF dahil) bu klasörde mevcut. Önceki paket: `MACRIA-RUNTIME-ASAMA2-BEFORE-20260928-231623` (ZIP SHA-256 `B9095F0E…019651`). |
| 2026-09-29 | `Macria.GeometryEngine.exe` | GeometryLab `f385952`, x64 Release | İşlenmiş/markalanmış kutu profil parça sınıfında Profil; temel stok solid başına (`baseStockProfiles`). Diğer 38 dosya aynı. Önceki paket: `MACRIA-RUNTIME-ISLENMIS-PROFIL-BEFORE-20260929-234720` (ZIP SHA-256 `94CA7909…81EEBAC`). |
| 2026-09-30 | `Macria.GeometryEngine.exe` | GeometryLab `a3f8c42`, x64 Release | DXF: büküm bölgesi iki kesik-noktalı sınır çizgisi, etiket aralarında; KESIM/BUKUM katmanları; `--dxf-klasor` her sac için ayrıca yalnız-KESIM DXF (`dxfCutOnlyFile`). Diğer 38 dosya aynı. Önceki paket: `MACRIA-RUNTIME-DXF-KATMAN-BEFORE-20260930-000124` (ZIP SHA-256 `D4030E16…6DDDECC`). |
| 2026-09-30 | `Macria.GeometryViewer.dll` | GeometryLab `b589ad9`, x64 Release | Montaj parçası vurgulama (`MacriaGeometryViewer_HighlightPart`): seçili parçanın örnekleri vurgulu, diğerleri soluk. Motor exe ve diğer dosyalar aynı. Önceki paket: `MACRIA-RUNTIME-VURGU-BEFORE-20260930-001816` (ZIP SHA-256 `0EC12BAD…61C6D2F1`). |
| 2026-09-30 | `Macria.GeometryViewer.dll` | GeometryLab `92e4bf2`, x64 Release | Yalnız seçili parça görünümü (`MacriaGeometryViewer_ShowPart`, Isolated/InAssembly; görünüm çizilene sığdırılır). `HighlightPart` aynı davranışla duruyor. Motor exe ve diğer dosyalar aynı. Önceki paket: `MACRIA-RUNTIME-YALNIZ-PARCA-BEFORE-20260930-222102` (ZIP SHA-256 `F8850864…228EA697`). |
| 2026-09-30 | `Macria.GeometryViewer.dll` | GeometryLab `1450b6f`, x64 Release | "Yalnız parça" (`Isolated`) artık tek örneği parçanın kendi koordinatında çizer (montaj yerleşimi yok, tek parçalı STEP ile aynı sınırlar); `InAssembly` aynı. Motor exe ve diğer dosyalar aynı. Önceki paket: `MACRIA-RUNTIME-TEK-ORNEK-BEFORE-20260930-231931` (ZIP SHA-256 `E805EDA8…ABFEDD25`). |
| 2026-10-02 | `Macria.GeometryEngine.exe` | GeometryLab `477c222`, x64 Release | Geçerlilik parça başına (bozuk parça Kontrol gerekli, montajın gerisi analiz edilir; `geometryValid`, `validityIssues`); kenar dışbükeyliği normallerden; parça başına süre sınırı `--parca-sure-siniri` (varsayılan 120 s; `analysisSeconds`, `analysisTimedOut`); gövde başına paralel analiz `--is-parcacigi` (varsayılan çekirdek − 1). Şema 1.2, alanlar yalnız ek. WGRV004423 (195 parça) artık analiz ediliyor: 59 s. Yeni sistem bağımlılığı yalnız UCRT `api-ms-win-crt-convert-l1-1-0.dll` (Windows'ta mevcut). Diğer 38 dosya (OCCT DLL'leri, viewer) bayt bayt aynı kaldı. Önceki paket: Macria git geçmişinde bir önceki commit (BEFORE ZIP yerine). |
| 2026-10-03 | `Macria.GeometryEngine.exe` | GeometryLab `237a30a`, x64 Release | `--ilerleme`: `--output` ile stdout'a satır satır `MACRIA-ILERLEME <aşama> <tamamlanan> <toplam>` (okuma, topoloji, parca, sac); bayraksız davranış ve JSON aynı. Yavaş parça notu (30 s) JSON yerine stderr'de. Diğer 38 dosya aynı. Önceki paket: Macria git geçmişinde bir önceki commit. |
| 2026-10-03 | `Macria.GeometryViewer.dll` | GeometryLab `3463249`, x64 Release | Paylaşılan model önbelleği: STEP süreç başına bir kez XDE ile okunur ve bir kez üçgenlenir; bütün oturumlar paylaşır. Yeni dışa açık fonksiyonlar `MacriaGeometryViewer_PreloadStep`, `MacriaGeometryViewer_ReleaseModels`. WGRV004423, 4 oturum: 194 s / 938 MB → 8,4 s / 742 MB. Doğrudan `TKXSBase.dll` bağımlılığı kalktı (dosya runtime'da kalıyor, motor kullanıyor). Motor exe ve diğer dosyalar aynı. Önceki paket: Macria git geçmişinde bir önceki commit. |
| 2026-10-03 | `motor-surumu.txt` (yeni) | — | Kurulu motorun sürümü: `2026.10.3.1`, GeometryLab `237a30a` (exe değişmedi). `.macria` projeleri motor sürümünü buradan alır. |
| 2026-10-04 | `Macria.GeometryEngine.exe`, `motor-surumu.txt` → `2026.10.4.1` | GeometryLab `adce5dd`, x64 Release | Sac kuralı gerçek et genişliğine bakar: kalınlık, açınıma sığan en büyük dairenin çapını (halka genişliği, somun cidarı; dişli delikler dahil) geçiyorsa parça sac değildir. WGRV004423'te 4 parça Sheet → Other: `257300_Duplicate_10` (halka), `Hex nut with collar`, `355742`, `343221` (Ø30/Ø8,5 burç). 52 regresyon STEP'inin 51'inde fark sıfır; WGRV'de başka alan ve DXF değişmedi. Şema 1.2, JSON alanları aynı. Diğer 38 dosya aynı (34 DLL hash ile karşılaştırıldı, exe bağımlılıkları aynı). Önceki paket: Macria git geçmişinde bir önceki commit. |
