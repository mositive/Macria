using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Macria;

public enum GeometryLabProfileListEligibility
{
    Eligible,
    ReviewRequired,
    Excluded
}

public enum GeometryLabDecisionSource
{
    Automatic,
    User,
    ThreeDScan
}

public enum GeometryLabExternalStepResultGroup
{
    DefiniteProfile,
    ProcessedProfile,
    ReviewRequired,
    Excluded,
    Unclassified
}

/// <summary>
/// Session-only, read-only presentation of one user-selected external STEP analysis.
/// It has no CATIA or ProfilRow references and never owns the input STEP file.
/// </summary>
public sealed class GeometryLabStepProfileListItem : INotifyPropertyChanged, IAnalizSatiri
{
    public required string SourceStepPath { get; init; }
    // Set for one part of an assembly STEP; the row then shows "file > part (xN)".
    public string? PartName { get; init; }
    public int PartQuantity { get; init; }
    // Engine identity of that part (localId, productId), for .macria decisions.
    public int? PartLocalId { get; init; }
    public string? PartProductId { get; init; }
    // The part's machining features (engraving, pockets ...), shown on every tab.
    public bool PartMachiningPresent { get; init; }
    public IReadOnlyList<string> PartMachiningKinds { get; init; } = Array.Empty<string>();
    // The part's engine reasons, for "Teknik ayrıntı" of Seçili Parça.
    public IReadOnlyList<string> PartEngineReasons { get; init; } = Array.Empty<string>();
    public string IslemeGosterimi => IslemeMetni.Kisa(PartMachiningPresent, PartMachiningKinds);
    int? IAnalizSatiri.ParcaLocalId => PartLocalId;
    // A profile row is a hollow section: open to the trials.
    string? IAnalizSatiri.DenemeyeKapaliNedeni => null;
    /// <summary>Built from a run on selected parts (Yeniden Analiz Et, Profil olarak dene).</summary>
    public SatirDenemesi? Deneme { get; private set; }

    public void DenemeyiIsaretle(SatirDenemesi deneme)
    {
        Deneme = deneme;
        foreach (string name in new[] { nameof(Deneme), nameof(KullaniciKarariMetni), nameof(DurumEtiketi) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public string SourceFileName => PartName is null
        ? System.IO.Path.GetFileName(SourceStepPath)
        : System.IO.Path.GetFileName(SourceStepPath) + " › " + PartName + " (" + PartQuantity + " adet)";
    // Table columns: the part (assembly part or the STEP's own name) and its
    // quantity; the file name is shown as a tooltip.
    public string PartDisplay => PartName ?? System.IO.Path.GetFileNameWithoutExtension(SourceStepPath);
    private int? _stepQuantity;
    public string QuantityDisplay => PartName != null
        ? PartQuantity.ToString(CultureInfo.InvariantCulture)
        : _stepQuantity?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private string _analysisStatus = "Bekliyor";
    public string AnalysisStatus { get => _analysisStatus; private set => Set(ref _analysisStatus, value); }
    public string EffectiveStatusDisplay => NormalizeEffectiveCategory(EffectiveCategory) switch
    {
        GeometryLabExternalStepResultGroup.DefiniteProfile => "Tanındı",
        GeometryLabExternalStepResultGroup.ReviewRequired => "İnceleme gerekli",
        // Automatic only (failed analysis, several solids, CATIA sheet): the
        // user's "Liste dışı" is a flag (ListeDisi), not this category.
        GeometryLabExternalStepResultGroup.Excluded when DecisionSource == GeometryLabDecisionSource.ThreeDScan => "Sac (CATIA)",
        GeometryLabExternalStepResultGroup.Excluded => AnalysisStatus,
        _ => "Tanımsız"
    };

    private string _profileType = "—";
    public string ProfileType { get => _profileType; private set => Set(ref _profileType, value); }
    public string EffectiveProfileTypeDisplay => _catiaSheetMetalConfirmed && !HasUserDecision ? "Sac Parça" : ProfileType;

    private string _sectionDisplay = "—";
    public string SectionDisplay { get => _sectionDisplay; private set => Set(ref _sectionDisplay, value); }

    private string _lengthDisplay = "—";
    /// <summary>Reliable physical span along the profile axis; never a saw length.</summary>
    public string LengthDisplay { get => _lengthDisplay; private set => Set(ref _lengthDisplay, value); }

    private string _topologyDisplay = "—";
    public string TopologyDisplay { get => _topologyDisplay; private set => Set(ref _topologyDisplay, value); }

    private string _cutDisplay = "—";
    public string CutDisplay { get => _cutDisplay; private set => Set(ref _cutDisplay, value); }

    private string _operationDisplay = "—";
    public string OperationDisplay { get => _operationDisplay; private set => Set(ref _operationDisplay, value); }

    private string _operationToolTip = "";
    public string OperationToolTip { get => _operationToolTip; private set => Set(ref _operationToolTip, value); }

    private string _evidenceStatus = "Bekliyor";
    public string EvidenceStatus { get => _evidenceStatus; private set => Set(ref _evidenceStatus, value); }

    private string _failureReason = "";
    public string FailureReason { get => _failureReason; private set => Set(ref _failureReason, value); }
    public string ExplanationDisplay => string.IsNullOrWhiteSpace(FailureReason) ? EvidenceStatus : FailureReason;
    public GeometryLabProfileListEligibility Eligibility { get; private set; } = GeometryLabProfileListEligibility.Excluded;
    public bool IsEligible => Eligibility == GeometryLabProfileListEligibility.Eligible;
    public GeometryLabExternalStepResultGroup ResultGroup { get; private set; } = GeometryLabExternalStepResultGroup.Unclassified;
    public string EligibilityDisplay => Eligibility switch
    {
        GeometryLabProfileListEligibility.Eligible => "Kesin profil",
        GeometryLabProfileListEligibility.ReviewRequired => "İnceleme gerekli",
        _ => "Liste dışı"
    };

    public GeometryLabExternalStepResultGroup AutomaticCategory { get; private set; } = GeometryLabExternalStepResultGroup.Unclassified;
    public GeometryLabExternalStepResultGroup EffectiveCategory { get; private set; } = GeometryLabExternalStepResultGroup.Unclassified;
    public GeometryLabDecisionSource DecisionSource { get; private set; } = GeometryLabDecisionSource.Automatic;
    public string UserDecisionNote { get; private set; } = "";
    /// <summary>Which user decision is in effect (MacriaProje.Karar*), or null.</summary>
    public string? KullaniciKarari { get; private set; }
    public string OriginalAutomaticReason { get; private set; } = "";
    public bool HasUserDecision => DecisionSource == GeometryLabDecisionSource.User;
    public string CatiaQuantityDisplay { get; private set; } = "—";
    public string CatiaMatchDisplay { get; private set; } = "CATIA taraması yok";
    public string CatiaReferenceTitle { get; private set; } = "";
    private bool _catiaSheetMetalConfirmed;
    public string DecisionToolTip => HasUserDecision
        ? "Karar kaynağı: Kullanıcı\nİlk otomatik karar: " + CategoryText(AutomaticCategory) +
          (string.IsNullOrWhiteSpace(UserDecisionNote) ? "" : "\nNot: " + UserDecisionNote)
        : "Karar kaynağı: Otomatik";

    // Technical evidence remains available to a future detail view without exposing face/edge IDs in the table.
    public GeometryLabLengthCandidatesTransport? RawLengthCandidates { get; private set; }
    public GeometryLabBaseStockProfileTransport? RawBaseStockProfile { get; private set; }
    public GeometryLabModificationAnalysisTransport? RawModificationAnalysis { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed record AutomaticPresentation(string AnalysisStatus, string ProfileType, string SectionDisplay,
        string LengthDisplay, string TopologyDisplay, string CutDisplay, string OperationDisplay,
        string OperationToolTip, string EvidenceStatus, string FailureReason);
    private AutomaticPresentation? _automaticPresentation;

    public void MarkAnalyzing()
    {
        _catiaSheetMetalConfirmed = false;
        CatiaQuantityDisplay = "—";
        CatiaMatchDisplay = "CATIA taraması yok";
        CatiaReferenceTitle = "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveProfileTypeDisplay)));
        AnalysisStatus = "Analiz ediliyor";
        EvidenceStatus = "GeometryLab çalışıyor";
        FailureReason = "";
        SetResultGroup(GeometryLabExternalStepResultGroup.Unclassified);
    }

    public void Apply(GeometryLabProcessAdapterResult result)
    {
        _stepQuantity = result.Analysis?.Parts.Count == 1 ? result.Analysis.Parts[0].Quantity : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantityDisplay)));
        RawLengthCandidates = null;
        RawBaseStockProfile = null;
        RawModificationAnalysis = null;
        ProfileType = "—";
        SectionDisplay = "—";
        LengthDisplay = "—";
        TopologyDisplay = "—";
        CutDisplay = "—";
        OperationDisplay = "—";
        OperationToolTip = "";

        if (!result.IsSuccess)
        {
            AnalysisStatus = result.Status == GeometryLabProcessAdapterStatus.Cancelled ? "İptal edildi" : "Analiz başarısız";
            EvidenceStatus = "GeometryLab analizi tamamlanamadı.";
            FailureReason = UserExplanation(result.Message);
            SetResultGroup(GeometryLabExternalStepResultGroup.Excluded);
            return;
        }

        GeometryLabAnalysisTransport? analysis = result.Analysis;
        if (analysis == null || !GeometryLabProcessAdapter.IsSupportedSchemaVersion(analysis.SchemaVersion))
        {
            AnalysisStatus = "Analiz başarısız";
            EvidenceStatus = "Şema doğrulanamadı";
            FailureReason = "GeometryLab JSON şema sürümü desteklenmiyor.";
            SetResultGroup(GeometryLabExternalStepResultGroup.Excluded);
            return;
        }
        if (!string.Equals(analysis.Status, "Succeeded", StringComparison.Ordinal))
        {
            AnalysisStatus = "Analiz başarısız";
            EvidenceStatus = "GeometryLab analizi başarılı tamamlanmadı.";
            FailureReason = UserExplanation(FirstMessage(analysis));
            SetResultGroup(GeometryLabExternalStepResultGroup.Excluded);
            return;
        }

        GeometryLabProfileRecognitionTransport[] profiles = analysis.ProfileRecognitions.ToArray();
        if (profiles.Length != 1)
        {
            AnalysisStatus = profiles.Length > 1 ? "Çoklu solid" : "Tanımsız";
            EvidenceStatus = profiles.Length > 1 ? "Birden fazla profil sonucu; otomatik seçim yapılmadı." : "Profil sonucu yok.";
            FailureReason = profiles.Length > 1 ? "Birden fazla solid bulundu; otomatik profil seçimi yapılmadı." : "Geometri kesin olarak sınıflandırılamadı.";
            SetResultGroup(profiles.Length > 1 ? GeometryLabExternalStepResultGroup.Excluded : GeometryLabExternalStepResultGroup.Unclassified);
            return;
        }

        GeometryLabProfileRecognitionTransport profile = profiles[0];
        RawLengthCandidates = profile.LengthCandidates;
        bool profileSucceeded = string.Equals(profile.Status, "Succeeded", StringComparison.Ordinal);
        bool sectionRecognized = string.Equals(profile.SectionRecognitionStatus, "Recognized", StringComparison.Ordinal);
        bool knownType = !string.IsNullOrWhiteSpace(profile.ProfileType) &&
                         !string.Equals(profile.ProfileType, "Unknown", StringComparison.OrdinalIgnoreCase);
        bool sectionFormatted = TryFormatSection(profile, out string section);
        if (!profileSucceeded || !sectionRecognized || !knownType || !sectionFormatted)
        {
            if (TryApplyBaseStockFallback(analysis, profiles)) return;

            AnalysisStatus = StatusFor(profile.SectionRecognitionStatus, profile.Status);
            EvidenceStatus = Evidence(profile);
            FailureReason = UserExplanation(profile.RejectionReason ?? (!sectionFormatted && sectionRecognized
                ? "Kesit ölçüleri raporlanamadı."
                : FirstMessage(analysis)));
            SetResultGroup(IsGeometricProfile(profile) ? GeometryLabExternalStepResultGroup.ReviewRequired : GroupForUnrecognized(profile));
            return;
        }

        ProfileType = TurkishProfileType(profile.ProfileType!);
        SectionDisplay = section;
        LengthDisplay = FormatPhysicalAxisLength(analysis, profile, null);
        TopologyDisplay = FormatTopology(profile.LengthSummary);
        CutDisplay = FormatCuts(profile.EndCutCandidates);
        EvidenceStatus = Evidence(profile);
        if (IsEligibleProductionProfile(profile))
        {
            AnalysisStatus = "Tanındı";
            FailureReason = string.IsNullOrWhiteSpace(profile.RejectionReason) ? "" : UserExplanation(profile.RejectionReason);
            SetResultGroup(GeometryLabExternalStepResultGroup.DefiniteProfile);
        }
        else
        {
            AnalysisStatus = "İnceleme gerekli";
            FailureReason = UserExplanation(EligibilityReason(profile));
            SetResultGroup(GeometryLabExternalStepResultGroup.ReviewRequired);
        }
    }

    public bool CanUserConfirmExistingHollowProfile =>
        ProfileType is "Kare Kutu Profil" or "Dikdörtgen Kutu Profil" or "Boru" && SectionDisplay != "—";

    public void ConfirmAsProfile(string? note = null)
    {
        ApplyUserCategory(GeometryLabExternalStepResultGroup.DefiniteProfile,
            "Kullanıcı tarafından kesin profil olarak onaylandı.", note, "Tanındı", MacriaProje.KararProfilOnayla);
    }

    /// <summary>
    /// Stores a user-confirmed hollow section for this session only.  The GeometryLab result
    /// and its raw evidence are deliberately left untouched.
    /// </summary>
    public void ConfirmManualHollowProfile(string profileType, string sectionDisplay, string? note = null)
    {
        ProfileType = profileType;
        SectionDisplay = sectionDisplay;
        ApplyUserCategory(GeometryLabExternalStepResultGroup.DefiniteProfile,
            "Profil türü ve kesit kullanıcı tarafından manuel olarak onaylandı.", note, "Tanındı", MacriaProje.KararElleProfil);
    }

    public void MoveToReview(string? note = null) => ApplyUserCategory(GeometryLabExternalStepResultGroup.ReviewRequired,
        "Kullanıcı kararıyla kontrol gerekliye alındı.", note, "İnceleme gerekli", MacriaProje.KararKontrole);

    /// <summary>Takes the row out of every list (Liste dışı); its decision stays for Geri Al.</summary>
    public void ExcludeFromList(string? note = null) => ListeDisinaCikar(note);

    /// <summary>Taken out of every list by the user; Geri Al brings it back to its tab.</summary>
    public bool ListeDisi { get; private set; }
    public string ListeDisiNotu { get; private set; } = "";

    public void ListeDisinaCikar(string? not = null)
    {
        ListeDisi = true;
        ListeDisiNotu = not?.Trim() ?? "";
        RaiseDecisionProperties();
    }

    public void ListeyeGeriAl()
    {
        ListeDisi = false;
        ListeDisiNotu = "";
        RaiseDecisionProperties();
    }

    public void RestoreAutomaticDecision()
    {
        if (!HasUserDecision || _automaticPresentation == null) return;
        AutomaticPresentation p = _automaticPresentation;
        AnalysisStatus = p.AnalysisStatus; ProfileType = p.ProfileType; SectionDisplay = p.SectionDisplay;
        LengthDisplay = p.LengthDisplay; TopologyDisplay = p.TopologyDisplay; CutDisplay = p.CutDisplay;
        OperationDisplay = p.OperationDisplay; OperationToolTip = p.OperationToolTip;
        EvidenceStatus = p.EvidenceStatus; FailureReason = p.FailureReason;
        if (_catiaSheetMetalConfirmed)
        {
            SetEffectiveCategory(GeometryLabExternalStepResultGroup.Excluded);
            DecisionSource = GeometryLabDecisionSource.ThreeDScan;
            FailureReason = "Sac parça — aktif 3B tarama verisinden doğrulandı.";
        }
        else
        {
            SetEffectiveCategory(NormalizeEffectiveCategory(AutomaticCategory));
            DecisionSource = GeometryLabDecisionSource.Automatic;
        }
        UserDecisionNote = "";
        KullaniciKarari = null;
        RaiseDecisionProperties();
    }

    public void ApplyCatiaComparison(IReadOnlyList<CatiaScanSnapshotItem> matches)
    {
        if (matches.Count == 0) { CatiaMatchDisplay = "Eşleşmedi"; RaiseDecisionProperties(); return; }
        if (matches.Count != 1) { CatiaMatchDisplay = "Eşleme belirsiz"; RaiseDecisionProperties(); return; }
        CatiaScanSnapshotItem match = matches[0];
        CatiaReferenceTitle = match.ReferenceTitle;
        CatiaQuantityDisplay = match.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "—";
        CatiaMatchDisplay = HasUserDecision ? "Kullanıcı kararı korundu" : "Eşleşti";
        if (!HasUserDecision && match.SheetMetalConfirmed)
        {
            _catiaSheetMetalConfirmed = true;
            SetEffectiveCategory(GeometryLabExternalStepResultGroup.Excluded);
            DecisionSource = GeometryLabDecisionSource.ThreeDScan;
            FailureReason = "Sac parça — aktif 3B tarama verisinden doğrulandı.";
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveProfileTypeDisplay)));
        RaiseDecisionProperties();
    }

    private void ApplyUserCategory(GeometryLabExternalStepResultGroup category, string explanation, string? note, string status,
        string kullaniciKarari)
    {
        if (_automaticPresentation == null) return;
        SetEffectiveCategory(category);
        DecisionSource = GeometryLabDecisionSource.User;
        KullaniciKarari = kullaniciKarari;
        _kullaniciKarariAciklamasi = explanation;
        UserDecisionNote = note?.Trim() ?? "";
        AnalysisStatus = status;
        FailureReason = explanation;
        RaiseDecisionProperties();
    }

    private bool TryApplyBaseStockFallback(
        GeometryLabAnalysisTransport analysis,
        GeometryLabProfileRecognitionTransport[] profiles)
    {
        GeometryLabBaseStockProfileTransport? baseStock = analysis.BaseStockProfile;
        GeometryLabModificationAnalysisTransport? modification = analysis.ModificationAnalysis;
        if (analysis.Solids.Count != 1 || profiles.Length != 1 || baseStock == null ||
            !string.Equals(baseStock.Status, "Recognized", StringComparison.Ordinal) ||
            !IsBaseStockHollowProfile(baseStock) ||
            !TryFormatBaseStockSection(baseStock, out string section) ||
            modification == null || !IsSupportedModification(modification.Status))
            return false;

        RawBaseStockProfile = baseStock;
        RawModificationAnalysis = modification;
        ProfileType = TurkishProfileType(baseStock.ProfileType!);
        SectionDisplay = section;
        // Base-stock recognition never invents length or cut values. Existing valid
        // normal values are kept only when the engine actually returned them.
        LengthDisplay = FormatPhysicalAxisLength(analysis, profiles[0], baseStock);
        TopologyDisplay = FormatTopology(profiles[0].LengthSummary);
        CutDisplay = HasValidNormalCuts(profiles[0]) ? FormatCuts(profiles[0].EndCutCandidates) : "—";
        OperationDisplay = modification.Status == "LocallyModified" ? "Lokal işlemli" : "Yoğun işlemli";
        OperationToolTip = modification.Status == "LocallyModified"
            ? "Aynı profil kesitini taşıyan temiz bölgeler arasında sınırlı bir geometrik değişiklik bulundu."
            : "Profil boyunca birden fazla veya geniş geometrik değişiklik bulundu; temel stok kesiti temiz bölgelerden tanındı.";
        AnalysisStatus = "Tanındı";
        EvidenceStatus = "Ayrık temiz kesit bölgelerinden temel stok profil tanındı.";
        FailureReason = "";
        SetResultGroup(GeometryLabExternalStepResultGroup.ProcessedProfile);
        return true;
    }

    private static string StatusFor(string? sectionStatus, string? profileStatus) =>
        string.Equals(sectionStatus, "InsufficientEvidence", StringComparison.Ordinal) ? "Tanımsız" :
        string.Equals(sectionStatus, "Ambiguous", StringComparison.Ordinal) ? "Tanımsız" :
        string.Equals(sectionStatus, "Unsupported", StringComparison.Ordinal) ? "Desteklenmiyor" :
        string.Equals(profileStatus, "Succeeded", StringComparison.Ordinal) ? "Tanımsız" : "Analiz başarısız";

    private static string Evidence(GeometryLabProfileRecognitionTransport profile) =>
        "Kesit: " + TurkishTechnicalStatus(profile.SectionRecognitionStatus) +
        " · Boy: " + TurkishTechnicalStatus(profile.LengthRecognitionStatus) +
        " · Açılı kesim: " + TurkishTechnicalStatus(profile.CutRecognitionStatus);

    private static GeometryLabExternalStepResultGroup GroupForUnrecognized(GeometryLabProfileRecognitionTransport profile) =>
        string.Equals(profile.SectionRecognitionStatus, "Unsupported", StringComparison.Ordinal) ||
        string.Equals(profile.Status, "Failed", StringComparison.Ordinal)
            ? GeometryLabExternalStepResultGroup.Excluded
            : GeometryLabExternalStepResultGroup.Unclassified;

    private static bool IsGeometricProfile(GeometryLabProfileRecognitionTransport profile) =>
        string.Equals(profile.Status, "Succeeded", StringComparison.Ordinal) &&
        string.Equals(profile.SectionRecognitionStatus, "Recognized", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(profile.ProfileType) &&
        !string.Equals(profile.ProfileType, "Unknown", StringComparison.OrdinalIgnoreCase);

    private static bool IsBaseStockHollowProfile(GeometryLabBaseStockProfileTransport profile) =>
        profile.ProfileType is "SquareHollowSection" or "RectangularHollowSection" or "CircularHollowSection";

    private static bool IsSupportedModification(string? value) =>
        value is "LocallyModified" or "ExtensivelyModified";

    private static bool HasValidNormalLength(GeometryLabProfileRecognitionTransport profile) =>
        string.Equals(profile.LengthRecognitionStatus, "Recognized", StringComparison.Ordinal) &&
        profile.LengthSummary?.MeasurementStatus == "Valid";

    private static bool HasValidNormalCuts(GeometryLabProfileRecognitionTransport profile) =>
        string.Equals(profile.CutRecognitionStatus, "Recognized", StringComparison.Ordinal) &&
        profile.EndCutCandidates.Count == 2 &&
        profile.EndCutCandidates.All(c => c.MeasurementStatus == "Valid" && c.CutAngleDegrees.HasValue);

    private static string FormatPhysicalAxisLength(
        GeometryLabAnalysisTransport analysis,
        GeometryLabProfileRecognitionTransport profile,
        GeometryLabBaseStockProfileTransport? baseStock)
    {
        GeometryLabProfileGeometryAnalysisTransport? geometry = analysis.ProfileGeometryAnalysis;
        if (geometry == null || !string.Equals(geometry.AxisDetectionStatus, "Determined", StringComparison.Ordinal)) return "—";

        GeometryLabProfileAxisCandidateTransport[] candidates = baseStock != null
            ? geometry.AxisCandidates.Where(x => x.LocalId == baseStock.AxisCandidateId && x.Reliable).ToArray()
            : geometry.AxisCandidates.Where(x => x.Reliable).ToArray();
        if (candidates.Length != 1) return "—";
        GeometryLabProfileAxisCandidateTransport candidate = candidates[0];
        return candidate.ProjectionSpanMm is double span && double.IsFinite(span) && span > 0
            ? Number(span) + " mm"
            : "—";
    }

    private static bool IsEligibleProductionProfile(GeometryLabProfileRecognitionTransport profile) =>
        profile.ProfileType is "SquareHollowSection" or "RectangularHollowSection" or "CircularHollowSection" &&
        string.Equals(profile.LengthRecognitionStatus, "Recognized", StringComparison.Ordinal) &&
        profile.LengthSummary?.MeasurementStatus == "Valid" &&
        (profile.LengthSummary.Classification == "Uniform" && profile.LengthSummary.UniformLengthMm.HasValue ||
         profile.LengthSummary.Classification == "VariableByCut" && profile.LengthSummary.ShortLengthMm.HasValue &&
         profile.LengthSummary.CenterLengthMm.HasValue && profile.LengthSummary.LongLengthMm.HasValue) &&
        string.Equals(profile.CutRecognitionStatus, "Recognized", StringComparison.Ordinal) &&
        profile.EndCutCandidates.Count == 2 &&
        profile.EndCutCandidates.All(c => c.MeasurementStatus == "Valid" && c.CutAngleDegrees.HasValue);

    private static string EligibilityReason(GeometryLabProfileRecognitionTransport profile)
    {
        if (profile.ProfileType is "SolidSquareBar" or "SolidRectangularBar" or "SolidCircularBar")
            return "Geometrik prizma; sac/lama üretim türü B-Rep'ten doğrulanamadı.";
        if (profile.LengthSummary?.MeasurementStatus != "Valid" || profile.LengthRecognitionStatus != "Recognized")
            return "Kesit tanındı; güvenilir profil boyu bulunamadı.";
        if (profile.CutRecognitionStatus != "Recognized" || profile.EndCutCandidates.Count != 2 ||
            profile.EndCutCandidates.Any(c => c.MeasurementStatus != "Valid" || !c.CutAngleDegrees.HasValue))
            return "Kesit tanındı; iki benzersiz geçerli uç doğrulanamadı.";
        return profile.RejectionReason ?? "Üretim profili uygunluk kanıtı yeterli değil.";
    }

    private static string? FirstMessage(GeometryLabAnalysisTransport analysis) =>
        analysis.Errors.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ??
        analysis.Warnings.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static bool TryFormatSection(GeometryLabProfileRecognitionTransport p, out string value)
    {
        value = "—";
        switch (p.ProfileType)
        {
            case "SquareHollowSection":
            case "RectangularHollowSection":
                if (p.OuterWidthMm is double w && p.OuterHeightMm is double h && p.WallThicknessMm is double wall)
                    value = $"{Number(w)} × {Number(h)} × {Number(wall)} mm";
                break;
            case "CircularHollowSection":
                if (p.OuterDiameterMm is double d && p.WallThicknessMm is double circularWall)
                    value = $"Ø{Number(d)} × {Number(circularWall)} mm";
                break;
            case "SolidSquareBar":
                if (p.OuterWidthMm is double side)
                    value = $"{Number(side)} × {Number(side)} mm";
                break;
            case "SolidRectangularBar":
                if (p.OuterWidthMm is double rw && p.OuterHeightMm is double rh)
                    value = $"{Number(rw)} × {Number(rh)} mm";
                break;
            case "SolidCircularBar":
                if (p.OuterDiameterMm is double diameter)
                    value = $"Ø{Number(diameter)} mm";
                break;
        }
        return value != "—";
    }

    private static bool TryFormatBaseStockSection(GeometryLabBaseStockProfileTransport p, out string value)
    {
        value = "—";
        switch (p.ProfileType)
        {
            case "SquareHollowSection":
            case "RectangularHollowSection":
                if (p.OuterWidthMm is double w && p.OuterHeightMm is double h &&
                    p.InnerWidthMm.HasValue && p.InnerHeightMm.HasValue && p.WallThicknessMm is double wall)
                    value = $"{Number(w)} × {Number(h)} × {Number(wall)} mm";
                break;
            case "CircularHollowSection":
                if (p.OuterDiameterMm is double d && p.InnerDiameterMm.HasValue && p.WallThicknessMm is double circularWall)
                    value = $"Ø{Number(d)} × {Number(circularWall)} mm";
                break;
        }
        return value != "—";
    }

    private static string FormatTopology(GeometryLabLengthSummaryTransport? summary)
    {
        if (summary?.MeasurementStatus != "Valid") return "—";
        if (summary.Classification == "VariableByCut" && summary.ShortLengthMm is double shortLength &&
            summary.CenterLengthMm is double centerLength && summary.LongLengthMm is double longLength)
            return $"Kısa {Number(shortLength)} / Merkez {Number(centerLength)} / Uzun {Number(longLength)} mm";
        return "—";
    }

    private static string FormatCuts(System.Collections.Generic.IReadOnlyList<GeometryLabEndCutCandidateTransport> cuts)
    {
        GeometryLabEndCutCandidateTransport[] valid = cuts
            .Where(c => c.MeasurementStatus == "Valid" && c.CutAngleDegrees.HasValue)
            .ToArray();
        if (valid.Length == 2 && cuts.Count == 2)
            return Number(valid[0].CutAngleDegrees!.Value) + "° / " + Number(valid[1].CutAngleDegrees!.Value) + "°";
        return "—";
    }

    private static string TurkishProfileType(string value) => value switch
    {
        "SquareHollowSection" => "Kare Kutu Profil",
        "RectangularHollowSection" => "Dikdörtgen Kutu Profil",
        "CircularHollowSection" => "Boru",
        "SolidSquare" or "SolidSquareBar" => "Kare Lama",
        "SolidRectangle" or "SolidRectangularBar" => "Dikdörtgen Lama",
        "SolidCircle" or "SolidCircularBar" => "Dolu Mil",
        _ => "—"
    };

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string TurkishTechnicalStatus(string? value) => value switch
    {
        "Recognized" => "Tanındı",
        "NotStarted" => "Başlatılmadı",
        "Ambiguous" => "Belirsiz",
        "InsufficientEvidence" => "Yetersiz kanıt",
        "Unsupported" => "Desteklenmiyor",
        "Failed" => "Analiz başarısız",
        "Valid" => "Geçerli",
        _ => "—"
    };

    private static string UserExplanation(string? technicalReason)
    {
        if (string.IsNullOrWhiteSpace(technicalReason)) return "Geometri kesin olarak sınıflandırılamadı.";
        if (technicalReason.Contains("No reliable profile axis", StringComparison.OrdinalIgnoreCase))
            return "Güvenilir profil ekseni bulunamadı.";
        if (technicalReason.Contains("Stable representative full sections", StringComparison.OrdinalIgnoreCase))
            return "Kararlı ve tam profil kesiti bulunamadı.";
        if (technicalReason.Contains("Disjoint full-section regions", StringComparison.OrdinalIgnoreCase))
            return "Ayrık kesit bölgeleri tek bir standart profil sınıflandırmasına izin vermedi.";
        if (technicalReason.Contains("outside the supported", StringComparison.OrdinalIgnoreCase))
            return "Kesit geometrisi desteklenen profil ailelerinin dışında.";
        if (technicalReason.Contains("multiple", StringComparison.OrdinalIgnoreCase) &&
            technicalReason.Contains("solid", StringComparison.OrdinalIgnoreCase))
            return "Birden fazla solid bulundu; otomatik profil seçimi yapılmadı.";
        if (technicalReason.Any(c => c > 127)) return technicalReason;
        return "Geometri kesin olarak sınıflandırılamadı.";
    }

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(EvidenceStatus) or nameof(FailureReason))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExplanationDisplay)));
    }

    private void SetEligibility(GeometryLabProfileListEligibility value)
    {
        if (Eligibility == value) return;
        Eligibility = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Eligibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEligible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EligibilityDisplay)));
    }

    private void SetResultGroup(GeometryLabExternalStepResultGroup value)
    {
        AutomaticCategory = value;
        OriginalAutomaticReason = FailureReason;
        DecisionSource = GeometryLabDecisionSource.Automatic;
        UserDecisionNote = "";
        KullaniciKarari = null;
        SetEffectiveCategory(NormalizeEffectiveCategory(value));
        _automaticPresentation = new AutomaticPresentation(AnalysisStatus, ProfileType, SectionDisplay,
            LengthDisplay, TopologyDisplay, CutDisplay, OperationDisplay, OperationToolTip, EvidenceStatus, FailureReason);
        RaiseDecisionProperties();
    }

    private static GeometryLabExternalStepResultGroup NormalizeEffectiveCategory(GeometryLabExternalStepResultGroup value) =>
        value == GeometryLabExternalStepResultGroup.ProcessedProfile
            ? GeometryLabExternalStepResultGroup.DefiniteProfile
            : value;

    private static string CategoryText(GeometryLabExternalStepResultGroup value) => NormalizeEffectiveCategory(value) switch
    {
        GeometryLabExternalStepResultGroup.DefiniteProfile => "Kesin profiller",
        GeometryLabExternalStepResultGroup.ReviewRequired => "İnceleme gerekli",
        GeometryLabExternalStepResultGroup.Excluded => "Liste dışı",
        _ => "Tanımsız"
    };

    private void SetEffectiveCategory(GeometryLabExternalStepResultGroup value)
    {
        if (ResultGroup != value)
        {
            ResultGroup = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResultGroup)));
        }
        if (EffectiveCategory != value)
        {
            EffectiveCategory = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveCategory)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveStatusDisplay)));
        }
        SetEligibility(value switch
        {
            GeometryLabExternalStepResultGroup.DefiniteProfile => GeometryLabProfileListEligibility.Eligible,
            GeometryLabExternalStepResultGroup.ReviewRequired => GeometryLabProfileListEligibility.ReviewRequired,
            _ => GeometryLabProfileListEligibility.Excluded
        });
    }

    private void RaiseDecisionProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutomaticCategory)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DecisionSource)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserDecisionNote)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalAutomaticReason)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUserDecision)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveProfileTypeDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DecisionToolTip)));
        foreach (string name in new[] { nameof(ListeDisi), nameof(Sekme), nameof(DurumEtiketi), nameof(KararGosterimi),
                                         nameof(AciklamaGosterimi), nameof(EffectiveStatusDisplay), nameof(KullaniciKarari),
                                         nameof(MotorGerekcesi), nameof(KullaniciKarariMetni) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // IAnalizSatiri: common columns of the mixed tabs and the common toolbar.
    public AnalizSekmesi Sekme => ListeDisi ? AnalizSekmesi.ListeDisi : NormalizeEffectiveCategory(EffectiveCategory) switch
    {
        GeometryLabExternalStepResultGroup.DefiniteProfile => AnalizSekmesi.Profiller,
        GeometryLabExternalStepResultGroup.ReviewRequired => AnalizSekmesi.KontrolGerekli,
        // Several solids, or CATIA calls the part a sheet: someone has to look.
        GeometryLabExternalStepResultGroup.Excluded when DecisionSource == GeometryLabDecisionSource.ThreeDScan ||
                                                         AnalysisStatus == "Çoklu solid" => AnalizSekmesi.KontrolGerekli,
        // The engine classified this part as a profile: there is evidence.
        GeometryLabExternalStepResultGroup.Unclassified when PartLocalId != null => AnalizSekmesi.KontrolGerekli,
        _ => AnalizSekmesi.Tanimsiz
    };
    bool IAnalizSatiri.ProfilSatiri => true;
    string IAnalizSatiri.KaynakYolu => SourceStepPath;
    string? IAnalizSatiri.ParcaAdi => PartName;
    string IAnalizSatiri.ParcaGosterimi => SourceFileName;
    string IAnalizSatiri.AdetGosterimi => QuantityDisplay;
    string IAnalizSatiri.TurGosterimi => EffectiveProfileTypeDisplay == "—" ? "Profil?" : EffectiveProfileTypeDisplay;
    string IAnalizSatiri.OlcuGosterimi => SectionDisplay;
    // A profile row from Profil olarak dene: the trial found a profile (an
    // unrecognized part stays a part row), so "(deneme)" is shown.
    public string DurumEtiketi => Deneme is { Mod: MacriaProje.DenemeProfil } ? EffectiveStatusDisplay + " (deneme)" : EffectiveStatusDisplay;
    public string KararGosterimi => DecisionSource switch
    {
        GeometryLabDecisionSource.User => "Kullanıcı",
        GeometryLabDecisionSource.ThreeDScan => "CATIA 3B tarama",
        _ => "Otomatik"
    };
    public string AciklamaGosterimi => ListeDisi && ListeDisiNotu.Length > 0
        ? ExplanationDisplay + " (Liste dışı: " + ListeDisiNotu + ")"
        : ExplanationDisplay;
    private string _kullaniciKarariAciklamasi = "";
    /// <summary>The automatic explanation, kept when a user decision replaces ExplanationDisplay.</summary>
    public string MotorGerekcesi
    {
        get
        {
            if (_automaticPresentation is not { } p) return string.IsNullOrWhiteSpace(ExplanationDisplay) ? "—" : ExplanationDisplay;
            string metin = string.IsNullOrWhiteSpace(p.FailureReason) ? p.EvidenceStatus : p.FailureReason;
            return string.IsNullOrWhiteSpace(metin) ? "—" : metin;
        }
    }
    public string KullaniciKarariMetni
    {
        get
        {
            var parcalar = new List<string>();
            if (Deneme != null) parcalar.Add(Deneme.Metin);
            if (HasUserDecision)
                parcalar.Add(_kullaniciKarariAciklamasi.TrimEnd('.') + (UserDecisionNote.Length > 0 ? " — " + UserDecisionNote : ""));
            if (ListeDisi)
                parcalar.Add("Liste dışı" + (ListeDisiNotu.Length > 0 ? ": " + ListeDisiNotu : ""));
            return parcalar.Count == 0 ? "—" : string.Join("; ", parcalar);
        }
    }
    bool IAnalizSatiri.OnayBekliyor => false;
    IReadOnlyList<KeyValuePair<string, string>> IAnalizSatiri.Ayrintilar => new KeyValuePair<string, string>[]
    {
        new("STEP dosyası", SourceFileName),
        new("Durum", DurumEtiketi),
        new("Parça türü", EffectiveProfileTypeDisplay),
        new("Kesit", SectionDisplay),
        new("Boy", LengthDisplay),
        new("Topoloji bilgisi", TopologyDisplay),
        new("Açılı kesim", CutDisplay),
        new("İşlem durumu", OperationDisplay),
        new("İşleme", IslemeMetni.Ayrinti(PartMachiningPresent, PartMachiningKinds, sac: false)),
        new("CATIA adedi", CatiaQuantityDisplay),
        new("Eşleşme", CatiaMatchDisplay),
        new("Karar", KararGosterimi),
        new("Motor gerekçesi", MotorGerekcesi),
        new(TeknikAyrinti.Anahtar, MotorGerekcesiMetni.Teknik(PartEngineReasons)),
        new("Kullanıcı kararı", KullaniciKarariMetni)
    };
    void IAnalizSatiri.KontrolGerekliyeAl() => MoveToReview();
}
