using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Macria;

// STEP / STP Analizi: "Sütunlar" of the common toolbar. Every tab keeps its
// own visible columns, order and the widths the user dragged the headers to
// (AnalizSutunDuzeni); Excel'e Aktar writes the open tab's visible columns in
// that order, the rows as the tab shows them.
public partial class MainWindow
{
    private static readonly AnalizSekmesi[] SutunluSekmeler =
    {
        AnalizSekmesi.Profiller, AnalizSekmesi.Saclar, AnalizSekmesi.KontrolGerekli, AnalizSekmesi.Tanimsiz, AnalizSekmesi.ListeDisi
    };

    // Shown only when the user turns them on.
    private static readonly HashSet<string> VarsayilanGizliSutunlar = new(StringComparer.Ordinal)
    {
        "STEP dosyası", "En küçük çevreleyen dikdörtgen (en × boy)"
    };

    private Dictionary<AnalizSekmesi, List<AnalizSutunu>> _analizSutunDuzeni = new();
    private readonly Dictionary<AnalizSekmesi, List<AnalizSutunu>> _analizVarsayilanSutunlari = new();
    // The designed width of every column, restored by "Varsayılan".
    private readonly Dictionary<DataGridColumn, DataGridLength> _sutunVarsayilanGenisligi = new();
    // A drag or reorder is saved once the mouse has settled; not while a layout is being applied.
    private readonly HashSet<AnalizSekmesi> _kaydedilecekSekmeler = new();
    private DispatcherTimer? _sutunKayitZamanlayici;
    private bool _sutunDuzeniUygulaniyor;
    // A size column ("en × boy") goes to Excel as two number columns.
    private static readonly Dictionary<string, (string Baslik, string Ozellik)[]> ExcelBolunenSutunlar = new()
    {
        [nameof(MontajParcaSatiri.AcinimOlcusuDisplay)] = new[]
        {
            ("Açınım eni (mm)", nameof(MontajParcaSatiri.AcinimEnMm)), ("Açınım boyu (mm)", nameof(MontajParcaSatiri.AcinimBoyMm))
        },
        [nameof(MontajParcaSatiri.EnKucukDikdortgenDisplay)] = new[]
        {
            ("En küçük dikdörtgen eni (mm)", nameof(MontajParcaSatiri.EnKucukDikdortgenEnMm)),
            ("En küçük dikdörtgen boyu (mm)", nameof(MontajParcaSatiri.EnKucukDikdortgenBoyMm))
        }
    };

    // Columns built in code bind through IAnalizSatiri: the property a cell shows.
    private readonly Dictionary<DataGridColumn, string> _sutunOzellikleri = new();

    private void AnalizSutunlariniKur()
    {
        _analizSutunDuzeni = AnalizSutunDuzeni.Oku(AnalizSutunDuzeni.Yol);
        var genislikIzleyici = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
        foreach (AnalizSekmesi sekme in SutunluSekmeler)
        {
            DataGrid grid = SekmeTablosu(sekme)!;
            _analizVarsayilanSutunlari[sekme] = grid.Columns
                .Select(c => new AnalizSutunu(SutunBasligi(c), !VarsayilanGizliSutunlar.Contains(SutunBasligi(c)))).ToList();
            foreach (DataGridColumn sutun in grid.Columns)
            {
                _sutunVarsayilanGenisligi[sutun] = sutun.Width;
                AnalizSekmesi sahibi = sekme;
                genislikIzleyici.AddValueChanged(sutun, (_, _) => SutunDuzeniDegisti(sahibi));
            }
            grid.ColumnReordered += (_, _) => SutunDuzeniDegisti(sekme);
            SutunDuzeniniUygula(sekme);
        }
        // A drag right before closing is not lost to the save delay.
        Closed += (_, _) =>
        {
            _sutunKayitZamanlayici?.Stop();
            SutunDuzeniniKaydet();
        };
    }

    private void SutunDuzeniDegisti(AnalizSekmesi sekme)
    {
        if (_sutunDuzeniUygulaniyor) return;
        _kaydedilecekSekmeler.Add(sekme);
        if (_sutunKayitZamanlayici == null)
        {
            _sutunKayitZamanlayici = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _sutunKayitZamanlayici.Tick += (_, _) =>
            {
                _sutunKayitZamanlayici.Stop();
                SutunDuzeniniKaydet();
            };
        }
        _sutunKayitZamanlayici.Stop();
        _sutunKayitZamanlayici.Start();
    }

    /// <summary>The changed tabs' grids as they stand (order, visibility, dragged widths) go to the layout file.</summary>
    private void SutunDuzeniniKaydet()
    {
        bool degisti = false;
        foreach (AnalizSekmesi sekme in _kaydedilecekSekmeler)
        {
            List<AnalizSutunu> yeni = SekmeTablosu(sekme)!.Columns.OrderBy(c => c.DisplayIndex)
                .Select(c => new AnalizSutunu(SutunBasligi(c), c.Visibility == Visibility.Visible, AyarlanmisGenislik(c))).ToList();
            // Layout passes re-fire Width with the same value: no write then.
            if (yeni.SequenceEqual(SekmeSutunDuzeni(sekme))) continue;
            _analizSutunDuzeni[sekme] = yeni;
            degisti = true;
        }
        _kaydedilecekSekmeler.Clear();
        if (degisti && AnalizSutunDuzeni.Yaz(AnalizSutunDuzeni.Yol, _analizSutunDuzeni) is string hata)
            LogError("Sütun genişlikleri kaydedilemedi: " + hata);
    }

    /// <summary>The width the user dragged the column to; null while it has its designed width.</summary>
    private double? AyarlanmisGenislik(DataGridColumn sutun)
    {
        DataGridLength varsayilan = _sutunVarsayilanGenisligi.GetValueOrDefault(sutun, sutun.Width);
        // Not DataGridLength's ==: that also compares the display width, which layout keeps changing.
        // A designed width below the header's minimum shows at that minimum: dragged back there, it is the default.
        if (!sutun.Width.IsAbsolute ||
            varsayilan.IsAbsolute && Math.Abs(sutun.Width.Value - Math.Max(varsayilan.Value, sutun.MinWidth)) < 0.5)
            return null;
        return Math.Round(sutun.Width.Value, 1);
    }

    private static string SutunBasligi(DataGridColumn sutun) =>
        sutun.Header as string ?? (sutun.Header as TextBlock)?.Text ?? "";

    private List<AnalizSutunu> SekmeSutunDuzeni(AnalizSekmesi sekme) =>
        AnalizSutunDuzeni.Birlestir(_analizSutunDuzeni.GetValueOrDefault(sekme), _analizVarsayilanSutunlari[sekme]);

    private void SutunDuzeniniUygula(AnalizSekmesi sekme)
    {
        DataGrid grid = SekmeTablosu(sekme)!;
        List<AnalizSutunu> duzen = SekmeSutunDuzeni(sekme);
        var sutunlar = duzen.Select(s => grid.Columns.First(c => SutunBasligi(c) == s.Baslik)).ToList();
        _sutunDuzeniUygulaniyor = true;
        try
        {
            for (int sira = 0; sira < duzen.Count; ++sira)
            {
                sutunlar[sira].Visibility = duzen[sira].Gorunur ? Visibility.Visible : Visibility.Collapsed;
                sutunlar[sira].Width = duzen[sira].Genislik is double genislik
                    ? new DataGridLength(genislik)
                    : _sutunVarsayilanGenisligi.GetValueOrDefault(sutunlar[sira], sutunlar[sira].Width);
            }
            // WPF shifts the others when DisplayIndex is set; left to right gives the saved order.
            for (int sira = 0; sira < sutunlar.Count; ++sira)
                sutunlar[sira].DisplayIndex = sira;
        }
        finally
        {
            _sutunDuzeniUygulaniyor = false;
        }
    }

    private static string SekmeAdi(AnalizSekmesi sekme) => sekme switch
    {
        AnalizSekmesi.Profiller => "Profiller",
        AnalizSekmesi.Saclar => "Saclar",
        AnalizSekmesi.KontrolGerekli => "Kontrol gerekli",
        AnalizSekmesi.Tanimsiz => "Tanımsız",
        _ => "Liste dışı"
    };

    private void btnAnalizSutunlar_Click(object sender, RoutedEventArgs e)
    {
        AnalizSekmesi sekme = AktifSekme();
        static List<ParcaSutunTanimi> Tanimlar(IEnumerable<AnalizSutunu> sutunlar) => sutunlar
            .Select(s => new ParcaSutunTanimi { Anahtar = s.Baslik, Baslik = s.Baslik, Gorunur = s.Gorunur }).ToList();
        // A drag still waiting for its save is part of the layout shown here.
        _sutunKayitZamanlayici?.Stop();
        SutunDuzeniniKaydet();
        ParcaSutunAyarlariWindow? pencere = null;
        pencere = new ParcaSutunAyarlariWindow(
            SekmeAdi(sekme) + " — Sütunlar",
            "Görünür sütunlar ve sıra yalnız bu sekmeye uygulanır" +
            (sekme == AnalizSekmesi.Saclar ? " (Lazer ve Şalama/Kütük birlikte)" : "") +
            ". Excel'e Aktar görünen sütunları bu sırayla yazar. Genişlikler başlık kenarından sürüklenerek ayarlanır; " +
            "Varsayılan onları da sıfırlar.",
            Tanimlar(SekmeSutunDuzeni(sekme)),
            () => Tanimlar(_analizVarsayilanSutunlari[sekme]),
            tanimlar =>
            {
                // Widths are not in the list: kept, unless "Varsayılan" was pressed.
                Dictionary<string, double?> genislikler = pencere?.VarsayilanaDonuldu == true
                    ? new()
                    : SekmeSutunDuzeni(sekme).ToDictionary(s => s.Baslik, s => s.Genislik);
                _analizSutunDuzeni[sekme] = tanimlar
                    .Select(t => new AnalizSutunu(t.Anahtar, t.Gorunur, genislikler.GetValueOrDefault(t.Anahtar))).ToList();
                return AnalizSutunDuzeni.Yaz(AnalizSutunDuzeni.Yol, _analizSutunDuzeni);
            })
        { Owner = this };
        OtomasyonModu.Gizle(pencere);
        if (pencere.ShowDialog() != true) return;
        SutunDuzeniniUygula(sekme);
        LogSuccess(SekmeAdi(sekme) + " sütunları güncellendi — görünür: " + SekmeSutunDuzeni(sekme).Count(s => s.Gorunur) + ".");
    }

    /// <summary>The property a column's cells show (Excel's value): its sort member or binding path.</summary>
    private string? SutunOzelligi(DataGridColumn sutun)
    {
        if (_sutunOzellikleri.TryGetValue(sutun, out string? ozellik)) return ozellik;
        if (!string.IsNullOrEmpty(sutun.SortMemberPath)) return sutun.SortMemberPath;
        return (sutun as DataGridBoundColumn)?.Binding is Binding baglama ? baglama.Path?.Path : null;
    }

    /// <summary>The grid's visible columns in display order and the rows it shows (filters and sort applied).</summary>
    private Rapor TabloRaporu(DataGrid grid, string sayfaAdi)
    {
        var rapor = new Rapor { SayfaAdi = sayfaAdi, TabloIlkSatirdanBaslar = true, IlkSatiriDondur = true, OtomatikFiltre = true };
        // (header, property, width) of every Excel column, a size column split in two.
        var sutunlar = new List<(string Baslik, string? Ozellik, double Genislik)>();
        foreach (DataGridColumn sutun in grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex))
        {
            string? ozellik = SutunOzelligi(sutun);
            if (ozellik != null && ExcelBolunenSutunlar.TryGetValue(ozellik, out var parcalar))
                sutunlar.AddRange(parcalar.Select(p => (p.Baslik, (string?)p.Ozellik, 1.3)));
            else
                sutunlar.Add((SutunBasligi(sutun), ozellik, Math.Clamp(sutun.ActualWidth / 65.0, 0.8, 5.0)));
        }
        List<object> satirlar = grid.Items.Cast<object>().Where(x => x != CollectionView.NewItemPlaceholder).ToList();
        var degerler = satirlar.Select(satir => sutunlar.Select(sutun => ExcelDegeri(AnalizSutunDuzeni.Deger(satir, sutun.Ozellik))).ToArray()).ToList();
        for (int i = 0; i < sutunlar.Count; ++i)
        {
            List<double> sayilar = degerler.Select(d => d[i]).OfType<double>().ToList();
            rapor.Sutunlar.Add(new RaporSutun
            {
                Ad = sutunlar[i].Baslik,
                Genislik = sutunlar[i].Genislik,
                Sayi = sayilar.Count > 0 && degerler.All(d => d[i] is null or double),
                Ondalik = sayilar.Count == 0 ? 0 : sayilar.Max(OndalikBasamagi)
            });
        }
        rapor.Satirlar.AddRange(degerler);
        return rapor;
    }

    private static object? ExcelDegeri(object? deger) => deger switch
    {
        null => null,
        double d => d,
        float f => (double)f,
        int i => (double)i,
        long l => (double)l,
        decimal m => (double)m,
        bool b => b ? "evet" : "hayır",
        _ => Convert.ToString(deger, CultureInfo.CurrentCulture) is string metin && metin is not "—" ? metin : null
    };

    private static int OndalikBasamagi(double deger)
    {
        for (int basamak = 0; basamak < 3; ++basamak)
            if (Math.Abs(Math.Round(deger, basamak) - deger) < 1e-9) return basamak;
        return 3;
    }
}
