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
    /// Rows for one analysed STEP: a single-part file (or a failed analysis)
    /// is one Profiller row; an assembly gives one Profiller row per profile
    /// part and Saclar / Kontrol gerekli rows for the others.
    /// </summary>
    public static (List<GeometryLabStepProfileListItem> Profil, List<MontajParcaSatiri> Montaj) Kur(
        string stepPath, GeometryLabProcessAdapterResult result, double laserMaximumMm)
    {
        if (result.IsSuccess && MontajParcaSatiri.IsAssembly(result.Analysis))
            return MontajSatirlari(stepPath, result, laserMaximumMm);
        var row = new GeometryLabStepProfileListItem { SourceStepPath = stepPath };
        row.Apply(result);
        return (new List<GeometryLabStepProfileListItem> { row }, new List<MontajParcaSatiri>());
    }

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
            if (row.KullaniciKarari is not string karar || kaynakId(row.SourceStepPath) is not string id) continue;
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
            if (row.KullaniciKarari is not string karar || kaynakId(row.SourceStepPath) is not string id) continue;
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
        switch (satir)
        {
            case GeometryLabStepProfileListItem profil:
                switch (karar.Karar)
                {
                    case MacriaProje.KararProfilOnayla: profil.ConfirmAsProfile(karar.Not); break;
                    case MacriaProje.KararElleProfil when karar.ElleProfil is { } elle:
                        profil.ConfirmManualHollowProfile(elle.Tur, elle.Kesit, karar.Not); break;
                    case MacriaProje.KararIncelemeye: profil.MoveToReview(karar.Not); break;
                    case MacriaProje.KararListeDisi: profil.ExcludeFromList(karar.Not); break;
                    default: return false;
                }
                return profil.KullaniciKarari == karar.Karar;
            case MontajParcaSatiri montaj:
                switch (karar.Karar)
                {
                    case MacriaProje.KararSacOnayla when montaj.CanApproveAsSheet: montaj.ApproveAsSheet(); break;
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
