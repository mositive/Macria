using System;
using System.IO;
using System.Windows;

namespace Macria;

public partial class OcctPreviewWindow : Window
{
    internal event EventHandler<OcctViewportDiagnosticEventArgs>? Diagnostic;
    // The part view is shared with the main window, which owns the choice.
    internal event EventHandler? PartViewToggleRequested;

    private string? _partName;
    private OcctPartView _partView = OcctPartView.Isolated;

    public OcctPreviewWindow()
    {
        InitializeComponent();
        WindowEffects.RoundCorners(this);
        viewport.StatusChanged += Viewport_StatusChanged;
        viewport.Diagnostic += Viewport_Diagnostic;
        UpdateControls(viewport.State, viewport.StatusMessage);
    }

    /// <summary>
    /// Shows a STEP file; for an assembly part row, <paramref name="partName"/>
    /// is shown the way <paramref name="partView"/> says, like the embedded view.
    /// </summary>
    internal void ShowStep(string? path, string? partName = null, OcctPartView partView = OcctPartView.Isolated)
    {
        _partName = string.IsNullOrWhiteSpace(partName) ? null : partName;
        _partView = partView;
        UpdatePartViewButton();
        if (string.IsNullOrWhiteSpace(path))
        {
            txtStepName.Text = "Listeden Bir STEP Seçin";
            txtStepPath.Text = string.Empty;
            txtStepPath.ToolTip = null;
            viewport.ClearModel();
            return;
        }

        txtStepName.Text = _partName == null ? Path.GetFileName(path) : _partName + " — " + Path.GetFileName(path);
        txtStepPath.Text = path;
        txtStepPath.ToolTip = path;

        if (!File.Exists(path))
        {
            viewport.ClearModel();
            txtStatus.Text = "STEP dosyası artık belirtilen konumda bulunmuyor.";
            return;
        }

        viewport.LoadStep(path);
        viewport.ShowPart(_partName, _partView);
    }

    internal void SetPartView(OcctPartView partView)
    {
        _partView = partView;
        UpdatePartViewButton();
        if (_partName != null) viewport.ShowPart(_partName, _partView);
    }

    private void UpdatePartViewButton()
    {
        btnPartView.Visibility = _partName == null ? Visibility.Collapsed : Visibility.Visible;
        btnPartView.Content = _partView == OcctPartView.Isolated ? "Montaj içinde göster" : "Yalnız parçayı göster";
        btnPartView.IsEnabled = _partName != null && viewport.State == OcctViewportState.Loaded;
    }

    private void btnPartView_Click(object sender, RoutedEventArgs e) =>
        PartViewToggleRequested?.Invoke(this, EventArgs.Empty);

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
        btnPartView.IsEnabled = loaded && _partName != null;
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
