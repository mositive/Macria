using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Macria;

// Where a part of an assembly STEP is listed in the Dosya Analiz Merkezi.
public enum MontajParcaKategorisi
{
    Sac,            // Saclar: approved (CATIA sheet-metal feature or user)
    OnayGerekli,    // Saclar: engine found a geometric sheet, not confirmed yet
    KontrolGerekli, // Kontrol gerekli tab: evidence found, not decided
    Diger,          // Kontrol gerekli tab, class "Diğer" (solid bar, ring, nut, shaft)
    Profil,         // Profiller tab (existing profile rows)
    Tanimsiz        // Tanımsız tab: no evidence, invalid geometry, timed out, no solid
}

/// <summary>
/// Session-only row for one part of an assembly STEP (GeometryLab schema 1.2).
/// The engine class is pure geometry; the CATIA sheet-metal feature and the
/// user's decision settle what Macria shows. No CATIA COM references.
/// </summary>
public sealed class MontajParcaSatiri : INotifyPropertyChanged, IAnalizSatiri
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public required string SourceStepPath { get; init; }
    public required string PartName { get; init; }
    // Engine identity of the part (localId, productId), for .macria decisions.
    public int PartLocalId { get; init; }
    public string? ProductId { get; init; }
    public int Quantity { get; init; }
    public string EngineClass { get; init; } = "";
    public IReadOnlyList<string> EngineReasons { get; init; } = Array.Empty<string>();
    /// <summary>Why the engine decided so (MotorSinifKodu) and whether it found any evidence.</summary>
    public string EngineCode { get; init; } = "";
    public bool EngineEvidence { get; init; }
    public bool SheetCandidate { get; init; }
    public string? ProfileCandidate { get; init; }
    /// <summary>
    /// The thickness the engine measured (offset skin pair), also when it
    /// could not unfold the part or did not take it for a sheet.
    /// </summary>
    public double? ThicknessMm { get; init; }
    /// <summary>The user's corrected thickness (e.g. the raw plate), or null: the engine value is used.</summary>
    public double? KullaniciKalinlikMm { get; private set; }
    /// <summary>The thickness Macria works with: the user's correction, otherwise the engine's.</summary>
    public double? EtkinKalinlikMm => KullaniciKalinlikMm ?? ThicknessMm;
    public bool KalinlikDuzeltildi => KullaniciKalinlikMm != null;
    /// <summary>Flat pattern size (width × height of the engine pattern), null without a flat pattern.</summary>
    public double? AcinimEnMm { get; init; }
    public double? AcinimBoyMm { get; init; }
    /// <summary>Smallest rectangle around the flat pattern at any angle (short × long side), null for old engines.</summary>
    public double? EnKucukDikdortgenEnMm { get; init; }
    public double? EnKucukDikdortgenBoyMm { get; init; }
    /// <summary>The engine recognized the sheet (its bends are counted), with or without a flat pattern.</summary>
    public bool SheetRecognized { get; init; }
    public int BendCount { get; init; }
    /// <summary>Machined plate: pockets, steps, counterbores; not cut in the DXF.</summary>
    public bool MachiningPresent { get; init; }
    /// <summary>The engine's machining kinds (Pocket, Engraving, Embossing); empty for old engines.</summary>
    public IReadOnlyList<string> MachiningKinds { get; init; } = Array.Empty<string>();
    public string HoleSummary { get; init; } = "—";
    /// <summary>Engine DXF (part-&lt;id&gt;.dxf in the analysis' DXF folder), or null.</summary>
    public string? DxfSourcePath { get; init; }
    /// <summary>Same pattern without bend information (part-&lt;id&gt;-kesim.dxf), or null.</summary>
    public string? DxfCutOnlySourcePath { get; init; }

    /// <summary>The engine DXF to use: with bend information or KESIM only.</summary>
    public string? DxfFor(bool bendInfo) => bendInfo ? DxfSourcePath : DxfCutOnlySourcePath;

    public string SourceFileName => System.IO.Path.GetFileName(SourceStepPath);
    /// <summary>
    /// A sheet, or what may become one: the Saclar categories, an engine sheet
    /// (with or without a flat pattern, or in conflict with a profile), or a
    /// thickness the user typed. Other parts (shaft, bolt, nut) show no sheet
    /// thickness or flat size; their Ölçü is the part's own size.
    /// </summary>
    public bool SacGibi => EffectiveCategory is MontajParcaKategorisi.Sac or MontajParcaKategorisi.OnayGerekli ||
                           EngineCode is MotorSinifKodu.Sheet or MotorSinifKodu.FlatPatternFailed or MotorSinifKodu.SheetProfileConflict ||
                           KalinlikDuzeltildi;
    /// <summary>The part's own size for a part that is no sheet: "Ø12 × 80", "40 × 40 × 100", or "—".</summary>
    public string GovdeOlcusu { get; init; } = "—";
    /// <summary>The sheet recognizer found a closed section (tube, box profile): no sheet, whatever its thickness.</summary>
    public bool KapaliKesit { get; init; }
    /// <summary>Profil / Sac olarak dene are closed to the part (DenemeKapisi): e.g. a shaft; null when open.</summary>
    public string? DenemeyeKapaliNedeni { get; init; }
    public string ThicknessDisplay => SacGibi && EtkinKalinlikMm is double t ? FormatNumber(t) + " mm" : "—";
    /// <summary>Editable thickness cell text (no unit).</summary>
    public string KalinlikMetni => EtkinKalinlikMm is double t ? FormatNumber(t) : "";
    public string MotorKalinlikDisplay => SacGibi && ThicknessMm is double t ? FormatNumber(t) + " mm" : "—";
    /// <summary>"En küçük çevreleyen dikdörtgen (en × boy)": short × long side, at any angle.</summary>
    public string EnKucukDikdortgenDisplay => SacGibi && EnKucukDikdortgenEnMm is double en && EnKucukDikdortgenBoyMm is double boy
        ? FormatNumber(Math.Round(en, 1)) + " × " + FormatNumber(Math.Round(boy, 1)) + " mm"
        : "—";
    /// <summary>"Açınım ölçüsü (en × boy)": the rectangle around the flat pattern.</summary>
    public string AcinimOlcusuDisplay => SacGibi && AcinimEnMm is double en && AcinimBoyMm is double boy
        ? FormatNumber(Math.Round(en, 1)) + " × " + FormatNumber(Math.Round(boy, 1)) + " mm"
        : "—";
    public string BendCountDisplay => SheetRecognized ? BendCount.ToString(CultureInfo.InvariantCulture) : "—";
    /// <summary>"var (gravür)", "var (cep)" ...; "—" without machining (IslemeMetni).</summary>
    public string MachiningDisplay => IslemeMetni.Kisa(MachiningPresent, MachiningKinds);
    public string IslemeGosterimi => MachiningDisplay;
    int? IAnalizSatiri.ParcaLocalId => PartLocalId;
    /// <summary>Built from a run on selected parts (Yeniden Analiz Et, Sac olarak dene).</summary>
    public SatirDenemesi? Deneme { get; private set; }

    /// <summary>
    /// The trial's reason when it found nothing: the row keeps the automatic
    /// result and only says what was tried and why it did not take (null when
    /// the row is the run's own result).
    /// </summary>
    public string? DenemeGerekcesi { get; private set; }

    /// <summary>`gerekce` null: this row is the run's result; otherwise the run found nothing and the row stays as it was.</summary>
    public void DenemeyiIsaretle(SatirDenemesi deneme, string? gerekce = null)
    {
        Deneme = deneme;
        DenemeGerekcesi = gerekce;
        foreach (string name in new[] { nameof(Deneme), nameof(KullaniciKarariMetni), nameof(DurumEtiketi), nameof(StatusDisplay),
                     nameof(MotorGerekcesi), nameof(DenemeGerekcesi) })
            Raise(name);
    }
    public string EngineReasonDisplay => EngineReasons.Count == 0 ? "" : string.Join(" ", EngineReasons);

    private double _laserMaximumMm = 20;
    // The engine's thickness carries float noise (20.000000000038 or
    // 19.99999999999933 for a 20 mm plate); a plate at the limit is Lazer.
    private const double LaserLimitToleranceMm = 0.001;
    /// <summary>Lazer or Şalama/Kütük: only for sheets (Saclar); "—" for any other part.</summary>
    public string GroupDisplay => EtkinKalinlikMm is null || EffectiveCategory is not (MontajParcaKategorisi.Sac or MontajParcaKategorisi.OnayGerekli)
        ? "—"
        : IsThickPlate ? "Şalama/Kütük" : "Lazer";

    /// <summary>
    /// Thicker than the laser maximum: listed under Şalama/Kütük. A sheet row
    /// without a thickness stays under Lazer, where its "—" group shows.
    /// </summary>
    public bool IsThickPlate => EtkinKalinlikMm is double t && t > _laserMaximumMm + LaserLimitToleranceMm;

    /// <summary>null: no CATIA comparison or no single match.</summary>
    public bool? CatiaSheetMetalFeature { get; private set; }
    public string CatiaMatchDisplay { get; private set; } = "CATIA taraması yok";
    public string CatiaQuantityDisplay { get; private set; } = "—";
    public string CatiaReferenceTitle { get; private set; } = "";

    public MontajParcaKategorisi AutomaticCategory { get; private set; } = MontajParcaKategorisi.KontrolGerekli;
    public MontajParcaKategorisi EffectiveCategory { get; private set; } = MontajParcaKategorisi.KontrolGerekli;
    public GeometryLabDecisionSource DecisionSource { get; private set; } = GeometryLabDecisionSource.Automatic;
    public bool HasUserDecision => DecisionSource == GeometryLabDecisionSource.User;
    /// <summary>Which user decision is in effect (MacriaProje.Karar*), or null.</summary>
    public string? KullaniciKarari => !HasUserDecision ? null
        : EffectiveCategory == MontajParcaKategorisi.Sac ? MacriaProje.KararSacOnayla : MacriaProje.KararKontrole;
    /// <summary>The engine unfolded the part: DXF Üret writes it. Approval does not need it.</summary>
    public bool CanApproveAsSheet => DxfSourcePath != null && ThicknessMm != null;
    public bool IsInSheetTab => !ListeDisi && EffectiveCategory is MontajParcaKategorisi.Sac or MontajParcaKategorisi.OnayGerekli;
    public bool IsInReviewTab => !ListeDisi && EffectiveCategory is MontajParcaKategorisi.KontrolGerekli or MontajParcaKategorisi.Diger;

    /// <summary>Taken out of every list by the user; Geri Al brings it back to its tab.</summary>
    public bool ListeDisi { get; private set; }
    public string ListeDisiNotu { get; private set; } = "";

    /// <summary>"DXF" column: the file DXF Üret writes, or why there is none.</summary>
    public string DxfDisplay => !SacGibi ? "—"
        : DxfSourcePath != null && EtkinKalinlikMm is double t ? DxfAdi.Uret(PartName, t, Quantity)
        : "açınım yok – CATIA'dan";

    public string StatusDisplay => (EffectiveCategory switch
    {
        MontajParcaKategorisi.Sac => "Sac",
        MontajParcaKategorisi.OnayGerekli => "Onay gerekli",
        MontajParcaKategorisi.Diger => "Diğer",
        MontajParcaKategorisi.Profil => "Profil",
        MontajParcaKategorisi.Tanimsiz => "Tanımsız",
        _ => "Kontrol gerekli"
    }) + (DenemeSonucuDegistirdi ? " (deneme)" : "");

    /// <summary>
    /// A part row shows "(deneme)" only when its trial found what it tried for
    /// (Sac olarak dene: a sheet). A trial that changed nothing says so in
    /// Kullanıcı kararı and the reason, not in the status.
    /// </summary>
    public bool DenemeSonucuDegistirdi => Deneme is { Mod: MacriaProje.DenemeSac } && DenemeGerekcesi is null && EngineCode == MotorSinifKodu.Sheet;

    public string DecisionDisplay => DecisionSource switch
    {
        GeometryLabDecisionSource.User => "Kullanıcı",
        GeometryLabDecisionSource.ThreeDScan => "CATIA sac unsuru",
        _ => "Otomatik"
    };

    public string ExplanationDisplay
    {
        get
        {
            if (HasUserDecision)
                return EffectiveCategory == MontajParcaKategorisi.Sac
                    ? (DxfSourcePath != null
                        ? "Kullanıcı tarafından sac olarak onaylandı."
                        : "Kullanıcı tarafından sac olarak onaylandı; motor açınım üretemedi, DXF CATIA'dan alınmalı.")
                    : "Kullanıcı kararıyla kontrol gerekliye alındı.";
            return EffectiveCategory switch
            {
                MontajParcaKategorisi.Sac when EngineClass == "ReviewRequired" =>
                    "Sac/profil çakışması CATIA sac unsuruyla çözüldü.",
                MontajParcaKategorisi.Sac => "Motor sac buldu; CATIA sac unsuru doğruluyor.",
                MontajParcaKategorisi.OnayGerekli when CatiaSheetMetalFeature == false =>
                    "CATIA'da sac unsuru yok; motor geometrik sac buldu.",
                MontajParcaKategorisi.OnayGerekli => "CATIA ile doğrulanmadı; motor geometrik sac buldu.",
                _ => MotorGerekcesi
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Row for one part; engine evidence comes from the same analysis.</summary>
    public static MontajParcaSatiri Olustur(string stepPath, GeometryLabAnalysisTransport analysis,
        GeometryLabPartTransport part, string? dxfSourcePath, double laserMaximumMm, string? dxfCutOnlySourcePath = null)
    {
        int? solidId = part.SolidIds.Count == 1 ? part.SolidIds[0].LocalId : null;
        GeometryLabSheetMetalTransport? sheet = solidId is null
            ? null
            : analysis.SheetMetalAnalyses.FirstOrDefault(x => x.SolidId?.LocalId == solidId);
        bool recognized = sheet?.Status == "Recognized";
        GeometryLabFlatPatternTransport? acinim = sheet?.FlatPattern?.Status == "Succeeded" ? sheet.FlatPattern : null;
        (string kod, bool kanit) = MotorSinifKodu.Belirle(analysis, part);
        var row = new MontajParcaSatiri
        {
            EngineCode = kod,
            EngineEvidence = kanit,
            SourceStepPath = stepPath,
            PartName = string.IsNullOrWhiteSpace(part.Name) ? "Parça " + part.LocalId : part.Name!,
            PartLocalId = part.LocalId,
            ProductId = string.IsNullOrWhiteSpace(part.ProductId) ? null : part.ProductId,
            Quantity = part.Quantity,
            EngineClass = part.Classification ?? "",
            EngineReasons = part.ClassificationReasons,
            SheetCandidate = part.SheetCandidate,
            ProfileCandidate = string.IsNullOrWhiteSpace(part.ProfileCandidate) ? null : part.ProfileCandidate,
            ThicknessMm = sheet?.ThicknessMm,
            SheetRecognized = recognized,
            BendCount = recognized ? sheet!.Bends.Count : 0,
            MachiningPresent = part.MachiningPresent == true,
            MachiningKinds = part.MachiningKinds,
            AcinimEnMm = acinim?.WidthMm,
            AcinimBoyMm = acinim?.HeightMm,
            EnKucukDikdortgenEnMm = acinim?.MinimumRectangle?.ShortMm,
            EnKucukDikdortgenBoyMm = acinim?.MinimumRectangle?.LongMm,
            HoleSummary = solidId is null
                ? "—"
                : HoleSummaryFor(analysis.HoleFeatures.Where(x => x.SolidId?.LocalId == solidId)),
            DxfSourcePath = part.SheetCandidate ? dxfSourcePath : null,
            DxfCutOnlySourcePath = part.SheetCandidate ? dxfCutOnlySourcePath : null,
            GovdeOlcusu = GovdeOlcusuHesapla(analysis, solidId, sheet),
            KapaliKesit = sheet?.ClosedSection == true,
            DenemeyeKapaliNedeni = solidId is null ? null : DenemeKapisi.Neden(analysis.ProfileRecognitions
                .FirstOrDefault(x => x.SolidId?.LocalId == solidId && x.SectionRecognitionStatus == "Recognized")?.ProfileType)
        };
        row._laserMaximumMm = laserMaximumMm;
        row.Recalculate();
        return row;
    }

    /// <summary>
    /// The size of a part that is no sheet. A solid bar the profile recognizer
    /// measured: "Ø20 × 204" or "40 × 40 × 100" (section × length). Otherwise
    /// the sheet recognizer's box: its "thickness" is the third side and a
    /// single-circle outline a diameter ("Ø12,5 × 44"). Nothing measured: "—".
    /// </summary>
    private static string GovdeOlcusuHesapla(GeometryLabAnalysisTransport analysis, int? solidId,
        GeometryLabSheetMetalTransport? sheet)
    {
        static string N(double mm) => FormatNumber(Math.Round(mm, 1));
        if (solidId is null) return "—";
        GeometryLabProfileRecognitionTransport? profil = analysis.ProfileRecognitions.FirstOrDefault(x => x.SolidId?.LocalId == solidId);
        // The length: the profile's measured length, otherwise the span of the
        // solid's only reliable axis (end to end; a shaft whose ends the
        // length stage could not prove still has it).
        var eksenler = analysis.ProfileGeometryAnalysis?.AxisCandidates
            .Where(x => x.SolidId?.LocalId == solidId && x.Reliable && x.ProjectionSpanMm > 0).ToList();
        double? boy = profil?.LengthSummary?.UniformLengthMm ?? profil?.LengthSummary?.LongLengthMm
                      ?? (eksenler is { Count: 1 } ? eksenler[0].ProjectionSpanMm : null);
        if (profil?.SectionRecognitionStatus == "Recognized")
        {
            if (profil.ProfileType == "SolidCircularBar" && profil.OuterDiameterMm is double d)
                return "Ø" + N(d) + (boy is double l ? " × " + N(l) : "") + " mm";
            if (profil.ProfileType is "SolidSquareBar" or "SolidRectangularBar" && profil.OuterWidthMm is double w && profil.OuterHeightMm is double h)
                return N(w) + " × " + N(h) + (boy is double l ? " × " + N(l) : "") + " mm";
        }
        GeometryLabFlatPatternTransport? acinim = sheet?.FlatPattern?.Status == "Succeeded" ? sheet.FlatPattern : null;
        if (acinim?.WidthMm is double en && acinim.HeightMm is double genislik && sheet!.ThicknessMm is double t)
            return acinim.Segments.Count == 1 && acinim.Segments[0].Type == "Circle"
                ? "Ø" + N(en) + " × " + N(t) + " mm"
                : N(en) + " × " + N(genislik) + " × " + N(t) + " mm";
        return "—";
    }

    /// <summary>
    /// The user's thickness (null or the engine value: back to the engine's).
    /// It names the DXF, picks Lazer or Şalama/Kütük and is saved in the project.
    /// </summary>
    public void KalinligiDuzelt(double? kalinlikMm)
    {
        KullaniciKalinlikMm = kalinlikMm is double k && !(ThicknessMm is double motor && Math.Abs(k - motor) < KalinlikToleransiMm)
            ? k
            : null;
        RaiseAll();
    }

    private const double KalinlikToleransiMm = 0.0001;

    /// <summary>
    /// A thickness typed into the cell: "16", "16,5", "16.5" or "16 mm"
    /// (0,05–1000 mm). Empty text means "back to the engine value" (null).
    /// False for anything else.
    /// </summary>
    public static bool KalinlikGirdisiniOku(string? metin, out double? kalinlikMm)
    {
        kalinlikMm = null;
        string temiz = (metin ?? "").Trim();
        if (temiz.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) temiz = temiz[..^2].Trim();
        if (temiz.Length == 0) return true;
        if (!double.TryParse(temiz.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double deger) ||
            double.IsNaN(deger) || deger < 0.05 || deger > 1000)
            return false;
        kalinlikMm = deger;
        return true;
    }

    private readonly List<string> _yazilanDxfYollari = new();
    /// <summary>DXFs DXF Üret wrote (or found) for this row; renamed when the thickness changes. Saved in the project.</summary>
    public IReadOnlyList<string> YazilanDxfYollari => _yazilanDxfYollari;
    /// <summary>The DXF file name for the current thickness, or null without a thickness.</summary>
    public string? DxfDosyaAdi => EtkinKalinlikMm is double t ? DxfAdi.Uret(PartName, t, Quantity) : null;

    public void DxfYazildi(string yol)
    {
        if (!_yazilanDxfYollari.Contains(yol, StringComparer.OrdinalIgnoreCase)) _yazilanDxfYollari.Add(yol);
    }

    /// <summary>A remembered DXF moved (yeni) or is gone (null).</summary>
    public void DxfYolunuDegistir(string eski, string? yeni)
    {
        _yazilanDxfYollari.RemoveAll(x => string.Equals(x, eski, StringComparison.OrdinalIgnoreCase));
        if (yeni != null) DxfYazildi(yeni);
    }

    public void SetLaserMaximum(double laserMaximumMm)
    {
        _laserMaximumMm = laserMaximumMm;
        Raise(nameof(GroupDisplay));
        Raise(nameof(IsThickPlate));
    }

    public void ApplyCatiaComparison(IReadOnlyList<CatiaScanSnapshotItem> matches)
    {
        CatiaReferenceTitle = "";
        CatiaQuantityDisplay = "—";
        CatiaSheetMetalFeature = null;
        if (matches.Count == 0)
            CatiaMatchDisplay = "Eşleşmedi";
        else if (matches.Count != 1)
            CatiaMatchDisplay = "Eşleme belirsiz";
        else
        {
            CatiaScanSnapshotItem match = matches[0];
            CatiaReferenceTitle = match.ReferenceTitle;
            CatiaQuantityDisplay = match.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "—";
            CatiaSheetMetalFeature = match.SheetMetalConfirmed;
            CatiaMatchDisplay = match.Quantity is int catiaQuantity && catiaQuantity != Quantity
                ? "Eşleşti (adet farklı: CATIA " + catiaQuantity + ")"
                : "Eşleşti";
        }
        Recalculate();
    }

    /// <summary>Saclar, with or without an engine flat pattern ("açınım yok – CATIA'dan").</summary>
    public void ApproveAsSheet()
    {
        DecisionSource = GeometryLabDecisionSource.User;
        EffectiveCategory = MontajParcaKategorisi.Sac;
        RaiseAll();
    }

    public void MoveToReview()
    {
        DecisionSource = GeometryLabDecisionSource.User;
        EffectiveCategory = MontajParcaKategorisi.KontrolGerekli;
        RaiseAll();
    }

    public void RestoreAutomaticDecision()
    {
        DecisionSource = GeometryLabDecisionSource.Automatic;
        Recalculate();
    }

    public void ListeDisinaCikar(string? not = null)
    {
        ListeDisi = true;
        ListeDisiNotu = not?.Trim() ?? "";
        RaiseAll();
    }

    public void ListeyeGeriAl()
    {
        ListeDisi = false;
        ListeDisiNotu = "";
        RaiseAll();
    }

    private void Recalculate()
    {
        bool catiaSheet = CatiaSheetMetalFeature == true;
        AutomaticCategory = EngineClass switch
        {
            "Sheet" when DxfSourcePath != null || ThicknessMm != null =>
                catiaSheet ? MontajParcaKategorisi.Sac : MontajParcaKategorisi.OnayGerekli,
            "ReviewRequired" when SheetCandidate && ProfileCandidate != null && catiaSheet && ThicknessMm != null =>
                MontajParcaKategorisi.Sac,
            "Profile" => MontajParcaKategorisi.Profil,
            _ when SekmeKurallari.Diger(EngineCode) => MontajParcaKategorisi.Diger,
            _ when SekmeKurallari.OtomatikSekme(EngineCode, EngineEvidence) == AnalizSekmesi.Tanimsiz =>
                MontajParcaKategorisi.Tanimsiz,
            _ => MontajParcaKategorisi.KontrolGerekli
        };
        if (!HasUserDecision)
        {
            EffectiveCategory = AutomaticCategory;
            DecisionSource = AutomaticCategory == MontajParcaKategorisi.Sac && catiaSheet
                ? GeometryLabDecisionSource.ThreeDScan
                : GeometryLabDecisionSource.Automatic;
        }
        RaiseAll();
    }

    /// <summary>"2× Ø10 havşa (Ø20), 1× M8x1.25 dişli (DXF'te yok)"; "—" without holes.</summary>
    public static string HoleSummaryFor(IEnumerable<GeometryLabHoleFeatureTransport> holes)
    {
        var parts = holes
            .Select(hole => Describe(hole))
            .GroupBy(text => text)
            .Select(group => group.Count() + "× " + group.Key)
            .ToList();
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    private static string Describe(GeometryLabHoleFeatureTransport hole)
    {
        string through = hole.ThroughDiameterMm is double d ? "Ø" + FormatNumber(d) : "Ø?";
        string head = hole.HeadDiameterMm is double h ? " (Ø" + FormatNumber(h) + ")" : "";
        if (!string.IsNullOrWhiteSpace(hole.ThreadDesignation))
            return hole.ThreadDesignation + " dişli (DXF'te yok)";
        return hole.Type switch
        {
            "Countersink" => through + " havşa" + head,
            "Counterbore" => through + " imbus" + head,
            "Through" => through + " düz",
            _ => through + " belirsiz"
        };
    }

    public static string FormatNumber(double value) => value.ToString("0.###", Tr);

    private void RaiseAll()
    {
        foreach (string name in new[]
        {
            nameof(EffectiveCategory), nameof(AutomaticCategory), nameof(DecisionSource), nameof(HasUserDecision),
            nameof(StatusDisplay), nameof(DecisionDisplay), nameof(ExplanationDisplay), nameof(IsInSheetTab),
            nameof(IsInReviewTab), nameof(CatiaMatchDisplay), nameof(CatiaQuantityDisplay), nameof(CatiaReferenceTitle),
            nameof(CatiaSheetMetalFeature), nameof(GroupDisplay), nameof(ListeDisi), nameof(Sekme),
            nameof(DurumEtiketi), nameof(KararGosterimi), nameof(AciklamaGosterimi), nameof(IsThickPlate),
            nameof(KullaniciKalinlikMm), nameof(EtkinKalinlikMm), nameof(KalinlikDuzeltildi), nameof(ThicknessDisplay),
            nameof(KalinlikMetni), nameof(DxfDisplay), nameof(OlcuGosterimiMetni), nameof(KullaniciKarariMetni),
            nameof(SacGibi), nameof(MotorKalinlikDisplay), nameof(AcinimOlcusuDisplay), nameof(EnKucukDikdortgenDisplay)
        })
            Raise(name);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // IAnalizSatiri: common columns of the mixed tabs and the common toolbar.
    public AnalizSekmesi Sekme => ListeDisi ? AnalizSekmesi.ListeDisi : EffectiveCategory switch
    {
        MontajParcaKategorisi.Sac or MontajParcaKategorisi.OnayGerekli => AnalizSekmesi.Saclar,
        MontajParcaKategorisi.Profil => AnalizSekmesi.Profiller,
        MontajParcaKategorisi.Tanimsiz => AnalizSekmesi.Tanimsiz,
        _ => AnalizSekmesi.KontrolGerekli
    };
    bool IAnalizSatiri.ProfilSatiri => false;
    string IAnalizSatiri.KaynakYolu => SourceStepPath;
    string? IAnalizSatiri.ParcaAdi => PartName;
    string IAnalizSatiri.ParcaGosterimi => PartName;
    string IAnalizSatiri.AdetGosterimi => Quantity.ToString(CultureInfo.InvariantCulture);
    string IAnalizSatiri.TurGosterimi => EffectiveCategory switch
    {
        MontajParcaKategorisi.Sac or MontajParcaKategorisi.OnayGerekli => "Sac",
        MontajParcaKategorisi.Diger => "Diğer",
        _ => ProfileCandidate != null ? "Sac / profil"
            : KapaliKesit && !SheetCandidate ? "Kapalı kesit"
            : SheetCandidate || ThicknessMm != null ? "Sac?" : "—"
    };
    string IAnalizSatiri.OlcuGosterimi => OlcuGosterimiMetni;
    public string OlcuGosterimiMetni => !SacGibi ? GovdeOlcusu
        : EtkinKalinlikMm is double t
            ? "t = " + FormatNumber(t) + " mm" + (KalinlikDuzeltildi && ThicknessMm is double m ? " (tespit edilen " + FormatNumber(m) + ")" : "")
            : "—";
    public string DurumEtiketi => StatusDisplay;
    public string KararGosterimi => DecisionDisplay;
    /// <summary>The engine's reason, result first and short (MotorGerekcesiMetni); its own text is in "Teknik ayrıntı".</summary>
    public string MotorGerekcesi => DenemeGerekcesi ?? MotorGerekcesiMetni.Kisa(EngineCode, EngineReasons);
    public string KullaniciKarariMetni
    {
        get
        {
            var parcalar = new List<string>();
            if (Deneme != null) parcalar.Add(Deneme.Metin);
            if (HasUserDecision)
                parcalar.Add(EffectiveCategory == MontajParcaKategorisi.Sac
                    ? "Sac olarak onaylandı" + (DxfSourcePath == null ? " (açınım yok, DXF CATIA'dan)" : "")
                    : "Kontrol gerekliye alındı");
            if (KullaniciKalinlikMm is double k)
                parcalar.Add("Ham sac kalınlığı " + FormatNumber(k) + " mm" + (ThicknessMm is double m ? " (tespit edilen " + FormatNumber(m) + " mm)" : ""));
            if (ListeDisi)
                parcalar.Add("Liste dışı" + (ListeDisiNotu.Length > 0 ? ": " + ListeDisiNotu : ""));
            return parcalar.Count == 0 ? "—" : string.Join("; ", parcalar);
        }
    }
    public string AciklamaGosterimi => ListeDisi && ListeDisiNotu.Length > 0
        ? ExplanationDisplay + " (Liste dışı: " + ListeDisiNotu + ")"
        : ExplanationDisplay;
    public bool OnayBekliyor => EffectiveCategory == MontajParcaKategorisi.OnayGerekli;
    IReadOnlyList<KeyValuePair<string, string>> IAnalizSatiri.Ayrintilar => new KeyValuePair<string, string>[]
    {
        new("STEP dosyası", SourceFileName),
        new("Parça", PartName + " (" + Quantity.ToString(CultureInfo.InvariantCulture) + " adet)"),
        new("Durum", StatusDisplay),
        new("Ölçü", OlcuGosterimiMetni),
        new("Ham sac kalınlığı", ThicknessDisplay + (KalinlikDuzeltildi ? " (kullanıcı düzeltti)" : "")),
        new("Tespit edilen kalınlık", MotorKalinlikDisplay),
        new("Açınım ölçüsü (en × boy)", AcinimOlcusuDisplay),
        new("En küçük çevreleyen dikdörtgen", EnKucukDikdortgenDisplay),
        new("Grup", GroupDisplay),
        new("Büküm", BendCountDisplay),
        new("Delikler", HoleSummary),
        new("İşleme", IslemeMetni.Ayrinti(MachiningPresent, MachiningKinds,
            sac: EffectiveCategory is MontajParcaKategorisi.Sac or MontajParcaKategorisi.OnayGerekli)),
        new("DXF", DxfDisplay),
        new("CATIA adedi", CatiaQuantityDisplay),
        new("Eşleşme", CatiaMatchDisplay),
        new("Karar", DecisionDisplay),
        new("Motor gerekçesi", MotorGerekcesi),
        new(TeknikAyrinti.Anahtar, MotorGerekcesiMetni.Teknik(EngineReasons)),
        new("Kullanıcı kararı", KullaniciKarariMetni)
    };
    void IAnalizSatiri.KontrolGerekliyeAl() => MoveToReview();

    /// <summary>True when a STEP holds more than one part or repeats its only part.</summary>
    public static bool IsAssembly(GeometryLabAnalysisTransport? analysis) =>
        analysis != null && (analysis.Parts.Count > 1 || (analysis.Parts.Count == 1 && analysis.Parts[0].Quantity > 1));

    /// <summary>
    /// The analysis narrowed to one part's solid, so the existing profile row
    /// (GeometryLabStepProfileListItem.Apply) judges it like a single-part STEP.
    /// The part's own base stock replaces the whole-shape one (a processed
    /// profile keeps its fallback); whole-shape axis candidates are left out.
    /// </summary>
    public static GeometryLabProcessAdapterResult ResultForPart(GeometryLabProcessAdapterResult result,
        GeometryLabPartTransport part)
    {
        if (result.Analysis is null) return result;
        var solidIds = part.SolidIds.Select(x => x.LocalId).ToHashSet();
        GeometryLabProfileRecognitionTransport[] profiles = result.Analysis.ProfileRecognitions
            .Where(x => x.SolidId != null && solidIds.Contains(x.SolidId.LocalId)).ToArray();
        GeometryLabSolidBaseStockTransport? stock = part.SolidIds.Count == 1
            ? result.Analysis.BaseStockProfiles.FirstOrDefault(x => x.SolidId?.LocalId == part.SolidIds[0].LocalId)
            : null;
        return result with
        {
            Analysis = result.Analysis with
            {
                ProfileRecognitions = profiles,
                ProfileRecognition = profiles.Length == 1 ? profiles[0] : null,
                Solids = result.Analysis.Solids.Take(part.SolidIds.Count).ToArray(),
                BaseStockProfile = stock?.BaseStockProfile,
                ModificationAnalysis = stock?.ModificationAnalysis,
                BaseStockProfiles = stock is null ? Array.Empty<GeometryLabSolidBaseStockTransport>() : new[] { stock },
                ProfileGeometryAnalysis = GeometryForPart(result.Analysis.ProfileGeometryAnalysis, solidIds),
                Parts = new[] { part }
            },
            HasMultipleProfileResults = profiles.Length > 1
        };
    }

    /// <summary>
    /// The profile axis analysis of one part's own solids. The assembly-wide
    /// status is "Unknown" as soon as any other part has no reliable axis, so
    /// it is recomputed with the engine's per-solid rule: every solid has
    /// exactly one reliable axis → Determined; one with several → Ambiguous;
    /// otherwise Unknown. Candidates without a solid (old engines) give null,
    /// as before, so the length stays "—" rather than being guessed.
    /// </summary>
    internal static GeometryLabProfileGeometryAnalysisTransport? GeometryForPart(
        GeometryLabProfileGeometryAnalysisTransport? geometry, IReadOnlySet<int> solidIds)
    {
        if (geometry is null || solidIds.Count == 0) return null;
        GeometryLabProfileAxisCandidateTransport[] candidates = geometry.AxisCandidates
            .Where(x => x.SolidId != null && solidIds.Contains(x.SolidId.LocalId)).ToArray();
        if (candidates.Length == 0) return null;
        int[] reliablePerSolid = solidIds
            .Select(id => candidates.Count(x => x.SolidId!.LocalId == id && x.Reliable)).ToArray();
        string status = reliablePerSolid.Any(count => count > 1) ? "Ambiguous"
            : reliablePerSolid.Any(count => count == 0) ? "Unknown"
            : "Determined";
        return geometry with { AxisDetectionStatus = status, AxisCandidates = candidates };
    }
}
