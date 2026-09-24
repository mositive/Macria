using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        // InfTypeLib.tlb / CatVisPropertyType: catVisPropertyColor = 2.
        private const int ResetTeshisiCatVisPropertyColor = 2;

        private void ResetPropertyKontrolluTeshisi_Click(object sender, RoutedEventArgs e)
        {
            ResetTeshisiContext? context = null;
            string operationStage = "CATIA/Selection context okuma";
            bool mutationMayHaveOccurred = false;
            try
            {
                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic editor = ((dynamic)catiaObject).ActiveEditor;
                dynamic selection = editor.Selection;
                context = CaptureResetTeshisiContext(catiaObject, (object)editor, selection);

                LogInfo(
                    "ResetProperty kontrollü teşhisi başlatıldı" +
                    " | CATIA oturumu=" + context.CatiaSessionKey +
                    " | aktif editör=" + context.EditorSessionKey +
                    " | PartBody=" + context.Body.SessionObjectKey +
                    " | Yüz A=" + context.FaceA.SessionObjectKey +
                    " | Yüz B=" + context.FaceB.SessionObjectKey +
                    "\nBu teşhis otomatik kayıt ve Undo kullanmaz. Test tamamlandıktan sonra CATIA belgesini kaydetmeyin.");

                operationStage = "başlangıç renklerini salt-okunur okuma";
                ResetTeshisiStage initial = ReadResetTeshisiStage(selection, "BAŞLANGIÇ", context);
                LogResetTeshisiStage(initial);
                RestoreResetTeshisiSelection(selection, context.OriginalSelection);

                AkilliRenk bodyTestColor = SelectResetTeshisiBodyColor(initial);
                AkilliRenk turquoise = SelectResetTeshisiTurquoise(initial.FaceB);

                if (!OnayWindow.Sor(
                        this,
                        "ResetProperty teşhisi — PartBody rengi",
                        "Yalnız seçili PartBody'ye geçici test rengi uygulanacak.\n\n" +
                        "PartBody COM: " + context.Body.ComType + "\n" +
                        "PartBody hedefi: " + context.Body.SessionObjectKey + "\n" +
                        "Test RGB: " + RenkMetni(bodyTestColor) + "\n" +
                        "Inheritance: 1\n\n" +
                        "Bu işlem gerçek CATIA grafik durumunu değiştirir. Undo ve otomatik kayıt kullanılmaz. " +
                        "Test belgesini kaydetmeyin.",
                        "PartBody test rengini uygula"))
                {
                    LogInfo("ResetProperty kontrollü teşhisi kullanıcı tarafından ilk yazmadan önce iptal edildi; renk yazılmadı.");
                    return;
                }

                operationStage = "PartBody SetRealColor öncesi güvenlik kontrolü";
                EnsureResetTeshisiContext(context);
                operationStage = "PartBody SetRealColor çağrısı";
                mutationMayHaveOccurred = true;
                ApplyResetTeshisiColor(selection, context.Body, bodyTestColor, 1);
                operationStage = "PartBody rengi sonrası salt-okunur okuma";
                ResetTeshisiStage afterBody = ReadResetTeshisiStage(selection, "BODY RENK SONRASI", context);
                LogResetTeshisiStage(afterBody);
                LogFaceRealRgbComparison("Yüz A", initial.FaceA, afterBody.FaceA);
                LogFaceRealRgbComparison("Yüz B", initial.FaceB, afterBody.FaceB);
                RestoreResetTeshisiSelection(selection, context.OriginalSelection);

                if (!OnayWindow.Sor(
                        this,
                        "ResetProperty teşhisi — Yüz B rengi",
                        "Yalnız elle seçtiğiniz Yüz B'ye turkuaz test rengi uygulanacak.\n\n" +
                        "Yüz B COM: " + context.FaceB.ComType + "\n" +
                        "Yüz B hedefi: " + context.FaceB.SessionObjectKey + "\n" +
                        "Turkuaz RGB: " + RenkMetni(turquoise) + "\n" +
                        "Inheritance: 1\n\n" +
                        "PartBody test rengi CATIA'da kalır. Test belgesini kaydetmeyin.",
                        "Yüz B turkuazını uygula"))
                {
                    LogError(
                        "ResetProperty kontrollü teşhisi Yüz B yazımından önce durduruldu. " +
                        "PartBody geçici test rengi CATIA'da kalmış olabilir; belgeyi kaydetmeyin.");
                    return;
                }

                operationStage = "Yüz B SetRealColor öncesi güvenlik kontrolü";
                EnsureResetTeshisiContext(context);
                operationStage = "Yüz B SetRealColor çağrısı";
                mutationMayHaveOccurred = true;
                ApplyResetTeshisiColor(selection, context.FaceB, turquoise, 1);
                operationStage = "Yüz B rengi sonrası salt-okunur okuma";
                ResetTeshisiStage afterFace = ReadResetTeshisiStage(selection, "YÜZ B RENK SONRASI", context);
                LogResetTeshisiStage(afterFace);
                LogTurquoiseVisibilityFinding(afterFace.FaceB, bodyTestColor, turquoise);
                RestoreResetTeshisiSelection(selection, context.OriginalSelection);

                if (!OnayWindow.Sor(
                        this,
                        "ResetProperty teşhisi — PartBody reset",
                        "Yalnız seçili PartBody üzerinde ResetProperty(catVisPropertyColor) çağrılacak.\n\n" +
                        "PartBody hedefi: " + context.Body.SessionObjectKey + "\n\n" +
                        "Bu çağrının eski kullanıcı renklerini geri getireceği henüz doğrulanmamıştır. " +
                        "Undo ve otomatik kayıt kullanılmaz. Test belgesini kaydetmeyin.",
                        "PartBody rengini resetle"))
                {
                    LogError(
                        "ResetProperty kontrollü teşhisi Reset öncesinde durduruldu. " +
                        "PartBody ve Yüz B test renkleri CATIA'da kalmış olabilir; belgeyi kaydetmeyin.");
                    return;
                }

                operationStage = "PartBody ResetProperty öncesi güvenlik kontrolü";
                EnsureResetTeshisiContext(context);
                operationStage = "PartBody ResetProperty çağrısı";
                mutationMayHaveOccurred = true;
                ResetTeshisiColorProperty(selection, context.Body);
                operationStage = "Reset sonrası salt-okunur okuma";
                ResetTeshisiStage afterReset = ReadResetTeshisiStage(selection, "RESET SONRASI", context);
                LogResetTeshisiStage(afterReset);
                LogResetTeshisiComparison(initial, afterBody, afterFace, afterReset, bodyTestColor, turquoise);

                LogSuccess(
                    "ResetProperty kontrollü teşhisi tamamlandı. Gerçek CATIA ölçümleri loglandı; " +
                    "A/B kararı otomatik verilmedi. CATIA test belgesini kaydetmeyin.");
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("ResetProperty kontrollü teşhisi", ex);
                LogError(
                    "ResetProperty kontrollü teşhisi tamamlanamadı: " + Kisa(ex.Message) +
                    " | Aşama=" + operationStage +
                    (mutationMayHaveOccurred
                        ? " | Bir renk/reset çağrısına ulaşılmıştır; geçici grafik değişikliği kalmış olabilir. CATIA belgesini kaydetmeyin."
                        : " | Renk yazma/reset aşamasına ulaşılmadı; CATIA grafik özelliği değiştirilmedi."));
            }
            finally
            {
                if (context != null)
                    TryRestoreResetTeshisiSelection(context);
            }
        }

        private static ResetTeshisiContext CaptureResetTeshisiContext(
            object catiaObject,
            object editorObject,
            dynamic selection)
        {
            int count;
            try { count = Convert.ToInt32(selection.Count2); }
            catch { count = Convert.ToInt32(selection.Count); }

            if (count != 3)
            {
                throw new InvalidOperationException(
                    "CATIA'da tam olarak üç öğe seçin: bir PartBody ve aynı PartBody'ye ait iki test yüzü.");
            }

            var items = new List<ResetTeshisiTarget>();
            for (int index = 1; index <= count; index++)
            {
                object selectedElement;
                object value;
                try
                {
                    selectedElement = selection.Item2(index);
                    value = ((dynamic)selectedElement).Value;
                }
                catch
                {
                    selectedElement = selection.Item(index);
                    value = ((dynamic)selectedElement).Value;
                }

                if (value == null)
                    throw new InvalidOperationException("Seçimin " + index + ". öğesinin Automation değeri alınamadı.");

                items.Add(new ResetTeshisiTarget(
                    "Seçim " + index,
                    value,
                    selectedElement,
                    ComProbe.TipAdi(value),
                    CatiaColorTargetService.GetSessionObjectKey(value),
                    ReadResetTeshisiSelectionType(selectedElement),
                    ReadResetTeshisiReferenceDisplayName(selectedElement),
                    ReadResetTeshisiLeafProductKey(selectedElement)));
            }

            List<ResetTeshisiTarget> bodies = items.Where(item => IsResetTeshisiBody(item.ComType)).ToList();
            List<ResetTeshisiTarget> faces = items.Where(item => IsResetTeshisiFace(item.ComType)).ToList();
            if (bodies.Count != 1 || faces.Count != 2)
            {
                throw new InvalidOperationException(
                    "Seçim 1 Body ve 2 Face olarak doğrulanamadı. Okunan türler: " +
                    string.Join(", ", items.Select(item => item.ComType)) + ".");
            }

            if (items.Select(item => item.SessionObjectKey).Distinct(StringComparer.Ordinal).Count() != 3)
                throw new InvalidOperationException("PartBody ve iki test yüzü üç farklı COM hedefi olarak doğrulanamadı.");

            ResetTeshisiTarget body = bodies[0].WithLabel("PartBody");
            ResetTeshisiTarget faceA = faces[0].WithLabel("Yüz A");
            ResetTeshisiTarget faceB = faces[1].WithLabel("Yüz B");

            return new ResetTeshisiContext(
                catiaObject,
                editorObject,
                selection,
                CatiaColorTargetService.GetSessionObjectKey(catiaObject),
                CatiaColorTargetService.GetSessionObjectKey(editorObject),
                body,
                faceA,
                faceB,
                items);
        }

        private static bool IsResetTeshisiBody(string comType) =>
            string.Equals(comType, "Body", StringComparison.OrdinalIgnoreCase) ||
            comType.EndsWith("Body", StringComparison.OrdinalIgnoreCase);

        private static bool IsResetTeshisiFace(string comType) =>
            comType.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void EnsureResetTeshisiContext(ResetTeshisiContext context)
        {
            string catiaKey = CatiaColorTargetService.GetSessionObjectKey(context.CatiaObject);
            if (!string.Equals(catiaKey, context.CatiaSessionKey, StringComparison.Ordinal))
                throw new InvalidOperationException("CATIA oturumu değişti; teşhis yazması durduruldu.");

            object currentEditor = ((dynamic)context.CatiaObject).ActiveEditor;
            string editorKey = CatiaColorTargetService.GetSessionObjectKey(currentEditor);
            if (!string.Equals(editorKey, context.EditorSessionKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Aktif CATIA editörü değişti; teşhis yazması durduruldu.");

            VerifyResetTeshisiTargetIdentity(context.Body);
            VerifyResetTeshisiTargetIdentity(context.FaceA);
            VerifyResetTeshisiTargetIdentity(context.FaceB);
        }

        private static void VerifyResetTeshisiTargetIdentity(ResetTeshisiTarget target)
        {
            if (IsResetTeshisiFace(target.ComType))
            {
                VerifyResetTeshisiSelectionContext(target, target.SelectedElement, "saklanan Selection context'i");
                return;
            }

            string currentKey = CatiaColorTargetService.GetSessionObjectKey(target.Value);
            if (!string.Equals(currentKey, target.SessionObjectKey, StringComparison.Ordinal))
                throw new InvalidOperationException(target.Label + " COM hedefi değişti; teşhis durduruldu.");
        }

        private static ResetTeshisiStage ReadResetTeshisiStage(
            dynamic selection,
            string stage,
            ResetTeshisiContext context)
        {
            EnsureResetTeshisiContext(context);
            return new ResetTeshisiStage(
                stage,
                ReadResetTeshisiTarget(selection, context.Body),
                ReadResetTeshisiTarget(selection, context.FaceA),
                ReadResetTeshisiTarget(selection, context.FaceB));
        }

        private static ResetTeshisiColorState ReadResetTeshisiTarget(
            dynamic selection,
            ResetTeshisiTarget target)
        {
            try
            {
                dynamic properties = SelectResetTeshisiTarget(selection, target);

                CatiaColorRgb realColor = ReadResetTeshisiRgb(
                    (object)properties,
                    false,
                    out string realColorError);
                CatiaColorInheritance realInheritance = ReadResetTeshisiInheritance(
                    (object)properties,
                    false,
                    out string realInheritanceError);
                CatiaColorRgb visibleColor = ReadResetTeshisiRgb(
                    (object)properties,
                    true,
                    out string visibleColorError);
                CatiaColorInheritance visibleInheritance = ReadResetTeshisiInheritance(
                    (object)properties,
                    true,
                    out string visibleInheritanceError);

                return ResetTeshisiColorState.Ok(
                    target,
                    realColor,
                    realInheritance,
                    visibleColor,
                    visibleInheritance,
                    realColorError,
                    realInheritanceError,
                    visibleColorError,
                    visibleInheritanceError);
            }
            catch (Exception ex)
            {
                return ResetTeshisiColorState.Fail(target, RenkTestHataAyrintisi("VisProperties okuma", ex));
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        private static CatiaColorRgb ReadResetTeshisiRgb(
            object propertiesObject,
            bool visible,
            out string error)
        {
            try
            {
                dynamic properties = propertiesObject;
                int red = 0;
                int green = 0;
                int blue = 0;
                object? statusObject = visible
                    ? properties.GetVisibleColor(ref red, ref green, ref blue)
                    : properties.GetRealColor(ref red, ref green, ref blue);
                error = "";
                return new CatiaColorRgb(red, green, blue, Convert.ToInt32(statusObject));
            }
            catch (Exception ex)
            {
                error = RenkTestHataAyrintisi(
                    visible ? "GetVisibleColor" : "GetRealColor",
                    ex);
                return new CatiaColorRgb(0, 0, 0, -1);
            }
        }

        private static CatiaColorInheritance ReadResetTeshisiInheritance(
            object propertiesObject,
            bool visible,
            out string error)
        {
            try
            {
                dynamic properties = propertiesObject;
                int inheritance = 0;
                object? statusObject = visible
                    ? properties.GetVisibleInheritance(ResetTeshisiCatVisPropertyColor, ref inheritance)
                    : properties.GetRealInheritance(ResetTeshisiCatVisPropertyColor, ref inheritance);
                error = "";
                return new CatiaColorInheritance(inheritance, Convert.ToInt32(statusObject));
            }
            catch (Exception ex)
            {
                error = RenkTestHataAyrintisi(
                    visible ? "GetVisibleInheritance(catVisPropertyColor)" :
                              "GetRealInheritance(catVisPropertyColor)",
                    ex);
                return new CatiaColorInheritance(0, -1);
            }
        }

        private static dynamic SelectResetTeshisiTarget(dynamic selection, ResetTeshisiTarget target)
        {
            selection.Clear();
            // Selection dokümanı çoklu-instance bağlamını korumak için SelectedElement'ın
            // yeniden eklenmesini önerir; çıplak Value yerine ilk seçim token'ı kullanılır.
            selection.Add(target.SelectedElement);

            int count;
            try { count = Convert.ToInt32(selection.Count2); }
            catch { count = Convert.ToInt32(selection.Count); }
            if (count != 1)
                throw new InvalidOperationException(target.Label + " seçim bağlamı tek öğe olarak kurulamadı.");

            object reboundSelectedElement;
            object selectedValue;
            try { reboundSelectedElement = selection.Item2(1); }
            catch { reboundSelectedElement = selection.Item(1); }
            selectedValue = ((dynamic)reboundSelectedElement).Value;

            if (IsResetTeshisiFace(target.ComType))
            {
                // DSYAutomation Selection.Add belgesi, multi-instance bağlamını korumak için
                // ilk SelectedElement nesnesinin yeniden eklenmesini söyler. Topolojik Face
                // Value dispatch'i yeniden üretilebildiğinden yüz güvenliği Value IUnknown'ı
                // yerine belgelenmiş Reference + LeafProduct selection context'iyle doğrulanır.
                VerifyResetTeshisiSelectionContext(target, reboundSelectedElement, "Selection.Add sonrası context");

                string reboundType = ComProbe.TipAdi(selectedValue);
                if (!string.Equals(reboundType, target.ComType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        target.Label + " Selection.Add sonrasında COM türü değişti; yazma/okuma durduruldu. " +
                        "Beklenen=" + target.ComType + " | Okunan=" + reboundType + ".");
                }

                return selection.VisProperties;
            }

            string selectedKey = CatiaColorTargetService.GetSessionObjectKey(selectedValue);
            if (!string.Equals(selectedKey, target.SessionObjectKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    target.Label + " Selection.Add sonrasında farklı COM hedefine çözüldü; yazma/okuma durduruldu.");
            }

            return selection.VisProperties;
        }

        private static void VerifyResetTeshisiSelectionContext(
            ResetTeshisiTarget target,
            object selectedElement,
            string stage)
        {
            string selectionType = ReadResetTeshisiSelectionType(selectedElement);
            string referenceDisplayName = ReadResetTeshisiReferenceDisplayName(selectedElement);
            string leafProductKey = ReadResetTeshisiLeafProductKey(selectedElement);

            if (!string.Equals(selectionType, target.SelectionType, StringComparison.Ordinal) ||
                !string.Equals(referenceDisplayName, target.ReferenceDisplayName, StringComparison.Ordinal) ||
                !string.Equals(leafProductKey, target.LeafProductKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    target.Label + " " + stage + " değişti; yazma/okuma durduruldu. " +
                    "Beklenen Type=" + target.SelectionType +
                    " | Reference=" + target.ReferenceDisplayName +
                    " | LeafProduct=" + target.LeafProductKey +
                    " || Okunan Type=" + selectionType +
                    " | Reference=" + referenceDisplayName +
                    " | LeafProduct=" + leafProductKey + ".");
            }
        }

        private static string ReadResetTeshisiSelectionType(object selectedElement) =>
            Convert.ToString(((dynamic)selectedElement).Type)?.Trim() ?? "";

        private static string ReadResetTeshisiReferenceDisplayName(object selectedElement)
        {
            object reference = ((dynamic)selectedElement).Reference;
            return Convert.ToString(((dynamic)reference).DisplayName)?.Trim() ?? "";
        }

        private static string ReadResetTeshisiLeafProductKey(object selectedElement)
        {
            object leafProduct = ((dynamic)selectedElement).LeafProduct;
            return CatiaColorTargetService.GetSessionObjectKey(leafProduct);
        }

        private static void ApplyResetTeshisiColor(
            dynamic selection,
            ResetTeshisiTarget target,
            AkilliRenk color,
            int inheritance)
        {
            try
            {
                dynamic properties = SelectResetTeshisiTarget(selection, target);
                // DSYAutomation.chm / InfTypeLib.tlb:
                // SetRealColor(long, long, long, long).
                properties.SetRealColor(
                    (int)color.Kirmizi,
                    (int)color.Yesil,
                    (int)color.Mavi,
                    inheritance);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        private static void ResetTeshisiColorProperty(dynamic selection, ResetTeshisiTarget target)
        {
            try
            {
                dynamic properties = SelectResetTeshisiTarget(selection, target);
                // DSYAutomation.chm / InfTypeLib.tlb:
                // ResetProperty(CatVisPropertyType).
                properties.ResetProperty(ResetTeshisiCatVisPropertyColor);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        private void LogResetTeshisiStage(ResetTeshisiStage stage)
        {
            LogInfo("ResetProperty teşhisi — " + stage.Name);
            LogInfo(FormatResetTeshisiState(stage.Body));
            LogInfo(FormatResetTeshisiState(stage.FaceA));
            LogInfo(FormatResetTeshisiState(stage.FaceB));
        }

        private static string FormatResetTeshisiState(ResetTeshisiColorState state)
        {
            if (!state.Success)
                return state.Target.Label + " | COM=" + state.Target.ComType + " | OKUMA BAŞARISIZ | " + state.Error;

            return state.Target.Label +
                   " | COM=" + state.Target.ComType +
                   " | hedef=" + state.Target.SessionObjectKey +
                   " | Real RGB=" + FormatResetTeshisiRgb(state.RealColor, state.RealColorError) +
                   " | Real Inheritance=" + FormatResetTeshisiInheritance(state.RealInheritance, state.RealInheritanceError) +
                   " | Visible RGB=" + FormatResetTeshisiRgb(state.VisibleColor, state.VisibleColorError) +
                   " | Visible Inheritance=" + FormatResetTeshisiInheritance(state.VisibleInheritance, state.VisibleInheritanceError);
        }

        private static string FormatResetTeshisiRgb(CatiaColorRgb value, string error = "") =>
            string.IsNullOrWhiteSpace(error)
                ? ColorPropertyStatusText(value.Status) +
                  (value.Status == 0 ? " " + RgbText(value) : " (RGB çıkışı geçersiz)")
                : "HATA (" + Kisa(error) + ")";

        private static string FormatResetTeshisiInheritance(CatiaColorInheritance value, string error = "") =>
            string.IsNullOrWhiteSpace(error)
                ? ColorPropertyStatusText(value.Status) +
                  (value.Status == 0 ? " " + value.Inheritance : " (inheritance çıkışı geçersiz)")
                : "HATA (" + Kisa(error) + ")";

        private void LogFaceRealRgbComparison(
            string label,
            ResetTeshisiColorState before,
            ResetTeshisiColorState after)
        {
            if (!TryCompareDefinedRgb(before, after, out bool equal, out string detail))
            {
                LogError("ResetProperty teşhisi — " + label + " REAL RGB karşılaştırılamadı | " + detail);
                return;
            }

            if (equal)
            {
                LogSuccess(
                    "ResetProperty teşhisi — " + label +
                    " REAL RGB KORUNDU; PartBody rengi yalnız görünürlükte bastırıyor olabilir | " + detail);
            }
            else
            {
                LogError(
                    "ResetProperty teşhisi — " + label +
                    " REAL RGB DEĞİŞTİ; overwrite bulgusu var | " + detail);
            }
        }

        private void LogTurquoiseVisibilityFinding(
            ResetTeshisiColorState faceB,
            AkilliRenk bodyColor,
            AkilliRenk turquoise)
        {
            if (!faceB.Success || faceB.RealColor.Status != 0 || faceB.VisibleColor.Status != 0)
            {
                LogError("ResetProperty teşhisi — Yüz B turkuaz Real/Visible karşılaştırması Defined değil.");
                return;
            }

            bool realTurquoise = RgbEquals(faceB.RealColor, turquoise);
            bool visibleTurquoise = RgbEquals(faceB.VisibleColor, turquoise);
            bool visibleBody = RgbEquals(faceB.VisibleColor, bodyColor);
            if (realTurquoise && !visibleTurquoise && visibleBody)
            {
                LogSuccess(
                    "ResetProperty teşhisi — Yüz B REAL turkuaz yazıldı fakat VISIBLE renk PartBody test rengi kaldı; " +
                    "Body inheritance yüz rengini bastırıyor.");
                return;
            }

            LogInfo(
                "ResetProperty teşhisi — Yüz B turkuaz sonucu" +
                " | Real=" + RgbText(faceB.RealColor) +
                " | Visible=" + RgbText(faceB.VisibleColor) +
                " | real turkuaz=" + EvetHayir(realTurquoise) +
                " | visible turkuaz=" + EvetHayir(visibleTurquoise) +
                " | visible Body rengi=" + EvetHayir(visibleBody));
        }

        private void LogResetTeshisiComparison(
            ResetTeshisiStage initial,
            ResetTeshisiStage afterBody,
            ResetTeshisiStage afterFace,
            ResetTeshisiStage afterReset,
            AkilliRenk bodyColor,
            AkilliRenk turquoise)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ResetProperty teşhisi — KARŞILAŞTIRMALI SONUÇ");
            AppendResetTeshisiComparisonRow(sb, "PartBody başlangıç", initial.Body);
            AppendResetTeshisiComparisonRow(sb, "PartBody Body-renk sonrası", afterBody.Body);
            AppendResetTeshisiComparisonRow(sb, "PartBody Reset sonrası", afterReset.Body);
            AppendResetTeshisiComparisonRow(sb, "Yüz A başlangıç", initial.FaceA);
            AppendResetTeshisiComparisonRow(sb, "Yüz A Body-renk sonrası", afterBody.FaceA);
            AppendResetTeshisiComparisonRow(sb, "Yüz A Reset sonrası", afterReset.FaceA);
            AppendResetTeshisiComparisonRow(sb, "Yüz B başlangıç", initial.FaceB);
            AppendResetTeshisiComparisonRow(sb, "Yüz B Body-renk sonrası", afterBody.FaceB);
            AppendResetTeshisiComparisonRow(sb, "Yüz B turkuaz sonrası", afterFace.FaceB);
            AppendResetTeshisiComparisonRow(sb, "Yüz B Reset sonrası", afterReset.FaceB);
            sb.AppendLine("Test Body RGB=" + RenkMetni(bodyColor) + " | Turkuaz RGB=" + RenkMetni(turquoise));
            sb.Append("A/B kararı verilmedi; yukarıdaki gerçek CATIA değerleri değerlendirilmelidir.");
            LogInfo(sb.ToString());

            LogPostResetComparison("Yüz A başlangıç ↔ Reset", initial.FaceA, afterReset.FaceA);
            LogPostResetComparison("Yüz B turkuaz ↔ Reset", afterFace.FaceB, afterReset.FaceB);
        }

        private static void AppendResetTeshisiComparisonRow(
            StringBuilder sb,
            string label,
            ResetTeshisiColorState state)
        {
            sb.Append(label).Append(": ");
            if (!state.Success)
            {
                sb.AppendLine("OKUMA BAŞARISIZ");
                return;
            }

            sb.Append("Real=").Append(FormatResetTeshisiRgb(state.RealColor, state.RealColorError))
              .Append(" / RealInh=").Append(FormatResetTeshisiInheritance(state.RealInheritance, state.RealInheritanceError))
              .Append(" / Visible=").Append(FormatResetTeshisiRgb(state.VisibleColor, state.VisibleColorError))
              .Append(" / VisibleInh=").AppendLine(FormatResetTeshisiInheritance(state.VisibleInheritance, state.VisibleInheritanceError));
        }

        private void LogPostResetComparison(
            string label,
            ResetTeshisiColorState expected,
            ResetTeshisiColorState actual)
        {
            if (!TryCompareDefinedRgb(expected, actual, out bool equal, out string detail))
            {
                LogError("ResetProperty teşhisi — " + label + " REAL RGB karşılaştırılamadı | " + detail);
                return;
            }

            if (equal)
                LogSuccess("ResetProperty teşhisi — " + label + " REAL RGB korundu | " + detail);
            else
                LogError("ResetProperty teşhisi — " + label + " REAL RGB korunmadı | " + detail);
        }

        private static bool TryCompareDefinedRgb(
            ResetTeshisiColorState before,
            ResetTeshisiColorState after,
            out bool equal,
            out string detail)
        {
            equal = false;
            if (!before.Success || !after.Success)
            {
                detail = "okumalardan en az biri başarısız";
                return false;
            }

            if (before.RealColor.Status != 0 || after.RealColor.Status != 0)
            {
                detail = "önce=" + ColorPropertyStatusText(before.RealColor.Status) +
                         " | sonra=" + ColorPropertyStatusText(after.RealColor.Status);
                return false;
            }

            equal = before.RealColor.Red == after.RealColor.Red &&
                    before.RealColor.Green == after.RealColor.Green &&
                    before.RealColor.Blue == after.RealColor.Blue;
            detail = "önce=" + RgbText(before.RealColor) + " | sonra=" + RgbText(after.RealColor);
            return true;
        }

        private static bool RgbEquals(CatiaColorRgb actual, AkilliRenk expected) =>
            actual.Red == expected.Kirmizi &&
            actual.Green == expected.Yesil &&
            actual.Blue == expected.Mavi;

        private static string RenkMetni(AkilliRenk color) =>
            color.Kirmizi + ", " + color.Yesil + ", " + color.Mavi;

        private static string EvetHayir(bool value) => value ? "EVET" : "HAYIR";

        private static AkilliRenk SelectResetTeshisiBodyColor(ResetTeshisiStage initial)
        {
            AkilliRenk[] candidates =
            {
                new(255, 35, 170),
                new(255, 120, 20),
                new(80, 40, 230)
            };

            foreach (AkilliRenk candidate in candidates)
            {
                if (!IsDefinedRgb(initial.Body, candidate) &&
                    !IsDefinedRgb(initial.FaceA, candidate) &&
                    !IsDefinedRgb(initial.FaceB, candidate))
                    return candidate;
            }

            return candidates[0];
        }

        private static AkilliRenk SelectResetTeshisiTurquoise(ResetTeshisiColorState faceB)
        {
            var primary = new AkilliRenk(0, 210, 200);
            return IsDefinedRgb(faceB, primary)
                ? new AkilliRenk(0, 155, 210)
                : primary;
        }

        private static bool IsDefinedRgb(ResetTeshisiColorState state, AkilliRenk color) =>
            state.Success && state.RealColor.Status == 0 && RgbEquals(state.RealColor, color);

        private static void RestoreResetTeshisiSelection(
            dynamic selection,
            IReadOnlyList<ResetTeshisiTarget> targets)
        {
            selection.Clear();
            foreach (ResetTeshisiTarget target in targets)
                selection.Add(target.SelectedElement);
        }

        private void TryRestoreResetTeshisiSelection(ResetTeshisiContext context)
        {
            try
            {
                object currentEditor = ((dynamic)context.CatiaObject).ActiveEditor;
                string editorKey = CatiaColorTargetService.GetSessionObjectKey(currentEditor);
                if (!string.Equals(editorKey, context.EditorSessionKey, StringComparison.Ordinal))
                {
                    LogError("ResetProperty teşhisi — aktif editör değiştiği için başlangıç seçimi geri yüklenmedi.");
                    return;
                }

                RestoreResetTeshisiSelection(context.Selection, context.OriginalSelection);
            }
            catch (Exception ex)
            {
                LogError("ResetProperty teşhisi — başlangıç seçimi geri yüklenemedi: " + Kisa(ex.Message));
            }
        }

        private sealed class ResetTeshisiContext
        {
            public ResetTeshisiContext(
                object catiaObject,
                object editor,
                object selection,
                string catiaSessionKey,
                string editorSessionKey,
                ResetTeshisiTarget body,
                ResetTeshisiTarget faceA,
                ResetTeshisiTarget faceB,
                IReadOnlyList<ResetTeshisiTarget> originalSelection)
            {
                CatiaObject = catiaObject;
                Editor = editor;
                Selection = selection;
                CatiaSessionKey = catiaSessionKey;
                EditorSessionKey = editorSessionKey;
                Body = body;
                FaceA = faceA;
                FaceB = faceB;
                OriginalSelection = originalSelection;
            }

            public object CatiaObject { get; }
            public object Editor { get; }
            public object Selection { get; }
            public string CatiaSessionKey { get; }
            public string EditorSessionKey { get; }
            public ResetTeshisiTarget Body { get; }
            public ResetTeshisiTarget FaceA { get; }
            public ResetTeshisiTarget FaceB { get; }
            public IReadOnlyList<ResetTeshisiTarget> OriginalSelection { get; }
        }

        private sealed class ResetTeshisiTarget
        {
            public ResetTeshisiTarget(
                string label,
                object value,
                object selectedElement,
                string comType,
                string sessionObjectKey,
                string selectionType,
                string referenceDisplayName,
                string leafProductKey)
            {
                Label = label;
                Value = value;
                SelectedElement = selectedElement;
                ComType = comType;
                SessionObjectKey = sessionObjectKey;
                SelectionType = selectionType;
                ReferenceDisplayName = referenceDisplayName;
                LeafProductKey = leafProductKey;
            }

            public string Label { get; }
            public object Value { get; }
            public object SelectedElement { get; }
            public string ComType { get; }
            public string SessionObjectKey { get; }
            public string SelectionType { get; }
            public string ReferenceDisplayName { get; }
            public string LeafProductKey { get; }

            public ResetTeshisiTarget WithLabel(string label) =>
                new(
                    label,
                    Value,
                    SelectedElement,
                    ComType,
                    SessionObjectKey,
                    SelectionType,
                    ReferenceDisplayName,
                    LeafProductKey);
        }

        private sealed class ResetTeshisiColorState
        {
            private ResetTeshisiColorState(
                ResetTeshisiTarget target,
                bool success,
                CatiaColorRgb realColor,
                CatiaColorInheritance realInheritance,
                CatiaColorRgb visibleColor,
                CatiaColorInheritance visibleInheritance,
                string realColorError,
                string realInheritanceError,
                string visibleColorError,
                string visibleInheritanceError,
                string error)
            {
                Target = target;
                Success = success;
                RealColor = realColor;
                RealInheritance = realInheritance;
                VisibleColor = visibleColor;
                VisibleInheritance = visibleInheritance;
                RealColorError = realColorError;
                RealInheritanceError = realInheritanceError;
                VisibleColorError = visibleColorError;
                VisibleInheritanceError = visibleInheritanceError;
                Error = error;
            }

            public ResetTeshisiTarget Target { get; }
            public bool Success { get; }
            public CatiaColorRgb RealColor { get; }
            public CatiaColorInheritance RealInheritance { get; }
            public CatiaColorRgb VisibleColor { get; }
            public CatiaColorInheritance VisibleInheritance { get; }
            public string RealColorError { get; }
            public string RealInheritanceError { get; }
            public string VisibleColorError { get; }
            public string VisibleInheritanceError { get; }
            public string Error { get; }

            public static ResetTeshisiColorState Ok(
                ResetTeshisiTarget target,
                CatiaColorRgb realColor,
                CatiaColorInheritance realInheritance,
                CatiaColorRgb visibleColor,
                CatiaColorInheritance visibleInheritance,
                string realColorError,
                string realInheritanceError,
                string visibleColorError,
                string visibleInheritanceError) =>
                new(
                    target,
                    true,
                    realColor,
                    realInheritance,
                    visibleColor,
                    visibleInheritance,
                    realColorError,
                    realInheritanceError,
                    visibleColorError,
                    visibleInheritanceError,
                    "");

            public static ResetTeshisiColorState Fail(ResetTeshisiTarget target, string error) =>
                new(
                    target,
                    false,
                    new CatiaColorRgb(0, 0, 0, -1),
                    new CatiaColorInheritance(0, -1),
                    new CatiaColorRgb(0, 0, 0, -1),
                    new CatiaColorInheritance(0, -1),
                    error,
                    error,
                    error,
                    error,
                    error);
        }

        private sealed class ResetTeshisiStage
        {
            public ResetTeshisiStage(
                string name,
                ResetTeshisiColorState body,
                ResetTeshisiColorState faceA,
                ResetTeshisiColorState faceB)
            {
                Name = name;
                Body = body;
                FaceA = faceA;
                FaceB = faceB;
            }

            public string Name { get; }
            public ResetTeshisiColorState Body { get; }
            public ResetTeshisiColorState FaceA { get; }
            public ResetTeshisiColorState FaceB { get; }
        }
    }
}
