using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Macria;

/// <summary>One column of a result tab: its header and whether it is shown.</summary>
public sealed record AnalizSutunu(string Baslik, bool Gorunur);

/// <summary>
/// "Sütunları Düzenle" of the STEP / STP Analizi tabs: per tab the visible
/// columns and their order, kept in %AppData%\Macria\analiz-sutunlari.txt
/// (one line per column: tab|header|1 or 0). Excel writes the visible columns
/// in this order. No WPF here; MainWindow.AnalizSutunlari applies it to the grids.
/// </summary>
public static class AnalizSutunDuzeni
{
    public static string VarsayilanYol => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macria", "analiz-sutunlari.txt");

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
        ["Motor kalınlığı"] = "Tespit edilen kalınlık"
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
                if (alanlar.Length != 3 || !Enum.TryParse(alanlar[0], out AnalizSekmesi sekme) || alanlar[1].Length == 0) continue;
                if (!duzen.TryGetValue(sekme, out List<AnalizSutunu>? liste)) duzen[sekme] = liste = new();
                liste.Add(new AnalizSutunu(EskiAdlar.GetValueOrDefault(alanlar[1], alanlar[1]), alanlar[2] == "1"));
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
                .SelectMany(x => x.Value.Select(s => x.Key + "|" + s.Baslik.Replace('|', '/') + "|" + (s.Gorunur ? "1" : "0"))));
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
