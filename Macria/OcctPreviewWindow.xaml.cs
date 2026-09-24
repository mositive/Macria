using System;
using System.IO;
using System.Windows;

namespace Macria;

public partial class OcctPreviewWindow : Window
{
    internal event EventHandler<OcctViewportDiagnosticEventArgs>? Diagnostic;

    public OcctPreviewWindow()
    {
        InitializeComponent();
        WindowEffects.RoundCorners(this);
        viewport.StatusChanged += Viewport_StatusChanged;
        viewport.Diagnostic += Viewport_Diagnostic;
        UpdateControls(viewport.State, viewport.StatusMessage);
    }

    internal void ShowStep(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            txtStepName.Text = "Listeden Bir STEP Seçin";
            txtStepPath.Text = string.Empty;
            txtStepPath.ToolTip = null;
            viewport.ClearModel();
            return;
        }

        txtStepName.Text = Path.GetFileName(path);
        txtStepPath.Text = path;
        txtStepPath.ToolTip = path;

        if (!File.Exists(path))
        {
            viewport.ClearModel();
            txtStatus.Text = "STEP dosyası artık belirtilen konumda bulunmuyor.";
            return;
        }

        viewport.LoadStep(path);
    }

    protected override void OnClosed(EventArgs e)
    {
        viewport.StatusChanged -= Viewport_StatusChanged;
        viewport.Diagnostic -= Viewport_Diagnostic;
        viewport.Shutdown();
        base.OnClosed(e);
    }

    private void Viewport_StatusChanged(object? sender, OcctViewportStatusChangedEventArgs e) =>
        UpdateControls(e.State, e.Message);

    private void Viewport_Diagnostic(object? sender, OcctViewportDiagnosticEventArgs e) =>
        Diagnostic?.Invoke(this, e);

    private void UpdateControls(OcctViewportState state, string message)
    {
        txtStatus.Text = message;
        bool loaded = state == OcctViewportState.Loaded;
        btnFitAll.IsEnabled = loaded;
        btnIsometric.IsEnabled = loaded;
        btnFront.IsEnabled = loaded;
        btnBack.IsEnabled = loaded;
        btnLeft.IsEnabled = loaded;
        btnRight.IsEnabled = loaded;
        btnTop.IsEnabled = loaded;
        btnBottom.IsEnabled = loaded;
    }

    private void btnFitAll_Click(object sender, RoutedEventArgs e) => viewport.FitAll();
    private void btnIsometric_Click(object sender, RoutedEventArgs e) => viewport.SetView(OcctStandardView.Isometric);
    private void btnFront_Click(object sender, RoutedEventArgs e) => viewport.SetView(OcctStandardView.Front);
    private void btnBack_Click(object sender, RoutedEventArgs e) => viewport.SetView(OcctStandardView.Back);
    private void btnLeft_Click(object sender, RoutedEventArgs e) => viewport.SetView(OcctStandardView.Left);
    private void btnRight_Click(object sender, RoutedEventArgs e) => viewport.SetView(OcctStandardView.Right);
    private void btnTop_Click(object sender, RoutedEventArgs e) => viewport.SetView(OcctStandardView.Top);
    private void btnBottom_Click(object sender, RoutedEventArgs e) => viewport.SetView(OcctStandardView.Bottom);

    private void btnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void btnMax_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
}
