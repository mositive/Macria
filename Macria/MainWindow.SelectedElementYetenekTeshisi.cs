using System;
using System.Globalization;
using System.Text;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        private void SelectedElementYetenekTeshisi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                object editorObject = ((dynamic)catiaObject).ActiveEditor;
                dynamic selection = ((dynamic)editorObject).Selection;

                var header = new StringBuilder();
                header.AppendLine("SelectedElement gerçek CATIA yetenek teşhisi — SALT OKUNUR");
                header.AppendLine("CATIA oturumu=" + CatiaColorTargetService.GetSessionObjectKey(catiaObject));
                header.AppendLine("Aktif editör=" + CatiaColorTargetService.GetSessionObjectKey(editorObject));

                int? count = null;
                ProbeSelectedElementMember(
                    header,
                    "Selection.Count",
                    () =>
                    {
                        int value = Convert.ToInt32(selection.Count);
                        count = value;
                        return value.ToString(CultureInfo.InvariantCulture);
                    });
                ProbeSelectedElementMember(
                    header,
                    "Selection.Count2 (deprecated)",
                    () =>
                    {
                        int value = Convert.ToInt32(selection.Count2);
                        count ??= value;
                        return value.ToString(CultureInfo.InvariantCulture);
                    });

                LogInfo(header.ToString().TrimEnd());
                if (count != 3)
                {
                    LogError(
                        "SelectedElement yetenek teşhisi durduruldu: CATIA'da tam olarak üç öğe seçin " +
                        "(PartBody, Yüz A, Yüz B). Selection.Count=" +
                        (count.HasValue ? count.Value.ToString(CultureInfo.InvariantCulture) : "FAILED") + ".");
                    return;
                }

                for (int index = 1; index <= count.Value; index++)
                    ProbeSelectedElement(selection, index);

                LogSuccess(
                    "SelectedElement yetenek teşhisi tamamlandı. Selection temizlenmedi/yeniden kurulmadı; " +
                    "renk, ResetProperty, Undo veya kayıt çağrısı yapılmadı. Face identity stratejisi seçilmedi.");
                MessageBox.Show(
                    this,
                    "Salt-okunur SelectedElement ölçümü tamamlandı. Sonuçları Macria logundan paylaşın.",
                    "SelectedElement Yetenek Teşhisi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogError(
                    "SelectedElement yetenek teşhisi başlatılamadı | " +
                    SelectedElementProbeFailure(ex));
            }
        }

        private void ProbeSelectedElement(dynamic selection, int index)
        {
            var log = new StringBuilder();
            log.AppendLine("SelectedElement teşhisi — seçim " + index.ToString(CultureInfo.InvariantCulture));

            object? selectedElement = null;
            ProbeSelectedElementMember(
                log,
                "Selection.Item(" + index.ToString(CultureInfo.InvariantCulture) + ")",
                () =>
                {
                    object value = selection.Item(index);
                    selectedElement = value;
                    return ComProbe.TipAdi(value) + " | identity=" +
                           CatiaColorTargetService.GetSessionObjectKey(value);
                });

            ProbeSelectedElementMember(
                log,
                "Selection.Item2(" + index.ToString(CultureInfo.InvariantCulture) + ") (deprecated)",
                () =>
                {
                    object value = selection.Item2(index);
                    selectedElement ??= value;
                    return ComProbe.TipAdi(value) + " | identity=" +
                           CatiaColorTargetService.GetSessionObjectKey(value);
                });

            if (selectedElement == null)
            {
                log.AppendLine("SelectedElement üyeleri = SKIPPED | Item ve Item2 başarısız");
                LogInfo(log.ToString().TrimEnd());
                return;
            }

            object element = selectedElement;
            ProbeSelectedElementMember(
                log,
                "Type",
                () => Convert.ToString(((dynamic)element).Type)?.Trim() ?? "(boş)");
            ProbeSelectedElementMember(
                log,
                "Value COM type",
                () =>
                {
                    object value = ((dynamic)element).Value;
                    return ComProbe.TipAdi(value);
                });
            ProbeSelectedElementMember(
                log,
                "Value COM identity (yalnız teşhis)",
                () =>
                {
                    object value = ((dynamic)element).Value;
                    return CatiaColorTargetService.GetSessionObjectKey(value);
                });
            ProbeSelectedElementMember(
                log,
                "Reference",
                () =>
                {
                    object reference = ((dynamic)element).Reference;
                    return ComProbe.TipAdi(reference) + " | identity=" +
                           CatiaColorTargetService.GetSessionObjectKey(reference);
                });
            ProbeSelectedElementMember(
                log,
                "Reference.DisplayName",
                () =>
                {
                    object reference = ((dynamic)element).Reference;
                    return Convert.ToString(((dynamic)reference).DisplayName)?.Trim() ?? "(boş)";
                });
            ProbeSelectedElementMember(
                log,
                "LeafProduct",
                () =>
                {
                    object leafProduct = ((dynamic)element).LeafProduct;
                    return ComProbe.TipAdi(leafProduct);
                });
            ProbeSelectedElementMember(
                log,
                "LeafProduct.Name",
                () =>
                {
                    object leafProduct = ((dynamic)element).LeafProduct;
                    return Convert.ToString(((dynamic)leafProduct).Name)?.Trim() ?? "(boş)";
                });
            ProbeSelectedElementMember(
                log,
                "LeafProduct COM type",
                () =>
                {
                    object leafProduct = ((dynamic)element).LeafProduct;
                    return ComProbe.TipAdi(leafProduct);
                });
            ProbeSelectedElementMember(
                log,
                "LeafProduct COM identity",
                () =>
                {
                    object leafProduct = ((dynamic)element).LeafProduct;
                    return CatiaColorTargetService.GetSessionObjectKey(leafProduct);
                });
            ProbeSelectedElementMember(
                log,
                "GetCoordinates",
                () =>
                {
                    object[] point = { 0.0, 0.0, 0.0 };
                    ((dynamic)element).GetCoordinates(point);
                    return string.Join(", ", Array.ConvertAll(
                        point,
                        value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(null)"));
                });

            LogInfo(log.ToString().TrimEnd());
        }

        private static void ProbeSelectedElementMember(
            StringBuilder log,
            string member,
            Func<string> read)
        {
            try
            {
                log.AppendLine(member + " = OK | " + read());
            }
            catch (Exception ex)
            {
                log.AppendLine(member + " = FAILED | " + SelectedElementProbeFailure(ex));
            }
        }

        private static string SelectedElementProbeFailure(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null)
                current = current.InnerException;

            return current.GetType().Name +
                   " | HRESULT=0x" + unchecked((uint)current.HResult).ToString("X8") +
                   " | " + Kisa(current.Message);
        }
    }
}
