using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Macria;

// hello

public sealed record CatiaScanSnapshotItem(string ReferenceTitle, string PlmName, string Revision,
    string ReferenceKey, int? Quantity, bool SheetMetalConfirmed);

public sealed class CatiaScanSnapshot
{
    public required DateTime CreatedAtUtc { get; init; }
    public required string ScanId { get; init; }
    public required IReadOnlyList<CatiaScanSnapshotItem> Items { get; init; }

    public static CatiaScanSnapshot FromAllRows(IEnumerable<SheetRow> rows) => new()
    {
        CreatedAtUtc = DateTime.UtcNow,
        ScanId = Guid.NewGuid().ToString("N"),
        Items = rows.Select(row => new CatiaScanSnapshotItem(row.ProductName, row.ReferenceName,
            row.Revision, row.ReferenceKey, row.Quantity > 0 ? row.Quantity : null, row.IsSheetMetal)).ToArray()
    };
}

public static class CatiaStepMatcher
{
    public static string NormalizeFileIdentity(string value)
    {
        string name = Path.GetFileNameWithoutExtension(value).Trim();
        return name.EndsWith("_Rep", StringComparison.OrdinalIgnoreCase) ? name[..^4].Trim() : name;
    }

    public static IReadOnlyList<CatiaScanSnapshotItem> Match(CatiaScanSnapshot snapshot, string stepPath) =>
        snapshot.Items.Where(x => string.Equals(NormalizeFileIdentity(x.ReferenceTitle), NormalizeFileIdentity(stepPath), StringComparison.OrdinalIgnoreCase)).ToArray();
}
