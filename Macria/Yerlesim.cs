using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Macria
{
    // DXF dosya adi tek yerden uretilir: hem disa aktarim hem de dosyayi
    // sonradan arayan onizleme ve yerlesim ayni adi kurmak zorunda.
    internal static class DxfAdi
    {
        public static string Uret(string urunAdi, double kalinlik, int adet)
        {
            string k = kalinlik.ToString("0.##", CultureInfo.InvariantCulture);
            return Temizle(urunAdi) + "_" + k + "mm_" + adet + "adet.dxf";
        }

        // Reference Title icinde Windows dosya adinda kullanilamayan
        // karakterler bulunabilir; export bu yuzden durmasin
        private static string Temizle(string deger)
        {
            string temiz = (deger ?? "").Trim();

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                temiz = temiz.Replace(c, '_');

            return temiz.Length == 0 ? "REFERENCE_TITLE_OKUNAMADI" : temiz;
        }
    }

    // Plakaya yerlestirilecek tek bir parca ornegi. Ayni parcadan 4 adet
    // varsa 4 ayri ornek olusur; her biri kendi basina tasinir ve donduruler.
    public sealed class YerlesimParca
    {
        public string Ad = "";
        public double Kalinlik;

        // Ayni cesitten gelen ornekler ayni renkte cizilir; gercek nesting
        // yazilimlarinda oldugu gibi hangi parca nerede, gozle secilir
        public int RenkIndeks;

        // Parcanin dort yanindaki dokunulmaz pay (mm). Iki komsu arasindaki
        // aciklik ikisinin payinin toplamidir; plaka kenarina uzaklik da
        // plakanin kenar payi artı bu deger olur. Cesit bazinda degistirilir:
        // ince islenmis bir parca daha genis pay isteyebilir.
        public double Pay;

        public double Genislik;    // mm, sinir kutusu
        public double Yukseklik;   // mm

        // Konturu (0,0)-(Genislik,Yukseklik) kutusuna oturtulmus, ekran gibi
        // y asagi bakan hali. Donduruldu ki tum ornekler paylassin.
        public Geometry Kontur;

        public int Plaka = -1;     // -1: bekleme alani
        public double X;           // plakanin sol ustune gore, mm
        public double Y;
        public bool Donuk;         // 90 derece cevrilmis

        public double EtkinGenislik { get { return Donuk ? Yukseklik : Genislik; } }
        public double EtkinYukseklik { get { return Donuk ? Genislik : Yukseklik; } }

        // Sinir kutusunun alani: yerlestirme bununla calisir
        public double Alan { get { return Genislik * Yukseklik; } }

        // Konturdan cikarilan asil alan (mm2). Fire hesabi bunu kullanir;
        // sinir kutusu kullanilsa yarim daire gibi parcalarda fire oldugundan
        // az gorunurdu.
        public double GercekAlan;

        public Rect Kutu
        {
            get { return new Rect(X, Y, EtkinGenislik, EtkinYukseklik); }
        }
    }

    public sealed class YerlesimPlaka
    {
        public double Kalinlik;
    }

    // Gorsel yerlesimin tuttugu her sey: plakalar, parca ornekleri ve
    // olcu ayarlari. Pencere bunun uzerinde calisir.
    internal sealed class YerlesimModel
    {
        public readonly List<YerlesimParca> Parcalar = new List<YerlesimParca>();
        public readonly List<YerlesimPlaka> Plakalar = new List<YerlesimPlaka>();

        public double PlakaGen = 3000;
        public double PlakaYuk = 1500;
        public double VarsayilanPay = 4;  // her parcanin dort yanindaki pay
        public double Kenar = 10;         // plaka kenar payi

        // Kurulum sirasinda disarida kalanlar
        public int DxfsizCesit;
        public int DxfsizAdet;
        public int OkunamayanCesit;
        public bool AdetSiniriAsildi;

        // Konturu kapali halkaya donusmedigi icin alani sinir kutusundan
        // alinan cesit sayisi; fire bu parcalarda oldugundan az gorunur
        public int AlaniTahminiCesit;

        public double PlakaAlaniM2
        {
            get { return (PlakaGen / 1000.0) * (PlakaYuk / 1000.0); }
        }

        public IEnumerable<YerlesimParca> PlakadakiParcalar(int plaka)
        {
            foreach (YerlesimParca p in Parcalar)
                if (p.Plaka == plaka) yield return p;
        }

        public IEnumerable<YerlesimParca> Bekleyenler()
        {
            foreach (YerlesimParca p in Parcalar)
                if (p.Plaka < 0) yield return p;
        }

        // ================= FIRE =================
        //
        // Doluluk ve fire, parcalarin gercek alanindan cikarilir. Ikisi
        // birbirini tamamlar: doluluk + fire = plakanin tamami.

        public double PlakaAlaniMm2 { get { return PlakaGen * PlakaYuk; } }

        public double ParcaAlaniMm2(int plaka)
        {
            double alan = 0;
            foreach (YerlesimParca p in PlakadakiParcalar(plaka)) alan += p.GercekAlan;
            return alan;
        }

        public double Doluluk(int plaka)
        {
            double plakaAlani = PlakaAlaniMm2;
            return plakaAlani > 0 ? ParcaAlaniMm2(plaka) / plakaAlani : 0;
        }

        public double FireAlaniMm2(int plaka)
        {
            double fire = PlakaAlaniMm2 - ParcaAlaniMm2(plaka);
            return fire > 0 ? fire : 0;
        }

        // Tek plakanin agirligi (kg): yogunluk g/cm3
        public double PlakaAgirligiKg(int plaka, double yogunluk)
        {
            if (plaka < 0 || plaka >= Plakalar.Count) return 0;

            double alanM2 = PlakaAlaniMm2 / 1e6;
            double kalinlikM = Plakalar[plaka].Kalinlik / 1000.0;

            return alanM2 * kalinlikM * yogunluk * 1000.0;
        }

        public double FireAgirligiKg(int plaka, double yogunluk)
        {
            if (plaka < 0 || plaka >= Plakalar.Count) return 0;

            double alanM2 = FireAlaniMm2(plaka) / 1e6;
            double kalinlikM = Plakalar[plaka].Kalinlik / 1000.0;

            return alanM2 * kalinlikM * yogunluk * 1000.0;
        }

        // Bir parca verilen yere konabilir mi: plakanin icinde mi, komsulariyla
        // cakisiyor mu, kalinlik tutuyor mu
        public bool Uygun(YerlesimParca parca, int plaka, double x, double y)
        {
            if (plaka < 0 || plaka >= Plakalar.Count) return false;

            // Farkli kalinliktaki saclar ayni plakadan kesilemez
            if (Math.Abs(Plakalar[plaka].Kalinlik - parca.Kalinlik) > 0.001) return false;

            double g = parca.EtkinGenislik;
            double h = parca.EtkinYukseklik;
            double pay = parca.Pay;

            // Kenar payi plakanin, pay parcanin; ikisi ust uste biner
            if (x < Kenar + pay - 0.01 || y < Kenar + pay - 0.01) return false;
            if (x + g > PlakaGen - Kenar - pay + 0.01) return false;
            if (y + h > PlakaYuk - Kenar - pay + 0.01) return false;

            var yeni = new Rect(x, y, g, h);

            foreach (YerlesimParca o in PlakadakiParcalar(plaka))
            {
                if (ReferenceEquals(o, parca)) continue;

                // Iki komsu arasindaki aciklik ikisinin payinin toplami
                if (Cakisiyor(yeni, o.Kutu, pay + o.Pay)) return false;
            }

            return true;
        }

        // Iki kutu arasinda en az "bosluk" kadar acikklik sart. Rect.IntersectsWith
        // kenar kenara degen kutulari da kesisiyor saydigi icin elle bakilir;
        // yoksa otomatik yerlesimin urettigi bitisik parcalar elle
        // tasindiginda gecersiz sayilirdi.
        private static bool Cakisiyor(Rect a, Rect b, double bosluk)
        {
            const double pay = 0.01;

            bool ayrik = a.X >= b.Right + bosluk - pay ||
                         b.X >= a.Right + bosluk - pay ||
                         a.Y >= b.Bottom + bosluk - pay ||
                         b.Y >= a.Bottom + bosluk - pay;

            return !ayrik;
        }

        // Bos plakalar aradan cikarilir, kalanlarin indeksleri kayar
        public void BosPlakalariAt()
        {
            for (int i = Plakalar.Count - 1; i >= 0; i--)
            {
                bool dolu = false;

                foreach (YerlesimParca p in Parcalar)
                    if (p.Plaka == i) { dolu = true; break; }

                if (dolu) continue;

                Plakalar.RemoveAt(i);

                foreach (YerlesimParca p in Parcalar)
                    if (p.Plaka > i) p.Plaka--;
            }
        }
    }

    internal static class YerlesimKurucu
    {
        // Tek seferde bu kadar ornekten fazlasi hem ekrani hem yerlestirmeyi
        // bogar; asilirsa kullaniciya soylenir
        private const int AdetSiniri = 1200;

        // Kaynak, export sayfasinin listesidir: parcanin DXF'i orada uretilir
        // ve yolu satirda durur. Yol yoksa son cikti klasorunde ayni adla
        // aranir — onizleme paneli de ayni yolu izler.
        public static YerlesimModel Kur(IEnumerable<SheetRow> satirlar,
                                        double plakaGen, double plakaYuk,
                                        double pay, double kenar,
                                        string dxfKlasoru)
        {
            var model = new YerlesimModel
            {
                PlakaGen = plakaGen,
                PlakaYuk = plakaYuk,
                VarsayilanPay = pay,
                Kenar = kenar
            };

            if (satirlar == null) return model;

            // Ayni dosya birden cok satirda gecebilir; bir kez okunur
            var onbellek = new Dictionary<string, DxfCizim>(StringComparer.OrdinalIgnoreCase);

            int cesit = 0;

            foreach (SheetRow r in satirlar)
            {
                if (r == null || r.Thickness <= 0 || r.Quantity <= 0) continue;

                string yol = DosyaBul(r, dxfKlasoru);

                if (yol == null)
                {
                    model.DxfsizCesit++;
                    model.DxfsizAdet += r.Quantity;
                    continue;
                }

                DxfCizim cizim;

                if (!onbellek.TryGetValue(yol, out cizim))
                {
                    string hata;
                    cizim = DxfOkuyucu.Oku(yol, out hata);
                    onbellek[yol] = cizim;
                }

                if (cizim == null || cizim.Bos ||
                    cizim.Genislik <= 0 || cizim.Yukseklik <= 0)
                {
                    model.OkunamayanCesit++;
                    model.DxfsizAdet += r.Quantity;
                    continue;
                }

                Geometry kontur = KonturuKur(cizim);

                bool tahmini;
                double gercekAlan = GercekAlan(cizim, out tahmini);

                if (tahmini) model.AlaniTahminiCesit++;

                int renk = cesit++;

                for (int i = 0; i < r.Quantity; i++)
                {
                    if (model.Parcalar.Count >= AdetSiniri)
                    {
                        model.AdetSiniriAsildi = true;
                        break;
                    }

                    model.Parcalar.Add(new YerlesimParca
                    {
                        Ad = r.PartName,
                        Kalinlik = Math.Round(r.Thickness, 2),
                        Genislik = cizim.Genislik,
                        Yukseklik = cizim.Yukseklik,
                        Kontur = kontur,
                        GercekAlan = gercekAlan,
                        RenkIndeks = renk,
                        Pay = pay
                    });
                }

                if (model.AdetSiniriAsildi) break;
            }

            return model;
        }

        private static string DosyaBul(SheetRow r, string klasor)
        {
            try
            {
                if (!string.IsNullOrEmpty(r.DxfYolu) &&
                    System.IO.File.Exists(r.DxfYolu)) return r.DxfYolu;

                if (string.IsNullOrEmpty(klasor)) return null;

                // Ad, disa aktarimda kullanilan ham sac kalinligiyla kurulur
                string yol = System.IO.Path.Combine(
                    klasor, DxfAdi.Uret(r.ProductName, r.HamSacKalinligi, r.Quantity));

                return System.IO.File.Exists(yol) ? yol : null;
            }
            catch
            {
                return null;
            }
        }

        // Cizimi sol ust kosesi (0,0) olan, y asagi bakan bir kutuya tasir.
        // DXF'te y yukari baktigi icin isaret ters cevrilir.
        private static Geometry KonturuKur(DxfCizim cizim)
        {
            var geo = new StreamGeometry();

            using (StreamGeometryContext ctx = geo.Open())
            {
                foreach (Point[] yol in cizim.Yollar)
                {
                    ctx.BeginFigure(Tasi(yol[0], cizim), false, false);

                    for (int i = 1; i < yol.Length; i++)
                        ctx.LineTo(Tasi(yol[i], cizim), true, false);
                }
            }

            geo.Freeze();
            return geo;
        }

        private static Point Tasi(Point p, DxfCizim cizim)
        {
            return new Point(p.X - cizim.MinX, cizim.MaxY - p.Y);
        }

        // Parcanin sac kaplayan asil alani.
        //
        // En genis kapali yol dis kontur sayilir, geri kalanlar delik kabul
        // edilip cikarilir. Cizimdeki isaret ve yazi gibi kucuk yollar da
        // cikarilir ama alanlari ihmal edilebilir. Sonuc sinir kutusunu
        // asamaz; asarsa (bozuk cizim) kutuya kirpilir.
        private static double GercekAlan(DxfCizim cizim, out bool tahmini)
        {
            tahmini = false;

            double kutu = cizim.Genislik * cizim.Yukseklik;

            List<List<Point>> halkalar = Halkalar(cizim);

            double enBuyuk = 0, toplam = 0;

            foreach (List<Point> h in halkalar)
            {
                double a = Math.Abs(KapaliAlan(h));
                if (a <= 0) continue;

                toplam += a;
                if (a > enBuyuk) enBuyuk = a;
            }

            // Kapali kontur cikmadiysa alan bilinmiyor; sinir kutusu kullanilir
            // ve bu durum kullaniciya soylenir
            if (enBuyuk <= 0)
            {
                tahmini = true;
                return kutu;
            }

            // Dis kontur eksi delikler
            double sonuc = enBuyuk - (toplam - enBuyuk);
            if (sonuc <= 0) sonuc = enBuyuk;

            return kutu > 0 && sonuc > kutu ? kutu : sonuc;
        }

        // DXF'te dis kontur tek bir kapali polyline degildir: onlarca ayri
        // LINE ve ARC parcasindan olusur. Alan cikarmak icin once bu parcalar
        // uc uca eklenip kapali halkalara donusturulur. Kapanmayan zincirler
        // (isaret cizgileri, yazi) atilir.
        private static List<List<Point>> Halkalar(DxfCizim cizim)
        {
            var halkalar = new List<List<Point>>();

            // Tolerans parcanin buyuklugune gore; DXF genelde tam kapanir ama
            // yuvarlama farklari birkac mikron acik birakabilir
            double tol = Math.Max(cizim.Genislik, cizim.Yukseklik) * 1e-5;
            if (tol < 0.002) tol = 0.002;

            var acik = new List<List<Point>>();

            foreach (Point[] yol in cizim.Yollar)
                if (yol != null && yol.Length >= 2) acik.Add(new List<Point>(yol));

            while (acik.Count > 0)
            {
                int sonIndeks = acik.Count - 1;

                List<Point> zincir = acik[sonIndeks];
                acik.RemoveAt(sonIndeks);

                bool buyudu = true;

                while (buyudu && !Yakin(zincir[0], zincir[zincir.Count - 1], tol))
                {
                    buyudu = false;

                    for (int i = 0; i < acik.Count; i++)
                    {
                        List<Point> aday = acik[i];
                        Point son = zincir[zincir.Count - 1];

                        if (Yakin(son, aday[0], tol))
                        {
                            zincir.AddRange(aday.GetRange(1, aday.Count - 1));
                        }
                        else if (Yakin(son, aday[aday.Count - 1], tol))
                        {
                            aday.Reverse();
                            zincir.AddRange(aday.GetRange(1, aday.Count - 1));
                        }
                        else continue;

                        acik.RemoveAt(i);
                        buyudu = true;
                        break;
                    }
                }

                if (zincir.Count >= 4 &&
                    Yakin(zincir[0], zincir[zincir.Count - 1], tol))
                    halkalar.Add(zincir);
            }

            return halkalar;
        }

        private static bool Yakin(Point a, Point b, double tol)
        {
            return Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;
        }

        // Gauss (ayakkabi bagi) formulu; yol kapali kabul edilir
        private static double KapaliAlan(List<Point> yol)
        {
            if (yol == null || yol.Count < 3) return 0;

            double s = 0;

            for (int i = 0; i < yol.Count; i++)
            {
                Point a = yol[i];
                Point b = yol[(i + 1) % yol.Count];

                s += a.X * b.Y - b.X * a.Y;
            }

            return s / 2.0;
        }
    }
}
