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
                    PartProductId = row.ProductId
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

    /// <summary>The user decisions in the rows; `kaynakId` maps a STEP path to its source id (null: not in the project).</summary>
    public static List<MacriaProjeKarari> KararlariTopla(IEnumerable<GeometryLabStepProfileListItem> profil,
        IEnumerable<MontajParcaSatiri> montaj, Func<string, string?> kaynakId)
    {
        var kararlar = new List<MacriaProjeKarari>();
        foreach (GeometryLabStepProfileListItem row in profil)
        {
            if (kaynakId(row.SourceStepPath) is not string id) continue;
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
            if (row.ListeDisi) kararlar.Add(ListeDisiKarari(id, MontajKimligi(row), MacriaProje.HedefMontaj, row.ListeDisiNotu));
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
