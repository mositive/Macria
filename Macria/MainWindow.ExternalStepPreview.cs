using System.IO;
using System.Windows;

namespace Macria;

public partial class MainWindow
{
    private OcctPreviewWindow? _externalStepPreviewWindow;
    // How a selected assembly part is shown, shared by the embedded views and
    // the "Büyük Aç" window: alone by default, or inside the faded assembly.
    private OcctPartView _montajParcaGorunumu = OcctPartView.Isolated;

    private void ExternalStepOnizlemesiniKur()
    {
        OcctViewportHost.InputTraceEnabled = Ayarlar.GomuluTeshisKaydi;
        // Notification cards stay above the embedded 3D view.
        externalStepViewport.AddOverlay(bildirimKatmani);
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
        ParcaGorunumuDugmesiniGuncelle(btnExternalStepParcaGorunumu, item?.PartName, externalStepViewport, gizle: true);
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
        // An assembly part row: the part alone or inside the faded assembly.
        externalStepViewport.ShowPart(item.PartName, _montajParcaGorunumu);
        _externalStepPreviewWindow?.ShowStep(item.SourceStepPath, item.PartName, _montajParcaGorunumu);
    }

    private GeometryLabStepProfileListItem? ExternalStepOnizlemeSatiri()
    {
        List<GeometryLabStepProfileListItem> selected = GorunenSeciliExternalStepSatirlari().ToList();
        return selected.Count == 1 ? selected[0] : null;
    }

    private void btnMontajParcaGorunumu_Click(object sender, RoutedEventArgs e) => MontajParcaGorunumunuDegistir();

    private void BuyukOnizleme_PartViewToggleRequested(object? sender, System.EventArgs e) => MontajParcaGorunumunuDegistir();

    private void MontajParcaGorunumunuDegistir()
    {
        _montajParcaGorunumu = _montajParcaGorunumu == OcctPartView.Isolated
            ? OcctPartView.InAssembly
            : OcctPartView.Isolated;
        MontajParcaGorunumunuUygula();
    }

    private void MontajParcaGorunumunuUygula()
    {
        GeometryLabStepProfileListItem? profil = ExternalStepOnizlemeSatiri();
        if (!string.IsNullOrEmpty(profil?.PartName))
            externalStepViewport.ShowPart(profil.PartName, _montajParcaGorunumu);
        ParcaGorunumuDugmesiniGuncelle(btnExternalStepParcaGorunumu, profil?.PartName, externalStepViewport, gizle: true);

        List<MontajParcaSatiri> kontrol = SeciliKontrolSatirlari();
        string? kontrolParca = kontrol.Count == 1 ? kontrol[0].PartName : null;
        if (!string.IsNullOrEmpty(kontrolParca))
            kontrolStepViewport.ShowPart(kontrolParca, _montajParcaGorunumu);
        ParcaGorunumuDugmesiniGuncelle(btnKontrolParcaGorunumu, kontrolParca, kontrolStepViewport, gizle: false);
        KontrolDurumYazisiniGuncelle();

        _externalStepPreviewWindow?.SetPartView(_montajParcaGorunumu);
    }

    // The button names the other view; it only applies to assembly part rows.
    private void ParcaGorunumuDugmesiniGuncelle(System.Windows.Controls.Button? button, string? partName, OcctViewportHost viewport, bool gizle)
    {
        if (button == null) return;
        bool part = !string.IsNullOrEmpty(partName);
        button.Content = _montajParcaGorunumu == OcctPartView.Isolated ? "Montaj içinde göster" : "Yalnız parçayı göster";
        button.IsEnabled = part && viewport.State == OcctViewportState.Loaded;
        if (gizle) button.Visibility = part ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExternalStepViewport_StatusChanged(object? sender, OcctViewportStatusChangedEventArgs e) =>
        ExternalStepViewportDurumunuGuncelle(e);

    private void ExternalStepViewport_Diagnostic(object? sender, OcctViewportDiagnosticEventArgs e) =>
        ViewportTeshisiniYaz(e);

    // WM_* traces go to the console only; real problems (e.g. a failed
    // highlight) are still shown as a notification.
    private void ViewportTeshisiniYaz(OcctViewportDiagnosticEventArgs e)
    {
        if (e.IsTrace) LogTrace(e.Message);
        else LogInfo(e.Message);
    }

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
        ParcaGorunumuDugmesiniGuncelle(btnExternalStepParcaGorunumu, ExternalStepOnizlemeSatiri()?.PartName, externalStepViewport, gizle: true);
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
            _externalStepPreviewWindow.ShowStep(item.SourceStepPath, item.PartName, _montajParcaGorunumu);
            if (_externalStepPreviewWindow.WindowState == WindowState.Minimized)
                _externalStepPreviewWindow.WindowState = WindowState.Normal;
            _externalStepPreviewWindow.Activate();
            return;
        }

        OcctPreviewWindow previewWindow = new() { Owner = this };
        previewWindow.Diagnostic += ExternalStepViewport_Diagnostic;
        previewWindow.PartViewToggleRequested += BuyukOnizleme_PartViewToggleRequested;
        _externalStepPreviewWindow = previewWindow;
        previewWindow.Closed += (_, _) =>
        {
            previewWindow.Diagnostic -= ExternalStepViewport_Diagnostic;
            previewWindow.PartViewToggleRequested -= BuyukOnizleme_PartViewToggleRequested;
            if (ReferenceEquals(_externalStepPreviewWindow, previewWindow))
                _externalStepPreviewWindow = null;
        };
        previewWindow.ShowStep(item.SourceStepPath, item.PartName, _montajParcaGorunumu);
        previewWindow.Show();
    }
}
