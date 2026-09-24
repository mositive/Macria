using System;
using System.Collections.Generic;
using System.Linq;

namespace Macria
{
    public enum CatiaInventoryTargetState
    {
        Unresolved,
        Resolved,
        Ambiguous
    }

    // COM içermeyen giriş modeli; üretim COM okuyucusu yalnız hafif alanları buna taşır.
    public sealed class CatiaLightOccurrenceRecord
    {
        public string? ReferenceKey { get; init; }
        public string OccurrenceKey { get; init; } = "";
        public string PartNumber { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Revision { get; init; } = "";
        public bool IsPart { get; init; }
        public bool IsRoot { get; init; }
        public bool HasPartBodyReference { get; init; }
        public List<CatiaLightOccurrenceRecord> Children { get; } = new();
    }

    public sealed class CatiaUniquePartInventoryItem
    {
        internal CatiaUniquePartInventoryItem(string key, CatiaLightOccurrenceRecord source)
        {
            ReferenceKey = key;
            PartNumber = source.PartNumber;
            DisplayName = source.DisplayName;
            Revision = source.Revision;
            TargetState = source.HasPartBodyReference
                ? CatiaInventoryTargetState.Unresolved
                : CatiaInventoryTargetState.Unresolved;
        }

        public string ReferenceKey { get; }
        public string PartNumber { get; }
        public string DisplayName { get; }
        public string Revision { get; }
        public int Quantity { get; internal set; }
        public List<string> OccurrenceKeys { get; } = new();
        public CatiaInventoryTargetState TargetState { get; internal set; }
        public bool ConflictingMetadata { get; internal set; }
        public string TargetType => "PartBody";
    }

    public sealed class CatiaGroupInventoryItem
    {
        internal CatiaGroupInventoryItem(string groupKey, CatiaLightOccurrenceRecord source, string path)
        {
            GroupKey = groupKey;
            DisplayName = source.DisplayName;
            PartNumber = source.PartNumber;
            ReferenceKey = source.ReferenceKey;
            ParentGroupKey = path;
            string segment = source.DisplayName + " [" + source.OccurrenceKey + "]";
            HierarchyPath = path.Length == 0 ? segment : path + "/" + segment;
            TargetState = source.Children.Count > 0 && source.ReferenceKey != null
                ? CatiaInventoryTargetState.Resolved
                : CatiaInventoryTargetState.Unresolved;
        }

        public string GroupKey { get; }
        public string DisplayName { get; }
        public string PartNumber { get; }
        public string? ReferenceKey { get; }
        public string ParentGroupKey { get; }
        public string HierarchyPath { get; }
        public List<string> ChildGroupKeys { get; } = new();
        public CatiaInventoryTargetState TargetState { get; }
        public string TargetType => "Product";
    }

    public sealed class CatiaLightInventorySnapshot
    {
        public IReadOnlyList<CatiaUniquePartInventoryItem> UniqueParts { get; internal set; } = Array.Empty<CatiaUniquePartInventoryItem>();
        public IReadOnlyList<CatiaGroupInventoryItem> Groups { get; internal set; } = Array.Empty<CatiaGroupInventoryItem>();
        public int TotalOccurrenceCount { get; internal set; }
        public int UnresolvedIdentityCount { get; internal set; }
        public int UnresolvedTargetCount { get; internal set; }
        public TimeSpan Duration { get; internal set; }
    }

    // Hafif, tek geçişli ve COM'suz çekirdek. CATIA okuyucusu bu modele kayıt üretir.
    public static class CatiaLightInventoryService
    {
        public static CatiaLightInventorySnapshot Build(IEnumerable<CatiaLightOccurrenceRecord> roots)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            var started = DateTime.UtcNow;
            var parts = new Dictionary<string, CatiaUniquePartInventoryItem>(StringComparer.Ordinal);
            var groups = new List<CatiaGroupInventoryItem>();
            var unresolvedIdentities = 0;
            var unresolvedTargets = 0;
            var total = 0;

            foreach (var root in roots)
                Visit(root, "", null, true, parts, groups, ref total, ref unresolvedIdentities, ref unresolvedTargets);

            return new CatiaLightInventorySnapshot
            {
                UniqueParts = parts.Values.OrderBy(x => x.ReferenceKey, StringComparer.Ordinal).ToArray(),
                Groups = groups.OrderBy(x => x.HierarchyPath, StringComparer.Ordinal).ToArray(),
                TotalOccurrenceCount = total,
                UnresolvedIdentityCount = unresolvedIdentities,
                UnresolvedTargetCount = unresolvedTargets,
                Duration = DateTime.UtcNow - started
            };
        }

        private static void Visit(
            CatiaLightOccurrenceRecord node,
            string parentPath,
            string? parentGroupKey,
            bool isRoot,
            Dictionary<string, CatiaUniquePartInventoryItem> parts,
            List<CatiaGroupInventoryItem> groups,
            ref int total,
            ref int unresolvedIdentities,
            ref int unresolvedTargets)
        {
            total++;
            string path = isRoot ? "" : parentPath;
            bool isGroup = node.Children.Count > 0;

            if (!isRoot && isGroup)
            {
                var group = new CatiaGroupInventoryItem(node.OccurrenceKey, node, path);
                groups.Add(group);
                if (parentGroupKey != null)
                {
                    CatiaGroupInventoryItem? parent = groups.FirstOrDefault(x =>
                        string.Equals(x.GroupKey, parentGroupKey, StringComparison.Ordinal));
                    parent?.ChildGroupKeys.Add(group.GroupKey);
                }
                if (group.TargetState != CatiaInventoryTargetState.Resolved) unresolvedTargets++;
                path = group.HierarchyPath;
                parentGroupKey = group.GroupKey;
            }

            if (!isRoot && node.IsPart && !isGroup)
            {
                if (string.IsNullOrWhiteSpace(node.ReferenceKey))
                {
                    unresolvedIdentities++;
                }
                else if (!parts.TryGetValue(node.ReferenceKey, out var item))
                {
                    item = new CatiaUniquePartInventoryItem(node.ReferenceKey, node);
                    parts.Add(node.ReferenceKey, item);
                }
                else if (!string.Equals(item.PartNumber, node.PartNumber, StringComparison.Ordinal) ||
                         !string.Equals(item.Revision, node.Revision, StringComparison.Ordinal))
                {
                    item.ConflictingMetadata = true;
                    item.TargetState = CatiaInventoryTargetState.Ambiguous;
                }

                if (!string.IsNullOrWhiteSpace(node.ReferenceKey))
                {
                    parts[node.ReferenceKey].Quantity++;
                    parts[node.ReferenceKey].OccurrenceKeys.Add(node.OccurrenceKey);
                    if (parts[node.ReferenceKey].TargetState == CatiaInventoryTargetState.Unresolved)
                        unresolvedTargets++;
                }
            }

            foreach (var child in node.Children)
                Visit(child, path, parentGroupKey, false, parts, groups, ref total, ref unresolvedIdentities, ref unresolvedTargets);
        }
    }
}
