using System;
using System.Windows;

namespace Macria;

public partial class OcctPreviewWindow : Window
{
    internal event EventHandler<OcctViewportDiagnosticEventArgs>? Diagnostic;
    // The part view is shared with the main window, which owns the choice.
    internal event EventHandler? PartViewToggleRequested;

    public OcctPreviewWindow()
    {
        InitializeComponent();
        WindowEffects.RoundCorners(this);
        panel.Diagnostic += Panel_Diagnostic;
        panel.ParcaGorunumuDegistirIstendi += Panel_ParcaGorunumuDegistirIstendi;
    }

    /// <summary>
    /// Shows a STEP file; for an assembly part row, <paramref name="partName"/>
    /// is shown the way <paramref name="partView"/> says, like the embedded view.
    /// </summary>
    internal void ShowStep(string? path, string? partName = null, OcctPartView partView = OcctPartView.Isolated) =>
        panel.Goster(path, partName, partView);

    internal void SetPartView(OcctPartView partView) => panel.SetPartView(partView);

    protected override void OnClosed(EventArgs e)
    {
        panel.Diagnostic -= Panel_Diagnostic;
        panel.ParcaGorunumuDegistirIstendi -= Panel_ParcaGorunumuDegistirIstendi;
        panel.Shutdown();
        base.OnClosed(e);
    }

    private void Panel_Diagnostic(object? sender, OcctViewportDiagnosticEventArgs e) => Diagnostic?.Invoke(this, e);

    private void Panel_ParcaGorunumuDegistirIstendi(object? sender, EventArgs e) =>
        PartViewToggleRequested?.Invoke(this, EventArgs.Empty);

    private void btnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void btnMax_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
}
