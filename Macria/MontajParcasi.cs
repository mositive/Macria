using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Macria;

// Where a part of an assembly STEP is listed in the Dosya Analiz Merkezi.
public enum MontajParcaKategorisi
{
    Sac,            // Saclar: approved (CATIA sheet-metal feature or user)
    OnayGerekli,    // Saclar: engine found a geometric sheet, not confirmed yet
    KontrolGerekli, // Kontrol gerekli tab
    Diger,          // Kontrol gerekli tab, class "Diğer"
    Profil          // Profiller tab (existing profile rows)
}

/// <summary>
/// Session-only row for one part of an assembly STEP (GeometryLab schema 1.2).
/// The engine class is pure geometry; the CATIA sheet-metal feature and the
/// user's decision settle what Macria shows. No CATIA COM references.
/// </summary>
public sealed class MontajParcaSatiri : INotifyPropertyChanged
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public required string SourceStepPath { get; init; }
    public required string PartName { get; init; }
    public int Quantity { get; init; }
    public string EngineClass { get; init; } = "";
    public IReadOnlyList<string> EngineReasons { get; init; } = Array.Empty<string>();
    public bool SheetCandidate { get; init; }
    public string? ProfileCandidate { get; init; }
    public double? ThicknessMm { get; init; }
    public int BendCount { get; init; }
    public string HoleSummary { get; init; } = "—";
    /// <summary>Engine DXF (part-&lt;id&gt;.dxf in the analysis' DXF folder), or null.</summary>
    public string? DxfSourcePath { get; init; }
    /// <summary>Same pattern without bend information (part-&lt;id&gt;-kesim.dxf), or null.</summary>
    public string? DxfCutOnlySourcePath { get; init; }

    /// <summary>The engine DXF to use: with bend information or KESIM only.</summary>
    public string? DxfFor(bool bendInfo) => bendInfo ? DxfSourcePath : DxfCutOnlySourcePath;

    public string SourceFileName => System.IO.Path.GetFileName(SourceStepPath);
    public string ThicknessDisplay => ThicknessMm is double t ? FormatNumber(t) + " mm" : "—";
    public string BendCountDisplay => ThicknessMm is null ? "—" : BendCount.ToString(CultureInfo.InvariantCulture);
    public string EngineReasonDisplay => EngineReasons.Count == 0 ? "" : string.Join(" ", EngineReasons);

    private double _laserMaximumMm = 20;
    public string GroupDisplay => ThicknessMm is double t
        ? (t <= _laserMaximumMm ? "Lazer" : "Şalama/Kütük")
        : "—";

    /// <summary>null: no CATIA comparison or no single match.</summary>
    public bool? CatiaSheetMetalFeature { get; private set; }
    public string CatiaMatchDisplay { get; private set; } = "CATIA taraması yok";
    public string CatiaQuantityDisplay { get; private set; } = "—";
    public string CatiaReferenceTitle { get; private set; } = "";

    public MontajParcaKategorisi AutomaticCategory { get; private set; } = MontajParcaKategorisi.KontrolGerekli;
    public MontajParcaKategorisi EffectiveCategory { get; private set; } = MontajParcaKategorisi.KontrolGerekli;
    public GeometryLabDecisionSource DecisionSource { get; private set; } = GeometryLabDecisionSource.Automatic;
    public bool HasUserDecision => DecisionSource == GeometryLabDecisionSource.User;
    public bool CanApproveAsSheet => DxfSourcePath != null && ThicknessMm != null;
    public bool IsInSheetTab => EffectiveCategory is MontajParcaKategorisi.Sac or MontajParcaKategorisi.OnayGerekli;
    public bool IsInReviewTab => EffectiveCategory is MontajParcaKategorisi.KontrolGerekli or MontajParcaKategorisi.Diger;

    public string StatusDisplay => EffectiveCategory switch
    {
        MontajParcaKategorisi.Sac => "Sac",
        MontajParcaKategorisi.OnayGerekli => "Geometrik sac, onay gerekli",
        MontajParcaKategorisi.Diger => "Diğer",
        MontajParcaKategorisi.Profil => "Profil",
        _ => "Kontrol gerekli"
    };

    public string DecisionDisplay => DecisionSource switch
    {
        GeometryLabDecisionSource.User => "Kullanıcı",
        GeometryLabDecisionSource.ThreeDScan => "CATIA sac unsuru",
        _ => "Otomatik"
    };

    public string ExplanationDisplay
    {
        get
        {
            if (HasUserDecision)
                return EffectiveCategory == MontajParcaKategorisi.Sac
                    ? "Kullanıcı tarafından sac olarak onaylandı."
                    : "Kullanıcı kararıyla kontrol gerekliye alındı.";
            return EffectiveCategory switch
            {
                MontajParcaKategorisi.Sac when EngineClass == "ReviewRequired" =>
                    "Sac/profil çakışması CATIA sac unsuruyla çözüldü.",
                MontajParcaKategorisi.Sac => "Motor sac buldu; CATIA sac unsuru doğruluyor.",
                MontajParcaKategorisi.OnayGerekli when CatiaSheetMetalFeature == false =>
                    "CATIA'da sac unsuru yok; motor geometrik sac buldu.",
                MontajParcaKategorisi.OnayGerekli => "CATIA ile doğrulanmadı; motor geometrik sac buldu.",
                _ => EngineReasonDisplay
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Row for one part; engine evidence comes from the same analysis.</summary>
    public static MontajParcaSatiri Olustur(string stepPath, GeometryLabAnalysisTransport analysis,
        GeometryLabPartTransport part, string? dxfSourcePath, double laserMaximumMm, string? dxfCutOnlySourcePath = null)
    {
        int? solidId = part.SolidIds.Count == 1 ? part.SolidIds[0].LocalId : null;
        GeometryLabSheetMetalTransport? sheet = solidId is null
            ? null
            : analysis.SheetMetalAnalyses.FirstOrDefault(x => x.SolidId?.LocalId == solidId);
        bool sheetUsable = part.SheetCandidate && sheet?.ThicknessMm != null;
        var row = new MontajParcaSatiri
        {
            SourceStepPath = stepPath,
            PartName = string.IsNullOrWhiteSpace(part.Name) ? "Parça " + part.LocalId : part.Name!,
            Quantity = part.Quantity,
            EngineClass = part.Classification ?? "",
            EngineReasons = part.ClassificationReasons,
            SheetCandidate = part.SheetCandidate,
            ProfileCandidate = string.IsNullOrWhiteSpace(part.ProfileCandidate) ? null : part.ProfileCandidate,
            ThicknessMm = sheetUsable ? sheet!.ThicknessMm : null,
            BendCount = sheetUsable ? sheet!.Bends.Count : 0,
            HoleSummary = solidId is null
                ? "—"
                : HoleSummaryFor(analysis.HoleFeatures.Where(x => x.SolidId?.LocalId == solidId)),
            DxfSourcePath = part.SheetCandidate ? dxfSourcePath : null,
            DxfCutOnlySourcePath = part.SheetCandidate ? dxfCutOnlySourcePath : null
        };
        row._laserMaximumMm = laserMaximumMm;
        row.Recalculate();
        return row;
    }

    public void SetLaserMaximum(double laserMaximumMm)
    {
        _laserMaximumMm = laserMaximumMm;
        Raise(nameof(GroupDisplay));
    }

    public void ApplyCatiaComparison(IReadOnlyList<CatiaScanSnapshotItem> matches)
    {
        CatiaReferenceTitle = "";
        CatiaQuantityDisplay = "—";
        CatiaSheetMetalFeature = null;
        if (matches.Count == 0)
            CatiaMatchDisplay = "Eşleşmedi";
        else if (matches.Count != 1)
            CatiaMatchDisplay = "Eşleme belirsiz";
        else
        {
            CatiaScanSnapshotItem match = matches[0];
            CatiaReferenceTitle = match.ReferenceTitle;
            CatiaQuantityDisplay = match.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "—";
            CatiaSheetMetalFeature = match.SheetMetalConfirmed;
            CatiaMatchDisplay = match.Quantity is int catiaQuantity && catiaQuantity != Quantity
                ? "Eşleşti (adet farklı: CATIA " + catiaQuantity + ")"
                : "Eşleşti";
        }
        Recalculate();
    }

    public void ApproveAsSheet()
    {
        if (!CanApproveAsSheet) return;
        DecisionSource = GeometryLabDecisionSource.User;
        EffectiveCategory = MontajParcaKategorisi.Sac;
        RaiseAll();
    }

    public void MoveToReview()
    {
        DecisionSource = GeometryLabDecisionSource.User;
        EffectiveCategory = MontajParcaKategorisi.KontrolGerekli;
        RaiseAll();
    }

    public void RestoreAutomaticDecision()
    {
        DecisionSource = GeometryLabDecisionSource.Automatic;
        Recalculate();
    }

    private void Recalculate()
    {
        bool catiaSheet = CatiaSheetMetalFeature == true;
        AutomaticCategory = EngineClass switch
        {
            "Sheet" when DxfSourcePath != null || ThicknessMm != null =>
                catiaSheet ? MontajParcaKategorisi.Sac : MontajParcaKategorisi.OnayGerekli,
            "ReviewRequired" when SheetCandidate && ProfileCandidate != null && catiaSheet && ThicknessMm != null =>
                MontajParcaKategorisi.Sac,
            "Profile" => MontajParcaKategorisi.Profil,
            "Other" => MontajParcaKategorisi.Diger,
            _ => MontajParcaKategorisi.KontrolGerekli
        };
        if (!HasUserDecision)
        {
            EffectiveCategory = AutomaticCategory;
            DecisionSource = AutomaticCategory == MontajParcaKategorisi.Sac && catiaSheet
                ? GeometryLabDecisionSource.ThreeDScan
                : GeometryLabDecisionSource.Automatic;
        }
        RaiseAll();
    }

    /// <summary>"2× Ø10 havşa (Ø20), 1× M8x1.25 dişli (DXF'te yok)"; "—" without holes.</summary>
    public static string HoleSummaryFor(IEnumerable<GeometryLabHoleFeatureTransport> holes)
    {
        var parts = holes
            .Select(hole => Describe(hole))
            .GroupBy(text => text)
            .Select(group => group.Count() + "× " + group.Key)
            .ToList();
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    private static string Describe(GeometryLabHoleFeatureTransport hole)
    {
        string through = hole.ThroughDiameterMm is double d ? "Ø" + FormatNumber(d) : "Ø?";
        string head = hole.HeadDiameterMm is double h ? " (Ø" + FormatNumber(h) + ")" : "";
        if (!string.IsNullOrWhiteSpace(hole.ThreadDesignation))
            return hole.ThreadDesignation + " dişli (DXF'te yok)";
        return hole.Type switch
        {
            "Countersink" => through + " havşa" + head,
            "Counterbore" => through + " imbus" + head,
            "Through" => through + " düz",
            _ => through + " belirsiz"
        };
    }

    public static string FormatNumber(double value) => value.ToString("0.###", Tr);

    private void RaiseAll()
    {
        foreach (string name in new[]
        {
            nameof(EffectiveCategory), nameof(AutomaticCategory), nameof(DecisionSource), nameof(HasUserDecision),
            nameof(StatusDisplay), nameof(DecisionDisplay), nameof(ExplanationDisplay), nameof(IsInSheetTab),
            nameof(IsInReviewTab), nameof(CatiaMatchDisplay), nameof(CatiaQuantityDisplay), nameof(CatiaReferenceTitle),
            nameof(CatiaSheetMetalFeature), nameof(GroupDisplay)
        })
            Raise(name);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>True when a STEP holds more than one part or repeats its only part.</summary>
    public static bool IsAssembly(GeometryLabAnalysisTransport? analysis) =>
        analysis != null && (analysis.Parts.Count > 1 || (analysis.Parts.Count == 1 && analysis.Parts[0].Quantity > 1));

    /// <summary>
    /// The analysis narrowed to one part's solid, so the existing profile row
    /// (GeometryLabStepProfileListItem.Apply) judges it like a single-part STEP.
    /// The part's own base stock replaces the whole-shape one (a processed
    /// profile keeps its fallback); whole-shape axis candidates are left out.
    /// </summary>
    public static GeometryLabProcessAdapterResult ResultForPart(GeometryLabProcessAdapterResult result,
        GeometryLabPartTransport part)
    {
        if (result.Analysis is null) return result;
        var solidIds = part.SolidIds.Select(x => x.LocalId).ToHashSet();
        GeometryLabProfileRecognitionTransport[] profiles = result.Analysis.ProfileRecognitions
            .Where(x => x.SolidId != null && solidIds.Contains(x.SolidId.LocalId)).ToArray();
        GeometryLabSolidBaseStockTransport? stock = part.SolidIds.Count == 1
            ? result.Analysis.BaseStockProfiles.FirstOrDefault(x => x.SolidId?.LocalId == part.SolidIds[0].LocalId)
            : null;
        return result with
        {
            Analysis = result.Analysis with
            {
                ProfileRecognitions = profiles,
                ProfileRecognition = profiles.Length == 1 ? profiles[0] : null,
                Solids = result.Analysis.Solids.Take(part.SolidIds.Count).ToArray(),
                BaseStockProfile = stock?.BaseStockProfile,
                ModificationAnalysis = stock?.ModificationAnalysis,
                BaseStockProfiles = stock is null ? Array.Empty<GeometryLabSolidBaseStockTransport>() : new[] { stock },
                ProfileGeometryAnalysis = null,
                Parts = new[] { part }
            },
            HasMultipleProfileResults = profiles.Length > 1
        };
    }
}
