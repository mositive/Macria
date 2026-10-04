using System.IO;
using System.Windows;

namespace Macria;

public partial class MainWindow
{
    private OcctPreviewWindow? _externalStepPreviewWindow;
    // How a selected assembly part is shown, shared by the embedded views and
    // the "Büyük Aç" window: alone by default, or inside the faded assembly.
    private OcctPartView _montajParcaGorunumu = OcctPartView.Isolated;

    // Every embedded 3D panel of Dosya Analiz Merkezi; wiring, the shared part
    // view and shutdown all go through this list. One panel serves every tab.
    private IEnumerable<Step3BPaneli> TumStep3BPanelleri()
    {
        if (onizleme3B != null) yield return onizleme3B;
    }

    private void ExternalStepOnizlemesiniKur()
    {
        TeshisAyariniUygula();
        Step3BModelHazirlayici.Gunluk = (mesaj, basarili) => Dispatcher.BeginInvoke(() =>
        {
            if (basarili) LogTrace(mesaj);
            else LogError(mesaj);
        });
        foreach (Step3BPaneli panel in TumStep3BPanelleri())
            Step3BPaneliniBagla(panel);
    }

    private void ExternalStepOnizlemeyiKapat()
    {
        OcctPreviewWindow? previewWindow = _externalStepPreviewWindow;
        _externalStepPreviewWindow = null;
        previewWindow?.Close();

        foreach (Step3BPaneli panel in TumStep3BPanelleri())
            Step3BPaneliniCoz(panel);
    }

    /// <summary>Ayarlar → Teşhis: WM_* input traces of every 3D view, embedded or large.</summary>
    private static void TeshisAyariniUygula() => OcctViewportHost.InputTraceEnabled = Ayarlar.GomuluTeshisKaydi;

    private void Step3BPaneliniBagla(Step3BPaneli panel)
    {
        // Notification cards stay above the embedded 3D view.
        panel.AddOverlay(bildirimKatmani);
        panel.Diagnostic += Onizleme_Diagnostic;
        panel.BuyukAcIstendi += Onizleme_BuyukAcIstendi;
        panel.GosterimDegisti += Onizleme_GosterimDegisti;
        panel.ParcaGorunumuDegistirIstendi += Onizleme_ParcaGorunumuDegistirIstendi;
    }

    private void Step3BPaneliniCoz(Step3BPaneli panel)
    {
        panel.Diagnostic -= Onizleme_Diagnostic;
        panel.BuyukAcIstendi -= Onizleme_BuyukAcIstendi;
        panel.GosterimDegisti -= Onizleme_GosterimDegisti;
        panel.ParcaGorunumuDegistirIstendi -= Onizleme_ParcaGorunumuDegistirIstendi;
        panel.Shutdown();
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
        foreach (Step3BPaneli panel in TumStep3BPanelleri())
            panel.SetPartView(_montajParcaGorunumu);
        _externalStepPreviewWindow?.SetPartView(_montajParcaGorunumu);
    }

    private void Onizleme_Diagnostic(object? sender, OcctViewportDiagnosticEventArgs e) =>
        ViewportTeshisiniYaz(e);

    // WM_* traces go to the console only; real problems (e.g. a failed
    // highlight) are still shown as a notification.
    private void ViewportTeshisiniYaz(OcctViewportDiagnosticEventArgs e)
    {
        if (e.IsTrace) LogTrace(e.Message);
        else LogInfo(e.Message);
    }

    // One "Büyük Aç" window for every tab: it shows what the right panel shows
    // (in Saclar 2D, the selected sheet) and follows the open tab's selection.
    private void Onizleme_BuyukAcIstendi(object? sender, System.EventArgs e)
    {
        if (sender is not Step3BPaneli kaynak || kaynak.StepYolu == null || !File.Exists(kaynak.StepYolu)) return;
        if (_externalStepPreviewWindow != null)
        {
            BuyukOnizlemeyiKaynakla();
            if (_externalStepPreviewWindow.WindowState == WindowState.Minimized)
                _externalStepPreviewWindow.WindowState = WindowState.Normal;
            _externalStepPreviewWindow.Activate();
            return;
        }

        OcctPreviewWindow previewWindow = new() { Owner = this };
        previewWindow.Diagnostic += Onizleme_Diagnostic;
        previewWindow.PartViewToggleRequested += BuyukOnizleme_PartViewToggleRequested;
        _externalStepPreviewWindow = previewWindow;
        previewWindow.Closed += (_, _) =>
        {
            previewWindow.Diagnostic -= Onizleme_Diagnostic;
            previewWindow.PartViewToggleRequested -= BuyukOnizleme_PartViewToggleRequested;
            if (ReferenceEquals(_externalStepPreviewWindow, previewWindow)) _externalStepPreviewWindow = null;
        };
        BuyukOnizlemeyiKaynakla();
        previewWindow.Show();
    }

    private void Onizleme_GosterimDegisti(object? sender, System.EventArgs e) => BuyukOnizlemeyiKaynakla();

    private void tabExternalStepSonuc_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // SelectionChanged also bubbles up from the grids and the Lazer/Şalama tabs.
        if (!ReferenceEquals(e.OriginalSource, tabExternalStepSonuc)) return;
        SagPaneliGuncelle();
        AracCubugunuGuncelle();
    }

    private void BuyukOnizlemeyiKaynakla()
    {
        if (_externalStepPreviewWindow == null || onizleme3B == null) return;
        bool sac2B = AktifSekme() == AnalizSekmesi.Saclar && !_sac3BModu;
        if (sac2B)
            _externalStepPreviewWindow.ShowStep(_sacOnizlemeSatiri?.SourceStepPath, _sacOnizlemeSatiri?.PartName, _montajParcaGorunumu);
        else
            _externalStepPreviewWindow.ShowStep(onizleme3B.StepYolu, onizleme3B.ParcaAdi, _montajParcaGorunumu);
    }
}
