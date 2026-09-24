using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Macria;

public enum DxfDwgMatchState { Matched, Unmatched, Ambiguous, Duplicate, FileError }

public sealed class DxfDwgFileItem
{
    public required string FullPath { get; init; }
    public string FileName => Path.GetFileName(FullPath);
    public string FileType => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    public string Folder => Path.GetDirectoryName(FullPath) ?? "";
    public long SizeBytes { get; init; }
    public DateTime LastWriteTime { get; init; }
    public bool IsDuplicate { get; init; }
    public string MatchKey { get; init; } = "";
    public DxfDwgMatchState MatchState { get; private set; } = DxfDwgMatchState.Unmatched;
    public int? CatiaQuantity { get; private set; }
    public string CatiaReferenceTitle { get; private set; } = "";
    public string Explanation { get; private set; } = "";
    public string StatusDisplay => MatchState switch
    {
        DxfDwgMatchState.Matched => "Eşleşti",
        DxfDwgMatchState.Ambiguous => "Eşleme belirsiz",
        DxfDwgMatchState.Duplicate => "Yinelenen dosya",
        DxfDwgMatchState.FileError => "Dosya hatası",
        _ => "Eşleşmedi"
    };

    public void Apply(CatiaScanSnapshot? snapshot, bool duplicate)
    {
        CatiaQuantity = null;
        CatiaReferenceTitle = "";

        if (duplicate)
        {
            MatchState = DxfDwgMatchState.Duplicate;
            Explanation = "Aynı normalize edilmiş dosya anahtarı birden fazla kez bulundu.";
            return;
        }

        if (snapshot == null)
        {
            MatchState = DxfDwgMatchState.Unmatched;
            Explanation = "CATIA taraması yok.";
            return;
        }

        DxfDwgMatchResult result = DxfDwgFileInventory.Match(snapshot, FullPath);
        if (result.Items.Count != 1)
        {
            MatchState = result.Items.Count == 0 ? DxfDwgMatchState.Unmatched : DxfDwgMatchState.Ambiguous;
            Explanation = result.Items.Count == 0
                ? "CATIA kaydı eşleşmedi."
                : "Birden fazla CATIA kaydı eşleşti.";
            return;
        }

        CatiaScanSnapshotItem match = result.Items[0];
        MatchState = DxfDwgMatchState.Matched;
        CatiaQuantity = match.Quantity;
        CatiaReferenceTitle = match.ReferenceTitle;
        Explanation = result.UsedProductionSuffix
            ? "Dosya adı üretim ekleri çıkarılarak CATIA kaydıyla eşleşti."
            : match.SheetMetalConfirmed
                ? "Sac parça — aktif 3B tarama verisinden doğrulandı."
                : "CATIA kaydı eşleşti; sac parça doğrulanmadı.";
    }
}

public sealed record DxfDwgMatchResult(IReadOnlyList<CatiaScanSnapshotItem> Items, bool UsedProductionSuffix);

public static class DxfDwgFileInventory
{
    private static readonly Regex ProductionSuffix = new(
        @"(?:[ _-]+(?:\d+(?:[.,]\d+)?[ _-]*mm|\d+[ _-]*adet))+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Normalize(string path) =>
        Path.GetFileNameWithoutExtension(path).Trim().Normalize(NormalizationForm.FormC);

    public static bool DefaultFilterVisible(DxfDwgMatchState state) => true;

    public static string MatchKey(string path)
    {
        string normalized = Normalize(path);
        string stripped = ProductionSuffix.Replace(normalized, "").TrimEnd(' ', '_', '-');
        return string.IsNullOrEmpty(stripped) ? normalized : stripped;
    }

    public static DxfDwgMatchResult Match(CatiaScanSnapshot snapshot, string path)
    {
        IReadOnlyList<CatiaScanSnapshotItem> exact = CatiaStepMatcher.Match(snapshot, path);
        if (exact.Count != 0)
            return new DxfDwgMatchResult(exact, false);

        string original = Normalize(path);
        string key = MatchKey(path);
        if (string.Equals(original, key, StringComparison.OrdinalIgnoreCase))
            return new DxfDwgMatchResult(Array.Empty<CatiaScanSnapshotItem>(), false);

        IReadOnlyList<CatiaScanSnapshotItem> suffixMatches = snapshot.Items
            .Where(item => string.Equals(CatiaStepMatcher.NormalizeFileIdentity(item.ReferenceTitle), key,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new DxfDwgMatchResult(suffixMatches, true);
    }

    public static IReadOnlyList<DxfDwgFileItem> Scan(string folder, bool recursive)
    {
        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] paths = Directory.EnumerateFiles(folder, "*.*", option)
            .Where(path => Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        HashSet<string> duplicates = paths.GroupBy(MatchKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return paths.Select(path =>
        {
            var file = new FileInfo(path);
            string key = MatchKey(path);
            bool duplicate = duplicates.Contains(key);
            var item = new DxfDwgFileItem
            {
                FullPath = path,
                SizeBytes = file.Length,
                LastWriteTime = file.LastWriteTime,
                IsDuplicate = duplicate,
                MatchKey = key
            };
            item.Apply(null, duplicate);
            return item;
        }).ToArray();
    }
}
