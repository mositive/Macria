using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;

namespace Macria;

public partial class MainWindow
{
    private readonly GeometryLabTemporaryStepExporter _geometryLabTemporaryStepExporter = new();
    private CancellationTokenSource? _geometryLabTestCts;

    // Invoked from the UI thread. It retains the established direct CATIA export attempts,
    // then uses the GeometryLab-only Export panel fallback when those attempts are unavailable.
    private Task<GeometryLabTemporaryStepExportResult> GeometryLabGeciciStepUretAsync(
        ProfilRow row,
        CancellationToken cancellationToken = default) =>
        _geometryLabTemporaryStepExporter.ExportAsync(
            row?.RepRef,
            async (repRef, outputPath, token) =>
            {
                if (token.IsCancellationRequested || _stopRequested) return false;
                return await GeometryLabGeciciStepExportEt(repRef, outputPath, token);
            },
            cancellationToken);

    // GeometryLab-only fallback. The user-facing "Seçiliyi STEP Kaydet" path remains
    // on ProfilStepExportEt and therefore retains its existing save-dialog behavior.
    private async Task<bool> GeometryLabGeciciStepExportEt(
        object repRef,
        string fullPath,
        CancellationToken cancellationToken)
    {
        dynamic catia = _catia;
        bool editorOpened = false;
        try
        {
            if (cancellationToken.IsCancellationRequested || _stopRequested) return false;
            dynamic service = catia.ActiveEditor.GetService("PLMOpenService");
            object? newEditor = null;
            service.PLMOpenInNewWindow(repRef, ref newEditor);
            editorOpened = true;
            await Task.Delay(2500, cancellationToken);

            object? activeObject = null;
            object? activeDocument = null;
            try { activeObject = catia.ActiveEditor.ActiveObject; } catch { }
            try { activeDocument = catia.ActiveDocument; } catch { }
            OnayIzleyiciBaslat();

            foreach ((string name, object? candidate) in new[]
                     {
                         ("ActiveDocument", activeDocument),
                         ("Yeni Editor", newEditor),
                         ("ActiveObject", activeObject)
                     })
            {
                if (candidate == null || cancellationToken.IsCancellationRequested || _stopRequested) continue;
                if (!ProfilExportDataDene(candidate, fullPath, out string reason))
                {
                    LogInfo("GeometryLab STEP — " + name + ".ExportData kullanılamadı: " + reason);
                    continue;
                }
                if (await ProfilStepDosyasiniBekle(fullPath, 30000)) return true;
                LogInfo("GeometryLab STEP — " + name + ".ExportData çıktı üretmedi.");
            }

            return await GeometryLabExportPaneliniDene(catia, fullPath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            LogError("GeometryLab geçici STEP export hatası: " + exception.Message);
            return false;
        }
        finally
        {
            OnayIzleyiciDurdur();
            if (editorOpened)
            {
                try { catia.ActiveWindow.Close(); } catch { }
                await WaitForAssembly(15000);
                await Task.Delay(800);
            }
        }
    }

    private async Task<bool> GeometryLabExportPaneliniDene(
        dynamic catia,
        string fullPath,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        AutomationElement? panel = null;
        try
        {
            string workspace = Path.GetDirectoryName(fullPath) ?? "";
            string stem = Path.GetFileNameWithoutExtension(fullPath);
            if (!Guid.TryParse(Path.GetFileName(workspace), out _) || !string.Equals(stem, "input", StringComparison.Ordinal))
            {
                LogError("GeometryLab Export panel hedefi uygulama sahipli GUID çalışma alanı değil.");
                return false;
            }

            IntPtr catiaWindow = PencereAraclari.AnaPencere();
            if (catiaWindow == IntPtr.Zero)
            {
                LogError("GeometryLab Export paneli — CATIA ana penceresi bulunamadı.");
                return false;
            }

            ForceForeground(catiaWindow);
            catia.StartCommand("Export");
            for (int elapsed = 0; elapsed < 8000 && !cancellationToken.IsCancellationRequested && !_stopRequested; elapsed += 250)
            {
                await Task.Delay(250, cancellationToken);
                panel = GeometryLabExportPanelAutomation.FindExportPanel(catiaWindow, diagnostics);
                if (panel != null) break;
            }

            if (panel == null)
            {
                GeometryLabPanelTanisiniYaz("Export panel bulunamadı", diagnostics);
                return false;
            }

            GeometryLabExportPanelSetupResult setup =
                GeometryLabExportPanelAutomation.Configure(panel, workspace, stem);
            foreach (string item in setup.Diagnostics) diagnostics.Add(item);
            if (!setup.IsSuccess || cancellationToken.IsCancellationRequested || _stopRequested)
            {
                GeometryLabExportPanelAutomation.TryCancel(panel, diagnostics);
                GeometryLabPanelTanisiniYaz(setup.Message.Length == 0 ? "Export panel doğrulanamadı" : setup.Message, diagnostics);
                return false;
            }

            if (!GeometryLabExportPanelAutomation.TryInvokeOk(panel, diagnostics))
            {
                GeometryLabExportPanelAutomation.TryCancel(panel, diagnostics);
                GeometryLabPanelTanisiniYaz("Export panel OK çağrısı başarısız", diagnostics);
                return false;
            }

            bool created = await ProfilStepDosyasiniBekle(fullPath, 30000);
            if (!created) GeometryLabPanelTanisiniYaz("Export panel sonrası input.stp oluşmadı", diagnostics);
            return created;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            GeometryLabExportPanelAutomation.TryCancel(panel, diagnostics);
            GeometryLabPanelTanisiniYaz("Export panel işlemi iptal edildi", diagnostics);
            return false;
        }
        catch (Exception exception)
        {
            GeometryLabExportPanelAutomation.TryCancel(panel, diagnostics);
            diagnostics.Add("Unhandled panel exception: " + exception.Message);
            GeometryLabPanelTanisiniYaz("Export panel işlemi başarısız", diagnostics);
            return false;
        }
    }

    private void GeometryLabPanelTanisiniYaz(string title, IEnumerable<string> diagnostics)
    {
        LogError("GeometryLab Export panel — " + title + ".");
        foreach (string item in diagnostics) LogInfo("GeometryLab Export panel tanı: " + item);
    }

    private async void btnGeometryLabTest_Click(object sender, RoutedEventArgs e)
    {
        if (_profilIslemde || _exporting) return;
        if (!(gridProfil.SelectedItem is ProfilRow row))
        {
            MessageBox.Show(this, "Önce Kutu Profil tablosundan bir satır seçin.",
                "GeometryLab ile Test Et", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (row.RepRef == null)
        {
            MessageBox.Show(this, "Seçili satırın CATIA parça referansı yok. Demo satırlarında gerçek test yapılamaz.",
                "GeometryLab ile Test Et", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        GeometryLabEngineLocation engine = GeometryLabEngineLocator.Locate();
        if (!engine.IsAvailable)
        {
            LogError("GeometryEngine bulunamadı. Beklenen yol: " + engine.ExpectedPackagedPath +
                " Ayrıntı: " + engine.Detail);
            MessageBox.Show(this,
                "Geometry analiz motoru bulunamadı veya eksik kuruldu. Macria kurulumunu onarın ya da yeniden kurun.",
                "GeometryLab ile Test Et", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _catia = GetCatia() ?? _catia;
        if (_catia == null)
        {
            MessageBox.Show(this, "CATIA bağlantısı kurulamadı; geçici STEP üretilemedi.",
                "GeometryLab ile Test Et", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        GeometryLabTemporaryStepWorkspace? workspace = null;
        GeometryLabProcessAdapterResult? analysisResult = null;
        string? failure = null;
        bool cancelled = false;
        _profilIslemde = true;
        _stopRequested = false;
        _geometryLabTestCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _geometryLabTestCts.Token;
        ProfilButonlariniGuncelle();
        ShowPipStart(row.ProductName, "GeometryLab Test");
        LogInfo("GeometryLab testi başladı: " + row.ProductName);

        try
        {
            SetExporting(true);
            GeometryLabTemporaryStepExportResult exportResult =
                await GeometryLabGeciciStepUretAsync(row, cancellationToken);
            if (!exportResult.IsSuccess)
            {
                cancelled = exportResult.Status == GeometryLabTemporaryStepExportStatus.Cancelled;
                failure = exportResult.Message ?? "Geçici STEP üretilemedi.";
            }
            else
            {
                workspace = exportResult.Workspace;
                if (workspace == null)
                {
                    failure = "Geçici STEP çalışma alanı alınamadı.";
                }
                else if (_stopRequested || cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                }
                else
                {
                    EnsurePip("GeometryLab Test").SetState(ExportPipWindow.PipState.Running, "GeometryLab analiz ediyor...");
                    var adapter = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
                    {
                        EngineExecutablePath = engine.ExecutablePath!,
                        Timeout = Ayarlar.GeometryLabZamanAsimi(),
                        PartTimeLimitSeconds = Ayarlar.ParcaSureSiniriSaniye,
                        ThreadCount = Ayarlar.MotorIsParcacigi
                    });
                    analysisResult = await adapter.AnalyzeAsync(workspace.StepFilePath, cancellationToken);
                    cancelled = analysisResult.Status == GeometryLabProcessAdapterStatus.Cancelled ||
                                _stopRequested || cancellationToken.IsCancellationRequested;
                    if (!analysisResult.IsSuccess && !cancelled)
                        failure = analysisResult.Message ?? "GeometryLab analizi tamamlanamadı.";
                }
            }
        }
        catch (Exception exception)
        {
            failure = "GeometryLab test hatası: " + exception.Message;
            LogError(failure);
        }
        finally
        {
            SetExporting(false);
            bool workspaceCleaned = workspace?.TryCleanup() ?? true;
            if (!workspaceCleaned)
                LogError("GeometryLab geçici STEP klasörü temizlenemedi.");
            _geometryLabTestCts?.Dispose();
            _geometryLabTestCts = null;
            _profilIslemde = false;
            ProfilButonlariniGuncelle();
            IslemSuresiniYaz("GeometryLab Test", stopwatch);
        }

        if (cancelled)
        {
            LogInfo("GeometryLab testi kullanıcı tarafından durduruldu.");
            await FinishPip(ExportPipWindow.PipState.Stopped, row.ProductName);
            MessageBox.Show(this, "GeometryLab testi durduruldu. Geçici çalışma alanı temizlendi.",
                "GeometryLab ile Test Et", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (analysisResult == null || !analysisResult.IsSuccess)
        {
            string message = failure ?? "GeometryLab analizi başarısız oldu.";
            LogError("GeometryLab testi başarısız: " + message);
            await FinishPip(ExportPipWindow.PipState.Error, row.ProductName);
            MessageBox.Show(this, message, "GeometryLab ile Test Et", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string summary = GeometryLabSonucOzeti(analysisResult);
        LogSuccess("GeometryLab testi tamamlandı: " + row.ProductName);
        await FinishPip(ExportPipWindow.PipState.Done, row.ProductName);
        MessageBox.Show(this, summary, "GeometryLab Test Sonucu", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string GeometryLabSonucOzeti(GeometryLabProcessAdapterResult result)
    {
        var text = new StringBuilder();
        GeometryLabAnalysisTransport? analysis = result.Analysis;
        text.AppendLine("Analiz durumu: " + (analysis?.Status ?? result.Status.ToString()));

        if (result.HasMultipleProfileResults)
        {
            text.AppendLine();
            text.Append("Birden fazla solid sonucu döndü; bu test ekranı otomatik sonuç seçmez.");
            WarningsAndErrors(text, analysis);
            return text.ToString();
        }

        GeometryLabProfileRecognitionTransport? profile = analysis?.ProfileRecognition;
        if (profile == null)
        {
            text.AppendLine("Profil sonucu: yok");
            WarningsAndErrors(text, analysis);
            return text.ToString();
        }

        text.AppendLine("Profil türü: " + ValueOrUnknown(profile.ProfileType));
        AppendDimensions(text, "Dış ölçü", profile.OuterWidthMm, profile.OuterHeightMm, profile.OuterDiameterMm);
        AppendDimensions(text, "İç ölçü", profile.InnerWidthMm, profile.InnerHeightMm, profile.InnerDiameterMm);
        if (profile.WallThicknessMm is double thickness)
            text.AppendLine("Et kalınlığı: " + Mm(thickness));

        foreach (GeometryLabEndCutCandidateTransport cut in profile.EndCutCandidates)
        {
            text.AppendLine("Uç kesim (" + ValueOrUnknown(cut.End) + "): " +
                            (cut.CutAngleDegrees is double angle ? Degrees(angle) : "bilinmiyor"));
        }

        GeometryLabLengthSummaryTransport? length = profile.LengthSummary;
        if (length?.Classification == "Uniform" && length.UniformLengthMm is double uniform)
            text.AppendLine("Boy: " + Mm(uniform));
        else if (length?.Classification == "VariableByCut")
        {
            text.AppendLine("Boy (kısa / merkez / uzun): " +
                            ValueMm(length.ShortLengthMm) + " / " +
                            ValueMm(length.CenterLengthMm) + " / " +
                            ValueMm(length.LongLengthMm));
        }
        else if (!string.IsNullOrWhiteSpace(length?.Classification))
            text.AppendLine("Boy özeti: " + length.Classification);

        WarningsAndErrors(text, analysis);
        return text.ToString();
    }

    private static void AppendDimensions(StringBuilder text, string label, double? width, double? height, double? diameter)
    {
        if (width is double w && height is double h)
            text.AppendLine(label + ": " + Mm(w) + " × " + Mm(h));
        else if (diameter is double d)
            text.AppendLine(label + ": Ø" + Mm(d));
    }

    private static void WarningsAndErrors(StringBuilder text, GeometryLabAnalysisTransport? analysis)
    {
        foreach (string warning in analysis?.Warnings ?? Array.Empty<string>())
            text.AppendLine("Uyarı: " + warning);
        foreach (string error in analysis?.Errors ?? Array.Empty<string>())
            text.AppendLine("Neden: " + error);
    }

    private static string ValueOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "bilinmiyor" : value;

    private static string Mm(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("tr-TR")) + " mm";
    private static string ValueMm(double? value) => value is double number ? Mm(number) : "bilinmiyor";
    private static string Degrees(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("tr-TR")) + "°";
}
