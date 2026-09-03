using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Macria
{
    // Makineye ozel ayarlar. CATIA panelinin duzeni her kurulumda ayni degil;
    // bu yuzden bekleme suresi ve ogretilmis "Save As" konumu uygulamanin
    // yaninda degil kullanicinin profilinde tutulur.
    internal static class Ayarlar
    {
        public static int PanelBekleme = 3000;  // komuttan sonra panel icin bekleme (ms)
        public static bool RehberGosterildi;    // export sekmesindeki ilk acilis rehberi
        public static bool FareUyarisiGizle;    // toplu export oncesi cikan fare uyarisi
        public static bool KonsolAcik;          // basliktaki konsol dugmesinin durumu
        public static bool IsiHaritasiAcik;     // maliyet tablosunda hucre tonlamasi
        public static bool OnizlemeAcik = true; // export sayfasindaki DXF onizleme paneli
        public static string SonCiktiKlasoru = "";  // onizleme burada DXF arar

        // Ogretilmis Save As konumu (bkz. Ayarlar penceresi)
        public static bool KonumVar;
        public static string PencereSinifi = "";
        public static int Dx;                   // pencerenin sol ustune gore
        public static int Dy;
        public static int PencereGenislik;      // ogretme anindaki pencere olcusu
        public static int PencereYukseklik;

        // Bend Information onay kutusu (bkz. BukumBulucu). Kutu isaretliyken
        // CATIA bazi parcalarda cokuyor; export oncesi kaldiriliyor.
        public static bool BukumKapat = true;
        public static double BukumGri = -1;         // ogretme anindaki kutu parlakligi
        public static double BukumDoygunluk = -1;   // ve renk doygunlugu

        // Agirlik ve maliyet sekmesi
        public static string MalzemeAdi = "DKP / St37 (Çelik)";
        public static double Yogunluk = 7.85;   // g/cm3
        public static double KgFiyat = 0;       // birim para / kg
        public static double KesimFiyat = 0;    // birim para / m
        public static string ParaBirimi = "₺";

        // Plaka tuketimi tahmini (bkz. Nesting.cs)
        public static double PlakaBoy = 3000;      // mm
        public static double PlakaEn = 1500;       // mm
        public static double NestingVerim = 80;    // yuzde
        public static double ParcaPayi = 4;        // her parcanin dort yani, mm
        public static double PlakaKenarPayi = 10;  // plaka kenari, mm

        // En son basarili kur cekiminden kalanlar (bkz. KurServisi)
        public static double KurEurTry;
        public static double KurUsdTry;
        public static DateTime KurTarihi;
        public static string KurKaynagi = "";

        private static string Klasor()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Macria");
        }

        private static string DosyaYolu()
        {
            return Path.Combine(Klasor(), "ayarlar.txt");
        }

        public static void Yukle()
        {
            try
            {
                string yol = DosyaYolu();
                if (!File.Exists(yol)) return;

                foreach (string satir in File.ReadAllLines(yol))
                {
                    int esit = satir.IndexOf('=');
                    if (esit <= 0) continue;

                    string anahtar = satir.Substring(0, esit).Trim();
                    string deger = satir.Substring(esit + 1).Trim();

                    switch (anahtar)
                    {
                        case "PanelBekleme": PanelBekleme = Sayi(deger, PanelBekleme); break;
                        case "RehberGosterildi": RehberGosterildi = deger == "1"; break;
                        case "FareUyarisiGizle": FareUyarisiGizle = deger == "1"; break;
                        case "KonsolAcik": KonsolAcik = deger == "1"; break;
                        case "IsiHaritasiAcik": IsiHaritasiAcik = deger == "1"; break;
                        case "OnizlemeAcik": OnizlemeAcik = deger == "1"; break;
                        case "SonCiktiKlasoru": SonCiktiKlasoru = deger; break;
                        case "KonumVar": KonumVar = deger == "1"; break;
                        case "PencereSinifi": PencereSinifi = deger; break;
                        case "Dx": Dx = Sayi(deger, Dx); break;
                        case "Dy": Dy = Sayi(deger, Dy); break;
                        case "PencereGenislik": PencereGenislik = Sayi(deger, PencereGenislik); break;
                        case "PencereYukseklik": PencereYukseklik = Sayi(deger, PencereYukseklik); break;
                        case "BukumKapat": BukumKapat = deger == "1"; break;
                        case "BukumGri": BukumGri = Ondalik(deger, BukumGri); break;
                        case "BukumDoygunluk": BukumDoygunluk = Ondalik(deger, BukumDoygunluk); break;

                        case "MalzemeAdi": if (deger.Length > 0) MalzemeAdi = deger; break;
                        case "Yogunluk": Yogunluk = Ondalik(deger, Yogunluk); break;
                        case "KgFiyat": KgFiyat = Ondalik(deger, KgFiyat); break;
                        case "KesimFiyat": KesimFiyat = Ondalik(deger, KesimFiyat); break;
                        case "ParaBirimi": if (deger.Length > 0) ParaBirimi = deger; break;
                        case "PlakaBoy": PlakaBoy = Ondalik(deger, PlakaBoy); break;
                        case "PlakaEn": PlakaEn = Ondalik(deger, PlakaEn); break;
                        case "NestingVerim": NestingVerim = Ondalik(deger, NestingVerim); break;
                        case "ParcaPayi": ParcaPayi = Ondalik(deger, ParcaPayi); break;
                        case "PlakaKenarPayi": PlakaKenarPayi = Ondalik(deger, PlakaKenarPayi); break;
                        case "KurEurTry": KurEurTry = Ondalik(deger, KurEurTry); break;
                        case "KurUsdTry": KurUsdTry = Ondalik(deger, KurUsdTry); break;
                        case "KurTarihi": KurTarihi = Gun(deger, KurTarihi); break;
                        case "KurKaynagi": KurKaynagi = deger; break;
                    }
                }
            }
            catch { }
        }

        public static void Kaydet()
        {
            try
            {
                Directory.CreateDirectory(Klasor());

                var satirlar = new List<string>
                {
                    "PanelBekleme=" + PanelBekleme,
                    "RehberGosterildi=" + (RehberGosterildi ? "1" : "0"),
                    "FareUyarisiGizle=" + (FareUyarisiGizle ? "1" : "0"),
                    "KonsolAcik=" + (KonsolAcik ? "1" : "0"),
                    "IsiHaritasiAcik=" + (IsiHaritasiAcik ? "1" : "0"),
                    "OnizlemeAcik=" + (OnizlemeAcik ? "1" : "0"),
                    "SonCiktiKlasoru=" + SonCiktiKlasoru,
                    "KonumVar=" + (KonumVar ? "1" : "0"),
                    "PencereSinifi=" + PencereSinifi,
                    "Dx=" + Dx,
                    "Dy=" + Dy,
                    "PencereGenislik=" + PencereGenislik,
                    "PencereYukseklik=" + PencereYukseklik,
                    "BukumKapat=" + (BukumKapat ? "1" : "0"),
                    "BukumGri=" + BukumGri.ToString(CultureInfo.InvariantCulture),
                    "BukumDoygunluk=" + BukumDoygunluk.ToString(CultureInfo.InvariantCulture),

                    "MalzemeAdi=" + MalzemeAdi,
                    "Yogunluk=" + Yogunluk.ToString(CultureInfo.InvariantCulture),
                    "KgFiyat=" + KgFiyat.ToString(CultureInfo.InvariantCulture),
                    "KesimFiyat=" + KesimFiyat.ToString(CultureInfo.InvariantCulture),
                    "ParaBirimi=" + ParaBirimi,
                    "PlakaBoy=" + PlakaBoy.ToString(CultureInfo.InvariantCulture),
                    "PlakaEn=" + PlakaEn.ToString(CultureInfo.InvariantCulture),
                    "NestingVerim=" + NestingVerim.ToString(CultureInfo.InvariantCulture),
                    "ParcaPayi=" + ParcaPayi.ToString(CultureInfo.InvariantCulture),
                    "PlakaKenarPayi=" + PlakaKenarPayi.ToString(CultureInfo.InvariantCulture),
                    "KurEurTry=" + KurEurTry.ToString(CultureInfo.InvariantCulture),
                    "KurUsdTry=" + KurUsdTry.ToString(CultureInfo.InvariantCulture),
                    "KurTarihi=" + KurTarihi.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "KurKaynagi=" + KurKaynagi
                };

                File.WriteAllLines(DosyaYolu(), satirlar);
            }
            catch { }
        }

        public static void KonumuTemizle()
        {
            KonumVar = false;
            PencereSinifi = "";
            Dx = 0; Dy = 0;
            PencereGenislik = 0; PencereYukseklik = 0;
            Kaydet();
        }

        private static int Sayi(string s, int varsayilan)
        {
            int d;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out d)
                ? d : varsayilan;
        }

        private static DateTime Gun(string s, DateTime varsayilan)
        {
            DateTime d;
            return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out d) ? d : varsayilan;
        }

        // Dosyada nokta ile yazilir; kullanici virgul girse de okunur
        private static double Ondalik(string s, double varsayilan)
        {
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out d))
                return d;

            return varsayilan;
        }
    }
}
