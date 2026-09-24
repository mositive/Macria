# GeometryEngine Runtime Paketi

Bu klasör Macria'nın ayrı process olarak çalıştırdığı `Macria.GeometryEngine.exe`
ve onun app-local native çalışma dosyalarının tek kaynak kontrollü paketidir.

Güncelleme kuralı: GeometryLab'ın doğrulanmış x64 Release çıktısından yalnız
`Macria.GeometryEngine.exe`, gerçek bağımlılık kapanışındaki DLL'ler (izole
çalıştırmada gerekli olduğu doğrulanan `FreeImage.dll` dahil) ve MSVC runtime
DLL'leri alınır. Test EXE'leri, `.pdb`, `.lib`, `.exp` ve CMake ara
dosyaları buraya konmaz. `Macria.csproj` bu klasörü Debug, Release ve publish
çıktılarında `GeometryEngine\` altına kopyalar.

Bu paket Macria işlemine native DLL yüklemez; motor yalnız ayrı bir child
process olarak başlatılır.
