using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Macria;

// Dosya Analiz Merkezi → STEP / STP Analizi: the five result tabs
// (Profiller | Saclar | Kontrol gerekli | Tanımsız | Liste dışı), the common
// toolbar above them and the right panel beside them
// (docs/SEKME_VE_ARAC_CUBUGU_PLANI.md). Profiller and Saclar keep their own
// columns; the mixed tabs list profile and part rows through IAnalizSatiri.
public partial class MainWindow
{
    // Every row of both kinds; the mixed tabs are filtered views of it.
    private readonly ObservableCollection<IAnalizSatiri> _tumSatirlar = new();
    private ListCollectionView? _kontrolView;
    private ListCollectionView? _tanimsizView;
    private ListCollectionView? _listeDisiView;

    private void AnalizSekmeleriniKur()
    {
        _kontrolView = SekmeGorunumu(AnalizSekmesi.KontrolGerekli);
        _tanimsizView = SekmeGorunumu(AnalizSekmesi.Tanimsiz);
        _listeDisiView = SekmeGorunumu(AnalizSekmesi.ListeDisi);
        foreach ((DataGrid grid, ListCollectionView view) in new[]
                 {
                     (gridKontrolParcalar, _kontrolView), (gridTanimsiz, _tanimsizView), (gridListeDisi, _listeDisiView)
                 })
        {
            OrtakSutunlariKur(grid);
            grid.ItemsSource = view;
            grid.SelectionChanged += KarmaTablo_SelectionChanged;
        }
        _externalStepProfileRows.CollectionChanged += (_, _) => TumSatirlariYenile();
        _montajParcaRows.CollectionChanged += (_, _) => TumSatirlariYenile();
        TumSatirlariYenile();
    }

    private ListCollectionView SekmeGorunumu(AnalizSekmesi sekme) =>
        new(_tumSatirlar) { Filter = item => item is IAnalizSatiri satir && satir.Sekme == sekme };

    private void TumSatirlariYenile()
    {
        _tumSatirlar.Clear();
        foreach (GeometryLabStepProfileListItem row in _externalStepProfileRows) _tumSatirlar.Add(row);
        foreach (MontajParcaSatiri row in _montajParcaRows) _tumSatirlar.Add(row);
        AnalizSekmeleriniGuncelle();
    }

    // Common columns of the mixed tabs; bound through the interface so both
    // row kinds (with explicit implementations) show the same cells.
    private void OrtakSutunlariKur(DataGrid grid)
    {
        grid.Columns.Clear();
        var hucre = (Style)FindResource("KirpilanHucre");
        foreach ((string baslik, string ozellik, DataGridLength genislik) in new[]
                 {
                     ("Durum", nameof(IAnalizSatiri.DurumEtiketi), new DataGridLength(150)),
                     ("Parça", nameof(IAnalizSatiri.ParcaGosterimi), new DataGridLength(220)),
                     ("Adet", nameof(IAnalizSatiri.AdetGosterimi), DataGridLength.Auto),
                     ("Tür", nameof(IAnalizSatiri.TurGosterimi), new DataGridLength(120)),
                     ("Ölçü", nameof(IAnalizSatiri.OlcuGosterimi), new DataGridLength(140)),
                     ("CATIA Adedi", nameof(IAnalizSatiri.CatiaQuantityDisplay), DataGridLength.Auto),
                     ("Eşleşme", nameof(IAnalizSatiri.CatiaMatchDisplay), new DataGridLength(130)),
                     ("Karar", nameof(IAnalizSatiri.KararGosterimi), new DataGridLength(100)),
                     ("Kullanıcı kararı", nameof(IAnalizSatiri.KullaniciKarariMetni), new DataGridLength(200)),
                     ("Motor gerekçesi", nameof(IAnalizSatiri.MotorGerekcesi), new DataGridLength(1, DataGridLengthUnitType.Star))
                 })
        {
            var yol = new PropertyPath("(0)", typeof(IAnalizSatiri).GetProperty(ozellik)!);
            var stil = new Style(typeof(TextBlock), hucre);
            stil.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding { Path = yol }));
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = baslik,
                Binding = new Binding { Path = yol, Mode = BindingMode.OneWay },
                Width = genislik,
                MinWidth = baslik == "Motor gerekçesi" ? 200 : 50,
                ElementStyle = stil
            });
        }
    }

    private AnalizSekmesi AktifSekme()
    {
        object secili = tabExternalStepSonuc?.SelectedItem!;
        return ReferenceEquals(secili, tabExternalStepSaclar) ? AnalizSekmesi.Saclar
            : ReferenceEquals(secili, tabExternalStepKontrol) ? AnalizSekmesi.KontrolGerekli
            : ReferenceEquals(secili, tabExternalStepTanimsiz) ? AnalizSekmesi.Tanimsiz
            : ReferenceEquals(secili, tabExternalStepListeDisi) ? AnalizSekmesi.ListeDisi
            : AnalizSekmesi.Profiller;
    }

    private DataGrid? SekmeTablosu(AnalizSekmesi sekme) => sekme switch
    {
        AnalizSekmesi.Saclar => gridSacParcalar,
        AnalizSekmesi.KontrolGerekli => gridKontrolParcalar,
        AnalizSekmesi.Tanimsiz => gridTanimsiz,
        AnalizSekmesi.ListeDisi => gridListeDisi,
        _ => gridExternalStepProfil
    };

    /// <summary>The selected rows of the open tab that still belong to it.</summary>
    private List<IAnalizSatiri> SeciliAnalizSatirlari()
    {
        AnalizSekmesi sekme = AktifSekme();
        return SekmeTablosu(sekme)?.SelectedItems.OfType<IAnalizSatiri>().Where(x => x.Sekme == sekme).ToList() ?? new();
    }

    private int SekmedekiSatirSayisi(AnalizSekmesi sekme) => _tumSatirlar.Count(x => x.Sekme == sekme);

    /// <summary>Re-filters every tab and updates the tab counts, the Saclar summary and the toolbar.</summary>
    private void AnalizSekmeleriniGuncelle()
    {
        _externalStepProfileView?.Refresh();
        _sacParcaView?.Refresh();
        _kontrolView?.Refresh();
        _tanimsizView?.Refresh();
        _listeDisiView?.Refresh();
        if (tabExternalStepProfiller == null) return;
        tabExternalStepProfiller.Header = "Profiller (" + SekmedekiSatirSayisi(AnalizSekmesi.Profiller) + ")";
        tabExternalStepSaclar.Header = "Saclar (" + SekmedekiSatirSayisi(AnalizSekmesi.Saclar) + ")";
        tabExternalStepKontrol.Header = "Kontrol gerekli (" + SekmedekiSatirSayisi(AnalizSekmesi.KontrolGerekli) + ")";
        tabExternalStepTanimsiz.Header = "Tanımsız (" + SekmedekiSatirSayisi(AnalizSekmesi.Tanimsiz) + ")";
        tabExternalStepListeDisi.Header = "Liste dışı (" + SekmedekiSatirSayisi(AnalizSekmesi.ListeDisi) + ")";
        tabSacLazer.Header = "Lazer (" + _montajParcaRows.Count(x => x.IsInSheetTab && !x.IsThickPlate) + ")";
        tabSacSalama.Header = "Şalama/Kütük (" + _montajParcaRows.Count(x => x.IsInSheetTab && x.IsThickPlate) + ")";
        List<MontajParcaSatiri> grup = SacGrubuSatirlari();
        int onayli = grup.Count(x => x.EffectiveCategory == MontajParcaKategorisi.Sac);
        int bekleyen = grup.Count(x => x.EffectiveCategory == MontajParcaKategorisi.OnayGerekli);
        string esik = MontajParcaSatiri.FormatNumber(_projeLazerMm);
        txtSacOzet.Text = SacGrubuAdi() + ": " + onayli + " onaylı sac, " + bekleyen + " onay bekleyen. " +
                          (_sacSalamaSekmesi ? "Kalınlık > " : "Kalınlık ≤ ") + esik + " mm.";
        AracCubugunuGuncelle();
        ExternalStepAnalizButonunuGuncelle();
    }

    private void AracCubugunuGuncelle()
    {
        if (btnAnalizExcel == null) return;
        AnalizSekmesi sekme = AktifSekme();
        AnalizAracDurumu durum = AnalizAracDurumu.Hesapla(sekme, SeciliAnalizSatirlari(), SekmedekiSatirSayisi(sekme) > 0,
            SacGrubuSatirlari().Any(x => x.EffectiveCategory == MontajParcaKategorisi.Sac));
        static Visibility Gorunur(bool gorunur) => gorunur ? Visibility.Visible : Visibility.Collapsed;
        btnAnalizExcel.IsEnabled = durum.ExcelEtkin;
        btnAnalizDosyayiAc.IsEnabled = durum.DosyayiAcEtkin;
        btnAnalizDxfUret.Visibility = Gorunur(durum.DxfUretGorunur);
        btnAnalizDxfUret.IsEnabled = durum.DxfUretEtkin;
        btnAnalizProfilOnayla.Visibility = Gorunur(durum.ProfilOnaylaGorunur);
        btnAnalizProfilOnayla.IsEnabled = durum.ProfilOnaylaEtkin;
        btnAnalizSacOnayla.Visibility = Gorunur(durum.SacOnaylaGorunur);
        btnAnalizSacOnayla.IsEnabled = durum.SacOnaylaEtkin;
        btnAnalizKontrole.Visibility = Gorunur(durum.KontroleGorunur);
        btnAnalizKontrole.IsEnabled = durum.KontroleEtkin;
        btnAnalizListeDisi.Visibility = Gorunur(durum.ListeDisiGorunur);
        btnAnalizListeDisi.IsEnabled = durum.ListeDisiEtkin;
        btnAnalizOtomatik.Visibility = Gorunur(durum.OtomatikGorunur);
        btnAnalizOtomatik.IsEnabled = durum.OtomatikEtkin;
        btnAnalizGeriAl.Visibility = Gorunur(durum.GeriAlGorunur);
        btnAnalizGeriAl.IsEnabled = durum.GeriAlEtkin;
    }

    // One right panel for every tab: the 3D view (in Saclar also the engine
    // DXF, "Açınım (2B) | 3B") and the "Seçili Parça" lines.
    private const string GenelBosMesaji = "Önizlemek için listeden bir parça seçin.";

    private void SagPaneliGuncelle()
    {
        if (onizleme3B == null) return;
        List<IAnalizSatiri> secili = SeciliAnalizSatirlari();
        bool sac = AktifSekme() == AnalizSekmesi.Saclar;
        bool ikiB = sac && !_sac3BModu;
        pnlSacGecis.Visibility = sac ? Visibility.Visible : Visibility.Collapsed;
        txtSagBaslik.Text = ikiB ? "Motor DXF Önizleme" : "3B Önizleme";
        sacOnizleme2B.Visibility = ikiB ? Visibility.Visible : Visibility.Collapsed;
        pnlSacOnizlemeAlt.Visibility = ikiB ? Visibility.Visible : Visibility.Collapsed;
        onizleme3B.Visibility = ikiB ? Visibility.Collapsed : Visibility.Visible;
        if (sac) SacOnizlemesiniGoster(secili.Count == 1 ? secili[0] as MontajParcaSatiri : null, secili.Count > 1);
        string mesaj = secili.Count > 1 ? "Birden fazla parça seçildi." : GenelBosMesaji;
        // In 2D the 3D model is let go, as before, to give the memory back.
        if (ikiB || secili.Count != 1) onizleme3B.Temizle(mesaj);
        else onizleme3B.Goster(secili[0].KaynakYolu, secili[0].ParcaAdi, _montajParcaGorunumu);
        SeciliParcaAyrintisiniGoster(secili);
        BuyukOnizlemeyiKaynakla();
    }

    private void SeciliParcaAyrintisiniGoster(List<IAnalizSatiri> secili)
    {
        pnlSeciliParca.Children.Clear();
        if (secili.Count != 1)
        {
            txtSeciliParcaMesaj.Visibility = Visibility.Visible;
            txtSeciliParcaMesaj.Text = secili.Count > 1 ? "Birden fazla parça seçildi." : "Ayrıntıları görmek için listeden bir parça seçin.";
            return;
        }
        txtSeciliParcaMesaj.Visibility = Visibility.Collapsed;
        foreach (KeyValuePair<string, string> satir in secili[0].Ayrintilar)
        {
            var metin = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            metin.Inlines.Add(new System.Windows.Documents.Run(satir.Key + ": ")
            {
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
            });
            metin.Inlines.Add(new System.Windows.Documents.Run(satir.Value));
            pnlSeciliParca.Children.Add(metin);
        }
    }

    private void KarmaTablo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection changes bubble up; only the open tab's grid counts.
        if (!ReferenceEquals(sender, SekmeTablosu(AktifSekme()))) return;
        AracCubugunuGuncelle();
        SagPaneliGuncelle();
    }

    // ------------------------------------------------------------- toolbar

    private void btnAnalizExcel_Click(object sender, RoutedEventArgs e)
    {
        switch (AktifSekme())
        {
            case AnalizSekmesi.Profiller: ProfilExcelAktar(); break;
            case AnalizSekmesi.Saclar: SacExcelAktar(); break;
            default: KarmaExcelAktar(AktifSekme()); break;
        }
    }

    private void btnAnalizDosyayiAc_Click(object sender, RoutedEventArgs e)
    {
        List<IAnalizSatiri> secili = SeciliAnalizSatirlari();
        if (secili.Count == 1) StepDosyasiniAc(secili[0].KaynakYolu);
    }

    private void btnAnalizProfilOnayla_Click(object sender, RoutedEventArgs e) =>
        ProfilleriOnayla(SeciliAnalizSatirlari().OfType<GeometryLabStepProfileListItem>().ToList());

    private void btnAnalizSacOnayla_Click(object sender, RoutedEventArgs e)
    {
        List<MontajParcaSatiri> secili = SeciliAnalizSatirlari().OfType<MontajParcaSatiri>()
            .Where(x => x.EffectiveCategory != MontajParcaKategorisi.Sac).ToList();
        KararUygula(secili.Cast<IAnalizSatiri>().ToList(), satir => ((MontajParcaSatiri)satir).ApproveAsSheet());
        foreach (MontajParcaSatiri satir in secili.Where(x => x.DxfSourcePath == null))
            LogInfo("Sac olarak onaylandı, motor açınımı yok: " + satir.PartName + " — DXF CATIA'dan alınmalı.");
    }

    private void btnAnalizKontrole_Click(object sender, RoutedEventArgs e) =>
        KararUygula(SeciliAnalizSatirlari(), satir => satir.KontrolGerekliyeAl());

    private void btnAnalizListeDisi_Click(object sender, RoutedEventArgs e) =>
        KararUygula(SeciliAnalizSatirlari(), satir => satir.ListeDisinaCikar());

    private void btnAnalizOtomatik_Click(object sender, RoutedEventArgs e) =>
        KararUygula(SeciliAnalizSatirlari().Where(x => x.HasUserDecision).ToList(), satir => satir.RestoreAutomaticDecision());

    private void btnAnalizGeriAl_Click(object sender, RoutedEventArgs e) =>
        KararUygula(SeciliAnalizSatirlari(), satir => satir.ListeyeGeriAl());

    private void KararUygula(List<IAnalizSatiri> satirlar, Action<IAnalizSatiri> karar)
    {
        if (satirlar.Count == 0 || ProjeSaltOkunurUyarisi()) return;
        foreach (IAnalizSatiri satir in satirlar) karar(satir);
        ProjeDegisti();
        AnalizSekmeleriniGuncelle();
    }

    private void StepDosyasiniAc(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "Seçilen STEP dosyası artık belirtilen konumda bulunmuyor.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (Path.GetExtension(path).ToLowerInvariant() is not ".stp" and not ".step")
        {
            MessageBox.Show(this, "Seçilen dosya desteklenen bir STEP dosyası değil.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception exception)
        {
            LogError("STEP dosyası açılamadı: " + exception.Message);
            MessageBox.Show(this, "Bu dosya türü için Windows'ta varsayılan bir uygulama tanımlı değil.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            LogError("STEP dosyası açılamadı: " + exception.Message);
            MessageBox.Show(this, "Seçilen STEP dosyası açılamadı.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Kontrol gerekli / Tanımsız / Liste dışı: the common columns, both row kinds.
    private void KarmaExcelAktar(AnalizSekmesi sekme)
    {
        string ad = sekme switch
        {
            AnalizSekmesi.KontrolGerekli => "Kontrol gerekli",
            AnalizSekmesi.Tanimsiz => "Tanımsız",
            _ => "Liste dışı"
        };
        List<IAnalizSatiri> satirlar = _tumSatirlar.Where(x => x.Sekme == sekme).ToList();
        if (satirlar.Count == 0)
        {
            MessageBox.Show(this, ad + " sekmesinde aktarılacak satır yok.", "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "STEP Analizi — " + ad,
            Filter = "Excel Çalışma Kitabı (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "Macria_STEP_" + ad.Replace(' ', '_').Replace('ı', 'i').Replace('ş', 's') + "_" +
                       DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        var rapor = new Rapor { SayfaAdi = ad, TabloIlkSatirdanBaslar = true, IlkSatiriDondur = true, OtomatikFiltre = true };
        foreach ((string sutun, double genislik) in new[]
                 {
                     ("Durum", 2.0), ("Parça", 2.6), ("Adet", 0.8), ("Tür", 1.4), ("Ölçü", 1.6), ("CATIA Adedi", 1.1),
                     ("CATIA Eşleşme", 1.9), ("Karar", 1.3), ("Kullanıcı kararı", 2.8), ("Motor gerekçesi", 4.0), ("STEP Dosyası", 2.2)
                 })
            rapor.Sutunlar.Add(new RaporSutun { Ad = sutun, Genislik = genislik });
        foreach (IAnalizSatiri satir in satirlar)
            rapor.Satirlar.Add(new object?[]
            {
                satir.DurumEtiketi, satir.ParcaGosterimi, satir.AdetGosterimi, satir.TurGosterimi, satir.OlcuGosterimi,
                satir.CatiaQuantityDisplay, satir.CatiaMatchDisplay, satir.KararGosterimi, satir.KullaniciKarariMetni, satir.MotorGerekcesi,
                Path.GetFileName(satir.KaynakYolu)
            });
        try
        {
            ExcelYazici.Yaz(rapor, dialog.FileName);
            LogSuccess("STEP analizi (" + ad + ") Excel'e yazıldı: " + dialog.FileName);
            MessageBox.Show(this, "Excel dosyası oluşturuldu:\n" + dialog.FileName, "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            LogError("STEP analizi Excel'e yazılamadı: " + exception.Message);
            MessageBox.Show(this, "Excel dosyası yazılamadı.\n" + exception.Message, "Excel'e Aktar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
