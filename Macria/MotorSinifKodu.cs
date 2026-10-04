using System;
using System.Linq;

namespace Macria;

/// <summary>
/// Why the engine classified a part as it did (GeometryLab classificationCode)
/// and whether any recognizer found evidence. Engine 2026.10.5.1+ writes both;
/// for older outputs (e.g. a saved .macria project) they are derived from the
/// Turkish reason texts and the sheet / profile records of the same analysis.
/// </summary>
public static class MotorSinifKodu
{
    public const string Sheet = "Sheet";
    public const string HollowProfile = "HollowProfile";
    public const string ProcessedProfile = "ProcessedProfile";
    public const string SheetProfileConflict = "SheetProfileConflict";
    public const string FlatPatternFailed = "FlatPatternFailed";
    public const string MultiSolid = "MultiSolid";
    public const string NoSolid = "NoSolid";
    public const string InvalidGeometry = "InvalidGeometry";
    public const string TimedOut = "TimedOut";
    public const string SolidBar = "SolidBar";
    public const string ThickerThanOutline = "ThickerThanOutline";
    public const string ThickerThanMaterial = "ThickerThanMaterial";
    public const string UnsupportedFaces = "UnsupportedFaces";
    public const string SheetAnalysisIncomplete = "SheetAnalysisIncomplete";
    public const string NotRecognized = "NotRecognized";

    public static (string Kod, bool Kanit) Belirle(GeometryLabAnalysisTransport analysis, GeometryLabPartTransport part)
    {
        if (!string.IsNullOrWhiteSpace(part.ClassificationCode))
            return (part.ClassificationCode!, part.RecognitionEvidence ?? false);
        string kod = KodCikar(part.Classification, part.ClassificationReasons);
        return (kod, kod is InvalidGeometry or TimedOut or NoSolid or MultiSolid ? false : KanitCikar(analysis, part));
    }

    /// <summary>The code from the reason texts the engine wrote before codes existed.</summary>
    public static string KodCikar(string? classification, System.Collections.Generic.IReadOnlyList<string> reasons)
    {
        bool Var(string metin) => reasons.Any(r => r.Contains(metin, StringComparison.Ordinal));
        bool Basliyor(string metin) => reasons.Any(r => r.StartsWith(metin, StringComparison.Ordinal));
        switch (classification)
        {
            case "Sheet":
                return Sheet;
            case "Profile":
                return Basliyor("İşlenmiş profil") ? ProcessedProfile : HollowProfile;
            case "ReviewRequired":
                if (Basliyor("Geçersiz geometri")) return InvalidGeometry;
                if (Basliyor("Analiz süresi aşıldı")) return TimedOut;
                if (Basliyor("Parçada solid yok")) return NoSolid;
                if (Basliyor("Parça birden fazla solid")) return MultiSolid;
                if (Basliyor("Sac ve profil tanıyıcıları")) return SheetProfileConflict;
                if (Basliyor("Sac tanındı ama açınım")) return FlatPatternFailed;
                if (Basliyor("Tanıyıcıların desteklemediği")) return UnsupportedFaces;
                return SheetAnalysisIncomplete;
            case "Other":
                if (Basliyor("Dolu kesit")) return SolidBar;
                if (Var("gerçek et genişliğinden")) return ThickerThanMaterial;
                if (Var("açınımın en dar ölçüsünden")) return ThickerThanOutline;
                return NotRecognized;
            default:
                return NotRecognized;
        }
    }

    /// <summary>
    /// As the engine decides it: a sheet thickness (offset skins), a recognized
    /// or ambiguous profile section, or a recognized base stock on the part's solid.
    /// </summary>
    public static bool KanitCikar(GeometryLabAnalysisTransport analysis, GeometryLabPartTransport part)
    {
        if (part.SolidIds.Count != 1) return false;
        int solid = part.SolidIds[0].LocalId;
        bool sac = analysis.SheetMetalAnalyses.Any(s => s.SolidId?.LocalId == solid && s.ThicknessMm != null);
        bool profil = analysis.ProfileRecognitions.Any(p => p.SolidId?.LocalId == solid &&
            p.SectionRecognitionStatus is "Recognized" or "Ambiguous");
        bool stok = analysis.BaseStockProfiles.Any(b => b.SolidId?.LocalId == solid &&
            b.BaseStockProfile?.Status == "Recognized");
        return sac || profil || stok;
    }
}
