using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Macria
{
    internal enum SutunTuru
    {
        Veri,         // CATIA'dan gelir, hesaplanmaz
        Hesaplanan,   // Macria'nin sabit hesaplari
        Ozel          // kullanicinin kendi formulu
    }

    internal class SutunTanimi
    {
        public string Anahtar = "";
        public string Baslik = "";
        public SutunTuru Tur;
        public bool Metin;            // sayi degil, yazi sutunu
        public bool Gorunur = true;
        public int Ondalik = 2;
        public double Genislik = 90;  // tablodaki piksel genisligi
        public string Formul = "";    // Hesaplanan: aciklama · Ozel: hesap ifadesi

        public bool Silinebilir { get { return Tur == SutunTuru.Ozel; } }

        // Para birimi baslikta {pb} olarak tutulur, gosterirken degistirilir
        public string GorunenBaslik(string paraBirimi)
        {
            return Baslik.Replace("{pb}", paraBirimi);
        }

        public SutunTanimi Kopya()
        {
            return (SutunTanimi)MemberwiseClone();
        }
    }

    // Kullanicinin kendi tanimladigi, formullerde kullanilabilen deger
    internal class ParametreTanimi
    {
        public string Anahtar = "";
        public string Ad = "";
        public string Birim = "";
        public double Deger;

        public ParametreTanimi Kopya()
        {
            return (ParametreTanimi)MemberwiseClone();
        }
    }

    // Tablo yapilandirmasi: sutun sirasi/gorunurlugu, ozel sutunlar ve
    // kullanicinin parametreleri. Kullanici profilinde saklanir.
    internal static class TabloDeposu
    {
        public static List<SutunTanimi> Sutunlar = new List<SutunTanimi>();
        public static List<ParametreTanimi> Parametreler = new List<ParametreTanimi>();

        // Formullerde kullanilabilen hazir degiskenler ve aciklamalari
        public static readonly string[][] HazirDegiskenler =
        {
            new[] { "hacim",          "Parçanın hacmi (m³)" },
            new[] { "alan",           "Toplam yüzey alanı (m²)" },
            new[] { "kalinlik",       "Sac kalınlığı (mm)" },
            new[] { "adet",           "Montajdaki adet" },
            new[] { "birimAgirlik",   "Bir parçanın ağırlığı (kg)" },
            new[] { "toplamAgirlik",  "Adet dahil ağırlık (kg)" },
            new[] { "kesimBoyu",      "Bir parçanın kesim boyu (m)" },
            new[] { "toplamKesim",    "Adet dahil kesim boyu (m)" },
            new[] { "malzemeMaliyet", "Malzeme bedeli" },
            new[] { "kesimMaliyet",   "Kesim bedeli" },
            new[] { "toplamMaliyet",  "Malzeme + kesim bedeli" },
            new[] { "yogunluk",       "Seçili malzemenin yoğunluğu (g/cm³)" },
            new[] { "kgFiyat",        "Malzeme fiyatı (birim para / kg)" },
            new[] { "kesimFiyat",     "Kesim fiyatı (birim para / m)" }
        };

        // ================= VARSAYILANLAR =================

        public static List<SutunTanimi> Varsayilanlar()
        {
            return new List<SutunTanimi>
            {
                new SutunTanimi { Anahtar = "urun", Baslik = "Ürün Adı",
                    Tur = SutunTuru.Veri, Metin = true, Genislik = 0,
                    Formul = "CATIA montaj ağacındaki ürün adı." },

                new SutunTanimi { Anahtar = "parca", Baslik = "Parça Adı",
                    Tur = SutunTuru.Veri, Metin = true, Genislik = 0,
                    Formul = "CATIA'daki sac parça adı." },

                new SutunTanimi { Anahtar = "kalinlik", Baslik = "Kalınlık (mm)",
                    Tur = SutunTuru.Veri, Ondalik = 2, Genislik = 72,
                    Formul = "Sac parça kalınlık parametresi (CATIA)." },

                new SutunTanimi { Anahtar = "adet", Baslik = "Adet",
                    Tur = SutunTuru.Veri, Ondalik = 0, Genislik = 55,
                    Formul = "Montajdaki toplam adet (CATIA)." },

                new SutunTanimi { Anahtar = "hacim", Baslik = "Hacim (m³)",
                    Tur = SutunTuru.Veri, Ondalik = 6, Genislik = 90, Gorunur = false,
                    Formul = "CATIA ölçümü: parçanın hacmi." },

                new SutunTanimi { Anahtar = "alan", Baslik = "Yüzey Alanı (m²)",
                    Tur = SutunTuru.Veri, Ondalik = 4, Genislik = 90, Gorunur = false,
                    Formul = "CATIA ölçümü: toplam yüzey alanı." },

                new SutunTanimi { Anahtar = "birimAgirlik", Baslik = "Birim Ağırlık (kg)",
                    Tur = SutunTuru.Hesaplanan, Ondalik = 3, Genislik = 82,
                    Formul = "hacim × yoğunluk × 1000" },

                new SutunTanimi { Anahtar = "toplamAgirlik", Baslik = "Toplam Ağırlık (kg)",
                    Tur = SutunTuru.Hesaplanan, Ondalik = 2, Genislik = 88,
                    Formul = "birimAgirlik × adet" },

                new SutunTanimi { Anahtar = "kesimBoyu", Baslik = "Kesim Boyu (m)",
                    Tur = SutunTuru.Hesaplanan, Ondalik = 2, Genislik = 82, Gorunur = false,
                    Formul = "(alan − 2 × hacim / kalınlık) / kalınlık" },

                new SutunTanimi { Anahtar = "toplamKesim", Baslik = "Kesim (m)",
                    Tur = SutunTuru.Hesaplanan, Ondalik = 2, Genislik = 72,
                    Formul = "kesimBoyu × adet" },

                new SutunTanimi { Anahtar = "malzemeMaliyet", Baslik = "Malzeme ({pb})",
                    Tur = SutunTuru.Hesaplanan, Ondalik = 2, Genislik = 92,
                    Formul = "toplamAgirlik × kgFiyat" },

                new SutunTanimi { Anahtar = "kesimMaliyet", Baslik = "Kesim ({pb})",
                    Tur = SutunTuru.Hesaplanan, Ondalik = 2, Genislik = 86,
                    Formul = "toplamKesim × kesimFiyat" },

                new SutunTanimi { Anahtar = "toplamMaliyet", Baslik = "Toplam ({pb})",
                    Tur = SutunTuru.Hesaplanan, Ondalik = 2, Genislik = 96,
                    Formul = "malzemeMaliyet + kesimMaliyet" },

                new SutunTanimi { Anahtar = "durum", Baslik = "Durum",
                    Tur = SutunTuru.Veri, Metin = true, Genislik = 105,
                    Formul = "Ölçüm sonucu." }
            };
        }

        // ================= DOSYA =================

        private static string DosyaYolu()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Macria", "tablo.txt");
        }

        public static void Yukle()
        {
            Sutunlar = Varsayilanlar();
            Parametreler = new List<ParametreTanimi>();

            try
            {
                string yol = DosyaYolu();
                if (!File.Exists(yol)) return;

                var varsayilan = new Dictionary<string, SutunTanimi>(StringComparer.Ordinal);
                foreach (SutunTanimi s in Varsayilanlar()) varsayilan[s.Anahtar] = s;

                var sirali = new List<SutunTanimi>();
                var okunan = new HashSet<string>(StringComparer.Ordinal);

                foreach (string satir in File.ReadAllLines(yol))
                {
                    string[] p = satir.Split('|');
                    if (p.Length < 2) continue;

                    if (p[0] == "S" && p.Length >= 8)
                    {
                        string anahtar = p[1];
                        SutunTanimi tanim;

                        if (varsayilan.TryGetValue(anahtar, out tanim))
                        {
                            tanim = tanim.Kopya();
                        }
                        else
                        {
                            // Kullanicinin kendi sutunu
                            tanim = new SutunTanimi { Anahtar = anahtar, Tur = SutunTuru.Ozel };
                        }

                        tanim.Baslik = p[2];
                        tanim.Gorunur = p[3] == "1";
                        tanim.Ondalik = Tam(p[4], tanim.Ondalik);
                        tanim.Genislik = Ondaliki(p[5], tanim.Genislik);

                        // Hazir sutunlarin formulu degistirilemez
                        if (tanim.Tur == SutunTuru.Ozel) tanim.Formul = p[6];

                        if (tanim.Tur == SutunTuru.Ozel) tanim.Metin = false;

                        if (okunan.Add(anahtar)) sirali.Add(tanim);
                    }
                    else if (p[0] == "P" && p.Length >= 5)
                    {
                        Parametreler.Add(new ParametreTanimi
                        {
                            Anahtar = p[1],
                            Ad = p[2],
                            Birim = p[3],
                            Deger = Ondaliki(p[4], 0)
                        });
                    }
                }

                // Dosyada olmayan hazir sutunlar sona eklenir
                foreach (SutunTanimi s in Varsayilanlar())
                    if (!okunan.Contains(s.Anahtar)) sirali.Add(s);

                if (sirali.Count > 0) Sutunlar = sirali;
            }
            catch
            {
                Sutunlar = Varsayilanlar();
                Parametreler = new List<ParametreTanimi>();
            }
        }

        public static void Kaydet(List<SutunTanimi> sutunlar, List<ParametreTanimi> parametreler)
        {
            Sutunlar = sutunlar;
            Parametreler = parametreler;

            try
            {
                string yol = DosyaYolu();
                Directory.CreateDirectory(Path.GetDirectoryName(yol));

                var satirlar = new List<string>();

                foreach (SutunTanimi s in sutunlar)
                    satirlar.Add(string.Join("|", new[]
                    {
                        "S",
                        s.Anahtar,
                        Temiz(s.Baslik),
                        s.Gorunur ? "1" : "0",
                        s.Ondalik.ToString(CultureInfo.InvariantCulture),
                        s.Genislik.ToString(CultureInfo.InvariantCulture),
                        Temiz(s.Formul),
                        s.Tur.ToString()
                    }));

                foreach (ParametreTanimi p in parametreler)
                    satirlar.Add(string.Join("|", new[]
                    {
                        "P",
                        p.Anahtar,
                        Temiz(p.Ad),
                        Temiz(p.Birim),
                        p.Deger.ToString(CultureInfo.InvariantCulture)
                    }));

                File.WriteAllLines(yol, satirlar);
            }
            catch { }
        }

        // Ayrac karakteri dosyayi bozar
        private static string Temiz(string s)
        {
            return (s ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static int Tam(string s, int varsayilan)
        {
            int d;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out d)
                ? d : varsayilan;
        }

        private static double Ondaliki(string s, double varsayilan)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                ? d : varsayilan;
        }
    }
}
