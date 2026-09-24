using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Macria;

public sealed record GeometryLabTemporaryStepExporterOptions
{
    public string? TemporaryRootDirectory { get; init; }
    public TimeSpan OutputStabilityDelay { get; init; } = TimeSpan.FromMilliseconds(120);
}

public enum GeometryLabTemporaryStepExportStatus
{
    Succeeded,
    MissingRepRef,
    Cancelled,
    WorkspaceCreationFailed,
    ExportFailed,
    InvalidOutput
}

public sealed class GeometryLabTemporaryStepWorkspace
{
    internal GeometryLabTemporaryStepWorkspace(string rootDirectory, string directoryPath)
    {
        RootDirectory = rootDirectory;
        DirectoryPath = directoryPath;
        StepFilePath = Path.Combine(directoryPath, "input.stp");
    }

    public string RootDirectory { get; }
    public string DirectoryPath { get; }
    public string StepFilePath { get; }

    /// <summary>Only deletes the GUID directory created by this workspace.</summary>
    public bool TryCleanup()
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootDirectory));
            string directory = Path.GetFullPath(DirectoryPath);
            string? parent = Path.GetDirectoryName(directory);
            if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(Path.GetFileName(directory), out _))
                return false;

            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            return !Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }
}

public sealed record GeometryLabTemporaryStepExportResult
{
    public GeometryLabTemporaryStepExportStatus Status { get; init; }
    public string? Message { get; init; }
    public GeometryLabTemporaryStepWorkspace? Workspace { get; init; }
    public bool TemporaryDirectoryCleaned { get; init; }
    public bool IsSuccess => Status == GeometryLabTemporaryStepExportStatus.Succeeded;
}

/// <summary>
/// Creates an application-owned STEP workspace for a future GeometryLab process call.
/// The supplied export callback must be invoked from the CATIA/UI thread; this class never
/// moves COM work to Task.Run and never deletes a user-selected source file.
/// </summary>
public sealed class GeometryLabTemporaryStepExporter
{
    private readonly GeometryLabTemporaryStepExporterOptions _options;

    public GeometryLabTemporaryStepExporter(GeometryLabTemporaryStepExporterOptions? options = null)
    {
        _options = options ?? new GeometryLabTemporaryStepExporterOptions();
    }

    public async Task<GeometryLabTemporaryStepExportResult> ExportAsync(
        object? repRef,
        Func<object, string, CancellationToken, Task<bool>> exportStepAsync,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Result(GeometryLabTemporaryStepExportStatus.Cancelled,
                "STEP export was cancelled before the workspace was created.");
        if (repRef == null)
            return Result(GeometryLabTemporaryStepExportStatus.MissingRepRef,
                "The selected profile has no CATIA representation reference.");
        if (exportStepAsync == null)
            throw new ArgumentNullException(nameof(exportStepAsync));

        if (!TryCreateWorkspace(out GeometryLabTemporaryStepWorkspace workspace, out string creationError))
            return Result(GeometryLabTemporaryStepExportStatus.WorkspaceCreationFailed, creationError);

        try
        {
            bool exported;
            try
            {
                // Deliberately do not use Task.Run or ConfigureAwait(false): CATIA COM remains
                // on the caller's apartment/UI thread throughout the existing export chain.
                exported = await exportStepAsync(repRef, workspace.StepFilePath, cancellationToken);
            }
            catch (Exception exception)
            {
                return CleanupFailure(workspace, GeometryLabTemporaryStepExportStatus.ExportFailed,
                    "STEP export callback failed: " + exception.Message);
            }

            if (cancellationToken.IsCancellationRequested)
                return CleanupFailure(workspace, GeometryLabTemporaryStepExportStatus.Cancelled,
                    "STEP export was cancelled.");
            if (!exported)
                return CleanupFailure(workspace, GeometryLabTemporaryStepExportStatus.ExportFailed,
                    "CATIA did not produce a temporary STEP file.");

            string? validationError = await ValidateOutputAsync(workspace.StepFilePath, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return CleanupFailure(workspace, GeometryLabTemporaryStepExportStatus.Cancelled,
                    "STEP export was cancelled while validating the output.");
            if (validationError != null)
                return CleanupFailure(workspace, GeometryLabTemporaryStepExportStatus.InvalidOutput, validationError);

            // Success deliberately retains the workspace. The future process orchestrator owns
            // cleanup after it has consumed input.stp; callers can call Workspace.TryCleanup().
            return new GeometryLabTemporaryStepExportResult
            {
                Status = GeometryLabTemporaryStepExportStatus.Succeeded,
                Workspace = workspace
            };
        }
        catch (Exception exception)
        {
            return CleanupFailure(workspace, GeometryLabTemporaryStepExportStatus.ExportFailed,
                "Temporary STEP export failed: " + exception.Message);
        }
    }

    private bool TryCreateWorkspace(out GeometryLabTemporaryStepWorkspace workspace, out string error)
    {
        workspace = null!;
        error = "";
        try
        {
            string root = string.IsNullOrWhiteSpace(_options.TemporaryRootDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Macria", "GeometryLab")
                : _options.TemporaryRootDirectory;
            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
            string directory = Path.Combine(root, Guid.NewGuid().ToString("D"));
            Directory.CreateDirectory(directory);
            workspace = new GeometryLabTemporaryStepWorkspace(root, directory);
            return true;
        }
        catch (Exception exception)
        {
            error = "Temporary STEP workspace could not be created: " + exception.Message;
            return false;
        }
    }

    private async Task<string?> ValidateOutputAsync(string stepFilePath, CancellationToken cancellationToken)
    {
        try
        {
            var first = new FileInfo(stepFilePath);
            if (!first.Exists || first.Length <= 0)
                return "Temporary STEP output was not created or is empty.";

            await Task.Delay(_options.OutputStabilityDelay, cancellationToken);
            var second = new FileInfo(stepFilePath);
            if (!second.Exists || second.Length <= 0 || second.Length != first.Length)
                return "Temporary STEP output did not reach a stable non-empty size.";

            using var stream = new FileStream(stepFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length == second.Length ? null : "Temporary STEP output size changed while being read.";
        }
        catch (OperationCanceledException)
        {
            return "Temporary STEP output validation was cancelled.";
        }
        catch (Exception exception)
        {
            return "Temporary STEP output could not be validated: " + exception.Message;
        }
    }

    private static GeometryLabTemporaryStepExportResult CleanupFailure(
        GeometryLabTemporaryStepWorkspace workspace,
        GeometryLabTemporaryStepExportStatus status,
        string message) => new()
        {
            Status = status,
            Message = message,
            TemporaryDirectoryCleaned = workspace.TryCleanup()
        };

    private static GeometryLabTemporaryStepExportResult Result(
        GeometryLabTemporaryStepExportStatus status,
        string message) => new() { Status = status, Message = message };
}
