using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Macria;

// This is a deliberately narrow transport contract for GeometryEngine JSON.
// It has no CATIA COM references and does not reuse analysis-local Face/Edge IDs.
public sealed record GeometryLabAnalysisTransport
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    /// <summary>
    /// "Automatic" (every part, automatic rules), "Parts" (selected parts),
    /// "TrialProfile" / "TrialSheet" (selected parts, one recognizer); null
    /// from engines before 2026.10.6. Only an automatic output is a STEP's analysis.
    /// </summary>
    [JsonPropertyName("analysisMode")]
    public string? AnalysisMode { get; init; }

    [JsonPropertyName("selectedPartIds")]
    public IReadOnlyList<int>? SelectedPartIds { get; init; }

    /// <summary>The output of a whole-STEP analysis under the automatic rules.</summary>
    [JsonIgnore]
    public bool Otomatik => AnalysisMode is null or "Automatic";

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    // Only the count is used by the external STEP list to avoid selecting a result
    // from a multi-solid analysis. These IDs are never mapped to Macria references.
    [JsonPropertyName("solids")]
    public IReadOnlyList<GeometryLabSolidTransport> Solids { get; init; } = Array.Empty<GeometryLabSolidTransport>();

    [JsonPropertyName("profileRecognition")]
    public GeometryLabProfileRecognitionTransport? ProfileRecognition { get; init; }

    [JsonPropertyName("profileRecognitions")]
    public IReadOnlyList<GeometryLabProfileRecognitionTransport> ProfileRecognitions { get; init; } =
        Array.Empty<GeometryLabProfileRecognitionTransport>();

    // Optional schema 1.0 additions from GeometryLab 4D. They supplement, and do
    // not replace, the existing per-solid profileRecognition result.
    [JsonPropertyName("baseStockProfile")]
    public GeometryLabBaseStockProfileTransport? BaseStockProfile { get; init; }

    [JsonPropertyName("modificationAnalysis")]
    public GeometryLabModificationAnalysisTransport? ModificationAnalysis { get; init; }

    // Existing schema 1.0 technical evidence used only to present an already
    // validated physical profile-axis span. Macria does not recalculate it.
    [JsonPropertyName("profileGeometryAnalysis")]
    public GeometryLabProfileGeometryAnalysisTransport? ProfileGeometryAnalysis { get; init; }

    // Schema 1.1 additions (absent in 1.0 JSON, so they default to null/empty).
    // Face, edge and solid IDs are analysis-local and only the localId is kept.
    [JsonPropertyName("sheetMetal")]
    public GeometryLabSheetMetalTransport? SheetMetal { get; init; }

    [JsonPropertyName("sheetMetalAnalyses")]
    public IReadOnlyList<GeometryLabSheetMetalTransport> SheetMetalAnalyses { get; init; } =
        Array.Empty<GeometryLabSheetMetalTransport>();

    [JsonPropertyName("holeFeatures")]
    public IReadOnlyList<GeometryLabHoleFeatureTransport> HoleFeatures { get; init; } =
        Array.Empty<GeometryLabHoleFeatureTransport>();

    // Schema 1.2 addition: distinct parts of the STEP product structure (an
    // assembly lists every part once with its instance count).
    [JsonPropertyName("parts")]
    public IReadOnlyList<GeometryLabPartTransport> Parts { get; init; } = Array.Empty<GeometryLabPartTransport>();

    // Per-solid base stock (processed profiles inside an assembly); the fields
    // above keep their single-solid meaning.
    [JsonPropertyName("baseStockProfiles")]
    public IReadOnlyList<GeometryLabSolidBaseStockTransport> BaseStockProfiles { get; init; } =
        Array.Empty<GeometryLabSolidBaseStockTransport>();
}

public sealed record GeometryLabSolidBaseStockTransport
{
    [JsonPropertyName("solidId")]
    public GeometryLabLocalIdTransport? SolidId { get; init; }

    [JsonPropertyName("baseStockProfile")]
    public GeometryLabBaseStockProfileTransport? BaseStockProfile { get; init; }

    [JsonPropertyName("modificationAnalysis")]
    public GeometryLabModificationAnalysisTransport? ModificationAnalysis { get; init; }
}

public sealed record GeometryLabPartTransport
{
    [JsonPropertyName("localId")]
    public int LocalId { get; init; }

    // STEP PRODUCT name as XDE reads it; productId/productName are the raw
    // STEP fields when the product could be traced (equal in 3DEXPERIENCE files).
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("productId")]
    public string? ProductId { get; init; }

    [JsonPropertyName("productName")]
    public string? ProductName { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("solidIds")]
    public IReadOnlyList<GeometryLabLocalIdTransport> SolidIds { get; init; } = Array.Empty<GeometryLabLocalIdTransport>();

    // Pure geometry: "Sheet", "Profile", "Other" or "ReviewRequired". Macria
    // settles a sheet/profile conflict with the CATIA sheet-metal feature.
    [JsonPropertyName("classification")]
    public string? Classification { get; init; }

    [JsonPropertyName("classificationReasons")]
    public IReadOnlyList<string> ClassificationReasons { get; init; } = Array.Empty<string>();

    // Engine 2026.10.5.1+: why, as a code (MotorSinifKodu), and whether a
    // recognizer found anything. Null in older outputs (MotorSinifKodu derives them).
    [JsonPropertyName("classificationCode")]
    public string? ClassificationCode { get; init; }

    [JsonPropertyName("recognitionEvidence")]
    public bool? RecognitionEvidence { get; init; }

    [JsonPropertyName("sheetCandidate")]
    public bool SheetCandidate { get; init; }

    [JsonPropertyName("profileCandidate")]
    public string? ProfileCandidate { get; init; }

    // Engine 2026.10.5.2+: a machined plate (pockets, steps, counterbores);
    // the DXF holds only the outline and the cut-through holes. Null in older outputs.
    [JsonPropertyName("machiningPresent")]
    public bool? MachiningPresent { get; init; }

    // Engine 2026.10.5.4+: Pocket, Engraving, Embossing.
    [JsonPropertyName("machiningKinds")]
    public IReadOnlyList<string> MachiningKinds { get; init; } = Array.Empty<string>();

    // File name the engine wrote with --dxf-klasor (part-<id>.dxf), or null.
    [JsonPropertyName("dxfFile")]
    public string? DxfFile { get; init; }

    // Same pattern without bend information (KESIM layer only), or null.
    [JsonPropertyName("dxfCutOnlyFile")]
    public string? DxfCutOnlyFile { get; init; }
}

public sealed record GeometryLabLocalIdTransport
{
    [JsonPropertyName("localId")]
    public int LocalId { get; init; }
}

public sealed record GeometryLabSheetMetalTransport
{
    [JsonPropertyName("solidId")]
    public GeometryLabLocalIdTransport? SolidId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; init; }

    [JsonPropertyName("thicknessMm")]
    public double? ThicknessMm { get; init; }

    [JsonPropertyName("closedSection")]
    public bool ClosedSection { get; init; }

    // Engine 2026.10.6.5+: a closed section's shape, "Box" or "Tube"; null in older outputs.
    [JsonPropertyName("closedSectionShape")]
    public string? ClosedSectionShape { get; init; }

    [JsonPropertyName("bends")]
    public IReadOnlyList<GeometryLabSheetBendTransport> Bends { get; init; } =
        Array.Empty<GeometryLabSheetBendTransport>();

    [JsonPropertyName("flatPattern")]
    public GeometryLabFlatPatternTransport? FlatPattern { get; init; }
}

public sealed record GeometryLabSheetBendTransport
{
    [JsonPropertyName("localId")]
    public int LocalId { get; init; }

    [JsonPropertyName("innerRadiusMm")]
    public double? InnerRadiusMm { get; init; }

    [JsonPropertyName("angleDegrees")]
    public double? AngleDegrees { get; init; }

    [JsonPropertyName("direction")]
    public string? Direction { get; init; }

    [JsonPropertyName("kFactor")]
    public double? KFactor { get; init; }

    [JsonPropertyName("allowanceMm")]
    public double? AllowanceMm { get; init; }
}

public sealed record GeometryLabFlatPatternTransport
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("referenceSkin")]
    public string? ReferenceSkin { get; init; }

    [JsonPropertyName("kFactorFormula")]
    public string? KFactorFormula { get; init; }

    [JsonPropertyName("widthMm")]
    public double? WidthMm { get; init; }

    [JsonPropertyName("heightMm")]
    public double? HeightMm { get; init; }

    // Engine 2026.10.5.4+: smallest rectangle around the pattern at any angle.
    [JsonPropertyName("minimumRectangle")]
    public GeometryLabFlatRectangleTransport? MinimumRectangle { get; init; }

    [JsonPropertyName("segments")]
    public IReadOnlyList<GeometryLabFlatSegmentTransport> Segments { get; init; } = Array.Empty<GeometryLabFlatSegmentTransport>();

    [JsonPropertyName("holes")]
    public IReadOnlyList<GeometryLabFlatHoleTransport> Holes { get; init; } =
        Array.Empty<GeometryLabFlatHoleTransport>();

    [JsonPropertyName("bendLines")]
    public IReadOnlyList<GeometryLabFlatBendLineTransport> BendLines { get; init; } =
        Array.Empty<GeometryLabFlatBendLineTransport>();

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; init; }
}

/// <summary>One edge of the flat pattern outline ("Line", "Arc", "Circle", "Polyline"); arcs and circles with their circle.</summary>
public sealed record GeometryLabFlatSegmentTransport
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    // "Outer" (the outline) or "Inner" (a cut-out).
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("center")]
    public GeometryLabFlatPointTransport? Center { get; init; }

    [JsonPropertyName("radiusMm")]
    public double? RadiusMm { get; init; }
}

public sealed record GeometryLabFlatPointTransport
{
    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }
}

public sealed record GeometryLabFlatRectangleTransport
{
    [JsonPropertyName("shortMm")]
    public double? ShortMm { get; init; }

    [JsonPropertyName("longMm")]
    public double? LongMm { get; init; }

    [JsonPropertyName("angleDegrees")]
    public double? AngleDegrees { get; init; }
}

public sealed record GeometryLabFlatHoleTransport
{
    [JsonPropertyName("holeFeatureId")]
    public GeometryLabLocalIdTransport? HoleFeatureId { get; init; }

    [JsonPropertyName("diameterMm")]
    public double? DiameterMm { get; init; }
}

public sealed record GeometryLabFlatBendLineTransport
{
    [JsonPropertyName("bendId")]
    public int BendId { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }
}

public sealed record GeometryLabHoleFeatureTransport
{
    [JsonPropertyName("localId")]
    public int LocalId { get; init; }

    [JsonPropertyName("solidId")]
    public GeometryLabLocalIdTransport? SolidId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("throughDiameterMm")]
    public double? ThroughDiameterMm { get; init; }

    [JsonPropertyName("headDiameterMm")]
    public double? HeadDiameterMm { get; init; }

    [JsonPropertyName("headDepthMm")]
    public double? HeadDepthMm { get; init; }

    [JsonPropertyName("headAngleDegrees")]
    public double? HeadAngleDegrees { get; init; }

    [JsonPropertyName("openingSide")]
    public string? OpeningSide { get; init; }

    [JsonPropertyName("onBend")]
    public bool OnBend { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    // Tapped hole (through diameter is an ISO metric thread minor diameter):
    // not drawn in the flat-pattern DXF; the warning says so.
    [JsonPropertyName("threadDesignation")]
    public string? ThreadDesignation { get; init; }

    [JsonPropertyName("warning")]
    public string? Warning { get; init; }
}

public sealed record GeometryLabSolidTransport
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed record GeometryLabProfileRecognitionTransport
{
    // Analysis-local solid this result belongs to; ties it to a part (schema 1.2).
    [JsonPropertyName("solidId")]
    public GeometryLabLocalIdTransport? SolidId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; init; }

    [JsonPropertyName("sectionRecognitionStatus")]
    public string? SectionRecognitionStatus { get; init; }

    [JsonPropertyName("lengthRecognitionStatus")]
    public string? LengthRecognitionStatus { get; init; }

    [JsonPropertyName("cutRecognitionStatus")]
    public string? CutRecognitionStatus { get; init; }

    [JsonPropertyName("profileType")]
    public string? ProfileType { get; init; }

    [JsonPropertyName("outerWidthMm")]
    public double? OuterWidthMm { get; init; }

    [JsonPropertyName("outerHeightMm")]
    public double? OuterHeightMm { get; init; }

    [JsonPropertyName("innerWidthMm")]
    public double? InnerWidthMm { get; init; }

    [JsonPropertyName("innerHeightMm")]
    public double? InnerHeightMm { get; init; }

    [JsonPropertyName("outerDiameterMm")]
    public double? OuterDiameterMm { get; init; }

    [JsonPropertyName("innerDiameterMm")]
    public double? InnerDiameterMm { get; init; }

    [JsonPropertyName("wallThicknessMm")]
    public double? WallThicknessMm { get; init; }

    [JsonPropertyName("endCutCandidates")]
    public IReadOnlyList<GeometryLabEndCutCandidateTransport> EndCutCandidates { get; init; } =
        Array.Empty<GeometryLabEndCutCandidateTransport>();

    // Raw, technical evidence is retained separately from the reporting summary.
    // It is intentionally not surfaced in the normal Macria table.
    [JsonPropertyName("lengthCandidates")]
    public GeometryLabLengthCandidatesTransport? LengthCandidates { get; init; }

    [JsonPropertyName("lengthSummary")]
    public GeometryLabLengthSummaryTransport? LengthSummary { get; init; }
}

public sealed record GeometryLabProfileGeometryAnalysisTransport
{
    [JsonPropertyName("axisDetectionStatus")]
    public string? AxisDetectionStatus { get; init; }

    [JsonPropertyName("axisCandidates")]
    public IReadOnlyList<GeometryLabProfileAxisCandidateTransport> AxisCandidates { get; init; } =
        Array.Empty<GeometryLabProfileAxisCandidateTransport>();
}

public sealed record GeometryLabProfileAxisCandidateTransport
{
    [JsonPropertyName("localId")]
    public int LocalId { get; init; }

    // Solid the candidate was found on; lets an assembly part use only its own
    // candidates. Missing in older engine outputs.
    [JsonPropertyName("solidId")]
    public GeometryLabLocalIdTransport? SolidId { get; init; }

    [JsonPropertyName("projectionSpanMm")]
    public double? ProjectionSpanMm { get; init; }

    [JsonPropertyName("reliable")]
    public bool Reliable { get; init; }
}

public sealed record GeometryLabEndCutCandidateTransport
{
    [JsonPropertyName("end")]
    public string? End { get; init; }

    [JsonPropertyName("measurementStatus")]
    public string? MeasurementStatus { get; init; }

    [JsonPropertyName("endFaceType")]
    public string? EndFaceType { get; init; }

    [JsonPropertyName("endPositionAlongAxisMm")]
    public double? EndPositionAlongAxisMm { get; init; }

    [JsonPropertyName("cutAngleDegrees")]
    public double? CutAngleDegrees { get; init; }

    [JsonPropertyName("angleConvention")]
    public string? AngleConvention { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed record GeometryLabLengthSummaryTransport
{
    [JsonPropertyName("measurementStatus")]
    public string? MeasurementStatus { get; init; }

    [JsonPropertyName("classification")]
    public string? Classification { get; init; }

    [JsonPropertyName("uniformLengthMm")]
    public double? UniformLengthMm { get; init; }

    [JsonPropertyName("shortLengthMm")]
    public double? ShortLengthMm { get; init; }

    [JsonPropertyName("centerLengthMm")]
    public double? CenterLengthMm { get; init; }

    [JsonPropertyName("longLengthMm")]
    public double? LongLengthMm { get; init; }
}

public sealed record GeometryLabLengthCandidatesTransport
{
    [JsonPropertyName("measurementStatus")]
    public string? MeasurementStatus { get; init; }

    [JsonPropertyName("axialCenterlineLengthMm")]
    public double? AxialCenterlineLengthMm { get; init; }

    [JsonPropertyName("outerContourMinimumLengthMm")]
    public double? OuterContourMinimumLengthMm { get; init; }

    [JsonPropertyName("outerContourMaximumLengthMm")]
    public double? OuterContourMaximumLengthMm { get; init; }

    [JsonPropertyName("innerContourMinimumLengthMm")]
    public double? InnerContourMinimumLengthMm { get; init; }

    [JsonPropertyName("innerContourMaximumLengthMm")]
    public double? InnerContourMaximumLengthMm { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// Supplemental base-stock conclusion. It is distinct from the ordinary profile
/// recognition transport because it does not supply a length or cut result.
/// </summary>
public sealed record GeometryLabBaseStockProfileTransport
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; init; }

    [JsonPropertyName("profileType")]
    public string? ProfileType { get; init; }

    [JsonPropertyName("axisCandidateId")]
    public int AxisCandidateId { get; init; }

    [JsonPropertyName("outerWidthMm")]
    public double? OuterWidthMm { get; init; }

    [JsonPropertyName("outerHeightMm")]
    public double? OuterHeightMm { get; init; }

    [JsonPropertyName("innerWidthMm")]
    public double? InnerWidthMm { get; init; }

    [JsonPropertyName("innerHeightMm")]
    public double? InnerHeightMm { get; init; }

    [JsonPropertyName("outerDiameterMm")]
    public double? OuterDiameterMm { get; init; }

    [JsonPropertyName("innerDiameterMm")]
    public double? InnerDiameterMm { get; init; }

    [JsonPropertyName("wallThicknessMm")]
    public double? WallThicknessMm { get; init; }

    // Kept for a future technical detail view; not rendered in the normal list.
    [JsonPropertyName("stableSectionRegions")]
    public IReadOnlyList<GeometryLabStableSectionRegionTransport> StableSectionRegions { get; init; } =
        Array.Empty<GeometryLabStableSectionRegionTransport>();
}

public sealed record GeometryLabStableSectionRegionTransport
{
    [JsonPropertyName("startProjectionMm")]
    public double? StartProjectionMm { get; init; }

    [JsonPropertyName("endProjectionMm")]
    public double? EndProjectionMm { get; init; }

    [JsonPropertyName("lengthMm")]
    public double? LengthMm { get; init; }

    [JsonPropertyName("validSampleCount")]
    public int? ValidSampleCount { get; init; }

    [JsonPropertyName("representativeSectionCount")]
    public int? RepresentativeSectionCount { get; init; }

    [JsonPropertyName("profileType")]
    public string? ProfileType { get; init; }
}

public sealed record GeometryLabModificationAnalysisTransport
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; init; }

    [JsonPropertyName("modifiedAxisIntervals")]
    public IReadOnlyList<GeometryLabModifiedAxisIntervalTransport> ModifiedAxisIntervals { get; init; } =
        Array.Empty<GeometryLabModifiedAxisIntervalTransport>();
}

public sealed record GeometryLabModifiedAxisIntervalTransport
{
    [JsonPropertyName("startProjectionMm")]
    public double? StartProjectionMm { get; init; }

    [JsonPropertyName("endProjectionMm")]
    public double? EndProjectionMm { get; init; }

    [JsonPropertyName("lengthMm")]
    public double? LengthMm { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
