using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        // Salt-okunur teşhis: yalnız seçili yaprak occurrence'ın PartBody renk durumunu okur.
        private async void RenkDurumuTeshisi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic editor = ((dynamic)catiaObject).ActiveEditor;
                object? root = editor.ActiveObject;
                if (root is not object activeRoot)
                    throw new InvalidOperationException("Aktif CATIA montaj kökü alınamadı.");
                if (!HasOccurrences((dynamic)activeRoot))
                {
                    throw new InvalidOperationException(
                        "Aktif CATIA nesnesi occurrence içeren bir Physical Product montajı değil.");
                }

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

                    string? referenceKey = AkilliRenklendirmeReferansAnahtari(selectedOccurrence);
                    if (string.IsNullOrWhiteSpace(referenceKey))
                        throw new InvalidOperationException("Seçilen occurrence için PLM referans anahtarı çözümlenemedi.");

                    List<object> leaves = await AkilliRenklendirmeYapraklariniTopla((dynamic)activeRoot);
                    int matchingOccurrenceCount = leaves.Count(leaf =>
                    {
                        try
                        {
                            return string.Equals(
                                referenceKey,
                                AkilliRenklendirmeReferansAnahtari(leaf),
                                StringComparison.Ordinal);
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    if (matchingOccurrenceCount <= 0)
                    {
                        throw new InvalidOperationException(
                            "Seçilen referans aktif montajın yaprak occurrence listesinde bulunamadı.");
                    }

                    var service = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
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

                    LogInfo(
                        "Renk durumu teşhisi — PartBody hedefi doğrulandı" +
                        " | CATIA oturumu=" + CatiaColorTargetService.GetSessionObjectKey(catiaObject) +
                        " | aktif editör=" + CatiaColorTargetService.GetSessionObjectKey((object)editor) +
                        " | Reference Title=" + ReadReferenceTitle(selectedOccurrence) +
                        " | ReferenceKey=" + referenceKey +
                        " | PartBody COM türü=" + target.Identity.ComType +
                        " | PartBody hedefi=" + target.Identity.SessionObjectKey +
                        " | occurrence=" + matchingOccurrenceCount +
                        " | zincir=" + discovery.AccessChain);

                    LogSelectionContext("PartBody", selection, target);
                    LogTargetColorState("PartBody", service, selection, target);

                    CatiaColorTargetResult occurrenceResult = service.ResolveOccurrence(selectedOccurrence);
                    if (!occurrenceResult.Success || occurrenceResult.Target == null)
                    {
                        LogError(
                            "Renk durumu teşhisi — Occurrence karşılaştırma hedefi çözümlenemedi | " +
                            occurrenceResult.Error);
                    }
                    else
                    {
                        CatiaColorTarget occurrenceTarget = occurrenceResult.Target;
                        LogSelectionContext("Occurrence", selection, occurrenceTarget);
                        LogTargetColorState("Occurrence", service, selection, occurrenceTarget);
                    }

                    LogInfo(
                        "Renk durumu teşhisi tamamlandı. Yalnız okuma yapıldı; " +
                        "SetRealColor ve ResetProperty çağrılmadı.");
                }
                finally
                {
                    SecimiGeriYukle(selection, previousSelection);
                }
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("Renk durumu teşhisi", ex);
                LogError("Renk durumu teşhisi uygulanamadı: " + Kisa(ex.Message));
            }
        }

        private void LogTargetColorState(
            string targetLabel,
            CatiaColorTargetService service,
            dynamic selection,
            CatiaColorTarget target)
        {
            CatiaColorOperationResult realColor = service.TryReadRgb(selection, target);
            LogColorQuery(targetLabel, "GetRealColor", realColor);

            CatiaColorOperationResult visibleColor = service.TryReadVisibleRgb(selection, target);
            LogColorQuery(targetLabel, "GetVisibleColor", visibleColor);

            CatiaColorInheritanceOperationResult realInheritance =
                service.TryReadRealColorInheritance(selection, target);
            LogInheritanceQuery(
                targetLabel,
                "GetRealInheritance(catVisPropertyColor)",
                realInheritance);

            CatiaColorInheritanceOperationResult visibleInheritance =
                service.TryReadVisibleColorInheritance(selection, target);
            LogInheritanceQuery(
                targetLabel,
                "GetVisibleInheritance(catVisPropertyColor)",
                visibleInheritance);
        }

        private void LogSelectionContext(
            string targetLabel,
            dynamic selection,
            CatiaColorTarget target)
        {
            try
            {
                selection.Clear();
                selection.Add(target.ComObject);

                int count;
                try { count = Convert.ToInt32(selection.Count2); }
                catch { count = Convert.ToInt32(selection.Count); }

                object? selectedValue = null;
                if (count > 0)
                {
                    try { selectedValue = selection.Item2(1).Value; }
                    catch { selectedValue = selection.Item(1).Value; }
                }

                object? properties = selection.VisProperties;
                string selectedType = selectedValue == null ? "(yok)" : ComProbe.TipAdi(selectedValue);
                string selectedKey = selectedValue == null
                    ? "(yok)"
                    : CatiaColorTargetService.GetSessionObjectKey(selectedValue);
                string propertiesType = properties == null ? "(yok)" : ComProbe.TipAdi(properties);
                bool sameTarget = string.Equals(
                    target.Identity.SessionObjectKey,
                    selectedKey,
                    StringComparison.Ordinal);

                LogInfo(
                    "Renk durumu teşhisi — " + targetLabel + " Selection" +
                    " | Count=" + count +
                    " | eklenen COM=" + target.Identity.ComType +
                    " | okunan COM=" + selectedType +
                    " | aynı COM hedefi=" + (sameTarget ? "EVET" : "HAYIR") +
                    " | VisProperties COM=" + propertiesType);
            }
            catch (Exception ex)
            {
                LogError(
                    "Renk durumu teşhisi — " + targetLabel +
                    " Selection bağlamı okunamadı | " +
                    RenkTestHataAyrintisi("Selection/VisProperties", ex));
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        private void LogColorQuery(
            string targetLabel,
            string query,
            CatiaColorOperationResult result)
        {
            if (!result.Success || !result.Color.HasValue)
            {
                string reason = result.Exception == null
                    ? result.Error
                    : RenkTestHataAyrintisi("VisProperties." + query, result.Exception);
                LogError(
                    "Renk durumu teşhisi — hedef=" + targetLabel +
                    " | " + query + " | başarısız | " + reason);
                return;
            }

            CatiaColorRgb value = result.Color.Value;
            string status = ColorPropertyStatusText(value.Status);
            string validity = value.Status == 0 ? "değer geçerli" : "değer geçerli sayılmadı";
            string message = "Renk durumu teşhisi — hedef=" + targetLabel +
                             " | " + query +
                             " | durum=" + status +
                             " | RGB=" + RgbText(value) +
                             " | " + validity;

            if (value.Status == 0)
                LogInfo(message);
            else
                LogError(message);
        }

        private void LogInheritanceQuery(
            string targetLabel,
            string query,
            CatiaColorInheritanceOperationResult result)
        {
            if (!result.Success || !result.Inheritance.HasValue)
            {
                string reason = result.Exception == null
                    ? result.Error
                    : RenkTestHataAyrintisi("VisProperties." + query, result.Exception);
                LogError(
                    "Renk durumu teşhisi — hedef=" + targetLabel +
                    " | " + query + " | başarısız | " + reason);
                return;
            }

            CatiaColorInheritance value = result.Inheritance.Value;
            string status = ColorPropertyStatusText(value.Status);
            string inheritance = value.Status == 0
                ? value.Inheritance switch
                {
                    0 => "0 (kalıtım yok)",
                    1 => "1 (kalıtım var)",
                    _ => value.Inheritance + " (beklenmeyen değer)"
                }
                : "(geçersiz; UnDefined durumda çıkış değeri kullanılmadı)";
            string message = "Renk durumu teşhisi — hedef=" + targetLabel +
                             " | " + query +
                             " | durum=" + status +
                             " | inheritance=" + inheritance;

            if (value.Status == 0)
                LogInfo(message);
            else
                LogError(message);
        }

        private static string ColorPropertyStatusText(int status) => status switch
        {
            0 => "Defined (0)",
            1 => "UnDefined (1)",
            _ => "Bilinmeyen (" + status + ")"
        };
    }
}
