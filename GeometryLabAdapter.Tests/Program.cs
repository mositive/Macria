using System.Text;
using System.Text.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Macria;

internal static class Program
{
    private static int _assertions;
    private static string _root = "";
    private static string _step = "";

    // Real-engine and real-file tests read the repository's fixed copies
    // (Fixtures/: .macria projects, single-part STEPs, montaj-1) and the
    // packaged engine, never the user's working files. An environment
    // variable still points a test elsewhere (engine development).
    private static readonly string DepoKoku = DepoKokunuBul();
    private static readonly string Fiksturler = Path.Combine(DepoKoku, "GeometryLabAdapter.Tests", "Fixtures");

    private static string DepoKokunuBul()
    {
        for (DirectoryInfo? klasor = new(AppContext.BaseDirectory); klasor != null; klasor = klasor.Parent)
            if (Directory.Exists(Path.Combine(klasor.FullName, "GeometryEngineRuntime")) &&
                Directory.Exists(Path.Combine(klasor.FullName, "GeometryLabAdapter.Tests")))
                return klasor.FullName;
        return "";
    }

    private static string? Ortam(string ad)
    {
        string? deger = Environment.GetEnvironmentVariable(ad);
        if (!string.IsNullOrWhiteSpace(deger) || DepoKoku.Length == 0) return deger;
        return ad switch
        {
            "MACRIA_GEOMETRY_ENGINE_EXE" => Path.Combine(DepoKoku, "GeometryEngineRuntime", "Macria.GeometryEngine.exe"),
            "MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR" => Path.Combine(Fiksturler, "stepler", "montaj-1"),
            "MACRIA_TEK_PARCA_KOK" => Path.Combine(Fiksturler, "stepler"),
            "MACRIA_ESKI_PROJELER" => Path.Combine(Fiksturler, "projeler", "WGRV004423 A.macria") + ";" +
                                      Path.Combine(Fiksturler, "projeler", "B-Rep Calisma A.macria"),
            _ => null
        };
    }

    private static async Task<int> Main()
    {
        _root = Path.Combine(Path.GetTempPath(), "Macria-GeometryLabAdapter-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _step = Path.Combine(_root, "input.stp");
        await File.WriteAllTextAsync(_step, "temporary test input");
        try
        {
            await MissingEngineAsync();
            await MissingStepAsync();
            EngineLocatorTests();
            await RealEngineAsync();
            await RealBaseStockEngineAsync();
            await RealNormalPriorityEngineAsync();
            await RealNegativeEligibilityEngineAsync();
            await TimeoutAsync();
            await NonZeroExitAsync();
            await MissingJsonAsync();
            await InvalidJsonAsync();
            await UnsupportedSchemaAsync();
            await SheetMetalSchemaAsync();
            await PartsSchemaAsync();
            await SeciliParcaAdaptoruAsync();
            await RealAssemblyEngineAsync();
            DenemeModeliTests();
            EkAnalizProjeTests();
            AssemblyPartRows();
            KalinlikDuzeltmeTests();
            KararSutunlariTests();
            SutunDuzeniTests();
            MotorGerekcesiTests();
            ExcelDegeriTests();
            await UctanUcaBoyAsync();
            ParcaAdiVeVirgulTests();
            AramaTests();
            DxfYenidenAdlandirmaTests();
            AssemblyProcessedProfilePart();
            AssemblyPartProfileGeometry();
            MotorDxfExport();
            await CancellationAsync();
            await MultipleProfilesAsync();
            StepProfileListFormatting();
            BaseStockTransportDeserialization();
            BaseStockFallbackFormatting();
            ExternalStepFilterFormatting();
            CatiaComparisonSyntheticTests();
            CatiaLightInventorySyntheticTests();
            DxfDwgInventoryTests();
            ProductionPackageTests();
            ProfilStepTests();
            PreviewCoreTests();
            PreviewInventoryTests();
            PreviewContentCheckTests();
            OcctStepPreviewAdapterTests();
            Step3BAracDurumuTests();
            RealViewerDllPinned();
            StoredJsonResultTests();
            MotorKimligiTests();
            MacriaProjeTests();
            SekmeKurallariTests();
            UcuncuTarafBildirimleriTests();
            RuntimeLisanslariTests();
            await RealProjectRoundTripAsync();
            await RealSinglePartPathAsync();
            RealOldProjectsTests();
            ExternalStepExcelWriter();
            await TemporaryStepWorkspaceAsync();
            await TemporaryStepFailureAndCancellationAsync();
            Console.WriteLine($"PASS: {_assertions} GeometryLab adapter assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL after {_assertions} assertions: {exception}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private static async Task MissingEngineAsync()
    {
        GeometryLabProcessAdapterResult result = await Adapter(Path.Combine(_root, "missing.exe")).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.EngineNotFound, "missing engine returns structured EngineNotFound");
    }

    private static async Task MissingStepAsync()
    {
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("valid", ValidJsonScript())).AnalyzeAsync(Path.Combine(_root, "missing.stp"));
        Check(result.Status == GeometryLabProcessAdapterStatus.StepNotFound, "missing STEP returns structured StepNotFound");
    }

    private static void EngineLocatorTests()
    {
        string locatorRoot = Path.Combine(_root, "locator");
        string packagedDirectory = Path.Combine(locatorRoot, "app", "GeometryEngine");
        string developmentDirectory = Path.Combine(locatorRoot, "development");
        CreateEngineInstallation(packagedDirectory);
        CreateEngineInstallation(developmentDirectory);

        GeometryLabEngineLocation packaged = GeometryLabEngineLocator.Locate(
            Path.Combine(locatorRoot, "app"), Path.Combine(locatorRoot, "missing", "Macria.GeometryEngine.exe"));
        Check(packaged.IsAvailable && packaged.Kind == GeometryLabEngineLocationKind.Packaged,
            "packaged GeometryEngine wins over an invalid development override");

        Directory.Delete(packagedDirectory, true);
        GeometryLabEngineLocation development = GeometryLabEngineLocator.Locate(
            Path.Combine(locatorRoot, "app"), Path.Combine(developmentDirectory, GeometryLabEngineLocator.EngineFileName));
        Check(development.IsAvailable && development.Kind == GeometryLabEngineLocationKind.DevelopmentOverride,
            "explicit development environment path is used only when packaged engine is unavailable");

        File.Delete(Path.Combine(developmentDirectory, "tbb12.dll"));
        GeometryLabEngineLocation missingRuntime = GeometryLabEngineLocator.Locate(
            Path.Combine(locatorRoot, "app"), Path.Combine(developmentDirectory, GeometryLabEngineLocator.EngineFileName));
        Check(!missingRuntime.IsAvailable && missingRuntime.Detail?.Contains("tbb12.dll", StringComparison.Ordinal) == true,
            "missing required runtime DLL yields a structured safe locator failure");

        GeometryLabEngineLocation missingEngine = GeometryLabEngineLocator.Locate(Path.Combine(locatorRoot, "empty"), null);
        Check(!missingEngine.IsAvailable && missingEngine.ExpectedPackagedPath?.EndsWith(
            "GeometryEngine" + Path.DirectorySeparatorChar + GeometryLabEngineLocator.EngineFileName, StringComparison.Ordinal) == true,
            "missing packaged engine reports only its deterministic app-local expected path");
    }

    private static void CreateEngineInstallation(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, GeometryLabEngineLocator.EngineFileName), "test engine");
        foreach (string runtimeFile in GeometryLabEngineLocator.RequiredRuntimeFileNames)
            File.WriteAllText(Path.Combine(directory, runtimeFile), "test runtime");
    }

    private static async Task RealEngineAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? step = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_STEP");
        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(step) || !File.Exists(engine) || !File.Exists(step))
        {
            Console.WriteLine("REAL_ENGINE: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_STEP unavailable.");
            return;
        }

        GeometryLabProcessAdapterResult result = await Adapter(engine, TimeSpan.FromMinutes(1)).AnalyzeAsync(step);
        Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded, "real engine succeeds through adapter");
        Check(GeometryLabProcessAdapter.IsSupportedSchemaVersion(result.Analysis?.SchemaVersion),
            "real engine JSON schema is accepted");
        Check(result.Analysis?.ProfileRecognition?.ProfileType == "SquareHollowSection",
            "real engine profile transport preserves profile type");
        Check(result.Analysis?.ProfileRecognition?.LengthSummary?.Classification is "Uniform" or "VariableByCut",
            "real engine profile transport preserves length summary");
        Check(result.TemporaryDirectoryCleaned, "real engine temporary directory is cleaned");
        string? expectedLength = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_EXPECTED_AXIS_LENGTH");
        if (!string.IsNullOrWhiteSpace(expectedLength))
        {
            var item = new GeometryLabStepProfileListItem { SourceStepPath = step };
            item.Apply(result);
            Check(item.LengthDisplay == expectedLength &&
                  (item.TopologyDisplay == "—" || item.TopologyDisplay.StartsWith("Kısa ", StringComparison.Ordinal)),
                "real engine presentation separates physical axis length from topology summary");
        }
    }

    // Macria crashed on close (TKXSBase access violation) when the last 3D
    // view freed the viewer DLL: OCCT's static destructors ran after TKDESTEP
    // was unmapped. The DLL is now pinned; freeing it as the old per-view
    // Dispose did must leave the DLL and OCCT loaded.
    private static void RealViewerDllPinned()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? folder = Ortam("MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR");
        string? step = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.stp").Concat(Directory.GetFiles(folder, "*.step")).FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(engine) || !File.Exists(engine) || step == null)
        {
            Console.WriteLine("REAL_VIEWER_PIN: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR unavailable.");
            return;
        }

        string directory = Path.GetDirectoryName(engine)!;
        Check(OcctViewerNative.TryLoad(directory, out OcctViewerNative? native, out string error) && native != null,
            "packaged viewer DLL loads: " + error);
        Check(native!.SupportsPreload && native.PreloadStep(step, out error), "viewer reads a real STEP: " + error);
        native.ReleaseModels();
        native.Dispose();

        IntPtr viewer = NativeModules.GetModuleHandle("Macria.GeometryViewer.dll");
        for (int index = 0; index < 8 && viewer != IntPtr.Zero; ++index)
            NativeModules.FreeLibrary(viewer);
        Check(NativeModules.GetModuleHandle("Macria.GeometryViewer.dll") != IntPtr.Zero,
            "viewer DLL stays loaded after Dispose and FreeLibrary (pinned)");
        Check(NativeModules.GetModuleHandle("TKXSBase.dll") != IntPtr.Zero && NativeModules.GetModuleHandle("TKDESTEP.dll") != IntPtr.Zero,
            "OCCT STEP modules stay loaded, so their static destructors run only at process exit");
    }

    // A .macria project rebuilds results from the stored analysis.json through
    // the same parsing as a live analysis.
    private static void StoredJsonResultTests()
    {
        const string json = "{\"schemaVersion\":\"1.2\",\"status\":\"Succeeded\",\"errors\":[],\"warnings\":[],\"profileRecognitions\":[]," +
                            "\"parts\":[{\"localId\":3,\"name\":\"P3\",\"quantity\":2,\"dxfFile\":\"part-3.dxf\"}]}";
        string dxf = Path.Combine(_root, "stored-dxf");
        GeometryLabProcessAdapterResult result = GeometryLabProcessAdapter.SonucuJsondanKur(json, dxf);
        Check(result.IsSuccess && result.AnalysisJson == json, "stored analysis.json yields a success result that keeps the raw JSON");
        Check(result.Analysis?.Parts.Count == 1 && result.PartDxfPath(result.Analysis.Parts[0]) == Path.Combine(dxf, "part-3.dxf"),
            "stored result resolves part DXFs in the given folder");
        Check(GeometryLabProcessAdapter.SonucuJsondanKur("{\"schemaVersion\":\"9.0\"}", null).Status ==
              GeometryLabProcessAdapterStatus.UnsupportedSchema, "stored JSON with an unknown engine schema is rejected");
        Check(GeometryLabProcessAdapter.SonucuJsondanKur("{bozuk", null).Status == GeometryLabProcessAdapterStatus.InvalidJson,
            "stored JSON that does not parse is InvalidJson");
    }

    private static void MotorKimligiTests()
    {
        string folder = Path.Combine(_root, "motor-kimligi");
        Directory.CreateDirectory(folder);
        string exe = Path.Combine(folder, GeometryLabEngineLocator.EngineFileName);
        File.WriteAllText(exe, "motor A");
        GeometryLabMotorKimligi bilinmeyen = GeometryLabMotorKimligi.Oku(exe);
        Check(bilinmeyen.Surum == null && bilinmeyen.Sha256.Length == 64, "engine without motor-surumu.txt has only a hash");

        File.WriteAllText(Path.Combine(folder, GeometryLabMotorKimligi.SurumDosyasi), "# yorum\n2026.10.3.1\nabc1234\n");
        File.SetLastWriteTimeUtc(exe, DateTime.UtcNow.AddMinutes(1)); // invalidates the cached identity
        GeometryLabMotorKimligi yeni = GeometryLabMotorKimligi.Oku(exe);
        Check(yeni.Surum == "2026.10.3.1" && yeni.Commit == "abc1234", "motor-surumu.txt gives version and commit");

        var eski = new GeometryLabMotorKimligi { Surum = "2026.10.2.1", Sha256 = "X" };
        var dahaYeni = new GeometryLabMotorKimligi { Surum = "2026.10.4.1", Sha256 = "Y" };
        Check(yeni.OncekindenFarkli(eski), "a newer engine version asks for a rescan");
        Check(!yeni.OncekindenFarkli(dahaYeni), "an older installed engine does not ask");
        Check(!yeni.OncekindenFarkli(yeni with { }), "the same engine does not ask");
        Check(yeni.OncekindenFarkli(yeni with { Sha256 = "BASKA" }), "the same version with a different exe asks");
        Check(yeni.OncekindenFarkli(null), "an analysis without engine identity asks");
    }

    private static void MacriaProjeTests()
    {
        string klasor = Path.Combine(_root, "proje");
        Directory.CreateDirectory(klasor);
        string step = Path.Combine(klasor, "Montaj A.stp");
        File.WriteAllText(step, "ISO-10303-21; sahte");
        string dxfKlasoru = Path.Combine(_root, "proje-dxf");
        Directory.CreateDirectory(dxfKlasoru);
        File.WriteAllText(Path.Combine(dxfKlasoru, "part-3.dxf"), "0\nSECTION\n");
        File.WriteAllText(Path.Combine(dxfKlasoru, "part-3-kesim.dxf"), "0\nEOF\n");
        const string json = "{\"schemaVersion\":\"1.2\",\"parts\":[{\"localId\":3,\"name\":\"P3\"}],\"çok\":\"ğüşıöç\"}";
        var motor = new GeometryLabMotorKimligi { Surum = "2026.10.3.1", Commit = "abc", Sha256 = "AA" };

        MacriaProjeVerisi Veri() => new()
        {
            Ayarlar = new MacriaProjeAyarlari { LazerAzamiKalinlikMm = 12.5, BukumBilgisiDxf = false, GrupGecisUyarisiKapali = true },
            Kaynaklar =
            {
                new MacriaProjeKaynagi
                {
                    Id = "k1", Yol = step, Sha256 = GeometryLabMotorKimligi.DosyaSha256(step), Boyut = new FileInfo(step).Length,
                    Analiz = new MacriaProjeAnalizi
                    {
                        Durum = nameof(GeometryLabProcessAdapterStatus.Succeeded), MotorSemaSurumu = "1.2",
                        Motor = MacriaProjeMotoru.Kimliktan(motor), Montaj = true
                    }
                }
            },
            Kararlar =
            {
                new MacriaProjeKarari { Kaynak = "k1", Hedef = MacriaProje.HedefMontaj, Karar = MacriaProje.KararSacOnayla,
                    Parca = new MacriaParcaKimligi { LocalId = 3, Ad = "P3", ProductId = "U3" } },
                new MacriaProjeKarari { Kaynak = "k1", Hedef = MacriaProje.HedefProfil, Karar = MacriaProje.KararListeDisi,
                    Not = "kaynak parçası", Parca = new MacriaParcaKimligi { LocalId = 4, Ad = "P4" } }
            }
        };
        var icerik = new Dictionary<string, MacriaProjeKaynakIcerigi> { ["k1"] = new(json, dxfKlasoru) };

        string proje = MacriaProje.VarsayilanYol(step);
        Check(proje == Path.Combine(klasor, "Montaj A.macria"), "default project path is next to the STEP with its name");
        MacriaProje.Kaydet(proje, Veri(), icerik, "Macria test");
        MacriaProje.Kaydet(proje, Veri(), icerik, "Macria test"); // over an existing file
        MacriaProjeAcilisi acilis = MacriaProje.Ac(proje, Path.Combine(_root, "acilan-dxf"));
        Check(acilis.SaltOkunurNedeni == null && acilis.Manifest.SchemaVersion == MacriaProje.SemaSurumu,
            "a saved project opens writable with the current schema");
        Check(acilis.AnalysisJson.TryGetValue("k1", out string? okunanJson) && okunanJson == json,
            "the engine's analysis.json comes back byte for byte");
        Check(acilis.DxfKlasoru.TryGetValue("k1", out string? acilanDxf) &&
              File.ReadAllText(Path.Combine(acilanDxf, "part-3.dxf")) == "0\nSECTION\n" &&
              File.Exists(Path.Combine(acilanDxf, "part-3-kesim.dxf")),
            "part DXFs are extracted into the source's folder");
        MacriaProjeVerisi okunan = acilis.Veri;
        Check(okunan.Ayarlar.LazerAzamiKalinlikMm == 12.5 && !okunan.Ayarlar.BukumBilgisiDxf && okunan.Ayarlar.GrupGecisUyarisiKapali,
            "project settings round-trip, the Lazer / Şalama warning switch included");
        Check(okunan.Kararlar.Count == 2 && okunan.Kararlar[1].Not == "kaynak parçası" &&
              okunan.Kararlar[0].Parca?.ProductId == "U3", "decisions round-trip with note and part identity");
        Check(okunan.Kaynaklar[0].GoreliYol == "Montaj A.stp", "the STEP path relative to the project is stored");
        Check(!File.Exists(proje + ".tmp"), "no temporary file is left after saving");

        // Source checks.
        MacriaProjeKaynagi kaynak = okunan.Kaynaklar[0];
        Check(MacriaProje.Denetle(kaynak, proje, true, motor).Durum == MacriaKaynakDurumu.Ayni,
            "unchanged STEP and same engine need no analysis");
        Check(MacriaProje.Denetle(kaynak, proje, true, motor with { Surum = "2026.10.4.1", Sha256 = "BB" }).Durum ==
              MacriaKaynakDurumu.MotorYeni, "a newer installed engine is reported");
        Check(MacriaProje.Denetle(kaynak, proje, false, motor).Durum == MacriaKaynakDurumu.Degismis,
            "a successful source without stored output must be rescanned");
        string tasinan = Path.Combine(_root, "tasinan");
        Directory.CreateDirectory(tasinan);
        File.Copy(step, Path.Combine(tasinan, "Montaj A.stp"));
        File.Copy(proje, Path.Combine(tasinan, "Montaj A.macria"));
        MacriaKaynakDenetimi goreli = MacriaProje.Denetle(new MacriaProjeKaynagi
        {
            Id = kaynak.Id, Yol = Path.Combine(klasor, "yok", "Montaj A.stp"), GoreliYol = kaynak.GoreliYol,
            Sha256 = kaynak.Sha256, Analiz = kaynak.Analiz
        }, Path.Combine(tasinan, "Montaj A.macria"), true, motor);
        Check(goreli.Durum == MacriaKaynakDurumu.Ayni, "a moved folder is found through the relative path");
        File.AppendAllText(step, " degisti");
        Check(MacriaProje.Denetle(kaynak, proje, true, motor).Durum == MacriaKaynakDurumu.Degismis, "a changed STEP is detected by SHA-256");
        kaynak.Yol = Path.Combine(klasor, "yok.stp");
        kaynak.GoreliYol = "yok.stp";
        Check(MacriaProje.Denetle(kaynak, proje, true, motor).Durum == MacriaKaynakDurumu.Bulunamadi, "a missing STEP is reported");

        // Schema versions and unsafe archives.
        Check(MacriaProje.SemaDenetle("1.9") != null, "a newer minor schema opens read-only");
        Check(Throws(() => MacriaProje.SemaDenetle("2.0")), "a newer major schema is refused");
        string kotu = Path.Combine(_root, "kotu.macria");
        using (var zip = System.IO.Compression.ZipFile.Open(kotu, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var yazici = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                yazici.Write("{\"format\":\"macria-proje\",\"schemaVersion\":\"1.0\"}");
            using (var yazici = new StreamWriter(zip.CreateEntry("../disari.txt").Open()))
                yazici.Write("x");
        }
        Check(Throws(() => MacriaProje.Ac(kotu, Path.Combine(_root, "kotu-dxf"))) &&
              !File.Exists(Path.Combine(_root, "disari.txt")), "a ZIP entry escaping the folder is refused (zip slip)");
        string projeDegil = Path.Combine(_root, "rastgele.macria");
        File.WriteAllText(projeDegil, "zip değil");
        Check(Throws(() => MacriaProje.Ac(projeDegil, _root)), "a file that is not a ZIP is refused");

        // Decision mapping.
        object satir3 = new(), satir4 = new(), satir5 = new();
        var adaylar = new List<MacriaKararAdayi>
        {
            new("k1", MacriaProje.HedefMontaj, new MacriaParcaKimligi { LocalId = 3, Ad = "P3", ProductId = "U3" }, satir3),
            new("k1", MacriaProje.HedefProfil, new MacriaParcaKimligi { LocalId = 4, Ad = "P4" }, satir4),
            new("k1", MacriaProje.HedefProfil, new MacriaParcaKimligi { LocalId = 5, Ad = "P4" }, satir5)
        };
        var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(Veri().Kararlar, adaylar, _ => false);
        Check(eslenen.Count == 2 && eslenen[0].Aday.Satir == satir3 && eslenen[1].Aday.Satir == satir4 && eslenemeyen.Count == 0,
            "same engine output: decisions go to the rows with the same localId");
        var yenidenAdaylar = new List<MacriaKararAdayi>
        {
            new("k1", MacriaProje.HedefMontaj, new MacriaParcaKimligi { LocalId = 30, Ad = "P3 yeni ad", ProductId = "U3" }, satir3),
            adaylar[1] with { Parca = new MacriaParcaKimligi { LocalId = 40, Ad = "P4" } }, adaylar[2]
        };
        (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(Veri().Kararlar, yenidenAdaylar, _ => true);
        Check(eslenen.Count == 1 && eslenen[0].Aday.Satir == satir3 && eslenemeyen.Count == 1 &&
              eslenemeyen[0].Karar == MacriaProje.KararListeDisi,
            "after a rescan: productId matches, two parts with the same name stay unmatched");
        var adiFarkli = new List<MacriaKararAdayi> { adaylar[0] with { Parca = adaylar[0].Parca! with { Ad = "Baska" } } };
        Check(MacriaProje.KararlariEsle(Veri().Kararlar.Take(1), adiFarkli, _ => false).Eslenemeyen.Count == 1,
            "same localId with a different name is not applied");
    }

    // Real engine: analyse, decide, save, open again without the engine and
    // compare every row. STEPs: the assembly folder's STEP, plus
    // MACRIA_PROJE_STEPS (separated by ';'), e.g. WGRV004423.
    private static async Task RealProjectRoundTripAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? folder = Ortam("MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR");
        var steps = new List<string>();
        if (Directory.Exists(folder)) steps.AddRange(Directory.GetFiles(folder, "*.stp"));
        steps.AddRange((Environment.GetEnvironmentVariable("MACRIA_PROJE_STEPS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.IsNullOrWhiteSpace(engine) || !File.Exists(engine) || steps.Count == 0)
        {
            Console.WriteLine("REAL_PROJECT: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR unavailable.");
            return;
        }

        GeometryLabMotorKimligi motor = GeometryLabMotorKimligi.Oku(engine);
        foreach (string step in steps)
        {
            Check(File.Exists(step), "project round-trip STEP exists: " + step);
            string dxfRoot = Path.Combine(_root, "proje-gercek-dxf", Guid.NewGuid().ToString("N"));
            var sure = System.Diagnostics.Stopwatch.StartNew();
            GeometryLabProcessAdapterResult result = await new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
            {
                EngineExecutablePath = engine,
                Timeout = TimeSpan.Zero,
                TemporaryRootDirectory = Path.Combine(_root, "work"),
                PartDxfRootDirectory = dxfRoot
            }).AnalyzeAsync(step);
            double analizSn = sure.Elapsed.TotalSeconds;
            Check(result.IsSuccess && result.AnalysisJson != null, "real analysis succeeds and keeps its JSON: " + Path.GetFileName(step));

            // Older engine outputs have no code: the derived code and evidence
            // must equal what the engine writes, for every part.
            int kodFarki = 0;
            foreach (GeometryLabPartTransport parca in result.Analysis!.Parts)
            {
                var motorun = MotorSinifKodu.Belirle(result.Analysis, parca);
                var turetilen = MotorSinifKodu.Belirle(result.Analysis, parca with { ClassificationCode = null, RecognitionEvidence = null });
                if (string.IsNullOrEmpty(parca.ClassificationCode) || motorun != turetilen)
                {
                    ++kodFarki;
                    Console.WriteLine($"REAL_CODE: {parca.Name}: motor {motorun} / türetilen {turetilen}");
                }
            }
            Check(kodFarki == 0, "codes derived from reason texts equal the engine's codes: " + Path.GetFileName(step));

            var (profil, montaj) = MacriaProjeSatirlari.Kur(step, result, 20);
            // Decisions of every kind the rows offer.
            profil.FirstOrDefault()?.ExcludeFromList("proje testi");
            profil.Skip(1).FirstOrDefault()?.MoveToReview();
            montaj.FirstOrDefault(x => x.CanApproveAsSheet)?.ApproveAsSheet();
            montaj.FirstOrDefault(x => !x.CanApproveAsSheet)?.MoveToReview();
            Func<string, string?> kaynakId = path => path == step ? "k1" : null;
            List<MacriaProjeKarari> kararlar = MacriaProjeSatirlari.KararlariTopla(profil, montaj, kaynakId);
            Check(kararlar.Count >= 1, "decisions are collected from the rows");

            var veri = new MacriaProjeVerisi
            {
                Kaynaklar =
                {
                    new MacriaProjeKaynagi
                    {
                        Id = "k1", Yol = step, Sha256 = GeometryLabMotorKimligi.DosyaSha256(step), Boyut = new FileInfo(step).Length,
                        Analiz = new MacriaProjeAnalizi
                        {
                            Durum = result.Status.ToString(), MotorSemaSurumu = result.Analysis!.SchemaVersion,
                            Motor = MacriaProjeMotoru.Kimliktan(motor), SureSn = analizSn, Montaj = montaj.Count > 0
                        }
                    }
                },
                Kararlar = kararlar
            };
            string proje = Path.Combine(_root, Path.GetFileNameWithoutExtension(step) + MacriaProje.Uzanti);
            sure.Restart();
            MacriaProje.Kaydet(proje, veri,
                new Dictionary<string, MacriaProjeKaynakIcerigi> { ["k1"] = new(result.AnalysisJson, result.PartDxfDirectory) },
                "Macria test");
            double kaydetSn = sure.Elapsed.TotalSeconds;

            // "Close": nothing of the session is used below except the file.
            sure.Restart();
            MacriaProjeAcilisi acilis = MacriaProje.Ac(proje, Path.Combine(_root, "proje-acilis-dxf", Guid.NewGuid().ToString("N")));
            MacriaProjeKaynagi kaynak = acilis.Veri.Kaynaklar[0];
            MacriaKaynakDenetimi denetim = MacriaProje.Denetle(kaynak, proje, acilis.AnalysisJson.ContainsKey("k1"), motor);
            Check(denetim.Durum == MacriaKaynakDurumu.Ayni, "unchanged STEP needs no analysis: " + denetim.Aciklama);
            GeometryLabProcessAdapterResult kayitli = GeometryLabProcessAdapter.SonucuJsondanKur(
                acilis.AnalysisJson["k1"], acilis.DxfKlasoru.GetValueOrDefault("k1"));
            var (profil2, montaj2) = MacriaProjeSatirlari.Kur(denetim.BulunanYol!, kayitli, acilis.Veri.Ayarlar.LazerAzamiKalinlikMm);
            var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(acilis.Veri.Kararlar,
                MacriaProjeSatirlari.Adaylar(profil2, montaj2, kaynakId), _ => false);
            int uygulanan = eslenen.Count(x => MacriaProjeSatirlari.Uygula(x.Karar, x.Aday.Satir));
            double acSn = sure.Elapsed.TotalSeconds;

            Check(eslenemeyen.Count == 0 && uygulanan == kararlar.Count, "every decision returns to its row");
            Check(Satirlar(profil, montaj).SequenceEqual(Satirlar(profil2, montaj2)), "opened project shows the same rows as the analysis");
            Check(montaj2.Where(x => x.DxfSourcePath != null).All(x => File.Exists(x.DxfSourcePath)),
                "engine DXFs of the opened project exist");
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"REAL_PROJECT: {Path.GetFileName(step)} parça={result.Analysis.Parts.Count} satır={profil.Count + montaj.Count} karar={kararlar.Count} analiz={analizSn:0.0}s kaydet={kaydetSn:0.00}s aç(hash+satır+karar)={acSn:0.00}s proje={new FileInfo(proje).Length / 1024.0:0}KB"));
        }
    }

    // Repository root: the folder holding Macria.slnx above the test output.
    private static string RepoKoku()
    {
        for (DirectoryInfo? klasor = new(AppContext.BaseDirectory); klasor != null; klasor = klasor.Parent)
            if (File.Exists(Path.Combine(klasor.FullName, "Macria.slnx")))
                return klasor.FullName;
        throw new InvalidOperationException("Macria.slnx bulunamadı: " + AppContext.BaseDirectory);
    }

    // THIRD_PARTY_NOTICES.txt and the Hakkında window tell the same source
    // code location and carry the notices the licenses ask for.
    private static void UcuncuTarafBildirimleriTests()
    {
        string macria = Path.Combine(RepoKoku(), "Macria");
        string? dosya = UcuncuTarafBildirimleri.BildirimDosyasi(macria);
        Check(dosya != null, "THIRD_PARTY_NOTICES.txt is in the Macria project folder");
        string metin = File.ReadAllText(dosya!);
        Check(metin.Contains(UcuncuTarafBildirimleri.KaynakKoduYeri) &&
              UcuncuTarafBildirimleri.KaynakKoduMetni.Contains(UcuncuTarafBildirimleri.KaynakKoduYeri),
            "the notices file and the Hakkında window name the same source code location");
        string tekSatir = metin.Replace("\r", "").Replace("\n", " ");
        foreach (string bildirim in new[]
                 {
                     "make use of facilities provided by the Open CASCADE Technology software",
                     "libraries from the FFmpeg project under the LGPLv2.1",
                     "FreeImage is used under the FIPL",
                     "work of the Independent JPEG Group",
                     "The FreeType Project"
                 })
            Check(tekSatir.Contains(bildirim), "required notice: " + bildirim);
        Check(UcuncuTarafBildirimleri.OcctBildirimi.Contains("Open CASCADE Technology"),
            "the Hakkında window shows the OCCT notice");
        Check(UcuncuTarafBildirimleri.BildirimDosyasi(Path.Combine(macria, "yok-boyle-klasor")) == null,
            "a missing notices file is reported as null");
    }

    // GeometryEngineRuntime/README.md lisans kuralı: every third-party file in
    // GeometryEngineRuntime is named in licenses/00-DIZIN.txt and in
    // THIRD_PARTY_NOTICES.txt; the OCCT DLL count matches what both say.
    private static void RuntimeLisanslariTests()
    {
        string kok = RepoKoku();
        string runtime = Path.Combine(kok, "GeometryEngineRuntime");
        string dizin = File.ReadAllText(Path.Combine(runtime, "licenses", "00-DIZIN.txt"));
        string bildirim = File.ReadAllText(Path.Combine(kok, "Macria", UcuncuTarafBildirimleri.DosyaAdi));
        string[] dosyalar = Directory.GetFiles(runtime, "*.dll").Concat(Directory.GetFiles(runtime, "*.exe"))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(ad => !ad.StartsWith("Macria.", StringComparison.OrdinalIgnoreCase)).ToArray();
        int occt = dosyalar.Count(ad => ad.StartsWith("TK", StringComparison.Ordinal));
        Check(occt > 0 && dizin.Contains("TK*.dll (" + occt + " dosya)") && bildirim.Contains("TK*.dll (" + occt + " dosya)"),
            "both license indexes name the " + occt + " OCCT DLLs");
        foreach (string ad in dosyalar.Where(ad => !ad.StartsWith("TK", StringComparison.Ordinal)))
            Check(dizin.Contains(ad, StringComparison.OrdinalIgnoreCase) && bildirim.Contains(ad, StringComparison.OrdinalIgnoreCase),
                "third-party file " + ad + " is named in licenses/00-DIZIN.txt and THIRD_PARTY_NOTICES.txt");
        foreach (string lisans in new[] { "LGPL-2.1.txt", "OCCT-LGPL-EXCEPTION.txt", "OCCT.txt", "FFmpeg.txt", "FreeImage.txt",
                     "FreeImage-FIPL-1.0.txt", "FreeType-FTL.txt", "oneTBB-Apache-2.0.txt", "jemalloc-BSD-2.txt", "OpenVR-BSD-3.txt" })
            Check(File.Exists(Path.Combine(runtime, "licenses", lisans)) && bildirim.Contains(lisans),
                "license file " + lisans + " exists and THIRD_PARTY_NOTICES.txt points to it");
        foreach (string lisans in Directory.GetFiles(Path.Combine(kok, "Macria", "licenses")).Select(Path.GetFileName))
            Check(bildirim.Contains(lisans!), "THIRD_PARTY_NOTICES.txt points to licenses/" + lisans);
    }

    private static void SekmeKurallariTests()
    {
        // Engine code + evidence -> tab (docs/SEKME_VE_ARAC_CUBUGU_PLANI.md).
        (string Kod, bool Kanit, AnalizSekmesi Sekme)[] tablo =
        {
            (MotorSinifKodu.Sheet, true, AnalizSekmesi.Saclar),
            (MotorSinifKodu.HollowProfile, true, AnalizSekmesi.Profiller),
            (MotorSinifKodu.ProcessedProfile, true, AnalizSekmesi.Profiller),
            (MotorSinifKodu.SheetProfileConflict, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.FlatPatternFailed, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.MultiSolid, false, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.SolidBar, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.ThickerThanOutline, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.ThickerThanMaterial, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.UnsupportedFaces, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.UnsupportedFaces, false, AnalizSekmesi.Tanimsiz),
            (MotorSinifKodu.SheetAnalysisIncomplete, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.SheetAnalysisIncomplete, false, AnalizSekmesi.Tanimsiz),
            (MotorSinifKodu.NotRecognized, true, AnalizSekmesi.KontrolGerekli),
            (MotorSinifKodu.NotRecognized, false, AnalizSekmesi.Tanimsiz),
            (MotorSinifKodu.InvalidGeometry, false, AnalizSekmesi.Tanimsiz),
            (MotorSinifKodu.TimedOut, false, AnalizSekmesi.Tanimsiz),
            (MotorSinifKodu.NoSolid, false, AnalizSekmesi.Tanimsiz),
            ("Bilinmeyen", false, AnalizSekmesi.KontrolGerekli)
        };
        foreach (var (kod, kanit, sekme) in tablo)
            Check(SekmeKurallari.OtomatikSekme(kod, kanit) == sekme, $"{kod} (kanıt {kanit}) -> {sekme}");
        Check(SekmeKurallari.Diger(MotorSinifKodu.ThickerThanMaterial) && !SekmeKurallari.Diger(MotorSinifKodu.NotRecognized),
            "solid bars and too-thick parts are Diğer; unrecognized parts are not");
        Check(MotorSinifKodu.KodCikar("ReviewRequired", new[] { "Tanıyıcıların desteklemediği yüz tipleri var (BSpline): x" }) ==
              MotorSinifKodu.UnsupportedFaces &&
              MotorSinifKodu.KodCikar("Other", new[] { "Sac kabuğu bulundu ama kalınlık (30 mm) parçanın gerçek et genişliğinden (6 mm) büyük", "Sac veya profil olarak tanınmadı." }) ==
              MotorSinifKodu.ThickerThanMaterial,
            "codes are derived from the engine's reason texts");

        // Toolbar: what each tab offers.
        var profilSatiri = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\a.stp" };
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        MontajParcaSatiri sac = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], "C:\\x.dxf", 20);
        IAnalizSatiri[] hicbiri = Array.Empty<IAnalizSatiri>();
        AnalizAracDurumu profiller = AnalizAracDurumu.Hesapla(AnalizSekmesi.Profiller, new IAnalizSatiri[] { profilSatiri }, true, false);
        Check(!profiller.ProfilOnaylaGorunur && !profiller.SacOnaylaGorunur && profiller.KontroleGorunur && profiller.ListeDisiGorunur &&
              profiller.OtomatikGorunur && !profiller.GeriAlGorunur && !profiller.DxfUretGorunur && profiller.DosyayiAcEtkin,
            "Profiller: Kontrol Gerekliye Al, Liste Dışına Çıkar, Otomatik Karara Dön");
        AnalizAracDurumu saclar = AnalizAracDurumu.Hesapla(AnalizSekmesi.Saclar, new IAnalizSatiri[] { sac }, true, true);
        Check(saclar.SacOnaylaGorunur && saclar.SacOnaylaEtkin && saclar.DxfUretGorunur && saclar.DxfUretEtkin &&
              !saclar.ProfilOnaylaGorunur && saclar.KontroleGorunur, "Saclar: Sac Olarak Onayla (onay bekleyen), DXF Üret");
        sac.ApproveAsSheet();
        Check(!AnalizAracDurumu.Hesapla(AnalizSekmesi.Saclar, new IAnalizSatiri[] { sac }, true, true).SacOnaylaEtkin,
            "an approved sheet cannot be approved again");
        AnalizAracDurumu kontrol = AnalizAracDurumu.Hesapla(AnalizSekmesi.KontrolGerekli,
            new IAnalizSatiri[] { profilSatiri, sac }, true, false);
        Check(kontrol.ProfilOnaylaGorunur && kontrol.ProfilOnaylaEtkin && kontrol.SacOnaylaGorunur && kontrol.SacOnaylaEtkin &&
              !kontrol.KontroleGorunur && !kontrol.DxfUretGorunur && !kontrol.DosyayiAcEtkin,
            "Kontrol gerekli: Profil / Sac Olarak Onayla; no Kontrol Gerekliye Al");
        AnalizAracDurumu tanimsiz = AnalizAracDurumu.Hesapla(AnalizSekmesi.Tanimsiz, hicbiri, false, false);
        Check(tanimsiz.ProfilOnaylaGorunur && tanimsiz.SacOnaylaGorunur && tanimsiz.KontroleGorunur && !tanimsiz.ListeDisiEtkin &&
              !tanimsiz.ExcelEtkin, "Tanımsız: every decision, nothing enabled without a selection");
        AnalizAracDurumu listeDisi = AnalizAracDurumu.Hesapla(AnalizSekmesi.ListeDisi, new IAnalizSatiri[] { sac }, true, false);
        Check(listeDisi.GeriAlGorunur && listeDisi.GeriAlEtkin && !listeDisi.ListeDisiGorunur && !listeDisi.OtomatikGorunur &&
              !listeDisi.SacOnaylaGorunur && !listeDisi.ProfilOnaylaGorunur && !listeDisi.KontroleGorunur,
            "Liste dışı: only Geri Al");

        // Schema 1.0 projects: Incelemeye -> Kontrol gerekli, profile ListeDisi -> flag,
        // a file-row decision (parca null) -> the source's single part.
        var tekParca = new GeometryLabStepProfileListItem
        {
            SourceStepPath = "C:\\t.stp", PartName = "T", PartQuantity = 1, PartLocalId = 1
        };
        tekParca.MarkAnalyzing();
        var adaylar = new List<MacriaKararAdayi>
        {
            new("k1", MacriaProje.HedefProfil, new MacriaParcaKimligi { LocalId = 1, Ad = "T" }, tekParca),
            new("k2", MacriaProje.HedefMontaj, new MacriaParcaKimligi { LocalId = 7, Ad = "S" }, sac)
        };
        var eski = new[]
        {
            new MacriaProjeKarari { Kaynak = "k1", Parca = null, Hedef = MacriaProje.HedefProfil, Karar = MacriaProje.KararIncelemeye },
            new MacriaProjeKarari { Kaynak = "k1", Parca = null, Hedef = MacriaProje.HedefProfil, Karar = MacriaProje.KararListeDisi, Not = "eski" },
            new MacriaProjeKarari { Kaynak = "k2", Parca = null, Hedef = MacriaProje.HedefProfil, Karar = MacriaProje.KararListeDisi }
        };
        var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(eski, adaylar, _ => false);
        Check(eslenen.Count == 3 && eslenemeyen.Count == 0, "1.0 file-row decisions find the single part of their source");
        Check(eslenen.All(x => MacriaProjeSatirlari.Uygula(x.Karar, x.Aday.Satir)), "1.0 decisions apply");
        Check(tekParca.Sekme == AnalizSekmesi.ListeDisi && tekParca.ListeDisiNotu == "eski" &&
              tekParca.KullaniciKarari == MacriaProje.KararKontrole,
            "Incelemeye becomes Kontrol gerekli, ListeDisi the flag over it");
        tekParca.ListeyeGeriAl();
        Check(tekParca.Sekme == AnalizSekmesi.KontrolGerekli, "Geri Al returns it to Kontrol gerekli");
        Check(sac.Sekme == AnalizSekmesi.ListeDisi, "a 1.0 file-row exclusion reaches a sheet part too");
        List<MacriaProjeKarari> toplanan = MacriaProjeSatirlari.KararlariTopla(new[] { tekParca }, new[] { sac },
            yol => yol == "C:\\t.stp" ? "k1" : "k2");
        Check(toplanan.Count(x => x.Karar == MacriaProje.KararListeDisi) == 1 &&
              toplanan.Any(x => x.Karar == MacriaProje.KararKontrole && x.Hedef == MacriaProje.HedefProfil) &&
              toplanan.Any(x => x.Karar == MacriaProje.KararSacOnayla),
            "1.1 writes the category decision and the Liste dışı flag apart");
    }

    // Every single-part STEP under MACRIA_TEK_PARCA_KOK: its profile row built
    // from the file (old path) and from its part (schema 1.1 path) must show
    // the same profile.
    private static async Task RealSinglePartPathAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? kok = Ortam("MACRIA_TEK_PARCA_KOK");
        if (string.IsNullOrWhiteSpace(engine) || !File.Exists(engine) || string.IsNullOrWhiteSpace(kok) || !Directory.Exists(kok))
        {
            Console.WriteLine("REAL_SINGLE_PART: SKIPPED - MACRIA_TEK_PARCA_KOK unavailable.");
            return;
        }
        int dosya = 0, profil = 0, digerSekme = 0;
        foreach (string step in Directory.GetFiles(kok, "*.stp", SearchOption.AllDirectories)
                     .Concat(Directory.GetFiles(kok, "*.step", SearchOption.AllDirectories)).OrderBy(x => x))
        {
            GeometryLabProcessAdapterResult result = await Adapter(engine, TimeSpan.Zero).AnalyzeAsync(step);
            if (!result.IsSuccess || result.Analysis?.Parts.Count != 1 || MontajParcaSatiri.IsAssembly(result.Analysis)) continue;
            ++dosya;
            var eskiYol = new GeometryLabStepProfileListItem { SourceStepPath = step };
            eskiYol.Apply(result);
            var (profiller, parcalar) = MacriaProjeSatirlari.Kur(step, result, 20);
            Check(profiller.Count + parcalar.Count == 1, "a single-part STEP gives one row: " + Path.GetFileName(step));
            if (profiller.Count == 1)
            {
                ++profil;
                GeometryLabStepProfileListItem yeni = profiller[0];
                string Gorunum(GeometryLabStepProfileListItem x) => string.Join("|", x.EffectiveStatusDisplay, x.EffectiveProfileTypeDisplay,
                    x.SectionDisplay, x.LengthDisplay, x.TopologyDisplay, x.CutDisplay, x.OperationDisplay, x.QuantityDisplay);
                Check(Gorunum(eskiYol) == Gorunum(yeni),
                    "single-part profile looks the same by file and by part: " + Path.GetFileName(step) + "\n  " + Gorunum(eskiYol) + "\n  " + Gorunum(yeni));
            }
            else
            {
                ++digerSekme;
                Console.WriteLine($"REAL_SINGLE_PART: {Path.GetFileName(step)} -> {parcalar[0].Sekme} ({parcalar[0].StatusDisplay}, {parcalar[0].EngineCode}); eskiden {eskiYol.EffectiveStatusDisplay}");
            }
        }
        Console.WriteLine($"REAL_SINGLE_PART: {dosya} tek parçalı STEP, {profil} profil aynı, {digerSekme} profil dışı sekmede");
    }

    // Saved schema 1.0 projects (MACRIA_ESKI_PROJELER, ';'-separated): their
    // decisions land on the new tabs.
    private static void RealOldProjectsTests()
    {
        string[] projeler = (Ortam("MACRIA_ESKI_PROJELER") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(File.Exists).ToArray();
        if (projeler.Length == 0)
        {
            Console.WriteLine("REAL_OLD_PROJECT: SKIPPED - MACRIA_ESKI_PROJELER unavailable.");
            return;
        }
        foreach (string proje in projeler)
        {
            MacriaProjeAcilisi acilis = MacriaProje.Ac(proje, Path.Combine(_root, "eski-proje", Guid.NewGuid().ToString("N")));
            var profil = new List<GeometryLabStepProfileListItem>();
            var montaj = new List<MontajParcaSatiri>();
            var kaynakIdleri = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (MacriaProjeKaynagi kaynak in acilis.Veri.Kaynaklar)
            {
                kaynakIdleri[kaynak.Yol] = kaynak.Id;
                if (!acilis.AnalysisJson.TryGetValue(kaynak.Id, out string? json)) continue;
                var (p, m) = MacriaProjeSatirlari.Kur(kaynak.Yol,
                    GeometryLabProcessAdapter.SonucuJsondanKur(json, acilis.DxfKlasoru.GetValueOrDefault(kaynak.Id)),
                    acilis.Veri.Ayarlar.LazerAzamiKalinlikMm);
                profil.AddRange(p);
                montaj.AddRange(m);
            }
            var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(acilis.Veri.Kararlar,
                MacriaProjeSatirlari.Adaylar(profil, montaj, yol => kaynakIdleri.GetValueOrDefault(yol)), _ => false);
            // A "Dene" decision (schema 1.3) replaces the part's row with its stored run;
            // it lands when its run is in the project and the run rows hold the part.
            int uygulanan = eslenen.Count(x =>
            {
                if (x.Karar.Karar != MacriaProje.KararDene) return MacriaProjeSatirlari.Uygula(x.Karar, x.Aday.Satir);
                string anahtar = MacriaProje.EkAnahtari(x.Karar.Kaynak, x.Karar.EkAnaliz ?? "");
                if (!acilis.EkAnalysisJson.TryGetValue(anahtar, out string? ekJson)) return false;
                GeometryLabProcessAdapterResult ek = GeometryLabProcessAdapter.SonucuJsondanKur(ekJson, acilis.EkDxfKlasoru.GetValueOrDefault(anahtar));
                int id = ((IAnalizSatiri)x.Aday.Satir).ParcaLocalId ?? -1;
                var (ekProfil, ekMontaj) = MacriaProjeSatirlari.EkSatirlari(((IAnalizSatiri)x.Aday.Satir).KaynakYolu, ek,
                    acilis.Veri.Ayarlar.LazerAzamiKalinlikMm, new SatirDenemesi(x.Karar.DenemeModu ?? MacriaProje.DenemeYeniden, x.Karar.EkAnaliz!));
                return ek.IsSuccess && !ek.Analysis!.Otomatik &&
                       ekProfil.Cast<IAnalizSatiri>().Concat(ekMontaj).Any(r => r.ParcaLocalId == id && r.Deneme != null);
            });
            Check(eslenemeyen.Count == 0 && uygulanan == acilis.Veri.Kararlar.Count,
                "every decision of an old project lands on a row: " + Path.GetFileName(proje) + " (" + uygulanan + " / " +
                acilis.Veri.Kararlar.Count + ", eşlenemeyen " + eslenemeyen.Count + ")");
            IEnumerable<IAnalizSatiri> hepsi = profil.Cast<IAnalizSatiri>().Concat(montaj);
            Console.WriteLine($"REAL_OLD_PROJECT: {Path.GetFileName(proje)} şema {acilis.Manifest.SchemaVersion}, {acilis.Veri.Kararlar.Count} karar uygulandı; " +
                string.Join(", ", hepsi.GroupBy(x => x.Sekme).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count())));
            foreach (var (karar, aday) in eslenen)
                Console.WriteLine($"REAL_OLD_PROJECT:    {karar.Karar} {karar.Parca?.Ad ?? "(dosya satırı)"} -> {((IAnalizSatiri)aday.Satir).Sekme}");
        }
    }

    private static IEnumerable<string> Satirlar(IEnumerable<GeometryLabStepProfileListItem> profil, IEnumerable<MontajParcaSatiri> montaj) =>
        profil.Select(x => string.Join("|", x.SourceFileName, x.EffectiveStatusDisplay, x.EffectiveProfileTypeDisplay, x.SectionDisplay,
                x.LengthDisplay, x.CutDisplay, x.DecisionSource, x.UserDecisionNote, x.KullaniciKarari))
            .Concat(montaj.Select(x => string.Join("|", x.PartName, x.Quantity, x.StatusDisplay, x.DecisionDisplay, x.GroupDisplay,
                x.ThicknessDisplay, x.HoleSummary, x.ExplanationDisplay, Path.GetFileName(x.DxfSourcePath ?? ""))));

    private static bool Throws(Action eylem)
    {
        try { eylem(); return false; }
        catch (MacriaProjeHatasi) { return true; }
    }

    private static class NativeModules
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string moduleName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern bool FreeLibrary(IntPtr module);
    }

    private static async Task RealBaseStockEngineAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? step = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_BASE_STEP");
        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(step) || !File.Exists(engine) || !File.Exists(step))
        {
            Console.WriteLine("REAL_BASE_STOCK_ENGINE: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_BASE_STEP unavailable.");
            return;
        }

        GeometryLabProcessAdapterResult result = await Adapter(engine, TimeSpan.FromMinutes(1)).AnalyzeAsync(step);
        var item = new GeometryLabStepProfileListItem { SourceStepPath = step };
        item.Apply(result);
        Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded &&
              result.Analysis?.BaseStockProfile?.Status == "Recognized",
            "real engine base-stock JSON reaches the Macria transport contract");
        Check(item.AnalysisStatus == "Tanındı" && item.Eligibility == GeometryLabProfileListEligibility.Eligible &&
              item.ProfileType == "Dikdörtgen Kutu Profil" && item.SectionDisplay == "60 × 40 × 3 mm",
            "real locally modified stock is admitted only through the conservative base-stock gate");
        Check(item.CutDisplay == "—" && item.TopologyDisplay == "—" &&
              item.RawBaseStockProfile != null && item.RawModificationAnalysis != null,
            "real base-stock row preserves evidence and does not fabricate normal topology or cut data");
        string? expectedLength = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_BASE_EXPECTED_AXIS_LENGTH");
        if (!string.IsNullOrWhiteSpace(expectedLength))
            Check(item.LengthDisplay == expectedLength,
                "real processed base-stock row displays only the validated base-axis span");
    }

    private static async Task RealNormalPriorityEngineAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? step = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_NORMAL_PRIORITY_STEP");
        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(step) || !File.Exists(engine) || !File.Exists(step))
        {
            Console.WriteLine("REAL_NORMAL_PRIORITY_ENGINE: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_NORMAL_PRIORITY_STEP unavailable.");
            return;
        }

        GeometryLabProcessAdapterResult result = await Adapter(engine, TimeSpan.FromMinutes(1)).AnalyzeAsync(step);
        var item = new GeometryLabStepProfileListItem { SourceStepPath = step };
        item.Apply(result);
        Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded && item.AnalysisStatus == "Tanındı" &&
              item.OperationDisplay == "—" && item.RawBaseStockProfile == null,
            "real definite normal profileRecognition remains ahead of the base-stock fallback");
        string? expectedLength = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_NORMAL_EXPECTED_AXIS_LENGTH");
        if (!string.IsNullOrWhiteSpace(expectedLength))
            Check(item.LengthDisplay == expectedLength,
                "real normal profile row displays the validated physical axis span");
    }

    private static async Task RealNegativeEligibilityEngineAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? paths = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_NEGATIVE_STEPS");
        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(paths) || !File.Exists(engine))
        {
            Console.WriteLine("REAL_NEGATIVE_ELIGIBILITY_ENGINE: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_NEGATIVE_STEPS unavailable.");
            return;
        }

        foreach (string step in paths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!File.Exists(step)) throw new InvalidOperationException("Configured negative STEP file is missing: " + step);
            GeometryLabProcessAdapterResult result = await Adapter(engine, TimeSpan.FromMinutes(1)).AnalyzeAsync(step);
            var item = new GeometryLabStepProfileListItem { SourceStepPath = step };
            item.Apply(result);
            Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded && !item.IsEligible,
                "real negative STEP is not admitted by normal or base-stock list eligibility: " + Path.GetFileName(step) +
                " (adapter=" + result.Status + ", item=" + item.AnalysisStatus + ", eligibility=" + item.Eligibility +
                ", detail=" + result.Message + ")");
        }
    }

    private static async Task TimeoutAsync()
    {
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("timeout", SlowEngineScript()),
            TimeSpan.FromMilliseconds(100)).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.TimedOut, "timeout kills only the child engine process");
        Check(result.TemporaryDirectoryCleaned, "timeout temporary directory is cleaned");
    }

    private static async Task NonZeroExitAsync()
    {
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("nonzero", "echo engine-error 1>&2\r\nexit /b 17")).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.EngineFailed && result.ExitCode == 17,
            "non-zero engine exit is retained in structured result");
        Check(result.StandardError.Contains("engine-error", StringComparison.Ordinal), "stderr is retained");
    }

    private static async Task MissingJsonAsync()
    {
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("missing-json", "echo stdout-test\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.JsonMissing, "missing output JSON is rejected");
        Check(result.StandardOutput.Contains("stdout-test", StringComparison.Ordinal), "stdout is retained");
    }

    private static async Task InvalidJsonAsync()
    {
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("invalid-json", "echo {>\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.InvalidJson, "invalid JSON is rejected");
    }

    private static async Task UnsupportedSchemaAsync()
    {
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("unsupported-schema",
            "echo {\"schemaVersion\":\"9.0\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"profileRecognitions\":[]}>\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.UnsupportedSchema, "unsupported schema is rejected");
    }

    private static async Task SheetMetalSchemaAsync()
    {
        const string json = "{\"schemaVersion\":\"1.1\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"solids\":[{}],\"profileRecognitions\":[],"
            + "\"sheetMetal\":{\"solidId\":{\"analysisId\":\"a\",\"localId\":1},\"status\":\"Recognized\",\"rejectionReason\":null,\"thicknessMm\":20,\"closedSection\":false,"
            + "\"bends\":[{\"localId\":1,\"innerRadiusMm\":4,\"angleDegrees\":90,\"direction\":\"Up\",\"kFactor\":0.150515,\"allowanceMm\":11.0117534}],"
            + "\"flatPattern\":{\"status\":\"Succeeded\",\"referenceSkin\":\"A\",\"kFactorFormula\":\"catia-log\",\"widthMm\":216.4187,\"heightMm\":460.7976,"
            + "\"holes\":[{\"holeFeatureId\":{\"analysisId\":\"a\",\"localId\":1},\"diameterMm\":16}],\"bendLines\":[{\"bendId\":1,\"label\":\"UP 90deg  R 4\"}],\"rejectionReason\":null}},"
            + "\"sheetMetalAnalyses\":[{\"status\":\"Recognized\"}],"
            + "\"holeFeatures\":[{\"localId\":1,\"solidId\":{\"analysisId\":\"a\",\"localId\":1},\"type\":\"Countersink\",\"status\":\"Recognized\",\"throughDiameterMm\":16,\"headDiameterMm\":40,\"openingSide\":\"A\",\"onBend\":false}]}";
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("schema-1-1", "echo " + json + ">\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded && result.Analysis?.SchemaVersion == "1.1",
            "schema 1.1 JSON is accepted");
        GeometryLabSheetMetalTransport? sheet = result.Analysis?.SheetMetal;
        Check(sheet?.Status == "Recognized" && sheet.ThicknessMm == 20 && sheet.SolidId?.LocalId == 1,
            "sheetMetal status, thickness and local solid ID are read");
        Check(sheet?.Bends.Count == 1 && sheet.Bends[0].AllowanceMm == 11.0117534 && sheet.Bends[0].Direction == "Up",
            "bend allowance and direction are read");
        Check(sheet?.FlatPattern?.Status == "Succeeded" && sheet.FlatPattern.Holes.Count == 1 &&
              sheet.FlatPattern.Holes[0].DiameterMm == 16 && sheet.FlatPattern.Holes[0].HoleFeatureId?.LocalId == 1 &&
              sheet.FlatPattern.BendLines[0].Label == "UP 90deg  R 4",
            "flat pattern holes and bend label are read");
        Check(result.Analysis?.SheetMetalAnalyses.Count == 1 && result.Analysis.HoleFeatures.Count == 1 &&
              result.Analysis.HoleFeatures[0].Type == "Countersink" && result.Analysis.HoleFeatures[0].ThroughDiameterMm == 16,
            "sheetMetalAnalyses and holeFeatures are read");

        GeometryLabProcessAdapterResult future = await Adapter(CreateEngine("schema-2-0",
            "echo {\"schemaVersion\":\"2.0\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"profileRecognitions\":[]}>\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(future.Status == GeometryLabProcessAdapterStatus.UnsupportedSchema, "schema 2.0 is rejected");

        GeometryLabAnalysisTransport? legacy = JsonSerializer.Deserialize<GeometryLabAnalysisTransport>("{\"schemaVersion\":\"1.0\",\"status\":\"Succeeded\"}");
        Check(legacy?.SheetMetal is null && legacy.SheetMetalAnalyses.Count == 0 && legacy.HoleFeatures.Count == 0,
            "schema 1.0 JSON leaves the 1.1 additions empty");
        Check(GeometryLabProcessAdapter.IsSupportedSchemaVersion("1.0") && GeometryLabProcessAdapter.IsSupportedSchemaVersion("1.1") &&
              !GeometryLabProcessAdapter.IsSupportedSchemaVersion(null) && !GeometryLabProcessAdapter.IsSupportedSchemaVersion("1.10"),
            "only the exact supported schema versions are accepted");
    }

    private static async Task PartsSchemaAsync()
    {
        const string json = "{\"schemaVersion\":\"1.2\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"solids\":[{},{}],\"profileRecognitions\":[],"
            + "\"parts\":[{\"localId\":1,\"name\":\"55RS100111-3\",\"productId\":\"55RS100111-3\",\"productName\":\"55RS100111-3\",\"quantity\":2,"
            + "\"solidIds\":[{\"analysisId\":\"a\",\"localId\":1}],\"classification\":\"Sheet\",\"classificationReasons\":[\"Sac\"],"
            + "\"sheetCandidate\":true,\"profileCandidate\":null,\"dxfFile\":\"part-1.dxf\",\"dxfCutOnlyFile\":\"part-1-kesim.dxf\"},"
            + "{\"localId\":2,\"name\":\"01-Duz-Duz\",\"quantity\":3,\"solidIds\":[{\"analysisId\":\"a\",\"localId\":2}],"
            + "\"classification\":\"Profile\",\"classificationReasons\":[],\"sheetCandidate\":false,\"profileCandidate\":\"SquareHollowSection\",\"dxfFile\":null}],"
            + "\"holeFeatures\":[{\"localId\":1,\"type\":\"Countersink\",\"status\":\"Recognized\",\"throughDiameterMm\":6.647,"
            + "\"threadDesignation\":\"M8x1.25\",\"warning\":\"threaded\"}]}";

        // Without PartDxfRootDirectory the engine gets exactly --input/--output.
        GeometryLabProcessAdapterResult plain = await Adapter(CreateEngine("parts-plain",
            "if not \"%~5\"==\"\" exit /b 3\r\necho " + json + ">\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(plain.Status == GeometryLabProcessAdapterStatus.Succeeded && plain.PartDxfDirectory is null,
            "without a DXF root no --dxf-klasor argument is passed and no DXF folder is reported");
        Check(plain.Analysis?.SchemaVersion == "1.2", "schema 1.2 JSON is accepted");
        GeometryLabPartTransport? sheet = plain.Analysis?.Parts.FirstOrDefault(part => part.Name == "55RS100111-3");
        Check(plain.Analysis?.Parts.Count == 2 && sheet is not null && sheet.Quantity == 2 && sheet.Classification == "Sheet" &&
              sheet.SheetCandidate && sheet.ProfileCandidate is null && sheet.SolidIds[0].LocalId == 1 &&
              sheet.ProductId == "55RS100111-3" && sheet.DxfFile == "part-1.dxf",
            "parts: name, product id, quantity, solid ids, class and candidates are read");
        Check(plain.Analysis?.Parts[1].ProfileCandidate == "SquareHollowSection" && plain.Analysis.Parts[1].DxfFile is null,
            "profile part keeps its profile candidate and has no DXF");
        Check(plain.Analysis?.HoleFeatures[0].ThreadDesignation == "M8x1.25" && plain.Analysis.HoleFeatures[0].Warning == "threaded",
            "thread designation and warning are read");
        Check(plain.PartDxfPath(sheet!) is null, "no DXF path without a DXF folder");

        // Part time limit and thread count are passed to the engine when set.
        var limited = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = CreateEngine("parts-limit",
                "if not \"%~5\"==\"--parca-sure-siniri\" exit /b 5\r\nif not \"%~6\"==\"45\" exit /b 6\r\n" +
                "if not \"%~7\"==\"--is-parcacigi\" exit /b 7\r\nif not \"%~8\"==\"3\" exit /b 8\r\n" +
                "echo " + json + ">\"%~4\"\r\nexit /b 0"),
            Timeout = TimeSpan.FromSeconds(10),
            TemporaryRootDirectory = Path.Combine(_root, "work"),
            PartTimeLimitSeconds = 45,
            ThreadCount = 3
        });
        Check((await limited.AnalyzeAsync(_step)).Status == GeometryLabProcessAdapterStatus.Succeeded,
            "--parca-sure-siniri and --is-parcacigi are passed with their values");

        // Progress: --ilerleme is passed, progress lines are reported in order
        // while other stdout lines are ignored; zero timeout means no limit.
        var reports = new System.Collections.Concurrent.ConcurrentQueue<GeometryLabProgress>();
        var withProgress = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = CreateEngine("parts-progress",
                "if not \"%~5\"==\"--ilerleme\" exit /b 5\r\n" +
                "echo MACRIA-ILERLEME okuma 0 0\r\necho MACRIA-ILERLEME topoloji 0 2\r\necho baska bir satir\r\n" +
                "echo MACRIA-ILERLEME parca 1 2\r\necho MACRIA-ILERLEME parca 2 2\r\necho MACRIA-ILERLEME sac 2 2\r\n" +
                "echo " + json + ">\"%~4\"\r\nexit /b 0"),
            Timeout = TimeSpan.Zero,
            TemporaryRootDirectory = Path.Combine(_root, "work"),
            ProgressChanged = reports.Enqueue
        });
        GeometryLabProcessAdapterResult progressed = await withProgress.AnalyzeAsync(_step);
        Check(progressed.Status == GeometryLabProcessAdapterStatus.Succeeded, "zero timeout runs without a limit and --ilerleme is passed");
        Check(string.Join(",", reports.Select(r => r.Stage + " " + r.Done + "/" + r.Total)) ==
              "okuma 0/0,topoloji 0/2,parca 1/2,parca 2/2,sac 2/2",
            "progress lines are reported in order; other stdout lines are not");
        Check(new GeometryLabProgress("parca", 142, 195).Display == "Parçalar analiz ediliyor: 142 / 195" &&
              GeometryLabProgress.TryParse("MACRIA-ILERLEME parca x 2") is null &&
              GeometryLabProgress.TryParse("parca 1 2") is null,
            "progress display text and malformed lines");

        // With PartDxfRootDirectory: a new folder under the root is passed as --dxf-klasor.
        string dxfRoot = Path.Combine(_root, "part-dxf");
        var adapter = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = CreateEngine("parts-dxf",
                "if not \"%~5\"==\"--dxf-klasor\" exit /b 4\r\nmkdir \"%~6\"\r\necho 0>\"%~6\\part-1.dxf\"\r\necho " + json + ">\"%~4\"\r\nexit /b 0"),
            Timeout = TimeSpan.FromSeconds(10),
            TemporaryRootDirectory = Path.Combine(_root, "work"),
            PartDxfRootDirectory = dxfRoot
        });
        GeometryLabProcessAdapterResult withDxf = await adapter.AnalyzeAsync(_step);
        string? dxfPath = withDxf.Analysis is null ? null : withDxf.PartDxfPath(withDxf.Analysis.Parts[0]);
        Check(withDxf.Status == GeometryLabProcessAdapterStatus.Succeeded && withDxf.PartDxfDirectory is not null &&
              Path.GetDirectoryName(withDxf.PartDxfDirectory) == Path.GetFullPath(dxfRoot),
            "the DXF folder is a new folder under the given root");
        Check(dxfPath is not null && File.Exists(dxfPath), "the part DXF written by the engine survives the analysis");
        string? cutOnlyPath = withDxf.Analysis is null ? null : withDxf.PartDxfPath(withDxf.Analysis.Parts[0], cutOnly: true);
        Check(cutOnlyPath is not null && Path.GetFileName(cutOnlyPath) == "part-1-kesim.dxf" &&
              withDxf.PartDxfPath(withDxf.Analysis!.Parts[1], cutOnly: true) is null,
            "the cut-only DXF path is resolved next to the full one");
        Check(withDxf.TemporaryDirectoryCleaned, "the JSON work folder is still cleaned");
        GeometryLabProcessAdapterResult second = await adapter.AnalyzeAsync(_step);
        Check(second.PartDxfDirectory is not null && second.PartDxfDirectory != withDxf.PartDxfDirectory,
            "every analysis gets its own DXF folder");
        Check(withDxf.PartDxfPath(new GeometryLabPartTransport { DxfFile = "..\\x.dxf" }) is null,
            "a DXF file name with path characters is not resolved");

        GeometryLabProcessAdapterResult future = await Adapter(CreateEngine("schema-1-3",
            "echo {\"schemaVersion\":\"1.3\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"profileRecognitions\":[]}>\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(future.Status == GeometryLabProcessAdapterStatus.UnsupportedSchema, "schema 1.3 is rejected");
        Check(GeometryLabProcessAdapter.IsSupportedSchemaVersion("1.2"), "1.2 is a supported schema version");
        GeometryLabAnalysisTransport? legacy = JsonSerializer.Deserialize<GeometryLabAnalysisTransport>("{\"schemaVersion\":\"1.1\",\"status\":\"Succeeded\"}");
        Check(legacy?.Parts.Count == 0, "schema 1.1 JSON has no parts");
    }

    // montaj-1 style folder: one assembly STEP and beklenen.txt with
    // "<part name>: <quantity> <class>" (Sac, Profil, Diğer, Kontrol gerekli).
    private static async Task RealAssemblyEngineAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string? folder = Ortam("MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR");
        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(folder))
        {
            Console.WriteLine("REAL_ASSEMBLY_ENGINE: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR unavailable.");
            return;
        }
        Check(File.Exists(engine) && Directory.Exists(folder), "given engine and assembly folder exist");
        string[] steps = Directory.GetFiles(folder).Where(path =>
            Path.GetExtension(path).Equals(".stp", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".step", StringComparison.OrdinalIgnoreCase)).ToArray();
        Check(steps.Length == 1, "assembly folder holds exactly one STEP");
        var expected = File.ReadAllLines(Path.Combine(folder, "beklenen.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line =>
            {
                int colon = line.LastIndexOf(':');
                string[] rest = line[(colon + 1)..].Trim().Split(' ', 2);
                return (Name: line[..colon].Trim(),
                    Quantity: int.Parse(rest[0], System.Globalization.CultureInfo.InvariantCulture),
                    Class: rest[1].Trim());
            }).ToArray();

        var adapter = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = engine,
            Timeout = TimeSpan.FromMinutes(5),
            TemporaryRootDirectory = Path.Combine(_root, "work"),
            PartDxfRootDirectory = Path.Combine(_root, "assembly-dxf")
        });
        GeometryLabProcessAdapterResult result = await adapter.AnalyzeAsync(steps[0]);
        Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded && result.Analysis?.SchemaVersion == "1.2",
            "real assembly analysis succeeds through the adapter with schema 1.2: " + result.Message);
        IReadOnlyList<GeometryLabPartTransport> parts = result.Analysis!.Parts;
        Check(parts.Count == expected.Length, "real assembly: part count " + parts.Count + " = " + expected.Length);
        foreach (var row in expected)
        {
            GeometryLabPartTransport? part = parts.FirstOrDefault(candidate => candidate.Name == row.Name);
            string engineClass = row.Class switch
            {
                "Sac" => "Sheet",
                "Profil" => "Profile",
                "Kontrol gerekli" => "ReviewRequired",
                _ => "Other"
            };
            Check(part is not null && part.Quantity == row.Quantity && part.Classification == engineClass,
                "real assembly " + row.Name + ": quantity " + part?.Quantity + "/" + row.Quantity +
                ", class " + part?.Classification + "/" + engineClass);
            string? dxf = part is null ? null : result.PartDxfPath(part);
            Check((row.Class == "Sac") == (dxf is not null && File.Exists(dxf) && new FileInfo(dxf).Length > 0),
                "real assembly " + row.Name + ": engine DXF exactly for sheet parts");
            string? cutOnly = part is null ? null : result.PartDxfPath(part, cutOnly: true);
            Check((row.Class == "Sac") == (cutOnly is not null && File.Exists(cutOnly) &&
                                           !File.ReadAllText(cutOnly).Contains("BUKUM", StringComparison.Ordinal)),
                "real assembly " + row.Name + ": cut-only DXF without the BUKUM layer for sheet parts");
            Console.WriteLine("REAL_ASSEMBLY_ENGINE: " + row.Name + " adet=" + part?.Quantity + " sinif=" + part?.Classification +
                (dxf is null ? "" : " dxf=" + Path.GetFileName(dxf)));
        }
        Check(result.Analysis.Otomatik && result.Analysis.AnalysisMode == "Automatic" && result.Analysis.SelectedPartIds is null,
            "real assembly: the whole-STEP output is marked Automatic");

        // Yeniden Analiz Et: the engine on one part, no time limit; the part
        // keeps its id, name, quantity and class of the full analysis.
        GeometryLabPartTransport secilen = parts.First(x => x.Classification == "ReviewRequired");
        var tekParca = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = engine,
            Timeout = TimeSpan.FromMinutes(5),
            TemporaryRootDirectory = Path.Combine(_root, "work"),
            PartDxfRootDirectory = Path.Combine(_root, "assembly-dxf"),
            PartTimeLimitSeconds = 0,
            SelectedPartIds = new[] { secilen.LocalId }
        });
        GeometryLabProcessAdapterResult ek = await tekParca.AnalyzeAsync(steps[0]);
        Check(ek.IsSuccess && ek.Analysis!.AnalysisMode == "Parts" && !ek.Analysis.Otomatik &&
              ek.Analysis.SelectedPartIds?.SequenceEqual(new[] { secilen.LocalId }) == true && ek.Analysis.Parts.Count == 1,
            "real selected-part run: one part, marked Parts: " + ek.Message);
        GeometryLabPartTransport ekParca = ek.Analysis!.Parts[0];
        Check(ekParca.LocalId == secilen.LocalId && ekParca.Name == secilen.Name && ekParca.Quantity == secilen.Quantity &&
              ekParca.Classification == secilen.Classification && ekParca.ClassificationCode == secilen.ClassificationCode,
            "real selected-part run: " + secilen.Name + " equals the full analysis (" + ekParca.Classification + ")");
        var (_, ekMontaj) = MacriaProjeSatirlari.EkSatirlari(steps[0], ek, 20, new SatirDenemesi(MacriaProje.DenemeYeniden, "e1"));
        Check(ekMontaj.Count == 1 && ekMontaj[0].PartLocalId == secilen.LocalId && ekMontaj[0].Deneme?.EkId == "e1",
            "real selected-part run: the part's row is built from it and marked");

        // Profile rows: length, topology and cuts from the part's own solid,
        // the same as the part's single-part STEP when one is given.
        string? singleFolder = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_SINGLE_PART_DIR");
        foreach (GeometryLabPartTransport part in parts.Where(x => x.Classification == "Profile"))
        {
            var assemblyRow = new GeometryLabStepProfileListItem { SourceStepPath = steps[0], PartName = part.Name, PartQuantity = part.Quantity };
            assemblyRow.Apply(MontajParcaSatiri.ResultForPart(result, part));
            Check(assemblyRow.LengthDisplay != "—", "real assembly " + part.Name + ": profile length is measured (" + assemblyRow.LengthDisplay + ")");
            Console.WriteLine("REAL_ASSEMBLY_PROFILE: " + part.Name + " kesit=" + assemblyRow.SectionDisplay + " boy=" + assemblyRow.LengthDisplay +
                " topoloji=" + assemblyRow.TopologyDisplay + " kesim=" + assemblyRow.CutDisplay);
            string? single = string.IsNullOrWhiteSpace(singleFolder) ? null : Directory.GetFiles(singleFolder)
                .Where(path => Path.GetFileName(path).StartsWith(part.Name + "_", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetFileNameWithoutExtension(path).Equals(part.Name, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(path => Path.GetExtension(path).Equals(".stp", StringComparison.OrdinalIgnoreCase) ||
                                        Path.GetExtension(path).Equals(".step", StringComparison.OrdinalIgnoreCase));
            if (single is null)
            {
                Console.WriteLine("REAL_ASSEMBLY_PROFILE: " + part.Name + " single-part comparison SKIPPED (MACRIA_GEOMETRY_ENGINE_SINGLE_PART_DIR).");
                continue;
            }
            var singleRow = new GeometryLabStepProfileListItem { SourceStepPath = single };
            singleRow.Apply(await adapter.AnalyzeAsync(single));
            Check(assemblyRow.SectionDisplay == singleRow.SectionDisplay && assemblyRow.LengthDisplay == singleRow.LengthDisplay &&
                  assemblyRow.TopologyDisplay == singleRow.TopologyDisplay && assemblyRow.CutDisplay == singleRow.CutDisplay,
                "real assembly " + part.Name + " equals its single-part STEP " + Path.GetFileName(single) + ": section " +
                assemblyRow.SectionDisplay + "/" + singleRow.SectionDisplay + ", length " + assemblyRow.LengthDisplay + "/" +
                singleRow.LengthDisplay + ", topology " + assemblyRow.TopologyDisplay + "/" + singleRow.TopologyDisplay +
                ", cuts " + assemblyRow.CutDisplay + "/" + singleRow.CutDisplay);
        }
    }

    private static void AssemblyPartProfileGeometry()
    {
        GeometryLabProfileAxisCandidateTransport Axis(int id, int solid, double span, bool reliable) => new()
        {
            LocalId = id, SolidId = new GeometryLabLocalIdTransport { LocalId = solid }, ProjectionSpanMm = span, Reliable = reliable
        };
        var geometry = new GeometryLabProfileGeometryAnalysisTransport
        {
            // Assembly-wide: solid 3 has no reliable axis, so the whole analysis is Unknown.
            AxisDetectionStatus = "Unknown",
            AxisCandidates = new[]
            {
                Axis(1, 1, 85, true), Axis(2, 1, 40, false),
                Axis(3, 2, 120, true), Axis(4, 2, 118, true),
                Axis(5, 3, 60, false)
            }
        };
        GeometryLabProfileGeometryAnalysisTransport? first = MontajParcaSatiri.GeometryForPart(geometry, new HashSet<int> { 1 });
        Check(first?.AxisDetectionStatus == "Determined" && first.AxisCandidates.Count == 2 &&
              first.AxisCandidates.Single(x => x.Reliable).ProjectionSpanMm == 85,
            "assembly part profile geometry: its own solid's one reliable axis is Determined (85 mm)");
        Check(MontajParcaSatiri.GeometryForPart(geometry, new HashSet<int> { 2 })?.AxisDetectionStatus == "Ambiguous",
            "assembly part profile geometry: two reliable axes on the part's solid stay Ambiguous");
        Check(MontajParcaSatiri.GeometryForPart(geometry, new HashSet<int> { 3 })?.AxisDetectionStatus == "Unknown",
            "assembly part profile geometry: no reliable axis stays Unknown");
        Check(MontajParcaSatiri.GeometryForPart(geometry, new HashSet<int> { 1, 3 })?.AxisDetectionStatus == "Unknown",
            "assembly part profile geometry: every solid of the part needs its axis");
        var old = geometry with { AxisCandidates = geometry.AxisCandidates.Select(x => x with { SolidId = null }).ToArray() };
        Check(MontajParcaSatiri.GeometryForPart(old, new HashSet<int> { 1 }) is null,
            "assembly part profile geometry: candidates without a solid (old engine) are not guessed");
    }

    private static GeometryLabAnalysisTransport AssemblyAnalysis() => new()
    {
        SchemaVersion = "1.2",
        Status = "Succeeded",
        Solids = new[] { new GeometryLabSolidTransport(), new GeometryLabSolidTransport(), new GeometryLabSolidTransport(),
            new GeometryLabSolidTransport(), new GeometryLabSolidTransport() },
        ProfileRecognitions = new[]
        {
            new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 1 }, Status = "Succeeded",
                SectionRecognitionStatus = "Recognized", ProfileType = "SquareHollowSection" },
            new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 2 }, ProfileType = "Unknown" }
        },
        SheetMetalAnalyses = new[]
        {
            new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 2 }, Status = "Recognized",
                ThicknessMm = 20, Bends = new[] { new GeometryLabSheetBendTransport(), new GeometryLabSheetBendTransport() } },
            new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 3 }, Status = "Recognized",
                ThicknessMm = 20.5 },
            new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 4 }, Status = "Recognized",
                ThicknessMm = 8 }
        },
        HoleFeatures = new[]
        {
            new GeometryLabHoleFeatureTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 2 }, Type = "Countersink",
                ThroughDiameterMm = 10, HeadDiameterMm = 20 },
            new GeometryLabHoleFeatureTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 2 }, Type = "Countersink",
                ThroughDiameterMm = 10, HeadDiameterMm = 20 },
            new GeometryLabHoleFeatureTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 2 }, Type = "Counterbore",
                ThroughDiameterMm = 5.4, HeadDiameterMm = 9.75 },
            new GeometryLabHoleFeatureTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 2 }, Type = "Countersink",
                ThroughDiameterMm = 6.647, HeadDiameterMm = 8.647, ThreadDesignation = "M8x1.25", Warning = "w" },
            new GeometryLabHoleFeatureTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 3 }, Type = "Through",
                ThroughDiameterMm = 16, HeadDiameterMm = 16 }
        },
        Parts = new[]
        {
            Part(1, "Kutu", 3, "Profile", false, "SquareHollowSection", null),
            Part(2, "Sac A", 2, "Sheet", true, null, "part-2.dxf"),
            Part(3, "Kalin Sac", 1, "Sheet", true, null, "part-3.dxf"),
            Part(4, "Cakisma", 1, "ReviewRequired", true, "RectangularHollowSection", "part-4.dxf"),
            Part(5, "Mil", 1, "Other", false, null, null)
        }
    };

    private static GeometryLabPartTransport Part(int id, string name, int quantity, string classification, bool sheet,
        string? profile, string? dxf) => new()
    {
        LocalId = id, Name = name, Quantity = quantity, Classification = classification, SheetCandidate = sheet,
        ProfileCandidate = profile, DxfFile = dxf, ClassificationReasons = new[] { "gerekçe " + id },
        SolidIds = new[] { new GeometryLabLocalIdTransport { LocalId = id } }
    };

    // (33) Saclar: the user corrects a thickness (raw plate); it names the
    // DXF, picks Lazer / Şalama, is saved in the project; the engine value stays.
    private static void KalinlikDuzeltmeTests()
    {
        foreach ((string metin, bool gecerli, double? deger) in new (string, bool, double?)[]
                 {
                     ("16", true, 16), ("16,5", true, 16.5), ("16.5", true, 16.5), (" 12 mm ", true, 12), ("", true, null),
                     ("abc", false, null), ("0", false, null), ("-3", false, null), ("2000", false, null)
                 })
            Check(MontajParcaSatiri.KalinlikGirdisiniOku(metin, out double? okunan) == gecerli && (!gecerli || okunan == deger),
                "thickness input \"" + metin + "\" is " + (gecerli ? "read as " + deger : "rejected"));

        GeometryLabAnalysisTransport analysis = AssemblyAnalysis() with
        {
            SheetMetalAnalyses = AssemblyAnalysis().SheetMetalAnalyses.Select(x => x.SolidId?.LocalId == 2
                ? x with { FlatPattern = new GeometryLabFlatPatternTransport { Status = "Succeeded", WidthMm = 120.04, HeightMm = 60,
                    MinimumRectangle = new GeometryLabFlatRectangleTransport { ShortMm = 54.96, LongMm = 118.04, AngleDegrees = 12 } } }
                : x).ToArray()
        };
        MontajParcaSatiri Satir() => MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], "C:\\x.dxf", 20);
        MontajParcaSatiri sac = Satir();
        Check(sac.AcinimOlcusuDisplay == "120 × 60 mm" && sac.MotorKalinlikDisplay == "20 mm" && !sac.KalinlikDuzeltildi,
            "Açınım ölçüsü is the rectangle around the flat pattern: " + sac.AcinimOlcusuDisplay);
        Check(sac.EnKucukDikdortgenDisplay == "55 × 118 mm" && sac.EnKucukDikdortgenEnMm == 54.96,
            "the smallest rectangle (short × long) comes from the engine: " + sac.EnKucukDikdortgenDisplay);
        sac.KalinligiDuzelt(25);
        Check(sac.KalinlikDuzeltildi && sac.EtkinKalinlikMm == 25 && sac.ThicknessMm == 20 && sac.ThicknessDisplay == "25 mm" &&
              sac.MotorKalinlikDisplay == "20 mm" && sac.KalinlikMetni == "25",
            "the corrected thickness is shown; the engine value stays apart");
        Check(sac.IsThickPlate && sac.GroupDisplay == "Şalama/Kütük" && sac.DxfDisplay == "Sac A_25mm_2adet.dxf",
            "the corrected thickness picks Şalama/Kütük and names the DXF: " + sac.DxfDisplay);
        Check(((IAnalizSatiri)sac).OlcuGosterimi == "t = 25 mm (tespit edilen 20)", "the mixed tabs show both thicknesses");
        sac.ApproveAsSheet();
        IReadOnlyList<MotorDxfIsi> plan = MotorDxfAktarici.Planla(new[] { sac }, "C:\\hedef");
        Check(plan.Count == 1 && Path.GetFileName(plan[0].Hedef) == "Sac A_25mm_2adet.dxf", "DXF Üret writes the corrected name");

        // Project: the thickness is a decision of its own, next to Sac onayı and Liste dışı.
        sac.ListeDisinaCikar("fason");
        List<MacriaProjeKarari> kararlar = MacriaProjeSatirlari.KararlariTopla(
            Array.Empty<GeometryLabStepProfileListItem>(), new[] { sac }, _ => "k1");
        Check(kararlar.Count == 3 && kararlar.Any(k => k.Karar == MacriaProje.KararKalinlik && k.KalinlikMm == 25),
            "the project keeps the thickness, the approval and the Liste dışı flag");
        MontajParcaSatiri yeni = Satir();
        var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(kararlar,
            MacriaProjeSatirlari.Adaylar(Array.Empty<GeometryLabStepProfileListItem>(), new[] { yeni }, _ => "k1"), _ => false);
        Check(eslenemeyen.Count == 0 && eslenen.All(x => MacriaProjeSatirlari.Uygula(x.Karar, x.Aday.Satir)),
            "all three decisions map to the reopened row");
        Check(yeni.EtkinKalinlikMm == 25 && yeni.EffectiveCategory == MontajParcaKategorisi.Sac && yeni.ListeDisi,
            "reopened: thickness 25, approved, Liste dışı");

        yeni.KalinligiDuzelt(20);
        Check(!yeni.KalinlikDuzeltildi && yeni.EtkinKalinlikMm == 20 && !yeni.IsThickPlate,
            "typing the engine value back clears the correction");
        yeni.KalinligiDuzelt(null);
        Check(MacriaProjeSatirlari.KararlariTopla(Array.Empty<GeometryLabStepProfileListItem>(), new[] { yeni }, _ => "k1")
                  .All(k => k.Karar != MacriaProje.KararKalinlik),
            "without a correction no thickness decision is saved");
    }

    // (31) A user decision does not wipe the engine's reason: "Motor gerekçesi"
    // and "Kullanıcı kararı" are separate columns.
    private static void KararSutunlariTests()
    {
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        MontajParcaSatiri sac = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], "C:\\x.dxf", 20);
        string sacGerekcesi = sac.MotorGerekcesi;
        Check(sacGerekcesi == "Sac" && sac.KullaniciKarariMetni == "—" &&
              ((IAnalizSatiri)sac).Ayrintilar.Any(x => x.Key == TeknikAyrinti.Anahtar && x.Value == "gerekçe 2"),
            "no decision: engine reason (short; its own text in Teknik ayrıntı), user column empty: " + sacGerekcesi);
        sac.ApproveAsSheet();
        sac.KalinligiDuzelt(25);
        sac.ListeDisinaCikar("fason");
        Check(sac.MotorGerekcesi == sacGerekcesi, "the engine reason stays after the user's decisions");
        Check(sac.KullaniciKarariMetni == "Sac olarak onaylandı; Ham sac kalınlığı 25 mm (tespit edilen 20 mm); Liste dışı: fason",
            "the user column lists every decision: " + sac.KullaniciKarariMetni);
        sac.RestoreAutomaticDecision();
        Check(sac.KullaniciKarariMetni == "Ham sac kalınlığı 25 mm (tespit edilen 20 mm); Liste dışı: fason",
            "Otomatik Karara Dön drops the category decision only: " + sac.KullaniciKarariMetni);
        MontajParcaSatiri acinimsiz = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[4], null, 20);
        acinimsiz.ApproveAsSheet();
        Check(acinimsiz.KullaniciKarariMetni == "Sac olarak onaylandı (açınım yok, DXF CATIA'dan)" &&
              ((IAnalizSatiri)acinimsiz).Ayrintilar.Any(x => x.Key == TeknikAyrinti.Anahtar && x.Value == "gerekçe 5"),
            "an approved part without a flat pattern says where its DXF comes from");

        var full = new GeometryLabProcessAdapterResult { Status = GeometryLabProcessAdapterStatus.Succeeded, Analysis = analysis };
        var profil = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\m.stp", PartName = "Kutu", PartQuantity = 3 };
        profil.Apply(MontajParcaSatiri.ResultForPart(full, analysis.Parts[0]));
        string motor = profil.MotorGerekcesi;
        Check(motor != "—" && profil.KullaniciKarariMetni == "—", "a profile row shows its automatic reason: " + motor);
        profil.MoveToReview("ölçü şüpheli");
        Check(profil.MotorGerekcesi == motor && profil.KullaniciKarariMetni == "Kullanıcı kararıyla kontrol gerekliye alındı — ölçü şüpheli",
            "a profile decision keeps the automatic reason: " + profil.KullaniciKarariMetni);
        Check(((IAnalizSatiri)profil).Ayrintilar.Any(x => x.Key == "Motor gerekçesi" && x.Value == motor) &&
              ((IAnalizSatiri)sac).Ayrintilar.Any(x => x.Key == "Kullanıcı kararı"),
            "Seçili Parça shows both lines");

        // A profile part carries its machining into the profile row (mixed tabs and Seçili Parça).
        GeometryLabAnalysisTransport islenmis = analysis with
        {
            Parts = analysis.Parts.Select((p, i) => i == 0 ? p with { MachiningPresent = true, MachiningKinds = new[] { "Engraving" } } : p).ToArray()
        };
        var (profilSatirlari, _) = MacriaProjeSatirlari.MontajSatirlari("C:\\m.stp",
            new GeometryLabProcessAdapterResult { Status = GeometryLabProcessAdapterStatus.Succeeded, Analysis = islenmis }, 20);
        IAnalizSatiri gravurluProfil = profilSatirlari.Single(r => r.PartLocalId == islenmis.Parts[0].LocalId);
        Check(gravurluProfil.IslemeGosterimi == "var (gravür)" &&
              gravurluProfil.Ayrintilar.Any(x => x.Key == "İşleme" && x.Value == "var (gravür)") &&
              ((IAnalizSatiri)profil).IslemeGosterimi == "—",
            "a profile row shows its part's machining, without the sheet's DXF note; a row without it shows \"—\"");
    }

    // (34) "Sütunlar": per tab visible columns and order, remembered; new
    // columns of a later Macria join where they stand by default.
    // (5) 55RS100111-7 (Ø107,317 × 3): the end-to-end length is shown as such; the
    // reason says the cut length, not the length, is what could not be proven.
    private static async Task UctanUcaBoyAsync()
    {
        string? engine = Ortam("MACRIA_GEOMETRY_ENGINE_EXE");
        string step = Path.Combine(Fiksturler, "stepler", "55RS100111-7", "55RS100111-7-stp.stp");
        if (string.IsNullOrWhiteSpace(engine) || !File.Exists(engine) || !File.Exists(step))
        {
            Console.WriteLine("REAL_END_TO_END_LENGTH: SKIPPED - engine or fixture unavailable.");
            return;
        }
        GeometryLabProcessAdapterResult sonuc = await new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = engine,
            Timeout = TimeSpan.FromMinutes(2),
            TemporaryRootDirectory = Path.Combine(_root, "work")
        }).AnalyzeAsync(step);
        var (profil, _) = MacriaProjeSatirlari.Kur(step, sonuc, 20);
        GeometryLabStepProfileListItem boru = profil.Single();
        Check(boru.LengthDisplay == "100 mm (uçtan uca)" &&
              boru.MotorGerekcesi.Contains("uçtan uca boy 100 mm") && !boru.MotorGerekcesi.Contains("güvenilir profil boyu bulunamadı"),
            "a tube whose cuts are not proven: \"Boy\" is end to end, the reason agrees: " + boru.SectionDisplay + " / " +
            boru.LengthDisplay + " / " + boru.MotorGerekcesi);
    }

    // (4) Excel and the mixed tabs: a profile row's Parça is the part's name; numbers use the decimal comma.
    private static void ParcaAdiVeVirgulTests()
    {
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        var (profil, _) = MacriaProjeSatirlari.MontajSatirlari("C:\\WGRV004423 A.stp",
            new GeometryLabProcessAdapterResult { Status = GeometryLabProcessAdapterStatus.Succeeded, Analysis = analysis }, 20);
        IAnalizSatiri kutu = profil.First();
        Check(kutu.ParcaGosterimi == "Kutu" && (string?)AnalizSutunDuzeni.Deger(kutu, nameof(IAnalizSatiri.ParcaGosterimi)) == "Kutu",
            "a profile row's Parça (and its Excel cell) is the part's name: " + kutu.ParcaGosterimi);
        var boru = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\b.stp", PartName = "357184", PartQuantity = 4, PartLocalId = 1 };
        boru.Apply(new GeometryLabProcessAdapterResult
        {
            Status = GeometryLabProcessAdapterStatus.Succeeded,
            Analysis = analysis with
            {
                ProfileRecognitions = new[]
                {
                    new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 1 }, Status = "Succeeded",
                        SectionRecognitionStatus = "Recognized", ProfileType = "CircularHollowSection", OuterDiameterMm = 107.3171946826, WallThicknessMm = 4.947 }
                }
            }
        });
        Check(boru.SectionDisplay == "Ø107,317 × 4,947 mm", "section numbers use the decimal comma: " + boru.SectionDisplay);
    }

    // (6) Excel: quantities are numbers; the STEP column names the file.
    private static void ExcelDegeriTests()
    {
        Check(AnalizSutunDuzeni.ExcelDegeri("Adet", "4") is double adet && adet == 4 &&
              AnalizSutunDuzeni.ExcelDegeri("CATIA Adedi", "12") is double catia && catia == 12 &&
              AnalizSutunDuzeni.ExcelDegeri("Adet", "—") is "—" && AnalizSutunDuzeni.ExcelDegeri("Adet", null) is null,
            "Adet and CATIA Adedi go to Excel as numbers; \"—\" is no number");
        Check(AnalizSutunDuzeni.ExcelDegeri("Parça", "1") is "1", "a part named \"1\" stays text");
        Check(AnalizSutunDuzeni.ExcelDegeri("Ham sac kalınlığı (mm)", 8.000000000017916) is double t && t == 8 &&
              AnalizSutunDuzeni.ExcelDegeri("Tespit edilen kalınlık", 12.345678) is double t2 && t2 == 12.35 &&
              AnalizSutunDuzeni.ExcelDegeri("Açınım eni (mm)", 45.000000000003865) is double en && en == 45 &&
              AnalizSutunDuzeni.ExcelDegeri("En küçük dikdörtgen boyu (mm)", 229.46) is double boy && boy == 229.5,
            "Excel: thickness to 0,01 mm, flat sizes to 0,1 mm");
        Check(AnalizSutunDuzeni.ExcelDegeri("Büküm", "4") is double bukum && bukum == 4 && AnalizSutunDuzeni.ExcelDegeri("Büküm", "—") is "—",
            "Excel: Büküm is a number");
        Check(AnalizSutunDuzeni.ExcelDegeri(AnalizSutunDuzeni.StepSutunu, @"C:\a\Montaj A.stp") is "Montaj A.stp",
            "the STEP column holds the file name");
    }

    // (1) Motor gerekçesi: the result first, a short reason; no face ids, no English.
    private static void MotorGerekcesiTests()
    {
        string K(string kod, params string[] g) => MotorGerekcesiMetni.Kisa(kod, g);
        var ornekler = new (string Kod, string[] Gerekce, string Beklenen)[]
        {
            (MotorSinifKodu.ThickerThanOutline, new[] { "Sac kabuğu bulundu ama kalınlık (30 mm) açınımın en dar ölçüsünden (11,876 mm) büyük: levha değil, çubuk.",
                "Sac veya profil olarak tanınmadı." }, "Sac değil (çubuk/mil): kalınlık 30 mm > en dar ölçü 11,9 mm"),
            (MotorSinifKodu.ThickerThanMaterial, new[] { "Sac kabuğu bulundu ama kalınlık (58 mm) parçanın gerçek et genişliğinden (49,108 mm) büyük: levha değil (halka, somun ya da mil)." },
                "Sac değil (halka/somun/mil): kalınlık 58 mm > et genişliği 49,1 mm"),
            (MotorSinifKodu.UnsupportedFaces, new[] { "Tanıyıcıların desteklemediği yüz tipleri var (Torus): Kabuklar arasında kalmayan yüzler var: F4088, F4089." },
                "Tanınamadı: yuvarlatılmış (torus) yüzler var"),
            (MotorSinifKodu.SheetAnalysisIncomplete, new[] { "Sac analizi Unsupported: Kalınlık çiftleri arasında düzlem yüz yok." }, "Sac değil: paralel düz yüz çifti yok"),
            (MotorSinifKodu.FlatPatternFailed, new[] { "Sac tanındı ama açınım üretilemedi: Büküm F3177 iki flanş arasında değil." }, "Sac, açınım yok: uçta biten büküm"),
            (MotorSinifKodu.Sheet, new[] { "Sac: t=20 mm, 0 büküm. İşleme var (cep, basamak, havşa / imbus başı); DXF'te yalnız dış hat ve boydan boya delikler." },
                "Sac: t = 20 mm, 0 büküm; işleme var"),
            (MotorSinifKodu.HollowProfile, new[] { "Profil: SquareHollowSection." }, "Profil: kare kutu"),
            (MotorSinifKodu.NotRecognized, new[] { "Sac veya profil olarak tanınmadı (Sabit ofsetli karşılıklı yüz çifti bulunamadı.)." },
                "Tanınamadı: karşılıklı paralel yüz yok"),
            (MotorSinifKodu.HollowProfile, new[] { "Profil olarak denendi (gevşetilmiş: kesiti tutarlı içi boş tek eksen seçildi (3 güvenilir eksenden)): Profil: SquareHollowSection." },
                "Profil olarak denendi → Profil: kare kutu (gevşetilen: tek eksen)"),
            (MotorSinifKodu.SolidBar, new[] { "Profil olarak denendi: profil tanınmadı (kesit kanıtı yetersiz: No reliable profile axis exists for this solid.).",
                "Dolu kesit (SolidCircularBar); içi boş olmadığı için profil sayılmaz, sac da değil." },
                "Profil olarak denendi, tanınmadı (kesit kanıtı yetersiz) → Profil değil: dolu yuvarlak çubuk (mil)"),
            (MotorSinifKodu.Sheet, new[] { "Sac olarak denendi (gevşetilmiş: levha değil kuralı uygulanmadı (kalınlık 44 mm > en dar ölçü 12,472 mm)): Sac: t=44 mm, 0 büküm." },
                "Sac olarak denendi → Sac: t = 44 mm, 0 büküm (gevşetilen: levha değil kuralı, kalınlık 44 mm > en dar ölçü 12,5 mm)"),
            (MotorSinifKodu.Sheet, new[] { "Sac olarak denendi: Sac, açınım yok: Büküm F47 iki flanş arasında değil. t=2 mm." },
                "Sac olarak denendi → Sac, açınım yok: uçta biten büküm; t = 2 mm"),
            (MotorSinifKodu.NotRecognized, new[] { "Profil olarak denendi: yuvarlak boru, boru tanıma henüz yok.", "Sac veya profil olarak tanınmadı." },
                "Profil olarak denendi: yuvarlak boru, boru tanıma henüz yok"),
            (MotorSinifKodu.ThickerThanOutline, new[] { "Sac olarak denendi: sac tanınmadı (kalınlık 44 mm, açınımın en dar ölçüsü 12,472 mm'den küçük değil: levha değil).",
                "Sac kabuğu bulundu ama kalınlık (44 mm) açınımın en dar ölçüsünden (12,472 mm) büyük: levha değil, çubuk.", "Sac veya profil olarak tanınmadı." },
                "Sac olarak denendi, tanınmadı → Sac değil (çubuk/mil): kalınlık 44 mm > en dar ölçü 12,5 mm"),
            (MotorSinifKodu.NotRecognized, new[] { "Sac olarak denendi: sac tanınmadı (kapalı kesit (boru / kutu) sac değil).",
                "Sac veya profil olarak tanınmadı (Kabuk kendi üzerine kapanıyor (kapalı kesit / boru); sac açınımı yok.)." },
                "Sac olarak denendi, tanınmadı → Sac değil: kapalı kesit (boru)"),
            // Engine 2026.10.6.4: the material-width rule holds in the trial; a closed section says its shape.
            (MotorSinifKodu.ThickerThanMaterial, new[] { "Sac olarak denendi: sac tanınmadı (kalınlık 30 mm > et genişliği 9,848 mm: levha değil (halka, burç, somun)).",
                "Sac kabuğu bulundu ama kalınlık (30 mm) parçanın gerçek et genişliğinden (9,848 mm) büyük: levha değil (halka, somun ya da mil)." },
                "Sac olarak denendi, tanınmadı → Sac değil (halka/somun/mil): kalınlık 30 mm > et genişliği 9,8 mm"),
            (MotorSinifKodu.NotRecognized, new[] { "Sac olarak denendi: sac tanınmadı (kapalı kesit (kutu profil) sac değil).",
                "Sac veya profil olarak tanınmadı (Kabuk kendi üzerine kapanıyor (kapalı kesit / boru); sac açınımı yok.)." },
                "Sac olarak denendi, tanınmadı: kapalı kesit (kutu profil)"),
            (MotorSinifKodu.NotRecognized, new[] { "Sac olarak denendi: sac tanınmadı (kapalı kesit (boru) sac değil).",
                "Sac veya profil olarak tanınmadı (Kabuk kendi üzerine kapanıyor (kapalı kesit / boru); sac açınımı yok.)." },
                "Sac olarak denendi, tanınmadı: kapalı kesit (boru)")
        };
        // (A) The automatic reason says a closed section's shape (engine 2026.10.6.5+); older outputs keep "(boru)".
        string[] kapali = { "Sac veya profil olarak tanınmadı (Kabuk kendi üzerine kapanıyor (kapalı kesit / boru); sac açınımı yok.)." };
        Check(MotorGerekcesiMetni.Kisa(MotorSinifKodu.NotRecognized, kapali, "Box") == "Sac değil: kapalı kesit (kutu profil)" &&
              MotorGerekcesiMetni.Kisa(MotorSinifKodu.NotRecognized, kapali, "Tube") == "Sac değil: kapalı kesit (boru)" &&
              MotorGerekcesiMetni.Kisa(MotorSinifKodu.NotRecognized, kapali) == "Sac değil: kapalı kesit (boru)",
            "automatic closed-section reason: kutu profil / boru; without the shape (older output) as before");
        foreach (var (kod, gerekce, beklenen) in ornekler)
        {
            string kisa = K(kod, gerekce);
            Check(kisa == beklenen, "motor gerekçesi " + kod + ": " + kisa);
            Check(!System.Text.RegularExpressions.Regex.IsMatch(kisa, @"\bF[0-9]+") && !kisa.Contains("Unsupported") && !kisa.Contains("reliable") &&
                  !kisa.Contains("Sac kabuğu bulundu"), "no face ids, English or inner steps: " + kisa);
        }
        Check(K(MotorSinifKodu.Sheet) == "—" && MotorGerekcesiMetni.Teknik(new[] { "a", "b" }) == "a b", "no reason: \"—\"; Teknik ayrıntı keeps the text");
    }

    private static void SutunDuzeniTests()
    {
        AnalizSutunu S(string baslik, bool gorunur = true) => new(baslik, gorunur);
        var varsayilan = new List<AnalizSutunu> { S("Durum"), S("Parça"), S("Adet"), S("Yeni"), S("STEP dosyası", false) };
        List<AnalizSutunu> duzen = AnalizSutunDuzeni.Birlestir(
            new List<AnalizSutunu> { S("Adet"), S("Durum", false), S("Eski"), S("Parça"), S("STEP dosyası", true) }, varsayilan);
        Check(string.Join(",", duzen.Select(x => x.Baslik + (x.Gorunur ? "" : "-"))) == "Adet,Yeni,Durum-,Parça,STEP dosyası",
            "saved order and visibility kept, an unknown saved column dropped, a new column after its neighbour: " +
            string.Join(",", duzen.Select(x => x.Baslik + (x.Gorunur ? "" : "-"))));
        Check(AnalizSutunDuzeni.Birlestir(null, varsayilan).SequenceEqual(varsayilan), "no saved layout: the default");
        Check(AnalizSutunDuzeni.Birlestir(varsayilan.Select(x => x with { Gorunur = false }).ToList(), varsayilan).SequenceEqual(varsayilan),
            "a layout with nothing visible falls back to the default");

        string yol = Path.Combine(_root, "sutunlar", "analiz-sutunlari.txt");
        var kayit = new Dictionary<AnalizSekmesi, List<AnalizSutunu>>
        {
            [AnalizSekmesi.Saclar] = new() { S("Parça"), S("Ham sac kalınlığı (mm)"), S("Motor gerekçesi", false) },
            [AnalizSekmesi.Tanimsiz] = new() { S("Durum", false), S("Parça") }
        };
        Check(AnalizSutunDuzeni.Yaz(yol, kayit) == null, "the layout is written");
        Dictionary<AnalizSekmesi, List<AnalizSutunu>> okunan = AnalizSutunDuzeni.Oku(yol);
        Check(okunan.Count == 2 && okunan[AnalizSekmesi.Saclar].SequenceEqual(kayit[AnalizSekmesi.Saclar]) &&
              okunan[AnalizSekmesi.Tanimsiz].SequenceEqual(kayit[AnalizSekmesi.Tanimsiz]), "every tab's layout reads back");
        Check(AnalizSutunDuzeni.Oku(Path.Combine(_root, "sutunlar", "yok.txt")).Count == 0, "no file: no saved layout");
        string eski = Path.Combine(_root, "sutunlar", "eski-adlar.txt");
        File.WriteAllLines(eski, new[] { "Saclar|Motor kalınlığı|0", "Saclar|Kalınlık (mm)|1", "Saclar|Parça|1" });
        Check(AnalizSutunDuzeni.Oku(eski)[AnalizSekmesi.Saclar].Select(x => x.Baslik + (x.Gorunur ? "" : "-")).SequenceEqual(
                  new[] { "Tespit edilen kalınlık-", "Ham sac kalınlığı (mm)", "Parça" }),
            "a layout saved under the old column names keeps working after the rename");
        // Dragged widths go with the layout; a three-field line (no width) still reads.
        var genislikli = new Dictionary<AnalizSekmesi, List<AnalizSutunu>>
        {
            [AnalizSekmesi.Profiller] = new() { new("Parça", true, 212.5), S("Adet"), new("Motor gerekçesi", false, 480) }
        };
        string genislikYolu = Path.Combine(_root, "sutunlar", "genislik.txt");
        Check(AnalizSutunDuzeni.Yaz(genislikYolu, genislikli) == null &&
              AnalizSutunDuzeni.Oku(genislikYolu)[AnalizSekmesi.Profiller].SequenceEqual(genislikli[AnalizSekmesi.Profiller]) &&
              File.ReadAllLines(genislikYolu)[0] == "Profiller|Parça|1|212.5" && File.ReadAllLines(genislikYolu)[1] == "Profiller|Adet|1",
            "column widths are written as a fourth field (invariant culture) and read back; a default width writes no field");
        File.WriteAllLines(genislikYolu, new[] { "Profiller|Parça|1|abc", "Profiller|Adet|1|-5", "Profiller|Durum|1|90|x" });
        Check(AnalizSutunDuzeni.Oku(genislikYolu)[AnalizSekmesi.Profiller].SequenceEqual(new[] { S("Parça"), S("Adet") }),
            "an unreadable or negative width is the default width; a line with extra fields is skipped");
        Check(AnalizSutunDuzeni.Birlestir(genislikli[AnalizSekmesi.Profiller], new List<AnalizSutunu> { S("Parça"), S("Adet"), S("Motor gerekçesi") })
                  .Select(x => x.Genislik).SequenceEqual(new double?[] { 212.5, null, 480 }),
            "the merged layout keeps the saved widths");

        // Excel values: the column's property, through IAnalizSatiri for explicit members.
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        MontajParcaSatiri sac = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], "C:\\x.dxf", 20);
        Check((string?)AnalizSutunDuzeni.Deger(sac, nameof(IAnalizSatiri.ParcaGosterimi)) == "Sac A" &&
              (double?)AnalizSutunDuzeni.Deger(sac, nameof(MontajParcaSatiri.EtkinKalinlikMm)) == 20 &&
              (int?)AnalizSutunDuzeni.Deger(sac, nameof(MontajParcaSatiri.Quantity)) == 2 &&
              AnalizSutunDuzeni.Deger(sac, "YokBoyle") == null && AnalizSutunDuzeni.Deger(sac, null) == null,
            "cell values come from the row's properties, explicit interface members included");
    }

    // (35) The tab search: part name, the part column and the STEP file name;
    // case and Turkish letters ignored.
    private static void AramaTests()
    {
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        MontajParcaSatiri sac = MontajParcaSatiri.Olustur("C:\\Proje\\Kılıç Montaj.stp", analysis, analysis.Parts[1], "C:\\x.dxf", 20);
        foreach ((string arama, bool uyar) in new[]
                 {
                     ("", true), ("  ", true), ("sac", true), ("SAC A", true), ("c a", true), ("KILIÇ", true), ("kılıç montaj", true),
                     ("montaj.stp", true), ("Proje", false), ("profil", false)
                 })
            Check(SekmeKurallari.AramayaUyar(sac, arama) == uyar, "search \"" + arama + "\" " + (uyar ? "matches" : "does not match"));
        var profil = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\m.stp", PartName = "Kutu", PartQuantity = 3 };
        Check(SekmeKurallari.AramayaUyar(profil, "kutu") && SekmeKurallari.AramayaUyar(profil, "M.STP") &&
              !SekmeKurallari.AramayaUyar(profil, "sac"), "profile rows are searched by part and file name");
        // (B) Several terms: any one matches; comma, semicolon, line break and tab; spaces and case ignored.
        var p1 = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\w.stp", PartName = "55RS100111-7", PartQuantity = 1 };
        var p2 = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\w.stp", PartName = "236948", PartQuantity = 4 };
        var p3 = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\w.stp", PartName = "M5 Baskı", PartQuantity = 2 };
        var p4 = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\w.stp", PartName = "434865", PartQuantity = 4 };
        var hepsi = new[] { p1, p2, p3, p4 };
        string Bulunan(string arama) => string.Join("|", hepsi.Where(x => SekmeKurallari.AramayaUyar(x, arama)).Select(x => x.PartName));
        Check(Bulunan("236948, m5 BASKI;55RS 100111-7") == "55RS100111-7|236948|M5 Baskı", "three terms, mixed separators: " + Bulunan("236948, m5 BASKI;55RS 100111-7"));
        Check(Bulunan("236948\r\n434865\t m5BASKI") == "236948|M5 Baskı|434865", "line breaks and tabs separate terms: " + Bulunan("236948\r\n434865\t m5BASKI"));
        // Turkish i / ı and case are one letter: baski = Baskı = BASKI = BASKİ.
        foreach (string baski in new[] { "m5 baski", "M5 BASKI", "m5 baskı", "M5 BASKİ" })
            Check(Bulunan(baski) == "M5 Baskı", "search \"" + baski + "\" finds M5 Baskı (i / ı and case ignored)");
        Check(SekmeKurallari.AramayaUyar(new GeometryLabStepProfileListItem { SourceStepPath = "C:\\w.stp", PartName = "DİREK", PartQuantity = 1 }, "direk") &&
              SekmeKurallari.AramayaUyar(new GeometryLabStepProfileListItem { SourceStepPath = "C:\\w.stp", PartName = "kılıç", PartQuantity = 1 }, "KILIC") == false,
            "İ / i are one letter; other Turkish letters stay (ç ≠ c)");
        Check(Bulunan("236948,, ;  ,") == "236948" && Bulunan(" , ; ") == "55RS100111-7|236948|M5 Baskı|434865" &&
              SekmeKurallari.AramaTerimleri(" , ;\n ").Count == 0, "empty terms are dropped; no term matches all");
        Check(SekmeKurallari.AramaYapistir("236948\r\n434865\r\n\r\nM5 Baskı\r\n") == "236948, 434865, M5 Baskı" &&
              SekmeKurallari.AramaYapistir("tek") == "tek", "a column pasted from Excel becomes \", \"-separated terms");
    }

    // Toplu DXF's "Güncelle" for Saclar: a DXF written under the old thickness
    // gets the new name; an existing file with that name is not replaced.
    private static void DxfYenidenAdlandirmaTests()
    {
        string klasor = Path.Combine(_root, "dxf-ad", "Motor-DXF");
        Directory.CreateDirectory(klasor);
        string Yaz(string ad) { string yol = Path.Combine(klasor, ad); File.WriteAllText(yol, ad); return yol; }
        string tasinan = Yaz("Sac A_20mm_2adet.dxf");
        string engelli = Yaz("Sac B_20mm_1adet.dxf");
        Yaz("Sac B_25mm_1adet.dxf");
        string yok = Path.Combine(klasor, "Sac C_20mm_1adet.dxf");
        List<DxfAdlandirmaSonucu> sonuc = MotorDxfAktarici.YenidenAdlandir(new[] { tasinan }, "Sac A_25mm_2adet.dxf");
        Check(sonuc.Single().Durum == DxfAdlandirmaDurumu.Tasindi && !File.Exists(tasinan) &&
              File.ReadAllText(Path.Combine(klasor, "Sac A_25mm_2adet.dxf")) == "Sac A_20mm_2adet.dxf",
            "the DXF is renamed to the new thickness in its folder, content kept");
        sonuc = MotorDxfAktarici.YenidenAdlandir(new[] { engelli, yok, Path.Combine(klasor, "Sac A_25mm_2adet.dxf") }, "Sac B_25mm_1adet.dxf");
        Check(sonuc[0].Durum == DxfAdlandirmaDurumu.HedefVar && File.Exists(engelli) &&
              File.ReadAllText(Path.Combine(klasor, "Sac B_25mm_1adet.dxf")) == "Sac B_25mm_1adet.dxf",
            "an existing file with the new name is not replaced");
        Check(sonuc[1].Durum == DxfAdlandirmaDurumu.DosyaYok, "a file that is gone is reported");
        Check(MotorDxfAktarici.YenidenAdlandir(new[] { engelli }, "Sac B_20mm_1adet.dxf").Single().Durum == DxfAdlandirmaDurumu.AyniAd,
            "same name: nothing to do");

        // DXF Üret's plan carries the row; the row remembers its files in the project.
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        MontajParcaSatiri sac = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], "C:\\x.dxf", 20);
        sac.ApproveAsSheet();
        Check(MotorDxfAktarici.Planla(new[] { sac }, klasor).Single().Satir == sac, "the DXF Üret plan knows its row");
        sac.DxfYazildi(Path.Combine(klasor, "Sac A_20mm_2adet.dxf"));
        sac.DxfYazildi(Path.Combine(klasor, "sac a_20mm_2adet.DXF"));
        sac.DxfYazildi(Path.Combine(_root, "baska", "Sac A_20mm_2adet.dxf"));
        sac.KalinligiDuzelt(25);
        Check(sac.YazilanDxfYollari.Count == 2 && sac.DxfDosyaAdi == "Sac A_25mm_2adet.dxf", "files are remembered once (case ignored)");
        List<MacriaProjeKarari> kararlar = MacriaProjeSatirlari.KararlariTopla(Array.Empty<GeometryLabStepProfileListItem>(), new[] { sac }, _ => "k1");
        Check(kararlar.Count(k => k.Karar == MacriaProje.KararDxfDosyasi) == 2, "every remembered DXF is saved");
        MontajParcaSatiri yeni = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], "C:\\x.dxf", 20);
        var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(kararlar,
            MacriaProjeSatirlari.Adaylar(Array.Empty<GeometryLabStepProfileListItem>(), new[] { yeni }, _ => "k1"), _ => false);
        Check(eslenemeyen.Count == 0 && eslenen.All(x => MacriaProjeSatirlari.Uygula(x.Karar, x.Aday.Satir)) &&
              yeni.YazilanDxfYollari.SequenceEqual(sac.YazilanDxfYollari) && yeni.EtkinKalinlikMm == 25 &&
              yeni.EffectiveCategory == MontajParcaKategorisi.Sac,
            "reopened: both DXF paths, the thickness and the approval");
        yeni.DxfYolunuDegistir(yeni.YazilanDxfYollari[0], Path.Combine(klasor, "Sac A_25mm_2adet.dxf"));
        yeni.DxfYolunuDegistir(yeni.YazilanDxfYollari[0], null);
        Check(yeni.YazilanDxfYollari.SequenceEqual(new[] { Path.Combine(klasor, "Sac A_25mm_2adet.dxf") }),
            "a renamed path replaces the old one; a missing one is forgotten");
    }

    private static void AssemblyPartRows()
    {
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        Check(MontajParcaSatiri.IsAssembly(analysis), "several parts make an assembly");
        Check(!MontajParcaSatiri.IsAssembly(analysis with { Parts = new[] { analysis.Parts[1] with { Quantity = 1 } } }),
            "a single part used once is not an assembly");
        Check(MontajParcaSatiri.IsAssembly(analysis with { Parts = new[] { analysis.Parts[1] } }),
            "a single part used twice is an assembly");

        MontajParcaSatiri Row(int index, string? dxf = "C:\\x.dxf") =>
            MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[index], dxf, 20);
        MontajParcaSatiri sheet = Row(1);
        Check(sheet.EffectiveCategory == MontajParcaKategorisi.OnayGerekli && sheet.IsInSheetTab &&
              sheet.StatusDisplay == "Onay gerekli" && sheet.ExplanationDisplay.Contains("doğrulanmadı"),
            "engine sheet without CATIA is a geometric sheet awaiting approval");
        Check(sheet.ThicknessDisplay == "20 mm" && sheet.GroupDisplay == "Lazer" && sheet.BendCountDisplay == "2",
            "thickness 20 = laser maximum is Lazer; bends counted");
        Check(sheet.HoleSummary == "2× Ø10 havşa (Ø20), 1× Ø5,4 imbus (Ø9,75), 1× M8x1.25 dişli (DXF'te yok)",
            "hole summary: countersink, counterbore and tapped hole: " + sheet.HoleSummary);
        MontajParcaSatiri thick = Row(2);
        Check(thick.GroupDisplay == "Şalama/Kütük" && thick.HoleSummary == "1× Ø16 düz", "20,5 mm is Şalama/Kütük");
        Check(!sheet.IsThickPlate && thick.IsThickPlate, "20 mm is in the Lazer sub-tab, 20,5 mm in Şalama/Kütük");
        thick.SetLaserMaximum(25);
        Check(thick.GroupDisplay == "Lazer", "the group follows the laser maximum setting");
        Check(!thick.IsThickPlate, "the sub-tab follows the laser maximum setting");
        // Float noise from the engine (WGRV004423: 20.000000000038 for a 20 mm plate).
        MontajParcaSatiri Kalinlik(double t) => MontajParcaSatiri.Olustur("C:\\m.stp", analysis with
        {
            SheetMetalAnalyses = analysis.SheetMetalAnalyses
                .Select(s => s.SolidId?.LocalId == 2 ? s with { ThicknessMm = t } : s).ToArray()
        }, analysis.Parts[1], "C:\\x.dxf", 20);
        Check(Kalinlik(20.000000000038).GroupDisplay == "Lazer" && !Kalinlik(20.000000000038).IsThickPlate,
            "a 20 mm plate with float noise stays Lazer");
        Check(Kalinlik(20.01).IsThickPlate, "20,01 mm is Şalama/Kütük");

        sheet.ApplyCatiaComparison(new[] { new CatiaScanSnapshotItem("Sac A", "p", "r", "k", 2, true) });
        Check(sheet.EffectiveCategory == MontajParcaKategorisi.Sac && sheet.DecisionSource == GeometryLabDecisionSource.ThreeDScan &&
              sheet.CatiaMatchDisplay == "Eşleşti", "CATIA sheet-metal feature confirms an engine sheet");
        sheet.ApplyCatiaComparison(new[] { new CatiaScanSnapshotItem("Sac A", "p", "r", "k", 4, false) });
        Check(sheet.EffectiveCategory == MontajParcaKategorisi.OnayGerekli && sheet.ExplanationDisplay.Contains("sac unsuru yok") &&
              sheet.CatiaMatchDisplay == "Eşleşti (adet farklı: CATIA 4)",
            "no CATIA sheet-metal feature: geometric sheet, approval needed; quantity difference shown");
        sheet.ApproveAsSheet();
        Check(sheet.EffectiveCategory == MontajParcaKategorisi.Sac && sheet.HasUserDecision && sheet.DecisionDisplay == "Kullanıcı",
            "the user approves a geometric sheet");
        sheet.RestoreAutomaticDecision();
        Check(sheet.EffectiveCategory == MontajParcaKategorisi.OnayGerekli && !sheet.HasUserDecision,
            "automatic decision restored");
        sheet.MoveToReview();
        Check(sheet.IsInReviewTab && !sheet.IsInSheetTab, "the user moves a sheet to Kontrol gerekli");

        MontajParcaSatiri conflict = Row(3);
        Check(conflict.EffectiveCategory == MontajParcaKategorisi.KontrolGerekli, "sheet/hollow profile conflict needs review");
        conflict.ApplyCatiaComparison(new[] { new CatiaScanSnapshotItem("Cakisma", "p", "r", "k", 1, true) });
        Check(conflict.EffectiveCategory == MontajParcaKategorisi.Sac && conflict.ExplanationDisplay.Contains("çakışması"),
            "the CATIA sheet-metal feature settles the conflict as Sac");

        sheet.ListeDisinaCikar("fason");
        Check(sheet.Sekme == AnalizSekmesi.ListeDisi && !sheet.IsInSheetTab && !sheet.IsInReviewTab &&
              sheet.AciklamaGosterimi.Contains("fason"), "Liste dışı takes a row out of every tab, with its note");
        sheet.ListeyeGeriAl();
        Check(sheet.Sekme == AnalizSekmesi.KontrolGerekli && sheet.HasUserDecision,
            "Geri Al returns the row to the tab of its decision");

        MontajParcaSatiri shaft = MontajParcaSatiri.Olustur("C:\\m.stp", analysis,
            analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.SolidBar, RecognitionEvidence = true }, null, 20);
        Check(shaft.EffectiveCategory == MontajParcaKategorisi.Diger && shaft.Sekme == AnalizSekmesi.KontrolGerekli &&
              !shaft.CanApproveAsSheet, "a solid bar is Diğer under Kontrol gerekli");
        shaft.ApproveAsSheet();
        Check(shaft.EffectiveCategory == MontajParcaKategorisi.Sac && shaft.Sekme == AnalizSekmesi.Saclar &&
              shaft.DxfDisplay == "açınım yok – CATIA'dan" && shaft.ExplanationDisplay.Contains("CATIA'dan"),
            "a part without flat pattern is approved as sheet; its DXF comes from CATIA");
        Check(sheet.DxfDisplay != "açınım yok – CATIA'dan", "a part with an engine DXF names the DXF file");
        // The engine measured a thickness but could not unfold the part
        // (FlatPatternFailed) or did not take it for a sheet: the thickness is
        // shown anyway; approved, the part is grouped by it.
        GeometryLabAnalysisTransport olculen = analysis with
        {
            SheetMetalAnalyses = analysis.SheetMetalAnalyses.Concat(new[]
            {
                new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, Status = "Recognized",
                    ThicknessMm = 30, Bends = new[] { new GeometryLabSheetBendTransport() } }
            }).ToArray()
        };
        MontajParcaSatiri acinimsiz = MontajParcaSatiri.Olustur("C:\\m.stp", olculen, analysis.Parts[4] with
        {
            Classification = "ReviewRequired", ClassificationCode = MotorSinifKodu.FlatPatternFailed, RecognitionEvidence = true,
            MachiningPresent = true
        }, null, 20);
        Check(acinimsiz.Sekme == AnalizSekmesi.KontrolGerekli && acinimsiz.ThicknessDisplay == "30 mm" &&
              acinimsiz.BendCountDisplay == "1" && acinimsiz.DxfDisplay == "açınım yok – CATIA'dan" &&
              acinimsiz.MachiningDisplay == "var" && !acinimsiz.CanApproveAsSheet,
            "no flat pattern: the measured thickness, bends and machining are shown, the DXF comes from CATIA");
        Check(acinimsiz.GroupDisplay == "—" && ((IAnalizSatiri)acinimsiz).Ayrintilar.Any(x => x.Key == "Grup" && x.Value == "—"),
            "a part that is not a sheet shows no Lazer / Şalama group");
        MontajParcaSatiri gravurlu = MontajParcaSatiri.Olustur("C:\\m.stp", olculen, analysis.Parts[4] with
        {
            MachiningPresent = true, MachiningKinds = new[] { "Engraving", "Pocket" }
        }, null, 20);
        Check(gravurlu.MachiningDisplay == "var (gravür, cep)" && ((IAnalizSatiri)gravurlu).IslemeGosterimi == "var (gravür, cep)" &&
              ((IAnalizSatiri)gravurlu).Ayrintilar.Any(x => x.Key == "İşleme" && x.Value == "var (gravür, cep, basamak, havşa / imbus başı)"),
            "engraving and a pocket show as \"var (gravür, cep)\": " + gravurlu.MachiningDisplay);
        MontajParcaSatiri gravurluSac = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1] with
        {
            MachiningPresent = true, MachiningKinds = new[] { "Engraving" }
        }, null, 20);
        Check(gravurluSac.Sekme == AnalizSekmesi.Saclar &&
              ((IAnalizSatiri)gravurluSac).Ayrintilar.Any(x => x.Key == "İşleme" && x.Value == "var (gravür); DXF'te yok"),
            "a sheet says its machining is not in its DXF");
        // Machining on every tab: a part that is no sheet shows it without the DXF note.
        MontajParcaSatiri cepliDiger = MontajParcaSatiri.Olustur("C:\\m.stp", olculen, analysis.Parts[4] with
        {
            ClassificationCode = MotorSinifKodu.NotRecognized, RecognitionEvidence = true,
            MachiningPresent = true, MachiningKinds = new[] { "Pocket" }
        }, null, 20);
        Check(cepliDiger.Sekme == AnalizSekmesi.KontrolGerekli && ((IAnalizSatiri)cepliDiger).IslemeGosterimi == "var (cep)" &&
              ((IAnalizSatiri)cepliDiger).Ayrintilar.Any(x => x.Key == "İşleme" && x.Value == "var (cep, basamak, havşa / imbus başı)"),
            "Kontrol gerekli shows the machining of a part that is not a sheet, without \"DXF'te yok\"");
        Check(IslemeMetni.Kisa(false, Array.Empty<string>()) == "—" && IslemeMetni.Kisa(true, Array.Empty<string>()) == "var" &&
              IslemeMetni.Kisa(true, new[] { "Embossing", "Engraving", "Embossing" }) == "var (kabartma, gravür)" &&
              IslemeMetni.Kisa(true, new[] { "Yeni" }) == "var" && IslemeMetni.Ayrinti(false, Array.Empty<string>(), sac: true) == "yok",
            "machining text: none, present without kinds (older output), repeated and unknown kinds");
        acinimsiz.ApproveAsSheet();
        Check(acinimsiz.Sekme == AnalizSekmesi.Saclar && acinimsiz.IsThickPlate && acinimsiz.GroupDisplay == "Şalama/Kütük",
            "approved without a flat pattern, a 30 mm part is listed under Şalama/Kütük by its measured thickness");
        MontajParcaSatiri sacDegil = MontajParcaSatiri.Olustur("C:\\m.stp", olculen with
        {
            SheetMetalAnalyses = new[]
            {
                new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, Status = "NotSheet", ThicknessMm = 2 }
            }
        }, analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.NotRecognized, RecognitionEvidence = true }, null, 20);
        // (2) A part that is no sheet shows no sheet thickness or flat size.
        Check(!sacDegil.SacGibi && sacDegil.ThicknessDisplay == "—" && sacDegil.MotorKalinlikDisplay == "—" &&
              sacDegil.OlcuGosterimiMetni == "—" && sacDegil.BendCountDisplay == "—" && sacDegil.DxfDisplay == "—",
            "a part the sheet recognizer rejected shows no sheet thickness: " + sacDegil.ThicknessDisplay);
        // Its Ölçü is its own size: a solid round bar Ø × length, a single-circle outline Ø × "thickness".
        GeometryLabAnalysisTransport milAnalizi = olculen with
        {
            ProfileRecognitions = new[]
            {
                new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, SectionRecognitionStatus = "Recognized",
                    ProfileType = "SolidCircularBar", OuterDiameterMm = 12.0, LengthSummary = new GeometryLabLengthSummaryTransport { UniformLengthMm = 80.0 } }
            }
        };
        MontajParcaSatiri mil = MontajParcaSatiri.Olustur("C:\\m.stp", milAnalizi,
            analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.SolidBar, RecognitionEvidence = true }, null, 20);
        // 330031: the length stage found no proof, the only reliable axis spans 204 mm.
        MontajParcaSatiri milEksenli = MontajParcaSatiri.Olustur("C:\\m.stp", milAnalizi with
        {
            ProfileRecognitions = new[]
            {
                new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, SectionRecognitionStatus = "Recognized",
                    ProfileType = "SolidCircularBar", OuterDiameterMm = 20.0, LengthRecognitionStatus = "InsufficientEvidence" }
            },
            ProfileGeometryAnalysis = new GeometryLabProfileGeometryAnalysisTransport
            {
                AxisCandidates = new[]
                {
                    new GeometryLabProfileAxisCandidateTransport { LocalId = 1, SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, ProjectionSpanMm = 20, Reliable = false },
                    new GeometryLabProfileAxisCandidateTransport { LocalId = 2, SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, ProjectionSpanMm = 204, Reliable = true }
                }
            }
        }, analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.SolidBar, RecognitionEvidence = true }, null, 20);
        Check(milEksenli.OlcuGosterimiMetni == "Ø20 × 204 mm", "a shaft without a proven length takes its axis span: " + milEksenli.OlcuGosterimiMetni);
        Check(mil.OlcuGosterimiMetni == "Ø12 × 80 mm" && mil.ThicknessDisplay == "—" && mil.AcinimOlcusuDisplay == "—" &&
              ((IAnalizSatiri)mil).Ayrintilar.Any(x => x.Key == "Ölçü" && x.Value == "Ø12 × 80 mm"),
            "a shaft shows Ø × length: " + mil.OlcuGosterimiMetni);
        GeometryLabAnalysisTransport baskiAnalizi = olculen with
        {
            SheetMetalAnalyses = new[]
            {
                new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, Status = "Recognized", ThicknessMm = 44,
                    FlatPattern = new GeometryLabFlatPatternTransport { Status = "Succeeded", WidthMm = 12.472, HeightMm = 12.472,
                        Segments = new[] { new GeometryLabFlatSegmentTransport { Type = "Circle" } } } }
            }
        };
        MontajParcaSatiri baski = MontajParcaSatiri.Olustur("C:\\m.stp", baskiAnalizi,
            analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.ThickerThanOutline, RecognitionEvidence = true }, null, 20);
        Check(baski.OlcuGosterimiMetni == "Ø12,5 × 44 mm" && baski.ThicknessDisplay == "—" && baski.AcinimOlcusuDisplay == "—",
            "a pin (sheet trial size, round outline) shows Ø × length: " + baski.OlcuGosterimiMetni);
        baski.ApproveAsSheet();
        Check(baski.SacGibi && baski.ThicknessDisplay == "44 mm" && baski.OlcuGosterimiMetni == "t = 44 mm",
            "approved as a sheet, the thickness is shown again");
        // (4) A turned part whose outline is arcs of one circle (359147, a ring with an inner
        // contour like 257300_Duplicate_10): Ø × thickness, the inner contour does not matter.
        GeometryLabFlatSegmentTransport Yay(string rol, double x, double y, double r) => new()
            { Type = "Arc", Role = rol, Center = new GeometryLabFlatPointTransport { X = x, Y = y }, RadiusMm = r };
        GeometryLabAnalysisTransport Disk(params GeometryLabFlatSegmentTransport[] parcalar) => olculen with
        {
            SheetMetalAnalyses = new[]
            {
                new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, Status = "Recognized", ThicknessMm = 58,
                    FlatPattern = new GeometryLabFlatPatternTransport { Status = "Succeeded", WidthMm = 119, HeightMm = 119, Segments = parcalar } }
            }
        };
        MontajParcaSatiri disk = MontajParcaSatiri.Olustur("C:\\m.stp", Disk(Yay("Outer", 59.5, 59.5, 59.5), Yay("Outer", 59.5, 59.5, 59.5),
            Yay("Inner", 59.5, 59.500000001, 40)), analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.ThickerThanMaterial, RecognitionEvidence = true }, null, 20);
        Check(disk.OlcuGosterimiMetni == "Ø119 × 58 mm" && ((IAnalizSatiri)disk).Ayrintilar.Any(x => x.Key == "Ölçü" && x.Value == "Ø119 × 58 mm"),
            "a round outline of arcs (with a hole) shows Ø × thickness: " + disk.OlcuGosterimiMetni);
        MontajParcaSatiri yuvarlakKoseli = MontajParcaSatiri.Olustur("C:\\m.stp", Disk(Yay("Outer", 10, 10, 10),
            new GeometryLabFlatSegmentTransport { Type = "Line", Role = "Outer" }), analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.ThickerThanMaterial, RecognitionEvidence = true }, null, 20);
        Check(yuvarlakKoseli.OlcuGosterimiMetni == "119 × 119 × 58 mm", "an outline with a straight edge is not round: " + yuvarlakKoseli.OlcuGosterimiMetni);
        // (3) A closed section (236948, a box profile) is no "Sac?".
        MontajParcaSatiri kutu = MontajParcaSatiri.Olustur("C:\\m.stp", olculen with
        {
            SheetMetalAnalyses = new[]
            {
                new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, Status = "NotSheet", ThicknessMm = 4, ClosedSection = true }
            }
        }, analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.NotRecognized, RecognitionEvidence = true }, null, 20);
        Check(((IAnalizSatiri)kutu).TurGosterimi == "Kapalı kesit", "a closed section's type: " + ((IAnalizSatiri)kutu).TurGosterimi);
        MontajParcaSatiri kutuSekilli = MontajParcaSatiri.Olustur("C:\\m.stp", olculen with
        {
            SheetMetalAnalyses = new[]
            {
                new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, Status = "NotSheet", ThicknessMm = 4,
                    ClosedSection = true, ClosedSectionShape = "Box" }
            }
        }, analysis.Parts[4] with
        {
            ClassificationCode = MotorSinifKodu.NotRecognized, RecognitionEvidence = true,
            ClassificationReasons = new[] { "Sac veya profil olarak tanınmadı (Kabuk kendi üzerine kapanıyor (kapalı kesit / boru); sac açınımı yok.)." }
        }, null, 20);
        Check(kutuSekilli.MotorGerekcesi == "Sac değil: kapalı kesit (kutu profil)", "236948-like box: automatic reason " + kutuSekilli.MotorGerekcesi);
        MontajParcaSatiri bos = Row(4, null);
        Check(bos.EngineCode == MotorSinifKodu.NotRecognized && !bos.EngineEvidence &&
              bos.EffectiveCategory == MontajParcaKategorisi.Tanimsiz && bos.Sekme == AnalizSekmesi.Tanimsiz,
            "an Other part with no evidence (old output, derived code) is Tanımsız");
        Check(Row(0).EffectiveCategory == MontajParcaKategorisi.Profil, "a profile part goes to the profile list");

        // Profile part as an existing profile row.
        var full = new GeometryLabProcessAdapterResult { Status = GeometryLabProcessAdapterStatus.Succeeded, Analysis = analysis };
        GeometryLabProcessAdapterResult narrowed = MontajParcaSatiri.ResultForPart(full, analysis.Parts[0]);
        Check(narrowed.Analysis!.ProfileRecognitions.Count == 1 && narrowed.Analysis.Solids.Count == 1 &&
              narrowed.Analysis.BaseStockProfile is null && !narrowed.HasMultipleProfileResults,
            "a part narrows the analysis to its own solid");
        var profileRow = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\m.stp", PartName = "Kutu", PartQuantity = 3 };
        profileRow.Apply(narrowed);
        Check(profileRow.AnalysisStatus != "Çoklu solid" && profileRow.SourceFileName == "m.stp › Kutu (3 adet)",
            "an assembly profile part is judged like a single-part STEP: " + profileRow.AnalysisStatus);
        Check(profileRow.PartDisplay == "Kutu" && profileRow.QuantityDisplay == "3",
            "assembly profile rows show part and quantity columns");
        var fileRow = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\x\01-Duz-Duz_Rep.stp" };
        fileRow.Apply(full with { Analysis = analysis with { Parts = new[] { analysis.Parts[0] with { Quantity = 1 } } } });
        Check(fileRow.PartDisplay == "01-Duz-Duz_Rep" && fileRow.QuantityDisplay == "1",
            "a single-part STEP row shows its file name as part and the STEP quantity");

        var snapshot = new CatiaScanSnapshot
        {
            CreatedAtUtc = DateTime.UtcNow, ScanId = "s",
            Items = new[] { new CatiaScanSnapshotItem("Sac A_Rep", "p", "r", "k", 2, true), new CatiaScanSnapshotItem("Diger", "p", "r", "k", 1, false) }
        };
        Check(CatiaStepMatcher.MatchPartName(snapshot, "Sac A").Count == 1 && CatiaStepMatcher.MatchPartName(snapshot, "sac a_rep").Count == 1 &&
              CatiaStepMatcher.MatchPartName(snapshot, "Yok").Count == 0, "CATIA rows are matched by part name");
    }

    // A marked/processed box profile inside an assembly: the section is not
    // uniform, the part's own base stock recognizes it; the profile row must
    // show it as a processed profile, as for a single-part STEP.
    private static void AssemblyProcessedProfilePart()
    {
        var baseStock = new GeometryLabBaseStockProfileTransport
        {
            Status = "Recognized", ProfileType = "RectangularHollowSection", AxisCandidateId = 3,
            OuterWidthMm = 60, OuterHeightMm = 40, InnerWidthMm = 54, InnerHeightMm = 34, WallThicknessMm = 3,
            StableSectionRegions = new[] { new GeometryLabStableSectionRegionTransport { LengthMm = 100 },
                new GeometryLabStableSectionRegionTransport { LengthMm = 100 } }
        };
        var analysis = new GeometryLabAnalysisTransport
        {
            SchemaVersion = "1.2",
            Status = "Succeeded",
            Solids = new[] { new GeometryLabSolidTransport(), new GeometryLabSolidTransport() },
            ProfileRecognitions = new[]
            {
                new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 1 },
                    Status = "Succeeded", SectionRecognitionStatus = "Ambiguous", ProfileType = "Unknown" },
                new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 2 },
                    Status = "Succeeded", SectionRecognitionStatus = "Recognized", ProfileType = "SquareHollowSection" }
            },
            // Whole-shape aggregate of a multi-solid analysis: no base stock.
            BaseStockProfile = new GeometryLabBaseStockProfileTransport { Status = "InsufficientEvidence" },
            ModificationAnalysis = new GeometryLabModificationAnalysisTransport { Status = "Unknown" },
            BaseStockProfiles = new[]
            {
                new GeometryLabSolidBaseStockTransport
                {
                    SolidId = new GeometryLabLocalIdTransport { LocalId = 1 }, BaseStockProfile = baseStock,
                    ModificationAnalysis = new GeometryLabModificationAnalysisTransport { Status = "LocallyModified" }
                },
                new GeometryLabSolidBaseStockTransport
                {
                    SolidId = new GeometryLabLocalIdTransport { LocalId = 2 },
                    BaseStockProfile = new GeometryLabBaseStockProfileTransport { Status = "InsufficientEvidence" },
                    ModificationAnalysis = new GeometryLabModificationAnalysisTransport { Status = "Unknown" }
                }
            },
            Parts = new[]
            {
                Part(1, "55RS100111-13", 1, "Profile", false, "RectangularHollowSection", null),
                Part(2, "Kutu", 2, "Profile", false, "SquareHollowSection", null)
            }
        };
        var full = new GeometryLabProcessAdapterResult { Status = GeometryLabProcessAdapterStatus.Succeeded, Analysis = analysis };
        GeometryLabProcessAdapterResult narrowed = MontajParcaSatiri.ResultForPart(full, analysis.Parts[0]);
        Check(narrowed.Analysis!.BaseStockProfile?.Status == "Recognized" &&
              narrowed.Analysis.ModificationAnalysis?.Status == "LocallyModified",
            "the part's own base stock replaces the multi-solid aggregate");
        var row = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\m.stp", PartName = "55RS100111-13", PartQuantity = 1 };
        row.Apply(narrowed);
        Check(row.AutomaticCategory == GeometryLabExternalStepResultGroup.ProcessedProfile &&
              row.ResultGroup == GeometryLabExternalStepResultGroup.DefiniteProfile && row.OperationDisplay == "Lokal işlemli",
            "a processed profile inside an assembly is listed as a processed profile: " + row.ResultGroup + " / " + row.AnalysisStatus);
        Check(MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[0], null, 20).EffectiveCategory ==
              MontajParcaKategorisi.Profil, "a processed profile part goes to the Profiller tab");
        GeometryLabProcessAdapterResult plain = MontajParcaSatiri.ResultForPart(full, analysis.Parts[1]);
        Check(plain.Analysis!.BaseStockProfile?.Status == "InsufficientEvidence",
            "a plain profile part keeps its own (unrecognized) base stock");
    }

    private static void MotorDxfExport()
    {
        string root = Path.Combine(_root, "motor-dxf-export");
        string source = Path.Combine(root, "kaynak");
        Directory.CreateDirectory(source);
        string a = Path.Combine(source, "part-2.dxf");
        string b = Path.Combine(source, "part-3.dxf");
        File.WriteAllText(a, "A");
        File.WriteAllText(b, "B");
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        string aCut = Path.Combine(source, "part-2-kesim.dxf");
        File.WriteAllText(aCut, "A-KESIM");
        MontajParcaSatiri sheetA = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], a, 20, aCut);
        MontajParcaSatiri thick = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[2], b, 20);
        MontajParcaSatiri pending = MontajParcaSatiri.Olustur("C:\\m.stp", analysis, analysis.Parts[1], a, 20);
        sheetA.ApproveAsSheet();
        thick.ApproveAsSheet();

        string target = Path.Combine(root, "hedef");
        IReadOnlyList<MotorDxfIsi> plan = MotorDxfAktarici.Planla(new[] { sheetA, thick, pending }, target);
        Check(plan.Count == 2, "only approved sheet rows are exported");
        IReadOnlyList<MotorDxfIsi> cutPlan = MotorDxfAktarici.Planla(new[] { sheetA, thick }, target, bukumBilgisi: false);
        Check(cutPlan.Count == 1 && cutPlan[0].Kaynak == aCut && cutPlan[0].Hedef == plan[0].Hedef,
            "without bend information the cut-only DXF is exported under the same name; a row without one is not");
        Check(plan[0].Hedef == Path.Combine(Path.GetFullPath(target), "Motor-DXF", "Sac A_20mm_2adet.dxf") &&
              plan[1].Hedef.EndsWith("Kalin Sac_20.5mm_1adet.dxf", StringComparison.Ordinal),
            "targets are <target>\\Motor-DXF\\<DxfAdi>: " + plan[0].Hedef);

        int asked = 0;
        MotorDxfAktarimSonucu first = MotorDxfAktarici.Uygula(plan, _ => { asked++; return (DxfCakismaSecimi.Atla, false); });
        Check(first.Yazilan.Count == 2 && asked == 0 && File.ReadAllText(plan[0].Hedef) == "A", "new files are written without asking");

        File.WriteAllText(a, "A2");
        MotorDxfAktarimSonucu skipped = MotorDxfAktarici.Uygula(plan, _ => { asked++; return (DxfCakismaSecimi.Atla, false); });
        Check(asked == 2 && skipped.Atlanan.Count == 2 && File.ReadAllText(plan[0].Hedef) == "A",
            "each existing file is asked for; Atla keeps it");
        asked = 0;
        MotorDxfAktarimSonucu overwritten = MotorDxfAktarici.Uygula(plan, _ => { asked++; return (DxfCakismaSecimi.UzerineYaz, true); });
        Check(asked == 1 && overwritten.Yazilan.Count == 2 && File.ReadAllText(plan[0].Hedef) == "A2",
            "Üzerine yaz with 'apply to all' asks once and replaces both");
        Check(!Directory.GetFiles(Path.GetDirectoryName(plan[0].Hedef)!, "*.tmp-*").Any(), "no temporary file is left");
        MotorDxfAktarimSonucu cancelled = MotorDxfAktarici.Uygula(plan, _ => (DxfCakismaSecimi.Iptal, false));
        Check(cancelled.IptalEdildi && cancelled.Yazilan.Count == 0, "İptal stops the export");

        File.Delete(b);
        MotorDxfAktarimSonucu missing = MotorDxfAktarici.Uygula(plan, _ => (DxfCakismaSecimi.Atla, true));
        Check(missing.Hatali.Count == 1 && missing.Atlanan.Count == 1, "a missing engine DXF is reported as an error");
    }

    private static async Task CancellationAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("cancel", SlowEngineScript()),
            TimeSpan.FromSeconds(10)).AnalyzeAsync(_step, cancellation.Token);
        Check(result.Status == GeometryLabProcessAdapterStatus.Cancelled, "caller cancellation returns Cancelled");
        Check(result.TemporaryDirectoryCleaned, "cancelled temporary directory is cleaned");
    }

    private static async Task MultipleProfilesAsync()
    {
        const string json = "{\"schemaVersion\":\"1.0\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"profileRecognition\":{\"profileType\":\"Unknown\"},\"profileRecognitions\":[{\"profileType\":\"SquareHollowSection\"},{\"profileType\":\"SolidCircularBar\"}]}";
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("multiple", "echo " + json + ">\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded && result.HasMultipleProfileResults,
            "multiple profile results are exposed without automatic selection");
        Check(result.Analysis?.ProfileRecognitions.Count == 2 && result.Analysis.ProfileRecognition?.ProfileType == "Unknown",
            "aggregate profile result is not replaced by an arbitrary solid");
    }

    private static void StepProfileListFormatting()
    {
        var item = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\02-Duz-45.stp" };
        item.MarkAnalyzing();
        Check(item.AnalysisStatus == "Analiz ediliyor", "external STEP item exposes its queued progress state");

        item.Apply(RecognizedResult(new GeometryLabProfileRecognitionTransport
        {
            Status = "Succeeded",
            SectionRecognitionStatus = "Recognized",
            LengthRecognitionStatus = "Recognized",
            CutRecognitionStatus = "Recognized",
            ProfileType = "SquareHollowSection",
            OuterWidthMm = 40,
            OuterHeightMm = 40,
            WallThicknessMm = 2,
            LengthCandidates = new GeometryLabLengthCandidatesTransport
            {
                MeasurementStatus = "Valid",
                AxialCenterlineLengthMm = 65,
                OuterContourMinimumLengthMm = 45,
                OuterContourMaximumLengthMm = 85
            },
            LengthSummary = new GeometryLabLengthSummaryTransport
            {
                MeasurementStatus = "Valid",
                Classification = "VariableByCut",
                ShortLengthMm = 45,
                CenterLengthMm = 65,
                LongLengthMm = 85
            },
            EndCutCandidates = new[]
            {
                new GeometryLabEndCutCandidateTransport { End = "NegativeAxisEnd", MeasurementStatus = "Valid", CutAngleDegrees = 45 },
                new GeometryLabEndCutCandidateTransport { End = "PositiveAxisEnd", MeasurementStatus = "Valid", CutAngleDegrees = 0 }
            }
        }, ReliableAxis(85)));
        Check(item.AnalysisStatus == "Tanındı" && item.Eligibility == GeometryLabProfileListEligibility.Eligible && item.ProfileType == "Kare Kutu Profil", "eligible gate accepts one successful hollow profile with valid length and two valid cuts");
        Check(item.SectionDisplay == "40 × 40 × 2 mm", "section formatter uses verified outer dimensions and wall thickness");
        Check(item.LengthDisplay == "85 mm" && item.TopologyDisplay == "Kısa 45 / Merkez 65 / Uzun 85 mm",
            "physical axis span is Boy while variable length summary is topology information");
        Check(item.CutDisplay == "45° / 0°", "cut formatter retains native NegativeAxisEnd PositiveAxisEnd order");
        Check(item.RawLengthCandidates?.OuterContourMinimumLengthMm == 45, "raw length candidates are retained outside the normal display");

        var incomplete = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\missing-length.stp" };
        incomplete.Apply(RecognizedResult(new GeometryLabProfileRecognitionTransport
        {
            Status = "Succeeded", SectionRecognitionStatus = "Recognized", LengthRecognitionStatus = "InsufficientEvidence",
            CutRecognitionStatus = "InsufficientEvidence", ProfileType = "CircularHollowSection", OuterDiameterMm = 60, WallThicknessMm = 3
        }));
        Check(incomplete.AnalysisStatus == "İnceleme gerekli" && incomplete.Eligibility == GeometryLabProfileListEligibility.ReviewRequired && incomplete.ProfileType == "Boru" && incomplete.SectionDisplay == "Ø60 × 3 mm" &&
              incomplete.LengthDisplay == "—" && incomplete.CutDisplay == "—",
            "recognized hollow section with missing length or cuts requires review but keeps technical evidence");

        var solid = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\solid-prism.stp" };
        solid.Apply(RecognizedResult(new GeometryLabProfileRecognitionTransport
        {
            Status = "Succeeded", SectionRecognitionStatus = "Recognized", LengthRecognitionStatus = "Recognized",
            CutRecognitionStatus = "Recognized", ProfileType = "SolidRectangularBar", OuterWidthMm = 69.162, OuterHeightMm = 13,
            LengthSummary = new GeometryLabLengthSummaryTransport { MeasurementStatus = "Valid", Classification = "Uniform", UniformLengthMm = 86.555 },
            EndCutCandidates = new[] { new GeometryLabEndCutCandidateTransport { MeasurementStatus = "Valid", CutAngleDegrees = 0 }, new GeometryLabEndCutCandidateTransport { MeasurementStatus = "Valid", CutAngleDegrees = 0 } }
        }));
        Check(solid.Eligibility == GeometryLabProfileListEligibility.ReviewRequired && solid.AnalysisStatus == "İnceleme gerekli" && solid.ProfileType == "Dikdörtgen Lama" &&
              solid.FailureReason.Contains("B-Rep", StringComparison.Ordinal),
            "solid geometric prisms never become automatic production-profile rows");

        var multiple = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\multi.stp" };
        multiple.Apply(new GeometryLabProcessAdapterResult
        {
            Status = GeometryLabProcessAdapterStatus.Succeeded,
            Analysis = new GeometryLabAnalysisTransport { SchemaVersion = "1.0", Status = "Succeeded", ProfileRecognitions = new[]
            {
                new GeometryLabProfileRecognitionTransport { ProfileType = "SquareHollowSection" },
                new GeometryLabProfileRecognitionTransport { ProfileType = "SolidCircularBar" }
            }}
        });
        Check(multiple.AnalysisStatus == "Çoklu solid" && multiple.Eligibility == GeometryLabProfileListEligibility.Excluded && multiple.SectionDisplay == "—" && multiple.LengthDisplay == "—",
            "multiple solids never select an arbitrary profile result");

        var schema10 = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\schema10.stp" };
        schema10.Apply(RecognizedResult(new GeometryLabProfileRecognitionTransport { ProfileType = "SquareHollowSection" }));
        GeometryLabProcessAdapterResult schema11Result = RecognizedResult(new GeometryLabProfileRecognitionTransport { ProfileType = "SquareHollowSection" });
        var schema11 = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\schema11.stp" };
        schema11.Apply(schema11Result with { Analysis = schema11Result.Analysis! with { SchemaVersion = "1.1" } });
        Check(schema11.EvidenceStatus != "Şema doğrulanamadı" && schema11.AnalysisStatus == schema10.AnalysisStatus &&
              schema11.Eligibility == schema10.Eligibility && schema11.SectionDisplay == schema10.SectionDisplay,
            "a schema 1.1 result is listed exactly like the same schema 1.0 result");
    }

    private static GeometryLabProcessAdapterResult RecognizedResult(
        GeometryLabProfileRecognitionTransport profile,
        GeometryLabProfileGeometryAnalysisTransport? geometry = null) => new()
    {
        Status = GeometryLabProcessAdapterStatus.Succeeded,
        Analysis = new GeometryLabAnalysisTransport
        {
            SchemaVersion = "1.0",
            Status = "Succeeded",
            ProfileRecognitions = new[] { profile },
            ProfileGeometryAnalysis = geometry
        }
    };

    private static GeometryLabProfileGeometryAnalysisTransport ReliableAxis(double span) => new()
    {
        AxisDetectionStatus = "Determined",
        AxisCandidates = new[] { new GeometryLabProfileAxisCandidateTransport { LocalId = 1, Reliable = true, ProjectionSpanMm = span } }
    };

    private static void BaseStockFallbackFormatting()
    {
        var local = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\55RS100111-13 A_tek-delik.stp" };
        local.Apply(BaseStockResult("LocallyModified"));
        Check(local.AnalysisStatus == "Tanındı" && local.Eligibility == GeometryLabProfileListEligibility.Eligible &&
              local.ProfileType == "Dikdörtgen Kutu Profil" && local.SectionDisplay == "60 × 40 × 3 mm",
            "recognized single-solid local base stock becomes a processed hollow profile row");
        Check(local.OperationDisplay == "Lokal işlemli" && local.LengthDisplay == "—" && local.CutDisplay == "—",
            "base-stock fallback never invents unavailable normal length or cut results");
        Check(local.OperationToolTip == "Aynı profil kesitini taşıyan temiz bölgeler arasında sınırlı bir geometrik değişiklik bulundu.",
            "locally modified base stock has the requested evidence-only operation tooltip");
        Check(local.RawBaseStockProfile?.StableSectionRegions.Count == 2 &&
              local.RawModificationAnalysis?.ModifiedAxisIntervals.Count == 1,
            "base-stock and modification raw technical evidence is retained outside the list");

        var baseWithAxis = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\base-with-axis.stp" };
        baseWithAxis.Apply(BaseStockResult("LocallyModified", axisSpanMm: 700));
        Check(baseWithAxis.LengthDisplay == "700 mm" && baseWithAxis.TopologyDisplay == "—",
            "processed base stock shows only the reliable JSON physical axis span as Boy");

        var incompleteNormalMeasurements = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\base-with-incomplete-normal-measurements.stp" };
        incompleteNormalMeasurements.Apply(BaseStockResult("LocallyModified", new GeometryLabProfileRecognitionTransport
        {
            Status = "Succeeded", SectionRecognitionStatus = "Ambiguous", LengthRecognitionStatus = "InsufficientEvidence",
            CutRecognitionStatus = "InsufficientEvidence", ProfileType = "Unknown",
            LengthSummary = new GeometryLabLengthSummaryTransport { MeasurementStatus = "Valid", Classification = "Uniform", UniformLengthMm = 85 },
            EndCutCandidates = new[]
            {
                new GeometryLabEndCutCandidateTransport { MeasurementStatus = "Valid", CutAngleDegrees = 0 },
                new GeometryLabEndCutCandidateTransport { MeasurementStatus = "Valid", CutAngleDegrees = 0 }
            }
        }));
        Check(incompleteNormalMeasurements.AnalysisStatus == "Tanındı" &&
              incompleteNormalMeasurements.LengthDisplay == "—" && incompleteNormalMeasurements.CutDisplay == "—",
            "base-stock rows require recognized normal length and cut statuses before displaying either value");

        var dense = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\55RS100111-13 A.stp" };
        dense.Apply(BaseStockResult("ExtensivelyModified"));
        Check(dense.AnalysisStatus == "Tanındı" && dense.OperationDisplay == "Yoğun işlemli" &&
            dense.Eligibility == GeometryLabProfileListEligibility.Eligible,
            "recognized extensive base stock becomes a processed hollow profile row");
        Check(dense.OperationToolTip == "Profil boyunca birden fazla veya geniş geometrik değişiklik bulundu; temel stok kesiti temiz bölgelerden tanındı.",
            "extensively modified base stock has the requested evidence-only operation tooltip");

        var normalPriority = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\normal-priority.stp" };
        normalPriority.Apply(BaseStockResult("LocallyModified", NormalRecognizedProfile()));
        Check(normalPriority.AnalysisStatus == "Tanındı" && normalPriority.ProfileType == "Kare Kutu Profil" &&
              normalPriority.OperationDisplay == "—" && normalPriority.RawBaseStockProfile == null,
            "a definite normal profileRecognition always takes priority over base stock");
        Check(normalPriority.OperationToolTip == "",
            "normal profile rows do not receive an operation tooltip");

        var missingModification = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\missing-modification.stp" };
        missingModification.Apply(BaseStockResult(null));
        Check(missingModification.Eligibility == GeometryLabProfileListEligibility.Excluded && missingModification.AnalysisStatus == "Tanımsız",
            "recognized base stock without a supported modification conclusion is not listed");

        var multiSolid = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\multi-base.stp" };
        GeometryLabProcessAdapterResult multi = BaseStockResult("LocallyModified");
        multiSolid.Apply(multi with { Analysis = multi.Analysis! with { Solids = new[] { new GeometryLabSolidTransport(), new GeometryLabSolidTransport() } } });
        Check(multiSolid.Eligibility == GeometryLabProfileListEligibility.Excluded && multiSolid.AnalysisStatus == "Tanımsız",
            "base-stock fallback never selects a result from multiple solids");
    }

    private static void BaseStockTransportDeserialization()
    {
        const string json = "{\"schemaVersion\":\"1.0\",\"status\":\"Succeeded\",\"solids\":[{}],\"profileRecognitions\":[{\"status\":\"Succeeded\",\"profileType\":\"Unknown\"}],\"baseStockProfile\":{\"status\":\"Recognized\",\"profileType\":\"RectangularHollowSection\",\"outerWidthMm\":60,\"outerHeightMm\":40,\"innerWidthMm\":54,\"innerHeightMm\":34,\"wallThicknessMm\":3,\"stableSectionRegions\":[{\"lengthMm\":100}]},\"modificationAnalysis\":{\"status\":\"LocallyModified\",\"modifiedAxisIntervals\":[{\"lengthMm\":30}]}}";
        GeometryLabAnalysisTransport? parsed = JsonSerializer.Deserialize<GeometryLabAnalysisTransport>(json);
        Check(parsed?.BaseStockProfile?.ProfileType == "RectangularHollowSection" &&
              parsed.BaseStockProfile.StableSectionRegions.Count == 1 &&
              parsed.ModificationAnalysis?.Status == "LocallyModified" &&
              parsed.ModificationAnalysis.ModifiedAxisIntervals.Count == 1,
            "optional 4D base-stock JSON fields deserialize without changing schema 1.0");

        GeometryLabAnalysisTransport? legacy = JsonSerializer.Deserialize<GeometryLabAnalysisTransport>("{\"schemaVersion\":\"1.0\",\"status\":\"Succeeded\"}");
        Check(legacy?.BaseStockProfile == null && legacy?.ModificationAnalysis == null && legacy?.Solids.Count == 0,
            "older schema 1.0 JSON without 4D optional fields remains compatible");
    }

    private static void ExternalStepFilterFormatting()
    {
        var definite = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\definite.stp" };
        definite.Apply(RecognizedResult(NormalRecognizedProfile(), ReliableAxis(85)));
        var processed = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\processed.stp" };
        processed.Apply(BaseStockResult("LocallyModified"));
        var review = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\review.stp" };
        review.Apply(RecognizedResult(new GeometryLabProfileRecognitionTransport
        {
            Status = "Succeeded", SectionRecognitionStatus = "Recognized", LengthRecognitionStatus = "InsufficientEvidence",
            CutRecognitionStatus = "InsufficientEvidence", ProfileType = "CircularHollowSection", OuterDiameterMm = 60, WallThicknessMm = 3
        }));
        var excluded = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\excluded.stp" };
        excluded.Apply(new GeometryLabProcessAdapterResult { Status = GeometryLabProcessAdapterStatus.EngineFailed, Message = "engine failed" });
        var unclassified = new GeometryLabStepProfileListItem { SourceStepPath = @"C:\\tests\\unclassified.stp" };
        unclassified.Apply(RecognizedResult(new GeometryLabProfileRecognitionTransport
        {
            Status = "Succeeded", SectionRecognitionStatus = "InsufficientEvidence", LengthRecognitionStatus = "NotStarted",
            CutRecognitionStatus = "NotStarted", ProfileType = "Unknown", RejectionReason = "Stable representative full sections are unavailable."
        }));
        GeometryLabStepProfileListItem[] rows = { definite, processed, review, excluded, unclassified };
        Check(processed.AutomaticCategory == GeometryLabExternalStepResultGroup.ProcessedProfile &&
              processed.EffectiveCategory == GeometryLabExternalStepResultGroup.DefiniteProfile &&
              rows.Select(x => x.ResultGroup).Distinct().Count() == 4,
            "processed base stock retains its automatic origin but shares the definite profile filter");

        Check(definite.Sekme == AnalizSekmesi.Profiller && processed.Sekme == AnalizSekmesi.Profiller &&
              review.Sekme == AnalizSekmesi.KontrolGerekli && unclassified.Sekme == AnalizSekmesi.Tanimsiz,
            "definite and processed profiles are on Profiller, review on Kontrol gerekli, a file row without a profile on Tanımsız");
        Check(unclassified.ExplanationDisplay == "Kararlı ve tam profil kesiti bulunamadı." &&
              !unclassified.ExplanationDisplay.Contains("Stable representative", StringComparison.Ordinal),
            "user-facing explanations translate known engine reasons and never expose the English text");

        review.MoveToReview();
        Check(review.AutomaticCategory == GeometryLabExternalStepResultGroup.ReviewRequired &&
              review.EffectiveCategory == GeometryLabExternalStepResultGroup.ReviewRequired &&
              review.DecisionSource == GeometryLabDecisionSource.User && review.HasUserDecision &&
              review.DecisionToolTip.Contains("Karar kaynağı: Kullanıcı", StringComparison.Ordinal),
            "a user decision preserves the automatic category and exposes its source");
        Check(review.Sekme == AnalizSekmesi.KontrolGerekli && review.KullaniciKarari == MacriaProje.KararKontrole,
            "a profile moved to review is listed under Kontrol gerekli");
        review.ExcludeFromList("Teknik inceleme sonrası");
        Check(review.ListeDisi && review.Sekme == AnalizSekmesi.ListeDisi && review.ListeDisiNotu == "Teknik inceleme sonrası" &&
              review.EffectiveCategory == GeometryLabExternalStepResultGroup.ReviewRequired && review.HasUserDecision &&
              review.AciklamaGosterimi.Contains("Teknik inceleme sonrası"),
            "Liste dışı is a flag over the row's decision, with its note");
        review.ListeyeGeriAl();
        Check(!review.ListeDisi && review.Sekme == AnalizSekmesi.KontrolGerekli, "Geri Al returns the profile to Kontrol gerekli");
        review.RestoreAutomaticDecision();
        Check(review.EffectiveCategory == GeometryLabExternalStepResultGroup.ReviewRequired &&
              review.DecisionSource == GeometryLabDecisionSource.Automatic && review.UserDecisionNote == "" &&
              review.AnalysisStatus == "İnceleme gerekli",
            "restore automatic decision reinstates the original session presentation without rerunning the engine");
        unclassified.ConfirmManualHollowProfile("Boru", "Ø60 × 3 mm");
        Check(unclassified.EffectiveCategory == GeometryLabExternalStepResultGroup.DefiniteProfile &&
              unclassified.ProfileType == "Boru" && unclassified.SectionDisplay == "Ø60 × 3 mm" &&
              unclassified.OriginalAutomaticReason == "Kararlı ve tam profil kesiti bulunamadı.",
            "manual hollow confirmation supplies only the session presentation while retaining the automatic evidence");
    }

    private static void CatiaComparisonSyntheticTests()
    {
        var snapshot = new CatiaScanSnapshot { CreatedAtUtc = DateTime.UtcNow, ScanId = "synthetic", Items = new[]
        {
            new CatiaScanSnapshotItem("01-Duz-Duz", "PLM-01", "A", "key-01", 4, false),
            new CatiaScanSnapshotItem("Sac-01", "PLM-S", "A", "key-s", 2, true),
            new CatiaScanSnapshotItem("Duplicate", "PLM-D1", "A", "key-d1", 1, false),
            new CatiaScanSnapshotItem("Duplicate", "PLM-D2", "A", "key-d2", 1, false)
        }};
        Check(CatiaStepMatcher.NormalizeFileIdentity("01-Duz-Duz_Rep.stp") == "01-Duz-Duz", "6A-01 _Rep normalization");
        Check(CatiaStepMatcher.NormalizeFileIdentity("  A.step ") == "A", "6A-02 extension and trim normalization");
        Check(CatiaStepMatcher.Match(snapshot, "01-Duz-Duz_Rep.stp").Count == 1, "6A-03 exact title match");
        Check(CatiaStepMatcher.Match(snapshot, "01-duz-duz.STEP").Count == 1, "6A-04 case insensitive match");
        Check(CatiaStepMatcher.Match(snapshot, "missing.stp").Count == 0, "6A-05 no match");
        Check(CatiaStepMatcher.Match(snapshot, "Duplicate_Rep.stp").Count == 2, "6A-06 ambiguous candidates retained");
        var item = new GeometryLabStepProfileListItem { SourceStepPath = "Sac-01_Rep.stp" }; item.MarkAnalyzing();
        item.ApplyCatiaComparison(CatiaStepMatcher.Match(snapshot, item.SourceStepPath));
        Check(item.CatiaMatchDisplay == "Eşleşti", "6A-07 single match display");
        Check(item.CatiaQuantityDisplay == "2", "6A-08 quantity transfer");
        Check(item.DecisionSource == GeometryLabDecisionSource.ThreeDScan, "6A-09 confirmed sheet decision source");
        Check(item.EffectiveCategory == GeometryLabExternalStepResultGroup.Excluded, "6A-10 confirmed sheet excluded");
        Check(item.EffectiveStatusDisplay == "Sac (CATIA)" && item.Sekme == AnalizSekmesi.KontrolGerekli,
            "6A-10b a profile row CATIA calls a sheet is shown as such under Kontrol gerekli");
        Check(item.EffectiveProfileTypeDisplay == "Sac Parça" && item.ProfileType == "—", "6A-10c CATIA sheet type is visible without overwriting technical type");
        var unknown = new GeometryLabStepProfileListItem { SourceStepPath = "01-Duz-Duz_Rep.stp" }; unknown.MarkAnalyzing(); unknown.ApplyCatiaComparison(CatiaStepMatcher.Match(snapshot, unknown.SourceStepPath));
        Check(unknown.DecisionSource != GeometryLabDecisionSource.ThreeDScan, "6A-11 false does not decide");
        Check(unknown.EffectiveCategory == GeometryLabExternalStepResultGroup.Unclassified, "6A-12 false preserves geometry category");
        var ambiguous = new GeometryLabStepProfileListItem { SourceStepPath = "Duplicate.stp" }; ambiguous.MarkAnalyzing(); ambiguous.ApplyCatiaComparison(CatiaStepMatcher.Match(snapshot, ambiguous.SourceStepPath));
        Check(ambiguous.CatiaMatchDisplay == "Eşleme belirsiz", "6A-13 ambiguity display");
        Check(ambiguous.DecisionSource == GeometryLabDecisionSource.Automatic, "6A-14 ambiguity no decision");
        var none = new GeometryLabStepProfileListItem { SourceStepPath = "none.stp" }; none.MarkAnalyzing(); none.ApplyCatiaComparison(CatiaStepMatcher.Match(snapshot, none.SourceStepPath));
        Check(none.CatiaMatchDisplay == "Eşleşmedi", "6A-15 unmatched display");
        Check(none.CatiaQuantityDisplay == "—", "6A-16 unmatched quantity blank");
        item.MoveToReview(); Check(item.HasUserDecision, "6A-17 user decision exists");
        item.ApplyCatiaComparison(CatiaStepMatcher.Match(snapshot, item.SourceStepPath)); Check(item.CatiaMatchDisplay == "Kullanıcı kararı korundu", "6A-18 user wins");
        item.RestoreAutomaticDecision(); Check(item.DecisionSource == GeometryLabDecisionSource.ThreeDScan, "6A-19 restore CATIA priority");
        Check(item.EffectiveProfileTypeDisplay == "Sac Parça", "6A-19b restore CATIA sheet type");
        unknown.MarkAnalyzing(); Check(unknown.CatiaMatchDisplay == "CATIA taraması yok", "6A-20 new analysis clears comparison");
        Check(snapshot.Items.All(x => x.Quantity is > 0), "6A-21 valid occurrence quantity");
        Check(snapshot.Items.All(x => !string.IsNullOrWhiteSpace(x.ReferenceKey)), "6A-22 snapshot identity present");
    }

    private static void DxfDwgInventoryTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "Macria-DxfDwg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string sub = Path.Combine(root, "sub"); Directory.CreateDirectory(sub);
        string dxf = Path.Combine(root, " Sac-01.dxf"); string dwg = Path.Combine(root, "plain.DWG"); string duplicate = Path.Combine(sub, "Sac-01.dwg");
        File.WriteAllText(dxf, "dxf"); File.WriteAllText(dwg, "dwg"); File.WriteAllText(duplicate, "dwg2"); File.WriteAllText(Path.Combine(root, "skip.txt"), "x");
        try
        {
            var top = DxfDwgFileInventory.Scan(root, false); Check(top.Count == 2, "6B-01 top-level DXF/DWG only");
            Check(top.Any(x => x.FileType == "DXF") && top.Any(x => x.FileType == "DWG"), "6B-02 extension inventory");
            var all = DxfDwgFileInventory.Scan(root, true); Check(all.Count == 3, "6B-03 recursive option");
            Check(all.First(x => x.FileName.TrimStart() == "Sac-01.dxf").MatchState == DxfDwgMatchState.Duplicate, "6B-04 duplicate safety");
            var snapshot = new CatiaScanSnapshot { CreatedAtUtc = DateTime.UtcNow, ScanId = "dxf", Items = new[] { new CatiaScanSnapshotItem("plain", "", "", "p", 3, true) } };
            var item = top.Single(x => x.FileName == "plain.DWG"); item.Apply(snapshot, false);
            Check(item.MatchState == DxfDwgMatchState.Matched && item.CatiaQuantity == 3, "6B-05 single CATIA match quantity");
            Check(item.Explanation.StartsWith("Sac parça", StringComparison.Ordinal), "6B-06 sheet confirmation text");
            Check(DxfDwgFileInventory.Normalize("  plain.DWG ") == "plain", "6B-07 normalization");
            Check(!all.Any(x => x.FileName == "skip.txt"), "6B-08 unsupported excluded");
            Check(Enum.GetValues<DxfDwgMatchState>().All(DxfDwgFileInventory.DefaultFilterVisible), "6B1-00 all DXF/DWG filters default to visible");

            var productionSnapshot = new CatiaScanSnapshot
            {
                CreatedAtUtc = DateTime.UtcNow,
                ScanId = "dxf-production",
                Items = new[]
                {
                    new CatiaScanSnapshotItem("55RS100111-12", "", "", "12", 2, false),
                    new CatiaScanSnapshotItem("55RS100111-1", "", "", "1", 1, false),
                    new CatiaScanSnapshotItem("55RS100111-2", "", "", "2", 1, false)
                }
            };
            Check(DxfDwgFileInventory.MatchKey("55RS100111-12_20mm_2adet.dxf") == "55RS100111-12", "6B1-01 chained production suffix removed");
            Check(DxfDwgFileInventory.MatchKey("55RS100111-1_13_mm_1_adet.dxf") == "55RS100111-1", "6B1-02 separator variants removed");
            Check(DxfDwgFileInventory.MatchKey("55RS100111-2_1,5mm.dxf") == "55RS100111-2", "6B1-03 comma thickness removed");
            Check(DxfDwgFileInventory.Match(productionSnapshot, "55RS100111-12_20MM_2ADET.dxf").Items.Count == 1, "6B1-04 case-insensitive suffix match");
            Check(DxfDwgFileInventory.MatchKey("55RS100111-12-20-middle.dxf") == "55RS100111-12-20-middle", "6B1-05 middle numbers retained");
            Check(DxfDwgFileInventory.MatchKey("55RS100111-12_custom.dxf") == "55RS100111-12_custom", "6B1-06 unknown suffix retained");
            DxfDwgMatchResult productionMatch = DxfDwgFileInventory.Match(productionSnapshot, "55RS100111-1_13mm_1adet.dxf");
            Check(productionMatch.Items.Count == 1 && productionMatch.UsedProductionSuffix, "6B1-07 suffix match reports its evidence");
            var ambiguousSnapshot = new CatiaScanSnapshot { CreatedAtUtc = DateTime.UtcNow, ScanId = "dxf-ambiguous", Items = new[] { new CatiaScanSnapshotItem("55RS100111-2", "", "", "a", 1, false), new CatiaScanSnapshotItem("55RS100111-2", "", "", "b", 1, false) } };
            Check(DxfDwgFileInventory.Match(ambiguousSnapshot, "55RS100111-2_8mm_1adet.dxf").Items.Count == 2, "6B1-08 suffix ambiguity preserved");
            byte[] before = File.ReadAllBytes(dxf);
            _ = DxfDwgFileInventory.Scan(root, false);
            Check(before.SequenceEqual(File.ReadAllBytes(dxf)), "6B1-09 inventory scan does not alter the DXF input");
        }
        finally { Directory.Delete(root, true); }
    }

    // C Aşama 2: "Profilleri STEP olarak yaz" names, quantities, clashes, results and the Excel sheets.
    private static void ProfilStepTests()
    {
        // Part number: productId; the name when the productId is a CAD default; the file for a lone part.
        Check(ProfilStepAdi.ParcaNo("236948", "236948") == ("236948", "productId"), "C2-01 productId is the part number");
        Check(ProfilStepAdi.ParcaNo("3D Shape00000338A", "55RS100111-13 Kutu") == ("55RS100111-13 Kutu", "parça adı (productId anlamsız: 3D Shape00000338A)"),
            "C2-02 a CAD default productId gives way to the part name");
        Check(ProfilStepAdi.ParcaNo("", "Kutu 100") == ("Kutu 100", "parça adı (productId yok)"), "C2-03 no productId: the name");
        Check(ProfilStepAdi.ParcaNo("3D Shape00000338A", "3D Shape00000338A", "55RS100111-13").No == "55RS100111-13" &&
              ProfilStepAdi.ParcaNo("Part1", "Body", null) == ("Part1", "productId (ad da anlamsız)"),
            "C2-04 both defaults: the lone part's file name, else the productId");
        foreach (string anlamsiz in new[] { "3D Shape00000338A", "Part1", "Body", "Solid12", "Physical Product00000772", "PartBody", "  " })
            Check(ProfilStepAdi.AnlamsizMi(anlamsiz), "C2-05 CAD default: \"" + anlamsiz + "\"");
        Check(!ProfilStepAdi.AnlamsizMi("246501") && !ProfilStepAdi.AnlamsizMi("55RS100111-13") && !ProfilStepAdi.AnlamsizMi("Partition plate"),
            "C2-06 real part numbers are kept");
        // Windows file names.
        Check(ProfilStepAdi.Temizle("A/B:C*D?\"E<F>G|H") == "A_B_C_D__E_F_G_H" && ProfilStepAdi.Temizle("CON") == "CON_" &&
              ProfilStepAdi.Temizle("parça. ") == "parça" && ProfilStepAdi.Temizle("") == "parca",
            "C2-07 forbidden characters, reserved names, trailing dot and space are cleaned");
        Check(ProfilStepAdi.DosyaAdi("236948", 4) == "236948_4Adet.stp", "C2-08 ParçaNo_XAdet.stp");
        Check(string.Join("|", ProfilStepAdi.Benzersiz(new[] { "A_4Adet.stp", "a_4adet.stp", "A_4Adet.stp", "B_2Adet.stp" })) ==
              "A_4Adet.stp|a_4adet_2.stp|A_4Adet_3.stp|B_2Adet.stp", "C2-09 clashes get _2, _3 (case ignored)");

        // Rows: STEP quantity when there is no CATIA quantity; quantities of different STEPs are not summed.
        ProductionPackageItem Profil(string step, string no, int adet, int? catia = null) => new()
        {
            SourcePath = Path.Combine(_root, step), PartCode = no, ProfilStep = true, PartLocalId = 7, StepQuantity = adet,
            CatiaQuantity = catia, Multiplier = 1
        };
        var wgrv = Profil("WGRV.stp", "236948", 4);
        var baska = Profil("Baska.stp", "236948", 2);
        var ayni = Profil("Ucuncu.stp", "236948", 4);
        var catiali = Profil("WGRV.stp", "246501", 4, catia: 6);
        foreach (ProductionPackageItem item in new[] { wgrv, baska, ayni, catiali }) item.Validate();
        Check(wgrv.Status == "Hazır" && wgrv.QuantitySource == ProductionQuantitySource.Step && wgrv.QuantitySourceDisplay == "STEP montajı" &&
              wgrv.TargetFileName == "236948_4Adet.stp" && !wgrv.ManualEntryAllowed, "C2-10 STEP assembly quantity, name 236948_4Adet.stp");
        Check(catiali.QuantitySource == ProductionQuantitySource.Catia && catiali.TargetFileName == "246501_6Adet.stp", "C2-11 a CATIA quantity comes first");
        ProductionPackageService.ResolveProfileNames(new[] { wgrv, baska, ayni, catiali });
        Check(wgrv.PackageFileName == "236948_4Adet.stp" && baska.PackageFileName == "236948_2Adet.stp" && ayni.PackageFileName == "236948_4Adet_2.stp" &&
              wgrv.NewFileDisplay == "Profil-STEP\\236948_4Adet.stp",
            "C2-12 same number in other STEPs: own quantities, not summed; a full clash gets _2");
        wgrv.Multiplier = 3; wgrv.Validate();
        Check(wgrv.FinalQuantity == 12 && wgrv.TargetFileName == "236948_12Adet.stp", "C2-13 the multiplier is in the name");

        // Engine results: written, failed read-back, skipped, no engine.
        var yazildi = ProductionPackageService.StepResult(new GeometryLabPartStepTransport
            { PartId = 7, Status = "Written", File = "part-7.stp", Aligned = true, LengthMm = 100, BoxLengthMm = 100, SourceVolumeMm3 = 1000, WrittenVolumeMm3 = 1000.5 }, null);
        Check(yazildi.Durum == "Yazıldı" && yazildi.Hizali && yazildi.HacimFarkiYuzde is double f && Math.Abs(f - 0.05) < 1e-9, "C2-14 written: aligned, volume difference");
        var basarisiz = ProductionPackageService.StepResult(new GeometryLabPartStepTransport
            { PartId = 7, Status = "Failed", Reasons = new[] { "hacim farkı %2" } }, null);
        Check(basarisiz.Durum == "Yazılamadı" && basarisiz.Aciklama == "hacim farkı %2", "C2-15 failed read-back: \"Yazılamadı\" with the reason");
        var atlandi = ProductionPackageService.StepResult(new GeometryLabPartStepTransport
            { PartId = 7, Status = "Skipped", Reasons = new[] { "geçersiz geometri; yazılmadı" } }, null);
        Check(atlandi.Durum == "Atlandı" && atlandi.Aciklama.Contains("geçersiz geometri"), "C2-16 invalid geometry: skipped, not tried");
        var motorsuz = ProductionPackageService.StepResult(null, "GeometryEngine bulunamadı.");
        Check(motorsuz.Durum == "Yazılamadı" && motorsuz.Aciklama.Contains("GeometryEngine bulunamadı"), "C2-17 no engine result: \"Yazılamadı\"");
        wgrv.SetStepResult(basarisiz);
        Check(wgrv.StatusDisplay == "Yazılamadı — hacim farkı %2", "C2-18 the row shows the result");

        // Excel: one workbook, the manifest and the "Profiller" sheet.
        string excel = Path.Combine(_root, "iki-sayfa.xlsx");
        var sayfa1 = new Rapor { SayfaAdi = "Üretim Paketi" }; sayfa1.Sutunlar.Add(new RaporSutun { Ad = "Yeni Dosya" }); sayfa1.Satirlar.Add(new object?[] { "a" });
        var sayfa2 = new Rapor { SayfaAdi = "Profiller" }; sayfa2.Sutunlar.Add(new RaporSutun { Ad = "STEP dosyası" }); sayfa2.Satirlar.Add(new object?[] { "Profil-STEP\\236948_4Adet.stp" });
        ExcelYazici.Yaz(new[] { sayfa1, sayfa2 }, excel);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(excel))
        {
            string kitap = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd();
            string ikinci = new StreamReader(zip.GetEntry("xl/worksheets/sheet2.xml")!.Open()).ReadToEnd();
            string turler = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open()).ReadToEnd();
            Check(kitap.Contains("name=\"Üretim Paketi\" sheetId=\"1\"") && kitap.Contains("name=\"Profiller\" sheetId=\"2\"") &&
                  turler.Contains("/xl/worksheets/sheet2.xml") && ikinci.Contains("STEP dosyası") && ikinci.Contains("236948_4Adet.stp"),
                "C2-19 the workbook has both sheets; Profiller has \"STEP dosyası\"");
        }
    }

    private static void ProductionPackageTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "Macria-Production-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "55RS100111-12_20mm_2adet.dxf");
        File.WriteAllText(source, "source-dxf");
        try
        {
            var catia = new ProductionPackageItem { SourcePath = source, PartCode = ProductionPackageService.PartCode(source), CatiaQuantity = 4, Multiplier = 5 }; catia.Validate();
            Check(catia.FinalQuantity == 20 && catia.QuantitySource == ProductionQuantitySource.Catia, "6C-01 CATIA 4 x 5 = 20");
            var file = new ProductionPackageItem { SourcePath = source, PartCode = ProductionPackageService.PartCode(source), FileNameQuantity = ProductionPackageService.FileNameQuantity(source), Multiplier = 5 }; file.Validate();
            Check(file.FinalQuantity == 10 && file.TargetFileName == "55RS100111-12_10 Adet.dxf", "6C-02 file quantity and clean target name");
            var manual = new ProductionPackageItem { SourcePath = source, PartCode = "part", ManualQuantity = 3, Multiplier = 10 }; manual.Validate(); Check(manual.FinalQuantity == 30, "6C-03 manual 3 x 10 = 30");
            var missing = new ProductionPackageItem { SourcePath = source, PartCode = "part", Multiplier = 2 }; missing.Validate(); Check(missing.Status == "Üretime hazır değil", "6C-04 missing quantity rejected");
            var conflict = new ProductionPackageItem { SourcePath = source, PartCode = "part", CatiaQuantity = 4, FileNameQuantity = 2, Multiplier = 2 }; conflict.Validate(); Check(conflict.QuantitySource == ProductionQuantitySource.Conflict, "6C-05 quantity conflict");
            var zero = new ProductionPackageItem { SourcePath = source, PartCode = "part", ManualQuantity = 1, Multiplier = 0 }; zero.Validate(); Check(zero.Status == "Geçersiz", "6C-06 zero multiplier rejected");
            var overflow = new ProductionPackageItem { SourcePath = source, PartCode = "part", ManualQuantity = int.MaxValue, Multiplier = 2 }; overflow.Validate(); Check(overflow.Status == "Geçersiz", "6C-07 overflow rejected");
            // Üretim Paketi window: why "Paketi Oluştur" is off, or what stays out (the button's rule is unchanged).
            string klasor = Path.GetDirectoryName(source)!;
            var eksik = new ProductionPackageItem { SourcePath = Path.Combine(klasor, "a.dxf"), PartCode = "a", Multiplier = 1 }; eksik.Validate();
            var hazir = new ProductionPackageItem { SourcePath = Path.Combine(klasor, "b_2adet.dxf"), PartCode = "b", FileNameQuantity = 2, Multiplier = 1 }; hazir.Validate();
            var (pasif, pasifMetni) = ProductionPackageService.PackageState(new[] { eksik }, ProductionPackageService.ValidateCommonFolder(new[] { eksik }));
            Check(!pasif && pasifMetni == "Paketi Oluştur pasif — Temel adet eksik: 1 satır", "6C-09 off: reason shown: " + pasifMetni);
            var (acik, acikMetni) = ProductionPackageService.PackageState(new[] { eksik, hazir, conflict }, klasor);
            Check(acik && acikMetni == "Pakete girmeyecek (2 satır) — Adet çelişkisi (CATIA ≠ dosya adı): 1 satır; Temel adet eksik: 1 satır",
                "6C-10 on: rows left out are named: " + acikMetni);
            var baska = new ProductionPackageItem { SourcePath = @"C:\Baska\c_2adet.dxf", PartCode = "c", FileNameQuantity = 2, Multiplier = 1 }; baska.Validate();
            var (farkli, farkliMetni) = ProductionPackageService.PackageState(new[] { hazir, baska }, ProductionPackageService.ValidateCommonFolder(new[] { hazir, baska }));
            Check(!farkli && farkliMetni == "Paketi Oluştur pasif — Kaynak dosyalar aynı klasörde olmalı: 2 farklı klasör", "6C-11 different folders: " + farkliMetni);
            Check(ProductionPackageService.PackageState(new[] { hazir }, klasor) == (true, null) &&
                  ProductionPackageService.PackageState(Array.Empty<ProductionPackageItem>(), null).Message == "Paketi Oluştur pasif: pakete alınacak satır yok.",
                "6C-12 all ready: no message; no rows: said");
            Check(eksik.StatusDisplay == "Üretime hazır değil — Temel adet kaynağı bulunamadı." && hazir.StatusDisplay == "Hazır" && hazir.SourceFileName == "b_2adet.dxf",
                "6C-13 Durum / Açıklama and file name columns");
            Check(ProductionPackageService.PartCode("55RS125394-5_Rep.stp") == "55RS125394-5", "6C-08 STEP Rep removed");
            Check(!ProductionPackageService.IsSupported("bad.pdf"), "6C-09 unsupported extension rejected");
            string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
            string folder = ProductionPackageService.CreateFolder(root, 5, new DateTime(2026, 9, 20, 21, 30, 0)); ProductionPackageService.CopyReady(file, folder);
            Check(File.Exists(Path.Combine(folder, file.TargetFileName)) && before == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))), "6C-10 copy preserves source hash");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void PreviewCoreTests()
    {
        Check(PreviewContentTypeResolver.Resolve("file.dxf") == PreviewContentType.Dxf, "preview resolves .dxf");
        Check(PreviewContentTypeResolver.Resolve("FILE.DXF") == PreviewContentType.Dxf, "preview resolves uppercase .DXF");
        Check(PreviewContentTypeResolver.Resolve("file.dwg") == PreviewContentType.Dwg, "preview resolves .dwg");
        Check(PreviewContentTypeResolver.Resolve("file.step") == PreviewContentType.Step, "preview resolves .step");
        Check(PreviewContentTypeResolver.Resolve("file.stp") == PreviewContentType.Step, "preview resolves .stp");
        Check(PreviewContentTypeResolver.Resolve("FILE.STEP") == PreviewContentType.Step, "preview resolves uppercase .STEP");
        Check(PreviewContentTypeResolver.Resolve("file.txt") == PreviewContentType.Unknown, "preview rejects unsupported extension");
        Check(PreviewContentTypeResolver.Resolve("extensionless") == PreviewContentType.Unknown, "preview rejects extensionless path");

        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Dxf, PreviewCapability.Preview2D) == PreviewSupportLevel.Supported,
            "DXF supports 2D preview");
        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Dxf, PreviewCapability.Preview3D) == PreviewSupportLevel.Unsupported,
            "DXF does not support 3D preview");
        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Dxf, PreviewCapability.Edit) == PreviewSupportLevel.RequiresContentValidation,
            "DXF edit requires content validation");
        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Step, PreviewCapability.Preview3D) == PreviewSupportLevel.Supported,
            "STEP supports 3D preview");
        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Step, PreviewCapability.Preview2D) == PreviewSupportLevel.Unsupported,
            "STEP does not support 2D preview");
        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Step, PreviewCapability.Edit) == PreviewSupportLevel.Unsupported,
            "STEP does not support edit");
        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Dwg, PreviewCapability.Preview2D) == PreviewSupportLevel.Unsupported,
            "DWG has no current 2D preview provider");
        Check(PreviewCapabilityResolver.Resolve(PreviewContentType.Unknown, PreviewCapability.Preview3D) == PreviewSupportLevel.Unsupported,
            "unknown content has no preview capability");

        string dxf = Path.Combine(_root, "preview.dxf");
        string step = Path.Combine(_root, "preview.STP");
        string text = Path.Combine(_root, "preview.txt");
        File.WriteAllText(dxf, "test dxf placeholder");
        File.WriteAllText(step, "test step placeholder");
        File.WriteAllText(text, "unsupported");
        var coordinator = new PreviewCoordinator();
        PreviewResult Resolve(string path, PreviewCapability capability, PreviewPresentation? presentation) =>
            coordinator.Resolve(new PreviewRequest { SourcePath = path, Capability = capability, Presentation = presentation });

        Check(coordinator.Resolve(null).Status == PreviewResultStatus.InvalidRequest, "null preview request is invalid");
        Check(Resolve("", PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status == PreviewResultStatus.InvalidRequest,
            "empty preview path is invalid");
        Check(Resolve(Path.Combine(_root, "missing.dxf"), PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status == PreviewResultStatus.MissingFile,
            "missing known-content file is reported");
        Check(Resolve(text, PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status == PreviewResultStatus.UnsupportedContent,
            "unsupported extension returns controlled result");
        Check(Resolve(step, PreviewCapability.Preview3D, PreviewPresentation.Embedded).IsReady,
            "STEP 3D embedded request is ready");
        Check(Resolve(step, PreviewCapability.Preview3D, PreviewPresentation.Large).IsReady,
            "STEP 3D large request is ready");
        Check(Resolve(dxf, PreviewCapability.Preview2D, PreviewPresentation.Embedded).IsReady,
            "DXF 2D embedded request is ready");
        Check(Resolve(dxf, PreviewCapability.Preview2D, PreviewPresentation.Large).IsReady,
            "DXF 2D large request is ready");
        Check(Resolve(step, PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status == PreviewResultStatus.UnsupportedCapability,
            "STEP 2D request is controlled unsupported capability");
        Check(Resolve(dxf, PreviewCapability.Edit, null).Status == PreviewResultStatus.RequiresContentValidation,
            "DXF edit remains separate and conditional");
        Check(Resolve(dxf, PreviewCapability.Edit, PreviewPresentation.Large).Status == PreviewResultStatus.InvalidRequest,
            "edit cannot be conflated with preview presentation");
    }

    // Pins the current preview inventory: ASCII DXF has 2D preview (embedded panel and the
    // large OnizlemeWindow) and content-validated edit, STEP/STP has 3D preview (embedded
    // viewport and the large OcctPreviewWindow), DWG and unknown extensions have nothing.
    // Content-level DXF outcomes (binary, invalid, empty) are pinned in DxfEdit.Tests.
    private static void PreviewInventoryTests()
    {
        var inventory = new (PreviewContentType Content, PreviewCapability Capability, PreviewSupportLevel Support)[]
        {
            (PreviewContentType.Dxf, PreviewCapability.Preview2D, PreviewSupportLevel.Supported),
            (PreviewContentType.Dxf, PreviewCapability.Preview3D, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Dxf, PreviewCapability.Edit, PreviewSupportLevel.RequiresContentValidation),
            (PreviewContentType.Step, PreviewCapability.Preview2D, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Step, PreviewCapability.Preview3D, PreviewSupportLevel.Supported),
            (PreviewContentType.Step, PreviewCapability.Edit, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Dwg, PreviewCapability.Preview2D, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Dwg, PreviewCapability.Preview3D, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Dwg, PreviewCapability.Edit, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Unknown, PreviewCapability.Preview2D, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Unknown, PreviewCapability.Preview3D, PreviewSupportLevel.Unsupported),
            (PreviewContentType.Unknown, PreviewCapability.Edit, PreviewSupportLevel.Unsupported),
        };

        // A new content type or capability must be added here explicitly; it cannot
        // silently inherit a capability.
        int pairs = Enum.GetValues<PreviewContentType>().Length * Enum.GetValues<PreviewCapability>().Length;
        Check(inventory.Length == pairs && inventory.Select(row => (row.Content, row.Capability)).Distinct().Count() == pairs,
            "preview inventory lists every content/capability pair exactly once");

        foreach (var row in inventory)
        {
            Check(PreviewCapabilityResolver.Resolve(row.Content, row.Capability) == row.Support,
                $"inventory: {row.Content} {row.Capability} is {row.Support}");
            bool presentable = row.Capability != PreviewCapability.Edit && row.Support == PreviewSupportLevel.Supported;
            foreach (PreviewPresentation presentation in Enum.GetValues<PreviewPresentation>())
                Check(PreviewCapabilityResolver.SupportsPresentation(row.Content, row.Capability, presentation) == presentable,
                    $"inventory: {row.Content} {row.Capability} {presentation} presentation is {(presentable ? "open" : "closed")}");
        }
        Check(!PreviewCapabilityResolver.SupportsPresentation(PreviewContentType.Dxf, PreviewCapability.Preview2D, (PreviewPresentation)42),
            "undefined presentation is never supported");

        string dir = Path.Combine(_root, "preview-inventory");
        Directory.CreateDirectory(dir);
        string Write(string name)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, "placeholder");
            return path;
        }
        string dxf = Write("part.dxf"), dxfUpper = Write("PART2.DXF");
        string stp = Write("model.stp"), step = Write("model.step");
        string dwg = Write("drawing.dwg"), dwgUpper = Write("DRAWING2.DWG");
        string text = Write("notes.txt"), noExtension = Write("noextension");
        string folderNamedDxf = Path.Combine(dir, "folder.dxf");
        Directory.CreateDirectory(folderNamedDxf);

        var coordinator = new PreviewCoordinator();
        PreviewResult Resolve(string? path, PreviewCapability capability, PreviewPresentation? presentation) =>
            coordinator.Resolve(new PreviewRequest
            {
                SourcePath = path,
                Capability = capability,
                Presentation = presentation,
                SourceContext = "preview inventory test"
            });

        foreach (string path in new[] { dxf, dxfUpper })
        {
            foreach (PreviewPresentation presentation in Enum.GetValues<PreviewPresentation>())
            {
                PreviewResult ready = Resolve(path, PreviewCapability.Preview2D, presentation);
                Check(ready.IsReady && ready.ContentType == PreviewContentType.Dxf &&
                      ready.SupportLevel == PreviewSupportLevel.Supported && ready.Presentation == presentation &&
                      ready.NormalizedPath == Path.GetFullPath(path) && ready.DiagnosticDetail == null,
                    $"DXF 2D {presentation} preview is ready: {Path.GetFileName(path)}");
                Check(Resolve(path, PreviewCapability.Preview3D, presentation).Status == PreviewResultStatus.UnsupportedCapability,
                    $"DXF has no 3D {presentation} preview: {Path.GetFileName(path)}");
            }
            PreviewResult edit = Resolve(path, PreviewCapability.Edit, null);
            Check(edit.Status == PreviewResultStatus.RequiresContentValidation && !edit.IsReady &&
                  edit.SupportLevel == PreviewSupportLevel.RequiresContentValidation,
                $"DXF edit is never granted from the extension alone: {Path.GetFileName(path)}");
        }

        foreach (string path in new[] { stp, step })
        {
            foreach (PreviewPresentation presentation in Enum.GetValues<PreviewPresentation>())
            {
                PreviewResult ready = Resolve(path, PreviewCapability.Preview3D, presentation);
                Check(ready.IsReady && ready.ContentType == PreviewContentType.Step &&
                      ready.SupportLevel == PreviewSupportLevel.Supported && ready.Presentation == presentation &&
                      ready.NormalizedPath == Path.GetFullPath(path) && ready.DiagnosticDetail == null,
                    $"STEP 3D {presentation} preview is ready: {Path.GetFileName(path)}");
            }
            Check(Resolve(path, PreviewCapability.Edit, null).Status == PreviewResultStatus.UnsupportedCapability,
                $"STEP has no edit capability: {Path.GetFileName(path)}");
        }

        foreach (string path in new[] { dwg, dwgUpper })
        {
            foreach (PreviewCapability capability in new[] { PreviewCapability.Preview2D, PreviewCapability.Preview3D })
                foreach (PreviewPresentation presentation in Enum.GetValues<PreviewPresentation>())
                {
                    PreviewResult result = Resolve(path, capability, presentation);
                    Check(result.Status == PreviewResultStatus.UnsupportedCapability && !result.IsReady &&
                          result.ContentType == PreviewContentType.Dwg && result.SupportLevel == PreviewSupportLevel.Unsupported,
                        $"DWG {capability} {presentation} stays closed: {Path.GetFileName(path)}");
                }
            Check(Resolve(path, PreviewCapability.Edit, null).Status == PreviewResultStatus.UnsupportedCapability,
                $"DWG edit stays closed: {Path.GetFileName(path)}");
        }

        foreach (string path in new[] { text, noExtension })
            foreach (PreviewCapability capability in Enum.GetValues<PreviewCapability>())
            {
                PreviewResult result = Resolve(path, capability,
                    capability == PreviewCapability.Edit ? null : PreviewPresentation.Embedded);
                Check(result.Status == PreviewResultStatus.UnsupportedContent && result.ContentType == PreviewContentType.Unknown,
                    $"unknown content {Path.GetFileName(path)} {capability} is a controlled unsupported result");
            }

        // Missing files keep their content type, so callers can still show the DWG message.
        PreviewResult missingDwg = Resolve(Path.Combine(dir, "missing.dwg"), PreviewCapability.Preview2D, PreviewPresentation.Embedded);
        Check(missingDwg.Status == PreviewResultStatus.MissingFile && missingDwg.ContentType == PreviewContentType.Dwg,
            "missing DWG is reported as missing with DWG content type");
        PreviewResult missingStep = Resolve(Path.Combine(dir, "missing.step"), PreviewCapability.Preview3D, PreviewPresentation.Large);
        Check(missingStep.Status == PreviewResultStatus.MissingFile && missingStep.ContentType == PreviewContentType.Step &&
              !missingStep.IsReady, "missing STEP large preview is a controlled result");
        Check(Resolve(Path.Combine(dir, "missing.dxf"), PreviewCapability.Edit, null).Status == PreviewResultStatus.MissingFile,
            "missing DXF edit is a controlled result");
        Check(Resolve(Path.Combine(dir, "missing.txt"), PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status ==
              PreviewResultStatus.UnsupportedContent, "unknown extension is reported before file existence");
        Check(Resolve(folderNamedDxf, PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status == PreviewResultStatus.MissingFile,
            "a directory with a DXF extension is not a previewable file");

        Check(Resolve("   ", PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status == PreviewResultStatus.InvalidRequest,
            "whitespace path is an invalid request");
        Check(Resolve(dxf, PreviewCapability.Preview2D, null).Status == PreviewResultStatus.InvalidRequest,
            "preview without presentation is an invalid request");
        Check(Resolve(stp, PreviewCapability.Preview3D, null).Status == PreviewResultStatus.InvalidRequest,
            "3D preview without presentation is an invalid request");
        Check(Resolve(dxf, (PreviewCapability)42, PreviewPresentation.Embedded).Status == PreviewResultStatus.InvalidRequest,
            "undefined capability is an invalid request");
        Check(Resolve(dxf, PreviewCapability.Preview2D, (PreviewPresentation)42).Status == PreviewResultStatus.InvalidRequest,
            "undefined presentation is an invalid request");
        Check(Resolve("bad\0name.dxf", PreviewCapability.Preview2D, PreviewPresentation.Embedded).Status == PreviewResultStatus.InvalidRequest,
            "path with an invalid character is an invalid request");
        Check(Resolve(Path.GetRelativePath(Environment.CurrentDirectory, dxf), PreviewCapability.Preview2D,
                  PreviewPresentation.Embedded).NormalizedPath == Path.GetFullPath(dxf),
            "relative path is normalized to a full path");

        // No request shape may throw. Ready is only ever returned for DXF 2D or STEP 3D
        // with a presentation, never for edit.
        string?[] paths = { null, "", "   ", dxf, stp, step, dwg, text, noExtension,
            Path.Combine(dir, "missing.dxf"), folderNamedDxf, "bad\0name.dxf" };
        PreviewCapability[] capabilities = Enum.GetValues<PreviewCapability>().Append((PreviewCapability)42).ToArray();
        PreviewPresentation?[] presentations = { null, PreviewPresentation.Embedded, PreviewPresentation.Large, (PreviewPresentation)42 };
        foreach (string? path in paths)
            foreach (PreviewCapability capability in capabilities)
                foreach (PreviewPresentation? presentation in presentations)
                {
                    PreviewResult result = Resolve(path, capability, presentation);
                    bool readyShape = !result.IsReady ||
                        (result.NormalizedPath != null && presentation != null && capability != PreviewCapability.Edit &&
                         (result.ContentType, capability) is (PreviewContentType.Dxf, PreviewCapability.Preview2D)
                             or (PreviewContentType.Step, PreviewCapability.Preview3D));
                    Check(!string.IsNullOrWhiteSpace(result.Message) &&
                          result.IsReady == (result.Status == PreviewResultStatus.Ready) &&
                          result.Capability == capability && result.Presentation == presentation && readyShape,
                        $"controlled preview result for path='{path?.Replace("\0", "\\0")}' capability={capability} presentation={presentation}");
                }
    }

    // Stage 2 contract: content evaluation is a separate result next to PreviewResultStatus.
    private static void PreviewContentCheckTests()
    {
        // The request-level status set stays as it is; content outcomes do not become new main statuses.
        string[] requestStatuses = { "Ready", "RequiresContentValidation", "UnsupportedCapability", "UnsupportedContent",
            "MissingFile", "InvalidRequest", "Failed" };
        Check(Enum.GetNames<PreviewResultStatus>().SequenceEqual(requestStatuses),
            "PreviewResultStatus has no content-check status");
        Check(Enum.GetValues<PreviewContentCheckStatus>().Length == 4 && Enum.GetValues<PreviewContentCheckReason>().Length == 5,
            "content check status/reason sets are pinned");

        PreviewContentCheckResult notChecked = PreviewContentCheckResult.NotChecked;
        Check(notChecked.Status == PreviewContentCheckStatus.NotChecked && notChecked.Reason == PreviewContentCheckReason.None &&
              notChecked.DiagnosticMessage == null && !notChecked.IsAccepted, "NotChecked has no reason and is not accepted");
        Check(ReferenceEquals(notChecked, PreviewContentCheckResult.NotChecked), "NotChecked is a single shared instance");

        PreviewContentCheckResult accepted = PreviewContentCheckResult.Accepted("12 entities");
        Check(accepted.Status == PreviewContentCheckStatus.Accepted && accepted.Reason == PreviewContentCheckReason.None &&
              accepted.IsAccepted && accepted.DiagnosticMessage == "12 entities", "Accepted keeps its diagnostic and has no reason");
        Check(PreviewContentCheckResult.Accepted().DiagnosticMessage == null, "Accepted diagnostic is optional");

        foreach (PreviewContentCheckReason reason in new[] { PreviewContentCheckReason.BinaryDxf,
                     PreviewContentCheckReason.NoDrawableEntities, PreviewContentCheckReason.InvalidContent })
        {
            PreviewContentCheckResult rejected = PreviewContentCheckResult.Rejected(reason, "detail " + reason);
            Check(rejected.Status == PreviewContentCheckStatus.Rejected && rejected.Reason == reason && !rejected.IsAccepted &&
                  rejected.DiagnosticMessage == "detail " + reason, $"Rejected carries {reason}");
        }

        foreach (PreviewContentCheckReason reason in new[] { PreviewContentCheckReason.None,
                     PreviewContentCheckReason.ReadError, (PreviewContentCheckReason)42 })
        {
            bool thrown = false;
            try { PreviewContentCheckResult.Rejected(reason); }
            catch (ArgumentOutOfRangeException) { thrown = true; }
            Check(thrown, $"Rejected refuses non-rejection reason {reason}");
        }

        PreviewContentCheckResult failed = PreviewContentCheckResult.Failed("IOException: locked");
        Check(failed.Status == PreviewContentCheckStatus.Failed && failed.Reason == PreviewContentCheckReason.ReadError &&
              !failed.IsAccepted && failed.DiagnosticMessage == "IOException: locked", "Failed is always a read error");

        // Every reason has exactly the statuses the factories allow.
        var allowed = new Dictionary<PreviewContentCheckReason, PreviewContentCheckStatus[]>
        {
            [PreviewContentCheckReason.None] = new[] { PreviewContentCheckStatus.NotChecked, PreviewContentCheckStatus.Accepted },
            [PreviewContentCheckReason.BinaryDxf] = new[] { PreviewContentCheckStatus.Rejected },
            [PreviewContentCheckReason.NoDrawableEntities] = new[] { PreviewContentCheckStatus.Rejected },
            [PreviewContentCheckReason.InvalidContent] = new[] { PreviewContentCheckStatus.Rejected },
            [PreviewContentCheckReason.ReadError] = new[] { PreviewContentCheckStatus.Failed },
        };
        Check(allowed.Count == Enum.GetValues<PreviewContentCheckReason>().Length, "status/reason table covers every reason");
        var produced = new[] { notChecked, accepted, failed }
            .Concat(new[] { PreviewContentCheckReason.BinaryDxf, PreviewContentCheckReason.NoDrawableEntities,
                PreviewContentCheckReason.InvalidContent }.Select(reason => PreviewContentCheckResult.Rejected(reason)));
        foreach (PreviewContentCheckResult result in produced)
            Check(allowed[result.Reason].Contains(result.Status), $"{result.Status}/{result.Reason} is an allowed pair");

        Check(PreviewContentCheckResult.Rejected(PreviewContentCheckReason.BinaryDxf, "x") ==
              PreviewContentCheckResult.Rejected(PreviewContentCheckReason.BinaryDxf, "x"), "content check results compare by value");
        Check(PreviewContentCheckResult.Rejected(PreviewContentCheckReason.BinaryDxf) !=
              PreviewContentCheckResult.Rejected(PreviewContentCheckReason.InvalidContent), "different reasons are different results");
    }

    // Shared 3D preview toolbar rules (Step3BPaneli): the same for every tab and the "Büyük Aç" window.
    private static void Step3BAracDurumuTests()
    {
        Step3BAracGorunumu bos = Step3BAracDurumu.Hesapla(StepViewportLoadState.Empty, false, null, OcctPartView.Isolated, "Seçin.");
        Check(!bos.GorunumDugmeleriAcik && !bos.BuyukAcAcik && !bos.ParcaDugmesiGorunur && !bos.ParcaDugmesiAcik,
            "empty viewport: every command off, part button hidden");
        Check(bos.DurumMetni == "Seçin.", "empty viewport: status is the viewport message");

        Step3BAracGorunumu yukleniyor = Step3BAracDurumu.Hesapla(StepViewportLoadState.Pending, true, "P-1", OcctPartView.Isolated, "Yükleniyor");
        Check(!yukleniyor.GorunumDugmeleriAcik && yukleniyor.BuyukAcAcik, "pending: views off, Büyük Aç on once the file exists");
        Check(yukleniyor.ParcaDugmesiGorunur && !yukleniyor.ParcaDugmesiAcik, "pending part row: part button shown but off");
        Check(yukleniyor.DurumMetni == "Yükleniyor", "pending: no part-view explanation before the model is loaded");

        Step3BAracGorunumu dosya = Step3BAracDurumu.Hesapla(StepViewportLoadState.Loaded, true, null, OcctPartView.Isolated, "Hazır");
        Check(dosya.GorunumDugmeleriAcik && dosya.BuyukAcAcik && !dosya.ParcaDugmesiGorunur, "loaded single STEP: views on, no part button");
        Check(dosya.DurumMetni == "Hazır", "loaded single STEP: status is the viewport message");

        Step3BAracGorunumu yalniz = Step3BAracDurumu.Hesapla(StepViewportLoadState.Loaded, true, "P-1", OcctPartView.Isolated, "Hazır");
        Check(yalniz.ParcaDugmesiGorunur && yalniz.ParcaDugmesiAcik, "loaded part row: part button on");
        Check(yalniz.ParcaDugmesiMetni == Step3BAracDurumu.MontajIcindeGoster, "isolated: button offers the assembly view");
        Check(yalniz.DurumMetni == "Hazır\n" + Step3BAracDurumu.YalnizParcaAciklamasi, "isolated: status explains the view");

        Step3BAracGorunumu montaj = Step3BAracDurumu.Hesapla(StepViewportLoadState.Loaded, true, "P-1", OcctPartView.InAssembly, null);
        Check(montaj.ParcaDugmesiMetni == Step3BAracDurumu.YalnizParcayiGoster, "in assembly: button offers the part alone");
        Check(montaj.DurumMetni == Step3BAracDurumu.MontajIcindeAciklamasi, "in assembly, no viewport message: explanation only");

        Step3BAracGorunumu hata = Step3BAracDurumu.Hesapla(StepViewportLoadState.Failed, true, "P-1", OcctPartView.Isolated, "DLL yok");
        Check(!hata.GorunumDugmeleriAcik && !hata.ParcaDugmesiAcik && hata.DurumMetni == "DLL yok",
            "failed viewport: views and part button off, error message kept");

        Step3BAracGorunumu eksik = Step3BAracDurumu.Hesapla(StepViewportLoadState.Failed, false, "P-1", OcctPartView.Isolated, "Yok");
        Check(!eksik.BuyukAcAcik, "missing file: Büyük Aç off");
    }

    // Stage 3: STEP 3D adapter over a fake viewport port; no HwndHost, native DLL or OCCT session.
    private static void OcctStepPreviewAdapterTests()
    {
        string dir = Path.Combine(_root, "occt-adapter");
        Directory.CreateDirectory(dir);
        string Write(string name)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, "placeholder");
            return path;
        }
        string stp = Write("model.stp"), step = Write("MODEL2.STEP"), dxf = Write("part.dxf"), dwg = Write("drawing.dwg"),
            text = Write("notes.txt");
        PreviewRequest Request(string? path, PreviewCapability capability, PreviewPresentation? presentation) =>
            new() { SourcePath = path, Capability = capability, Presentation = presentation };

        Check(Throws<ArgumentNullException>(() => new OcctStepPreviewAdapter(null!)), "adapter requires a viewport port");

        // Ready: STEP/STP 3D embedded and large forward one load with the normalized path.
        foreach (string path in new[] { stp, step })
            foreach (PreviewPresentation presentation in Enum.GetValues<PreviewPresentation>())
            {
                var port = new FakeStepViewportPort { StateAfterLoad = StepViewportLoadState.Loaded };
                PreviewRequest request = Request(path, PreviewCapability.Preview3D, presentation);
                PreviewResult result = new OcctStepPreviewAdapter(port).Load(request);
                Check(result.IsReady && result == new PreviewCoordinator().Resolve(request) &&
                      port.Calls.SequenceEqual(new[] { "Load:" + Path.GetFullPath(path) }),
                    $"STEP 3D {presentation} forwards exactly one load: {Path.GetFileName(path)}");
            }

        // Pending: the native viewer window does not exist yet; the host keeps the path and loads it later.
        var pending = new FakeStepViewportPort { StateAfterLoad = StepViewportLoadState.Pending };
        Check(new OcctStepPreviewAdapter(pending).Load(Request(stp, PreviewCapability.Preview3D, PreviewPresentation.Embedded)).IsReady &&
              pending.Calls.Count == 1, "pending viewport accepts the load");

        // Load failure reported by the viewport state.
        var failing = new FakeStepViewportPort
        {
            StateAfterLoad = StepViewportLoadState.Failed,
            MessageAfterLoad = "STEP önizleme yüklenemedi: native error"
        };
        PreviewResult failed = new OcctStepPreviewAdapter(failing).Load(Request(stp, PreviewCapability.Preview3D, PreviewPresentation.Large));
        Check(failed.Status == PreviewResultStatus.Failed && failed.Message == "STEP önizleme yüklenemedi: native error" &&
              failed.DiagnosticDetail == failing.MessageAfterLoad && failed.ContentType == PreviewContentType.Step &&
              failed.NormalizedPath == Path.GetFullPath(stp) && failing.Calls.Count == 1,
            "viewport load failure is a controlled Failed result with the host message");
        var silentFailure = new FakeStepViewportPort { StateAfterLoad = StepViewportLoadState.Failed, MessageAfterLoad = "" };
        Check(new OcctStepPreviewAdapter(silentFailure).Load(Request(stp, PreviewCapability.Preview3D, PreviewPresentation.Embedded))
              .Message == "STEP önizleme yüklenemedi.", "failure without host message gets a fallback message");

        // A throwing port is contained.
        var throwing = new FakeStepViewportPort { ThrowOn = "Load" };
        PreviewResult thrown = new OcctStepPreviewAdapter(throwing).Load(Request(stp, PreviewCapability.Preview3D, PreviewPresentation.Embedded));
        Check(thrown.Status == PreviewResultStatus.Failed && thrown.Message == "STEP önizleme yüklenemedi." &&
              thrown.DiagnosticDetail!.Contains("fake Load failure", StringComparison.Ordinal),
            "port exception becomes a controlled Failed result");

        // Nothing reaches the viewport for requests the coordinator or adapter refuses.
        var refused = new (string Name, PreviewRequest? Request, PreviewResultStatus Expected)[]
        {
            ("null request", null, PreviewResultStatus.InvalidRequest),
            ("empty path", Request("", PreviewCapability.Preview3D, PreviewPresentation.Embedded), PreviewResultStatus.InvalidRequest),
            ("no presentation", Request(stp, PreviewCapability.Preview3D, null), PreviewResultStatus.InvalidRequest),
            ("undefined presentation", Request(stp, PreviewCapability.Preview3D, (PreviewPresentation)42), PreviewResultStatus.InvalidRequest),
            ("missing STEP", Request(Path.Combine(dir, "missing.stp"), PreviewCapability.Preview3D, PreviewPresentation.Large),
                PreviewResultStatus.MissingFile),
            ("unknown extension", Request(text, PreviewCapability.Preview3D, PreviewPresentation.Embedded), PreviewResultStatus.UnsupportedContent),
            ("STEP 2D", Request(stp, PreviewCapability.Preview2D, PreviewPresentation.Embedded), PreviewResultStatus.UnsupportedCapability),
            ("STEP edit", Request(stp, PreviewCapability.Edit, null), PreviewResultStatus.UnsupportedCapability),
            ("DXF 2D", Request(dxf, PreviewCapability.Preview2D, PreviewPresentation.Embedded), PreviewResultStatus.UnsupportedCapability),
            ("DXF 3D", Request(dxf, PreviewCapability.Preview3D, PreviewPresentation.Large), PreviewResultStatus.UnsupportedCapability),
            ("DXF edit", Request(dxf, PreviewCapability.Edit, null), PreviewResultStatus.UnsupportedCapability),
            ("DWG 3D", Request(dwg, PreviewCapability.Preview3D, PreviewPresentation.Embedded), PreviewResultStatus.UnsupportedCapability),
        };
        foreach (var (name, request, expected) in refused)
        {
            var port = new FakeStepViewportPort();
            PreviewResult result = new OcctStepPreviewAdapter(port).Load(request);
            Check(result.Status == expected && !result.IsReady && !string.IsNullOrWhiteSpace(result.Message) && port.Calls.Count == 0,
                $"{name} is {expected} and never reaches the viewport");
        }
        PreviewResult dxfRefusal = new OcctStepPreviewAdapter(new FakeStepViewportPort())
            .Load(Request(dxf, PreviewCapability.Preview2D, PreviewPresentation.Embedded));
        Check(dxfRefusal.SupportLevel == PreviewSupportLevel.Unsupported && dxfRefusal.ContentType == PreviewContentType.Dxf,
            "coordinator-ready DXF 2D is refused by the STEP adapter as unsupported");

        // Commands forward one-to-one.
        var commands = new FakeStepViewportPort { State = StepViewportLoadState.Loaded };
        var adapter = new OcctStepPreviewAdapter(commands);
        Check(adapter.FitAll() && adapter.Clear(), "FitAll and Clear succeed on a healthy viewport");
        foreach (OcctStandardView view in Enum.GetValues<OcctStandardView>())
            Check(adapter.SetView(view), $"SetView {view} succeeds");
        string[] expectedCalls = new[] { "FitAll", "Clear" }
            .Concat(Enum.GetValues<OcctStandardView>().Select(view => "SetView:" + view)).ToArray();
        Check(commands.Calls.SequenceEqual(expectedCalls), "commands reach the viewport in order, exactly once each");
        Check(!adapter.SetView((OcctStandardView)42) && commands.Calls.Count == expectedCalls.Length,
            "undefined view is refused without reaching the viewport");

        var unavailable = new FakeStepViewportPort { State = StepViewportLoadState.Failed };
        var unavailableAdapter = new OcctStepPreviewAdapter(unavailable);
        Check(!unavailableAdapter.FitAll() && !unavailableAdapter.SetView(OcctStandardView.Top) && unavailable.Calls.Count == 2,
            "commands on a failed viewport are forwarded but reported as unsuccessful");
        var throwingCommands = new OcctStepPreviewAdapter(new FakeStepViewportPort { ThrowOn = "FitAll" });
        Check(!throwingCommands.FitAll(), "throwing command is contained");
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        return false;
    }

    private sealed class FakeStepViewportPort : IStepViewportPort
    {
        public List<string> Calls { get; } = new();
        public StepViewportLoadState State { get; set; } = StepViewportLoadState.Empty;
        public StepViewportLoadState? StateAfterLoad { get; init; }
        public string MessageAfterLoad { get; init; } = "";
        public string? ThrowOn { get; init; }
        public StepViewportLoadState LoadState => State;
        public string StatusMessage { get; private set; } = "";

        public void LoadStep(string path)
        {
            Record("Load", "Load:" + path);
            if (StateAfterLoad is StepViewportLoadState state) State = state;
            StatusMessage = MessageAfterLoad;
        }

        public void ClearModel() => Record("Clear", "Clear");
        public void FitAll() => Record("FitAll", "FitAll");
        public void SetView(OcctStandardView view) => Record("SetView", "SetView:" + view);

        private void Record(string operation, string call)
        {
            Calls.Add(call);
            if (ThrowOn == operation) throw new InvalidOperationException("fake " + operation + " failure");
        }
    }

    private static void ExternalStepExcelWriter()
    {
        string output = Path.Combine(Path.GetTempPath(), "Macria-ExternalStepExcel-" + Guid.NewGuid().ToString("N") + ".xlsx");
        var report = new Rapor
        {
            SayfaAdi = "STEP Analiz Sonuçları",
            TabloIlkSatirdanBaslar = true,
            IlkSatiriDondur = true,
            OtomatikFiltre = true
        };
        foreach (string header in new[] { "Durum", "STEP Dosyası", "Parça Türü", "Kesit", "Boy", "Topoloji Bilgisi", "Açılı Kesim", "İşlem Durumu", "Kanıt / Açıklama", "Karar Kaynağı", "Karar Notu" })
            report.Sutunlar.Add(new RaporSutun { Ad = header, Genislik = 1, Sayi = false, Durum = false, Ondalik = 0 });
        report.Toplam = null;
        report.Satirlar.Add(new object?[] { "Tanındı", "01-Duz-Duz_Rep.stp", "Kare Kutu Profil", "40 × 40 × 2 mm", "85 mm", "—", "0° / 0°", "—", "Kesit: Tanındı", "Kullanıcı", "Teknik onay" });
        ExcelYazici.Yaz(report, output);
        using var archive = System.IO.Compression.ZipFile.OpenRead(output);
        string sheet;
        using (var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open())) sheet = reader.ReadToEnd();
        Check(sheet.Contains("<pane ySplit=\"1\"") && sheet.Contains("<autoFilter ref=\"A1:K2\""),
            "external STEP Excel uses a frozen first row and an autofilter over visible rows");
        Check(sheet.Contains("STEP Dosyası") && sheet.Contains("Kare Kutu Profil") && sheet.Contains("Karar Kaynağı") &&
              sheet.Contains("Teknik onay") && !sheet.Contains("SquareHollowSection"),
            "external STEP Excel contains Turkish display columns, effective decision source and note");
    }

    private static GeometryLabProcessAdapterResult BaseStockResult(
        string? modificationStatus,
        GeometryLabProfileRecognitionTransport? normalProfile = null,
        double? axisSpanMm = null) => new()
    {
        Status = GeometryLabProcessAdapterStatus.Succeeded,
        Analysis = new GeometryLabAnalysisTransport
        {
            SchemaVersion = "1.0",
            Status = "Succeeded",
            Solids = new[] { new GeometryLabSolidTransport { Status = "Succeeded" } },
            ProfileRecognitions = new[] { normalProfile ?? new GeometryLabProfileRecognitionTransport
            {
                Status = "Succeeded", SectionRecognitionStatus = "Ambiguous", LengthRecognitionStatus = "NotStarted",
                CutRecognitionStatus = "NotStarted", ProfileType = "Unknown"
            } },
            BaseStockProfile = new GeometryLabBaseStockProfileTransport
            {
                Status = "Recognized", ProfileType = "RectangularHollowSection", OuterWidthMm = 60, OuterHeightMm = 40,
                InnerWidthMm = 54, InnerHeightMm = 34, WallThicknessMm = 3, AxisCandidateId = 1,
                StableSectionRegions = new[]
                {
                    new GeometryLabStableSectionRegionTransport { LengthMm = 100, ValidSampleCount = 3, RepresentativeSectionCount = 3 },
                    new GeometryLabStableSectionRegionTransport { LengthMm = 100, ValidSampleCount = 3, RepresentativeSectionCount = 3 }
                }
            },
            ModificationAnalysis = modificationStatus == null ? null : new GeometryLabModificationAnalysisTransport
            {
                Status = modificationStatus,
                ModifiedAxisIntervals = new[] { new GeometryLabModifiedAxisIntervalTransport { LengthMm = 30, Status = "Modified" } }
            },
            ProfileGeometryAnalysis = axisSpanMm is double span ? ReliableAxis(span) : null
        }
    };

    private static GeometryLabProfileRecognitionTransport NormalRecognizedProfile() => new()
    {
        Status = "Succeeded", SectionRecognitionStatus = "Recognized", LengthRecognitionStatus = "Recognized",
        CutRecognitionStatus = "Recognized", ProfileType = "SquareHollowSection", OuterWidthMm = 40, OuterHeightMm = 40,
        WallThicknessMm = 2,
        LengthSummary = new GeometryLabLengthSummaryTransport { MeasurementStatus = "Valid", Classification = "Uniform", UniformLengthMm = 85 },
        EndCutCandidates = new[]
        {
            new GeometryLabEndCutCandidateTransport { MeasurementStatus = "Valid", CutAngleDegrees = 0 },
            new GeometryLabEndCutCandidateTransport { MeasurementStatus = "Valid", CutAngleDegrees = 0 }
        }
    };

    private static async Task TemporaryStepWorkspaceAsync()
    {
        string workspaceRoot = Path.Combine(_root, "temporary-step-workspaces");
        var exporter = new GeometryLabTemporaryStepExporter(new GeometryLabTemporaryStepExporterOptions
        {
            TemporaryRootDirectory = workspaceRoot,
            OutputStabilityDelay = TimeSpan.FromMilliseconds(1)
        });
        string untouchedInput = Path.Combine(_root, "user-owned.stp");
        await File.WriteAllTextAsync(untouchedInput, "do-not-change");

        GeometryLabTemporaryStepExportResult result = await exporter.ExportAsync(
            new object(),
            async (_, output, _) =>
            {
                await File.WriteAllTextAsync(output, "ISO-10303-21");
                return true;
            });

        Check(result.Status == GeometryLabTemporaryStepExportStatus.Succeeded && result.Workspace != null,
            "successful export retains an application-owned workspace for the next process stage");
        Check(result.Workspace!.StepFilePath.EndsWith("input.stp", StringComparison.OrdinalIgnoreCase) &&
              File.Exists(result.Workspace.StepFilePath),
            "successful export creates a non-empty input.stp in its GUID workspace");
        Check(await File.ReadAllTextAsync(untouchedInput) == "do-not-change",
            "temporary STEP export does not modify a user-owned STEP file");
        Check(result.Workspace.TryCleanup() && !Directory.Exists(result.Workspace.DirectoryPath) && Directory.Exists(workspaceRoot),
            "cleanup removes only the GUID workspace and preserves its containing root");
    }

    private static async Task TemporaryStepFailureAndCancellationAsync()
    {
        string workspaceRoot = Path.Combine(_root, "temporary-step-failures");
        var exporter = new GeometryLabTemporaryStepExporter(new GeometryLabTemporaryStepExporterOptions
        {
            TemporaryRootDirectory = workspaceRoot,
            OutputStabilityDelay = TimeSpan.FromMilliseconds(1)
        });

        GeometryLabTemporaryStepExportResult empty = await exporter.ExportAsync(
            new object(),
            (_, _, _) => Task.FromResult(true));
        Check(empty.Status == GeometryLabTemporaryStepExportStatus.InvalidOutput && empty.TemporaryDirectoryCleaned,
            "empty export output is rejected and its GUID workspace is cleaned");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        GeometryLabTemporaryStepExportResult cancelled = await exporter.ExportAsync(
            new object(),
            (_, _, _) => Task.FromResult(true),
            cancellation.Token);
        Check(cancelled.Status == GeometryLabTemporaryStepExportStatus.Cancelled && cancelled.Workspace == null,
            "pre-cancelled export creates no workspace and returns a structured result");

        using var cancellationAfterExport = new CancellationTokenSource();
        GeometryLabTemporaryStepExportResult cancelledAfterExport = await exporter.ExportAsync(
            new object(),
            async (_, output, _) =>
            {
                await File.WriteAllTextAsync(output, "ISO-10303-21");
                cancellationAfterExport.Cancel();
                return true;
            },
            cancellationAfterExport.Token);
        Check(cancelledAfterExport.Status == GeometryLabTemporaryStepExportStatus.Cancelled &&
              cancelledAfterExport.TemporaryDirectoryCleaned,
            "cancellation after CATIA export cleans only its temporary GUID workspace");
        Check(Directory.GetDirectories(workspaceRoot).Length == 0,
            "failure and cancellation leave no application-owned GUID workspace behind");
    }

    private static void CatiaLightInventorySyntheticTests()
    {
        var root = new CatiaLightOccurrenceRecord
        {
            OccurrenceKey = "ROOT",
            DisplayName = "Montaj",
            IsRoot = true,
            ReferenceKey = "ROOT-REF"
        };
        var groupA = new CatiaLightOccurrenceRecord
        {
            OccurrenceKey = "GROUP-A-1",
            ReferenceKey = "GROUP-REF",
            DisplayName = "Grup A"
        };
        var groupB = new CatiaLightOccurrenceRecord
        {
            OccurrenceKey = "GROUP-A-2",
            ReferenceKey = "GROUP-REF",
            DisplayName = "Grup A"
        };
        groupA.Children.Add(new CatiaLightOccurrenceRecord
        {
            OccurrenceKey = "P-1",
            ReferenceKey = "PART-1|VERSION:A",
            PartNumber = "P-1",
            DisplayName = "Parça 1",
            Revision = "A",
            IsPart = true,
            HasPartBodyReference = true
        });
        groupA.Children.Add(new CatiaLightOccurrenceRecord
        {
            OccurrenceKey = "P-2",
            ReferenceKey = "PART-1|VERSION:A",
            PartNumber = "P-1",
            DisplayName = "Parça 1",
            Revision = "A",
            IsPart = true,
            HasPartBodyReference = true
        });
        groupB.Children.Add(new CatiaLightOccurrenceRecord
        {
            OccurrenceKey = "P-3",
            ReferenceKey = "PART-2|VERSION:A",
            PartNumber = "P-2",
            DisplayName = "Parça 2",
            Revision = "A",
            IsPart = true,
            HasPartBodyReference = true
        });
        var missingIdentity = new CatiaLightOccurrenceRecord
        {
            OccurrenceKey = "P-MISSING",
            PartNumber = "Bilinmeyen",
            IsPart = true
        };
        root.Children.Add(groupA);
        root.Children.Add(groupB);
        root.Children.Add(missingIdentity);

        CatiaLightInventorySnapshot snapshot = CatiaLightInventoryService.Build(new[] { root });
        Check(snapshot.TotalOccurrenceCount == 7, "light inventory traverses root, groups and leaves once");
        Check(snapshot.UniqueParts.Count == 2 && snapshot.UniqueParts.Single(x => x.ReferenceKey == "PART-1|VERSION:A").Quantity == 2,
            "same reference occurrences merge with quantity two");
        Check(snapshot.UniqueParts.Any(x => x.ReferenceKey == "PART-2|VERSION:A") &&
              snapshot.UniqueParts.Count(x => x.ReferenceKey.Contains("PART-", StringComparison.Ordinal)) == 2,
            "different references are not merged");
        Check(snapshot.Groups.Count == 2 && snapshot.Groups.Select(x => x.HierarchyPath).Distinct(StringComparer.Ordinal).Count() == 2,
            "same group reference keeps two hierarchy occurrence positions");
        Check(snapshot.UnresolvedIdentityCount == 1 && snapshot.UniqueParts.All(x => x.TargetType == "PartBody"),
            "missing PLM identity is unresolved and not silently merged");
        Check(snapshot.Groups.All(x => x.TargetType == "Product" && x.TargetState == CatiaInventoryTargetState.Resolved) &&
              snapshot.Groups.Any(x => x.ParentGroupKey.Length == 0),
            "root is excluded as a coloring target and groups resolve as Product targets");
    }

    // Yeniden Analiz Et / deneme (docs/YENIDEN_ANALIZ_VE_DENEME_PLANI.md):
    // --parcalar and --deneme reach the engine only when asked for; a trial
    // without parts never starts; a whole-STEP run must come back Automatic.
    private static async Task SeciliParcaAdaptoruAsync()
    {
        const string govde = "\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"profileRecognitions\":[],\"parts\":[]}";
        string Json(string mod) => "{\"schemaVersion\":\"1.2\",\"analysisMode\":\"" + mod + "\",\"selectedPartIds\":[1,2]," + govde;
        var deneme = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = CreateEngine("parts-trial",
                // cmd splits "1,2" at the comma (the engine gets one argument): %6 = 1, %7 = 2.
                "if not \"%~5\"==\"--parcalar\" exit /b 5\r\nif not \"%~6\"==\"1\" exit /b 6\r\nif not \"%~7\"==\"2\" exit /b 7\r\n" +
                "if not \"%~8\"==\"--deneme\" exit /b 8\r\nif not \"%~9\"==\"sac\" exit /b 9\r\n" +
                "echo " + Json("TrialSheet") + ">\"%~4\"\r\nexit /b 0"),
            Timeout = TimeSpan.FromSeconds(10),
            TemporaryRootDirectory = Path.Combine(_root, "work"),
            SelectedPartIds = new[] { 2, 1, 2, -3 },
            Deneme = MotorDenemesi.Sac
        });
        GeometryLabProcessAdapterResult denendi = await deneme.AnalyzeAsync(_step);
        Check(denendi.IsSuccess && denendi.Analysis!.AnalysisMode == "TrialSheet" && !denendi.Analysis.Otomatik &&
              denendi.Analysis.SelectedPartIds!.SequenceEqual(new[] { 1, 2 }),
            "--parcalar (sorted, unique, positive) and --deneme are passed; the trial output is read: " + denendi.Message);

        var parcasiz = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = CreateEngine("parts-trial-refused", "exit /b 9"),
            TemporaryRootDirectory = Path.Combine(_root, "work"),
            Deneme = MotorDenemesi.Profil
        });
        Check((await parcasiz.AnalyzeAsync(_step)).Status == GeometryLabProcessAdapterStatus.InvalidConfiguration,
            "a trial without selected parts is refused before the engine starts");

        // A whole-STEP run that comes back as a selected-part run is not the STEP's analysis.
        GeometryLabProcessAdapterResult yanlis = await Adapter(CreateEngine("parts-not-automatic",
            "if not \"%~5\"==\"\" exit /b 5\r\necho " + Json("Parts") + ">\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        Check(yanlis.Status == GeometryLabProcessAdapterStatus.InvalidJson, "a whole-STEP run must be Automatic: " + yanlis.Message);
        GeometryLabProcessAdapterResult otomatik = await Adapter(CreateEngine("parts-automatic",
            "echo " + Json("Automatic") + ">\"%~4\"\r\nexit /b 0")).AnalyzeAsync(_step);
        GeometryLabProcessAdapterResult eski = await Adapter(CreateEngine("parts-old", ValidJsonScript())).AnalyzeAsync(_step);
        Check(otomatik.IsSuccess && otomatik.Analysis!.Otomatik && eski.IsSuccess && eski.Analysis!.Otomatik &&
              eski.Analysis.AnalysisMode is null,
            "Automatic and an older engine without analysisMode are the STEP's analysis");
    }

    private static void DenemeModeliTests()
    {
        GeometryLabAnalysisTransport analysis = AssemblyAnalysis();
        var ana = new GeometryLabProcessAdapterResult { Status = GeometryLabProcessAdapterStatus.Succeeded, Analysis = analysis };
        GeometryLabProcessAdapterResult EkSonuc(params int[] idler) => new()
        {
            Status = GeometryLabProcessAdapterStatus.Succeeded,
            Analysis = analysis with
            {
                AnalysisMode = "Parts", SelectedPartIds = idler,
                Parts = analysis.Parts.Where(p => idler.Contains(p.LocalId)).ToArray()
            }
        };
        Check(MacriaProjeSatirlari.AnaAnaliz(ana).IsSuccess && !MacriaProjeSatirlari.AnaAnaliz(EkSonuc(5)).IsSuccess,
            "a selected-part run is never taken as a STEP's own analysis");

        // Rows of a run: only its parts, marked; the decision text says what happened.
        var (profil, montaj) = MacriaProjeSatirlari.EkSatirlari("C:\\m.stp", EkSonuc(1, 5), 20,
            new SatirDenemesi(MacriaProje.DenemeYeniden, "e2"));
        Check(profil.Count == 1 && montaj.Count == 1 && profil[0].PartLocalId == 1 && montaj[0].PartLocalId == 5 &&
              profil[0].Deneme?.EkId == "e2" && montaj[0].Deneme?.Mod == MacriaProje.DenemeYeniden,
            "a run's rows: one per analysed part, marked with the run");
        Check(montaj[0].KullaniciKarariMetni == "Yeniden analiz edildi (süre sınırı yok)" && montaj[0].DurumEtiketi == montaj[0].StatusDisplay &&
              !montaj[0].StatusDisplay.Contains("deneme") && ((IAnalizSatiri)profil[0]).ParcaLocalId == 1,
            "Yeniden analiz: the user decision column says so; the status has no \"(deneme)\"");
        var profilDenemesi = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\m.stp", PartName = "Kutu", PartQuantity = 3, PartLocalId = 1 };
        profilDenemesi.Apply(MontajParcaSatiri.ResultForPart(ana, analysis.Parts[0]));
        profilDenemesi.DenemeyiIsaretle(new SatirDenemesi(MacriaProje.DenemeProfil, "e3"));
        Check(profilDenemesi.DurumEtiketi.EndsWith(" (deneme)") && profilDenemesi.KullaniciKarariMetni.StartsWith("Profil olarak denendi") &&
              ((IAnalizSatiri)profilDenemesi).Ayrintilar.Any(x => x.Key == "Durum" && x.Value.EndsWith(" (deneme)")),
            "a trial shows \"(deneme)\" after the status, in Seçili Parça too");

        // Sac olarak dene: a part the engine now calls a sheet is approved (no DXF: from CATIA).
        GeometryLabProcessAdapterResult sacSonuc = EkSonuc(5) with
        {
            Analysis = EkSonuc(5).Analysis! with
            {
                AnalysisMode = "TrialSheet",
                Parts = new[] { analysis.Parts[4] with { Classification = "Sheet", ClassificationCode = MotorSinifKodu.Sheet, RecognitionEvidence = true } },
                SheetMetalAnalyses = new[]
                {
                    new GeometryLabSheetMetalTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, Status = "Recognized", ThicknessMm = 44 }
                }
            }
        };
        var (_, sacMontaj) = MacriaProjeSatirlari.EkSatirlari("C:\\m.stp", sacSonuc, 20, new SatirDenemesi(MacriaProje.DenemeSac, "e4"));
        Check(sacMontaj.Count == 1 && sacMontaj[0].EffectiveCategory == MontajParcaKategorisi.Sac && sacMontaj[0].Sekme == AnalizSekmesi.Saclar &&
              sacMontaj[0].StatusDisplay == "Sac (deneme)" && sacMontaj[0].IsThickPlate &&
              sacMontaj[0].KullaniciKarariMetni.StartsWith("Sac olarak denendi; Sac olarak onaylandı (açınım yok, DXF CATIA'dan)"),
            "a sheet trial's sheet is approved, marked \"(deneme)\", grouped by its thickness: " + sacMontaj[0].StatusDisplay + " / " +
            sacMontaj[0].KullaniciKarariMetni);
        Check(IslemeMetni.Kisa(true, new[] { "Machined" }) == "var (kabuk dışı)", "trial machining has its own text");
        // (3) A trial that found nothing: no "(deneme)" in the status, the user column and reason say it.
        var (_, denenmisDiger) = MacriaProjeSatirlari.EkSatirlari("C:\\m.stp", EkSonuc(5) with
        {
            Analysis = EkSonuc(5).Analysis! with { AnalysisMode = "TrialSheet" }
        }, 20, new SatirDenemesi(MacriaProje.DenemeSac, "e5"));
        Check(denenmisDiger.Count == 1 && !denenmisDiger[0].StatusDisplay.Contains("deneme") && !denenmisDiger[0].DenemeSonucuDegistirdi &&
              denenmisDiger[0].KullaniciKarariMetni.StartsWith("Sac olarak denendi"),
            "an unchanged trial row has no \"(deneme)\": " + denenmisDiger[0].StatusDisplay);

        // Decisions: a run is a "Dene" decision naming the run, with the part.
        List<MacriaProjeKarari> kararlar = MacriaProjeSatirlari.KararlariTopla(profil, montaj, _ => "k1");
        MacriaProjeKarari? dene = kararlar.FirstOrDefault(k => k.Karar == MacriaProje.KararDene && k.Parca?.LocalId == 5);
        Check(kararlar.Count(k => k.Karar == MacriaProje.KararDene) == 2 && dene?.EkAnaliz == "e2" &&
              dene.DenemeModu == MacriaProje.DenemeYeniden && dene.Parca?.Ad == "Mil",
            "every run row gives a Dene decision with the run and the part");

        // Matching: by localId on the same output, whatever the row type
        // there; after a rescan by product id / name.
        var anaSatirlar = MacriaProjeSatirlari.MontajSatirlari("C:\\m.stp", ana, 20);
        List<MacriaKararAdayi> adaylar = MacriaProjeSatirlari.Adaylar(anaSatirlar.Profil, anaSatirlar.Montaj, _ => "k1");
        var profilHedefli = new MacriaProjeKarari
        {
            Kaynak = "k1", Hedef = MacriaProje.HedefProfil, Karar = MacriaProje.KararDene, DenemeModu = MacriaProje.DenemeProfil,
            EkAnaliz = "e3", Parca = new MacriaParcaKimligi { LocalId = 5, Ad = "Mil" }
        };
        var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(new[] { profilHedefli }, adaylar, _ => false);
        Check(eslenen.Count == 1 && eslenemeyen.Count == 0 && ((IAnalizSatiri)eslenen[0].Aday.Satir).ParcaLocalId == 5 &&
              eslenen[0].Aday.Hedef == MacriaProje.HedefMontaj,
            "a Dene decision saved on a profile row finds the part row it was tried from");
        var (yeniden, _) = MacriaProje.KararlariEsle(new[] { new MacriaProjeKarari
        {
            Kaynak = "k1", Hedef = MacriaProje.HedefMontaj, Karar = MacriaProje.KararDene, DenemeModu = MacriaProje.DenemeYeniden,
            EkAnaliz = "e1", Parca = new MacriaParcaKimligi { LocalId = 99, Ad = "Mil" }
        } }, adaylar, _ => true);
        Check(yeniden.Count == 1 && ((IAnalizSatiri)yeniden[0].Aday.Satir).ParcaLocalId == 5,
            "after a rescan a Dene decision follows the part's name");
        Check(!MacriaProjeSatirlari.Uygula(profilHedefli, anaSatirlar.Montaj[0]),
            "a Dene decision is not applied as a row decision");

        // Toolbar: Yeniden Analiz Et on Kontrol gerekli and Tanımsız, for parts, not while the engine runs.
        IAnalizSatiri[] secili = { montaj[0] };
        AnalizAracDurumu kontrol = AnalizAracDurumu.Hesapla(AnalizSekmesi.KontrolGerekli, secili, true, false);
        Check(kontrol.YenidenAnalizGorunur && kontrol.YenidenAnalizEtkin && kontrol.OtomatikEtkin &&
              AnalizAracDurumu.Hesapla(AnalizSekmesi.Tanimsiz, secili, true, false).YenidenAnalizGorunur &&
              !AnalizAracDurumu.Hesapla(AnalizSekmesi.Saclar, secili, true, false).YenidenAnalizGorunur &&
              !AnalizAracDurumu.Hesapla(AnalizSekmesi.Profiller, secili, true, false).YenidenAnalizGorunur &&
              !AnalizAracDurumu.Hesapla(AnalizSekmesi.KontrolGerekli, secili, true, false, motorMesgul: true).YenidenAnalizEtkin,
            "Yeniden Analiz Et: Kontrol gerekli and Tanımsız only, off while the engine runs; Otomatik Karara Dön undoes it");
        // A shaft the profile recognizer measured is closed to the trials (not to Yeniden Analiz Et).
        GeometryLabAnalysisTransport milli = analysis with
        {
            ProfileRecognitions = new[]
            {
                new GeometryLabProfileRecognitionTransport { SolidId = new GeometryLabLocalIdTransport { LocalId = 5 }, SectionRecognitionStatus = "Recognized",
                    ProfileType = "SolidCircularBar", OuterDiameterMm = 20 }
            }
        };
        MontajParcaSatiri milSatiri = MontajParcaSatiri.Olustur("C:\\m.stp", milli,
            analysis.Parts[4] with { ClassificationCode = MotorSinifKodu.SolidBar, RecognitionEvidence = true }, null, 20);
        AnalizAracDurumu milDurumu = AnalizAracDurumu.Hesapla(AnalizSekmesi.KontrolGerekli, new IAnalizSatiri[] { milSatiri }, true, false);
        Check(milSatiri.DenemeyeKapaliNedeni == DenemeKapisi.DoluMil && !milDurumu.DenemeEtkin && milDurumu.YenidenAnalizEtkin &&
              milDurumu.DenemeKapaliNedeni == "Dolu mil olarak tanındı; denemeye kapalı",
            "a shaft: Profil / Sac olarak dene off with the reason, Yeniden Analiz Et on");
        AnalizAracDurumu karisik = AnalizAracDurumu.Hesapla(AnalizSekmesi.KontrolGerekli, new IAnalizSatiri[] { milSatiri, montaj[0] }, true, false);
        Check(karisik.DenemeEtkin && karisik.DenemeKapaliNedeni == null && montaj[0].DenemeyeKapaliNedeni == null,
            "a shaft and another part selected: the trials run on the other part");
        Check(DenemeKapisi.Neden("SquareHollowSection") == null && DenemeKapisi.Neden(null) == null, "only the listed kinds are closed");
        var dosyaSatiri = new GeometryLabStepProfileListItem { SourceStepPath = "C:\\eski.stp" };
        Check(!AnalizAracDurumu.Hesapla(AnalizSekmesi.Tanimsiz, new IAnalizSatiri[] { dosyaSatiri }, true, false).YenidenAnalizEtkin,
            "a file row without engine parts cannot be re-analysed by part");
    }

    // .macria 1.3: selected-part runs are stored under kaynaklar/<k>/ek/<e>/ and come back.
    private static void EkAnalizProjeTests()
    {
        string klasor = Path.Combine(_root, "ek-proje");
        Directory.CreateDirectory(klasor);
        string step = Path.Combine(klasor, "Montaj.stp");
        File.WriteAllText(step, "ISO-10303-21;");
        string ekDxf = Path.Combine(klasor, "ek-dxf");
        Directory.CreateDirectory(ekDxf);
        File.WriteAllText(Path.Combine(ekDxf, "part-5.dxf"), "0\nEOF\n");
        const string anaJson = "{\"schemaVersion\":\"1.2\",\"analysisMode\":\"Automatic\"}";
        const string ekJson = "{\"schemaVersion\":\"1.2\",\"analysisMode\":\"Parts\",\"selectedPartIds\":[5]}";
        MacriaProjeVerisi Veri() => new()
        {
            Kaynaklar =
            {
                new MacriaProjeKaynagi
                {
                    Id = "k1", Yol = step, Sha256 = GeometryLabMotorKimligi.DosyaSha256(step),
                    Analiz = new MacriaProjeAnalizi { Durum = nameof(GeometryLabProcessAdapterStatus.Succeeded), MotorSemaSurumu = "1.2" },
                    EkAnalizler =
                    {
                        new MacriaEkAnaliz { Id = "e1", Mod = MacriaProje.DenemeYeniden, Parcalar = { 5 }, SureSn = 3.2 },
                        new MacriaEkAnaliz { Id = "e2", Mod = MacriaProje.DenemeProfil, Parcalar = { 7 } }
                    }
                }
            },
            Kararlar =
            {
                new MacriaProjeKarari { Kaynak = "k1", Hedef = MacriaProje.HedefMontaj, Karar = MacriaProje.KararDene,
                    DenemeModu = MacriaProje.DenemeYeniden, EkAnaliz = "e1", Parca = new MacriaParcaKimligi { LocalId = 5, Ad = "Mil" } }
            }
        };
        string proje = Path.Combine(klasor, "Montaj.macria");
        // e2 has no stored output (as after Otomatik Karara Dön): only e1 is written.
        MacriaProje.Kaydet(proje, Veri(), new Dictionary<string, MacriaProjeKaynakIcerigi>
        {
            ["k1"] = new(anaJson, null, new Dictionary<string, MacriaEkIcerik> { ["e1"] = new(ekJson, ekDxf) })
        }, "Macria test");
        MacriaProjeAcilisi acilis = MacriaProje.Ac(proje, Path.Combine(_root, "ek-acilan"));
        Check(acilis.Manifest.SchemaVersion == "1.3" && acilis.SaltOkunurNedeni == null, "schema 1.3 is written and opens writable");
        Check(acilis.EkAnalysisJson.TryGetValue("k1/e1", out string? okunan) && okunan == ekJson && !acilis.EkAnalysisJson.ContainsKey("k1/e2") &&
              acilis.EkDxfKlasoru.TryGetValue("k1/e1", out string? dxfKlasoru) && File.Exists(Path.Combine(dxfKlasoru, "part-5.dxf")) &&
              acilis.AnalysisJson["k1"] == anaJson,
            "a run's output and DXFs come back next to the STEP's own analysis");
        MacriaEkAnaliz ek = acilis.Veri.Kaynaklar[0].EkAnalizler[0];
        MacriaProjeKarari dene = acilis.Veri.Kararlar[0];
        Check(ek.Mod == MacriaProje.DenemeYeniden && ek.Parcalar.SequenceEqual(new[] { 5 }) && ek.SureSn == 3.2 &&
              dene.Karar == MacriaProje.KararDene && dene.EkAnaliz == "e1" && dene.DenemeModu == MacriaProje.DenemeYeniden,
            "the run record and the Dene decision round-trip");

        // A 1.2 project (no runs) still opens; a run with a bad id is refused.
        string eskiProje = Path.Combine(klasor, "Eski.macria");
        using (var zip = System.IO.Compression.ZipFile.Open(eskiProje, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Yaz(string ad, string metin)
            {
                using var akis = new StreamWriter(zip.CreateEntry(ad).Open(), new UTF8Encoding(false));
                akis.Write(metin);
            }
            Yaz("manifest.json", "{\"format\":\"macria-proje\",\"schemaVersion\":\"1.2\"}");
            Yaz("proje.json", "{\"kaynaklar\":[{\"id\":\"k1\",\"yol\":\"" + step.Replace("\\", "\\\\") + "\"}],\"kararlar\":[]}");
        }
        MacriaProjeAcilisi eskiAcilis = MacriaProje.Ac(eskiProje, Path.Combine(_root, "eski-acilan"));
        Check(eskiAcilis.SaltOkunurNedeni == null && eskiAcilis.Veri.Kaynaklar[0].EkAnalizler.Count == 0,
            "a schema 1.2 project opens writable without runs");
        MacriaProjeVerisi bozuk = Veri();
        bozuk.Kaynaklar[0].EkAnalizler[1].Id = "../x";
        string bozukProje = Path.Combine(klasor, "Bozuk.macria");
        MacriaProje.Kaydet(bozukProje, bozuk, new Dictionary<string, MacriaProjeKaynakIcerigi>(), "Macria test");
        Check(Throws(() => MacriaProje.Ac(bozukProje, Path.Combine(_root, "bozuk-acilan"))), "a run id that is not e<n> is refused");
    }

    private static GeometryLabProcessAdapter Adapter(string engine, TimeSpan? timeout = null) => new(
        new GeometryLabProcessAdapterOptions
        {
            EngineExecutablePath = engine,
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
            TemporaryRootDirectory = Path.Combine(_root, "work")
        });

    private static string CreateEngine(string name, string body)
    {
        string path = Path.Combine(_root, name + ".cmd");
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n", Encoding.ASCII);
        return path;
    }

    // ~5 s wait without reading stdin: "timeout /t" exits at once when input is redirected (CI, agents).
    private static string SlowEngineScript() => "ping -n 6 127.0.0.1 >nul\r\nexit /b 0";

    private static string ValidJsonScript() =>
        "echo {\"schemaVersion\":\"1.0\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"profileRecognitions\":[]}>\"%~4\"\r\nexit /b 0";

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
