using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        // Salt-okunur teşhis: renk yazmaz, parça editörü açmaz ve yalnız seçili referansı inceler.
        private async void PartBodyHedefKesfi_Click(object sender, RoutedEventArgs e)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic catia = catiaObject;
                dynamic editor = catia.ActiveEditor;
                object? root = editor.ActiveObject;
                if (root is not object activeRoot)
                    throw new InvalidOperationException("Aktif CATIA montaj kökü alınamadı.");
                if (!HasOccurrences((dynamic)activeRoot))
                    throw new InvalidOperationException("Aktif CATIA nesnesi occurrence içeren bir Physical Product montajı değil.");

                List<object> selectionItems = SecimiSakla(editor.Selection);
                if (selectionItems.Count != 1)
                    throw new InvalidOperationException("CATIA'da PartBody'si araştırılacak tam olarak bir parça occurrence seçin.");

                object selectedOccurrence = selectionItems[0];
                var targetService = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
                CatiaColorTargetResult selectedResult = targetService.ResolveOccurrence(selectedOccurrence);
                if (!selectedResult.Success)
                    throw new InvalidOperationException(selectedResult.Error);

                string? referenceKey = AkilliRenklendirmeReferansAnahtari(selectedOccurrence);
                if (string.IsNullOrWhiteSpace(referenceKey))
                    throw new InvalidOperationException("Seçilen occurrence için PLM referans anahtarı çözümlenemedi.");

                List<object> leaves = await AkilliRenklendirmeYapraklariniTopla((dynamic)activeRoot);
                var matchingOccurrences = new List<object>();
                foreach (object leaf in leaves)
                {
                    string? leafReferenceKey;
                    try { leafReferenceKey = AkilliRenklendirmeReferansAnahtari(leaf); }
                    catch (Exception ex)
                    {
                        LogError("PartBody hedef keşfi — occurrence referansı okunamadı: " +
                                 OccurrenceAdi(leaf) + " | " + Kisa(ex.Message));
                        continue;
                    }

                    if (string.Equals(referenceKey, leafReferenceKey, StringComparison.Ordinal))
                        matchingOccurrences.Add(leaf);
                }

                if (matchingOccurrences.Count == 0)
                    throw new InvalidOperationException("Seçilen referans aktif montajın yaprak occurrence listesinde bulunamadı.");

                var resolvedTargets = new List<CatiaColorTarget>();
                int failedCount = 0;
                foreach (object occurrence in matchingOccurrences)
                {
                    PartBodyDiscoveryResult discovery = ResolveMountedPartBody(targetService, occurrence);
                    if (!discovery.Success || discovery.Target == null)
                    {
                        failedCount++;
                        LogError("PartBody hedef keşfi — " + OccurrenceAdi(occurrence) +
                                 " | başarısız | " + discovery.Error);
                        continue;
                    }

                    resolvedTargets.Add(discovery.Target);
                    LogInfo("PartBody hedef keşfi — " + OccurrenceAdi(occurrence) +
                            " | zincir=" + discovery.AccessChain +
                            " | COM türü=" + discovery.Target.Identity.ComType +
                            " | oturum hedefi=" + discovery.Target.Identity.SessionObjectKey);
                }

                int distinctTargetCount = resolvedTargets
                    .Select(target => target.Identity.SessionObjectKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                stopwatch.Stop();
                string commonState = resolvedTargets.Count == matchingOccurrences.Count && distinctTargetCount == 1
                    ? "aynı referansın occurrence'larında ortak PartBody hedefi görüldü"
                    : "ortak PartBody hedefi doğrulanamadı";

                LogInfo("PartBody hedef keşfi sonucu\n" +
                        "Referans: " + referenceKey + "\n" +
                        "Eş occurrence: " + matchingOccurrences.Count + "\n" +
                        "Çözülen PartBody: " + resolvedTargets.Count + "\n" +
                        "Başarısız: " + failedCount + "\n" +
                        "Farklı oturum hedefi: " + distinctTargetCount + "\n" +
                        "Sonuç: " + commonState + "\n" +
                        "Süre: " + stopwatch.ElapsedMilliseconds + " ms\n" +
                        "Renk uygulanmadı; parça editörü açılmadı.");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogError("PartBody hedef keşfi uygulanamadı: " + Kisa(ex.Message));
            }
        }

        private static PartBodyDiscoveryResult ResolveMountedPartBody(
            CatiaColorTargetService targetService,
            object occurrence)
        {
            try
            {
                object? repOccurrencesObject = ((dynamic)occurrence).RepOccurrences;
                if (repOccurrencesObject is not object availableRepOccurrences)
                    return PartBodyDiscoveryResult.Fail("Occurrence üzerinde RepOccurrences bulunamadı.");

                dynamic repOccurrences = availableRepOccurrences;
                int count = Convert.ToInt32(repOccurrences.Count);
                if (count <= 0)
                    return PartBodyDiscoveryResult.Fail("Occurrence üzerinde RepOccurrences bulunamadı.");

                var failures = new List<string>();
                for (int index = 1; index <= count; index++)
                {
                    try
                    {
                        dynamic repOccurrence = repOccurrences.Item(index);
                        dynamic repInstance = repOccurrence.RelatedRepInstance;
                        object? repReference = repInstance.ReferenceInstanceOf;
                        if (repReference == null)
                        {
                            failures.Add("representation " + index + ": ReferenceInstanceOf yok");
                            continue;
                        }

                        object? part = ParcaNesnesiAl(repReference);
                        if (part == null)
                        {
                            failures.Add("representation " + index + ": Part/CATIAPart alınamadı");
                            continue;
                        }

                        CatiaColorTargetResult targetResult = targetService.ResolvePartBody(part);
                        if (!targetResult.Success || targetResult.Target == null)
                        {
                            failures.Add("representation " + index + ": " + targetResult.Error);
                            continue;
                        }

                        string comType = targetResult.Target.Identity.ComType;
                        if (comType.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            failures.Add("representation " + index +
                                         ": MainBody döndü fakat COM türü Body olarak doğrulanamadı (" + comType + ")");
                            continue;
                        }

                        return PartBodyDiscoveryResult.Ok(
                            targetResult.Target,
                            "Occurrence.RepOccurrences[" + index +
                            "] → RelatedRepInstance → ReferenceInstanceOf → GetItem(Part/CATIAPart) → MainBody");
                    }
                    catch (Exception ex)
                    {
                        failures.Add("representation " + index + ": " +
                                     ex.GetType().Name + " 0x" +
                                     unchecked((uint)ex.HResult).ToString("X8") + " — " + ex.Message);
                    }
                }

                return PartBodyDiscoveryResult.Fail(
                    failures.Count == 0
                        ? "PartBody erişim zinciri sonuç üretmedi."
                        : string.Join(" | ", failures));
            }
            catch (Exception ex)
            {
                return PartBodyDiscoveryResult.Fail(
                    ex.GetType().Name + " 0x" + unchecked((uint)ex.HResult).ToString("X8") +
                    " — " + ex.Message);
            }
        }

        private sealed class PartBodyDiscoveryResult
        {
            private PartBodyDiscoveryResult(
                CatiaColorTarget? target,
                string accessChain,
                string error)
            {
                Target = target;
                AccessChain = accessChain;
                Error = error;
            }

            public CatiaColorTarget? Target { get; }
            public string AccessChain { get; }
            public string Error { get; }
            public bool Success => Target != null;

            public static PartBodyDiscoveryResult Ok(CatiaColorTarget target, string accessChain) =>
                new(target, accessChain, "");

            public static PartBodyDiscoveryResult Fail(string error) =>
                new(null, "", error);
        }
    }
}
