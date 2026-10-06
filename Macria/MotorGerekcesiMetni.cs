using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Macria;

/// <summary>
/// The engine's reasons as the tables, Excel and the console show them: the
/// result first, then a short reason ("Sac değil (çubuk/mil): kalınlık 30 mm
/// > en dar ölçü 11,9 mm"). No face ids, no English, no inner steps; the
/// engine's own text stays in "Teknik ayrıntı" of Seçili Parça. The engine
/// decision is not touched: only its wording.
/// </summary>
public static class MotorGerekcesiMetni
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Short text for a part row: the engine code (MotorSinifKodu) and its reasons.</summary>
    public static string Kisa(string kod, IReadOnlyList<string> gerekceler)
    {
        if (gerekceler.Count == 0) return "—";
        string ilk = gerekceler[0];
        // Trials: the trial's own line first, then what it found or why not.
        foreach (string deneme in new[] { "Profil olarak denendi", "Sac olarak denendi" })
        {
            if (!ilk.StartsWith(deneme, StringComparison.Ordinal)) continue;
            string tur = deneme.StartsWith("Profil", StringComparison.Ordinal) ? "profil" : "sac";
            Match tanimadi = Regex.Match(ilk, "^" + deneme + @": (?:profil|sac) tanınmadı \((.*)\)\.?$");
            if (tanimadi.Success)
            {
                string neden = DenemeNedeni(tanimadi.Groups[1].Value);
                string otomatik = gerekceler.Count > 1 ? Kisa(kod, gerekceler.Skip(1).ToList()) : "";
                // The trial's reason is often the automatic one: said once.
                return otomatik.Length == 0 ? deneme + ", tanınmadı: " + neden
                    : otomatik.Contains(neden, StringComparison.Ordinal) ? deneme + ", tanınmadı → " + otomatik
                    : deneme + ", tanınmadı (" + neden + ") → " + otomatik;
            }
            string gevsetilen = "";
            string sonuc = ilk;
            Match gevsek = Regex.Match(ilk, "^" + deneme + @" \(gevşetilmiş: (.*?)\): (.*)$");
            if (gevsek.Success)
            {
                gevsetilen = Gevsetilenler(gevsek.Groups[1].Value);
                sonuc = gevsek.Groups[2].Value;
            }
            else if (ilk.StartsWith(deneme + ": ", StringComparison.Ordinal))
                sonuc = ilk.Substring(deneme.Length + 2);
            return deneme + " → " + Otomatik(kod, new[] { sonuc }.Concat(gerekceler.Skip(1)).ToList()) +
                   (gevsetilen.Length > 0 ? " (gevşetilen: " + gevsetilen + ")" : "");
        }
        return Otomatik(kod, gerekceler);
    }

    /// <summary>The engine's own text for "Teknik ayrıntı".</summary>
    public static string Teknik(IReadOnlyList<string> gerekceler) => gerekceler.Count == 0 ? "—" : string.Join(" ", gerekceler);

    private static string Otomatik(string kod, IReadOnlyList<string> gerekceler)
    {
        string hepsi = string.Join(" ", gerekceler);
        switch (kod)
        {
            case MotorSinifKodu.Sheet:
            {
                Match sac = Regex.Match(hepsi, @"Sac: t=([0-9.,]+) mm, ([0-9]+) büküm");
                if (sac.Success)
                    return "Sac: t = " + Sayi(sac.Groups[1].Value) + " mm, " + sac.Groups[2].Value + " büküm" +
                           (hepsi.Contains("İşleme var", StringComparison.Ordinal) ? "; işleme var" : "");
                Match acinimsiz = Regex.Match(hepsi, @"Sac, açınım yok: (.*?)\s*t=([0-9.,]+) mm");
                if (acinimsiz.Success)
                    return "Sac, açınım yok: " + AcinimNedeni(acinimsiz.Groups[1].Value) + "; t = " + Sayi(acinimsiz.Groups[2].Value) + " mm";
                return "Sac";
            }
            case MotorSinifKodu.FlatPatternFailed:
                return "Sac, açınım yok: " + AcinimNedeni(hepsi);
            case MotorSinifKodu.ThickerThanOutline:
            {
                Match m = Regex.Match(hepsi, @"kalınlık \(([0-9.,]+) mm\) açınımın en dar ölçüsünden \(([0-9.,]+) mm\)");
                return m.Success
                    ? "Sac değil (çubuk/mil): kalınlık " + Sayi(m.Groups[1].Value) + " mm > en dar ölçü " + Sayi(m.Groups[2].Value) + " mm"
                    : "Sac değil (çubuk/mil): kalınlık açınımın en dar ölçüsünden büyük";
            }
            case MotorSinifKodu.ThickerThanMaterial:
            {
                Match m = Regex.Match(hepsi, @"kalınlık \(([0-9.,]+) mm\) parçanın gerçek et genişliğinden \(([0-9.,]+) mm\)");
                return m.Success
                    ? "Sac değil (halka/somun/mil): kalınlık " + Sayi(m.Groups[1].Value) + " mm > et genişliği " + Sayi(m.Groups[2].Value) + " mm"
                    : "Sac değil (halka/somun/mil): kalınlık et genişliğinden büyük";
            }
            case MotorSinifKodu.SolidBar:
                return "Profil değil: " + DoluKesit(hepsi);
            case MotorSinifKodu.HollowProfile:
                return "Profil: " + ProfilTuru(hepsi);
            case MotorSinifKodu.ProcessedProfile:
                return "İşlenmiş profil: " + ProfilTuru(hepsi);
            case MotorSinifKodu.SheetProfileConflict:
                return "Sac mı profil mi belirsiz: iki tanıyıcı da tanıdı (" + ProfilTuru(hepsi) + ")";
            case MotorSinifKodu.UnsupportedFaces:
                return "Tanınamadı: " + YuzTipleri(hepsi);
            case MotorSinifKodu.SheetAnalysisIncomplete:
                return hepsi.Contains("Kalınlık çiftleri arasında düzlem yüz yok", StringComparison.Ordinal) ? "Sac değil: paralel düz yüz çifti yok"
                    : hepsi.Contains("kalınlık adayı", StringComparison.Ordinal) ? "Tanınamadı: birden fazla kalınlık adayı var"
                    : hepsi.Contains("süresi", StringComparison.Ordinal) ? "Tanınamadı: sac analizi yarıda kaldı"
                    : "Tanınamadı: sac analizi tamamlanamadı";
            case MotorSinifKodu.NotRecognized:
                return SacNedeni(hepsi);
            case MotorSinifKodu.InvalidGeometry:
                return "Tanınamadı: geçersiz geometri";
            case MotorSinifKodu.MultiSolid:
                return "Kontrol: çok gövdeli parça";
            case MotorSinifKodu.NoSolid:
                return "Tanınamadı: parçada katı gövde yok";
            case MotorSinifKodu.TimedOut:
                return "Tanınamadı: analiz süre sınırını aştı";
            default:
                return Temizle(gerekceler[0]);
        }
    }

    // Why the sheet recognizer did not take a part, from its rejection text.
    private static string SacNedeni(string metin) =>
        metin.Contains("Sabit ofsetli karşılıklı yüz çifti bulunamadı", StringComparison.Ordinal) ? "Tanınamadı: karşılıklı paralel yüz yok"
        : metin.Contains("Kabuklar arasında kalmayan yüzler", StringComparison.Ordinal) ? "Tanınamadı: sac kalınlığı dışına taşan yüzler var"
        : metin.Contains("Kabuk kendi üzerine kapanıyor", StringComparison.Ordinal) ? "Sac değil: kapalı kesit (boru)"
        : metin.Contains("kabukları ayrışmadı", StringComparison.Ordinal) ? "Tanınamadı: sac yüzleri ayrışmadı"
        : metin.Contains("Kalınlık çiftleri arasında düzlem yüz yok", StringComparison.Ordinal) ? "Sac değil: paralel düz yüz çifti yok"
        : "Tanınamadı";

    private static string AcinimNedeni(string metin) =>
        metin.Contains("iki flanş arasında değil", StringComparison.Ordinal) ? "uçta biten büküm"
        : metin.Contains("kapalı kesit", StringComparison.Ordinal) ? "kapalı kesit (boru)"
        : metin.Contains("döngü", StringComparison.Ordinal) ? "bükümler kapalı döngü oluşturuyor"
        : metin.Contains("bağlantısız", StringComparison.Ordinal) ? "bükümle bağlanmayan flanş var"
        : "açınım çıkarılamadı";

    // A trial's "not recognized" reason, without the engine's English detail.
    private static string DenemeNedeni(string metin)
    {
        int iki = metin.IndexOf(':');
        string kisa = iki > 0 && metin.Take(iki).All(c => c < 128 || char.IsLetter(c)) ? metin[..iki] : metin;
        if (kisa.Contains("Kabuklar arasında kalmayan yüzler", StringComparison.Ordinal) ||
            metin.StartsWith("Kabuklar arasında kalmayan yüzler", StringComparison.Ordinal))
        {
            Match pay = Regex.Match(metin, @"alanın %([0-9.,]+)'i");
            return metin.Contains("torus ya da B-spline", StringComparison.Ordinal) ? "kabuk dışı yüzlerde torus ya da B-spline var"
                : pay.Success ? "kabuk dışı yüzler alanın %" + Sayi(pay.Groups[1].Value) + "'i, sınır %10"
                : "sac kalınlığı dışına taşan yüzler var";
        }
        return Temizle(SacNedeniMi(metin) ?? kisa);
    }

    private static string? SacNedeniMi(string metin)
    {
        string neden = SacNedeni(metin);
        return neden == "Tanınamadı" ? null : neden.Replace("Tanınamadı: ", "").Replace("Sac değil: ", "");
    }

    private static string Gevsetilenler(string metin) => string.Join(", ", metin.Split("; ").Select(x =>
        x.StartsWith("levha değil kuralı", StringComparison.Ordinal) ? LevhaDegil(x)
        : x.StartsWith("kesiti tutarlı içi boş tek eksen", StringComparison.Ordinal) ? "tek eksen"
        : x.StartsWith("boy kesitten kısa", StringComparison.Ordinal) ? "kısa boy"
        : x.StartsWith("sac tanıyıcının sonucu", StringComparison.Ordinal) ? "sac sonucu sayılmadı"
        : x.StartsWith("profil tanıyıcının sonucu", StringComparison.Ordinal) ? "profil sonucu sayılmadı"
        : x.StartsWith("kapalı kesit sac", StringComparison.Ordinal) ? "kapalı kesit"
        : x.StartsWith("artık", StringComparison.Ordinal) ? "kabuk dışı yüzler işleme"
        : Temizle(x)));

    // "levha değil kuralı uygulanmadı (kalınlık 30 mm > en dar ölçü 11,876 mm)" → "levha değil kuralı, kalınlık 30 mm > en dar ölçü 11,9 mm"
    private static string LevhaDegil(string metin)
    {
        Match m = Regex.Match(metin, @"kalınlık ([0-9.,]+) mm > (en dar ölçü|et genişliği) ([0-9.,]+) mm");
        return m.Success
            ? "levha değil kuralı, kalınlık " + Sayi(m.Groups[1].Value) + " mm > " + m.Groups[2].Value + " " + Sayi(m.Groups[3].Value) + " mm"
            : "levha değil kuralı";
    }

    private static string ProfilTuru(string metin) =>
        metin.Contains("SquareHollowSection", StringComparison.Ordinal) ? "kare kutu"
        : metin.Contains("RectangularHollowSection", StringComparison.Ordinal) ? "dikdörtgen kutu"
        : metin.Contains("CircularHollowSection", StringComparison.Ordinal) ? "yuvarlak boru"
        : "profil";

    private static string DoluKesit(string metin) =>
        metin.Contains("SolidCircularBar", StringComparison.Ordinal) ? "dolu yuvarlak çubuk (mil)"
        : metin.Contains("SolidSquareBar", StringComparison.Ordinal) ? "dolu kare çubuk"
        : metin.Contains("SolidRectangularBar", StringComparison.Ordinal) ? "dolu dikdörtgen çubuk"
        : "dolu kesit";

    private static string YuzTipleri(string metin)
    {
        Match tipler = Regex.Match(metin, @"yüz tipleri var \(([^)]*)\)");
        string[] adlar = tipler.Success ? tipler.Groups[1].Value.Split(", ") : Array.Empty<string>();
        var parcalar = new List<string>();
        if (adlar.Contains("Torus")) parcalar.Add("yuvarlatılmış (torus)");
        if (adlar.Contains("BSpline")) parcalar.Add("serbest eğri (B-spline)");
        if (adlar.Any(x => x is not "Torus" and not "BSpline")) parcalar.Add("desteklenmeyen");
        if (parcalar.Count == 0) return "desteklenmeyen yüzler var";
        string liste = parcalar.Count == 1 ? parcalar[0] : string.Join(", ", parcalar.Take(parcalar.Count - 1)) + " ve " + parcalar[^1];
        return liste + " yüzler var";
    }

    // "11.876" / "11,876" → "11,9"; integers stay whole.
    private static string Sayi(string metin) =>
        double.TryParse(metin.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double sayi)
            ? Math.Round(sayi, 1).ToString("0.#", Tr)
            : metin;

    // No face ids, no inner engine steps.
    private static string Temizle(string metin) =>
        Regex.Replace(Regex.Replace(metin, @":?\s*F[0-9]+(, F[0-9]+)*\.?", ""), @"\s+", " ").Trim().TrimEnd('.');
}
