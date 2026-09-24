using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        private SingleReferenceColorRestoreState? _singleReferenceColorRestoreState;

        private async void TekReferansPartBodyRenkTesti_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_singleReferenceColorRestoreState != null)
                {
                    throw new InvalidOperationException(
                        "Önceki tek referans renk testi için geri yükleme kaydı bekliyor. " +
                        "Yeni testten önce 'Tek Referans Test Rengini Geri Yükle' komutunu çalıştırın.");
                }

                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic editor = ((dynamic)catiaObject).ActiveEditor;
                object? root = editor.ActiveObject;
                if (root is not object activeRoot)
                    throw new InvalidOperationException("Aktif CATIA montaj kökü alınamadı.");
                if (!HasOccurrences((dynamic)activeRoot))
                    throw new InvalidOperationException(
                        "Aktif CATIA nesnesi occurrence içeren bir Physical Product montajı değil.");

                dynamic selection = editor.Selection;
                List<object> previousSelection = SecimiSakla(selection);
                try
                {
                    if (previousSelection.Count != 1)
                        throw new InvalidOperationException("CATIA'da tam olarak bir yaprak parça occurrence seçin.");

                    object selectedOccurrence = previousSelection[0];
                    string selectedType = ComProbe.TipAdi(selectedOccurrence);
                    if (selectedType.IndexOf("Occurrence", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        throw new InvalidOperationException(
                            "Seçilen nesne occurrence olarak doğrulanamadı (COM türü=" + selectedType + ").");
                    }

                    if (AkilliRenklendirmeAltOccurrenceSayisi((dynamic)selectedOccurrence) != 0)
                        throw new InvalidOperationException("Seçilen occurrence yaprak parça değildir.");

                    var service = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
                    CatiaColorTargetResult occurrenceResult = service.ResolveOccurrence(selectedOccurrence);
                    if (!occurrenceResult.Success)
                        throw new InvalidOperationException(occurrenceResult.Error);

                    string? referenceKey = AkilliRenklendirmeReferansAnahtari(selectedOccurrence);
                    if (string.IsNullOrWhiteSpace(referenceKey))
                        throw new InvalidOperationException("Seçilen occurrence için PLM referans anahtarı çözümlenemedi.");

                    List<object> leaves = await AkilliRenklendirmeYapraklariniTopla((dynamic)activeRoot);
                    int matchingOccurrenceCount = 0;
                    foreach (object leaf in leaves)
                    {
                        string? leafReferenceKey;
                        try { leafReferenceKey = AkilliRenklendirmeReferansAnahtari(leaf); }
                        catch { continue; }
                        if (string.Equals(referenceKey, leafReferenceKey, StringComparison.Ordinal))
                            matchingOccurrenceCount++;
                    }

                    if (matchingOccurrenceCount <= 0)
                        throw new InvalidOperationException("Seçilen referans aktif montajın yaprak occurrence listesinde bulunamadı.");

                    PartBodyDiscoveryResult discovery = ResolveMountedPartBody(service, selectedOccurrence);
                    if (!discovery.Success || discovery.Target == null)
                        throw new InvalidOperationException("PartBody hedefi çözümlenemedi: " + discovery.Error);

                    CatiaColorTarget target = discovery.Target;
                    if (target.Identity.TargetType != CatiaColorTargetType.PartBody ||
                        target.Identity.ComType.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        throw new InvalidOperationException(
                            "Çözülen hedef gerçek PartBody olarak doğrulanamadı (COM türü=" +
                            target.Identity.ComType + ").");
                    }

                    if (!string.IsNullOrWhiteSpace(target.Identity.ReferenceKey) &&
                        !string.Equals(referenceKey, target.Identity.ReferenceKey, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Çözülen PartBody farklı bir referans kimliği bildirdi: " + target.Identity.ReferenceKey);
                    }

                    CatiaColorOperationResult readResult = service.TryReadRgb(selection, target);
                    if (!readResult.Success || !readResult.Color.HasValue)
                    {
                        string reason = readResult.Exception == null
                            ? readResult.Error
                            : RenkTestHataAyrintisi("VisProperties.GetRealColor", readResult.Exception);
                        LogError("Tek referans PartBody renk testi — RGB okunamadı; renk yazılmadı. " + reason);
                        return;
                    }

                    CatiaColorRgb previousRgb = readResult.Color.Value;
                    CatiaColorInheritanceOperationResult inheritanceResult =
                        service.TryReadRealColorInheritance(selection, target);
                    if (!TryGetDefinedColorInheritance(
                            inheritanceResult,
                            out int previousColorInheritance,
                            out string inheritanceDetail))
                    {
                        LogError(
                            "Tek referans PartBody renk testi — gerçek renk kalıtımı " +
                            "güvenilir okunamadı; renk yazılmadı. " + inheritanceDetail);
                        return;
                    }

                    AkilliRenk testColor = SelectTemporaryPartBodyColor(previousRgb);
                    string referenceTitle = ReadReferenceTitle(selectedOccurrence);
                    string catiaSessionKey = CatiaColorTargetService.GetSessionObjectKey(catiaObject);
                    string editorSessionKey = CatiaColorTargetService.GetSessionObjectKey((object)editor);
                    LogInfo(
                        "Tek referans PartBody renk testi — mevcut RGB okundu" +
                        " | Reference Title=" + referenceTitle +
                        " | referans=" + referenceKey +
                        " | eş occurrence=" + matchingOccurrenceCount +
                        " | COM türü=" + target.Identity.ComType +
                        " | RGB=" + RgbText(previousRgb) +
                        " | durum=" + previousRgb.Status +
                        " | gerçek renk kalıtımı=" + previousColorInheritance +
                        " | zincir=" + discovery.AccessChain);

                    string confirmation =
                        "Yalnız seçilen yaprak occurrence'ın ortak PartBody hedefi renklendirilecek.\n\n" +
                        "Reference Title: " + referenceTitle + "\n" +
                        "Eş occurrence: " + matchingOccurrenceCount + "\n" +
                        "Önceki RGB: " + RgbText(previousRgb) + "\n" +
                        "Önceki renk kalıtımı: " + previousColorInheritance + "\n" +
                        "Test RGB: " + testColor.Kirmizi + ", " + testColor.Yesil + ", " + testColor.Mavi + "\n\n" +
                        "Aynı referansı paylaşan occurrence'ların görsel etkisini CATIA'da gözlemleyin. " +
                        "Önceki RGB yalnız aynı CATIA oturumu ve aynı doğrulanmış PartBody hedefinde geri yüklenebilir.";

                    if (!OnayWindow.Sor(
                            this,
                            "Tek referansta PartBody renk testi",
                            confirmation,
                            "Test rengini uygula"))
                    {
                        LogInfo("Tek referans PartBody renk testi kullanıcı tarafından iptal edildi; renk yazılmadı.");
                        return;
                    }

                    CatiaColorOperationResult writeResult = service.TryApplyRgb(
                        selection,
                        target,
                        testColor.Kirmizi,
                        testColor.Yesil,
                        testColor.Mavi);
                    if (!writeResult.Success)
                    {
                        string reason = writeResult.Exception == null
                            ? writeResult.Error
                            : RenkTestHataAyrintisi("VisProperties.SetRealColor", writeResult.Exception);
                        LogError("Tek referans PartBody renk testi — test RGB yazılamadı. " + reason);
                        return;
                    }

                    // SetRealColor başarılı döndükten hemen sonra, geri okuma başarısız olsa bile eski RGB korunur.
                    _singleReferenceColorRestoreState = new SingleReferenceColorRestoreState(
                        catiaSessionKey,
                        editorSessionKey,
                        referenceKey,
                        target.Identity.SessionObjectKey,
                        selectedOccurrence,
                        previousRgb,
                        testColor,
                        matchingOccurrenceCount,
                        referenceTitle);

                    CatiaColorOperationResult verifyResult = service.TryReadRgb(selection, target);
                    if (!TryVerifyRgb(verifyResult, testColor.Kirmizi, testColor.Yesil, testColor.Mavi, out string verifyDetail))
                    {
                        LogError(
                            "Tek referans PartBody renk testi — SetRealColor tamamlandı fakat geri okuma doğrulanamadı. " +
                            verifyDetail + " Geri yükleme kaydı korunuyor.");
                        return;
                    }

                    LogSuccess(
                        "Tek referans PartBody test rengi doğrulandı" +
                        " | Reference Title=" + referenceTitle +
                        " | referans=" + referenceKey +
                        " | eş occurrence=" + matchingOccurrenceCount +
                        " | önceki RGB=" + RgbText(previousRgb) +
                        " | test RGB=" + testColor.Kirmizi + ", " + testColor.Yesil + ", " + testColor.Mavi +
                        "\nCATIA'daki occurrence yayılımını manuel doğrulayın. Geri yükleme kaydı hazır.");
                }
                finally
                {
                    SecimiGeriYukle(selection, previousSelection);
                }
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("Tek referans PartBody renk testi", ex);
                LogError("Tek referans PartBody renk testi uygulanamadı: " + Kisa(ex.Message));
            }
        }

        private void TekReferansPartBodyRenginiGeriYukle_Click(object sender, RoutedEventArgs e)
        {
            SingleReferenceColorRestoreState? restoreState = _singleReferenceColorRestoreState;
            if (restoreState == null)
            {
                LogError("Tek referans PartBody geri yükleme kaydı bulunamadı.");
                return;
            }

            try
            {
                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                string currentCatiaKey = CatiaColorTargetService.GetSessionObjectKey(catiaObject);
                if (!string.Equals(currentCatiaKey, restoreState.CatiaSessionKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("CATIA oturumu değişti; kayıtlı RGB güvenli biçimde geri yüklenemez.");

                dynamic editor = ((dynamic)catiaObject).ActiveEditor;
                string currentEditorKey = CatiaColorTargetService.GetSessionObjectKey((object)editor);
                if (!string.Equals(currentEditorKey, restoreState.EditorSessionKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("Aktif CATIA editörü değişti; kayıtlı RGB güvenli biçimde geri yüklenemez.");

                dynamic selection = editor.Selection;
                List<object> previousSelection = SecimiSakla(selection);
                try
                {
                    string? currentReferenceKey = AkilliRenklendirmeReferansAnahtari(restoreState.SourceOccurrence);
                    if (!string.Equals(currentReferenceKey, restoreState.ReferenceKey, StringComparison.Ordinal))
                        throw new InvalidOperationException("Kayıtlı occurrence artık aynı referansı bildirmiyor.");

                    var service = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
                    PartBodyDiscoveryResult discovery = ResolveMountedPartBody(service, restoreState.SourceOccurrence);
                    if (!discovery.Success || discovery.Target == null)
                        throw new InvalidOperationException("Kayıtlı PartBody yeniden çözümlenemedi: " + discovery.Error);

                    CatiaColorTarget currentTarget = discovery.Target;
                    if (currentTarget.Identity.TargetType != CatiaColorTargetType.PartBody ||
                        currentTarget.Identity.ComType.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Yeniden çözülen hedef PartBody değildir.");

                    if (!string.Equals(
                            currentTarget.Identity.SessionObjectKey,
                            restoreState.TargetSessionKey,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("PartBody hedefi değişti; kayıtlı RGB başka hedefe yazılmadı.");
                    }

                    CatiaColorOperationResult beforeRestore = service.TryReadRgb(selection, currentTarget);
                    if (!beforeRestore.Success || !beforeRestore.Color.HasValue)
                    {
                        string reason = beforeRestore.Exception == null
                            ? beforeRestore.Error
                            : RenkTestHataAyrintisi("Geri yükleme öncesi GetRealColor", beforeRestore.Exception);
                        throw new InvalidOperationException("Geri yükleme öncesi hedef RGB okunamadı; yazma yapılmadı. " + reason);
                    }

                    LogInfo(
                        "Tek referans PartBody geri yükleme — hedef doğrulandı" +
                        " | Reference Title=" + restoreState.ReferenceTitle +
                        " | referans=" + restoreState.ReferenceKey +
                        " | mevcut RGB=" + RgbText(beforeRestore.Color.Value) +
                        " | geri yüklenecek RGB=" + RgbText(restoreState.PreviousRgb));

                    CatiaColorOperationResult writeResult = service.TryApplyRgb(
                        selection,
                        currentTarget,
                        restoreState.PreviousRgb.Red,
                        restoreState.PreviousRgb.Green,
                        restoreState.PreviousRgb.Blue);
                    if (!writeResult.Success)
                    {
                        string reason = writeResult.Exception == null
                            ? writeResult.Error
                            : RenkTestHataAyrintisi("Geri yükleme SetRealColor", writeResult.Exception);
                        throw new InvalidOperationException("Önceki RGB yazılamadı. " + reason);
                    }

                    CatiaColorOperationResult verifyResult = service.TryReadRgb(selection, currentTarget);
                    if (!TryVerifyRgb(
                            verifyResult,
                            restoreState.PreviousRgb.Red,
                            restoreState.PreviousRgb.Green,
                            restoreState.PreviousRgb.Blue,
                            out string verifyDetail))
                    {
                        LogError(
                            "Tek referans PartBody geri yükleme — SetRealColor tamamlandı fakat RGB doğrulanamadı. " +
                            verifyDetail + " Geri yükleme kaydı korunuyor.");
                        return;
                    }

                    _singleReferenceColorRestoreState = null;
                    LogSuccess(
                        "Tek referans PartBody önceki RGB değeri doğrulanarak geri yüklendi" +
                        " | Reference Title=" + restoreState.ReferenceTitle +
                        " | referans=" + restoreState.ReferenceKey +
                        " | RGB=" + RgbText(restoreState.PreviousRgb) +
                        " | ilişkili occurrence=" + restoreState.MatchingOccurrenceCount +
                        "\nNot: yalnız RGB doğrulandı; renk kalıtımı ve diğer görünüm özellikleri geri yüklenmiş sayılmaz.");
                }
                finally
                {
                    SecimiGeriYukle(selection, previousSelection);
                }
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("Tek referans PartBody RGB geri yükleme", ex);
                LogError("Tek referans PartBody RGB geri yüklenemedi: " + Kisa(ex.Message));
            }
        }

        private static string ReadReferenceTitle(object occurrence)
        {
            try
            {
                object? reference = ReferansAl((dynamic)occurrence);
                if (reference == null) return "(Reference Title okunamadı)";
                string title = PlmBaslik(reference);
                return string.IsNullOrWhiteSpace(title) ? "(Reference Title okunamadı)" : title;
            }
            catch
            {
                return "(Reference Title okunamadı)";
            }
        }

        private static AkilliRenk SelectTemporaryPartBodyColor(CatiaColorRgb previousRgb)
        {
            var primary = new AkilliRenk(255, 35, 170);
            if (previousRgb.Red != primary.Kirmizi ||
                previousRgb.Green != primary.Yesil ||
                previousRgb.Blue != primary.Mavi)
                return primary;

            return new AkilliRenk(45, 220, 80);
        }

        private static bool TryVerifyRgb(
            CatiaColorOperationResult result,
            int expectedRed,
            int expectedGreen,
            int expectedBlue,
            out string detail)
        {
            if (!result.Success || !result.Color.HasValue)
            {
                detail = result.Exception == null
                    ? result.Error
                    : RenkTestHataAyrintisi("VisProperties.GetRealColor doğrulaması", result.Exception);
                return false;
            }

            CatiaColorRgb actual = result.Color.Value;
            detail = "beklenen=" + expectedRed + ", " + expectedGreen + ", " + expectedBlue +
                     " | okunan=" + RgbText(actual) + " | durum=" + actual.Status;
            return actual.Red == expectedRed &&
                   actual.Green == expectedGreen &&
                   actual.Blue == expectedBlue;
        }

        private static string RgbText(CatiaColorRgb color) =>
            color.Red + ", " + color.Green + ", " + color.Blue;

        private static bool TryGetDefinedColorInheritance(
            CatiaColorInheritanceOperationResult result,
            out int inheritance,
            out string detail)
        {
            inheritance = -1;
            if (!result.Success || !result.Inheritance.HasValue)
            {
                detail = result.Exception == null
                    ? result.Error
                    : RenkTestHataAyrintisi(
                        "VisProperties.GetRealInheritance(catVisPropertyColor)",
                        result.Exception);
                return false;
            }

            CatiaColorInheritance value = result.Inheritance.Value;
            if (value.Status != 0)
            {
                detail = "durum=" + ColorPropertyStatusText(value.Status) +
                         "; dönen inheritance çıkışı geçersizdir ve kullanılmadı.";
                return false;
            }

            if (value.Inheritance != 0 && value.Inheritance != 1)
            {
                detail = "Defined durumunda beklenmeyen inheritance değeri: " + value.Inheritance;
                return false;
            }

            inheritance = value.Inheritance;
            detail = "durum=Defined (0), inheritance=" + inheritance;
            return true;
        }

        private sealed class SingleReferenceColorRestoreState
        {
            public SingleReferenceColorRestoreState(
                string catiaSessionKey,
                string editorSessionKey,
                string referenceKey,
                string targetSessionKey,
                object sourceOccurrence,
                CatiaColorRgb previousRgb,
                AkilliRenk testColor,
                int matchingOccurrenceCount,
                string referenceTitle)
            {
                CatiaSessionKey = catiaSessionKey;
                EditorSessionKey = editorSessionKey;
                ReferenceKey = referenceKey;
                TargetSessionKey = targetSessionKey;
                SourceOccurrence = sourceOccurrence;
                PreviousRgb = previousRgb;
                TestColor = testColor;
                MatchingOccurrenceCount = matchingOccurrenceCount;
                ReferenceTitle = referenceTitle;
            }

            public string CatiaSessionKey { get; }
            public string EditorSessionKey { get; }
            public string ReferenceKey { get; }
            public string TargetSessionKey { get; }
            public object SourceOccurrence { get; }
            public CatiaColorRgb PreviousRgb { get; }
            public AkilliRenk TestColor { get; }
            public int MatchingOccurrenceCount { get; }
            public string ReferenceTitle { get; }
        }
    }
}
