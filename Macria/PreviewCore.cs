using System;
using System.IO;

namespace Macria;

public enum PreviewContentType { Unknown, Dxf, Dwg, Step }
public enum PreviewCapability { Preview2D, Preview3D, Edit }
public enum PreviewPresentation { Embedded, Large }
public enum PreviewSupportLevel { Unsupported, Supported, RequiresContentValidation }
public enum PreviewResultStatus
{
    Ready, RequiresContentValidation, UnsupportedCapability, UnsupportedContent,
    MissingFile, InvalidRequest, Failed
}

public sealed record PreviewRequest
{
    public string? SourcePath { get; init; }
    public PreviewCapability Capability { get; init; }
    public PreviewPresentation? Presentation { get; init; }
    public string? SourceContext { get; init; }
}

public sealed record PreviewResult
{
    public required PreviewResultStatus Status { get; init; }
    public PreviewContentType ContentType { get; init; }
    public PreviewCapability? Capability { get; init; }
    public PreviewPresentation? Presentation { get; init; }
    public PreviewSupportLevel SupportLevel { get; init; }
    public string? NormalizedPath { get; init; }
    public required string Message { get; init; }
    public string? DiagnosticDetail { get; init; }
    public bool IsReady => Status == PreviewResultStatus.Ready;
}

public enum PreviewContentCheckStatus { NotChecked, Accepted, Rejected, Failed }
public enum PreviewContentCheckReason { None, BinaryDxf, NoDrawableEntities, InvalidContent, ReadError }

/// <summary>
/// Outcome of inspecting file content after the request itself was resolved (PreviewResultStatus).
/// It only says whether the content can be drawn; it does not grant or deny DXF edit, which stays
/// with DxfEditOturumu (a structurally valid DXF without entities is rejected here but stays editable).
/// Instances come only from the factories, so Status and Reason cannot disagree.
/// </summary>
public sealed record PreviewContentCheckResult
{
    private PreviewContentCheckResult(PreviewContentCheckStatus status, PreviewContentCheckReason reason,
        string? diagnosticMessage)
    {
        Status = status;
        Reason = reason;
        DiagnosticMessage = diagnosticMessage;
    }

    public PreviewContentCheckStatus Status { get; }
    public PreviewContentCheckReason Reason { get; }
    public string? DiagnosticMessage { get; }
    public bool IsAccepted => Status == PreviewContentCheckStatus.Accepted;

    public static PreviewContentCheckResult NotChecked { get; } =
        new(PreviewContentCheckStatus.NotChecked, PreviewContentCheckReason.None, null);

    public static PreviewContentCheckResult Accepted(string? diagnosticMessage = null) =>
        new(PreviewContentCheckStatus.Accepted, PreviewContentCheckReason.None, diagnosticMessage);

    /// <summary>The content was read and is not drawable: BinaryDxf, NoDrawableEntities or InvalidContent.</summary>
    public static PreviewContentCheckResult Rejected(PreviewContentCheckReason reason, string? diagnosticMessage = null) =>
        reason is PreviewContentCheckReason.BinaryDxf or PreviewContentCheckReason.NoDrawableEntities
            or PreviewContentCheckReason.InvalidContent
            ? new(PreviewContentCheckStatus.Rejected, reason, diagnosticMessage)
            : throw new ArgumentOutOfRangeException(nameof(reason), reason, "Rejected requires a content rejection reason.");

    /// <summary>The content could not be read at all.</summary>
    public static PreviewContentCheckResult Failed(string? diagnosticMessage = null) =>
        new(PreviewContentCheckStatus.Failed, PreviewContentCheckReason.ReadError, diagnosticMessage);
}

public static class PreviewContentTypeResolver
{
    public static PreviewContentType Resolve(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return PreviewContentType.Unknown;
        string extension;
        try { extension = Path.GetExtension(sourcePath); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return PreviewContentType.Unknown; }
        return extension.ToUpperInvariant() switch
        {
            ".DXF" => PreviewContentType.Dxf,
            ".DWG" => PreviewContentType.Dwg,
            ".STEP" or ".STP" => PreviewContentType.Step,
            _ => PreviewContentType.Unknown
        };
    }
}

public static class PreviewCapabilityResolver
{
    public static PreviewSupportLevel Resolve(PreviewContentType contentType, PreviewCapability capability) =>
        (contentType, capability) switch
        {
            (PreviewContentType.Dxf, PreviewCapability.Preview2D) => PreviewSupportLevel.Supported,
            (PreviewContentType.Dxf, PreviewCapability.Edit) => PreviewSupportLevel.RequiresContentValidation,
            (PreviewContentType.Step, PreviewCapability.Preview3D) => PreviewSupportLevel.Supported,
            _ => PreviewSupportLevel.Unsupported
        };

    public static bool SupportsPresentation(PreviewContentType contentType, PreviewCapability capability,
        PreviewPresentation presentation) =>
        capability is PreviewCapability.Preview2D or PreviewCapability.Preview3D
        && Resolve(contentType, capability) == PreviewSupportLevel.Supported
        && Enum.IsDefined(presentation);
}

/// <summary>Resolves requests without creating controls, opening viewers, or starting geometry analysis.</summary>
public sealed class PreviewCoordinator
{
    public PreviewResult Resolve(PreviewRequest? request)
    {
        if (request == null) return Invalid("\u00d6nizleme iste\u011fi bulunamad\u0131.", "Request is null.");
        if (string.IsNullOrWhiteSpace(request.SourcePath))
            return Invalid("\u00d6nizleme kaynak yolu bo\u015f olamaz.", "SourcePath is empty.", request);
        if (!Enum.IsDefined(request.Capability))
            return Invalid("\u0130stenen \u00f6nizleme yetene\u011fi ge\u00e7ersiz.", "Capability is undefined.", request);
        bool isPreview = request.Capability is PreviewCapability.Preview2D or PreviewCapability.Preview3D;
        if (isPreview && request.Presentation == null)
            return Invalid("\u00d6nizleme sunum bi\u00e7imi belirtilmelidir.", "Presentation is required.", request);
        if (request.Presentation is PreviewPresentation presentation && !Enum.IsDefined(presentation))
            return Invalid("\u00d6nizleme sunum bi\u00e7imi ge\u00e7ersiz.", "Presentation is undefined.", request);
        if (request.Capability == PreviewCapability.Edit && request.Presentation != null)
            return Invalid("D\u00fczenleme iste\u011fi sunum bi\u00e7imi kullanmaz.", "Edit is separate from presentation.", request);
        return ResolvePath(request);
    }

    private static PreviewResult ResolvePath(PreviewRequest request)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(request.SourcePath!); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return Invalid("\u00d6nizleme kaynak yolu ge\u00e7ersiz.", exception.Message, request); }
        catch (Exception exception)
        { return Failed(request, null, "Kaynak yolu \u00e7\u00f6z\u00fcmlenemedi.", exception); }

        PreviewContentType contentType = PreviewContentTypeResolver.Resolve(fullPath);
        if (contentType == PreviewContentType.Unknown)
            return Result(request, PreviewResultStatus.UnsupportedContent, contentType, PreviewSupportLevel.Unsupported,
                fullPath, "Bu dosya t\u00fcr\u00fc \u00f6nizleme i\u00e7in desteklenmiyor.",
                $"Unsupported extension: '{Path.GetExtension(fullPath)}'.");
        if (!File.Exists(fullPath))
            return Result(request, PreviewResultStatus.MissingFile, contentType, PreviewSupportLevel.Unsupported,
                fullPath, "\u00d6nizleme kaynak dosyas\u0131 bulunamad\u0131.", "File.Exists returned false.");

        PreviewSupportLevel support = PreviewCapabilityResolver.Resolve(contentType, request.Capability);
        if (support == PreviewSupportLevel.Unsupported)
            return Result(request, PreviewResultStatus.UnsupportedCapability, contentType, support, fullPath,
                "Bu dosya t\u00fcr\u00fc istenen \u00f6nizleme yetene\u011fini desteklemiyor.",
                $"{contentType} does not support {request.Capability}.");
        if (support == PreviewSupportLevel.RequiresContentValidation)
            return Result(request, PreviewResultStatus.RequiresContentValidation, contentType, support, fullPath,
                "D\u00fczenleme deste\u011fi dosya i\u00e7eri\u011fi do\u011fruland\u0131ktan sonra belirlenebilir.",
                "DXF editability depends on DxfOkuyucu/DxfEditOturumu validation.");
        return Result(request, PreviewResultStatus.Ready, contentType, support, fullPath,
            "\u00d6nizleme iste\u011fi destekleniyor.", null);
    }

    private static PreviewResult Invalid(string message, string detail, PreviewRequest? request = null) => new()
    {
        Status = PreviewResultStatus.InvalidRequest,
        ContentType = PreviewContentType.Unknown,
        Capability = request?.Capability,
        Presentation = request?.Presentation,
        SupportLevel = PreviewSupportLevel.Unsupported,
        Message = message,
        DiagnosticDetail = detail
    };

    private static PreviewResult Failed(PreviewRequest request, string? path, string message, Exception exception) =>
        Result(request, PreviewResultStatus.Failed, PreviewContentType.Unknown,
            PreviewSupportLevel.Unsupported, path, message, exception.ToString());

    private static PreviewResult Result(PreviewRequest request, PreviewResultStatus status,
        PreviewContentType contentType, PreviewSupportLevel support, string? path, string message, string? detail) => new()
    {
        Status = status,
        ContentType = contentType,
        Capability = request.Capability,
        Presentation = request.Presentation,
        SupportLevel = support,
        NormalizedPath = path,
        Message = message,
        DiagnosticDetail = detail
    };
}
