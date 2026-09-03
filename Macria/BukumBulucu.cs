using System;
using System.IO;

namespace Macria
{
    // "Save as Dxf" panelindeki Bend Information onay kutusunu bulur ve
    // isaretli olup olmadigini soyler.
    //
    // Kutu isaretliyken CATIA bazi parcalarda cokuyor; bu yuzden Save As'e
    // basmadan once kaldirilmasi gerekiyor. Panel kendi cizildigi icin kutu
    // ne UIA ile ne de alt pencere olarak sorgulanabiliyor (bkz. SaveAsBulucu),
    // geriye yine piksele bakmak kaliyor.
    //
    // Iki adimli calisir:
    //
    // 1) SATIRI BUL — saklanan ornek yalnizca kutunun sagindaki "Bend
    //    Information" yazisidir, kutunun kendisi orneğe girmez. Boylece ornek
    //    kutunun durumundan bagimsiz olur: isaretli de olsa bos da olsa satir
    //    ayni benzerlikle bulunur. Yazi orneğe dahil edilmese, panelde ust uste
    //    duran bes onay kutusu birbirinin ayni gorundugu icin hangisinin
    //    tiklanacagi bilinemezdi.
    //
    // 2) DURUMU OKU — bulunan satirin solundaki kutunun ici okunur. Isaretli
    //    kutu vurgu renginde dolu, bos kutu notr; bu ikisi hem parlaklik hem
    //    renk doygunlugu bakimindan ayrilir. Esik sabit degil: ogretme aninda
    //    isaretli kutudan olculen degerler ayarlara yazilir, calisma aninda
    //    olculen degerle karsilastirilir. Tema ya da vurgu rengi degisse bile
    //    karsilastirma ayni makinede tutarli kalir.
    //
    // Tiklama yalnizca kutu isaretli gorunuyorsa yapilir. Bulunamazsa ya da
    // durum okunamazsa hicbir sey yapilmaz: yanlis yere tiklamaktansa kutuyu
    // oldugu gibi birakmak yeglenir.
    internal static class BukumBulucu
    {
        // Ornek, kutunun sagindan baslar ve yaziyi kapsar
        private const int OrnekGen = 156;
        private const int OrnekYuk = 22;

        // Ornegin sol kenari, kutunun merkezinden bu kadar sagdadir.
        // Aramada bulunan sol kenardan geri sayilarak kutuya donulur.
        private const int KutuUzakligi = 14;

        // Kutunun icinden okunan karenin yarisi (9x9 piksel)
        private const int KutuYari = 4;

        // Save As dugmesindekinden biraz dusuk: ornek burada duz yazi, panelin
        // arka plani kadar zengin bir desen degil.
        public const double Esik = 0.75;

        // Olculen deger ogretilen degere bu kadar yakinsa kutu isaretli sayilir
        private const double GriTolerans = 24;
        private const double DoygunlukTolerans = 34;

        public static string DosyaYolu()
        {
            return GorselEslesme.OrnekYolu("bukum.png");
        }

        public static bool VarMi()
        {
            try { return File.Exists(DosyaYolu()) && Ayarlar.BukumGri >= 0; }
            catch { return false; }
        }

        public static void Sil()
        {
            try { if (File.Exists(DosyaYolu())) File.Delete(DosyaYolu()); }
            catch { }

            Ayarlar.BukumGri = -1;
            Ayarlar.BukumDoygunluk = -1;
            Ayarlar.Kaydet();
        }

        // ================= OGRETME =================

        // Kullanici ISARETLI kutunun uzerindeyken cagrilir: yazinin goruntusu
        // saklanir, kutunun rengi olculup ayarlara yazilir.
        public static bool Ogret(int x, int y, out string hata)
        {
            hata = "";

            int sol = x + KutuUzakligi;
            int ust = y - OrnekYuk / 2;

            // Kesilecek alanin ustunde Macria'nin kendi penceresi durmasin,
            // yoksa panel yerine kendi arayuzumuzu ogrenirdik
            if (GorselEslesme.KendiPenceremizVar(x - KutuYari, ust,
                                                 OrnekGen + KutuUzakligi + KutuYari,
                                                 OrnekYuk))
            {
                hata = "Alanın üstünde Macria penceresi duruyor. " +
                       "Macria'yı kenara alıp tekrar deneyin.";
                return false;
            }

            byte[] bgra = GorselEslesme.EkranAl(sol, ust, OrnekGen, OrnekYuk);

            if (bgra == null)
            {
                hata = "Ekran görüntüsü alınamadı.";
                return false;
            }

            // Yazinin bulunmadigi bos bir serit her satira benzer
            if (GorselEslesme.Duz(bgra))
            {
                hata = "Kutunun sağında yazı görünmüyor; imleci " +
                       "\"Bend Information\" kutusunun üzerine getirin.";
                return false;
            }

            double gri, doygunluk;

            if (!Olc(x, y, out gri, out doygunluk))
            {
                hata = "Kutunun rengi okunamadı.";
                return false;
            }

            try
            {
                GorselEslesme.PngYaz(bgra, OrnekGen, OrnekYuk, DosyaYolu());
            }
            catch
            {
                hata = "Görüntü kaydedilemedi.";
                return false;
            }

            Ayarlar.BukumGri = gri;
            Ayarlar.BukumDoygunluk = doygunluk;
            Ayarlar.Kaydet();

            return true;
        }

        // ================= ARAMA =================

        internal sealed class Durum
        {
            public bool Bulundu;
            public bool Isaretli;
            public int X;              // kutunun ekrandaki merkezi
            public int Y;
            public double Skor;
            public double Gri;
            public double Doygunluk;
        }

        public static Durum Bul(IntPtr pencere)
        {
            var d = new Durum();

            byte[] ornek;
            int og, oy;

            if (!GorselEslesme.PngOku(DosyaYolu(), out ornek, out og, out oy))
                return d;

            GorselEslesme.Eslesme e =
                GorselEslesme.Bul(ornek, og, oy, pencere, Esik);

            if (e == null) return d;

            d.Bulundu = true;
            d.Skor = e.Skor;
            d.X = e.Sol - KutuUzakligi;
            d.Y = e.MerkezY;

            if (!Olc(d.X, d.Y, out d.Gri, out d.Doygunluk)) return d;

            d.Isaretli = Isaretli(d.Gri, d.Doygunluk);
            return d;
        }

        // Kutunun icinden ortalama parlaklik ve renk doygunlugu
        public static bool Olc(int x, int y, out double gri, out double doygunluk)
        {
            gri = 0; doygunluk = 0;

            int k = KutuYari * 2 + 1;

            byte[] p = GorselEslesme.EkranAl(x - KutuYari, y - KutuYari, k, k);
            if (p == null) return false;

            int n = k * k;
            long griToplam = 0, doygunlukToplam = 0;

            for (int i = 0; i < n; i++)
            {
                int j = i * 4;

                int mavi = p[j], yesil = p[j + 1], kirmizi = p[j + 2];

                griToplam += (mavi + yesil * 2 + kirmizi) >> 2;

                int enCok = Math.Max(mavi, Math.Max(yesil, kirmizi));
                int enAz = Math.Min(mavi, Math.Min(yesil, kirmizi));

                doygunlukToplam += enCok - enAz;
            }

            gri = (double)griToplam / n;
            doygunluk = (double)doygunlukToplam / n;

            return true;
        }

        // Olculen degerler ogretilen isaretli kutuya yeterince yakin mi
        public static bool Isaretli(double gri, double doygunluk)
        {
            if (Ayarlar.BukumGri < 0) return false;

            return Math.Abs(gri - Ayarlar.BukumGri) <= GriTolerans &&
                   Math.Abs(doygunluk - Ayarlar.BukumDoygunluk) <= DoygunlukTolerans;
        }
    }
}
