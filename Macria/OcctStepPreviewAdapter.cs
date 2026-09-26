using System;

namespace Macria;

/// <summary>Viewport load state as the preview layer sees it; the host port translates OcctViewportState.</summary>
public enum StepViewportLoadState { Empty, Pending, Loaded, Failed }

/// <summary>
/// Thin port over the existing public entry points of OcctViewportHost, so the adapter can be
/// tested without an HwndHost, the native viewer DLL or an OCCT session.
/// </summary>
public interface IStepViewportPort
{
    void LoadStep(string path);
    void ClearModel();
    void FitAll();
    void SetView(OcctStandardView view);
    StepViewportLoadState LoadState { get; }
    string StatusMessage { get; }
}

/// <summary>
/// Routes STEP/STP 3D preview requests (embedded or large) through PreviewCoordinator to an
/// existing viewport. It adds no viewer behavior; no UI call site uses it yet.
/// </summary>
public sealed class OcctStepPreviewAdapter
{
    private readonly IStepViewportPort _viewport;
    private readonly PreviewCoordinator _coordinator;

    public OcctStepPreviewAdapter(IStepViewportPort viewport, PreviewCoordinator? coordinator = null)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _coordinator = coordinator ?? new PreviewCoordinator();
    }

    /// <summary>
    /// Ready means the viewport accepted the load: the model is loaded, or it is pending until the
    /// native viewer window exists. Later native errors still arrive through the host's StatusChanged.
    /// </summary>
    public PreviewResult Load(PreviewRequest? request)
    {
        PreviewResult resolved = _coordinator.Resolve(request);
        if (!resolved.IsReady && resolved.Status != PreviewResultStatus.RequiresContentValidation) return resolved;
        if (resolved.ContentType != PreviewContentType.Step || resolved.Capability != PreviewCapability.Preview3D)
            return resolved with
            {
                Status = PreviewResultStatus.UnsupportedCapability,
                SupportLevel = PreviewSupportLevel.Unsupported,
                Message = "OCCT 3B önizleme yalnızca STEP/STP dosyalarını gösterir.",
                DiagnosticDetail = $"OCCT adapter serves Step/Preview3D only; got {resolved.ContentType}/{resolved.Capability}."
            };

        try
        {
            _viewport.LoadStep(resolved.NormalizedPath!);
        }
        catch (Exception exception)
        {
            return Failed(resolved, "STEP önizleme yüklenemedi.", exception.ToString());
        }

        if (_viewport.LoadState == StepViewportLoadState.Failed)
        {
            string status = _viewport.StatusMessage;
            return Failed(resolved, string.IsNullOrWhiteSpace(status) ? "STEP önizleme yüklenemedi." : status, status);
        }
        return resolved;
    }

    public bool Clear() => Run(_viewport.ClearModel);

    public bool FitAll() => Run(_viewport.FitAll);

    public bool SetView(OcctStandardView view) => Enum.IsDefined(view) && Run(() => _viewport.SetView(view));

    // True when the viewport accepted the command and did not end up in a failed state.
    private bool Run(Action command)
    {
        try { command(); }
        catch (Exception) { return false; }
        return _viewport.LoadState != StepViewportLoadState.Failed;
    }

    private static PreviewResult Failed(PreviewResult resolved, string message, string? detail) => resolved with
    {
        Status = PreviewResultStatus.Failed,
        Message = message,
        DiagnosticDetail = detail
    };
}
