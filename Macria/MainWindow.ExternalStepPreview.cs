using System.IO;
using System.Windows;

namespace Macria;

public partial class MainWindow
{
    private OcctPreviewWindow? _externalStepPreviewWindow;

    private void ExternalStepOnizlemesiniKur()
    {
        externalStepViewport.StatusChanged += ExternalStepViewport_StatusChanged;
        externalStepViewport.Diagnostic += ExternalStepViewport_Diagnostic;
        ExternalStepViewportDurumunuGuncelle(
            new OcctViewportStatusChangedEventArgs(externalStepViewport.State, externalStepViewport.StatusMessage));
    }

    private void ExternalStepOnizlemeyiKapat()
    {
        OcctPreviewWindow? previewWindow = _externalStepPreviewWindow;
        _externalStepPreviewWindow = null;
        previewWindow?.Close();

        externalStepViewport.StatusChanged -= ExternalStepViewport_StatusChanged;
        externalStepViewport.Diagnostic -= ExternalStepViewport_Diagnostic;
        externalStepViewport.Shutdown();
    }

    private void ExternalStepOnizlemesiniGuncelle(GeometryLabStepProfileListItem? item)
    {
        if (item == null)
        {
            btnExternalStepBuyukAc.IsEnabled = false;
            externalStepViewport.ClearModel();
            _externalStepPreviewWindow?.ShowStep(null);
            return;
        }

        if (!File.Exists(item.SourceStepPath))
        {
            btnExternalStepBuyukAc.IsEnabled = false;
            externalStepViewport.ClearModel();
            txtExternalStepViewportStatus.Text = "STEP dosyası artık belirtilen konumda bulunmuyor.";
            _externalStepPreviewWindow?.ShowStep(item.SourceStepPath);
            return;
        }

        btnExternalStepBuyukAc.IsEnabled = true;
        externalStepViewport.LoadStep(item.SourceStepPath);
        // An assembly part row: the part is highlighted inside the assembly.
        externalStepViewport.HighlightPart(item.PartName);
        _externalStepPreviewWindow?.ShowStep(item.SourceStepPath);
    }

    private void ExternalStepViewport_StatusChanged(object? sender, OcctViewportStatusChangedEventArgs e) =>
        ExternalStepViewportDurumunuGuncelle(e);

    private void ExternalStepViewport_Diagnostic(object? sender, OcctViewportDiagnosticEventArgs e) =>
        LogInfo(e.Message);

    private void ExternalStepViewportDurumunuGuncelle(OcctViewportStatusChangedEventArgs e)
    {
        txtExternalStepViewportStatus.Text = e.Message;
        bool loaded = e.State == OcctViewportState.Loaded;
        btnExternalStepFitAll.IsEnabled = loaded;
        btnExternalStepIsometric.IsEnabled = loaded;
        btnExternalStepFront.IsEnabled = loaded;
        btnExternalStepBack.IsEnabled = loaded;
        btnExternalStepLeft.IsEnabled = loaded;
        btnExternalStepRight.IsEnabled = loaded;
        btnExternalStepTop.IsEnabled = loaded;
        btnExternalStepBottom.IsEnabled = loaded;
    }

    private void btnExternalStepFitAll_Click(object sender, RoutedEventArgs e) => externalStepViewport.FitAll();
    private void btnExternalStepIsometric_Click(object sender, RoutedEventArgs e) => externalStepViewport.SetView(OcctStandardView.Isometric);
    private void btnExternalStepFront_Click(object sender, RoutedEventArgs e) => externalStepViewport.SetView(OcctStandardView.Front);
    private void btnExternalStepBack_Click(object sender, RoutedEventArgs e) => externalStepViewport.SetView(OcctStandardView.Back);
    private void btnExternalStepLeft_Click(object sender, RoutedEventArgs e) => externalStepViewport.SetView(OcctStandardView.Left);
    private void btnExternalStepRight_Click(object sender, RoutedEventArgs e) => externalStepViewport.SetView(OcctStandardView.Right);
    private void btnExternalStepTop_Click(object sender, RoutedEventArgs e) => externalStepViewport.SetView(OcctStandardView.Top);
    private void btnExternalStepBottom_Click(object sender, RoutedEventArgs e) => externalStepViewport.SetView(OcctStandardView.Bottom);

    private void btnExternalStepBuyukAc_Click(object sender, RoutedEventArgs e)
    {
        GeometryLabStepProfileListItem? item = GorunenSeciliExternalStepSatirlari().SingleOrDefault();
        if (item == null || !File.Exists(item.SourceStepPath)) return;

        if (_externalStepPreviewWindow != null)
        {
            _externalStepPreviewWindow.ShowStep(item.SourceStepPath);
            if (_externalStepPreviewWindow.WindowState == WindowState.Minimized)
                _externalStepPreviewWindow.WindowState = WindowState.Normal;
            _externalStepPreviewWindow.Activate();
            return;
        }

        OcctPreviewWindow previewWindow = new() { Owner = this };
        previewWindow.Diagnostic += ExternalStepViewport_Diagnostic;
        _externalStepPreviewWindow = previewWindow;
        previewWindow.Closed += (_, _) =>
        {
            previewWindow.Diagnostic -= ExternalStepViewport_Diagnostic;
            if (ReferenceEquals(_externalStepPreviewWindow, previewWindow))
                _externalStepPreviewWindow = null;
        };
        previewWindow.ShowStep(item.SourceStepPath);
        previewWindow.Show();
    }
}
