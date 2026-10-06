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
        AnalizSutunlariniKur();
        _externalStepProfileRows.CollectionChanged += (_, _) => TumSatirlariYenile();
        _montajParcaRows.CollectionChanged += (_, _) => TumSatirlariYenile();
        TumSatirlariYenile();
    }

    private ListCollectionView SekmeGorunumu(AnalizSekmesi sekme) =>
        new(_tumSatirlar) { Filter = item => item is IAnalizSatiri satir && satir.Sekme == sekme && AramayaUyar(satir) };

    // The search box above the tabs: every tab and the Lazer / Şalama sub-tabs
    // list only the rows whose part name matches; the tab counts follow it.
    private string _analizArama = "";

    private bool AramayaUyar(IAnalizSatiri satir) => SekmeKurallari.AramayaUyar(satir, _analizArama);

    private void txtAnalizArama_TextChanged(object sender, TextChangedEventArgs e)
    {
        _analizArama = (txtAnalizArama.Text ?? "").Trim();
        AnalizSekmeleriniGuncelle();
        SagPaneliGuncelle();
    }

    private void txtAnalizArama_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || txtAnalizArama.Text.Length == 0) return;
        txtAnalizArama.Text = "";
        e.Handled = true;
    }

    // A column pasted from Excel: its lines become ", "-separated terms (the
    // one-line box would keep only the first line).
    private void txtAnalizArama_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string metin || !metin.Contains('\n') && !metin.Contains('\r')) return;
        e.DataObject = new DataObject(DataFormats.UnicodeText, SekmeKurallari.AramaYapistir(metin));
    }

    /// <summary>"Saclar (12)", or while searching "Saclar (3 / 12)".</summary>
    private string SekmeBasligi(string ad, int bulunan, int toplam) =>
        ad + " (" + (SekmeKurallari.AramaTerimleri(_analizArama).Count == 0 ? toplam.ToString() : bulunan + " / " + toplam) + ")";

    // A row's place in the mixed tabs, by its part (STEP + localId; a file
    // row by itself): given when the part first appears, kept when a run
    // replaces its row, even by a row of the other kind.
    private readonly Dictionary<object, int> _satirYerleri = new(new SatirYeriKarsilastirici());

    // Part keys (strings) by value, file rows by reference.
    private sealed class SatirYeriKarsilastirici : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => x is string a && y is string b ? a == b : ReferenceEquals(x, y);
        public int GetHashCode(object o) => o is string s ? s.GetHashCode() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }

    private static object SatirYeriAnahtari(IAnalizSatiri satir) => satir.ParcaLocalId is int id
        ? Path.GetFullPath(satir.KaynakYolu).ToUpperInvariant() + "|" + id
        : satir;

    private void TumSatirlariYenile()
    {
        _tumSatirlar.Clear();
        if (_externalStepProfileRows.Count == 0 && _montajParcaRows.Count == 0) _satirYerleri.Clear();
        var satirlar = _externalStepProfileRows.Cast<IAnalizSatiri>().Concat(_montajParcaRows).ToList();
        foreach (IAnalizSatiri satir in satirlar)
            if (!_satirYerleri.ContainsKey(SatirYeriAnahtari(satir)))
                _satirYerleri[SatirYeriAnahtari(satir)] = _satirYerleri.Count;
        foreach (IAnalizSatiri satir in satirlar.OrderBy(x => _satirYerleri[SatirYeriAnahtari(x)]))
            _tumSatirlar.Add(satir);
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
                     ("İşleme", nameof(IAnalizSatiri.IslemeGosterimi), new DataGridLength(120)),
                     ("CATIA Adedi", nameof(IAnalizSatiri.CatiaQuantityDisplay), DataGridLength.Auto),
                     ("Eşleşme", nameof(IAnalizSatiri.CatiaMatchDisplay), new DataGridLength(130)),
                     ("Karar", nameof(IAnalizSatiri.KararGosterimi), new DataGridLength(100)),
                     ("Kullanıcı kararı", nameof(IAnalizSatiri.KullaniciKarariMetni), new DataGridLength(200)),
                     ("Motor gerekçesi", nameof(IAnalizSatiri.MotorGerekcesi), new DataGridLength(360)),
                     ("STEP dosyası", nameof(IAnalizSatiri.KaynakYolu), new DataGridLength(260))
                 })
        {
            var yol = new PropertyPath("(0)", typeof(IAnalizSatiri).GetProperty(ozellik)!);
            var stil = new Style(typeof(TextBlock), hucre);
            stil.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding { Path = yol }));
            var sutun = new DataGridTextColumn
            {
                Header = baslik,
                Binding = new Binding { Path = yol, Mode = BindingMode.OneWay },
                Width = genislik,
                MinWidth = 50,
                ElementStyle = stil
            };
            _sutunOzellikleri[sutun] = ozellik;
            grid.Columns.Add(sutun);
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

    /// <summary>Rows the tab lists (the search applied).</summary>
    private int SekmedekiSatirSayisi(AnalizSekmesi sekme) => _tumSatirlar.Count(x => x.Sekme == sekme && AramayaUyar(x));

    private int SekmedekiToplamSatir(AnalizSekmesi sekme) => _tumSatirlar.Count(x => x.Sekme == sekme);

    /// <summary>Re-filters every tab and updates the tab counts, the Saclar summary and the toolbar.</summary>
    private void AnalizSekmeleriniGuncelle()
    {
        _externalStepProfileView?.Refresh();
        _sacParcaView?.Refresh();
        _kontrolView?.Refresh();
        _tanimsizView?.Refresh();
        _listeDisiView?.Refresh();
        if (tabExternalStepProfiller == null) return;
        foreach ((TabItem sekmeBasligi, string ad, AnalizSekmesi sekme) in new[]
                 {
                     (tabExternalStepProfiller, "Profiller", AnalizSekmesi.Profiller),
                     (tabExternalStepSaclar, "Saclar", AnalizSekmesi.Saclar),
                     (tabExternalStepKontrol, "Kontrol gerekli", AnalizSekmesi.KontrolGerekli),
                     (tabExternalStepTanimsiz, "Tanımsız", AnalizSekmesi.Tanimsiz),
                     (tabExternalStepListeDisi, "Liste dışı", AnalizSekmesi.ListeDisi)
                 })
            sekmeBasligi.Header = SekmeBasligi(ad, SekmedekiSatirSayisi(sekme), SekmedekiToplamSatir(sekme));
        tabSacLazer.Header = SekmeBasligi("Lazer", _montajParcaRows.Count(x => x.IsInSheetTab && !x.IsThickPlate && AramayaUyar(x)),
            _montajParcaRows.Count(x => x.IsInSheetTab && !x.IsThickPlate));
        tabSacSalama.Header = SekmeBasligi("Şalama/Kütük", _montajParcaRows.Count(x => x.IsInSheetTab && x.IsThickPlate && AramayaUyar(x)),
            _montajParcaRows.Count(x => x.IsInSheetTab && x.IsThickPlate));
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
            SacGrubuSatirlari().Any(x => x.EffectiveCategory == MontajParcaKategorisi.Sac),
            _externalStepProfileAnalysisRunning || _projeIslemde);
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
        btnAnalizYenidenAnaliz.Visibility = Gorunur(durum.YenidenAnalizGorunur);
        btnAnalizYenidenAnaliz.IsEnabled = durum.YenidenAnalizEtkin;
        // The trials go with Yeniden Analiz Et: same tabs, same rows.
        foreach ((Button dugme, string ipucu) in new[] { (btnAnalizProfilDene, ProfilDeneIpucu), (btnAnalizSacDene, SacDeneIpucu) })
        {
            dugme.Visibility = Gorunur(durum.YenidenAnalizGorunur);
            dugme.IsEnabled = durum.DenemeEtkin;
            // A part closed to trials says why, also on the disabled button.
            dugme.ToolTip = durum.DenemeKapaliNedeni ?? ipucu;
        }
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
            if (satir.Key == TeknikAyrinti.Anahtar)
            {
                // The engine's own text, folded: face ids and inner steps stay out of sight.
                pnlSeciliParca.Children.Add(new Expander
                {
                    Header = TeknikAyrinti.Anahtar,
                    IsExpanded = false,
                    Margin = new Thickness(0, 6, 0, 0),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    Content = new TextBlock { Text = satir.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) }
                });
                continue;
            }
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

    private void btnAnalizOtomatik_Click(object sender, RoutedEventArgs e)
    {
        List<IAnalizSatiri> secili = SeciliAnalizSatirlari();
        // A row from a run on selected parts goes back to the STEP's own analysis.
        List<IAnalizSatiri> denemeli = secili.Where(x => x.Deneme != null).ToList();
        if (denemeli.Count > 0 && !ProjeSaltOkunurUyarisi()) DenemeleriKaldir(denemeli);
        KararUygula(secili.Except(denemeli).Where(x => x.HasUserDecision).ToList(), satir => satir.RestoreAutomaticDecision());
    }

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
        if (SekmeTablosu(sekme)!.Items.Count == 0)
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
        Rapor rapor = TabloRaporu(SekmeTablosu(sekme)!, ad);
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
