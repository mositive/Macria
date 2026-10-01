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
        profilOnizleme.BuyukAcIstendi += Onizleme_BuyukAcIstendi;
        profilOnizleme.GosterimDegisti += Onizleme_GosterimDegisti;
        profilOnizleme.ParcaGorunumuDegistirIstendi += Onizleme_ParcaGorunumuDegistirIstendi;
    }

    private void ExternalStepOnizlemeyiKapat()
    {
        OcctPreviewWindow? previewWindow = _externalStepPreviewWindow;
        _externalStepPreviewWindow = null;
        previewWindow?.Close();

        profilOnizleme.Diagnostic -= ExternalStepViewport_Diagnostic;
        profilOnizleme.BuyukAcIstendi -= Onizleme_BuyukAcIstendi;
        profilOnizleme.GosterimDegisti -= Onizleme_GosterimDegisti;
        profilOnizleme.ParcaGorunumuDegistirIstendi -= Onizleme_ParcaGorunumuDegistirIstendi;
        profilOnizleme.Shutdown();
    }

    private void ExternalStepOnizlemesiniGuncelle(GeometryLabStepProfileListItem? item)
    {
        // An assembly part row: the part alone or inside the faded assembly; a
        // missing file is reported by the panel. A null row clears it.
        profilOnizleme.Goster(item?.SourceStepPath, item?.PartName, _montajParcaGorunumu);
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

    // One "Büyük Aç" window for every panel; it follows the panel that opened
    // it last, including when that panel is cleared.
    private Step3BPaneli? _buyukOnizlemeKaynagi;

    private void Onizleme_BuyukAcIstendi(object? sender, System.EventArgs e)
    {
        if (sender is not Step3BPaneli kaynak || kaynak.StepYolu == null || !File.Exists(kaynak.StepYolu)) return;
        _buyukOnizlemeKaynagi = kaynak;
        if (_externalStepPreviewWindow != null)
        {
            BuyukOnizlemeyiKaynakla();
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
            {
                _externalStepPreviewWindow = null;
                _buyukOnizlemeKaynagi = null;
            }
        };
        BuyukOnizlemeyiKaynakla();
        previewWindow.Show();
    }

    private void Onizleme_GosterimDegisti(object? sender, System.EventArgs e)
    {
        if (ReferenceEquals(sender, _buyukOnizlemeKaynagi)) BuyukOnizlemeyiKaynakla();
    }

    private void BuyukOnizlemeyiKaynakla()
    {
        if (_externalStepPreviewWindow == null || _buyukOnizlemeKaynagi == null) return;
        _externalStepPreviewWindow.ShowStep(_buyukOnizlemeKaynagi.StepYolu, _buyukOnizlemeKaynagi.ParcaAdi, _montajParcaGorunumu);
    }
}
