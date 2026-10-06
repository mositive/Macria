using System.Collections.Generic;
using System.Linq;

namespace Macria;

/// <summary>Result tabs of Dosya Analiz Merkezi → STEP / STP Analizi.</summary>
public enum AnalizSekmesi
{
    Profiller,
    Saclar,
    KontrolGerekli,
    Tanimsiz,
    ListeDisi
}

/// <summary>
/// What every result row offers, profile row or assembly part row alike: the
/// mixed tabs (Kontrol gerekli, Tanımsız, Liste dışı) list both kinds, and the
/// common toolbar acts on either.
/// </summary>
public interface IAnalizSatiri
{
    AnalizSekmesi Sekme { get; }
    /// <summary>True for a profile row (GeometryLabStepProfileListItem), false for a part row.</summary>
    bool ProfilSatiri { get; }
    string KaynakYolu { get; }
    string? ParcaAdi { get; }
    string ParcaGosterimi { get; }
    string AdetGosterimi { get; }
    string TurGosterimi { get; }
    string OlcuGosterimi { get; }
    /// <summary>The engine's machining features: "—", "var (gravür)", "var (cep)" ... (IslemeMetni).</summary>
    string IslemeGosterimi { get; }
    string DurumEtiketi { get; }
    string KararGosterimi { get; }
    string AciklamaGosterimi { get; }
    /// <summary>Why the engine (or its automatic rules) put the row where it is; a user decision does not change it.</summary>
    string MotorGerekcesi { get; }
    /// <summary>The user's decisions on the row (category, thickness, Liste dışı, notes), or "—".</summary>
    string KullaniciKarariMetni { get; }
    string CatiaQuantityDisplay { get; }
    string CatiaMatchDisplay { get; }
    bool HasUserDecision { get; }
    bool ListeDisi { get; }
    /// <summary>A geometric sheet in Saclar that is not approved yet ("Onay gerekli", shown amber).</summary>
    bool OnayBekliyor { get; }
    /// <summary>"Seçili Parça" lines of the right panel (label, value).</summary>
    IReadOnlyList<KeyValuePair<string, string>> Ayrintilar { get; }
    /// <summary>The part's localId in its STEP's engine analysis; null for a file row (no engine parts).</summary>
    int? ParcaLocalId { get; }
    /// <summary>The row comes from a run on selected parts (Yeniden Analiz Et, Profil / Sac olarak dene), or null.</summary>
    SatirDenemesi? Deneme { get; }
    void ListeDisinaCikar(string? not = null);
    void ListeyeGeriAl();
    void KontrolGerekliyeAl();
    void RestoreAutomaticDecision();
}

/// <summary>"Seçili Parça" line shown folded: the engine's own reason text (face ids, inner steps).</summary>
public static class TeknikAyrinti
{
    public const string Anahtar = "Teknik ayrıntı";
}

/// <summary>
/// A row built from an engine run on selected parts (docs/YENIDEN_ANALIZ_VE_DENEME_PLANI.md):
/// the run's mode (MacriaProje.Deneme*) and its id among the source's EkAnalizler.
/// </summary>
public sealed record SatirDenemesi(string Mod, string EkId)
{
    /// <summary>"Kullanıcı kararı" text of the row.</summary>
    public string Metin => Mod switch
    {
        MacriaProje.DenemeProfil => "Profil olarak denendi",
        MacriaProje.DenemeSac => "Sac olarak denendi",
        _ => "Yeniden analiz edildi (süre sınırı yok)"
    };

    /// <summary>Shown after the status of a row a trial recognized ("Profil (deneme)").</summary>
    public bool Deneme => Mod is MacriaProje.DenemeProfil or MacriaProje.DenemeSac;
}

/// <summary>Where the engine's classification puts a part (docs/SEKME_VE_ARAC_CUBUGU_PLANI.md).</summary>
public static class SekmeKurallari
{
    /// <summary>
    /// Found something but not sure → Kontrol gerekli; found nothing or could
    /// not look (invalid geometry, timed out, no solid) → Tanımsız.
    /// </summary>
    public static AnalizSekmesi OtomatikSekme(string kod, bool kanit) => kod switch
    {
        MotorSinifKodu.Sheet => AnalizSekmesi.Saclar,
        MotorSinifKodu.HollowProfile or MotorSinifKodu.ProcessedProfile => AnalizSekmesi.Profiller,
        MotorSinifKodu.InvalidGeometry or MotorSinifKodu.TimedOut or MotorSinifKodu.NoSolid => AnalizSekmesi.Tanimsiz,
        MotorSinifKodu.UnsupportedFaces or MotorSinifKodu.SheetAnalysisIncomplete or MotorSinifKodu.NotRecognized =>
            kanit ? AnalizSekmesi.KontrolGerekli : AnalizSekmesi.Tanimsiz,
        // Conflicts, missing flat pattern, multi-solid, solid bars, too thick
        // for a plate, and anything unknown: someone has to look at it.
        _ => AnalizSekmesi.KontrolGerekli
    };

    /// <summary>
    /// The tab search: the text (case and Turkish letters ignored) in the part
    /// name, the part column ("file › part") or the STEP file name. Empty matches all.
    /// </summary>
    public static bool AramayaUyar(IAnalizSatiri satir, string? arama)
    {
        string aranan = (arama ?? "").Trim();
        if (aranan.Length == 0) return true;
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
        const System.Globalization.CompareOptions secenek = System.Globalization.CompareOptions.IgnoreCase;
        bool Icerir(string? metin) => !string.IsNullOrEmpty(metin) && tr.IndexOf(metin, aranan, secenek) >= 0;
        return Icerir(satir.ParcaAdi) || Icerir(satir.ParcaGosterimi) || Icerir(System.IO.Path.GetFileName(satir.KaynakYolu));
    }

    /// <summary>A part the engine identified as neither sheet nor profile (shown as "Diğer").</summary>
    public static bool Diger(string kod) =>
        kod is MotorSinifKodu.SolidBar or MotorSinifKodu.ThickerThanOutline or MotorSinifKodu.ThickerThanMaterial;
}

/// <summary>Visibility and enabled state of the common toolbar buttons.</summary>
public sealed record AnalizAracDurumu
{
    public bool DxfUretGorunur { get; init; }
    public bool DxfUretEtkin { get; init; }
    public bool ProfilOnaylaGorunur { get; init; }
    public bool ProfilOnaylaEtkin { get; init; }
    public bool SacOnaylaGorunur { get; init; }
    public bool SacOnaylaEtkin { get; init; }
    public bool KontroleGorunur { get; init; }
    public bool KontroleEtkin { get; init; }
    public bool ListeDisiGorunur { get; init; }
    public bool ListeDisiEtkin { get; init; }
    public bool OtomatikGorunur { get; init; }
    public bool OtomatikEtkin { get; init; }
    public bool GeriAlGorunur { get; init; }
    public bool GeriAlEtkin { get; init; }
    public bool YenidenAnalizGorunur { get; init; }
    public bool YenidenAnalizEtkin { get; init; }
    public bool DosyayiAcEtkin { get; init; }
    public bool ExcelEtkin { get; init; }

    /// <summary>
    /// `secili`: the selected rows of the open tab; `sekmedeSatirVar`: the tab
    /// lists anything; `onayliSacVar`: the open Saclar sub-tab holds approved
    /// sheets; `motorMesgul`: an engine run is going on.
    /// </summary>
    public static AnalizAracDurumu Hesapla(AnalizSekmesi sekme, IReadOnlyCollection<IAnalizSatiri> secili,
        bool sekmedeSatirVar, bool onayliSacVar, bool motorMesgul = false)
    {
        bool var = secili.Count > 0;
        bool listeDisi = sekme == AnalizSekmesi.ListeDisi;
        bool sacOnaylaGorunur = sekme is AnalizSekmesi.Saclar or AnalizSekmesi.KontrolGerekli or AnalizSekmesi.Tanimsiz;
        return new AnalizAracDurumu
        {
            ExcelEtkin = sekmedeSatirVar,
            DosyayiAcEtkin = secili.Count == 1,
            DxfUretGorunur = sekme == AnalizSekmesi.Saclar,
            DxfUretEtkin = onayliSacVar,
            ProfilOnaylaGorunur = sekme is AnalizSekmesi.KontrolGerekli or AnalizSekmesi.Tanimsiz,
            ProfilOnaylaEtkin = secili.Any(x => x.ProfilSatiri),
            SacOnaylaGorunur = sacOnaylaGorunur,
            SacOnaylaEtkin = sekme == AnalizSekmesi.Saclar
                ? secili.Any(x => !x.ProfilSatiri && x.OnayBekliyor)
                : secili.Any(x => !x.ProfilSatiri),
            KontroleGorunur = sekme is AnalizSekmesi.Profiller or AnalizSekmesi.Saclar or AnalizSekmesi.Tanimsiz,
            KontroleEtkin = var,
            ListeDisiGorunur = !listeDisi,
            ListeDisiEtkin = var,
            OtomatikGorunur = !listeDisi,
            OtomatikEtkin = secili.Any(x => x.HasUserDecision || x.Deneme != null),
            GeriAlGorunur = listeDisi,
            GeriAlEtkin = var,
            // Parts of an analysed STEP; a file row has no part to select.
            YenidenAnalizGorunur = sekme is AnalizSekmesi.KontrolGerekli or AnalizSekmesi.Tanimsiz,
            YenidenAnalizEtkin = !motorMesgul && secili.Any(x => x.ParcaLocalId != null)
        };
    }
}
