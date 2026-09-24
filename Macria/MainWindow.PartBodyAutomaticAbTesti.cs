using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        // InfTypeLib.tlb / CatVisPropertyType: catVisPropertyColor = 2.
        private const int AutomaticAbCatVisPropertyColor = 2;
        private const int AutomaticAbTestRed = 255;
        private const int AutomaticAbTestGreen = 35;
        private const int AutomaticAbTestBlue = 170;

        private AutomaticAbState? _automaticAbState;

        // Tek menü komutu, gerçek CATIA kullanıcısının yapacağı UI Automatic adımı için
        // çağrılar arasında güvenli biçimde bekleyen kontrollü A/B akışını ilerletir.
        private void PartBodyAutomaticAbTesti_Click(object sender, RoutedEventArgs e)
        {
            var progress = new AutomaticAbProgress();
            try
            {
                if (_automaticAbState == null)
                {
                    StartAutomaticResetBranch(progress);
                    return;
                }

                switch (_automaticAbState.Stage)
                {
                    case AutomaticAbStage.AwaitAutomationReset:
                        CompleteAutomaticResetBranch(_automaticAbState, progress);
                        break;
                    case AutomaticAbStage.AwaitUiBranchWrite:
                        StartUiAutomaticBranch(_automaticAbState, progress);
                        break;
                    case AutomaticAbStage.AwaitUiAutomaticRead:
                        CompleteUiAutomaticBranch(_automaticAbState, progress);
                        break;
                    default:
                        throw new InvalidOperationException("Automatic A/B teşhis aşaması tanınmıyor.");
                }
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("PartBody Automatic A/B teşhisi", ex);
                LogError(
                    "PartBody Automatic A/B teşhisi tamamlanamadı: " + Kisa(ex.Message) +
                    " | Aşama=" + progress.Stage +
                    (progress.MutationCallReached
                        ? " | Bir renk/reset çağrısına ulaşılmıştır; geçici grafik değişikliği kalmış olabilir. Belgeyi kaydetmeyin."
                        : " | Bu çağrıda renk/reset aşamasına ulaşılmadı; CATIA grafik özelliği değiştirilmedi."));
            }
        }

        private void StartAutomaticResetBranch(AutomaticAbProgress progress)
        {
            progress.Stage = "A başlangıç Selection context'i";
            AutomaticAbRuntime runtime = OpenAutomaticAbRuntime(null);
            List<object> originalSelection = CaptureAutomaticSelectionTokens(runtime.Selection);
            try
            {
                AutomaticAbTarget target = CaptureAutomaticAbTarget(runtime.Selection);
                NativeResetColorState initial = ReadAutomaticAbState(runtime.Selection, target);

                LogAutomaticAbIdentity("A — BAŞLANGIÇ", runtime, target);
                LogAutomaticAbState("A — BAŞLANGIÇ (UI AUTOMATIC BEKLENİYOR)", initial);
                if (!initial.AllReadsSucceeded)
                {
                    throw new InvalidOperationException(
                        "Başlangıçtaki dört grafik sorgusunun tamamı okunamadı; SetRealColor çağrılmadı.");
                }

                if (NativeResetColorEquals(initial.RealColor, AutomaticAbTestRed, AutomaticAbTestGreen, AutomaticAbTestBlue) ||
                    NativeResetColorEquals(initial.VisibleColor, AutomaticAbTestRed, AutomaticAbTestGreen, AutomaticAbTestBlue))
                {
                    throw new InvalidOperationException(
                        "Başlangıç Real veya Visible rengi test RGB'siyle aynı; deney ayırt edici olmayacağı için yazma yapılmadı.");
                }

                if (!OnayWindow.Sor(
                        this,
                        "PARTBODY AUTOMATIC A/B — A",
                        "Başlamadan önce seçili PartBody'yi CATIA UI'da Color > Automatic yapmış olmalısınız.\n\n" +
                        "Yalnız bu PartBody'ye RGB 255, 35, 170; inheritance=1 uygulanacak.\n" +
                        "Belge kaydedilmemelidir. Undo, Product Reset, Reset All ve Keep kullanılmamalıdır.\n\n" +
                        "PartBody COM: " + target.ComType + "\n" +
                        "PartBody identity: " + target.SessionObjectKey + "\n\n" +
                        "Başlangıcın UI Automatic olduğunu onaylıyor ve test rengini uygulamak istiyor musunuz?",
                        "A test rengini uygula"))
                {
                    LogInfo("PartBody Automatic A/B — A kullanıcı tarafından ilk yazmadan önce iptal edildi; renk yazılmadı.");
                    return;
                }

                progress.Stage = "A SetRealColor öncesi güvenlik kontrolü";
                RevalidateAutomaticAbRuntime(runtime, target);
                progress.Stage = "A SetRealColor(255,35,170,1)";
                progress.MutationCallReached = true;
                ApplyAutomaticAbTestColor(runtime.Selection, target);

                var state = new AutomaticAbState(
                    runtime.CatiaSessionKey,
                    runtime.EditorSessionKey,
                    target,
                    initial);
                _automaticAbState = state;

                progress.Stage = "A test rengi sonrası salt-okunur okuma";
                state.AfterAutomationWrite = ReadAutomaticAbState(runtime.Selection, target);
                LogAutomaticAbState("A — TEST RENGİ SONRASI", state.AfterAutomationWrite);

                CompleteAutomaticResetBranch(state, progress, runtime, originalSelection);
            }
            finally
            {
                RestoreAutomaticSelectionTokens(runtime.Selection, originalSelection);
            }
        }

        private void CompleteAutomaticResetBranch(
            AutomaticAbState state,
            AutomaticAbProgress progress,
            AutomaticAbRuntime? knownRuntime = null,
            List<object>? knownOriginalSelection = null)
        {
            progress.Stage = "A ResetProperty öncesi context doğrulama";
            AutomaticAbRuntime runtime = knownRuntime ?? OpenAutomaticAbRuntime(state);
            List<object> originalSelection = knownOriginalSelection ?? CaptureAutomaticSelectionTokens(runtime.Selection);
            bool ownsSelectionRestore = knownOriginalSelection == null;
            try
            {
                RevalidateAutomaticAbRuntime(runtime, state.Target);

                if (!OnayWindow.Sor(
                        this,
                        "PARTBODY AUTOMATIC A/B — A RESET",
                        "Ayrı onay: yalnız aynı PartBody üzerinde\n" +
                        "ResetProperty(catVisPropertyColor) çağrılacak.\n\n" +
                        "Başka renk, Reset All, Product Reset, Undo veya Save çağrısı yapılmayacaktır.\n" +
                        "Bu API'nin UI Automatic ile eşdeğerliği henüz doğrulanmamıştır.\n\n" +
                        "PartBody identity: " + state.Target.SessionObjectKey,
                        "Yalnız color property'yi resetle"))
                {
                    LogError(
                        "PartBody Automatic A/B — A ResetProperty öncesinde durduruldu. " +
                        "Test rengi PartBody üzerinde kalır; belgeyi kaydetmeyin. Aynı komut A reset onayından devam edebilir.");
                    return;
                }

                progress.Stage = "A ResetProperty öncesi güvenlik kontrolü";
                RevalidateAutomaticAbRuntime(runtime, state.Target);
                progress.Stage = "A ResetProperty(catVisPropertyColor)";
                progress.MutationCallReached = true;
                ResetAutomaticAbColor(runtime.Selection, state.Target);

                progress.Stage = "A ResetProperty sonrası salt-okunur okuma";
                state.AfterAutomationReset = ReadAutomaticAbState(runtime.Selection, state.Target);
                state.Stage = AutomaticAbStage.AwaitUiBranchWrite;
                LogAutomaticAbState("A — AUTOMATION RESET SONRASI", state.AfterAutomationReset);
                LogAutomaticAbComparison(
                    "A AUTOMATION RESET ↔ BAŞLANGIÇ",
                    "sol=BAŞLANGIÇ, sağ=A AUTOMATION RESET",
                    CompareNativeResetStates(state.Initial, state.AfterAutomationReset));

                LogInfo(
                    "A dalı tamamlandı. Şimdi CATIA UI Color listesinin Automatic gösterip göstermediğini elle kontrol edip not edin.\n" +
                    "B dalının temiz başlangıcı için PartBody'yi UI'da Color > Automatic durumuna getirin; ardından aynı " +
                    "'PARTBODY AUTOMATIC A/B TESTİ' komutunu yeniden çalıştırın.\n" +
                    "Belgeyi kaydetmeyin; Undo, Product Reset, Reset All ve Keep kullanmayın.");
            }
            finally
            {
                if (ownsSelectionRestore)
                    RestoreAutomaticSelectionTokens(runtime.Selection, originalSelection);
            }
        }

        private void StartUiAutomaticBranch(AutomaticAbState state, AutomaticAbProgress progress)
        {
            progress.Stage = "B temiz Automatic başlangıç okuması";
            AutomaticAbRuntime runtime = OpenAutomaticAbRuntime(state);
            List<object> originalSelection = CaptureAutomaticSelectionTokens(runtime.Selection);
            try
            {
                RevalidateAutomaticAbRuntime(runtime, state.Target);
                NativeResetColorState baseline = ReadAutomaticAbState(runtime.Selection, state.Target);
                LogAutomaticAbIdentity("B — BAŞLANGIÇ", runtime, state.Target);
                LogAutomaticAbState("B — TEMİZ UI AUTOMATIC BAŞLANGICI", baseline);
                if (!baseline.AllReadsSucceeded)
                {
                    throw new InvalidOperationException(
                        "B başlangıcındaki dört grafik sorgusunun tamamı okunamadı; SetRealColor çağrılmadı.");
                }

                NativeResetComparison baselineComparison = CompareNativeResetStates(state.Initial, baseline);
                LogAutomaticAbComparison(
                    "B BAŞLANGIÇ ↔ A BAŞLANGIÇ",
                    "sol=A BAŞLANGIÇ, sağ=B BAŞLANGIÇ",
                    baselineComparison);
                if (AutomaticAbHasDefiniteDifference(baselineComparison))
                {
                    throw new InvalidOperationException(
                        "B başlangıcı A başlangıcından kesin olarak farklı okundu. PartBody'yi UI'da Color > Automatic " +
                        "yapıp aynı komutu yeniden çalıştırın; test rengi yazılmadı.");
                }

                if (!OnayWindow.Sor(
                        this,
                        "PARTBODY AUTOMATIC A/B — B",
                        "A dalında ResetProperty sonrasında Color listesinin Automatic olup olmadığını not etmiş olmalısınız.\n" +
                        "Şu anda aynı PartBody UI'da Color > Automatic temiz başlangıcında olmalıdır.\n\n" +
                        "Yalnız RGB 255, 35, 170; inheritance=1 tekrar uygulanacak.\n" +
                        "Sonrasında kod reset yapmadan duracak ve UI Automatic seçiminizi bekleyecektir.\n\n" +
                        "Bu temiz başlangıcı onaylıyor musunuz?",
                        "B test rengini uygula"))
                {
                    LogInfo("PartBody Automatic A/B — B kullanıcı tarafından yazmadan önce iptal edildi; renk yazılmadı.");
                    return;
                }

                progress.Stage = "B SetRealColor öncesi güvenlik kontrolü";
                RevalidateAutomaticAbRuntime(runtime, state.Target);
                progress.Stage = "B SetRealColor(255,35,170,1)";
                progress.MutationCallReached = true;
                ApplyAutomaticAbTestColor(runtime.Selection, state.Target);

                state.UiBranchBaseline = baseline;
                state.Stage = AutomaticAbStage.AwaitUiAutomaticRead;
                progress.Stage = "B test rengi sonrası salt-okunur okuma";
                state.AfterUiBranchWrite = ReadAutomaticAbState(runtime.Selection, state.Target);
                LogAutomaticAbState("B — TEST RENGİ SONRASI", state.AfterUiBranchWrite);

                LogInfo(
                    "B test rengi yazıldı. Şimdi CATIA UI'dan seçili PartBody için yalnız Color > Automatic seçin.\n" +
                    "Ardından aynı 'PARTBODY AUTOMATIC A/B TESTİ' komutunu yeniden çalıştırın; üçüncü çağrı yalnız okuyacaktır.\n" +
                    "ResetProperty, Product Reset, Reset All, Keep, Undo ve Save kullanmayın.");
            }
            finally
            {
                RestoreAutomaticSelectionTokens(runtime.Selection, originalSelection);
            }
        }

        private void CompleteUiAutomaticBranch(AutomaticAbState state, AutomaticAbProgress progress)
        {
            progress.Stage = "B UI Automatic sonrası kullanıcı doğrulaması";
            AutomaticAbRuntime runtime = OpenAutomaticAbRuntime(state);
            List<object> originalSelection = CaptureAutomaticSelectionTokens(runtime.Selection);
            try
            {
                RevalidateAutomaticAbRuntime(runtime, state.Target);
                if (!OnayWindow.Sor(
                        this,
                        "PARTBODY AUTOMATIC A/B — B OKUMA",
                        "Bu komuttan önce CATIA UI'da aynı PartBody için Color > Automatic seçmiş olmalısınız.\n\n" +
                        "Devam edilirse hiçbir renk/reset yazılmayacak; yalnız dört grafik property okunup A ile karşılaştırılacaktır.\n" +
                        "Color > Automatic işlemini tamamladığınızı onaylıyor musunuz?",
                        "Yalnız oku ve karşılaştır"))
                {
                    LogInfo("PartBody Automatic A/B — B okuması ertelendi; hiçbir CATIA property çağrısıyla yazma yapılmadı.");
                    return;
                }

                progress.Stage = "B UI Automatic sonrası salt-okunur okuma";
                NativeResetColorState afterUiAutomatic = ReadAutomaticAbState(runtime.Selection, state.Target);
                LogAutomaticAbIdentity("B — UI AUTOMATIC SONRASI", runtime, state.Target);
                LogAutomaticAbState("B — UI AUTOMATIC SONRASI", afterUiAutomatic);

                NativeResetComparison aVsB = CompareNativeResetStates(state.AfterAutomationReset, afterUiAutomatic);
                NativeResetComparison initialVsA = CompareNativeResetStates(state.Initial, state.AfterAutomationReset);
                NativeResetComparison bBaselineVsB = CompareNativeResetStates(state.UiBranchBaseline, afterUiAutomatic);

                LogAutomaticAbComparison(
                    "ANA KARŞILAŞTIRMA: A AUTOMATION RESET ↔ B UI AUTOMATIC",
                    "sol=A ResetProperty, sağ=B Color > Automatic",
                    aVsB);
                LogAutomaticAbComparison(
                    "A KONTROLÜ: BAŞLANGIÇ ↔ A AUTOMATION RESET",
                    "sol=A başlangıç, sağ=A ResetProperty",
                    initialVsA);
                LogAutomaticAbComparison(
                    "B KONTROLÜ: B BAŞLANGIÇ ↔ B UI AUTOMATIC",
                    "sol=B temiz başlangıç, sağ=B Color > Automatic",
                    bBaselineVsB);

                string result;
                if (AutomaticAbHasDefiniteDifference(aVsB))
                {
                    result = "SONUÇ: A ve B arasında en az bir kesin fark okundu. ResetProperty ile UI Automatic eşdeğerliği doğrulanmadı.";
                    LogError(result);
                }
                else if (AutomaticAbHasUnknown(aVsB))
                {
                    result = "SONUÇ: A ve B arasında kesin fark okunmadı; en az bir alan UnDefined/başarısız olduğu için eşdeğerlik BELİRSİZ.";
                    LogInfo(result);
                }
                else
                {
                    result = "SONUÇ: A ve B'nin dört Automation alanı aynı okundu. Nihai gerçek CATIA kararı için A sonrası UI listesinin Automatic gözlemi de kullanıcı tarafından doğrulanmalıdır.";
                    LogSuccess(result);
                }

                LogInfo(
                    "PartBody Automatic A/B akışı tamamlandı. Belge otomatik kaydedilmedi; test belgesini kaydetmeyin. " +
                    "A sonrası Color listesinin elle gözlenen sonucunu logla birlikte raporlayın.");
                _automaticAbState = null;
            }
            finally
            {
                RestoreAutomaticSelectionTokens(runtime.Selection, originalSelection);
            }
        }

        private AutomaticAbRuntime OpenAutomaticAbRuntime(AutomaticAbState? state)
        {
            object? catiaObject = GetCatia();
            if (catiaObject == null)
                throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

            object editorObject = (object)((dynamic)catiaObject).ActiveEditor;
            var runtime = new AutomaticAbRuntime(
                catiaObject,
                editorObject,
                ((dynamic)editorObject).Selection,
                CatiaColorTargetService.GetSessionObjectKey(catiaObject),
                CatiaColorTargetService.GetSessionObjectKey(editorObject));

            if (state != null)
            {
                if (!string.Equals(runtime.CatiaSessionKey, state.CatiaSessionKey, StringComparison.Ordinal) ||
                    !string.Equals(runtime.EditorSessionKey, state.EditorSessionKey, StringComparison.Ordinal))
                {
                    _automaticAbState = null;
                    throw new InvalidOperationException(
                        "CATIA oturumu veya aktif editör değişti. Bekleyen A/B state'i güvenlik nedeniyle bırakıldı; testi baştan başlatın.");
                }
            }

            return runtime;
        }

        private static AutomaticAbTarget CaptureAutomaticAbTarget(dynamic selection)
        {
            int count;
            try { count = Convert.ToInt32(selection.Count2); }
            catch { count = Convert.ToInt32(selection.Count); }
            if (count != 1)
                throw new InvalidOperationException("CATIA'da tam olarak bir PartBody seçin (okunan seçim sayısı=" + count + ").");

            object selectedElement;
            try { selectedElement = selection.Item2(1); }
            catch { selectedElement = selection.Item(1); }

            object value = ((dynamic)selectedElement).Value;
            if (value == null)
                throw new InvalidOperationException("Seçili PartBody'nin Automation Value nesnesi alınamadı.");

            string comType = ComProbe.TipAdi(value);
            string selectionType = Convert.ToString(((dynamic)selectedElement).Type)?.Trim() ?? "";
            if (!IsResetTeshisiBody(comType) ||
                selectionType.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    "Seçim PartBody/Body olarak doğrulanamadı: SelectedElement.Type=" + selectionType +
                    " | Value COM türü=" + comType + ".");
            }

            string name;
            try { name = Convert.ToString(((dynamic)value).Name)?.Trim() ?? ""; }
            catch { name = ""; }

            return new AutomaticAbTarget(
                value,
                selectedElement,
                comType,
                selectionType,
                CatiaColorTargetService.GetSessionObjectKey(value),
                string.IsNullOrWhiteSpace(name) ? "(ad okunamadı)" : name);
        }

        private void RevalidateAutomaticAbRuntime(AutomaticAbRuntime runtime, AutomaticAbTarget target)
        {
            object? currentCatia = GetCatia();
            if (currentCatia == null ||
                !string.Equals(
                    CatiaColorTargetService.GetSessionObjectKey(currentCatia),
                    runtime.CatiaSessionKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CATIA oturumu değişti; teşhis işlemi durduruldu.");
            }

            object currentEditor = (object)((dynamic)currentCatia).ActiveEditor;
            if (!string.Equals(
                    CatiaColorTargetService.GetSessionObjectKey(currentEditor),
                    runtime.EditorSessionKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Aktif CATIA editörü değişti; teşhis işlemi durduruldu.");
            }

            string currentBodyKey = CatiaColorTargetService.GetSessionObjectKey(target.Value);
            if (!string.Equals(currentBodyKey, target.SessionObjectKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Saklanan PartBody COM hedefi değişti; teşhis işlemi durduruldu.");
        }

        private static NativeResetColorState ReadAutomaticAbState(dynamic selection, AutomaticAbTarget target)
        {
            try
            {
                dynamic properties = SelectAutomaticAbTarget(selection, target);
                CatiaColorRgb realColor = ReadResetTeshisiRgb((object)properties, false, out string realColorError);
                CatiaColorRgb visibleColor = ReadResetTeshisiRgb((object)properties, true, out string visibleColorError);
                CatiaColorInheritance realInheritance = ReadResetTeshisiInheritance(
                    (object)properties,
                    false,
                    out string realInheritanceError);
                CatiaColorInheritance visibleInheritance = ReadResetTeshisiInheritance(
                    (object)properties,
                    true,
                    out string visibleInheritanceError);

                return new NativeResetColorState(
                    AutomaticAbColorResult(realColor, realColorError),
                    AutomaticAbColorResult(visibleColor, visibleColorError),
                    AutomaticAbInheritanceResult(realInheritance, realInheritanceError),
                    AutomaticAbInheritanceResult(visibleInheritance, visibleInheritanceError));
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        private static CatiaColorOperationResult AutomaticAbColorResult(CatiaColorRgb value, string error) =>
            string.IsNullOrWhiteSpace(error)
                ? CatiaColorOperationResult.Ok(value)
                : CatiaColorOperationResult.Fail(error);

        private static CatiaColorInheritanceOperationResult AutomaticAbInheritanceResult(
            CatiaColorInheritance value,
            string error) =>
            string.IsNullOrWhiteSpace(error)
                ? CatiaColorInheritanceOperationResult.Ok(value)
                : CatiaColorInheritanceOperationResult.Fail(error);

        private static dynamic SelectAutomaticAbTarget(dynamic selection, AutomaticAbTarget target)
        {
            selection.Clear();
            // İlk kullanıcı seçimindeki SelectedElement token'ı korunur; Body.Value yeniden aranmaz.
            selection.Add(target.SelectedElement);

            int count;
            try { count = Convert.ToInt32(selection.Count2); }
            catch { count = Convert.ToInt32(selection.Count); }
            if (count != 1)
                throw new InvalidOperationException("PartBody seçim context'i tek öğe olarak yeniden kurulamadı.");

            object reboundSelectedElement;
            try { reboundSelectedElement = selection.Item2(1); }
            catch { reboundSelectedElement = selection.Item(1); }
            object reboundValue = ((dynamic)reboundSelectedElement).Value;

            string reboundType = ComProbe.TipAdi(reboundValue);
            string reboundSelectionType = Convert.ToString(((dynamic)reboundSelectedElement).Type)?.Trim() ?? "";
            string reboundKey = CatiaColorTargetService.GetSessionObjectKey(reboundValue);
            if (!string.Equals(reboundType, target.ComType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(reboundSelectionType, target.SelectionType, StringComparison.Ordinal) ||
                !string.Equals(reboundKey, target.SessionObjectKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Selection.Add sonrasında PartBody context'i değişti; okuma/yazma durduruldu. " +
                    "Beklenen Type=" + target.SelectionType + ", COM=" + target.ComType + ", identity=" + target.SessionObjectKey +
                    " | Okunan Type=" + reboundSelectionType + ", COM=" + reboundType + ", identity=" + reboundKey + ".");
            }

            return selection.VisProperties;
        }

        private static void ApplyAutomaticAbTestColor(dynamic selection, AutomaticAbTarget target)
        {
            try
            {
                dynamic properties = SelectAutomaticAbTarget(selection, target);
                // DSYAutomation.chm / InfTypeLib.tlb: SetRealColor(long,long,long,long).
                properties.SetRealColor(
                    AutomaticAbTestRed,
                    AutomaticAbTestGreen,
                    AutomaticAbTestBlue,
                    1);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        private static void ResetAutomaticAbColor(dynamic selection, AutomaticAbTarget target)
        {
            try
            {
                dynamic properties = SelectAutomaticAbTarget(selection, target);
                // DSYAutomation.chm / InfTypeLib.tlb: ResetProperty(CatVisPropertyType).
                properties.ResetProperty(AutomaticAbCatVisPropertyColor);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        private void LogAutomaticAbIdentity(string stage, AutomaticAbRuntime runtime, AutomaticAbTarget target)
        {
            LogInfo(
                "PartBody Automatic A/B — " + stage + " HEDEF" +
                " | CATIA oturumu=" + runtime.CatiaSessionKey +
                " | aktif editör=" + runtime.EditorSessionKey +
                " | Body adı=" + target.Name +
                " | SelectedElement.Type=" + target.SelectionType +
                " | Body COM türü=" + target.ComType +
                " | Body COM identity=" + target.SessionObjectKey);
        }

        private void LogAutomaticAbState(string stage, NativeResetColorState state)
        {
            var text = new StringBuilder();
            text.AppendLine("PartBody Automatic A/B — " + stage);
            text.AppendLine("Real RGB: " + NativeResetColorText(state.RealColor));
            text.AppendLine("Visible RGB: " + NativeResetColorText(state.VisibleColor));
            text.AppendLine("Real inheritance: " + NativeResetInheritanceText(state.RealInheritance));
            text.Append("Visible inheritance: " + NativeResetInheritanceText(state.VisibleInheritance));

            if (state.AllReadsSucceeded)
                LogInfo(text.ToString());
            else
                LogError(text.ToString());
        }

        private void LogAutomaticAbComparison(
            string title,
            string sides,
            NativeResetComparison comparison)
        {
            var text = new StringBuilder();
            text.AppendLine("PartBody Automatic A/B — " + title);
            text.AppendLine(sides);
            text.AppendLine(comparison.RealColor.Text);
            text.AppendLine(comparison.VisibleColor.Text);
            text.AppendLine(comparison.RealInheritance.Text);
            text.AppendLine(comparison.VisibleInheritance.Text);

            if (AutomaticAbHasDefiniteDifference(comparison))
            {
                text.Append("Karşılaştırma: FARKLI — en az bir alan kesin olarak farklı.");
                LogError(text.ToString());
            }
            else if (AutomaticAbHasUnknown(comparison))
            {
                text.Append("Karşılaştırma: BELİRSİZ — kesin fark yok, fakat en az bir alan tam karşılaştırılamadı.");
                LogInfo(text.ToString());
            }
            else
            {
                text.Append("Karşılaştırma: AYNI — dört alanın status ve geçerli output değerleri aynı.");
                LogSuccess(text.ToString());
            }
        }

        private static bool AutomaticAbHasDefiniteDifference(NativeResetComparison comparison) =>
            string.Equals(comparison.RealColor.Answer, "HAYIR", StringComparison.Ordinal) ||
            string.Equals(comparison.VisibleColor.Answer, "HAYIR", StringComparison.Ordinal) ||
            string.Equals(comparison.RealInheritance.Answer, "HAYIR", StringComparison.Ordinal) ||
            string.Equals(comparison.VisibleInheritance.Answer, "HAYIR", StringComparison.Ordinal);

        private static bool AutomaticAbHasUnknown(NativeResetComparison comparison) =>
            string.Equals(comparison.RealColor.Answer, "BELİRSİZ", StringComparison.Ordinal) ||
            string.Equals(comparison.VisibleColor.Answer, "BELİRSİZ", StringComparison.Ordinal) ||
            string.Equals(comparison.RealInheritance.Answer, "BELİRSİZ", StringComparison.Ordinal) ||
            string.Equals(comparison.VisibleInheritance.Answer, "BELİRSİZ", StringComparison.Ordinal) ||
            !comparison.ExactRestoreObserved;

        private static List<object> CaptureAutomaticSelectionTokens(dynamic selection)
        {
            var tokens = new List<object>();
            int count;
            try { count = Convert.ToInt32(selection.Count2); }
            catch
            {
                try { count = Convert.ToInt32(selection.Count); }
                catch { return tokens; }
            }

            for (int index = 1; index <= count; index++)
            {
                try { tokens.Add((object)selection.Item2(index)); }
                catch
                {
                    try { tokens.Add((object)selection.Item(index)); }
                    catch { }
                }
            }

            return tokens;
        }

        private static void RestoreAutomaticSelectionTokens(dynamic selection, IReadOnlyList<object> tokens)
        {
            try
            {
                selection.Clear();
                foreach (object token in tokens)
                {
                    try { selection.Add(token); } catch { }
                }
            }
            catch { }
        }

        private enum AutomaticAbStage
        {
            AwaitAutomationReset,
            AwaitUiBranchWrite,
            AwaitUiAutomaticRead
        }

        private sealed class AutomaticAbState
        {
            public AutomaticAbState(
                string catiaSessionKey,
                string editorSessionKey,
                AutomaticAbTarget target,
                NativeResetColorState initial)
            {
                CatiaSessionKey = catiaSessionKey;
                EditorSessionKey = editorSessionKey;
                Target = target;
                Initial = initial;
                AfterAutomationWrite = initial;
                AfterAutomationReset = initial;
                UiBranchBaseline = initial;
                AfterUiBranchWrite = initial;
                Stage = AutomaticAbStage.AwaitAutomationReset;
            }

            public string CatiaSessionKey { get; }
            public string EditorSessionKey { get; }
            public AutomaticAbTarget Target { get; }
            public NativeResetColorState Initial { get; }
            public NativeResetColorState AfterAutomationWrite { get; set; }
            public NativeResetColorState AfterAutomationReset { get; set; }
            public NativeResetColorState UiBranchBaseline { get; set; }
            public NativeResetColorState AfterUiBranchWrite { get; set; }
            public AutomaticAbStage Stage { get; set; }
        }

        private sealed class AutomaticAbTarget
        {
            public AutomaticAbTarget(
                object value,
                object selectedElement,
                string comType,
                string selectionType,
                string sessionObjectKey,
                string name)
            {
                Value = value;
                SelectedElement = selectedElement;
                ComType = comType;
                SelectionType = selectionType;
                SessionObjectKey = sessionObjectKey;
                Name = name;
            }

            public object Value { get; }
            public object SelectedElement { get; }
            public string ComType { get; }
            public string SelectionType { get; }
            public string SessionObjectKey { get; }
            public string Name { get; }
        }

        private sealed class AutomaticAbRuntime
        {
            public AutomaticAbRuntime(
                object catiaObject,
                object editorObject,
                object selection,
                string catiaSessionKey,
                string editorSessionKey)
            {
                CatiaObject = catiaObject;
                EditorObject = editorObject;
                Selection = selection;
                CatiaSessionKey = catiaSessionKey;
                EditorSessionKey = editorSessionKey;
            }

            public object CatiaObject { get; }
            public object EditorObject { get; }
            public dynamic Selection { get; }
            public string CatiaSessionKey { get; }
            public string EditorSessionKey { get; }
        }

        private sealed class AutomaticAbProgress
        {
            public string Stage { get; set; } = "CATIA/Selection context okuma";
            public bool MutationCallReached { get; set; }
        }
    }
}
