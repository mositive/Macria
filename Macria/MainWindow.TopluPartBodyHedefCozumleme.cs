using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        // Salt-okunur teşhis: her eşsiz referans için PartBody yalnız bir kez çözülür.
        // Renk yazmaz, parça editörü açmaz ve ana Akıllı Renklendirme akışını değiştirmez.
        private void TopluPartBodyHedefCozumleme_Click(object sender, RoutedEventArgs e)
        {
            var totalStopwatch = Stopwatch.StartNew();
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
                    throw new InvalidOperationException(
                        "Aktif CATIA nesnesi occurrence içeren bir Physical Product montajı değil.");

                LogInfo("Toplu PartBody hedef çözümleme başlatıldı. Renk uygulanmayacak; parça editörü açılmayacak.");

                var inventoryReader = new CatiaLightInventoryReader();
                CatiaLightInventoryReadResult inventoryResult = inventoryReader.Read(activeRoot);
                if (!inventoryResult.Success || inventoryResult.Snapshot == null)
                {
                    LogError("Toplu PartBody hedef çözümleme — hafif envanter alınamadı: " +
                             inventoryResult.Error);
                    foreach (string diagnostic in inventoryResult.Diagnostics.Take(20))
                        LogError("Toplu PartBody envanter tanısı — " + diagnostic);
                    return;
                }

                CatiaLightInventorySnapshot inventory = inventoryResult.Snapshot;
                BulkPartBodyResolutionBatch batch = ResolveBulkPartBodyTargets(
                    inventory,
                    inventoryResult.OccurrencesByReference);

                foreach (BulkPartBodyResolution resolution in batch.Resolutions)
                    LogBulkResolution(resolution);

                foreach (KeyValuePair<string, IReadOnlyList<string>> collision in batch.Collisions)
                {
                    LogError("Toplu PartBody hedef çakışması — farklı referanslar aynı COM hedefini bildirdi: " +
                             string.Join(", ", collision.Value) +
                             " | oturum hedefi=" + collision.Key);
                }

                int resolvedCount = batch.Resolutions.Count(result => result.Success);
                int failedCount = batch.Resolutions.Count - resolvedCount;
                int safeResolvedCount = batch.SafeResolutions.Count;
                int linkedOccurrenceCount = batch.SafeResolutions.Sum(result => result.OccurrenceCount);

                totalStopwatch.Stop();
                LogInfo("Toplu PartBody hedef çözümleme sonucu\n" +
                        "Eşsiz referans: " + inventory.UniqueParts.Count + "\n" +
                        "Body bulunan referans: " + resolvedCount + "\n" +
                        "Güvenli çözülen referans: " + safeResolvedCount + "\n" +
                        "Başarısız / çözümlenmemiş referans: " + failedCount + "\n" +
                        "Kimliği çözümlenemeyen occurrence: " + inventory.UnresolvedIdentityCount + "\n" +
                        "Hedefle ilişkilendirilen occurrence: " + linkedOccurrenceCount + "\n" +
                        "Referanslar arası hedef çakışması: " + batch.Collisions.Count + "\n" +
                        "Envanter durumu: " + (inventoryResult.IsComplete ? "tam" : "EKSİK") + "\n" +
                        "Toplam süre: " + totalStopwatch.ElapsedMilliseconds + " ms\n" +
                        "Renk uygulanmadı; parça editörü açılmadı.");

                if (!inventoryResult.IsComplete)
                {
                    LogError("Toplu PartBody hedef çözümleme eksik envanterle çalıştı. " +
                             "Çözümlenemeyen kayıtlar güvenli biçimde hedef dışı bırakıldı.");
                    foreach (string diagnostic in inventoryResult.Diagnostics.Take(20))
                        LogError("Toplu PartBody envanter tanısı — " + diagnostic);
                }
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                LogError("Toplu PartBody hedef çözümleme uygulanamadı: " + ComDiagnostic(ex));
            }
        }

        // Renklendirme 2.0 ve salt-okunur toplu teşhis aynı doğrulanmış hedef planını kullanır.
        // Her unique reference için yalnız representative occurrence üzerinden bir kez PartBody çözülür.
        private static BulkPartBodyResolutionBatch ResolveBulkPartBodyTargets(
            CatiaLightInventorySnapshot inventory,
            IReadOnlyDictionary<string, IReadOnlyList<object>> occurrencesByReference,
            ISet<string>? includedReferenceKeys = null)
        {
            var targetService = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
            var resolutions = new List<BulkPartBodyResolution>();

            foreach (CatiaUniquePartInventoryItem item in inventory.UniqueParts)
            {
                if (includedReferenceKeys != null &&
                    !includedReferenceKeys.Contains(item.ReferenceKey))
                {
                    continue;
                }

                var referenceStopwatch = Stopwatch.StartNew();
                BulkPartBodyResolution resolution;

                if (string.IsNullOrWhiteSpace(item.ReferenceKey))
                {
                    resolution = BulkPartBodyResolution.Fail(
                        item, 0, "Envanter referans kimliği eksik.", referenceStopwatch.Elapsed);
                }
                else if (item.ConflictingMetadata || item.TargetState == CatiaInventoryTargetState.Ambiguous)
                {
                    resolution = BulkPartBodyResolution.Fail(
                        item, 0, "Aynı referans için çelişkili envanter metadatası bulundu.", referenceStopwatch.Elapsed);
                }
                else if (!occurrencesByReference.TryGetValue(
                             item.ReferenceKey,
                             out IReadOnlyList<object>? occurrences) ||
                         occurrences.Count == 0)
                {
                    resolution = BulkPartBodyResolution.Fail(
                        item, 0, "Envanter referansı için yaprak occurrence bulunamadı.", referenceStopwatch.Elapsed);
                }
                else if (occurrences.Count != item.Quantity)
                {
                    resolution = BulkPartBodyResolution.Fail(
                        item,
                        occurrences.Count,
                        "Envanter adedi ile eşleşen occurrence sayısı farklı " +
                        "(envanter=" + item.Quantity + ", eşleşen=" + occurrences.Count + ").",
                        referenceStopwatch.Elapsed);
                }
                else
                {
                    object representativeOccurrence = occurrences[0];
                    PartBodyDiscoveryResult discovery = ResolveMountedPartBody(
                        targetService,
                        representativeOccurrence);
                    referenceStopwatch.Stop();
                    if (!discovery.Success || discovery.Target == null)
                    {
                        resolution = BulkPartBodyResolution.Fail(
                            item,
                            occurrences.Count,
                            discovery.Error,
                            referenceStopwatch.Elapsed);
                    }
                    else
                    {
                        string comType = discovery.Target.Identity.ComType;
                        if (comType.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            resolution = BulkPartBodyResolution.Fail(
                                item,
                                occurrences.Count,
                                "Çözülen hedefin COM türü Body olarak doğrulanamadı (" + comType + ").",
                                referenceStopwatch.Elapsed);
                        }
                        else
                        {
                            string? targetReferenceKey = discovery.Target.Identity.ReferenceKey;
                            if (!string.IsNullOrWhiteSpace(targetReferenceKey) &&
                                !string.Equals(item.ReferenceKey, targetReferenceKey, StringComparison.Ordinal))
                            {
                                resolution = BulkPartBodyResolution.Fail(
                                    item,
                                    occurrences.Count,
                                    "Çözülen Body farklı bir referans kimliği bildirdi (" +
                                    targetReferenceKey + ").",
                                    referenceStopwatch.Elapsed);
                            }
                            else
                            {
                                resolution = BulkPartBodyResolution.Ok(
                                    item,
                                    occurrences.Count,
                                    representativeOccurrence,
                                    discovery.Target,
                                    discovery.AccessChain,
                                    referenceStopwatch.Elapsed);
                            }
                        }
                    }
                }

                referenceStopwatch.Stop();
                resolutions.Add(resolution);
            }

            var collisions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var collidingReferenceKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (IGrouping<string, BulkPartBodyResolution> collision in resolutions
                         .Where(result => result.Success && result.Target != null)
                         .GroupBy(result => result.Target!.Identity.SessionObjectKey, StringComparer.Ordinal)
                         .Where(group => group.Select(result => result.Item.ReferenceKey)
                             .Distinct(StringComparer.Ordinal).Count() > 1))
            {
                string[] referenceKeys = collision
                    .Select(result => result.Item.ReferenceKey)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray();
                collisions.Add(collision.Key, referenceKeys);
                foreach (string referenceKey in referenceKeys)
                    collidingReferenceKeys.Add(referenceKey);
            }

            return new BulkPartBodyResolutionBatch(
                resolutions,
                resolutions
                    .Where(result => result.Success &&
                                     !collidingReferenceKeys.Contains(result.Item.ReferenceKey))
                    .ToArray(),
                collidingReferenceKeys,
                collisions);
        }

        private void LogBulkResolution(BulkPartBodyResolution resolution)
        {
            string title = string.IsNullOrWhiteSpace(resolution.Item.DisplayName)
                ? "(Reference Title okunamadı)"
                : resolution.Item.DisplayName;
            string prefix = "Toplu PartBody — Reference Title=" + title +
                            " | referans=" + resolution.Item.ReferenceKey +
                            " | occurrence=" + resolution.OccurrenceCount +
                            " | süre=" + Math.Max(0, (long)resolution.Duration.TotalMilliseconds) + " ms";

            if (!resolution.Success || resolution.Target == null)
            {
                LogError(prefix + " | ÇÖZÜMLENEMEDİ | " + resolution.Error);
                return;
            }

            LogInfo(prefix +
                    " | COM türü=" + resolution.Target.Identity.ComType +
                    " | oturum hedefi=" + resolution.Target.Identity.SessionObjectKey +
                    " | zincir=" + resolution.AccessChain);
        }

        private static string ComDiagnostic(Exception exception) =>
            exception.GetType().Name + " 0x" +
            unchecked((uint)exception.HResult).ToString("X8") + " — " + exception.Message;

        private sealed class BulkPartBodyResolution
        {
            private BulkPartBodyResolution(
                CatiaUniquePartInventoryItem item,
                int occurrenceCount,
                object? representativeOccurrence,
                CatiaColorTarget? target,
                string accessChain,
                string error,
                TimeSpan duration)
            {
                Item = item;
                OccurrenceCount = occurrenceCount;
                RepresentativeOccurrence = representativeOccurrence;
                Target = target;
                AccessChain = accessChain;
                Error = error;
                Duration = duration;
            }

            public CatiaUniquePartInventoryItem Item { get; }
            public int OccurrenceCount { get; }
            public object? RepresentativeOccurrence { get; }
            public CatiaColorTarget? Target { get; }
            public string AccessChain { get; }
            public string Error { get; }
            public TimeSpan Duration { get; }
            public bool Success => Target != null;

            public static BulkPartBodyResolution Ok(
                CatiaUniquePartInventoryItem item,
                int occurrenceCount,
                object representativeOccurrence,
                CatiaColorTarget target,
                string accessChain,
                TimeSpan duration) =>
                new(item, occurrenceCount, representativeOccurrence, target, accessChain, "", duration);

            public static BulkPartBodyResolution Fail(
                CatiaUniquePartInventoryItem item,
                int occurrenceCount,
                string error,
                TimeSpan duration) =>
                new(item, occurrenceCount, null, null, "", error, duration);
        }

        private sealed class BulkPartBodyResolutionBatch
        {
            public BulkPartBodyResolutionBatch(
                IReadOnlyList<BulkPartBodyResolution> resolutions,
                IReadOnlyList<BulkPartBodyResolution> safeResolutions,
                IReadOnlySet<string> collidingReferenceKeys,
                IReadOnlyDictionary<string, IReadOnlyList<string>> collisions)
            {
                Resolutions = resolutions;
                SafeResolutions = safeResolutions;
                CollidingReferenceKeys = collidingReferenceKeys;
                Collisions = collisions;
            }

            public IReadOnlyList<BulkPartBodyResolution> Resolutions { get; }
            public IReadOnlyList<BulkPartBodyResolution> SafeResolutions { get; }
            public IReadOnlySet<string> CollidingReferenceKeys { get; }
            public IReadOnlyDictionary<string, IReadOnlyList<string>> Collisions { get; }
        }
    }
}
