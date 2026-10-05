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
    private string? _motorDxfOturumKlasoru;
    // Saclar alt sekmesi: false = Lazer, true = Şalama/Kütük.
    private bool _sacSalamaSekmesi;

    private void MontajParcaListesiniKur()
    {
        // Saclar: approved and waiting sheets of the open sub-tab (Lazer or Şalama/Kütük).
        _sacParcaView = new ListCollectionView(_montajParcaRows)
        {
            Filter = item => item is MontajParcaSatiri row && row.Sekme == AnalizSekmesi.Saclar &&
                             row.IsThickPlate == _sacSalamaSekmesi && AramayaUyar(row)
        };
        if (gridSacParcalar != null) gridSacParcalar.ItemsSource = _sacParcaView;
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
    /// A STEP with engine parts (a single-part file too) replaces its file row
    /// by one profile row per profile part and part rows for the others
    /// (Saclar, Kontrol gerekli, Tanımsız). Returns false for a failed analysis
    /// or an older schema without parts, whose file row stays as it is.
    /// </summary>
    private bool MontajSonucunuDagit(GeometryLabStepProfileListItem fileRow, GeometryLabProcessAdapterResult result)
    {
        if (!MacriaProjeSatirlari.ParcaYolundan(result)) return false;
        bool ilkMontaj = _montajParcaRows.Count == 0;
        GeometryLabAnalysisTransport analysis = result.Analysis!;
        int index = _externalStepProfileRows.IndexOf(fileRow);
        if (index >= 0) _externalStepProfileRows.RemoveAt(index);
        else index = _externalStepProfileRows.Count;

        var (profil, montaj) = MacriaProjeSatirlari.MontajSatirlari(fileRow.SourceStepPath, result, _projeLazerMm);
        foreach (GeometryLabStepProfileListItem profileRow in profil)
            _externalStepProfileRows.Insert(index++, profileRow);
        foreach (MontajParcaSatiri row in montaj)
            _montajParcaRows.Add(row);
        LogSuccess("Montaj STEP'i: " + fileRow.SourceFileName + " — " + analysis.Parts.Count + " parça (" +
                   _montajParcaRows.Count(x => x.SourceStepPath == fileRow.SourceStepPath && x.IsInSheetTab) + " sac, " +
                   _montajParcaRows.Count(x => x.SourceStepPath == fileRow.SourceStepPath && x.Sekme == AnalizSekmesi.KontrolGerekli) + " kontrol, " +
                   _montajParcaRows.Count(x => x.SourceStepPath == fileRow.SourceStepPath && x.Sekme == AnalizSekmesi.Tanimsiz) + " tanımsız).");
        // A new list opens on the sub-tab that has sheets: Lazer unless it is empty.
        if (ilkMontaj)
            SacGrubunuSec(!_montajParcaRows.Any(x => x.IsInSheetTab && !x.IsThickPlate) &&
                          _montajParcaRows.Any(x => x.IsInSheetTab && x.IsThickPlate));
        MontajSekmeleriniGuncelle();
        return true;
    }

    private void MontajSekmeleriniGuncelle() => AnalizSekmeleriniGuncelle();

    /// <summary>Sheet rows of the open sub-tab (Lazer or Şalama/Kütük), filters ignored.</summary>
    private List<MontajParcaSatiri> SacGrubuSatirlari() =>
        _montajParcaRows.Where(x => x.Sekme == AnalizSekmesi.Saclar && x.IsThickPlate == _sacSalamaSekmesi).ToList();

    private string SacGrubuAdi() => _sacSalamaSekmesi ? "Şalama/Kütük" : "Lazer";

    private void SacGrubunuSec(bool salama)
    {
        _sacSalamaSekmesi = salama;
        if (tabSacGrubu != null)
            tabSacGrubu.SelectedItem = salama ? tabSacSalama : tabSacLazer;
    }

    private void tabSacGrubu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep the switch from reaching the outer tab controls.
        e.Handled = true;
        if (!ReferenceEquals(e.OriginalSource, tabSacGrubu)) return;
        bool salama = ReferenceEquals(tabSacGrubu.SelectedItem, tabSacSalama);
        if (salama == _sacSalamaSekmesi && e.RemovedItems.Count > 0) return;
        _sacSalamaSekmesi = salama;
        // The first selection happens while the window is still being built.
        if (_sacParcaView == null) return;
        MontajSekmeleriniGuncelle();
        SagPaneliGuncelle();
    }

    private void MontajLazerEsiginiUygula()
    {
        foreach (MontajParcaSatiri row in _montajParcaRows) row.SetLaserMaximum(Ayarlar.LazerAzamiKalinlikMm);
        ProjeLazerSiniriniGuncelle(Ayarlar.LazerAzamiKalinlikMm);
        MontajSekmeleriniGuncelle();
        // The bend-information setting changes which engine DXF is shown.
        SagPaneliGuncelle();
    }

    private void MontajCatiaKarsilastir(CatiaScanSnapshot snapshot)
    {
        foreach (MontajParcaSatiri row in _montajParcaRows)
            row.ApplyCatiaComparison(CatiaStepMatcher.MatchPartName(snapshot, row.PartName));
        MontajSekmeleriniGuncelle();
    }

    private List<MontajParcaSatiri> SeciliSacSatirlari() =>
        gridSacParcalar?.SelectedItems.OfType<MontajParcaSatiri>()
            .Where(x => x.IsInSheetTab && x.IsThickPlate == _sacSalamaSekmesi).ToList() ?? new();

    private void gridSacParcalar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AracCubugunuGuncelle();
        SagPaneliGuncelle();
    }

    // ------------------------------------------------- thickness correction

    // Focusing the thickness cell selects its row, as in Toplu DXF's Ham Sac.
    private void SacKalinlik_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox kutu || kutu.DataContext is not MontajParcaSatiri satir) return;
        if (!gridSacParcalar.SelectedItems.Contains(satir)) gridSacParcalar.SelectedItem = satir;
        kutu.Dispatcher.BeginInvoke(new Action(kutu.SelectAll), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SacKalinlik_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox kutu) return;
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            KalinlikGirdisiniUygula(kutu);
            kutu.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape && kutu.DataContext is MontajParcaSatiri satir)
        {
            kutu.Text = satir.KalinlikMetni;
            kutu.SelectAll();
            e.Handled = true;
        }
    }

    private void SacKalinlik_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox kutu) KalinlikGirdisiniUygula(kutu);
    }

    /// <summary>
    /// The typed thickness becomes the row's user thickness (empty: back to
    /// the engine value). It renames the DXF, may move the row between Lazer
    /// and Şalama/Kütük and is a project decision.
    /// </summary>
    private void KalinlikGirdisiniUygula(TextBox kutu)
    {
        if (kutu.DataContext is not MontajParcaSatiri satir) return;
        if (kutu.Text.Trim() == satir.KalinlikMetni) return;
        if (!MontajParcaSatiri.KalinlikGirdisiniOku(kutu.Text, out double? kalinlik))
        {
            LogError("Ham sac kalınlığı okunamadı: \"" + kutu.Text.Trim() + "\" (" + satir.PartName + "). 0,05–1000 mm arası bir sayı girin.");
            kutu.Text = satir.KalinlikMetni;
            return;
        }
        if (ProjeSaltOkunurUyarisi())
        {
            kutu.Text = satir.KalinlikMetni;
            return;
        }
        bool kalinOnce = satir.IsThickPlate;
        satir.KalinligiDuzelt(kalinlik);
        kutu.Text = satir.KalinlikMetni;
        YazilanDxfleriYenidenAdlandir(satir);
        LogInfo(satir.KalinlikDuzeltildi
            ? "Ham sac kalınlığı düzeltildi: " + satir.PartName + " " + satir.ThicknessDisplay + " (tespit edilen " + satir.MotorKalinlikDisplay + ")."
            : "Ham sac kalınlığı tespit edilen değere döndü: " + satir.PartName + " " + satir.ThicknessDisplay + ".");
        ProjeDegisti();
        if (satir.IsThickPlate != kalinOnce) GrupGecisiniBildir(satir);
        // The row may leave the open sub-tab; refresh after the focus change is done.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AnalizSekmeleriniGuncelle();
            SagPaneliGuncelle();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Toplu DXF's "Güncelle": DXFs DXF Üret wrote for the row get the name of
    /// the new thickness in their folder; a file with that name is not replaced.
    /// </summary>
    private void YazilanDxfleriYenidenAdlandir(MontajParcaSatiri satir)
    {
        if (satir.YazilanDxfYollari.Count == 0 || satir.DxfDosyaAdi is not string yeniAd) return;
        foreach (DxfAdlandirmaSonucu sonuc in MotorDxfAktarici.YenidenAdlandir(satir.YazilanDxfYollari.ToList(), yeniAd))
        {
            switch (sonuc.Durum)
            {
                case DxfAdlandirmaDurumu.Tasindi:
                    satir.DxfYolunuDegistir(sonuc.Eski, sonuc.Yeni);
                    LogSuccess("DXF yeniden adlandırıldı: " + Path.GetFileName(sonuc.Eski) + " → " + Path.GetFileName(sonuc.Yeni) +
                               " (" + Path.GetDirectoryName(sonuc.Yeni) + ")");
                    break;
                case DxfAdlandirmaDurumu.DosyaYok:
                    satir.DxfYolunuDegistir(sonuc.Eski, null);
                    LogInfo("Daha önce yazılan DXF artık yok, unutuldu: " + sonuc.Eski);
                    break;
                case DxfAdlandirmaDurumu.HedefVar:
                    LogError("DXF yeniden adlandırılamadı: " + sonuc.Yeni + " zaten var; " + Path.GetFileName(sonuc.Eski) +
                             " eski adıyla kaldı.");
                    break;
                case DxfAdlandirmaDurumu.Hata:
                    LogError("DXF yeniden adlandırılamadı: " + sonuc.Eski + " — " + sonuc.Neden);
                    break;
            }
        }
    }

    /// <summary>
    /// The thickness moved the part between Lazer and Şalama/Kütük: logged, and
    /// told in a window unless the project says "Bu proje için bir daha gösterme".
    /// </summary>
    private void GrupGecisiniBildir(MontajParcaSatiri satir)
    {
        string yeni = satir.IsThickPlate ? "Şalama/Kütük" : "Lazer";
        string mesaj = satir.PartName + " artık " + yeni + " alt sekmesinde: ham sac kalınlığı " + satir.ThicknessDisplay +
                       ", lazer sınırı " + MontajParcaSatiri.FormatNumber(_projeLazerMm) + " mm.";
        LogInfo(mesaj);
        // No windows in automation mode (tests, hidden runs).
        if (_projeGrupGecisUyarisiKapali || OtomasyonModu.Acik) return;
        if (!OnayWindow.Bilgi(this, "Lazer / Şalama/Kütük değişti", mesaj, "Bu proje için bir daha gösterme")) return;
        _projeGrupGecisUyarisiKapali = true;
        ProjeDegisti();
        LogInfo("Lazer / Şalama/Kütük geçiş uyarısı bu proje için kapatıldı.");
    }

    // Saclar: "Açınım (2B) | 3B" in the shared right panel; back in 2D the
    // 3D model is cleared to give the memory back.
    private bool _sac3BModu;
    private MontajParcaSatiri? _sacOnizlemeSatiri;
    private bool _sacOnizlemeCoklu;

    private void btnSacOnizleme2B_Click(object sender, RoutedEventArgs e) => SacOnizlemeModunuAyarla(false);

    private void btnSacOnizleme3B_Click(object sender, RoutedEventArgs e) => SacOnizlemeModunuAyarla(true);

    private void SacOnizlemeModunuAyarla(bool ucB)
    {
        _sac3BModu = ucB;
        btnSacOnizleme2B.Style = (Style)FindResource(ucB ? "SecondaryButton" : "PrimaryButton");
        btnSacOnizleme3B.Style = (Style)FindResource(ucB ? "PrimaryButton" : "SecondaryButton");
        SagPaneliGuncelle();
    }

    /// <summary>The engine DXF of the selected sheet in the 2D view (the 3D view is SagPaneliGuncelle's).</summary>
    private void SacOnizlemesiniGoster(MontajParcaSatiri? row, bool coklu)
    {
        _sacOnizlemeSatiri = row;
        _sacOnizlemeCoklu = coklu;
        string? dxf = row?.DxfFor(Ayarlar.BukumBilgisiDxf);
        if (row == null || dxf == null)
        {
            SacOnizlemesiniBosalt(coklu ? "Birden fazla parça seçildi."
                : row == null ? "Önizlemek için listeden bir sac parça seçin."
                : "Bu parça için motor DXF'i yok (açınım yok – CATIA'dan).");
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
                txtSacOnizlemeDosya.Text = row.EtkinKalinlikMm is double t ? DxfAdi.Uret(row.PartName, t, row.Quantity) : row.PartName;
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
        // Only the open sub-tab (Lazer or Şalama/Kütük) is written.
        List<MontajParcaSatiri> grup = SacGrubuSatirlari();
        List<MontajParcaSatiri> onayli = grup.Where(x => x.EffectiveCategory == MontajParcaKategorisi.Sac).ToList();
        int bekleyen = grup.Count(x => x.EffectiveCategory == MontajParcaKategorisi.OnayGerekli);
        if (onayli.Count == 0)
        {
            MessageBox.Show(this, SacGrubuAdi() + " sekmesinde onaylı sac satırı yok. " + (bekleyen > 0
                    ? bekleyen + " parça onay bekliyor: seçip \"Sac Olarak Onayla\" deyin ya da CATIA ile karşılaştırın."
                    : ""), "DXF Üret", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFolderDialog { Title = SacGrubuAdi() + " DXF'lerinin Yazılacağı Klasörü Seçin (Motor-DXF alt klasörü oluşturulur)" };
        if (!string.IsNullOrWhiteSpace(Ayarlar.SonCiktiKlasoru) && Directory.Exists(Ayarlar.SonCiktiKlasoru))
            dialog.InitialDirectory = Ayarlar.SonCiktiKlasoru;
        if (dialog.ShowDialog(this) != true) return;

        IReadOnlyList<MotorDxfIsi> plan = MotorDxfAktarici.Planla(onayli, dialog.FolderName, Ayarlar.BukumBilgisiDxf);
        MotorDxfAktarimSonucu sonuc = MotorDxfAktarici.Uygula(plan, hedef => DxfCakismaWindow.Sor(this, hedef));

        string klasor = Path.Combine(dialog.FolderName, MotorDxfAktarici.AltKlasor);
        // The rows remember their DXFs (written, or already there) for a later thickness rename.
        bool hatirlandi = false;
        foreach (string path in sonuc.Yazilan.Concat(sonuc.Atlanan))
            foreach (MotorDxfIsi is_ in plan.Where(x => x.Hedef == path && x.Satir != null))
            {
                is_.Satir!.DxfYazildi(path);
                hatirlandi = true;
            }
        if (hatirlandi) ProjeDegisti();
        foreach (string path in sonuc.Yazilan) LogSuccess("Motor DXF yazıldı: " + path);
        foreach (string path in sonuc.Atlanan) LogInfo("Motor DXF atlandı (dosya zaten var): " + path);
        foreach ((string hedef, string neden) in sonuc.Hatali) LogError("Motor DXF yazılamadı: " + hedef + " — " + neden);

        string rapor = (sonuc.IptalEdildi ? "İşlem iptal edildi.\n\n" : "") +
                       "Grup: " + SacGrubuAdi() + "\n" +
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

    private void SacExcelAktar()
    {
        List<MontajParcaSatiri> rows = (_sacParcaView?.Cast<MontajParcaSatiri>() ?? Enumerable.Empty<MontajParcaSatiri>()).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, SacGrubuAdi() + " sekmesinde aktarılacak satır yok.", "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Montaj Sac Listesini Kaydet — " + SacGrubuAdi(),
            Filter = "Excel Çalışma Kitabı (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "Macria_Montaj_Saclar_" + (_sacSalamaSekmesi ? "Salama-Kutuk" : "Lazer") + "_" +
                       DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ExcelYazici.Yaz(TabloRaporu(gridSacParcalar, _sacSalamaSekmesi ? "Saclar - Şalama-Kütük" : "Saclar - Lazer"), dialog.FileName);
            LogSuccess("Montaj sac listesi (" + SacGrubuAdi() + ") Excel'e yazıldı: " + dialog.FileName);
            MessageBox.Show(this, "Excel dosyası oluşturuldu:\n" + dialog.FileName, "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            LogError("Montaj sac listesi Excel'e yazılamadı: " + exception.Message);
            MessageBox.Show(this, "Excel dosyası yazılamadı.\n" + exception.Message, "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
