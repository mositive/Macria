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
            PreviewCoreTests();
            PreviewInventoryTests();
            PreviewContentCheckTests();
            OcctStepPreviewAdapterTests();
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
        string? engine = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_EXE");
        string? step = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_STEP");
        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(step) || !File.Exists(engine) || !File.Exists(step))
        {
            Console.WriteLine("REAL_ENGINE: SKIPPED - MACRIA_GEOMETRY_ENGINE_EXE / MACRIA_GEOMETRY_ENGINE_STEP unavailable.");
            return;
        }

        GeometryLabProcessAdapterResult result = await Adapter(engine, TimeSpan.FromMinutes(1)).AnalyzeAsync(step);
        Check(result.Status == GeometryLabProcessAdapterStatus.Succeeded, "real engine succeeds through adapter");
        Check(result.Analysis?.SchemaVersion == GeometryLabProcessAdapter.SupportedSchemaVersion,
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

    private static async Task RealBaseStockEngineAsync()
    {
        string? engine = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_EXE");
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
        string? engine = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_EXE");
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
        string? engine = Environment.GetEnvironmentVariable("MACRIA_GEOMETRY_ENGINE_EXE");
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
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("timeout", "timeout /t 5 /nobreak >nul\r\nexit /b 0"),
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

    private static async Task CancellationAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        GeometryLabProcessAdapterResult result = await Adapter(CreateEngine("cancel", "timeout /t 5 /nobreak >nul\r\nexit /b 0"),
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

        var filters = new GeometryLabExternalStepFilterState();
        Check(rows.Count(x => filters.IsVisible(x.ResultGroup)) == 2,
            "default filters show normal and processed definite profiles together");
        Check(filters.TumunuDegistirMetni == "Tümünü Göster",
            "the bulk filter button initially offers to show hidden groups");
        filters.TumunuGorunurYap(true);
        Check(rows.All(x => filters.IsVisible(x.ResultGroup)) && filters.TumunuDegistirMetni == "Tümünü Gizle",
            "bulk show reveals all four filter groups and updates its caption");
        filters.TumunuGorunurYap(false);
        Check(rows.All(x => !filters.IsVisible(x.ResultGroup)) && filters.TumunuDegistirMetni == "Tümünü Göster",
            "bulk hide hides all groups without mutating retained rows");
        filters.TumunuGorunurYap(true);
        filters.TumunuGorunurYap(false);
        filters.DefiniteProfilesVisible = true;
        Check(rows.Count(x => filters.IsVisible(x.ResultGroup)) == 2,
            "the definite filter contains both normal and processed profiles exactly once");
        filters.DefiniteProfilesVisible = false;
        Check(rows.Count(x => filters.IsVisible(x.ResultGroup)) == 0,
            "all filters disabled hide the table rows without changing the session results");
        filters.ReviewRequiredVisible = filters.ExcludedVisible = filters.UnclassifiedVisible = true;
        Check(rows.Count(x => filters.IsVisible(x.ResultGroup)) == 3,
            "review, excluded, and unclassified filters independently restore their rows");
        Check(unclassified.ExplanationDisplay == "Kararlı ve tam profil kesiti bulunamadı." &&
              !unclassified.ExplanationDisplay.Contains("Stable representative", StringComparison.Ordinal),
            "user-facing explanations translate known engine reasons and never expose the English text");

        review.MoveToReview();
        Check(review.AutomaticCategory == GeometryLabExternalStepResultGroup.ReviewRequired &&
              review.EffectiveCategory == GeometryLabExternalStepResultGroup.ReviewRequired &&
              review.DecisionSource == GeometryLabDecisionSource.User && review.HasUserDecision &&
              review.DecisionToolTip.Contains("Karar kaynağı: Kullanıcı", StringComparison.Ordinal),
            "a user decision preserves the automatic category and exposes its source");
        review.ExcludeFromList("Teknik inceleme sonrası");
        Check(review.EffectiveCategory == GeometryLabExternalStepResultGroup.Excluded &&
              review.UserDecisionNote == "Teknik inceleme sonrası" && review.AnalysisStatus == "Liste dışı",
            "bulk-compatible user exclusion changes only the effective presentation");
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
        Check(item.EffectiveStatusDisplay == "Liste dışı", "6A-10b effective CATIA exclusion is shown in status column");
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
        item.ExcludeFromList(); Check(item.HasUserDecision, "6A-17 user decision exists");
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

    private static string ValidJsonScript() =>
        "echo {\"schemaVersion\":\"1.0\",\"status\":\"Succeeded\",\"exitCode\":0,\"errors\":[],\"warnings\":[],\"profileRecognitions\":[]}>\"%~4\"\r\nexit /b 0";

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
