using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        private const int NativeResetTestRed = 255;
        private const int NativeResetTestGreen = 35;
        private const int NativeResetTestBlue = 170;

        private NativeResetAbState? _nativeResetAbState;

        // Kontrollü CATIA deneyi: production inheritance güvenlik kapısını değiştirmez.
        private void NativeResetAbTesti_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_nativeResetAbState != null)
                {
                    throw new InvalidOperationException(
                        "Önceki Native Reset A/B testi için başlangıç snapshot'ı bekliyor. " +
                        "CATIA UI resetinden sonra önce 'NATIVE RESET SONRASI OKU' komutunu çalıştırın.");
                }

                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic editor = ((dynamic)catiaObject).ActiveEditor;
                object editorObject = (object)editor;
                EnsureNativeResetAssemblyContext(editor);

                dynamic selection = editor.Selection;
                List<object> previousSelection = SecimiSakla(selection);
                try
                {
                    if (previousSelection.Count != 1)
                        throw new InvalidOperationException("CATIA'da tam olarak bir yaprak parça occurrence seçin.");

                    object selectedOccurrence = previousSelection[0];
                    NativeResetTargetContext context = ResolveNativeResetTarget(selectedOccurrence);
                    NativeResetColorState initial = ReadNativeResetColorState(
                        context.Service,
                        selection,
                        context.Target);

                    LogNativeResetIdentity("BAŞLANGIÇ", catiaObject, editorObject, context);
                    LogNativeResetColorState("BAŞLANGIÇ", initial);

                    if (!initial.AllReadsSucceeded)
                    {
                        LogError(
                            "Native Reset A/B testi — başlangıçtaki dört grafik sorgusunun tamamı okunamadı; " +
                            "SetRealColor çağrılmadı.");
                        return;
                    }

                    if (NativeResetColorEquals(initial.RealColor, NativeResetTestRed, NativeResetTestGreen, NativeResetTestBlue) ||
                        NativeResetColorEquals(initial.VisibleColor, NativeResetTestRed, NativeResetTestGreen, NativeResetTestBlue))
                    {
                        LogError(
                            "Native Reset A/B testi — başlangıç Real veya Visible rengi test rengiyle aynı. " +
                            "Deney ayırt edici olmayacağı için SetRealColor çağrılmadı.");
                        return;
                    }

                    string warning =
                        "Bu deney PartBody grafik özelliğini geçici olarak değiştirecektir.\n" +
                        "Belge kaydedilmemelidir.\n" +
                        "Keep Graphical Properties, Reset All ve Undo kullanılmamalıdır.\n" +
                        "Devam?\n\n" +
                        "Reference Title: " + context.ReferenceTitle + "\n" +
                        "Reference identity: " + context.ReferenceKey + "\n" +
                        "PartBody: " + context.Target.Identity.SessionObjectKey + "\n" +
                        "Test RGB: 255, 35, 170";

                    if (!OnayWindow.Sor(
                            this,
                            "NATIVE RESET A/B TESTİ",
                            warning,
                            "Test rengini uygula"))
                    {
                        LogInfo("Native Reset A/B testi kullanıcı tarafından iptal edildi; renk yazılmadı.");
                        return;
                    }

                    NativeResetTargetContext writeContext = RevalidateNativeResetTarget(
                        catiaObject,
                        editorObject,
                        selectedOccurrence,
                        context);

                    CatiaColorOperationResult writeResult = writeContext.Service.TryApplyRgb(
                        selection,
                        writeContext.Target,
                        NativeResetTestRed,
                        NativeResetTestGreen,
                        NativeResetTestBlue);
                    if (!writeResult.Success)
                    {
                        string reason = writeResult.Exception == null
                            ? writeResult.Error
                            : RenkTestHataAyrintisi("VisProperties.SetRealColor", writeResult.Exception);
                        LogError("Native Reset A/B testi — test rengi yazılamadı. " + reason);
                        return;
                    }

                    // SetRealColor başarılı döndüğü anda başlangıç kanıtını bellekte koru.
                    _nativeResetAbState = new NativeResetAbState(
                        CatiaColorTargetService.GetSessionObjectKey(catiaObject),
                        CatiaColorTargetService.GetSessionObjectKey(editorObject),
                        writeContext.ReferenceKey,
                        writeContext.ReferenceTitle,
                        writeContext.Target.Identity.SessionObjectKey,
                        selectedOccurrence,
                        initial);

                    NativeResetColorState afterWrite = ReadNativeResetColorState(
                        writeContext.Service,
                        selection,
                        writeContext.Target);
                    LogNativeResetColorState("BODY TEST RENGİ SONRASI", afterWrite);

                    if (!afterWrite.AllReadsSucceeded)
                    {
                        LogError(
                            "Native Reset A/B testi — SetRealColor tamamlandı fakat yazım sonrası dört durumun " +
                            "tamamı okunamadı. Başlangıç snapshot'ı korunuyor; belgeyi kaydetmeyin.");
                    }
                    else
                    {
                        LogInfo(
                            "Native Reset A/B testi — test rengi yazıldı. " +
                            "Inheritance parametresi mevcut doğrulanmış test yolu ile aynı şekilde 1 kullanıldı. " +
                            "Başlangıçtaki UnDefined inheritance bu özel teşhiste yazmayı engellemedi.");
                    }

                    LogInfo(
                        "Şimdi CATIA UI'dan yalnız Reset Graphical Properties çalıştırın.\n" +
                        "Sonra 'NATIVE RESET SONRASI OKU' komutunu çalıştırın.\n" +
                        "Keep Graphical Properties, Reset All, Undo ve Save kullanmayın.");
                }
                finally
                {
                    SecimiGeriYukle(selection, previousSelection);
                }
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("Native Reset A/B testi", ex);
                LogError("Native Reset A/B testi uygulanamadı: " + Kisa(ex.Message));
            }
        }

        // Salt-okunur ikinci aşama. CATIA reset komutu veya ResetProperty çağırmaz.
        private void NativeResetSonrasiOku_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic editor = ((dynamic)catiaObject).ActiveEditor;
                object editorObject = (object)editor;
                EnsureNativeResetAssemblyContext(editor);

                dynamic selection = editor.Selection;
                List<object> previousSelection = SecimiSakla(selection);
                try
                {
                    NativeResetAbState? testState = _nativeResetAbState;
                    object occurrence;
                    if (testState == null)
                    {
                        if (previousSelection.Count != 1)
                        {
                            throw new InvalidOperationException(
                                "Bellekte Native Reset A/B başlangıç snapshot'ı yok. " +
                                "Salt-okunur durum için tam olarak bir yaprak occurrence seçin.");
                        }

                        occurrence = previousSelection[0];
                    }
                    else
                    {
                        string catiaKey = CatiaColorTargetService.GetSessionObjectKey(catiaObject);
                        string editorKey = CatiaColorTargetService.GetSessionObjectKey(editorObject);
                        if (!string.Equals(catiaKey, testState.CatiaSessionKey, StringComparison.Ordinal) ||
                            !string.Equals(editorKey, testState.EditorSessionKey, StringComparison.Ordinal))
                        {
                            _nativeResetAbState = null;
                            throw new InvalidOperationException(
                                "CATIA oturumu veya aktif editör değişti. Eski başlangıç snapshot'ı " +
                                "karşılaştırmada kullanılmadan bırakıldı; komutu yeniden çalıştırın.");
                        }

                        occurrence = testState.SourceOccurrence;
                    }

                    NativeResetTargetContext context = ResolveNativeResetTarget(occurrence);
                    NativeResetColorState current = ReadNativeResetColorState(
                        context.Service,
                        selection,
                        context.Target);

                    LogNativeResetIdentity("RESET SONRASI", catiaObject, editorObject, context);
                    LogNativeResetColorState("RESET SONRASI", current);

                    if (testState == null)
                    {
                        LogInfo(
                            "Native Reset sonrası salt-okunur durum loglandı. Bellekte başlangıç snapshot'ı " +
                            "olmadığı için A/B karşılaştırması veya başarı kararı üretilmedi.");
                        return;
                    }

                    if (!string.Equals(context.ReferenceKey, testState.ReferenceKey, StringComparison.Ordinal))
                        throw new InvalidOperationException("Occurrence artık başlangıçtaki referansı bildirmiyor.");
                    if (!string.Equals(
                            context.Target.Identity.SessionObjectKey,
                            testState.TargetSessionKey,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Yeniden çözülen PartBody başlangıçtaki COM hedefi değildir; karşılaştırma yapılmadı.");
                    }

                    NativeResetComparison comparison = CompareNativeResetStates(testState.Initial, current);
                    LogNativeResetComparison(comparison);

                    if (comparison.ObservableStateMatches)
                    {
                        _nativeResetAbState = null;
                    }
                    else
                    {
                        LogInfo(
                            "Native Reset başlangıç snapshot'ı korunuyor. CATIA UI reset henüz çalıştırılmadıysa " +
                            "yalnız Reset Graphical Properties sonrasında bu komut tekrar çalıştırılabilir.");
                    }
                }
                finally
                {
                    SecimiGeriYukle(selection, previousSelection);
                }
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("Native Reset sonrası okuma", ex);
                LogError("Native Reset sonrası okuma uygulanamadı: " + Kisa(ex.Message));
            }
        }

        private static void EnsureNativeResetAssemblyContext(dynamic editor)
        {
            object? root = editor.ActiveObject;
            if (root is not object activeRoot)
                throw new InvalidOperationException("Aktif CATIA montaj kökü alınamadı.");
            if (!HasOccurrences((dynamic)activeRoot))
            {
                throw new InvalidOperationException(
                    "Aktif CATIA nesnesi occurrence içeren bir Physical Product montajı değil.");
            }
        }

        private static NativeResetTargetContext ResolveNativeResetTarget(object selectedOccurrence)
        {
            string selectedType = ComProbe.TipAdi(selectedOccurrence);
            if (selectedType.IndexOf("Occurrence", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    "Seçilen nesne occurrence olarak doğrulanamadı (COM türü=" + selectedType + ").");
            }

            if (AkilliRenklendirmeAltOccurrenceSayisi((dynamic)selectedOccurrence) != 0)
                throw new InvalidOperationException("Seçilen occurrence yaprak parça değildir.");

            string? referenceKey = AkilliRenklendirmeReferansAnahtari(selectedOccurrence);
            if (string.IsNullOrWhiteSpace(referenceKey))
                throw new InvalidOperationException("Seçilen occurrence için PLM referans kimliği çözümlenemedi.");

            var service = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
            CatiaColorTargetResult occurrenceResult = service.ResolveOccurrence(selectedOccurrence);
            if (!occurrenceResult.Success)
                throw new InvalidOperationException(occurrenceResult.Error);

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
                    "Çözülen PartBody farklı bir referans kimliği bildirdi: " +
                    target.Identity.ReferenceKey);
            }

            return new NativeResetTargetContext(
                service,
                selectedOccurrence,
                target,
                referenceKey,
                ReadReferenceTitle(selectedOccurrence),
                discovery.AccessChain);
        }

        private NativeResetTargetContext RevalidateNativeResetTarget(
            object catiaObject,
            object editorObject,
            object sourceOccurrence,
            NativeResetTargetContext initial)
        {
            object? currentCatia = GetCatia();
            if (currentCatia == null ||
                !string.Equals(
                    CatiaColorTargetService.GetSessionObjectKey(currentCatia),
                    CatiaColorTargetService.GetSessionObjectKey(catiaObject),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Kullanıcı onayı sırasında CATIA oturumu değişti; renk yazılmadı.");
            }

            object currentEditor = (object)((dynamic)currentCatia).ActiveEditor;
            if (!string.Equals(
                    CatiaColorTargetService.GetSessionObjectKey(currentEditor),
                    CatiaColorTargetService.GetSessionObjectKey(editorObject),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Kullanıcı onayı sırasında aktif editör değişti; renk yazılmadı.");
            }

            NativeResetTargetContext current = ResolveNativeResetTarget(sourceOccurrence);
            if (!string.Equals(current.ReferenceKey, initial.ReferenceKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Kullanıcı onayı sırasında occurrence referansı değişti; renk yazılmadı.");
            if (!string.Equals(
                    current.Target.Identity.SessionObjectKey,
                    initial.Target.Identity.SessionObjectKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Kullanıcı onayı sırasında PartBody hedefi değişti; renk yazılmadı.");
            }

            return current;
        }

        private static NativeResetColorState ReadNativeResetColorState(
            CatiaColorTargetService service,
            dynamic selection,
            CatiaColorTarget target) =>
            new(
                service.TryReadRgb(selection, target),
                service.TryReadVisibleRgb(selection, target),
                service.TryReadRealColorInheritance(selection, target),
                service.TryReadVisibleColorInheritance(selection, target));

        private void LogNativeResetIdentity(
            string stage,
            object catiaObject,
            object editorObject,
            NativeResetTargetContext context)
        {
            LogInfo(
                "Native Reset A/B — " + stage + " HEDEF" +
                " | CATIA oturumu=" + CatiaColorTargetService.GetSessionObjectKey(catiaObject) +
                " | aktif editör=" + CatiaColorTargetService.GetSessionObjectKey(editorObject) +
                " | Reference Title=" + context.ReferenceTitle +
                " | Reference identity=" + context.ReferenceKey +
                " | Body COM türü=" + context.Target.Identity.ComType +
                " | Body COM identity=" + context.Target.Identity.SessionObjectKey +
                " | zincir=" + context.AccessChain);
        }

        private void LogNativeResetColorState(string stage, NativeResetColorState state)
        {
            var text = new StringBuilder();
            text.AppendLine("Native Reset A/B — " + stage);
            text.AppendLine("Real RGB: " + NativeResetColorText(state.RealColor));
            text.AppendLine("Visible RGB: " + NativeResetColorText(state.VisibleColor));
            text.AppendLine("Real inheritance: " + NativeResetInheritanceText(state.RealInheritance));
            text.Append("Visible inheritance: " + NativeResetInheritanceText(state.VisibleInheritance));

            if (state.AllReadsSucceeded)
                LogInfo(text.ToString());
            else
                LogError(text.ToString());
        }

        private static string NativeResetColorText(CatiaColorOperationResult result)
        {
            if (!result.Success || !result.Color.HasValue)
            {
                return "FAILED | " + (result.Exception == null
                    ? result.Error
                    : RenkTestHataAyrintisi("VisProperties renk okuması", result.Exception));
            }

            CatiaColorRgb value = result.Color.Value;
            return "status=" + ColorPropertyStatusText(value.Status) +
                   " | RGB=" + RgbText(value) +
                   (value.Status == 0 ? " | değer geçerli" : " | output değerleri geçerli sayılmadı");
        }

        private static string NativeResetInheritanceText(CatiaColorInheritanceOperationResult result)
        {
            if (!result.Success || !result.Inheritance.HasValue)
            {
                return "FAILED | " + (result.Exception == null
                    ? result.Error
                    : RenkTestHataAyrintisi("VisProperties inheritance okuması", result.Exception));
            }

            CatiaColorInheritance value = result.Inheritance.Value;
            if (value.Status != 0)
            {
                return "status=" + ColorPropertyStatusText(value.Status) +
                       " | output değeri geçersizdir ve yorumlanmadı";
            }

            string inheritance = value.Inheritance switch
            {
                0 => "0 (kalıtım yok)",
                1 => "1 (kalıtım var)",
                _ => value.Inheritance + " (beklenmeyen değer)"
            };
            return "status=Defined (0) | inheritance=" + inheritance;
        }

        private static bool NativeResetColorEquals(
            CatiaColorOperationResult result,
            int red,
            int green,
            int blue)
        {
            if (!result.Success || !result.Color.HasValue || result.Color.Value.Status != 0)
                return false;

            CatiaColorRgb value = result.Color.Value;
            return value.Red == red && value.Green == green && value.Blue == blue;
        }

        private static NativeResetComparison CompareNativeResetStates(
            NativeResetColorState initial,
            NativeResetColorState current)
        {
            NativeResetFieldComparison realColor = CompareNativeResetColor(
                "Real RGB",
                initial.RealColor,
                current.RealColor);
            NativeResetFieldComparison visibleColor = CompareNativeResetColor(
                "Visible RGB",
                initial.VisibleColor,
                current.VisibleColor);
            NativeResetFieldComparison realInheritance = CompareNativeResetInheritance(
                "Real inheritance",
                initial.RealInheritance,
                current.RealInheritance);
            NativeResetFieldComparison visibleInheritance = CompareNativeResetInheritance(
                "Visible inheritance",
                initial.VisibleInheritance,
                current.VisibleInheritance);

            return new NativeResetComparison(
                realColor,
                visibleColor,
                realInheritance,
                visibleInheritance);
        }

        private static NativeResetFieldComparison CompareNativeResetColor(
            string label,
            CatiaColorOperationResult initialResult,
            CatiaColorOperationResult currentResult)
        {
            if (!initialResult.Success || !initialResult.Color.HasValue ||
                !currentResult.Success || !currentResult.Color.HasValue)
            {
                return NativeResetFieldComparison.Unknown(label, "okumalardan en az biri başarısız");
            }

            CatiaColorRgb initial = initialResult.Color.Value;
            CatiaColorRgb current = currentResult.Color.Value;
            if (initial.Status != current.Status)
            {
                return NativeResetFieldComparison.Different(
                    label,
                    "status başlangıç=" + ColorPropertyStatusText(initial.Status) +
                    ", reset sonrası=" + ColorPropertyStatusText(current.Status));
            }

            if (initial.Status != 0)
            {
                return NativeResetFieldComparison.Unknown(
                    label,
                    "status aynı (" + ColorPropertyStatusText(initial.Status) +
                    "); RGB output değerleri geçersiz olduğundan dönüş kanıtlanamadı");
            }

            bool same = initial.Red == current.Red &&
                        initial.Green == current.Green &&
                        initial.Blue == current.Blue;
            string detail = "başlangıç=" + RgbText(initial) + " | reset sonrası=" + RgbText(current);
            return same
                ? NativeResetFieldComparison.Same(label, detail)
                : NativeResetFieldComparison.Different(label, detail);
        }

        private static NativeResetFieldComparison CompareNativeResetInheritance(
            string label,
            CatiaColorInheritanceOperationResult initialResult,
            CatiaColorInheritanceOperationResult currentResult)
        {
            if (!initialResult.Success || !initialResult.Inheritance.HasValue ||
                !currentResult.Success || !currentResult.Inheritance.HasValue)
            {
                return NativeResetFieldComparison.Unknown(label, "okumalardan en az biri başarısız");
            }

            CatiaColorInheritance initial = initialResult.Inheritance.Value;
            CatiaColorInheritance current = currentResult.Inheritance.Value;
            if (initial.Status != current.Status)
            {
                return NativeResetFieldComparison.Different(
                    label,
                    "status başlangıç=" + ColorPropertyStatusText(initial.Status) +
                    ", reset sonrası=" + ColorPropertyStatusText(current.Status));
            }

            if (initial.Status != 0)
            {
                return NativeResetFieldComparison.Same(
                    label,
                    "status aynı (" + ColorPropertyStatusText(initial.Status) +
                    "); output inheritance değeri yorumlanmadı",
                    exact: false);
            }

            bool same = initial.Inheritance == current.Inheritance;
            string detail = "status aynı (Defined); başlangıç inheritance=" + initial.Inheritance +
                            " | reset sonrası=" + current.Inheritance;
            return same
                ? NativeResetFieldComparison.Same(label, detail)
                : NativeResetFieldComparison.Different(label, detail);
        }

        private void LogNativeResetComparison(NativeResetComparison comparison)
        {
            var text = new StringBuilder();
            text.AppendLine("Native Reset A/B — KARŞILAŞTIRMA");
            text.AppendLine(comparison.RealColor.Text);
            text.AppendLine(comparison.VisibleColor.Text);
            text.AppendLine(comparison.RealInheritance.Text);
            text.AppendLine(comparison.VisibleInheritance.Text);
            text.AppendLine("RGB başlangıca döndü mü? " + comparison.RealColor.Answer);
            text.AppendLine("Visible RGB başlangıca döndü mü? " + comparison.VisibleColor.Answer);
            text.AppendLine(
                "Inheritance status başlangıçla aynı mı? Real=" + comparison.RealInheritance.Answer +
                ", Visible=" + comparison.VisibleInheritance.Answer);

            if (comparison.ExactRestoreObserved)
            {
                text.Append(
                    "SONUÇ: Exact restore BAŞARILI — RGB, status ve geçerli inheritance değerleri " +
                    "başlangıçla aynı okundu.");
                LogSuccess(text.ToString());
            }
            else if (comparison.ObservableStateMatches)
            {
                text.Append(
                    "SONUÇ: Başlangıçla aynı gözlemlenebilir durum okundu; ancak en az bir alan UnDefined " +
                    "olduğu için exact restore BAŞARILI ilan edilmedi. Gerçek CATIA kararı BEKLİYOR.");
                LogInfo(text.ToString());
            }
            else
            {
                text.Append(
                    "SONUÇ: Başlangıç durumu eksiksiz doğrulanmadı. Native reset başarısı ilan edilmedi; " +
                    "gerçek CATIA kararı BEKLİYOR.");
                LogError(text.ToString());
            }
        }

        private sealed class NativeResetTargetContext
        {
            public NativeResetTargetContext(
                CatiaColorTargetService service,
                object sourceOccurrence,
                CatiaColorTarget target,
                string referenceKey,
                string referenceTitle,
                string accessChain)
            {
                Service = service;
                SourceOccurrence = sourceOccurrence;
                Target = target;
                ReferenceKey = referenceKey;
                ReferenceTitle = referenceTitle;
                AccessChain = accessChain;
            }

            public CatiaColorTargetService Service { get; }
            public object SourceOccurrence { get; }
            public CatiaColorTarget Target { get; }
            public string ReferenceKey { get; }
            public string ReferenceTitle { get; }
            public string AccessChain { get; }
        }

        private sealed class NativeResetColorState
        {
            public NativeResetColorState(
                CatiaColorOperationResult realColor,
                CatiaColorOperationResult visibleColor,
                CatiaColorInheritanceOperationResult realInheritance,
                CatiaColorInheritanceOperationResult visibleInheritance)
            {
                RealColor = realColor;
                VisibleColor = visibleColor;
                RealInheritance = realInheritance;
                VisibleInheritance = visibleInheritance;
            }

            public CatiaColorOperationResult RealColor { get; }
            public CatiaColorOperationResult VisibleColor { get; }
            public CatiaColorInheritanceOperationResult RealInheritance { get; }
            public CatiaColorInheritanceOperationResult VisibleInheritance { get; }

            public bool AllReadsSucceeded =>
                RealColor.Success && RealColor.Color.HasValue &&
                VisibleColor.Success && VisibleColor.Color.HasValue &&
                RealInheritance.Success && RealInheritance.Inheritance.HasValue &&
                VisibleInheritance.Success && VisibleInheritance.Inheritance.HasValue;
        }

        private sealed class NativeResetAbState
        {
            public NativeResetAbState(
                string catiaSessionKey,
                string editorSessionKey,
                string referenceKey,
                string referenceTitle,
                string targetSessionKey,
                object sourceOccurrence,
                NativeResetColorState initial)
            {
                CatiaSessionKey = catiaSessionKey;
                EditorSessionKey = editorSessionKey;
                ReferenceKey = referenceKey;
                ReferenceTitle = referenceTitle;
                TargetSessionKey = targetSessionKey;
                SourceOccurrence = sourceOccurrence;
                Initial = initial;
            }

            public string CatiaSessionKey { get; }
            public string EditorSessionKey { get; }
            public string ReferenceKey { get; }
            public string ReferenceTitle { get; }
            public string TargetSessionKey { get; }
            public object SourceOccurrence { get; }
            public NativeResetColorState Initial { get; }
        }

        private sealed class NativeResetFieldComparison
        {
            private NativeResetFieldComparison(
                string label,
                string answer,
                string detail,
                bool observableMatch,
                bool exactMatch)
            {
                Label = label;
                Answer = answer;
                Detail = detail;
                ObservableMatch = observableMatch;
                ExactMatch = exactMatch;
            }

            public string Label { get; }
            public string Answer { get; }
            public string Detail { get; }
            public bool ObservableMatch { get; }
            public bool ExactMatch { get; }
            public string Text => Label + ": " + Answer + " | " + Detail;

            public static NativeResetFieldComparison Same(
                string label,
                string detail,
                bool exact = true) =>
                new(label, "EVET", detail, observableMatch: true, exactMatch: exact);

            public static NativeResetFieldComparison Different(string label, string detail) =>
                new(label, "HAYIR", detail, observableMatch: false, exactMatch: false);

            public static NativeResetFieldComparison Unknown(string label, string detail) =>
                new(label, "BELİRSİZ", detail, observableMatch: false, exactMatch: false);
        }

        private sealed class NativeResetComparison
        {
            public NativeResetComparison(
                NativeResetFieldComparison realColor,
                NativeResetFieldComparison visibleColor,
                NativeResetFieldComparison realInheritance,
                NativeResetFieldComparison visibleInheritance)
            {
                RealColor = realColor;
                VisibleColor = visibleColor;
                RealInheritance = realInheritance;
                VisibleInheritance = visibleInheritance;
            }

            public NativeResetFieldComparison RealColor { get; }
            public NativeResetFieldComparison VisibleColor { get; }
            public NativeResetFieldComparison RealInheritance { get; }
            public NativeResetFieldComparison VisibleInheritance { get; }

            public bool ObservableStateMatches =>
                RealColor.ObservableMatch &&
                VisibleColor.ObservableMatch &&
                RealInheritance.ObservableMatch &&
                VisibleInheritance.ObservableMatch;

            public bool ExactRestoreObserved =>
                RealColor.ExactMatch &&
                VisibleColor.ExactMatch &&
                RealInheritance.ExactMatch &&
                VisibleInheritance.ExactMatch;
        }
    }
}
