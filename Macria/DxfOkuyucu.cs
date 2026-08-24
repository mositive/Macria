using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Macria
{
    // Bir DXF dosyasindan onizleme cizimi cikarir.
    //
    // Amac tam bir CAD okuyucusu degil: "acinim dogru mu, bos mu, bekledigim
    // sekil mi" sorusunu goz karariyla cevaplayabilmek. Bu yuzden yaylar,
    // daireler ve egriler kucuk dogru parcalarina bolunur; ekrana cizilen tek
    // sey kirik cizgilerdir.
    internal sealed class DxfCizim
    {
        public readonly List<Point[]> Yollar = new List<Point[]>();
        public int NesneSayisi;

        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;

        public bool Bos { get { return Yollar.Count == 0; } }
        public double Genislik { get { return Bos ? 0 : MaxX - MinX; } }
        public double Yukseklik { get { return Bos ? 0 : MaxY - MinY; } }

        internal void Ekle(List<Point> noktalar)
        {
            if (noktalar == null || noktalar.Count < 2) return;

            foreach (Point p in noktalar)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y)) return;
                if (double.IsInfinity(p.X) || double.IsInfinity(p.Y)) return;
            }

            Yollar.Add(noktalar.ToArray());

            foreach (Point p in noktalar)
            {
                if (p.X < MinX) MinX = p.X;
                if (p.X > MaxX) MaxX = p.X;
                if (p.Y < MinY) MinY = p.Y;
                if (p.Y > MaxY) MaxY = p.Y;
            }
        }

        // DXF'te Y yukari, ekranda asagi bakar; geometri kurulurken
        // Y isareti ters cevrilir.
        public Geometry Geometri()
        {
            var geo = new StreamGeometry();

            using (StreamGeometryContext ctx = geo.Open())
            {
                foreach (Point[] yol in Yollar)
                {
                    ctx.BeginFigure(new Point(yol[0].X, -yol[0].Y), false, false);

                    for (int i = 1; i < yol.Length; i++)
                        ctx.LineTo(new Point(yol[i].X, -yol[i].Y), true, false);
                }
            }

            geo.Freeze();
            return geo;
        }
    }

    internal static class DxfOkuyucu
    {
        // Yaylar kac derecede bir kirilsin
        private const double YayAdimi = 4.0;

        private struct Ikili
        {
            public int Kod;
            public string Deger;
        }

        private sealed class Varlik
        {
            public string Tip = "";
            public readonly List<Ikili> Kodlar = new List<Ikili>();

            public double Sayi(int kod, double varsayilan)
            {
                foreach (Ikili i in Kodlar)
                    if (i.Kod == kod) return Ondalik(i.Deger, varsayilan);

                return varsayilan;
            }

            public string Yazi(int kod)
            {
                foreach (Ikili i in Kodlar)
                    if (i.Kod == kod) return i.Deger;

                return "";
            }
        }

        // ================= GIRIS =================

        public static DxfCizim Oku(string yol, out string hata)
        {
            hata = null;

            try
            {
                if (!File.Exists(yol)) { hata = "Dosya bulunamadı."; return null; }

                // Latin1: sayilar zaten ASCII, blok adlari da giris ile tanim
                // arasinda ayni bayt dizisine cozuldugu icin eslesmeleri bozmaz.
                string[] satirlar = File.ReadAllLines(yol, Encoding.Latin1);

                if (satirlar.Length > 0 &&
                    satirlar[0].StartsWith("AutoCAD Binary", StringComparison.Ordinal))
                {
                    hata = "İkili (binary) DXF önizlenemiyor.";
                    return null;
                }

                List<Ikili> kodlar = Coz(satirlar);

                var bloklar = new Dictionary<string, List<Varlik>>(StringComparer.OrdinalIgnoreCase);
                List<Varlik> varliklar = Bolumler(kodlar, bloklar);

                var cizim = new DxfCizim();
                Isle(varliklar, bloklar, Matrix.Identity, cizim, 0);

                if (cizim.Bos) hata = "Dosyada çizilebilir bir nesne bulunamadı.";
                return cizim;
            }
            catch (Exception ex)
            {
                hata = ex.Message;
                return null;
            }
        }

        // ================= AYRISTIRMA =================

        // DXF, alt alta "grup kodu / deger" satirlarindan olusur
        private static List<Ikili> Coz(string[] satirlar)
        {
            var liste = new List<Ikili>(satirlar.Length / 2 + 1);

            for (int i = 0; i + 1 < satirlar.Length; i += 2)
            {
                int kod;
                if (!int.TryParse(satirlar[i].Trim(), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out kod))
                    continue;

                liste.Add(new Ikili { Kod = kod, Deger = satirlar[i + 1].Trim() });
            }

            return liste;
        }

        // ENTITIES bolumunu ve BLOCKS icindeki blok tanimlarini toplar
        private static List<Varlik> Bolumler(List<Ikili> kodlar,
                                             Dictionary<string, List<Varlik>> bloklar)
        {
            var varliklar = new List<Varlik>();
            int i = 0;

            while (i < kodlar.Count)
            {
                if (kodlar[i].Kod != 0 || kodlar[i].Deger != "SECTION") { i++; continue; }

                i++;
                string bolum = "";

                while (i < kodlar.Count && kodlar[i].Kod != 0)
                {
                    if (kodlar[i].Kod == 2) bolum = kodlar[i].Deger;
                    i++;
                }

                if (bolum == "ENTITIES")
                {
                    varliklar.AddRange(VarlikOku(kodlar, ref i, "ENDSEC"));
                }
                else if (bolum == "BLOCKS")
                {
                    BloklariOku(kodlar, ref i, bloklar);
                }
                else
                {
                    while (i < kodlar.Count &&
                           !(kodlar[i].Kod == 0 && kodlar[i].Deger == "ENDSEC")) i++;
                }
            }

            return varliklar;
        }

        private static void BloklariOku(List<Ikili> kodlar, ref int i,
                                        Dictionary<string, List<Varlik>> bloklar)
        {
            while (i < kodlar.Count)
            {
                if (kodlar[i].Kod == 0 && kodlar[i].Deger == "ENDSEC") { i++; return; }

                if (kodlar[i].Kod != 0 || kodlar[i].Deger != "BLOCK") { i++; continue; }

                i++;
                string ad = "";
                double tx = 0, ty = 0;

                while (i < kodlar.Count && kodlar[i].Kod != 0)
                {
                    if (kodlar[i].Kod == 2) ad = kodlar[i].Deger;
                    else if (kodlar[i].Kod == 10) tx = Ondalik(kodlar[i].Deger, 0);
                    else if (kodlar[i].Kod == 20) ty = Ondalik(kodlar[i].Deger, 0);
                    i++;
                }

                List<Varlik> icerik = VarlikOku(kodlar, ref i, "ENDBLK");

                // Blogun taban noktasi, yerlestirme noktasinin karsiligidir
                if (tx != 0 || ty != 0)
                    icerik.Insert(0, new Varlik { Tip = "__TABAN",
                        Kodlar = { new Ikili { Kod = 10, Deger = Metin(tx) },
                                   new Ikili { Kod = 20, Deger = Metin(ty) } } });

                if (ad.Length > 0) bloklar[ad] = icerik;
            }
        }

        // 0 kodlu satirlar varlik sinirlarini belirler
        private static List<Varlik> VarlikOku(List<Ikili> kodlar, ref int i, string bitis)
        {
            var liste = new List<Varlik>();
            Varlik simdiki = null;

            while (i < kodlar.Count)
            {
                Ikili k = kodlar[i];

                if (k.Kod == 0)
                {
                    if (k.Deger == bitis || k.Deger == "ENDSEC") { i++; return liste; }

                    simdiki = new Varlik { Tip = k.Deger };
                    liste.Add(simdiki);
                    i++;
                    continue;
                }

                if (simdiki != null) simdiki.Kodlar.Add(k);
                i++;
            }

            return liste;
        }

        // ================= CIZIM =================

        private static void Isle(List<Varlik> varliklar,
                                 Dictionary<string, List<Varlik>> bloklar,
                                 Matrix donusum, DxfCizim cizim, int derinlik)
        {
            if (derinlik > 8) return;   // ic ice bloklarda sonsuz donguye karsi

            for (int i = 0; i < varliklar.Count; i++)
            {
                Varlik v = varliklar[i];

                switch (v.Tip)
                {
                    case "__TABAN":
                        // Blogun taban noktasi cizimi otelemez, kaydirir
                        donusum = Kaydir(donusum, -v.Sayi(10, 0), -v.Sayi(20, 0));
                        break;

                    case "LINE": Cizgi(v, donusum, cizim); break;
                    case "CIRCLE": Daire(v, donusum, cizim); break;
                    case "ARC": Yay(v, donusum, cizim); break;
                    case "ELLIPSE": Elips(v, donusum, cizim); break;
                    case "LWPOLYLINE": HafifCokgen(v, donusum, cizim); break;
                    case "SPLINE": Egri(v, donusum, cizim); break;

                    case "POLYLINE":
                        EskiCokgen(v, varliklar, ref i, donusum, cizim);
                        break;

                    case "INSERT":
                        Yerlestir(v, bloklar, donusum, cizim, derinlik);
                        break;
                }
            }
        }

        private static Matrix Kaydir(Matrix ust, double dx, double dy)
        {
            var m = Matrix.Identity;
            m.Translate(dx, dy);
            m.Append(ust);
            return m;
        }

        private static void Cizgi(Varlik v, Matrix m, DxfCizim cizim)
        {
            var noktalar = new List<Point>
            {
                m.Transform(new Point(v.Sayi(10, 0), v.Sayi(20, 0))),
                m.Transform(new Point(v.Sayi(11, 0), v.Sayi(21, 0)))
            };

            cizim.NesneSayisi++;
            cizim.Ekle(noktalar);
        }

        private static void Daire(Varlik v, Matrix m, DxfCizim cizim)
        {
            double r = v.Sayi(40, 0);
            if (r <= 0) return;

            cizim.NesneSayisi++;
            cizim.Ekle(YayNoktalari(m, v.Sayi(10, 0), v.Sayi(20, 0), r, 0, 360));
        }

        private static void Yay(Varlik v, Matrix m, DxfCizim cizim)
        {
            double r = v.Sayi(40, 0);
            if (r <= 0) return;

            double bas = v.Sayi(50, 0);
            double son = v.Sayi(51, 0);
            while (son <= bas) son += 360;

            cizim.NesneSayisi++;
            cizim.Ekle(YayNoktalari(m, v.Sayi(10, 0), v.Sayi(20, 0), r, bas, son));
        }

        private static List<Point> YayNoktalari(Matrix m, double cx, double cy,
                                                double r, double basAci, double sonAci)
        {
            double kapsam = sonAci - basAci;
            int adet = (int)Math.Ceiling(Math.Abs(kapsam) / YayAdimi);
            if (adet < 8) adet = 8;
            if (adet > 720) adet = 720;

            var noktalar = new List<Point>(adet + 1);

            for (int i = 0; i <= adet; i++)
            {
                double a = (basAci + kapsam * i / adet) * Math.PI / 180.0;
                noktalar.Add(m.Transform(new Point(cx + r * Math.Cos(a),
                                                   cy + r * Math.Sin(a))));
            }

            return noktalar;
        }

        private static void Elips(Varlik v, Matrix m, DxfCizim cizim)
        {
            double cx = v.Sayi(10, 0), cy = v.Sayi(20, 0);
            double ax = v.Sayi(11, 0), ay = v.Sayi(21, 0);
            double oran = v.Sayi(40, 1);

            double buyuk = Math.Sqrt(ax * ax + ay * ay);
            if (buyuk <= 0) return;

            double bas = v.Sayi(41, 0);
            double son = v.Sayi(42, 2 * Math.PI);
            while (son <= bas) son += 2 * Math.PI;

            int adet = (int)Math.Ceiling((son - bas) * 180.0 / Math.PI / YayAdimi);
            if (adet < 12) adet = 12;
            if (adet > 720) adet = 720;

            // Kucuk eksen, buyuk eksene dik ve oran katinda
            double bx = -ay * oran, by = ax * oran;

            var noktalar = new List<Point>(adet + 1);

            for (int i = 0; i <= adet; i++)
            {
                double t = bas + (son - bas) * i / adet;
                double c = Math.Cos(t), s = Math.Sin(t);

                noktalar.Add(m.Transform(new Point(cx + ax * c + bx * s,
                                                   cy + ay * c + by * s)));
            }

            cizim.NesneSayisi++;
            cizim.Ekle(noktalar);
        }

        private static void HafifCokgen(Varlik v, Matrix m, DxfCizim cizim)
        {
            bool kapali = ((int)v.Sayi(70, 0) & 1) != 0;

            var ham = new List<Point>();
            var kambur = new List<double>();

            double? x = null;

            foreach (Ikili k in v.Kodlar)
            {
                if (k.Kod == 10)
                {
                    x = Ondalik(k.Deger, 0);
                }
                else if (k.Kod == 20 && x.HasValue)
                {
                    ham.Add(new Point(x.Value, Ondalik(k.Deger, 0)));
                    kambur.Add(0);
                    x = null;
                }
                else if (k.Kod == 42 && ham.Count > 0)
                {
                    // Kambur, ait oldugu noktanin ardindan yazilir
                    kambur[kambur.Count - 1] = Ondalik(k.Deger, 0);
                }
            }

            if (ham.Count < 2) return;

            cizim.NesneSayisi++;
            cizim.Ekle(KamburAc(ham, kambur, kapali, m));
        }

        private static void EskiCokgen(Varlik v, List<Varlik> varliklar, ref int i,
                                       Matrix m, DxfCizim cizim)
        {
            bool kapali = ((int)v.Sayi(70, 0) & 1) != 0;

            var ham = new List<Point>();
            var kambur = new List<double>();

            int j = i + 1;

            while (j < varliklar.Count && varliklar[j].Tip != "SEQEND")
            {
                if (varliklar[j].Tip == "VERTEX")
                {
                    ham.Add(new Point(varliklar[j].Sayi(10, 0), varliklar[j].Sayi(20, 0)));
                    kambur.Add(varliklar[j].Sayi(42, 0));
                }

                j++;
            }

            i = j;   // SEQEND'e kadar tuketildi

            if (ham.Count < 2) return;

            cizim.NesneSayisi++;
            cizim.Ekle(KamburAc(ham, kambur, kapali, m));
        }

        // Kambur (bulge) degeri, iki nokta arasinin duz degil yay oldugunu
        // soyler: b = tan(aci / 4). Yay burada dogru parcalarina bolunur.
        private static List<Point> KamburAc(List<Point> ham, List<double> kambur,
                                            bool kapali, Matrix m)
        {
            var cikti = new List<Point>();
            int adet = ham.Count;

            for (int i = 0; i < adet; i++)
            {
                Point p1 = ham[i];
                bool sonuncu = i == adet - 1;

                if (sonuncu && !kapali) { cikti.Add(m.Transform(p1)); break; }

                Point p2 = sonuncu ? ham[0] : ham[i + 1];
                double b = i < kambur.Count ? kambur[i] : 0;

                if (Math.Abs(b) < 1e-9)
                {
                    cikti.Add(m.Transform(p1));
                    continue;
                }

                double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
                double kiris = Math.Sqrt(dx * dx + dy * dy);

                if (kiris < 1e-12) { cikti.Add(m.Transform(p1)); continue; }

                double aci = 4 * Math.Atan(b);              // yayin toplam acisi
                double r = kiris / (2 * Math.Sin(aci / 2));

                // Merkez, kirisin ortasindan sola dogru r*cos(aci/2) kadar
                double ortX = (p1.X + p2.X) / 2, ortY = (p1.Y + p2.Y) / 2;
                double dikX = -dy / kiris, dikY = dx / kiris;
                double uzak = r * Math.Cos(aci / 2);

                double cx = ortX + dikX * uzak, cy = ortY + dikY * uzak;

                double bas = Math.Atan2(p1.Y - cy, p1.X - cx);
                double yr = Math.Abs(r);

                int bolme = (int)Math.Ceiling(Math.Abs(aci) * 180.0 / Math.PI / YayAdimi);
                if (bolme < 4) bolme = 4;
                if (bolme > 360) bolme = 360;

                for (int k = 0; k < bolme; k++)
                {
                    double a = bas + aci * k / bolme;
                    cikti.Add(m.Transform(new Point(cx + yr * Math.Cos(a),
                                                    cy + yr * Math.Sin(a))));
                }
            }

            if (kapali && cikti.Count > 1) cikti.Add(cikti[0]);

            return cikti;
        }

        // Egriler onizleme icin denetim noktalarindan gecirilir; gercek
        // NURBS cozumu bu olcekte gozle ayirt edilemeyecek bir fark yaratir.
        private static void Egri(Varlik v, Matrix m, DxfCizim cizim)
        {
            var oturtma = new List<Point>();
            var denetim = new List<Point>();

            double? fx = null, cx = null;

            foreach (Ikili k in v.Kodlar)
            {
                if (k.Kod == 11) fx = Ondalik(k.Deger, 0);
                else if (k.Kod == 21 && fx.HasValue)
                {
                    oturtma.Add(new Point(fx.Value, Ondalik(k.Deger, 0)));
                    fx = null;
                }
                else if (k.Kod == 10) cx = Ondalik(k.Deger, 0);
                else if (k.Kod == 20 && cx.HasValue)
                {
                    denetim.Add(new Point(cx.Value, Ondalik(k.Deger, 0)));
                    cx = null;
                }
            }

            List<Point> kaynak = oturtma.Count >= 2 ? oturtma : denetim;
            if (kaynak.Count < 2) return;

            var noktalar = new List<Point>(kaynak.Count);
            foreach (Point p in kaynak) noktalar.Add(m.Transform(p));

            cizim.NesneSayisi++;
            cizim.Ekle(noktalar);
        }

        private static void Yerlestir(Varlik v, Dictionary<string, List<Varlik>> bloklar,
                                      Matrix ust, DxfCizim cizim, int derinlik)
        {
            string ad = v.Yazi(2);

            List<Varlik> icerik;
            if (ad.Length == 0 || !bloklar.TryGetValue(ad, out icerik)) return;

            double sx = v.Sayi(41, 1), sy = v.Sayi(42, 1);
            if (sx == 0) sx = 1;
            if (sy == 0) sy = 1;

            var m = Matrix.Identity;
            m.Scale(sx, sy);
            m.Rotate(v.Sayi(50, 0));                       // DXF'te saat yonunun tersi
            m.Translate(v.Sayi(10, 0), v.Sayi(20, 0));
            m.Append(ust);

            Isle(icerik, bloklar, m, cizim, derinlik + 1);
        }

        // ================= YARDIMCI =================

        private static double Ondalik(string s, double varsayilan)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                ? d : varsayilan;
        }

        private static string Metin(double d)
        {
            return d.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
