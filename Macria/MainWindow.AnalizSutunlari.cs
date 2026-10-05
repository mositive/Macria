using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Macria;

// STEP / STP Analizi: "Sütunlar" of the common toolbar. Every tab keeps its
// own visible columns and order (AnalizSutunDuzeni); Excel'e Aktar writes the
// open tab's visible columns in that order, the rows as the tab shows them.
public partial class MainWindow
{
    private static readonly AnalizSekmesi[] SutunluSekmeler =
    {
        AnalizSekmesi.Profiller, AnalizSekmesi.Saclar, AnalizSekmesi.KontrolGerekli, AnalizSekmesi.Tanimsiz, AnalizSekmesi.ListeDisi
    };

    // Shown only when the user turns them on.
    private static readonly HashSet<string> VarsayilanGizliSutunlar = new(StringComparer.Ordinal) { "STEP dosyası" };

    private Dictionary<AnalizSekmesi, List<AnalizSutunu>> _analizSutunDuzeni = new();
    private readonly Dictionary<AnalizSekmesi, List<AnalizSutunu>> _analizVarsayilanSutunlari = new();
    // Columns built in code bind through IAnalizSatiri: the property a cell shows.
    private readonly Dictionary<DataGridColumn, string> _sutunOzellikleri = new();

    private void AnalizSutunlariniKur()
    {
        _analizSutunDuzeni = AnalizSutunDuzeni.Oku(AnalizSutunDuzeni.VarsayilanYol);
        foreach (AnalizSekmesi sekme in SutunluSekmeler)
        {
            DataGrid grid = SekmeTablosu(sekme)!;
            _analizVarsayilanSutunlari[sekme] = grid.Columns
                .Select(c => new AnalizSutunu(SutunBasligi(c), !VarsayilanGizliSutunlar.Contains(SutunBasligi(c)))).ToList();
            SutunDuzeniniUygula(sekme);
        }
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
        for (int sira = 0; sira < duzen.Count; ++sira)
            sutunlar[sira].Visibility = duzen[sira].Gorunur ? Visibility.Visible : Visibility.Collapsed;
        // WPF shifts the others when DisplayIndex is set; left to right gives the saved order.
        for (int sira = 0; sira < sutunlar.Count; ++sira)
            sutunlar[sira].DisplayIndex = sira;
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
        var pencere = new ParcaSutunAyarlariWindow(
            SekmeAdi(sekme) + " — Sütunlar",
            "Görünür sütunlar ve sıra yalnız bu sekmeye uygulanır" +
            (sekme == AnalizSekmesi.Saclar ? " (Lazer ve Şalama/Kütük birlikte)" : "") +
            ". Excel'e Aktar görünen sütunları bu sırayla yazar.",
            Tanimlar(SekmeSutunDuzeni(sekme)),
            () => Tanimlar(_analizVarsayilanSutunlari[sekme]),
            tanimlar =>
            {
                _analizSutunDuzeni[sekme] = tanimlar.Select(t => new AnalizSutunu(t.Anahtar, t.Gorunur)).ToList();
                return AnalizSutunDuzeni.Yaz(AnalizSutunDuzeni.VarsayilanYol, _analizSutunDuzeni);
            })
        { Owner = this };
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
        List<DataGridColumn> sutunlar = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToList();
        List<object> satirlar = grid.Items.Cast<object>().Where(x => x != CollectionView.NewItemPlaceholder).ToList();
        var degerler = satirlar.Select(satir => sutunlar.Select(sutun => ExcelDegeri(AnalizSutunDuzeni.Deger(satir, SutunOzelligi(sutun)))).ToArray()).ToList();
        for (int i = 0; i < sutunlar.Count; ++i)
        {
            List<double> sayilar = degerler.Select(d => d[i]).OfType<double>().ToList();
            rapor.Sutunlar.Add(new RaporSutun
            {
                Ad = SutunBasligi(sutunlar[i]),
                Genislik = Math.Clamp(sutunlar[i].ActualWidth / 65.0, 0.8, 5.0),
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
