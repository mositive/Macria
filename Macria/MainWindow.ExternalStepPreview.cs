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
    // view and shutdown all go through this list.
    private IEnumerable<Step3BPaneli> TumStep3BPanelleri()
    {
        if (profilOnizleme != null) yield return profilOnizleme;
        if (kontrolOnizleme != null) yield return kontrolOnizleme;
        // Created on the first 3D choice in Saclar.
        if (_sacOnizleme3B != null) yield return _sacOnizleme3B;
    }

    private void ExternalStepOnizlemesiniKur()
    {
        TeshisAyariniUygula();
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

    // One "Büyük Aç" window for every tab. It shows the selection of the tab
    // that is open now: opening it from a tab, or switching tabs while it is
    // open, makes that tab the source; it then follows that tab's selection.
    private enum BuyukOnizlemeSekmesi { Yok, Profiller, Saclar, Kontrol }

    private BuyukOnizlemeSekmesi _buyukOnizlemeKaynagi;

    private BuyukOnizlemeSekmesi Step3BSekmesi(object? panel) =>
        panel == null ? BuyukOnizlemeSekmesi.Yok
        : ReferenceEquals(panel, profilOnizleme) ? BuyukOnizlemeSekmesi.Profiller
        : ReferenceEquals(panel, kontrolOnizleme) ? BuyukOnizlemeSekmesi.Kontrol
        : ReferenceEquals(panel, _sacOnizleme3B) ? BuyukOnizlemeSekmesi.Saclar
        : BuyukOnizlemeSekmesi.Yok;

    private void Onizleme_BuyukAcIstendi(object? sender, System.EventArgs e)
    {
        if (sender is not Step3BPaneli kaynak || kaynak.StepYolu == null || !File.Exists(kaynak.StepYolu)) return;
        _buyukOnizlemeKaynagi = Step3BSekmesi(kaynak);
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
            if (ReferenceEquals(_externalStepPreviewWindow, previewWindow))
            {
                _externalStepPreviewWindow = null;
                _buyukOnizlemeKaynagi = BuyukOnizlemeSekmesi.Yok;
            }
        };
        BuyukOnizlemeyiKaynakla();
        previewWindow.Show();
    }

    // Saclar is not followed through its 3D panel, which is emptied in 2D mode
    // or may not exist yet; SacOnizlemesiniGoster updates the window directly.
    private void Onizleme_GosterimDegisti(object? sender, System.EventArgs e)
    {
        BuyukOnizlemeSekmesi sekme = Step3BSekmesi(sender);
        if (sekme != BuyukOnizlemeSekmesi.Saclar && sekme == _buyukOnizlemeKaynagi) BuyukOnizlemeyiKaynakla();
    }

    private void tabExternalStepSonuc_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // SelectionChanged also bubbles up from the grids and the Lazer/Şalama tabs.
        if (!ReferenceEquals(e.OriginalSource, tabExternalStepSonuc) || _externalStepPreviewWindow == null) return;
        object secili = tabExternalStepSonuc.SelectedItem;
        BuyukOnizlemeSekmesi sekme =
            ReferenceEquals(secili, tabExternalStepProfiller) ? BuyukOnizlemeSekmesi.Profiller
            : ReferenceEquals(secili, tabExternalStepSaclar) ? BuyukOnizlemeSekmesi.Saclar
            : ReferenceEquals(secili, tabExternalStepKontrol) ? BuyukOnizlemeSekmesi.Kontrol
            : BuyukOnizlemeSekmesi.Yok;
        if (sekme == BuyukOnizlemeSekmesi.Yok || sekme == _buyukOnizlemeKaynagi) return;
        _buyukOnizlemeKaynagi = sekme;
        BuyukOnizlemeyiKaynakla();
    }

    private void BuyukOnizlemeyiKaynakla()
    {
        if (_externalStepPreviewWindow == null) return;
        switch (_buyukOnizlemeKaynagi)
        {
            case BuyukOnizlemeSekmesi.Profiller:
                _externalStepPreviewWindow.ShowStep(profilOnizleme.StepYolu, profilOnizleme.ParcaAdi, _montajParcaGorunumu);
                break;
            case BuyukOnizlemeSekmesi.Kontrol:
                _externalStepPreviewWindow.ShowStep(kontrolOnizleme.StepYolu, kontrolOnizleme.ParcaAdi, _montajParcaGorunumu);
                break;
            case BuyukOnizlemeSekmesi.Saclar:
                _externalStepPreviewWindow.ShowStep(_sacOnizlemeSatiri?.SourceStepPath, _sacOnizlemeSatiri?.PartName, _montajParcaGorunumu);
                break;
        }
    }
}
