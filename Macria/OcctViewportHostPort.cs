namespace Macria;

/// <summary>Forwards IStepViewportPort to an existing OcctViewportHost without changing it.</summary>
internal sealed class OcctViewportHostPort(OcctViewportHost host) : IStepViewportPort
{
    public void LoadStep(string path) => host.LoadStep(path);
    public void ClearModel() => host.ClearModel();
    public void FitAll() => host.FitAll();
    public void SetView(OcctStandardView view) => host.SetView(view);

    public StepViewportLoadState LoadState => host.State switch
    {
        OcctViewportState.Idle => StepViewportLoadState.Empty,
        OcctViewportState.Initializing or OcctViewportState.Loading => StepViewportLoadState.Pending,
        OcctViewportState.Loaded => StepViewportLoadState.Loaded,
        _ => StepViewportLoadState.Failed
    };

    public string StatusMessage => host.StatusMessage;
}
