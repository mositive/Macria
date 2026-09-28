using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Macria;

public enum DxfCakismaSecimi
{
    Atla,
    UzerineYaz,
    Iptal
}

public sealed record MotorDxfIsi(string ParcaAdi, string Kaynak, string Hedef);

public sealed class MotorDxfAktarimSonucu
{
    public List<string> Yazilan { get; } = new();
    public List<string> Atlanan { get; } = new();
    public List<(string Hedef, string Neden)> Hatali { get; } = new();
    public bool IptalEdildi { get; set; }
}

/// <summary>
/// Copies approved engine DXFs (part-&lt;id&gt;.dxf) into "&lt;target&gt;\Motor-DXF\" under
/// the DxfAdi name. An existing file is never replaced without asking; the
/// answer may apply to all remaining conflicts.
/// </summary>
public static class MotorDxfAktarici
{
    public const string AltKlasor = "Motor-DXF";

    /// <summary>Only approved sheet rows with an engine DXF; the rest is not planned.</summary>
    public static IReadOnlyList<MotorDxfIsi> Planla(IEnumerable<MontajParcaSatiri> satirlar, string hedefKlasor)
    {
        string klasor = Path.Combine(Path.GetFullPath(hedefKlasor), AltKlasor);
        return satirlar
            .Where(x => x.EffectiveCategory == MontajParcaKategorisi.Sac && x.DxfSourcePath != null && x.ThicknessMm != null)
            .Select(x => new MotorDxfIsi(x.PartName, x.DxfSourcePath!,
                Path.Combine(klasor, DxfAdi.Uret(x.PartName, x.ThicknessMm!.Value, x.Quantity))))
            .ToList();
    }

    /// <param name="sor">Asked for each existing target: the choice and whether it
    /// applies to all remaining conflicts.</param>
    public static MotorDxfAktarimSonucu Uygula(IReadOnlyList<MotorDxfIsi> isler,
        Func<string, (DxfCakismaSecimi Secim, bool Tumune)> sor)
    {
        var sonuc = new MotorDxfAktarimSonucu();
        DxfCakismaSecimi? tumuneUygulanan = null;
        foreach (MotorDxfIsi is_ in isler)
        {
            if (!File.Exists(is_.Kaynak))
            {
                sonuc.Hatali.Add((is_.Hedef, "Motor DXF'i bulunamadı: " + is_.Kaynak));
                continue;
            }
            if (File.Exists(is_.Hedef))
            {
                DxfCakismaSecimi secim;
                if (tumuneUygulanan is DxfCakismaSecimi hepsi)
                    secim = hepsi;
                else
                {
                    (DxfCakismaSecimi cevap, bool tumune) = sor(is_.Hedef);
                    secim = cevap;
                    if (tumune) tumuneUygulanan = cevap;
                }
                if (secim == DxfCakismaSecimi.Iptal)
                {
                    sonuc.IptalEdildi = true;
                    return sonuc;
                }
                if (secim == DxfCakismaSecimi.Atla)
                {
                    sonuc.Atlanan.Add(is_.Hedef);
                    continue;
                }
            }
            string gecici = is_.Hedef + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(is_.Hedef)!);
                File.Copy(is_.Kaynak, gecici, false);
                File.Move(gecici, is_.Hedef, true);
                sonuc.Yazilan.Add(is_.Hedef);
            }
            catch (Exception exception)
            {
                sonuc.Hatali.Add((is_.Hedef, exception.Message));
                try { if (File.Exists(gecici)) File.Delete(gecici); } catch { }
            }
        }
        return sonuc;
    }
}
