using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        private void FaceSelectionContextRoundTripTeshisi_Click(object sender, RoutedEventArgs e)
        {
            FaceRoundTripContext? context = null;
            try
            {
                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                object editorObject = ((dynamic)catiaObject).ActiveEditor;
                dynamic selection = ((dynamic)editorObject).Selection;
                context = CaptureFaceRoundTripContext(catiaObject, editorObject, selection);

                LogInfo(
                    "Face Selection context round-trip teşhisi — SALT OKUNUR" +
                    " | CATIA=" + context.CatiaSessionKey +
                    " | Editör=" + context.EditorSessionKey +
                    "\nYalnız Yüz A ve Yüz B yeniden Selection'a eklenecek; PartBody.Reference çağrılmayacak.");

                foreach (FaceRoundTripTarget face in context.Faces)
                    RunFaceRoundTrip(selection, face);

                LogSuccess(
                    "Face Selection context round-trip teşhisi tamamlandı. COM identity farkları yalnız loglandı; " +
                    "Face identity/production güvenlik stratejisi seçilmedi. Renk, ResetProperty, Undo veya Save çağrısı yapılmadı.");
                MessageBox.Show(
                    this,
                    "Salt-okunur Face Selection round-trip ölçümü tamamlandı. Karşılaştırmalı sonucu Macria logundan paylaşın.",
                    "Face Selection Context Round-Trip",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogError(
                    "Face Selection context round-trip teşhisi tamamlanamadı | " +
                    SelectedElementProbeFailure(ex));
            }
            finally
            {
                if (context != null)
                    TryRestoreFaceRoundTripSelection(context);
            }
        }

        private FaceRoundTripContext CaptureFaceRoundTripContext(
            object catiaObject,
            object editorObject,
            dynamic selection)
        {
            int? count = null;
            int? count2 = null;
            string countError = "";
            string count2Error = "";
            try { count = Convert.ToInt32(selection.Count); }
            catch (Exception ex) { countError = SelectedElementProbeFailure(ex); }
            try { count2 = Convert.ToInt32(selection.Count2); }
            catch (Exception ex) { count2Error = SelectedElementProbeFailure(ex); }

            int scanCount = Math.Max(count ?? -1, count2 ?? -1);
            var diagnostics = new StringBuilder();
            diagnostics.AppendLine("Face round-trip başlangıç Selection teşhisi");
            diagnostics.AppendLine(
                "Selection.Count = " +
                (count.HasValue ? count.Value.ToString(CultureInfo.InvariantCulture) : "FAILED | " + countError));
            diagnostics.AppendLine(
                "Selection.Count2 = " +
                (count2.HasValue ? count2.Value.ToString(CultureInfo.InvariantCulture) : "FAILED | " + count2Error));

            var originalSelection = new List<object>();
            var faceTokens = new List<object>();
            int bodyCount = 0;
            var compactTypes = new List<string>();
            for (int index = 1; index <= Math.Max(scanCount, 0); index++)
            {
                object? item = null;
                object? item2 = null;
                FaceRoundTripField itemAccess = ReadFaceRoundTripField(() =>
                {
                    item = selection.Item(index);
                    return ComProbe.TipAdi(item);
                });
                FaceRoundTripField item2Access = ReadFaceRoundTripField(() =>
                {
                    item2 = selection.Item2(index);
                    return ComProbe.TipAdi(item2);
                });

                FaceRoundTripField itemType = item == null
                    ? FaceRoundTripField.Fail("Selection.Item başarısız")
                    : ReadFaceRoundTripField(() =>
                        Convert.ToString(((dynamic)item).Type)?.Trim() ?? "(boş)");
                FaceRoundTripField itemValueType = item == null
                    ? FaceRoundTripField.Fail("Selection.Item başarısız")
                    : ReadFaceRoundTripField(() =>
                    {
                        object value = ((dynamic)item).Value;
                        return ComProbe.TipAdi(value);
                    });
                FaceRoundTripField item2Type = item2 == null
                    ? FaceRoundTripField.Fail("Selection.Item2 başarısız")
                    : ReadFaceRoundTripField(() =>
                        Convert.ToString(((dynamic)item2).Type)?.Trim() ?? "(boş)");
                FaceRoundTripField item2ValueType = item2 == null
                    ? FaceRoundTripField.Fail("Selection.Item2 başarısız")
                    : ReadFaceRoundTripField(() =>
                    {
                        object value = ((dynamic)item2).Value;
                        return ComProbe.TipAdi(value);
                    });

                diagnostics.AppendLine("İndeks " + index.ToString(CultureInfo.InvariantCulture) + ":");
                AppendFaceRoundTripField(diagnostics, "  Selection.Item erişimi", itemAccess);
                AppendFaceRoundTripField(diagnostics, "  Selection.Item(i).Type", itemType);
                AppendFaceRoundTripField(diagnostics, "  Selection.Item(i).Value COM type", itemValueType);
                AppendFaceRoundTripField(diagnostics, "  Selection.Item2 erişimi", item2Access);
                AppendFaceRoundTripField(diagnostics, "  Selection.Item2(i).Type", item2Type);
                AppendFaceRoundTripField(diagnostics, "  Selection.Item2(i).Value COM type", item2ValueType);

                object? selectedElement = item ?? item2;
                FaceRoundTripField selectedType = item != null ? itemType : item2Type;
                bool isBody = selectedType.Success && IsFaceRoundTripBodyType(selectedType.Value);
                bool isFace = selectedType.Success && IsFaceRoundTripFaceType(selectedType.Value);
                diagnostics.AppendLine(
                    "  Seçilen SelectedElement Type = " +
                    (selectedType.Success ? selectedType.Value : "FAILED | " + selectedType.Error));
                diagnostics.AppendLine(
                    "  Sınıflandırma = Body:" + EvetHayir(isBody) + " | Face:" + EvetHayir(isFace));
                compactTypes.Add(
                    "#" + index.ToString(CultureInfo.InvariantCulture) + "=" +
                    (selectedType.Success ? selectedType.Value : "FAILED") +
                    "(Body:" + EvetHayir(isBody) + ",Face:" + EvetHayir(isFace) + ")");

                if (selectedElement == null)
                    continue;
                originalSelection.Add(selectedElement);
                if (isFace)
                    faceTokens.Add(selectedElement);
                else if (isBody)
                    bodyCount++;
            }

            diagnostics.AppendLine(
                "Özet = scanCount:" + scanCount.ToString(CultureInfo.InvariantCulture) +
                " | Body:" + bodyCount.ToString(CultureInfo.InvariantCulture) +
                " | Face:" + faceTokens.Count.ToString(CultureInfo.InvariantCulture));
            LogInfo(diagnostics.ToString().TrimEnd());

            if (scanCount != 3 || originalSelection.Count != 3 ||
                faceTokens.Count != 2 || bodyCount != 1)
            {
                throw new InvalidOperationException(
                    "İlk Selection 1 Body + 2 Face olarak doğrulanamadı; seçim değiştirilmedi. " +
                    "Count=" + (count.HasValue ? count.Value.ToString(CultureInfo.InvariantCulture) : "FAILED") +
                    " | Count2=" + (count2.HasValue ? count2.Value.ToString(CultureInfo.InvariantCulture) : "FAILED") +
                    " | scanCount=" + scanCount.ToString(CultureInfo.InvariantCulture) +
                    " | Body=" + bodyCount.ToString(CultureInfo.InvariantCulture) +
                    " | Face=" + faceTokens.Count.ToString(CultureInfo.InvariantCulture) +
                    " | Types=[" + string.Join(", ", compactTypes) + "].");
            }

            var faces = new List<FaceRoundTripTarget>
            {
                new("Yüz A", faceTokens[0], ReadFaceRoundTripSnapshot(faceTokens[0])),
                new("Yüz B", faceTokens[1], ReadFaceRoundTripSnapshot(faceTokens[1]))
            };

            return new FaceRoundTripContext(
                catiaObject,
                editorObject,
                selection,
                CatiaColorTargetService.GetSessionObjectKey(catiaObject),
                CatiaColorTargetService.GetSessionObjectKey(editorObject),
                originalSelection,
                faces);
        }

        private static bool IsFaceRoundTripBodyType(string type) =>
            string.Equals(type, "Body", StringComparison.OrdinalIgnoreCase);

        private static bool IsFaceRoundTripFaceType(string type) =>
            string.Equals(type, "Face", StringComparison.OrdinalIgnoreCase) ||
            type.EndsWith("Face", StringComparison.OrdinalIgnoreCase);

        private void RunFaceRoundTrip(dynamic selection, FaceRoundTripTarget face)
        {
            var log = new StringBuilder();
            log.AppendLine("Face Selection round-trip — " + face.Label);
            AppendFaceRoundTripSnapshot(log, "İLK SEÇİM", face.Initial);

            FaceRoundTripSnapshot? roundTrip = null;
            try
            {
                selection.Clear();
                // DSYAutomation Selection.Add belgesindeki multi-instance yöntemi:
                // çıplak Value yerine ilk Selection'dan saklanan SelectedElement yeniden eklenir.
                selection.Add(face.SelectedElement);

                int count;
                try { count = Convert.ToInt32(selection.Count); }
                catch { count = Convert.ToInt32(selection.Count2); }
                if (count != 1)
                    throw new InvalidOperationException("Round-trip Selection.Count=" + count + "; beklenen 1.");

                object returnedSelectedElement;
                try { returnedSelectedElement = selection.Item(1); }
                catch { returnedSelectedElement = selection.Item2(1); }
                roundTrip = ReadFaceRoundTripSnapshot(returnedSelectedElement);
                AppendFaceRoundTripSnapshot(log, "ROUND-TRIP", roundTrip);
                AppendFaceRoundTripComparisons(log, face.Initial, roundTrip);
            }
            catch (Exception ex)
            {
                log.AppendLine("ROUND-TRIP = FAILED | " + SelectedElementProbeFailure(ex));
            }

            LogInfo(log.ToString().TrimEnd());
        }

        private static FaceRoundTripSnapshot ReadFaceRoundTripSnapshot(object selectedElement) =>
            new(
                ReadFaceRoundTripField(() =>
                    Convert.ToString(((dynamic)selectedElement).Type)?.Trim() ?? "(boş)"),
                ReadFaceRoundTripField(() =>
                {
                    object reference = ((dynamic)selectedElement).Reference;
                    return Convert.ToString(((dynamic)reference).DisplayName)?.Trim() ?? "(boş)";
                }),
                ReadFaceRoundTripField(() =>
                {
                    object leafProduct = ((dynamic)selectedElement).LeafProduct;
                    return Convert.ToString(((dynamic)leafProduct).Name)?.Trim() ?? "(boş)";
                }),
                ReadFaceRoundTripField(() =>
                {
                    object value = ((dynamic)selectedElement).Value;
                    return ComProbe.TipAdi(value);
                }),
                ReadFaceRoundTripField(() =>
                {
                    object value = ((dynamic)selectedElement).Value;
                    return CatiaColorTargetService.GetSessionObjectKey(value);
                }),
                ReadFaceRoundTripField(() =>
                {
                    object leafProduct = ((dynamic)selectedElement).LeafProduct;
                    return CatiaColorTargetService.GetSessionObjectKey(leafProduct);
                }));

        private static FaceRoundTripField ReadFaceRoundTripField(Func<string> read)
        {
            try
            {
                return FaceRoundTripField.Ok(read());
            }
            catch (Exception ex)
            {
                return FaceRoundTripField.Fail(SelectedElementProbeFailure(ex));
            }
        }

        private static void AppendFaceRoundTripSnapshot(
            StringBuilder log,
            string stage,
            FaceRoundTripSnapshot snapshot)
        {
            log.AppendLine(stage + ":");
            AppendFaceRoundTripField(log, "  Type", snapshot.Type);
            AppendFaceRoundTripField(log, "  Reference.DisplayName", snapshot.ReferenceDisplayName);
            AppendFaceRoundTripField(log, "  LeafProduct.Name", snapshot.LeafProductName);
            AppendFaceRoundTripField(log, "  Value COM type", snapshot.ValueComType);
            AppendFaceRoundTripField(log, "  Value COM identity (yalnız teşhis)", snapshot.ValueComIdentity);
            AppendFaceRoundTripField(log, "  LeafProduct COM identity (yalnız teşhis)", snapshot.LeafProductComIdentity);
        }

        private static void AppendFaceRoundTripField(
            StringBuilder log,
            string label,
            FaceRoundTripField field)
        {
            log.AppendLine(
                label + " = " +
                (field.Success ? "OK | " + field.Value : "FAILED | " + field.Error));
        }

        private static void AppendFaceRoundTripComparisons(
            StringBuilder log,
            FaceRoundTripSnapshot initial,
            FaceRoundTripSnapshot roundTrip)
        {
            log.AppendLine("KARŞILAŞTIRMA:");
            AppendFaceRoundTripExactComparison(log, "Type aynı mı?", initial.Type, roundTrip.Type);
            AppendFaceRoundTripExactComparison(
                log,
                "Reference.DisplayName tam string aynı mı?",
                initial.ReferenceDisplayName,
                roundTrip.ReferenceDisplayName);
            AppendFaceRoundTripExactComparison(
                log,
                "LeafProduct.Name aynı mı?",
                initial.LeafProductName,
                roundTrip.LeafProductName);
            AppendFaceRoundTripExactComparison(
                log,
                "Value COM type aynı mı?",
                initial.ValueComType,
                roundTrip.ValueComType);
            AppendFaceRoundTripIdentityComparison(
                log,
                "Value COM identity",
                initial.ValueComIdentity,
                roundTrip.ValueComIdentity);
            AppendFaceRoundTripIdentityComparison(
                log,
                "LeafProduct COM identity",
                initial.LeafProductComIdentity,
                roundTrip.LeafProductComIdentity);
            log.AppendLine("Not: COM identity değişimi tek başına hata veya kimlik kaybı sayılmadı.");
        }

        private static void AppendFaceRoundTripExactComparison(
            StringBuilder log,
            string label,
            FaceRoundTripField initial,
            FaceRoundTripField roundTrip)
        {
            if (!initial.Success || !roundTrip.Success)
            {
                log.AppendLine(label + " KARŞILAŞTIRILAMADI | " +
                               FaceRoundTripComparisonFailure(initial, roundTrip));
                return;
            }

            bool same = string.Equals(initial.Value, roundTrip.Value, StringComparison.Ordinal);
            log.AppendLine(label + " " + (same ? "EVET" : "HAYIR"));
        }

        private static void AppendFaceRoundTripIdentityComparison(
            StringBuilder log,
            string label,
            FaceRoundTripField initial,
            FaceRoundTripField roundTrip)
        {
            if (!initial.Success || !roundTrip.Success)
            {
                log.AppendLine(label + " değişti mi? KARŞILAŞTIRILAMADI | " +
                               FaceRoundTripComparisonFailure(initial, roundTrip));
                return;
            }

            bool changed = !string.Equals(initial.Value, roundTrip.Value, StringComparison.Ordinal);
            log.AppendLine(label + " değişti mi? " + (changed ? "EVET" : "HAYIR") +
                           " | yalnız teşhis bilgisi");
        }

        private static string FaceRoundTripComparisonFailure(
            FaceRoundTripField initial,
            FaceRoundTripField roundTrip) =>
            "ilk=" + (initial.Success ? "OK" : "FAILED: " + initial.Error) +
            " | round-trip=" + (roundTrip.Success ? "OK" : "FAILED: " + roundTrip.Error);

        private void TryRestoreFaceRoundTripSelection(FaceRoundTripContext context)
        {
            try
            {
                object currentEditor = ((dynamic)context.CatiaObject).ActiveEditor;
                string editorKey = CatiaColorTargetService.GetSessionObjectKey(currentEditor);
                if (!string.Equals(editorKey, context.EditorSessionKey, StringComparison.Ordinal))
                {
                    LogError(
                        "Face Selection round-trip — aktif editör değişti; ilk Selection güvenlik nedeniyle geri yüklenmedi.");
                    return;
                }

                dynamic selection = context.Selection;
                selection.Clear();
                foreach (object selectedElement in context.OriginalSelection)
                {
                    try
                    {
                        selection.Add(selectedElement);
                    }
                    catch (Exception ex)
                    {
                        LogError(
                            "Face Selection round-trip — ilk Selection öğelerinden biri geri yüklenemedi | " +
                            SelectedElementProbeFailure(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(
                    "Face Selection round-trip — ilk Selection geri yüklenemedi | " +
                    SelectedElementProbeFailure(ex));
            }
        }

        private sealed class FaceRoundTripContext
        {
            public FaceRoundTripContext(
                object catiaObject,
                object editorObject,
                object selection,
                string catiaSessionKey,
                string editorSessionKey,
                IReadOnlyList<object> originalSelection,
                IReadOnlyList<FaceRoundTripTarget> faces)
            {
                CatiaObject = catiaObject;
                EditorObject = editorObject;
                Selection = selection;
                CatiaSessionKey = catiaSessionKey;
                EditorSessionKey = editorSessionKey;
                OriginalSelection = originalSelection;
                Faces = faces;
            }

            public object CatiaObject { get; }
            public object EditorObject { get; }
            public object Selection { get; }
            public string CatiaSessionKey { get; }
            public string EditorSessionKey { get; }
            public IReadOnlyList<object> OriginalSelection { get; }
            public IReadOnlyList<FaceRoundTripTarget> Faces { get; }
        }

        private sealed class FaceRoundTripTarget
        {
            public FaceRoundTripTarget(
                string label,
                object selectedElement,
                FaceRoundTripSnapshot initial)
            {
                Label = label;
                SelectedElement = selectedElement;
                Initial = initial;
            }

            public string Label { get; }
            public object SelectedElement { get; }
            public FaceRoundTripSnapshot Initial { get; }
        }

        private sealed class FaceRoundTripSnapshot
        {
            public FaceRoundTripSnapshot(
                FaceRoundTripField type,
                FaceRoundTripField referenceDisplayName,
                FaceRoundTripField leafProductName,
                FaceRoundTripField valueComType,
                FaceRoundTripField valueComIdentity,
                FaceRoundTripField leafProductComIdentity)
            {
                Type = type;
                ReferenceDisplayName = referenceDisplayName;
                LeafProductName = leafProductName;
                ValueComType = valueComType;
                ValueComIdentity = valueComIdentity;
                LeafProductComIdentity = leafProductComIdentity;
            }

            public FaceRoundTripField Type { get; }
            public FaceRoundTripField ReferenceDisplayName { get; }
            public FaceRoundTripField LeafProductName { get; }
            public FaceRoundTripField ValueComType { get; }
            public FaceRoundTripField ValueComIdentity { get; }
            public FaceRoundTripField LeafProductComIdentity { get; }
        }

        private sealed class FaceRoundTripField
        {
            private FaceRoundTripField(bool success, string value, string error)
            {
                Success = success;
                Value = value;
                Error = error;
            }

            public bool Success { get; }
            public string Value { get; }
            public string Error { get; }

            public static FaceRoundTripField Ok(string value) => new(true, value, "");
            public static FaceRoundTripField Fail(string error) => new(false, "", error);
        }
    }
}
