using System;
using System.Collections.Generic;
using System.Linq;

namespace Macria;

/// <summary>
/// Rows of Dosya Analiz Merkezi from an engine result, and the user decisions
/// on them for a .macria project. No WPF: the live analysis, project opening
/// and the tests all build rows through here.
/// </summary>
public static class MacriaProjeSatirlari
{
    /// <summary>
    /// Rows for one analysed STEP. Every STEP with engine parts (schema 1.2),
    /// a single-part file too, gives one Profiller row per profile part and a
    /// part row for each other part (Saclar, Kontrol gerekli, Tanımsız). A
    /// failed analysis or an older schema without parts stays one file row.
    /// </summary>
    public static (List<GeometryLabStepProfileListItem> Profil, List<MontajParcaSatiri> Montaj) Kur(
        string stepPath, GeometryLabProcessAdapterResult result, double laserMaximumMm)
    {
        if (ParcaYolundan(result))
            return MontajSatirlari(stepPath, result, laserMaximumMm);
        var row = new GeometryLabStepProfileListItem { SourceStepPath = stepPath };
        row.Apply(result);
        return (new List<GeometryLabStepProfileListItem> { row }, new List<MontajParcaSatiri>());
    }

    /// <summary>The result is listed by its engine parts (see Kur).</summary>
    public static bool ParcaYolundan(GeometryLabProcessAdapterResult result) =>
        result.IsSuccess && result.Analysis?.Parts.Count > 0;

    public static (List<GeometryLabStepProfileListItem> Profil, List<MontajParcaSatiri> Montaj) MontajSatirlari(
        string stepPath, GeometryLabProcessAdapterResult result, double laserMaximumMm)
    {
        GeometryLabAnalysisTransport analysis = result.Analysis!;
        var profil = new List<GeometryLabStepProfileListItem>();
        var montaj = new List<MontajParcaSatiri>();
        foreach (GeometryLabPartTransport part in analysis.Parts)
        {
            var row = MontajParcaSatiri.Olustur(stepPath, analysis, part, result.PartDxfPath(part),
                laserMaximumMm, result.PartDxfPath(part, cutOnly: true));
            if (row.EffectiveCategory == MontajParcaKategorisi.Profil)
            {
                var profileRow = new GeometryLabStepProfileListItem
                {
                    SourceStepPath = stepPath,
                    PartName = row.PartName,
                    PartQuantity = part.Quantity,
                    PartLocalId = part.LocalId,
                    PartProductId = row.ProductId,
                    PartMachiningPresent = part.MachiningPresent == true,
                    PartMachiningKinds = part.MachiningKinds,
                    PartEngineReasons = part.ClassificationReasons
                };
                profileRow.Apply(MontajParcaSatiri.ResultForPart(result, part));
                profil.Add(profileRow);
            }
            else
            {
                montaj.Add(row);
            }
        }
        return (profil, montaj);
    }

    /// <summary>
    /// Rows of the parts a run on selected parts analysed (`ek` holds only
    /// them), marked with the run: they replace those parts' rows.
    /// </summary>
    public static (List<GeometryLabStepProfileListItem> Profil, List<MontajParcaSatiri> Montaj) EkSatirlari(
        string stepPath, GeometryLabProcessAdapterResult ek, double laserMaximumMm, SatirDenemesi deneme)
    {
        if (!ParcaYolundan(ek)) return (new(), new());
        var (profil, montaj) = MontajSatirlari(stepPath, ek, laserMaximumMm);
        foreach (GeometryLabStepProfileListItem row in profil) row.DenemeyiIsaretle(deneme);
        foreach (MontajParcaSatiri row in montaj)
        {
            row.DenemeyiIsaretle(deneme);
            // The user asked for a sheet and the engine found one: it is approved
            // (with or without a flat pattern), marked "(deneme)".
            if (deneme.Mod == MacriaProje.DenemeSac && row.EngineCode == MotorSinifKodu.Sheet &&
                row.EffectiveCategory == MontajParcaKategorisi.OnayGerekli)
                row.ApproveAsSheet();
        }
        return (profil, montaj);
    }

    /// <summary>
    /// The run found what it was asked for: Profil olarak dene a profile, Sac
    /// olarak dene a sheet. Yeniden Analiz Et always "finds" (its result is the
    /// automatic one, recomputed). A trial that found nothing leaves the
    /// automatic row as it is (docs/YENIDEN_ANALIZ_VE_DENEME_PLANI.md).
    /// </summary>
    public static bool DenemeBuldu(GeometryLabProcessAdapterResult ek, int localId, string mod)
    {
        if (mod is not (MacriaProje.DenemeProfil or MacriaProje.DenemeSac)) return true;
        GeometryLabPartTransport? parca = ek.Analysis?.Parts.FirstOrDefault(x => x.LocalId == localId);
        return parca?.Classification == (mod == MacriaProje.DenemeProfil ? "Profile" : "Sheet");
    }

    /// <summary>The run's short reason for a part (MotorGerekcesiMetni), or null when the part is not in it.</summary>
    public static string? DenemeGerekcesi(GeometryLabProcessAdapterResult ek, int localId)
    {
        if (ek.Analysis is not { } analiz || analiz.Parts.FirstOrDefault(x => x.LocalId == localId) is not { } parca) return null;
        (string kod, _) = MotorSinifKodu.Belirle(analiz, parca);
        return MotorGerekcesiMetni.Kisa(kod, parca.ClassificationReasons);
    }

    /// <summary>
    /// A STEP's stored or returned analysis as its rows' source: only an
    /// automatic whole-STEP output; a run on selected parts is refused.
    /// </summary>
    public static GeometryLabProcessAdapterResult AnaAnaliz(GeometryLabProcessAdapterResult sonuc) =>
        !sonuc.IsSuccess || sonuc.Analysis!.Otomatik
            ? sonuc
            : new GeometryLabProcessAdapterResult
            {
                Status = GeometryLabProcessAdapterStatus.InvalidJson,
                Message = "Kayıtlı motor çıktısı STEP'in kendi analizi değil (" + sonuc.Analysis.AnalysisMode + ")."
            };

    /// <summary>The user decisions in the rows; `kaynakId` maps a STEP path to its source id (null: not in the project).</summary>
    public static List<MacriaProjeKarari> KararlariTopla(IEnumerable<GeometryLabStepProfileListItem> profil,
        IEnumerable<MontajParcaSatiri> montaj, Func<string, string?> kaynakId)
    {
        var kararlar = new List<MacriaProjeKarari>();
        foreach (GeometryLabStepProfileListItem row in profil)
        {
            if (kaynakId(row.SourceStepPath) is not string id) continue;
            if (row.Deneme is SatirDenemesi profilDenemesi)
                kararlar.Add(DenemeKarari(id, ProfilKimligi(row), MacriaProje.HedefProfil, profilDenemesi));
            if (row.ListeDisi) kararlar.Add(ListeDisiKarari(id, ProfilKimligi(row), MacriaProje.HedefProfil, row.ListeDisiNotu));
            if (row.KullaniciKarari is not string karar) continue;
            kararlar.Add(new MacriaProjeKarari
            {
                Kaynak = id,
                Parca = ProfilKimligi(row),
                Hedef = MacriaProje.HedefProfil,
                Karar = karar,
                Not = string.IsNullOrWhiteSpace(row.UserDecisionNote) ? null : row.UserDecisionNote,
                ElleProfil = karar == MacriaProje.KararElleProfil
                    ? new MacriaElleProfil { Tur = row.ProfileType, Kesit = row.SectionDisplay }
                    : null
            });
        }
        foreach (MontajParcaSatiri row in montaj)
        {
            if (kaynakId(row.SourceStepPath) is not string id) continue;
            if (row.Deneme is SatirDenemesi montajDenemesi)
                kararlar.Add(DenemeKarari(id, MontajKimligi(row), MacriaProje.HedefMontaj, montajDenemesi));
            if (row.ListeDisi) kararlar.Add(ListeDisiKarari(id, MontajKimligi(row), MacriaProje.HedefMontaj, row.ListeDisiNotu));
            foreach (string yol in row.YazilanDxfYollari)
                kararlar.Add(new MacriaProjeKarari
                {
                    Kaynak = id,
                    Parca = MontajKimligi(row),
                    Hedef = MacriaProje.HedefMontaj,
                    Karar = MacriaProje.KararDxfDosyasi,
                    DxfYolu = yol
                });
            if (row.KullaniciKalinlikMm is double kalinlik)
                kararlar.Add(new MacriaProjeKarari
                {
                    Kaynak = id,
                    Parca = MontajKimligi(row),
                    Hedef = MacriaProje.HedefMontaj,
                    Karar = MacriaProje.KararKalinlik,
                    KalinlikMm = kalinlik
                });
            if (row.KullaniciKarari is not string karar) continue;
            kararlar.Add(new MacriaProjeKarari
            {
                Kaynak = id,
                Parca = MontajKimligi(row),
                Hedef = MacriaProje.HedefMontaj,
                Karar = karar
            });
        }
        return kararlar;
    }

    private static MacriaProjeKarari DenemeKarari(string kaynak, MacriaParcaKimligi? parca, string hedef, SatirDenemesi deneme) => new()
    {
        Kaynak = kaynak,
        Parca = parca,
        Hedef = hedef,
        Karar = MacriaProje.KararDene,
        DenemeModu = deneme.Mod,
        EkAnaliz = deneme.EkId
    };

    private static MacriaProjeKarari ListeDisiKarari(string kaynak, MacriaParcaKimligi? parca, string hedef, string not) => new()
    {
        Kaynak = kaynak,
        Parca = parca,
        Hedef = hedef,
        Karar = MacriaProje.KararListeDisi,
        Not = string.IsNullOrWhiteSpace(not) ? null : not
    };

    public static List<MacriaKararAdayi> Adaylar(IEnumerable<GeometryLabStepProfileListItem> profil,
        IEnumerable<MontajParcaSatiri> montaj, Func<string, string?> kaynakId)
    {
        var adaylar = new List<MacriaKararAdayi>();
        foreach (GeometryLabStepProfileListItem row in profil)
            if (kaynakId(row.SourceStepPath) is string id)
                adaylar.Add(new MacriaKararAdayi(id, MacriaProje.HedefProfil, ProfilKimligi(row), row));
        foreach (MontajParcaSatiri row in montaj)
            if (kaynakId(row.SourceStepPath) is string id)
                adaylar.Add(new MacriaKararAdayi(id, MacriaProje.HedefMontaj, MontajKimligi(row), row));
        return adaylar;
    }

    /// <summary>Applies a decision through the row's own decision method; false when it does not apply to that row.</summary>
    public static bool Uygula(MacriaProjeKarari karar, object satir)
    {
        if (karar.Karar == MacriaProje.KararListeDisi && satir is IAnalizSatiri analizSatiri)
        {
            analizSatiri.ListeDisinaCikar(karar.Not);
            return analizSatiri.ListeDisi;
        }
        switch (satir)
        {
            case GeometryLabStepProfileListItem profil:
                switch (karar.Karar)
                {
                    case MacriaProje.KararProfilOnayla: profil.ConfirmAsProfile(karar.Not); break;
                    case MacriaProje.KararElleProfil when karar.ElleProfil is { } elle:
                        profil.ConfirmManualHollowProfile(elle.Tur, elle.Kesit, karar.Not); break;
                    case MacriaProje.KararIncelemeye or MacriaProje.KararKontrole: profil.MoveToReview(karar.Not); break;
                    default: return false;
                }
                return profil.KullaniciKarari == (karar.Karar == MacriaProje.KararIncelemeye ? MacriaProje.KararKontrole : karar.Karar);
            case MontajParcaSatiri montaj when karar.Karar == MacriaProje.KararDxfDosyasi:
                if (string.IsNullOrWhiteSpace(karar.DxfYolu)) return false;
                montaj.DxfYazildi(karar.DxfYolu);
                return true;
            case MontajParcaSatiri montaj when karar.Karar == MacriaProje.KararKalinlik:
                if (karar.KalinlikMm is not double kalinlik) return false;
                montaj.KalinligiDuzelt(kalinlik);
                return montaj.KullaniciKalinlikMm == kalinlik;
            case MontajParcaSatiri montaj:
                switch (karar.Karar)
                {
                    case MacriaProje.KararSacOnayla: montaj.ApproveAsSheet(); break;
                    case MacriaProje.KararKontrole: montaj.MoveToReview(); break;
                    default: return false;
                }
                return montaj.KullaniciKarari == karar.Karar;
            default:
                return false;
        }
    }

    private static MacriaParcaKimligi? ProfilKimligi(GeometryLabStepProfileListItem row) =>
        row.PartLocalId is int localId
            ? new MacriaParcaKimligi { LocalId = localId, Ad = row.PartName, ProductId = row.PartProductId }
            : null;

    private static MacriaParcaKimligi MontajKimligi(MontajParcaSatiri row) =>
        new() { LocalId = row.PartLocalId, Ad = row.PartName, ProductId = row.ProductId };
}
