using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Macria;

/// <summary>One column of a result tab: its header, whether it is shown and the width the user dragged it to (null: default).</summary>
public sealed record AnalizSutunu(string Baslik, bool Gorunur, double? Genislik = null);

/// <summary>
/// "Sütunları Düzenle" of the STEP / STP Analizi tabs: per tab the visible
/// columns and their order, kept in %AppData%\Macria\analiz-sutunlari.txt
/// (one line per column: tab|header|1 or 0[|width]). Excel writes the visible columns
/// in this order. No WPF here; MainWindow.AnalizSutunlari applies it to the grids.
/// </summary>
public static class AnalizSutunDuzeni
{
    public static string VarsayilanYol => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macria", "analiz-sutunlari.txt");

    /// <summary>The column every tab has (hidden by default) with the row's STEP file.</summary>
    public const string StepSutunu = "STEP dosyası";

    // Whole-number columns: a whole number in the cell goes to Excel as a number, not text.
    private static readonly HashSet<string> AdetSutunlari = new(StringComparer.Ordinal) { "Adet", "CATIA Adedi", "Büküm" };

    // The engine's sizes carry float noise (8.000000000017916): a thickness
    // goes to Excel to 0,01 mm, a flat size to 0,1 mm, as the cells show them.
    private static readonly HashSet<string> KalinlikSutunlari = new(StringComparer.Ordinal) { "Ham sac kalınlığı (mm)", "Tespit edilen kalınlık" };
    private static readonly HashSet<string> AcinimSutunlari = new(StringComparer.Ordinal)
    {
        "Açınım eni (mm)", "Açınım boyu (mm)", "En küçük dikdörtgen eni (mm)", "En küçük dikdörtgen boyu (mm)"
    };

    /// <summary>
    /// A cell value as Excel writes it: "4" in a quantity or bend column is the
    /// number 4 ("—" stays empty); thicknesses rounded to 0,01 mm and flat
    /// sizes to 0,1 mm; the STEP column holds the file name, not the path.
    /// </summary>
    public static object? ExcelDegeri(string baslik, object? deger)
    {
        if (deger is double sayi && KalinlikSutunlari.Contains(baslik)) return Math.Round(sayi, 2);
        if (deger is double olcu && AcinimSutunlari.Contains(baslik)) return Math.Round(olcu, 1);
        if (deger is string metin && AdetSutunlari.Contains(baslik) &&
            int.TryParse(metin.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int adet))
            return (double)adet;
        if (deger is string yol && baslik == StepSutunu && yol.Length > 0)
            return Path.GetFileName(yol);
        return deger;
    }

    /// <summary>Where MainWindow keeps the layout; a test harness points it elsewhere.</summary>
    public static string Yol { get; set; } = VarsayilanYol;

    /// <summary>
    /// The saved layout over the grid's current columns: saved columns in
    /// their saved order and visibility, columns the save does not know
    /// (new in this Macria) where they stand by default, unknown saved ones
    /// dropped. At least one column stays visible.
    /// </summary>
    public static List<AnalizSutunu> Birlestir(IReadOnlyList<AnalizSutunu>? kayitli, IReadOnlyList<AnalizSutunu> varsayilan)
    {
        var bilinen = varsayilan.Select(x => x.Baslik).ToHashSet(StringComparer.Ordinal);
        var sonuc = new List<AnalizSutunu>();
        if (kayitli != null)
            foreach (AnalizSutunu sutun in kayitli)
                if (bilinen.Contains(sutun.Baslik) && sonuc.All(x => x.Baslik != sutun.Baslik))
                    sonuc.Add(sutun);
        for (int sira = 0; sira < varsayilan.Count; ++sira)
        {
            AnalizSutunu yeni = varsayilan[sira];
            if (sonuc.Any(x => x.Baslik == yeni.Baslik)) continue;
            // After the default column that comes before it, or first.
            int once = sira == 0 ? -1 : sonuc.FindIndex(x => x.Baslik == varsayilan[sira - 1].Baslik);
            sonuc.Insert(once + 1, yeni);
        }
        if (!sonuc.Any(x => x.Gorunur)) return varsayilan.ToList();
        return sonuc;
    }

    /// <summary>Columns renamed since their layout was saved: old header → new.</summary>
    public static readonly IReadOnlyDictionary<string, string> EskiAdlar = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Kalınlık (mm)"] = "Ham sac kalınlığı (mm)",
        ["Motor kalınlığı"] = "Tespit edilen kalınlık",
        ["Ham sac ölçüsü"] = "Açınım ölçüsü (en × boy)"
    };

    public static Dictionary<AnalizSekmesi, List<AnalizSutunu>> Oku(string yol)
    {
        var duzen = new Dictionary<AnalizSekmesi, List<AnalizSutunu>>();
        try
        {
            if (!File.Exists(yol)) return duzen;
            foreach (string satir in File.ReadAllLines(yol))
            {
                string[] alanlar = satir.Split('|');
                if (alanlar.Length is < 3 or > 4 || !Enum.TryParse(alanlar[0], out AnalizSekmesi sekme) || alanlar[1].Length == 0) continue;
                if (!duzen.TryGetValue(sekme, out List<AnalizSutunu>? liste)) duzen[sekme] = liste = new();
                double? genislik = alanlar.Length == 4 &&
                                   double.TryParse(alanlar[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double g) && g > 0
                    ? g
                    : null;
                liste.Add(new AnalizSutunu(EskiAdlar.GetValueOrDefault(alanlar[1], alanlar[1]), alanlar[2] == "1", genislik));
            }
        }
        catch (Exception istisna) when (istisna is IOException or UnauthorizedAccessException)
        {
            duzen.Clear();
        }
        return duzen;
    }

    /// <summary>Writes every tab's layout; null on success, otherwise the reason.</summary>
    public static string? Yaz(string yol, IReadOnlyDictionary<AnalizSekmesi, List<AnalizSutunu>> duzen)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
            File.WriteAllLines(yol, duzen.OrderBy(x => x.Key)
                .SelectMany(x => x.Value.Select(s => x.Key + "|" + s.Baslik.Replace('|', '/') + "|" + (s.Gorunur ? "1" : "0") +
                                                     (s.Genislik is double g ? "|" + g.ToString("0.#", CultureInfo.InvariantCulture) : ""))));
            return null;
        }
        catch (Exception istisna) when (istisna is IOException or UnauthorizedAccessException)
        {
            return istisna.Message;
        }
    }

    /// <summary>
    /// A cell's value for Excel: the row's property `ozellik` (the column's
    /// sort member). On the mixed tabs the properties are IAnalizSatiri's,
    /// some implemented explicitly, so the interface is asked first.
    /// </summary>
    public static object? Deger(object satir, string? ozellik)
    {
        if (string.IsNullOrEmpty(ozellik)) return null;
        PropertyInfo? bilgi = satir is IAnalizSatiri ? typeof(IAnalizSatiri).GetProperty(ozellik) : null;
        bilgi ??= satir.GetType().GetProperty(ozellik, BindingFlags.Instance | BindingFlags.Public);
        return bilgi?.GetValue(satir);
    }
}
