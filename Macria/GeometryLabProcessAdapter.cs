using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Macria;

public sealed record GeometryLabProcessAdapterOptions
{
    public required string EngineExecutablePath { get; init; }
    // Whole-run limit; zero or negative means no limit (the engine has its
    // own per-part limit, PartTimeLimitSeconds).
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    // Engine progress (--ilerleme), called on a background thread as the
    // engine reports stages and parts; null: no progress lines are requested.
    public Action<GeometryLabProgress>? ProgressChanged { get; init; }
    public string? TemporaryRootDirectory { get; init; }

    // When set, the engine writes one DXF per sheet part (--dxf-klasor) into a
    // new, unique folder under this root. The folder outlives the analysis;
    // naming, moving and cleaning the files is the caller's job.
    public string? PartDxfRootDirectory { get; init; }

    // Engine --parca-sure-siniri: seconds per part, 0 = no limit; null leaves
    // the engine default (120 s). A part over it comes back ReviewRequired.
    public int? PartTimeLimitSeconds { get; init; }

    // Engine --is-parcacigi: worker threads, 0 = cores - 1; null = engine default.
    public int? ThreadCount { get; init; }

    // Engine --parcalar: only these parts (localIds of the full analysis);
    // null or empty = the whole STEP, the STEP's own analysis.
    public IReadOnlyList<int>? SelectedPartIds { get; init; }

    // Engine --deneme: "Profil / Sac olarak dene" on the selected parts only.
    public MotorDenemesi Deneme { get; init; } = MotorDenemesi.Yok;

    // Engine --step-yaz / --step-klasor (with SelectedPartIds): each selected
    // part is also written here as part-<id>.stp and read back (Analysis.PartSteps).
    public string? PartStepDirectory { get; init; }
}

/// <summary>The engine's trial modes (--deneme); Yok runs the automatic rules.</summary>
public enum MotorDenemesi
{
    Yok,
    Profil,
    Sac
}

/// <summary>
/// One engine progress line "MACRIA-ILERLEME &lt;stage&gt; &lt;done&gt; &lt;total&gt;":
/// "okuma" (reading the STEP), "topoloji", then "parca" and "sac" per solid.
/// </summary>
public sealed record GeometryLabProgress(string Stage, int Done, int Total)
{
    public const string LinePrefix = "MACRIA-ILERLEME ";

    public static GeometryLabProgress? TryParse(string? line)
    {
        if (line is null || !line.StartsWith(LinePrefix, StringComparison.Ordinal))
            return null;
        string[] fields = line.Substring(LinePrefix.Length).Trim().Split(' ');
        if (fields.Length != 3 || fields[0].Length == 0 ||
            !int.TryParse(fields[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int done) ||
            !int.TryParse(fields[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int total))
            return null;
        return new GeometryLabProgress(fields[0], done, total);
    }

    // "STEP okunuyor", "Parçalar analiz ediliyor: 142 / 195", ...
    public string Display => Stage switch
    {
        "okuma" => "STEP okunuyor",
        "topoloji" => "Topoloji çıkarılıyor (" + Total + " parça)",
        "parca" => "Parçalar analiz ediliyor: " + Done + " / " + Total,
        "sac" => "Sac tanıma: " + Done + " / " + Total,
        _ => Stage + ": " + Done + " / " + Total
    };
}

public enum GeometryLabProcessAdapterStatus
{
    Succeeded,
    InvalidConfiguration,
    EngineNotFound,
    StepNotFound,
    StartFailed,
    TimedOut,
    Cancelled,
    EngineFailed,
    JsonMissing,
    InvalidJson,
    UnsupportedSchema
}

public sealed record GeometryLabProcessAdapterResult
{
    public GeometryLabProcessAdapterStatus Status { get; init; }
    public string? Message { get; init; }
    public int? ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public GeometryLabAnalysisTransport? Analysis { get; init; }
    // The engine's analysis.json exactly as written; a .macria project stores it.
    public string? AnalysisJson { get; init; }
    public bool HasMultipleProfileResults { get; init; }
    public bool TemporaryDirectoryCleaned { get; init; }
    // Folder the engine wrote the part DXFs into; null without PartDxfRootDirectory.
    public string? PartDxfDirectory { get; init; }
    public bool IsSuccess => Status == GeometryLabProcessAdapterStatus.Succeeded;

    // Full path of a part's engine DXF (with or without bend information),
    // or null when none was written.
    public string? PartDxfPath(GeometryLabPartTransport part, bool cutOnly = false)
    {
        string? file = cutOnly ? part.DxfCutOnlyFile : part.DxfFile;
        if (PartDxfDirectory is null || string.IsNullOrWhiteSpace(file) ||
            file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return Path.Combine(PartDxfDirectory, file);
    }
}

/// <summary>
/// Isolated bridge to the externally supplied GeometryEngine executable.
/// It never passes CATIA COM objects to the process and never deletes the input STEP file.
/// </summary>
public sealed class GeometryLabProcessAdapter
{
    public const string SupportedSchemaVersion = "1.0";

    // 1.1 adds sheetMetal, sheetMetalAnalyses and holeFeatures; 1.2 adds parts.
    // Every 1.0 field is unchanged.
    public static readonly IReadOnlyList<string> SupportedSchemaVersions = new[] { SupportedSchemaVersion, "1.1", "1.2" };

    public static bool IsSupportedSchemaVersion(string? schemaVersion)
    {
        foreach (string supported in SupportedSchemaVersions)
        {
            if (string.Equals(schemaVersion, supported, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GeometryLabProcessAdapterOptions _options;

    public GeometryLabProcessAdapter(GeometryLabProcessAdapterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<GeometryLabProcessAdapterResult> AnalyzeAsync(
        string stepFilePath,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Result(GeometryLabProcessAdapterStatus.Cancelled, "Analysis was cancelled before the engine started.");
        if (string.IsNullOrWhiteSpace(_options.EngineExecutablePath))
            return Result(GeometryLabProcessAdapterStatus.InvalidConfiguration,
                "EngineExecutablePath is required.");
        // A trial judges only parts the user selected, never a whole STEP.
        if (_options.Deneme != MotorDenemesi.Yok && SeciliParcalar.Count == 0)
            return Result(GeometryLabProcessAdapterStatus.InvalidConfiguration,
                "Deneme (profil / sac olarak dene) yalnız seçili parçalarda çalışır.");

        string enginePath;
        string inputPath;
        try
        {
            enginePath = Path.GetFullPath(_options.EngineExecutablePath);
            inputPath = Path.GetFullPath(stepFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(GeometryLabProcessAdapterStatus.InvalidConfiguration, exception.Message);
        }

        if (!File.Exists(enginePath))
            return Result(GeometryLabProcessAdapterStatus.EngineNotFound, "GeometryLab engine executable was not found.");
        if (!File.Exists(inputPath))
            return Result(GeometryLabProcessAdapterStatus.StepNotFound, "Input STEP file was not found.");

        string temporaryRoot;
        try
        {
            temporaryRoot = string.IsNullOrWhiteSpace(_options.TemporaryRootDirectory)
                ? Path.Combine(Path.GetTempPath(), "Macria", "GeometryLab")
                : Path.GetFullPath(_options.TemporaryRootDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(GeometryLabProcessAdapterStatus.InvalidConfiguration, exception.Message);
        }

        string? partDxfDirectory = null;
        if (!string.IsNullOrWhiteSpace(_options.PartDxfRootDirectory))
        {
            try
            {
                partDxfDirectory = Path.Combine(Path.GetFullPath(_options.PartDxfRootDirectory), Guid.NewGuid().ToString("N"));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Result(GeometryLabProcessAdapterStatus.InvalidConfiguration, exception.Message);
            }
        }

        string workDirectory = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
        GeometryLabProcessAdapterResult result;
        bool cleaned = false;
        try
        {
            Directory.CreateDirectory(workDirectory);
            result = await RunEngineAsync(enginePath, inputPath, workDirectory, partDxfDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            result = Result(GeometryLabProcessAdapterStatus.StartFailed, exception.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, true);
                cleaned = !Directory.Exists(workDirectory);
            }
            catch
            {
                cleaned = false;
            }
        }

        return result with
        {
            TemporaryDirectoryCleaned = cleaned,
            PartDxfDirectory = result.IsSuccess ? partDxfDirectory : null
        };
    }

    private async Task<GeometryLabProcessAdapterResult> RunEngineAsync(
        string enginePath,
        string inputPath,
        string workDirectory,
        string? partDxfDirectory,
        CancellationToken cancellationToken)
    {
        string outputPath = Path.Combine(workDirectory, "analysis.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = enginePath,
            WorkingDirectory = Path.GetDirectoryName(enginePath) ?? workDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        if (partDxfDirectory is not null)
        {
            startInfo.ArgumentList.Add("--dxf-klasor");
            startInfo.ArgumentList.Add(partDxfDirectory);
        }
        if (_options.PartTimeLimitSeconds is int partTimeLimit)
        {
            startInfo.ArgumentList.Add("--parca-sure-siniri");
            startInfo.ArgumentList.Add(Math.Max(0, partTimeLimit).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (_options.ThreadCount is int threadCount)
        {
            startInfo.ArgumentList.Add("--is-parcacigi");
            startInfo.ArgumentList.Add(Math.Max(0, threadCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (SeciliParcalar.Count > 0 && !string.IsNullOrWhiteSpace(_options.PartStepDirectory))
        {
            startInfo.ArgumentList.Add("--step-yaz");
            startInfo.ArgumentList.Add(string.Join(",", SeciliParcalar.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            startInfo.ArgumentList.Add("--step-klasor");
            startInfo.ArgumentList.Add(_options.PartStepDirectory!);
        }
        else if (SeciliParcalar.Count > 0)
        {
            startInfo.ArgumentList.Add("--parcalar");
            startInfo.ArgumentList.Add(string.Join(",", SeciliParcalar.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        if (_options.Deneme != MotorDenemesi.Yok)
        {
            startInfo.ArgumentList.Add("--deneme");
            startInfo.ArgumentList.Add(_options.Deneme == MotorDenemesi.Profil ? "profil" : "sac");
        }
        Action<GeometryLabProgress>? progress = _options.ProgressChanged;
        if (progress is not null)
            startInfo.ArgumentList.Add("--ilerleme");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                return Result(GeometryLabProcessAdapterStatus.StartFailed, "GeometryLab engine process did not start.");
        }
        catch (Exception exception)
        {
            return Result(GeometryLabProcessAdapterStatus.StartFailed, exception.Message);
        }

        Task<string> stdoutTask = progress is null
            ? process.StandardOutput.ReadToEndAsync()
            : ReadLinesAsync(process.StandardOutput, progress);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.Timeout > TimeSpan.Zero)
            timeoutCancellation.CancelAfter(_options.Timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            bool cancelled = cancellationToken.IsCancellationRequested;
            TryKillProcessTree(process);
            await WaitForTerminationAsync(process).ConfigureAwait(false);
            return Result(
                cancelled ? GeometryLabProcessAdapterStatus.Cancelled : GeometryLabProcessAdapterStatus.TimedOut,
                cancelled ? "GeometryLab analysis was cancelled." : "GeometryLab analysis timed out.",
                ReadCompleted(stdoutTask), ReadCompleted(stderrTask),
                process.HasExited ? process.ExitCode : null);
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            return Result(GeometryLabProcessAdapterStatus.EngineFailed,
                "GeometryLab engine returned a non-zero exit code.", stdout, stderr, process.ExitCode);
        if (!File.Exists(outputPath))
            return Result(GeometryLabProcessAdapterStatus.JsonMissing,
                "GeometryLab engine completed without producing analysis JSON.", stdout, stderr, process.ExitCode);

        string json;
        try
        {
            json = await File.ReadAllTextAsync(outputPath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            return Result(GeometryLabProcessAdapterStatus.InvalidJson, exception.Message, stdout, stderr, process.ExitCode);
        }
        GeometryLabProcessAdapterResult sonuc = SonucuJsondanKur(json, partDxfDirectory) with
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr
        };
        // A whole-STEP run must come back as the STEP's own (automatic) analysis.
        if (sonuc.IsSuccess && SeciliParcalar.Count == 0 && !sonuc.Analysis!.Otomatik)
            return Result(GeometryLabProcessAdapterStatus.InvalidJson,
                "Motor çıktısı otomatik analiz değil (" + sonuc.Analysis.AnalysisMode + ").", stdout, stderr, process.ExitCode);
        return sonuc;
    }

    private IReadOnlyList<int> SeciliParcalar =>
        _options.SelectedPartIds?.Where(id => id > 0).Distinct().OrderBy(id => id).ToList() ?? (IReadOnlyList<int>)Array.Empty<int>();

    /// <summary>
    /// The result of an engine analysis.json: the live analysis and a .macria
    /// project's stored engine output both go through here (schema check and
    /// deserialization). `partDxfDirectory` is where the part DXFs are now.
    /// </summary>
    public static GeometryLabProcessAdapterResult SonucuJsondanKur(string json, string? partDxfDirectory)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.String)
                return Result(GeometryLabProcessAdapterStatus.InvalidJson,
                    "GeometryLab JSON does not contain a string schemaVersion.");
            if (!IsSupportedSchemaVersion(schemaElement.GetString()))
                return Result(GeometryLabProcessAdapterStatus.UnsupportedSchema,
                    "GeometryLab JSON schemaVersion is not supported.");

            GeometryLabAnalysisTransport? analysis = JsonSerializer.Deserialize<GeometryLabAnalysisTransport>(json, JsonOptions);
            if (analysis is null)
                return Result(GeometryLabProcessAdapterStatus.InvalidJson,
                    "GeometryLab JSON could not be deserialized.");
            return new GeometryLabProcessAdapterResult
            {
                Status = GeometryLabProcessAdapterStatus.Succeeded,
                Analysis = analysis,
                AnalysisJson = json,
                PartDxfDirectory = partDxfDirectory,
                HasMultipleProfileResults = analysis.ProfileRecognitions.Count > 1
            };
        }
        catch (JsonException exception)
        {
            return Result(GeometryLabProcessAdapterStatus.InvalidJson, exception.Message);
        }
    }

    private static GeometryLabProcessAdapterResult Result(
        GeometryLabProcessAdapterStatus status,
        string message,
        string stdout = "",
        string stderr = "",
        int? exitCode = null) => new()
        {
            Status = status,
            Message = message,
            ExitCode = exitCode,
            StandardOutput = stdout,
            StandardError = stderr
        };

    // Reads stdout line by line, reports each progress line and returns the
    // whole text (as ReadToEndAsync would). A failing callback does not stop
    // the reading.
    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<GeometryLabProgress> progress)
    {
        var text = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            text.AppendLine(line);
            if (GeometryLabProgress.TryParse(line) is GeometryLabProgress report)
            {
                try { progress(report); }
                catch (Exception) { }
            }
        }
        return text.ToString();
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task WaitForTerminationAsync(Process process)
    {
        try
        {
            if (!process.HasExited) await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
    }

    private static string ReadCompleted(Task<string> task) => task.IsCompletedSuccessfully ? task.Result : "";
}
