using System;
using System.Collections.Generic;
using System.Windows;

namespace Macria
{
    // Otomatik yerlestirme.
    //
    // Parcalari sinir kutulariyla, MaxRects (Best Short Side Fit) yontemiyle
    // plakaya dizer. Bu, dikdortgen yerlestirmeler icinde iyi bir sonuc verir;
    // gercek nesting yazilimlarinin yaptigi gibi parcalari konturlarindan ic
    // ice gecirmez. Yani ciktisi "elle yapilabilecegin iyi bir hali"dir,
    // teorik en iyi degil.
    //
    // Kalinliklar ayri plakalara gider; ayni plakaya farkli kalinlik konmaz.
    internal static class YerlesimCozucu
    {
        private sealed class Sayfa
        {
            public int Indeks;
            public readonly List<Rect> Bos = new List<Rect>();
        }

        public static void Otomatik(YerlesimModel model)
        {
            if (model == null || model.Parcalar.Count == 0) return;

            foreach (YerlesimParca p in model.Parcalar)
            {
                p.Plaka = -1;
                p.Donuk = false;
            }

            model.Plakalar.Clear();

            // Kalinliga gore ayir
            var gruplar = new Dictionary<double, List<YerlesimParca>>();
            var sira = new List<double>();

            foreach (YerlesimParca p in model.Parcalar)
            {
                List<YerlesimParca> liste;

                if (!gruplar.TryGetValue(p.Kalinlik, out liste))
                {
                    liste = new List<YerlesimParca>();
                    gruplar[p.Kalinlik] = liste;
                    sira.Add(p.Kalinlik);
                }

                liste.Add(p);
            }

            sira.Sort();

            foreach (double kalinlik in sira)
                GrubuYerlestir(model, gruplar[kalinlik], kalinlik);

            model.BosPlakalariAt();
        }

        private static void GrubuYerlestir(YerlesimModel model,
                                           List<YerlesimParca> parcalar,
                                           double kalinlik)
        {
            // Once buyuk parcalar: kucukler sonradan bosluklara sizabilir
            parcalar.Sort(delegate (YerlesimParca a, YerlesimParca b)
            {
                double ax = Math.Max(a.Genislik, a.Yukseklik);
                double bx = Math.Max(b.Genislik, b.Yukseklik);

                int k = bx.CompareTo(ax);
                return k != 0 ? k : b.Alan.CompareTo(a.Alan);
            });

            var sayfalar = new List<Sayfa>();

            foreach (YerlesimParca p in parcalar)
            {
                // Parca kendi payiyla birlikte yerlestirilir: ayak izi her
                // yandan pay kadar buyuk. Iki ayak izi bitistiginde parcalar
                // arasinda ikisinin payinin toplami kadar aciklik kalir.
                double g = p.Genislik + 2 * p.Pay;
                double h = p.Yukseklik + 2 * p.Pay;

                if (!Sigar(model, g, h))
                {
                    // Plakadan buyuk parca: bekleme alaninda kalir
                    p.Plaka = -1;
                    continue;
                }

                bool kondu = false;

                foreach (Sayfa s in sayfalar)
                {
                    if (Dene(model, s, p, g, h)) { kondu = true; break; }
                }

                if (kondu) continue;

                // Hicbir plakaya sigmadi, yenisini ac
                Sayfa yeni = SayfaAc(model, kalinlik);
                sayfalar.Add(yeni);

                if (!Dene(model, yeni, p, g, h)) p.Plaka = -1;
            }
        }

        private static bool Sigar(YerlesimModel model, double g, double h)
        {
            double ic = IcGenislik(model);
            double iy = IcYukseklik(model);

            return (g <= ic && h <= iy) || (h <= ic && g <= iy);
        }

        private static double IcGenislik(YerlesimModel model)
        {
            return model.PlakaGen - 2 * model.Kenar;
        }

        private static double IcYukseklik(YerlesimModel model)
        {
            return model.PlakaYuk - 2 * model.Kenar;
        }

        private static Sayfa SayfaAc(YerlesimModel model, double kalinlik)
        {
            model.Plakalar.Add(new YerlesimPlaka { Kalinlik = kalinlik });

            var s = new Sayfa { Indeks = model.Plakalar.Count - 1 };

            s.Bos.Add(new Rect(model.Kenar, model.Kenar,
                               IcGenislik(model), IcYukseklik(model)));
            return s;
        }

        // En dar bosluga oturtan yeri arar (Best Short Side Fit)
        private static bool Dene(YerlesimModel model, Sayfa sayfa,
                                 YerlesimParca parca, double g, double h)
        {
            double enIyiKisa = double.MaxValue;
            double enIyiUzun = double.MaxValue;

            bool bulundu = false;
            double sx = 0, sy = 0;
            bool sDonuk = false;

            foreach (Rect f in sayfa.Bos)
            {
                for (int d = 0; d < 2; d++)
                {
                    double pg = d == 0 ? g : h;
                    double ph = d == 0 ? h : g;

                    if (pg > f.Width + 1e-9 || ph > f.Height + 1e-9) continue;

                    double artanX = f.Width - pg;
                    double artanY = f.Height - ph;

                    double kisa = Math.Min(artanX, artanY);
                    double uzun = Math.Max(artanX, artanY);

                    if (kisa < enIyiKisa - 1e-9 ||
                        (Math.Abs(kisa - enIyiKisa) <= 1e-9 && uzun < enIyiUzun))
                    {
                        enIyiKisa = kisa;
                        enIyiUzun = uzun;

                        sx = f.X;
                        sy = f.Y;
                        sDonuk = d == 1;
                        bulundu = true;
                    }
                }
            }

            if (!bulundu) return false;

            parca.Plaka = sayfa.Indeks;
            parca.Donuk = sDonuk;

            // Ayak izinin sol ustu bulundu; parcanin kendisi pay kadar iceride
            parca.X = sx + parca.Pay;
            parca.Y = sy + parca.Pay;

            double kg = sDonuk ? h : g;
            double kh = sDonuk ? g : h;

            Bol(sayfa.Bos, new Rect(sx, sy, kg, kh));
            Buda(sayfa.Bos);

            return true;
        }

        // Kullanilan alani ortusen bos dikdortgenler parcalanir
        private static void Bol(List<Rect> bos, Rect kul)
        {
            for (int i = bos.Count - 1; i >= 0; i--)
            {
                Rect f = bos[i];

                if (kul.X >= f.Right - 1e-9 || kul.Right <= f.X + 1e-9 ||
                    kul.Y >= f.Bottom - 1e-9 || kul.Bottom <= f.Y + 1e-9)
                    continue;

                bos.RemoveAt(i);

                if (kul.Y > f.Y + 1e-9)
                    bos.Add(new Rect(f.X, f.Y, f.Width, kul.Y - f.Y));

                if (kul.Bottom < f.Bottom - 1e-9)
                    bos.Add(new Rect(f.X, kul.Bottom, f.Width, f.Bottom - kul.Bottom));

                if (kul.X > f.X + 1e-9)
                    bos.Add(new Rect(f.X, f.Y, kul.X - f.X, f.Height));

                if (kul.Right < f.Right - 1e-9)
                    bos.Add(new Rect(kul.Right, f.Y, f.Right - kul.Right, f.Height));
            }
        }

        // Baskasinin icinde kalan bosluklar listeyi sisirir, atilir
        private static void Buda(List<Rect> bos)
        {
            for (int i = bos.Count - 1; i >= 0; i--)
            {
                if (bos[i].Width <= 1e-9 || bos[i].Height <= 1e-9)
                {
                    bos.RemoveAt(i);
                    continue;
                }

                for (int j = 0; j < bos.Count; j++)
                {
                    if (i == j || j >= bos.Count) continue;

                    if (Icinde(bos[i], bos[j]))
                    {
                        bos.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool Icinde(Rect kucuk, Rect buyuk)
        {
            return kucuk.X >= buyuk.X - 1e-9 &&
                   kucuk.Y >= buyuk.Y - 1e-9 &&
                   kucuk.Right <= buyuk.Right + 1e-9 &&
                   kucuk.Bottom <= buyuk.Bottom + 1e-9;
        }
    }
}
