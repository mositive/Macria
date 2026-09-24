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
}

public sealed record GeometryLabSolidTransport
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed record GeometryLabProfileRecognitionTransport
{
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
