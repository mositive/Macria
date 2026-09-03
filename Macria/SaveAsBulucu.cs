using System;
using System.IO;

namespace Macria
{
    // Save As dugmesini goruntusunden bulur.
    //
    // 3DEXPERIENCE paneli kendi ciziyor: Windows'a ne UIA agaci ne de metinli
    // alt pencere veriyor, bu yuzden dugme "nesne" olarak sorgulanamiyor.
    // Geriye kalan tek saglam yol pikseline bakmak.
    //
    // Ogretme aninda tiklanan noktanin etrafindan bir goruntu parcasi kesilip
    // saklanir. Export sirasinda panel ekrandan yakalanir ve bu parca icinde
    // aranir; bulunursa merkezine tiklanir. Boylece panel taşınsa da, boyutu
    // degisse de, baska bir yerde acilsa da dugme bulunur.
    //
    // Yakalama ve eslestirme isini GorselEslesme yapiyor.
    internal static class SaveAsBulucu
    {
        // Ogretilen parcanin olcusu (piksel). Dugmeden biraz genis tutulur ki
        // cevresindeki desen de eslesmeye katilsin.
        private const int OrnekGen = 140;
        private const int OrnekYuk = 44;

        // Bu benzerligin altinda tiklanmaz. Panel her seferinde birebir ayni
        // gorundugu icin dogru eslesme 0,95'in uzerinde cikar; esik dusuk
        // tutulmus olsa yanlis yere tiklama riski dogardi.
        public const double Esik = 0.80;

        public static string DosyaYolu()
        {
            return GorselEslesme.OrnekYolu("saveas.png");
        }

        public static bool VarMi()
        {
            try { return File.Exists(DosyaYolu()); }
            catch { return false; }
        }

        public static void Sil()
        {
            try { if (File.Exists(DosyaYolu())) File.Delete(DosyaYolu()); }
            catch { }
        }

        // ================= OGRETME =================

        // Henuz saklanmamis ornek. Tiklamadan once alinip, tiklama tuttuysa
        // saklanir; boylece dogru dugmenin goruntusu ogrenildigi kesinlesir.
        internal sealed class OrnekAdayi
        {
            internal byte[] Bgra;
            internal int G;
            internal int Y;
        }

        // Verilen ekran noktasinin etrafini keser; saklamaz
        public static OrnekAdayi AdayAl(int x, int y, out string hata)
        {
            hata = "";

            int sol = x - OrnekGen / 2;
            int ust = y - OrnekYuk / 2;

            // Kesilecek alanin ustunde Macria'nin kendi penceresi durmasin,
            // yoksa dugme yerine kendi arayuzumuzu ogrenirdik
            if (GorselEslesme.KendiPenceremizVar(sol, ust, OrnekGen, OrnekYuk))
            {
                hata = "Alanın üstünde Macria penceresi duruyor. " +
                       "Macria'yı kenara alıp tekrar deneyin.";
                return null;
            }

            byte[] bgra = GorselEslesme.EkranAl(sol, ust, OrnekGen, OrnekYuk);

            if (bgra == null)
            {
                hata = "Ekran görüntüsü alınamadı.";
                return null;
            }

            // Tek renk bir alan hicbir seye benzemez, her yere de benzer
            if (GorselEslesme.Duz(bgra))
            {
                hata = "Seçilen alan düz renk; düğmenin üzerinde durduğunuzdan " +
                       "emin olun.";
                return null;
            }

            return new OrnekAdayi { Bgra = bgra, G = OrnekGen, Y = OrnekYuk };
        }

        public static bool AdayiSakla(OrnekAdayi aday)
        {
            if (aday == null || aday.Bgra == null) return false;

            try
            {
                GorselEslesme.PngYaz(aday.Bgra, aday.G, aday.Y, DosyaYolu());
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Ayarlar ekranindaki ogretme: kes ve hemen sakla
        public static bool Ogret(int x, int y, out string hata)
        {
            OrnekAdayi aday = AdayAl(x, y, out hata);
            if (aday == null) return false;

            if (AdayiSakla(aday)) return true;

            hata = "Görüntü kaydedilemedi.";
            return false;
        }

        // ================= ARAMA =================

        // Verilen pencerenin icinde dugmeyi arar. Bulursa merkezinin ekran
        // koordinatini ve benzerligi doner.
        public static bool Bul(IntPtr pencere, out int x, out int y, out double skor)
        {
            x = 0; y = 0; skor = 0;

            byte[] ornekBgra;
            int og, oy;

            if (!GorselEslesme.PngOku(DosyaYolu(), out ornekBgra, out og, out oy))
                return false;

            GorselEslesme.Eslesme e =
                GorselEslesme.Bul(ornekBgra, og, oy, pencere, Esik);

            if (e == null) return false;

            x = e.MerkezX;
            y = e.MerkezY;
            skor = e.Skor;

            return true;
        }
    }
}
