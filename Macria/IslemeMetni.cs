using System.Collections.Generic;
using System.Linq;

namespace Macria;

/// <summary>
/// The engine's machining features (parts[].machiningKinds) as the tables and
/// "Seçili Parça" show them, the same on every tab and row type.
/// </summary>
public static class IslemeMetni
{
    /// <summary>Table cell: "—", "var", or "var (gravür, cep)".</summary>
    public static string Kisa(bool var, IReadOnlyList<string> turler)
    {
        if (!var && turler.Count == 0) return "—";
        List<string> adlar = Adlar(turler, ayrinti: false);
        return adlar.Count == 0 ? "var" : "var (" + string.Join(", ", adlar) + ")";
    }

    /// <summary>
    /// "Seçili Parça" line: "yok", or the features in full; a sheet adds that
    /// they are not in its DXF. Other parts have no DXF to leave them out of.
    /// </summary>
    public static string Ayrinti(bool var, IReadOnlyList<string> turler, bool sac)
    {
        if (!var && turler.Count == 0) return "yok";
        List<string> adlar = Adlar(turler, ayrinti: true);
        return (adlar.Count == 0 ? "var" : "var (" + string.Join(", ", adlar) + ")") + (sac ? "; DXF'te yok" : "");
    }

    // Pocket is the engine's machined-plate step: pockets, steps, counterbore and countersink heads.
    private static List<string> Adlar(IReadOnlyList<string> turler, bool ayrinti) => turler
        .Select(tur => tur switch
        {
            "Engraving" => "gravür",
            "Embossing" => "kabartma",
            "Pocket" => ayrinti ? "cep, basamak, havşa / imbus başı" : "cep",
            // Sac olarak dene: faces left outside the skins taken as machining.
            "Machined" => ayrinti ? "kabuk dışı yüzler (sac denemesi)" : "kabuk dışı",
            _ => null
        })
        .OfType<string>().Distinct().ToList();
}
