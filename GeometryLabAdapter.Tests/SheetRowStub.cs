namespace Macria;

// Test-only compile surface for the COM-free snapshot factory.
public sealed class SheetRow
{
    public string ProductName { get; set; } = "";
    public string ReferenceName { get; set; } = "";
    public string Revision { get; set; } = "";
    public string ReferenceKey { get; set; } = "";
    public int Quantity { get; set; }
    public bool IsSheetMetal { get; set; }
}
