using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Macria;

public enum ProductionQuantitySource { Catia, FileName, Manual, Missing, Conflict, Step }

/// <summary>
/// One profile part written by the engine (--step-yaz) into Profil-STEP\:
/// what came of it and the read-back check.
/// </summary>
public sealed record ProfilStepSonucu(string Durum, string Aciklama, bool Hizali, double? BoyMm, double? KutuBoyuMm, double? HacimFarkiYuzde);

public sealed class ProductionPackageItem : INotifyPropertyChanged
{
    public required string SourcePath { get; init; }
    public required string PartCode { get; init; }
    public int? CatiaQuantity { get; init; }
    public int? FileNameQuantity { get; init; }
    /// <summary>
    /// "Profilleri STEP olarak yaz": the row is one profile part of a STEP
    /// (its own file under Profil-STEP\, named ParçaNo_XAdet.stp), not a copy
    /// of the source file.
    /// </summary>
    public bool ProfilStep { get; init; }
    /// <summary>The part's localId in the STEP's analysis (--step-yaz).</summary>
    public int? PartLocalId { get; init; }
    /// <summary>The part's quantity in its STEP (assembly instances); used without a CATIA quantity.</summary>
    public int? StepQuantity { get; init; }
    /// <summary>The part's name in the STEP.</summary>
    public string PartName { get; init; } = "";
    /// <summary>Where the part number comes from ("productId", "parça adı (…)").</summary>
    public string PartCodeSource { get; init; } = "";
    /// <summary>The row came from "Profil olarak dene": the engine needs the trial to find its axis.</summary>
    public bool ProfileTrial { get; init; }
    public string SectionDisplay { get; init; } = "";
    public string LengthDisplay { get; init; } = "";
    /// <summary>"Parça kodu" tooltip: for a profile part, where the number comes from.</summary>
    public string PartCodeToolTip => ProfilStep && PartCodeSource.Length > 0 ? PartCode + " — kaynak: " + PartCodeSource : PartCode;
    /// <summary>The file name in the package after name clashes are resolved (_2, _3); empty until then.</summary>
    public string PackageFileName { get; private set; } = "";
    /// <summary>"Yeni dosya": the copy's name, or Profil-STEP\name for a profile part.</summary>
    public string NewFileDisplay => !ProfilStep ? TargetFileName
        : TargetFileName.Length == 0 ? "" : "Profil-STEP\\" + (PackageFileName.Length > 0 ? PackageFileName : TargetFileName);
    /// <summary>What the engine made of the part; null before Paketi Oluştur.</summary>
    public ProfilStepSonucu? StepResult { get; private set; }

    public void SetPackageFileName(string name)
    {
        PackageFileName = name;
        Raise(string.Empty);
    }

    public void SetStepResult(ProfilStepSonucu result)
    {
        StepResult = result;
        Raise(string.Empty);
    }
    public int? ManualQuantity { get; set; }
    public int Multiplier { get; set; }
    public ProductionQuantitySource QuantitySource { get; private set; }
    public int? BaseQuantity { get; private set; }
    public int? FinalQuantity { get; private set; }
    public string TargetFileName { get; private set; } = "";
    public string Status { get; private set; } = "Bekliyor";
    public string Explanation { get; private set; } = "";
    public bool ManualEntryAllowed => CatiaQuantity is not > 0 && FileNameQuantity is not > 0 && StepQuantity is not > 0;
    /// <summary>The source's file name (the window shows it; the full path is in its tooltip).</summary>
    public string SourceFileName => Path.GetFileName(SourcePath);
    /// <summary>"Durum / Açıklama": the status, and why when it is not ready.</summary>
    public string StatusDisplay => StepResult is { } adim
        ? adim.Durum + (adim.Aciklama.Length > 0 ? " — " + adim.Aciklama : "")
        : Explanation.Length == 0 ? Status : Status + " — " + Explanation;
    public int? DisplayedBaseQuantity
    {
        get => BaseQuantity;
        set
        {
            if (!ManualEntryAllowed || value is not > 0) return;
            ManualQuantity = value;
            Validate();
        }
    }
    public string QuantitySourceDisplay => QuantitySource switch
    {
        ProductionQuantitySource.Catia => "CATIA taraması",
        ProductionQuantitySource.FileName => "Dosya adı",
        ProductionQuantitySource.Manual => "Manuel giriş",
        ProductionQuantitySource.Conflict => "Adet çelişkisi",
        ProductionQuantitySource.Step => "STEP montajı",
        _ => "Eksik"
    };
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Validate()
    {
        try { ValidateCore(); }
        // Every displayed value may change: the rows show the new state at once.
        finally { Raise(string.Empty); }
    }

    private void ValidateCore()
    {
        BaseQuantity = null; FinalQuantity = null; TargetFileName = ""; PackageFileName = "";
        if (!ProductionPackageService.IsSupported(SourcePath)) { Status = "Geçersiz"; Explanation = "Desteklenmeyen dosya uzantısı."; return; }
        if (Multiplier <= 0) { Status = "Geçersiz"; Explanation = "Üretim çarpanı pozitif tam sayı olmalıdır."; return; }
        if (CatiaQuantity is > 0 && FileNameQuantity is > 0 && CatiaQuantity != FileNameQuantity) { QuantitySource = ProductionQuantitySource.Conflict; Status = "Adet çelişkisi"; Explanation = "CATIA adedi ile dosya adındaki adet farklı."; return; }
        if (CatiaQuantity is > 0) { BaseQuantity = CatiaQuantity; QuantitySource = ProductionQuantitySource.Catia; }
        else if (StepQuantity is > 0) { BaseQuantity = StepQuantity; QuantitySource = ProductionQuantitySource.Step; }
        else if (FileNameQuantity is > 0) { BaseQuantity = FileNameQuantity; QuantitySource = ProductionQuantitySource.FileName; }
        else if (ManualQuantity is > 0) { BaseQuantity = ManualQuantity; QuantitySource = ProductionQuantitySource.Manual; }
        else { QuantitySource = ProductionQuantitySource.Missing; Status = "Üretime hazır değil"; Explanation = "Temel adet kaynağı bulunamadı."; return; }
        try { FinalQuantity = checked(BaseQuantity.Value * Multiplier); }
        catch (OverflowException) { Status = "Geçersiz"; Explanation = "Üretim adedi taşması."; return; }
        TargetFileName = ProfilStep
            ? ProfilStepAdi.DosyaAdi(PartCode, FinalQuantity.Value)
            : $"{PartCode}_{FinalQuantity} Adet{Path.GetExtension(SourcePath)}";
        Status = "Hazır"; Explanation = "";
    }
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// "Profilleri STEP olarak yaz": file names under Profil-STEP\. The part
/// number is the STEP productId, or the part name when the productId is a
/// CAD default ("3D Shape00000338A", "Part1"); a name Windows refuses is
/// cleaned. Name clashes get _2, _3; parts of different STEPs with the same
/// number keep their own quantities (no summing).
/// </summary>
public static class ProfilStepAdi
{
    public const string Klasor = "Profil-STEP";

    // CAD default names: they say nothing about the part.
    private static readonly Regex Anlamsiz = new(
        @"^(3D\s*Shape|Shape|Part|PartBody|Body|Solid|Product|Physical\s*Product|Component|Assembly|Parça|Unnamed|Untitled)[\s_-]*\d*[A-Z]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool AnlamsizMi(string? ad) => string.IsNullOrWhiteSpace(ad) || Anlamsiz.IsMatch(ad.Trim());

    /// <summary>
    /// The part number and where it came from (the package report says it):
    /// the productId; the part name when the productId is empty or a CAD
    /// default; for the only part of a STEP whose productId and name are both
    /// defaults, the STEP file's identity (`tekParcaDosyaKimligi`).
    /// </summary>
    public static (string No, string Kaynak) ParcaNo(string? productId, string? parcaAdi, string? tekParcaDosyaKimligi = null)
    {
        string id = (productId ?? "").Trim(), ad = (parcaAdi ?? "").Trim();
        if (!AnlamsizMi(id)) return (Temizle(id), "productId");
        if (!AnlamsizMi(ad))
            return (Temizle(ad), id.Length == 0 ? "parça adı (productId yok)" : "parça adı (productId anlamsız: " + id + ")");
        if (!AnlamsizMi(tekParcaDosyaKimligi))
            return (Temizle(tekParcaDosyaKimligi!.Trim()), "STEP dosya adı (tek parça; productId ve ad anlamsız: " + (id.Length > 0 ? id : ad) + ")");
        string yine = id.Length > 0 ? id : ad;
        return (Temizle(yine), id.Length > 0 ? "productId (ad da anlamsız)" : "parça adı (productId yok, ad anlamsız)");
    }

    private static readonly HashSet<string> Ayrilmis = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>A file-name part Windows accepts: forbidden characters become "_", no trailing dot or space, no reserved name.</summary>
    public static string Temizle(string ad)
    {
        var gecersiz = new HashSet<char>(Path.GetInvalidFileNameChars());
        string temiz = new string(ad.Select(c => gecersiz.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (temiz.Length == 0) temiz = "parca";
        if (Ayrilmis.Contains(temiz)) temiz += "_";
        return temiz;
    }

    public static string DosyaAdi(string parcaNo, int adet) => parcaNo + "_" + adet + "Adet.stp";

    /// <summary>Unique names in the given order: a clash gets _2, _3 before the extension (case ignored).</summary>
    public static List<string> Benzersiz(IEnumerable<string> adlar)
    {
        var kullanilan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sonuc = new List<string>();
        foreach (string ad in adlar)
        {
            string aday = ad;
            for (int sira = 2; !kullanilan.Add(aday); ++sira)
                aday = Path.GetFileNameWithoutExtension(ad) + "_" + sira + Path.GetExtension(ad);
            sonuc.Add(aday);
        }
        return sonuc;
    }
}

public static class ProductionPackageService
{
    /// <summary>Profile rows: package names without clashes (_2, _3), in row order.</summary>
    public static void ResolveProfileNames(IEnumerable<ProductionPackageItem> rows)
    {
        List<ProductionPackageItem> hazir = rows.Where(item => item.ProfilStep && item.Status == "Hazır").ToList();
        List<string> adlar = ProfilStepAdi.Benzersiz(hazir.Select(item => item.TargetFileName));
        for (int i = 0; i < hazir.Count; ++i) hazir[i].SetPackageFileName(adlar[i]);
    }

    /// <summary>
    /// What the engine reported for a profile part: written (the file is in
    /// Profil-STEP\), not written (the read-back failed), skipped (invalid
    /// geometry, not tried) or no report (the engine run failed).
    /// </summary>
    public static ProfilStepSonucu StepResult(GeometryLabPartStepTransport? kayit, string? motorHatasi)
    {
        if (kayit is null)
            return new ProfilStepSonucu("Yazılamadı", "motor sonucu yok" + (string.IsNullOrEmpty(motorHatasi) ? "" : ": " + motorHatasi), false, null, null, null);
        double? fark = kayit.SourceVolumeMm3 is > 0 && kayit.WrittenVolumeMm3 is double yazilan
            ? Math.Abs(yazilan - kayit.SourceVolumeMm3.Value) / kayit.SourceVolumeMm3.Value * 100.0 : null;
        string nedenler = string.Join("; ", kayit.Reasons);
        return kayit.Status switch
        {
            "Written" => new ProfilStepSonucu("Yazıldı", nedenler, kayit.Aligned, kayit.LengthMm, kayit.BoxLengthMm, fark),
            "Skipped" => new ProfilStepSonucu("Atlandı", nedenler.Length > 0 ? nedenler : "denenmedi", false, null, null, null),
            _ => new ProfilStepSonucu("Yazılamadı", nedenler.Length > 0 ? nedenler : "doğrulama başarısız", kayit.Aligned, kayit.LengthMm, kayit.BoxLengthMm, fark)
        };
    }

    private static readonly Regex QuantitySuffix = new(@"(?:[ _-]+)(\d+)[ _-]*adet$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool IsSupported(string path) => new[] { ".dxf", ".dwg", ".stp", ".step" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static string PartCode(string path) => Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase) ? DxfDwgFileInventory.MatchKey(path) : CatiaStepMatcher.NormalizeFileIdentity(path);
    public static int? FileNameQuantity(string path)
    {
        Match match = QuantitySuffix.Match(Path.GetFileNameWithoutExtension(path));
        return match.Success && int.TryParse(match.Groups[1].Value, out int quantity) && quantity > 0 ? quantity : null;
    }
    /// <summary>
    /// Why "Paketi Oluştur" is off, or what stays out of the package; null
    /// when every row is ready. `common` is ValidateCommonFolder's result.
    /// The button is on exactly when the folder is common and a row is ready.
    /// </summary>
    public static (bool CanCreate, string? Message) PackageState(IReadOnlyCollection<ProductionPackageItem> rows, string? common)
    {
        if (rows.Count == 0) return (false, "Paketi Oluştur pasif: pakete alınacak satır yok.");
        int ready = rows.Count(item => item.Status == "Hazır");
        bool canCreate = common != null && ready > 0;
        var reasons = new List<string>();
        if (common == null)
            reasons.Add("Kaynak dosyalar aynı klasörde olmalı: " +
                        rows.Select(item => Path.GetDirectoryName(item.SourcePath) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() + " farklı klasör");
        foreach (var group in rows.Where(item => item.Status != "Hazır").GroupBy(Reason).OrderBy(g => g.Key, StringComparer.Ordinal))
            reasons.Add(group.Key + ": " + group.Count() + " satır");
        if (reasons.Count == 0) return (canCreate, null);
        return (canCreate, canCreate
            ? "Pakete girmeyecek (" + (rows.Count - ready) + " satır) — " + string.Join("; ", reasons)
            : "Paketi Oluştur pasif — " + string.Join("; ", reasons));
    }

    private static string Reason(ProductionPackageItem item) => item.QuantitySource switch
    {
        _ when item.Explanation.StartsWith("Desteklenmeyen", StringComparison.Ordinal) => "Desteklenmeyen dosya",
        _ when item.Explanation.StartsWith("Üretim çarpanı", StringComparison.Ordinal) => "Üretim çarpanı pozitif tam sayı olmalı",
        _ when item.Explanation.StartsWith("Üretim adedi taşması", StringComparison.Ordinal) => "Üretim adedi taşması",
        ProductionQuantitySource.Conflict => "Adet çelişkisi (CATIA ≠ dosya adı)",
        ProductionQuantitySource.Missing => "Temel adet eksik",
        _ => item.Status
    };

    public static string? ValidateCommonFolder(IEnumerable<ProductionPackageItem> items)
    {
        string[] folders = items.Select(item => Path.GetDirectoryName(item.SourcePath) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return folders.Length == 1 ? folders[0] : null;
    }
    public static string CreateFolder(string sourceFolder, int multiplier, DateTime now)
    {
        string baseName = $"{multiplier} Kat Üretim - {now:yyyy-MM-dd HH-mm}";
        string target = Path.Combine(sourceFolder, baseName); int sequence = 2;
        while (Directory.Exists(target)) target = Path.Combine(sourceFolder, $"{baseName} ({sequence++})");
        Directory.CreateDirectory(target); return target;
    }
    public static void CopyReady(ProductionPackageItem item, string folder)
    {
        if (item.Status != "Hazır") return;
        string target = Path.Combine(folder, item.TargetFileName);
        if (File.Exists(target)) throw new IOException("Hedef adı çakışıyor.");
        File.Copy(item.SourcePath, target, false);
    }
}
