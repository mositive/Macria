using System;
using System.Collections.Generic;

namespace Macria
{
    // Plaka tuketimi tahmini.
    //
    // Parcalari plakaya tek tek yerlestirmeye calismaz; ortalama tuketim
    // sorusunu alan uzerinden cevaplar:
    //
    //   duz alan     A = Hacim / kalinlik          (parca basina, m2)
    //   plaka sayisi N = ceil(toplam A / (plaka alani * verim))
    //
    // Ilk esitlik CATIA'dan gelen hacme dayandigi icin kesindir; DXF'e ya da
    // sinir kutusuna ihtiyac yoktur. Belirsiz olan tek sayi verimdir, o da
    // kullanicinin ayari: atolye kendi gecmisinden bilir ve birkac isten
    // sonra gercek satin almayla karsilastirip duzeltir.
    //
    // Yapmadigi seyler: gercek yerlesim, haddeleme yonu, ortak kenardan
    // kesim, plakadan buyuk parcanin bolunmesi.
    // Tabloya baglandigi icin disa acik
    public sealed class PlakaGrubu
    {
        public double Kalinlik { get; set; }          // mm
        public int SatirAdedi { get; set; }           // kac farkli parca
        public int ParcaAdedi { get; set; }           // adetler dahil

        public double DuzAlanM2 { get; set; }         // parcalarin toplam alani
        public double PlakaAlaniM2 { get; set; }      // tek plaka
        public int PlakaSayisi { get; set; }

        public double KaplananAlanM2 { get { return PlakaSayisi * PlakaAlaniM2; } }

        public double Doluluk
        {
            get
            {
                return KaplananAlanM2 > 0 ? DuzAlanM2 / KaplananAlanM2 : 0;
            }
        }

        public double HurdaAlanM2 { get { return KaplananAlanM2 - DuzAlanM2; } }

        public double PlakaAgirligiKg { get; set; }   // tek plaka
        public double ToplamPlakaKg { get { return PlakaSayisi * PlakaAgirligiKg; } }
        public double ParcaAgirligiKg { get; set; }   // parcalarin toplami
        public double HurdaKg { get { return ToplamPlakaKg - ParcaAgirligiKg; } }

        public double PlakaMaliyet { get; set; }      // plaka satin alma
        public double ParcaMaliyet { get; set; }      // mevcut modelin dedigi

        public double Fark { get { return PlakaMaliyet - ParcaMaliyet; } }

        // Tabloda "8 mm" diye gorunur
        public string KalinlikMetni
        {
            get
            {
                return Kalinlik.ToString("0.##",
                    System.Globalization.CultureInfo.CurrentCulture) + " mm";
            }
        }

        // Tek parcasi bile plakaya sigmayan grup isaretlenir
        public bool Sigmayan { get; set; }
    }

    internal sealed class NestingSonuc
    {
        public readonly List<PlakaGrubu> Gruplar = new List<PlakaGrubu>();

        public double PlakaBoy;      // mm
        public double PlakaEn;       // mm
        public double Verim;         // 0..1

        public int OlculmemisSatir;  // hacmi olmayan, hesaba girmeyen satirlar
        public int SigmayanGrup;

        public int ToplamPlaka;
        public double ToplamDuzAlanM2;
        public double ToplamHurdaKg;
        public double ToplamPlakaMaliyet;
        public double ToplamParcaMaliyet;

        public double ToplamFark { get { return ToplamPlakaMaliyet - ToplamParcaMaliyet; } }

        public bool Bos { get { return Gruplar.Count == 0; } }
    }

    internal static class NestingHesap
    {
        public static NestingSonuc Hesapla(IEnumerable<CostRow> satirlar,
                                           double plakaBoyMm, double plakaEnMm,
                                           double verimYuzde,
                                           double yogunluk, double kgFiyat)
        {
            var sonuc = new NestingSonuc
            {
                PlakaBoy = plakaBoyMm,
                PlakaEn = plakaEnMm,
                Verim = verimYuzde / 100.0
            };

            if (plakaBoyMm <= 0 || plakaEnMm <= 0 || sonuc.Verim <= 0)
                return sonuc;

            double plakaAlani = (plakaBoyMm / 1000.0) * (plakaEnMm / 1000.0);

            // Kalinliga gore toplanir: farkli kalinliklar ayni plakaya girmez.
            // Anahtar yuvarlanir, yoksa 2 ile 2.0000001 ayri grup olur.
            var gruplar = new Dictionary<double, PlakaGrubu>();
            var sira = new List<double>();

            foreach (CostRow r in satirlar)
            {
                if (r == null) continue;

                if (!r.HacimM3.HasValue || r.Thickness <= 0 || r.Quantity <= 0)
                {
                    sonuc.OlculmemisSatir++;
                    continue;
                }

                double kalinlik = Math.Round(r.Thickness, 2);
                double kalinlikM = kalinlik / 1000.0;

                // Tek parcanin acinim alani
                double birimAlan = r.HacimM3.Value / kalinlikM;
                if (birimAlan <= 0 || double.IsNaN(birimAlan) ||
                    double.IsInfinity(birimAlan))
                {
                    sonuc.OlculmemisSatir++;
                    continue;
                }

                PlakaGrubu g;

                if (!gruplar.TryGetValue(kalinlik, out g))
                {
                    g = new PlakaGrubu
                    {
                        Kalinlik = kalinlik,
                        PlakaAlaniM2 = plakaAlani,
                        PlakaAgirligiKg = plakaAlani * kalinlikM * yogunluk * 1000.0
                    };

                    gruplar[kalinlik] = g;
                    sira.Add(kalinlik);
                }

                g.SatirAdedi++;
                g.ParcaAdedi += r.Quantity;
                g.DuzAlanM2 += birimAlan * r.Quantity;
                g.ParcaAgirligiKg += r.ToplamAgirlik ?? 0;
                g.ParcaMaliyet += r.MalzemeMaliyet ?? 0;

                // Alan olarak bile plakaya sigmayan parca varsa sonuc guvenilmez
                if (birimAlan > plakaAlani) g.Sigmayan = true;
            }

            sira.Sort();

            foreach (double k in sira)
            {
                PlakaGrubu g = gruplar[k];

                double kullanilabilir = plakaAlani * sonuc.Verim;

                g.PlakaSayisi = (int)Math.Ceiling(g.DuzAlanM2 / kullanilabilir);
                if (g.PlakaSayisi < 1) g.PlakaSayisi = 1;

                g.PlakaMaliyet = g.ToplamPlakaKg * kgFiyat;

                sonuc.Gruplar.Add(g);

                sonuc.ToplamPlaka += g.PlakaSayisi;
                sonuc.ToplamDuzAlanM2 += g.DuzAlanM2;
                sonuc.ToplamHurdaKg += g.HurdaKg;
                sonuc.ToplamPlakaMaliyet += g.PlakaMaliyet;
                sonuc.ToplamParcaMaliyet += g.ParcaMaliyet;

                if (g.Sigmayan) sonuc.SigmayanGrup++;
            }

            return sonuc;
        }
    }
}
