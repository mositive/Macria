using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        // Yalnız kullanıcı tarafından çağrılan, renk yazmayan hafif CATIA occurrence envanteri.
        private void RenkEnvanteriTesti_Click(object sender, RoutedEventArgs e)
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
                    throw new InvalidOperationException("Aktif CATIA nesnesi occurrence içeren bir Physical Product montajı değil.");

                var reader = new CatiaLightInventoryReader();
                CatiaLightInventoryReadResult result = reader.Read(activeRoot);
                if (!result.Success || result.Snapshot == null)
                {
                    LogError("Renk envanteri tamamlanamadı: " + result.Error);
                    foreach (string diagnostic in result.Diagnostics.Take(10))
                        LogError("Renk envanteri tanı — " + diagnostic);
                    return;
                }

                CatiaLightInventorySnapshot snapshot = result.Snapshot;
                int totalQuantity = snapshot.UniqueParts.Sum(item => item.Quantity);
                string state = result.IsComplete ? "tam" : "EKSİK";
                LogInfo(
                    "Renk envanteri testi (" + state + ")\n" +
                    "Toplam occurrence: " + snapshot.TotalOccurrenceCount + "\n" +
                    "Eşsiz parça: " + snapshot.UniqueParts.Count + "\n" +
                    "Toplam parça adedi: " + totalQuantity + "\n" +
                    "Grup sayısı: " + snapshot.Groups.Count + "\n" +
                    "Kimliği çözümlenemeyen: " + snapshot.UnresolvedIdentityCount + "\n" +
                    "Hedefi çözümlenemeyen: " + snapshot.UnresolvedTargetCount + "\n" +
                    "Toplam süre: " + Math.Max(0, (long)snapshot.Duration.TotalMilliseconds) + " ms");

                foreach (CatiaUniquePartInventoryItem item in snapshot.UniqueParts.Take(10))
                {
                    LogInfo("Renk envanteri parça — Kod=" + Empty(item.PartNumber) +
                            " | Referans=" + item.ReferenceKey + " | Adet=" + item.Quantity);
                }

                foreach (CatiaGroupInventoryItem group in snapshot.Groups.Take(10))
                {
                    LogInfo("Renk envanteri grup — Ad=" + Empty(group.DisplayName) +
                            " | Occurrence=" + group.GroupKey + " | Yol=" + group.HierarchyPath);
                }

                if (!result.IsComplete)
                {
                    LogError("Renk envanteri eksik veriyle üretildi; aşağıdaki düğüm tanılarını kontrol edin.");
                    foreach (string diagnostic in result.Diagnostics.Take(20))
                        LogError("Renk envanteri tanı — " + diagnostic);
                }
                else
                {
                    LogSuccess("Renk envanteri testi tamamlandı. Renk uygulanmadı.");
                }
            }
            catch (Exception ex)
            {
                LogError("Renk envanteri testi uygulanamadı: " + Kisa(ex.Message));
            }
        }

        private static string Empty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "(okunamadı)" : value;

        private sealed class CatiaLightInventoryReader
        {
            private readonly List<string> _diagnostics = new();
            private readonly HashSet<string> _occurrenceKeys = new(StringComparer.Ordinal);
            private readonly HashSet<string> _ancestorSessionKeys = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<object>> _occurrencesByReference =
                new(StringComparer.Ordinal);
            private bool _complete = true;

            public CatiaLightInventoryReadResult Read(object root)
            {
                if (root == null)
                    return CatiaLightInventoryReadResult.Fail("Aktif montaj kökü alınamadı.");

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    CatiaLightOccurrenceRecord rootRecord = ReadNode(root, "", 0, true);
                    CatiaLightInventorySnapshot snapshot = CatiaLightInventoryService.Build(new[] { rootRecord });
                    stopwatch.Stop();
                    snapshot.Duration = stopwatch.Elapsed;
                    return CatiaLightInventoryReadResult.Ok(
                        snapshot,
                        _complete,
                        _diagnostics,
                        _occurrencesByReference);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _diagnostics.Add("Kök occurrence okunamadı: " + ComError(ex));
                    return CatiaLightInventoryReadResult.Fail("Aktif montaj envanteri üretilemedi.", _diagnostics);
                }
            }

            private CatiaLightOccurrenceRecord ReadNode(object node, string parentPath, int siblingIndex, bool isRoot)
            {
                // Occurrence Name yalnız oturumluk teknik yol içindir; kullanıcıya parça/grup adı olarak gösterilmez.
                string occurrenceName = ReadOccurrenceName(node);
                string nodePath = isRoot
                    ? "ROOT"
                    : parentPath + "/" + PathSegment(occurrenceName, siblingIndex);
                string sessionKey = SessionTraversalKey(node);
                if (!_ancestorSessionKeys.Add(sessionKey))
                {
                    _complete = false;
                    _diagnostics.Add("Döngü algılandı; düğüm atlandı: " + nodePath);
                    return new CatiaLightOccurrenceRecord
                    {
                        OccurrenceKey = UniqueOccurrenceKey(null, nodePath),
                        IsRoot = isRoot
                    };
                }

                try
                {
                    object? reference = null;
                    try { reference = ReferansAl((dynamic)node); }
                    catch (Exception ex) { AddNodeError(nodePath, "PLM referansı", ex); }

                    string referenceKey = ReadReferenceKey(reference, nodePath);
                    string partNumber = ReadPlm(reference, "PLM_ExternalID", nodePath);
                    string revision = ReadFirstPlm(reference, nodePath, "V_version", "revision", "Revision");
                    // Ürün ağacıyla aynı Reference Title kaynağı: V_Name, ardından Title.
                    string displayName = reference == null ? "" : PlmBaslik(reference);

                    int childCount = ReadChildCount(node, nodePath, out bool childrenRead);
                    bool isGroup = childrenRead && childCount > 0;
                    string occurrenceKey = UniqueOccurrenceKey(ReadOccurrencePlmKey(node, nodePath), nodePath);
                    var record = new CatiaLightOccurrenceRecord
                    {
                        OccurrenceKey = occurrenceKey,
                        ReferenceKey = referenceKey,
                        PartNumber = partNumber,
                        DisplayName = displayName,
                        Revision = revision,
                        IsRoot = isRoot,
                        // PartBody çözümü özellikle sonraki aşamaya bırakılır.
                        HasPartBodyReference = false,
                        IsPart = !isRoot && childrenRead && !isGroup
                    };

                    if (record.IsPart && !string.IsNullOrWhiteSpace(referenceKey))
                    {
                        if (!_occurrencesByReference.TryGetValue(referenceKey, out List<object>? occurrences))
                        {
                            occurrences = new List<object>();
                            _occurrencesByReference.Add(referenceKey, occurrences);
                        }

                        occurrences.Add(node);
                    }

                    if (!isRoot && string.IsNullOrWhiteSpace(referenceKey))
                    {
                        _complete = false;
                        _diagnostics.Add("PLM referans kimliği okunamadı: " + nodePath +
                                         " | occurrence=" + occurrenceKey);
                    }

                    if (!isRoot && string.IsNullOrWhiteSpace(displayName))
                    {
                        _complete = false;
                        _diagnostics.Add("Reference Title okunamadı: " + nodePath +
                                         " | occurrence=" + occurrenceKey);
                    }

                    if (!childrenRead) return record;

                    for (int index = 1; index <= childCount; index++)
                    {
                        try
                        {
                            object child = ((dynamic)node).Occurrences.Item(index);
                            if (child == null)
                            {
                                _complete = false;
                                _diagnostics.Add("Boş child occurrence: " + nodePath + " | sıra=" + index);
                                continue;
                            }
                            record.Children.Add(ReadNode(child, nodePath, index, false));
                        }
                        catch (Exception ex)
                        {
                            AddNodeError(nodePath + " | child=" + index, "Alt occurrence", ex);
                        }
                    }

                    return record;
                }
                finally
                {
                    _ancestorSessionKeys.Remove(sessionKey);
                }
            }

            private int ReadChildCount(object node, string nodePath, out bool childrenRead)
            {
                childrenRead = false;
                try
                {
                    dynamic children = ((dynamic)node).Occurrences;
                    childrenRead = true;
                    return children == null ? 0 : Math.Max(0, Convert.ToInt32(children.Count));
                }
                catch (Exception ex)
                {
                    AddNodeError(nodePath, "Occurrences", ex);
                    return 0;
                }
            }

            private string ReadOccurrenceName(object node)
            {
                try
                {
                    string? value = Convert.ToString(((dynamic)node).Name);
                    return value?.Trim() ?? "";
                }
                catch
                {
                    return "";
                }
            }

            private string ReadReferenceKey(object? reference, string nodePath)
            {
                if (reference == null) return "";
                string externalId = ReadPlm(reference, "PLM_ExternalID", nodePath);
                string version = ReadFirstPlm(reference, nodePath, "V_version", "revision", "Revision");
                return AkilliRenklendirmeMantigi.ReferansAnahtari(externalId, version) ?? "";
            }

            private string? ReadOccurrencePlmKey(object node, string nodePath)
            {
                try
                {
                    object? entity = ((dynamic)node).PLMEntity;
                    if (entity == null) return null;
                    string externalId = ReadPlm(entity, "PLM_ExternalID", nodePath);
                    string version = ReadFirstPlm(entity, nodePath, "V_version", "revision", "Revision");
                    string? key = AkilliRenklendirmeMantigi.ReferansAnahtari(externalId, version);
                    return string.IsNullOrWhiteSpace(key) ? null : "PLM:" + key;
                }
                catch (Exception ex)
                {
                    AddNodeError(nodePath, "Occurrence PLM kimliği", ex);
                    return null;
                }
            }

            private string UniqueOccurrenceKey(string? stableKey, string path)
            {
                if (!string.IsNullOrWhiteSpace(stableKey) && _occurrenceKeys.Add(stableKey))
                    return stableKey;

                string fallback = "PATH:" + path;
                _occurrenceKeys.Add(fallback);
                return fallback;
            }

            private string ReadFirstPlm(object? source, string nodePath, params string[] members)
            {
                foreach (string member in members)
                {
                    string value = ReadPlm(source, member, nodePath);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                return "";
            }

            private string ReadPlm(object? source, string member, string nodePath)
            {
                if (source == null) return "";
                try
                {
                    object? value = ((dynamic)source).GetAttributeValue(member);
                    string? text = Convert.ToString(value);
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                }
                catch (Exception ex)
                {
                    // API sürümleri property erişimi sunabilir; bu ilk deneme hata sayılmaz.
                    if (KritikComHatasiMi(ex)) AddNodeError(nodePath, member, ex);
                }

                try
                {
                    dynamic d = source;
                    object? value = member switch
                    {
                        "PLM_ExternalID" => d.PLM_ExternalID,
                        "V_version" => d.V_version,
                        "revision" => d.revision,
                        "Revision" => d.Revision,
                        "V_Name" => d.V_Name,
                        "Title" => d.Title,
                        _ => null
                    };
                    return Convert.ToString(value)?.Trim() ?? "";
                }
                catch (Exception ex)
                {
                    if (KritikComHatasiMi(ex)) AddNodeError(nodePath, member, ex);
                    return "";
                }
            }

            private void AddNodeError(string nodePath, string stage, Exception exception)
            {
                _complete = false;
                _diagnostics.Add("Düğüm=" + nodePath + " | aşama=" + stage + " | " + ComError(exception));
            }

            private static string PathSegment(string displayName, int siblingIndex)
            {
                string safe = string.IsNullOrWhiteSpace(displayName) ? "(adsız)" : displayName.Replace("/", "_");
                return safe + "#" + siblingIndex;
            }

            // Yalnız aynı aktif traversal yolundaki döngüleri engeller; kalıcı kimlik değildir.
            private static string SessionTraversalKey(object node)
            {
                IntPtr unknown = IntPtr.Zero;
                try
                {
                    if (Marshal.IsComObject(node))
                    {
                        unknown = Marshal.GetIUnknownForObject(node);
                        return "COM:" + unknown.ToInt64().ToString("X");
                    }
                }
                catch { }
                finally
                {
                    if (unknown != IntPtr.Zero) Marshal.Release(unknown);
                }

                return "RCW:" + RuntimeHelpers.GetHashCode(node).ToString("X");
            }

            private static string ComError(Exception exception) =>
                "tür=" + exception.GetType().Name +
                " | HRESULT=0x" + unchecked((uint)exception.HResult).ToString("X8") +
                " | mesaj=" + exception.Message;
        }

        private sealed class CatiaLightInventoryReadResult
        {
            private CatiaLightInventoryReadResult(
                bool success,
                CatiaLightInventorySnapshot? snapshot,
                bool isComplete,
                string error,
                IReadOnlyList<string> diagnostics,
                IReadOnlyDictionary<string, IReadOnlyList<object>> occurrencesByReference)
            {
                Success = success;
                Snapshot = snapshot;
                IsComplete = isComplete;
                Error = error;
                Diagnostics = diagnostics;
                OccurrencesByReference = occurrencesByReference;
            }

            public bool Success { get; }
            public CatiaLightInventorySnapshot? Snapshot { get; }
            public bool IsComplete { get; }
            public string Error { get; }
            public IReadOnlyList<string> Diagnostics { get; }
            public IReadOnlyDictionary<string, IReadOnlyList<object>> OccurrencesByReference { get; }

            public static CatiaLightInventoryReadResult Ok(
                CatiaLightInventorySnapshot snapshot,
                bool isComplete,
                IReadOnlyList<string> diagnostics,
                IReadOnlyDictionary<string, List<object>> occurrencesByReference) =>
                new(
                    true,
                    snapshot,
                    isComplete,
                    "",
                    diagnostics.ToArray(),
                    occurrencesByReference.ToDictionary(
                        pair => pair.Key,
                        pair => (IReadOnlyList<object>)pair.Value.ToArray(),
                        StringComparer.Ordinal));

            public static CatiaLightInventoryReadResult Fail(
                string error,
                IReadOnlyList<string>? diagnostics = null) =>
                new(
                    false,
                    null,
                    false,
                    error,
                    diagnostics?.ToArray() ?? Array.Empty<string>(),
                    new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal));
        }
    }
}
