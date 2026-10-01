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
        profilOnizleme.AddOverlay(bildirimKatmani);
        profilOnizleme.Diagnostic += ExternalStepViewport_Diagnostic;
        profilOnizleme.BuyukAcIstendi += ProfilOnizleme_BuyukAcIstendi;
        profilOnizleme.ParcaGorunumuDegistirIstendi += Onizleme_ParcaGorunumuDegistirIstendi;
    }

    private void ExternalStepOnizlemeyiKapat()
    {
        OcctPreviewWindow? previewWindow = _externalStepPreviewWindow;
        _externalStepPreviewWindow = null;
        previewWindow?.Close();

        profilOnizleme.Diagnostic -= ExternalStepViewport_Diagnostic;
        profilOnizleme.BuyukAcIstendi -= ProfilOnizleme_BuyukAcIstendi;
        profilOnizleme.ParcaGorunumuDegistirIstendi -= Onizleme_ParcaGorunumuDegistirIstendi;
        profilOnizleme.Shutdown();
    }

    private void ExternalStepOnizlemesiniGuncelle(GeometryLabStepProfileListItem? item)
    {
        if (item == null)
        {
            profilOnizleme.Temizle(null);
            if (!_buyukOnizlemeKontrolden) _externalStepPreviewWindow?.ShowStep(null);
            return;
        }

        // An assembly part row: the part alone or inside the faded assembly; a
        // missing file is reported by the panel.
        profilOnizleme.Goster(item.SourceStepPath, item.PartName, _montajParcaGorunumu);
        if (!_buyukOnizlemeKontrolden) _externalStepPreviewWindow?.ShowStep(item.SourceStepPath, item.PartName, _montajParcaGorunumu);
    }

    private GeometryLabStepProfileListItem? ExternalStepOnizlemeSatiri()
    {
        List<GeometryLabStepProfileListItem> selected = GorunenSeciliExternalStepSatirlari().ToList();
        return selected.Count == 1 ? selected[0] : null;
    }

    private void Onizleme_ParcaGorunumuDegistirIstendi(object? sender, System.EventArgs e) => MontajParcaGorunumunuDegistir();

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
        profilOnizleme.SetPartView(_montajParcaGorunumu);
        kontrolOnizleme.SetPartView(_montajParcaGorunumu);
        _externalStepPreviewWindow?.SetPartView(_montajParcaGorunumu);
    }

    private void ExternalStepViewport_Diagnostic(object? sender, OcctViewportDiagnosticEventArgs e) =>
        ViewportTeshisiniYaz(e);

    // WM_* traces go to the console only; real problems (e.g. a failed
    // highlight) are still shown as a notification.
    private void ViewportTeshisiniYaz(OcctViewportDiagnosticEventArgs e)
    {
        if (e.IsTrace) LogTrace(e.Message);
        else LogInfo(e.Message);
    }

    private void ProfilOnizleme_BuyukAcIstendi(object? sender, System.EventArgs e)
    {
        string? path = profilOnizleme.StepYolu;
        if (path == null || !File.Exists(path)) return;
        BuyukOnizlemeyiAc(path, profilOnizleme.ParcaAdi, fromKontrol: false);
    }

    // One "Büyük Aç" window for both tabs; it follows the selection of the tab
    // that opened it last.
    private bool _buyukOnizlemeKontrolden;

    private void BuyukOnizlemeyiAc(string stepPath, string? partName, bool fromKontrol = true)
    {
        _buyukOnizlemeKontrolden = fromKontrol;
        if (_externalStepPreviewWindow != null)
        {
            _externalStepPreviewWindow.ShowStep(stepPath, partName, _montajParcaGorunumu);
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
        previewWindow.ShowStep(stepPath, partName, _montajParcaGorunumu);
        previewWindow.Show();
    }
}
