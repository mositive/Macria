using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Macria;

public enum ProductionQuantitySource { Catia, FileName, Manual, Missing, Conflict }

public sealed class ProductionPackageItem : INotifyPropertyChanged
{
    public required string SourcePath { get; init; }
    public required string PartCode { get; init; }
    public int? CatiaQuantity { get; init; }
    public int? FileNameQuantity { get; init; }
    public int? ManualQuantity { get; set; }
    public int Multiplier { get; set; }
    public ProductionQuantitySource QuantitySource { get; private set; }
    public int? BaseQuantity { get; private set; }
    public int? FinalQuantity { get; private set; }
    public string TargetFileName { get; private set; } = "";
    public string Status { get; private set; } = "Bekliyor";
    public string Explanation { get; private set; } = "";
    public bool ManualEntryAllowed => CatiaQuantity is not > 0 && FileNameQuantity is not > 0;
    /// <summary>The source's file name (the window shows it; the full path is in its tooltip).</summary>
    public string SourceFileName => Path.GetFileName(SourcePath);
    /// <summary>"Durum / Açıklama": the status, and why when it is not ready.</summary>
    public string StatusDisplay => Explanation.Length == 0 ? Status : Status + " — " + Explanation;
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
        BaseQuantity = null; FinalQuantity = null; TargetFileName = "";
        if (!ProductionPackageService.IsSupported(SourcePath)) { Status = "Geçersiz"; Explanation = "Desteklenmeyen dosya uzantısı."; return; }
        if (Multiplier <= 0) { Status = "Geçersiz"; Explanation = "Üretim çarpanı pozitif tam sayı olmalıdır."; return; }
        if (CatiaQuantity is > 0 && FileNameQuantity is > 0 && CatiaQuantity != FileNameQuantity) { QuantitySource = ProductionQuantitySource.Conflict; Status = "Adet çelişkisi"; Explanation = "CATIA adedi ile dosya adındaki adet farklı."; return; }
        if (CatiaQuantity is > 0) { BaseQuantity = CatiaQuantity; QuantitySource = ProductionQuantitySource.Catia; }
        else if (FileNameQuantity is > 0) { BaseQuantity = FileNameQuantity; QuantitySource = ProductionQuantitySource.FileName; }
        else if (ManualQuantity is > 0) { BaseQuantity = ManualQuantity; QuantitySource = ProductionQuantitySource.Manual; }
        else { QuantitySource = ProductionQuantitySource.Missing; Status = "Üretime hazır değil"; Explanation = "Temel adet kaynağı bulunamadı."; return; }
        try { FinalQuantity = checked(BaseQuantity.Value * Multiplier); }
        catch (OverflowException) { Status = "Geçersiz"; Explanation = "Üretim adedi taşması."; return; }
        TargetFileName = $"{PartCode}_{FinalQuantity} Adet{Path.GetExtension(SourcePath)}";
        Status = "Hazır"; Explanation = "";
    }
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class ProductionPackageService
{
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
