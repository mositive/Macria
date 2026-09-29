using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Macria;

public sealed record GeometryLabProcessAdapterOptions
{
    public required string EngineExecutablePath { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
    public string? TemporaryRootDirectory { get; init; }

    // When set, the engine writes one DXF per sheet part (--dxf-klasor) into a
    // new, unique folder under this root. The folder outlives the analysis;
    // naming, moving and cleaning the files is the caller's job.
    public string? PartDxfRootDirectory { get; init; }
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
        if (string.IsNullOrWhiteSpace(_options.EngineExecutablePath) || _options.Timeout <= TimeSpan.Zero)
            return Result(GeometryLabProcessAdapterStatus.InvalidConfiguration,
                "EngineExecutablePath and a positive timeout are required.");

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

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.String)
                return Result(GeometryLabProcessAdapterStatus.InvalidJson,
                    "GeometryLab JSON does not contain a string schemaVersion.", stdout, stderr, process.ExitCode);
            if (!IsSupportedSchemaVersion(schemaElement.GetString()))
                return Result(GeometryLabProcessAdapterStatus.UnsupportedSchema,
                    "GeometryLab JSON schemaVersion is not supported.", stdout, stderr, process.ExitCode);

            GeometryLabAnalysisTransport? analysis = JsonSerializer.Deserialize<GeometryLabAnalysisTransport>(json, JsonOptions);
            if (analysis is null)
                return Result(GeometryLabProcessAdapterStatus.InvalidJson,
                    "GeometryLab JSON could not be deserialized.", stdout, stderr, process.ExitCode);
            return new GeometryLabProcessAdapterResult
            {
                Status = GeometryLabProcessAdapterStatus.Succeeded,
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Analysis = analysis,
                HasMultipleProfileResults = analysis.ProfileRecognitions.Count > 1
            };
        }
        catch (JsonException exception)
        {
            return Result(GeometryLabProcessAdapterStatus.InvalidJson, exception.Message, stdout, stderr, process.ExitCode);
        }
        catch (IOException exception)
        {
            return Result(GeometryLabProcessAdapterStatus.InvalidJson, exception.Message, stdout, stderr, process.ExitCode);
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
