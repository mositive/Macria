using System;
using System.IO;

namespace Macria;

/// <summary>AutoCAD binary DXF sentinel, compared as raw bytes (no text decoding, no line splitting).</summary>
internal static class DxfBinarySignature
{
    public static ReadOnlySpan<byte> Bytes => "AutoCAD Binary DXF\r\n\u001A\0"u8;

    public static int Length => Bytes.Length;

    public static bool Matches(ReadOnlySpan<byte> header) => header.StartsWith(Bytes);
}

/// <summary>Request result, content evaluation and, only when the content is accepted, the preview model.</summary>
internal sealed record DxfPreviewReadResult(PreviewResult Preview, PreviewContentCheckResult Content, DxfCizim? Model);

/// <summary>
/// Read-only DXF 2D preview adapter over DxfOkuyucu. PreviewCoordinator resolves the request and the
/// extension; this adapter evaluates the content. It never writes the source and never grants or denies
/// edit: a structurally valid DXF without entities stays editable through DxfEditOturumu.
/// Used by the Dosya Analiz Merkezi DXF/DWG preview panel (MainWindow.DxfDwgFiles).
/// </summary>
internal sealed class DxfPreviewAdapter
{
    private readonly PreviewCoordinator _coordinator;

    public DxfPreviewAdapter(PreviewCoordinator? coordinator = null) =>
        _coordinator = coordinator ?? new PreviewCoordinator();

    public DxfPreviewReadResult Read(PreviewRequest? request)
    {
        PreviewResult resolved = _coordinator.Resolve(request);
        if (!resolved.IsReady && resolved.Status != PreviewResultStatus.RequiresContentValidation)
            return NotChecked(resolved);
        if (resolved.ContentType != PreviewContentType.Dxf || resolved.Capability != PreviewCapability.Preview2D)
            return NotChecked(resolved with
            {
                Status = PreviewResultStatus.UnsupportedCapability,
                SupportLevel = PreviewSupportLevel.Unsupported,
                Message = "DXF önizleme yalnızca 2B gömülü veya büyük önizleme isteğini karşılar.",
                DiagnosticDetail = $"DXF adapter serves Dxf/Preview2D only; got {resolved.ContentType}/{resolved.Capability}."
            });

        string path = resolved.NormalizedPath!;
        try
        {
            if (DxfBinarySignature.Matches(ReadHeader(path)))
                return Rejected(resolved, PreviewContentCheckReason.BinaryDxf, "AutoCAD binary DXF signature found.");

            DxfCizim? model = DxfOkuyucu.Oku(path, out string? readerMessage);
            if (model == null)
            {
                if (!File.Exists(path)) return Missing(resolved);
                return Rejected(resolved, PreviewContentCheckReason.InvalidContent, "DxfOkuyucu returned no model: " + readerMessage);
            }
            if (!model.Bos)
                return new DxfPreviewReadResult(resolved, PreviewContentCheckResult.Accepted(), model);
            return model.KaynakBelge != null
                ? Rejected(resolved, PreviewContentCheckReason.NoDrawableEntities, "Structurally valid DXF without drawable entities.")
                : Rejected(resolved, PreviewContentCheckReason.InvalidContent, "No drawable entities and no DXF source records.");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Missing(resolved);
        }
        catch (Exception exception)
        {
            PreviewResult failed = resolved with
            {
                Status = PreviewResultStatus.Failed,
                Message = "DXF önizleme için dosya okunamadı.",
                DiagnosticDetail = exception.ToString()
            };
            return new DxfPreviewReadResult(failed, PreviewContentCheckResult.Failed(exception.Message), null);
        }
    }

    // Reads at most the signature length; shares the file so an open CAD application is not disturbed.
    private static byte[] ReadHeader(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[DxfBinarySignature.Length];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return buffer[..read];
    }

    private static DxfPreviewReadResult NotChecked(PreviewResult preview) =>
        new(preview, PreviewContentCheckResult.NotChecked, null);

    private static DxfPreviewReadResult Rejected(PreviewResult preview, PreviewContentCheckReason reason, string diagnostic) =>
        new(preview, PreviewContentCheckResult.Rejected(reason, diagnostic), null);

    private static DxfPreviewReadResult Missing(PreviewResult resolved) => NotChecked(resolved with
    {
        Status = PreviewResultStatus.MissingFile,
        SupportLevel = PreviewSupportLevel.Unsupported,
        Message = "Önizleme kaynak dosyası bulunamadı.",
        DiagnosticDetail = "File disappeared after request resolution."
    });
}
