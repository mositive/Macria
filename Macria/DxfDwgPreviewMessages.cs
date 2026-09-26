namespace Macria;

/// <summary>
/// User-facing messages of the Dosya Analiz Merkezi DXF/DWG preview panel. Technical diagnostics
/// never go into these texts; they stay in the adapter result and the console log.
/// </summary>
internal static class DxfDwgPreviewMessages
{
    public const string DwgUnsupported = "DWG önizleme henüz desteklenmiyor. Dosyayı Aç komutunu kullanabilirsiniz.";
    public const string MissingFile = "Seçili DXF dosyası bulunamadı.";
    public const string BinaryDxf = "İkili (binary) DXF önizlenemiyor.";
    public const string NothingDrawable = "Dosyada çizilebilir bir nesne bulunamadı.";
    public const string ReadError = "DXF dosyası okunamadı. Dosyaya erişimi ve dosyanın geçerliliğini kontrol edin.";
    public const string RenderFailed = "DXF önizleme oluşturulamadı.";

    /// <summary>Null when the model should be drawn; otherwise the message to show instead.</summary>
    public static string? For(DxfPreviewReadResult result)
    {
        if (result.Content.Status == PreviewContentCheckStatus.Failed) return ReadError;
        if (result.Content.Status == PreviewContentCheckStatus.Rejected)
            return result.Content.Reason == PreviewContentCheckReason.BinaryDxf ? BinaryDxf : NothingDrawable;
        if (result.Preview.IsReady && result.Content.IsAccepted && result.Model != null) return null;

        // Request-level outcomes keep the panel's earlier coordinator messages.
        if (result.Preview.ContentType == PreviewContentType.Dwg) return DwgUnsupported;
        if (result.Preview.Status == PreviewResultStatus.MissingFile) return MissingFile;
        return result.Preview.IsReady ? NothingDrawable : result.Preview.Message;
    }

    /// <summary>Technical detail for the log only.</summary>
    public static string? Diagnostic(DxfPreviewReadResult result) =>
        result.Content.DiagnosticMessage ?? result.Preview.DiagnosticDetail;
}
