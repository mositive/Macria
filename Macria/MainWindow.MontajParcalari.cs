using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Macria;

// Dosya Analiz Merkezi: montaj STEP'inin Saclar ve Kontrol gerekli sekmeleri.
// Profil parcalari mevcut Profiller listesine gider; burada yalniz sac adaylari
// ve motorun karar veremedigi parcalar tutulur. Motor DXF'leri oturumluk gecici
// klasordedir; "DXF Uret" onayli olanlari Motor-DXF\ altina DxfAdi ile tasir.
public partial class MainWindow
{
    private readonly ObservableCollection<MontajParcaSatiri> _montajParcaRows = new();
    private ICollectionView? _sacParcaView;
    private ICollectionView? _kontrolParcaView;
    private string? _motorDxfOturumKlasoru;

    private void MontajParcaListesiniKur()
    {
        _sacParcaView = new ListCollectionView(_montajParcaRows) { Filter = item => item is MontajParcaSatiri row && row.IsInSheetTab };
        _kontrolParcaView = new ListCollectionView(_montajParcaRows) { Filter = item => item is MontajParcaSatiri row && row.IsInReviewTab };
        if (gridSacParcalar != null) gridSacParcalar.ItemsSource = _sacParcaView;
        if (gridKontrolParcalar != null) gridKontrolParcalar.ItemsSource = _kontrolParcaView;
        MontajSekmeleriniGuncelle();
    }

    /// <summary>A new, empty folder for this analysis run's engine DXFs; the previous run's is removed.</summary>
    private string MotorDxfOturumunuYenile()
    {
        MotorDxfOturumunuTemizle();
        _motorDxfOturumKlasoru = Path.Combine(Path.GetTempPath(), "Macria", "MotorDxf", Guid.NewGuid().ToString("N"));
        return _motorDxfOturumKlasoru;
    }

    private void MotorDxfOturumunuTemizle()
    {
        if (_motorDxfOturumKlasoru == null) return;
        try
        {
            if (Directory.Exists(_motorDxfOturumKlasoru)) Directory.Delete(_motorDxfOturumKlasoru, true);
        }
        catch (Exception exception)
        {
            LogError("Geçici motor DXF klasörü silinemedi: " + exception.Message);
        }
        _motorDxfOturumKlasoru = null;
    }

    /// <summary>
    /// An assembly STEP replaces its file row by one profile row per profile part
    /// and adds the other parts to the Saclar / Kontrol gerekli tabs.
    /// Returns false for a single-part STEP, whose row stays as it is.
    /// </summary>
    private bool MontajSonucunuDagit(GeometryLabStepProfileListItem fileRow, GeometryLabProcessAdapterResult result)
    {
        if (!result.IsSuccess || !MontajParcaSatiri.IsAssembly(result.Analysis)) return false;
        GeometryLabAnalysisTransport analysis = result.Analysis!;
        int index = _externalStepProfileRows.IndexOf(fileRow);
        if (index >= 0) _externalStepProfileRows.RemoveAt(index);
        else index = _externalStepProfileRows.Count;

        foreach (GeometryLabPartTransport part in analysis.Parts)
        {
            var row = MontajParcaSatiri.Olustur(fileRow.SourceStepPath, analysis, part, result.PartDxfPath(part),
                Ayarlar.LazerAzamiKalinlikMm, result.PartDxfPath(part, cutOnly: true));
            if (row.EffectiveCategory == MontajParcaKategorisi.Profil)
            {
                var profileRow = new GeometryLabStepProfileListItem
                {
                    SourceStepPath = fileRow.SourceStepPath,
                    PartName = row.PartName,
                    PartQuantity = part.Quantity
                };
                profileRow.Apply(MontajParcaSatiri.ResultForPart(result, part));
                _externalStepProfileRows.Insert(index++, profileRow);
            }
            else
            {
                _montajParcaRows.Add(row);
            }
        }
        LogSuccess("Montaj STEP'i: " + fileRow.SourceFileName + " — " + analysis.Parts.Count + " parça (" +
                   _montajParcaRows.Count(x => x.SourceStepPath == fileRow.SourceStepPath && x.IsInSheetTab) + " sac, " +
                   _montajParcaRows.Count(x => x.SourceStepPath == fileRow.SourceStepPath && x.IsInReviewTab) + " kontrol).");
        MontajSekmeleriniGuncelle();
        return true;
    }

    private void MontajSekmeleriniGuncelle()
    {
        _sacParcaView?.Refresh();
        _kontrolParcaView?.Refresh();
        int sac = _montajParcaRows.Count(x => x.IsInSheetTab);
        int kontrol = _montajParcaRows.Count(x => x.IsInReviewTab);
        bool montajVar = _montajParcaRows.Count > 0;
        if (tabExternalStepSaclar != null)
        {
            tabExternalStepSaclar.Header = "Saclar (" + sac + ")";
            tabExternalStepSaclar.Visibility = montajVar ? Visibility.Visible : Visibility.Collapsed;
        }
        if (tabExternalStepKontrol != null)
        {
            tabExternalStepKontrol.Header = "Kontrol gerekli (" + kontrol + ")";
            tabExternalStepKontrol.Visibility = montajVar ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!montajVar && tabExternalStepSonuc != null && tabExternalStepProfiller != null)
            tabExternalStepSonuc.SelectedItem = tabExternalStepProfiller;
        if (txtSacOzet != null)
        {
            int onayli = _montajParcaRows.Count(x => x.EffectiveCategory == MontajParcaKategorisi.Sac);
            int bekleyen = _montajParcaRows.Count(x => x.EffectiveCategory == MontajParcaKategorisi.OnayGerekli);
            txtSacOzet.Text = onayli + " onaylı sac, " + bekleyen + " onay bekleyen. Lazer ≤ " +
                              MontajParcaSatiri.FormatNumber(Ayarlar.LazerAzamiKalinlikMm) + " mm (Ayarlar).";
        }
        MontajKomutlariniGuncelle();
    }

    private void MontajLazerEsiginiUygula()
    {
        foreach (MontajParcaSatiri row in _montajParcaRows) row.SetLaserMaximum(Ayarlar.LazerAzamiKalinlikMm);
        MontajSekmeleriniGuncelle();
        // The bend-information setting changes which engine DXF is shown.
        List<MontajParcaSatiri> selected = SeciliSacSatirlari();
        SacOnizlemesiniGoster(selected.Count == 1 ? selected[0] : null, selected.Count > 1);
    }

    private void MontajCatiaKarsilastir(CatiaScanSnapshot snapshot)
    {
        foreach (MontajParcaSatiri row in _montajParcaRows)
            row.ApplyCatiaComparison(CatiaStepMatcher.MatchPartName(snapshot, row.PartName));
        MontajSekmeleriniGuncelle();
    }

    private List<MontajParcaSatiri> SeciliSacSatirlari() =>
        gridSacParcalar?.SelectedItems.OfType<MontajParcaSatiri>().Where(x => x.IsInSheetTab).ToList() ?? new();

    private List<MontajParcaSatiri> SeciliKontrolSatirlari() =>
        gridKontrolParcalar?.SelectedItems.OfType<MontajParcaSatiri>().Where(x => x.IsInReviewTab).ToList() ?? new();

    private void MontajKomutlariniGuncelle()
    {
        List<MontajParcaSatiri> sac = SeciliSacSatirlari();
        if (btnSacOnayla != null) btnSacOnayla.IsEnabled = sac.Any(x => x.EffectiveCategory != MontajParcaKategorisi.Sac && x.CanApproveAsSheet);
        if (btnSacKontrole != null) btnSacKontrole.IsEnabled = sac.Count > 0;
        if (btnSacOtomatik != null) btnSacOtomatik.IsEnabled = sac.Any(x => x.HasUserDecision);
        if (btnSacDxfUret != null) btnSacDxfUret.IsEnabled = _montajParcaRows.Any(x => x.EffectiveCategory == MontajParcaKategorisi.Sac);
        List<MontajParcaSatiri> kontrol = SeciliKontrolSatirlari();
        if (btnKontrolSacOnayla != null) btnKontrolSacOnayla.IsEnabled = kontrol.Any(x => x.CanApproveAsSheet);
        if (btnKontrolOtomatik != null) btnKontrolOtomatik.IsEnabled = kontrol.Any(x => x.HasUserDecision);
    }

    private void gridSacParcalar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MontajKomutlariniGuncelle();
        List<MontajParcaSatiri> selected = SeciliSacSatirlari();
        SacOnizlemesiniGoster(selected.Count == 1 ? selected[0] : null, selected.Count > 1);
    }

    private void gridKontrolParcalar_SelectionChanged(object sender, SelectionChangedEventArgs e) => MontajKomutlariniGuncelle();

    private void btnSacOnayla_Click(object sender, RoutedEventArgs e) => MontajKarariUygula(SeciliSacSatirlari(), row => row.ApproveAsSheet());

    private void btnSacKontrole_Click(object sender, RoutedEventArgs e) => MontajKarariUygula(SeciliSacSatirlari(), row => row.MoveToReview());

    private void btnSacOtomatik_Click(object sender, RoutedEventArgs e) =>
        MontajKarariUygula(SeciliSacSatirlari().Where(x => x.HasUserDecision).ToList(), row => row.RestoreAutomaticDecision());

    private void btnKontrolSacOnayla_Click(object sender, RoutedEventArgs e)
    {
        List<MontajParcaSatiri> selected = SeciliKontrolSatirlari();
        if (selected.Count > 0 && !selected.Any(x => x.CanApproveAsSheet))
        {
            MessageBox.Show(this, "Seçilen parçalar için motor açınım (DXF) üretemedi; sac olarak onaylanamaz.", "Sac Olarak Onayla", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MontajKarariUygula(selected.Where(x => x.CanApproveAsSheet).ToList(), row => row.ApproveAsSheet());
    }

    private void btnKontrolOtomatik_Click(object sender, RoutedEventArgs e) =>
        MontajKarariUygula(SeciliKontrolSatirlari().Where(x => x.HasUserDecision).ToList(), row => row.RestoreAutomaticDecision());

    private void MontajKarariUygula(List<MontajParcaSatiri> rows, Action<MontajParcaSatiri> karar)
    {
        foreach (MontajParcaSatiri row in rows) karar(row);
        MontajSekmeleriniGuncelle();
    }

    private void SacOnizlemesiniGoster(MontajParcaSatiri? row, bool coklu)
    {
        string? dxf = row?.DxfFor(Ayarlar.BukumBilgisiDxf);
        if (row == null || dxf == null)
        {
            SacOnizlemesiniBosalt(coklu ? "Birden fazla parça seçildi."
                : row == null ? "Önizlemek için listeden bir sac parça seçin."
                : "Bu parça için motor DXF'i yok.");
            return;
        }
        DxfPreviewReadResult result = _dxfDwgPreviewAdapter.Read(new PreviewRequest
        {
            SourcePath = dxf,
            Capability = PreviewCapability.Preview2D,
            Presentation = PreviewPresentation.Embedded,
            SourceContext = "Dosya Analiz Merkezi — Saclar"
        });
        string? message = DxfDwgPreviewMessages.For(result);
        if (message != null || result.Model is not DxfCizim drawing)
        {
            if (result.Content.Status == PreviewContentCheckStatus.Failed)
                LogError("Motor DXF önizlemesi okunamadı — " + row.PartName + ": " + DxfDwgPreviewMessages.Diagnostic(result));
            SacOnizlemesiniBosalt(message ?? DxfDwgPreviewMessages.NothingDrawable);
            return;
        }
        try
        {
            if (sacOnizlemeCizim != null) { sacOnizlemeCizim.Data = drawing.Geometri(); sacOnizlemeCizim.Visibility = Visibility.Visible; }
            if (txtSacOnizlemeMesaj != null) txtSacOnizlemeMesaj.Visibility = Visibility.Collapsed;
            if (txtSacOnizlemeDosya != null)
                txtSacOnizlemeDosya.Text = row.ThicknessMm is double t ? DxfAdi.Uret(row.PartName, t, row.Quantity) : row.PartName;
            if (txtSacOnizlemeOlcu != null)
                txtSacOnizlemeOlcu.Text = $"{drawing.Genislik:N1} × {drawing.Yukseklik:N1} mm · {drawing.NesneSayisi} nesne";
        }
        catch (Exception exception)
        {
            SacOnizlemesiniBosalt(DxfDwgPreviewMessages.RenderFailed + "\n" + exception.Message);
        }
    }

    private void SacOnizlemesiniBosalt(string message)
    {
        if (sacOnizlemeCizim != null) { sacOnizlemeCizim.Data = null; sacOnizlemeCizim.Visibility = Visibility.Collapsed; }
        if (txtSacOnizlemeMesaj != null) { txtSacOnizlemeMesaj.Text = message; txtSacOnizlemeMesaj.Visibility = Visibility.Visible; }
        if (txtSacOnizlemeDosya != null) txtSacOnizlemeDosya.Text = "";
        if (txtSacOnizlemeOlcu != null) txtSacOnizlemeOlcu.Text = "";
    }

    private void btnSacDxfUret_Click(object sender, RoutedEventArgs e)
    {
        List<MontajParcaSatiri> onayli = _montajParcaRows.Where(x => x.EffectiveCategory == MontajParcaKategorisi.Sac).ToList();
        int bekleyen = _montajParcaRows.Count(x => x.EffectiveCategory == MontajParcaKategorisi.OnayGerekli);
        if (onayli.Count == 0)
        {
            MessageBox.Show(this, "Onaylı sac satırı yok. " + (bekleyen > 0
                    ? bekleyen + " parça onay bekliyor: seçip \"Sac Olarak Onayla\" deyin ya da CATIA ile karşılaştırın."
                    : ""), "DXF Üret", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFolderDialog { Title = "DXF'lerin Yazılacağı Klasörü Seçin (Motor-DXF alt klasörü oluşturulur)" };
        if (!string.IsNullOrWhiteSpace(Ayarlar.SonCiktiKlasoru) && Directory.Exists(Ayarlar.SonCiktiKlasoru))
            dialog.InitialDirectory = Ayarlar.SonCiktiKlasoru;
        if (dialog.ShowDialog(this) != true) return;

        IReadOnlyList<MotorDxfIsi> plan = MotorDxfAktarici.Planla(onayli, dialog.FolderName, Ayarlar.BukumBilgisiDxf);
        MotorDxfAktarimSonucu sonuc = MotorDxfAktarici.Uygula(plan, hedef => DxfCakismaWindow.Sor(this, hedef));

        string klasor = Path.Combine(dialog.FolderName, MotorDxfAktarici.AltKlasor);
        foreach (string path in sonuc.Yazilan) LogSuccess("Motor DXF yazıldı: " + path);
        foreach (string path in sonuc.Atlanan) LogInfo("Motor DXF atlandı (dosya zaten var): " + path);
        foreach ((string hedef, string neden) in sonuc.Hatali) LogError("Motor DXF yazılamadı: " + hedef + " — " + neden);

        string rapor = (sonuc.IptalEdildi ? "İşlem iptal edildi.\n\n" : "") +
                       "Klasör: " + klasor + "\n" +
                       "Büküm bilgisi: " + (Ayarlar.BukumBilgisiDxf ? "yazıldı (BUKUM katmanı)" : "yazılmadı (yalnız KESIM)") + "\n\n" +
                       "Yazılan: " + sonuc.Yazilan.Count + "\n" +
                       "Atlanan (zaten vardı): " + sonuc.Atlanan.Count + "\n" +
                       "Hatalı: " + sonuc.Hatali.Count +
                       (sonuc.Hatali.Count > 0 ? "\n  " + string.Join("\n  ", sonuc.Hatali.Take(5).Select(x => Path.GetFileName(x.Hedef) + ": " + x.Neden)) : "") +
                       (bekleyen > 0 ? "\n\nOnay bekleyen " + bekleyen + " parça yazılmadı." : "") +
                       (plan.Count < onayli.Count ? "\nMotor DXF'i olmayan " + (onayli.Count - plan.Count) + " onaylı parça yazılmadı." : "");
        MessageBox.Show(this, rapor, "DXF Üret — Rapor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void btnSacExcelAktar_Click(object sender, RoutedEventArgs e)
    {
        List<MontajParcaSatiri> rows = (_sacParcaView?.Cast<MontajParcaSatiri>() ?? Enumerable.Empty<MontajParcaSatiri>()).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Saclar sekmesinde aktarılacak satır yok.", "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Montaj Sac Listesini Kaydet",
            Filter = "Excel Çalışma Kitabı (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "Macria_Montaj_Saclar_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ExcelYazici.Yaz(SacExcelRaporuHazirla(rows), dialog.FileName);
            LogSuccess("Montaj sac listesi Excel'e yazıldı: " + dialog.FileName);
            MessageBox.Show(this, "Excel dosyası oluşturuldu:\n" + dialog.FileName, "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            LogError("Montaj sac listesi Excel'e yazılamadı: " + exception.Message);
            MessageBox.Show(this, "Excel dosyası yazılamadı.\n" + exception.Message, "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static Rapor SacExcelRaporuHazirla(IEnumerable<MontajParcaSatiri> rows)
    {
        var rapor = new Rapor
        {
            SayfaAdi = "Montaj Saclar",
            TabloIlkSatirdanBaslar = true,
            IlkSatiriDondur = true,
            OtomatikFiltre = true
        };
        foreach ((string name, double width) in new[]
        {
            ("Durum", 2.0), ("Parça", 2.2), ("Adet", 0.8), ("Kalınlık (mm)", 1.1), ("Grup", 1.3), ("Büküm", 0.8),
            ("Delikler", 3.4), ("DXF Adı", 3.0), ("CATIA Adedi", 1.1), ("CATIA Eşleşme", 1.9), ("Karar", 1.3),
            ("Açıklama", 3.3), ("STEP Dosyası", 2.2)
        })
            rapor.Sutunlar.Add(new RaporSutun { Ad = name, Genislik = width });
        foreach (MontajParcaSatiri row in rows)
            rapor.Satirlar.Add(new object?[]
            {
                row.StatusDisplay, row.PartName, row.Quantity, row.ThicknessMm, row.GroupDisplay,
                row.ThicknessMm is null ? null : row.BendCount, row.HoleSummary,
                row.ThicknessMm is double t ? DxfAdi.Uret(row.PartName, t, row.Quantity) : "",
                row.CatiaQuantityDisplay, row.CatiaMatchDisplay, row.DecisionDisplay, row.ExplanationDisplay, row.SourceFileName
            });
        return rapor;
    }
}
