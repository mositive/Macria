using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Macria
{
    // Yogunlugu bilinen hazir malzemeler. Listeden secim yapilinca yogunluk
    // otomatik dolar; kullanici yine de elle degistirebilir.
    public class Malzeme
    {
        public string Ad { get; set; } = "";
        public double Yogunluk { get; set; }   // g/cm3
        public bool Ozel { get; set; }         // kullanicinin ekledigi

        // Acilir listede ozel malzemeler isaretli gorunsun
        public override string ToString()
        {
            return Ozel ? Ad + "  ·  özel" : Ad;
        }

        public static List<Malzeme> Varsayilanlar()
        {
            return new List<Malzeme>
            {
                new Malzeme { Ad = "DKP / St37 (Çelik)",   Yogunluk = 7.85 },
                new Malzeme { Ad = "Galvanizli Sac",       Yogunluk = 7.85 },
                new Malzeme { Ad = "HARDOX 450",           Yogunluk = 7.85 },
                new Malzeme { Ad = "Paslanmaz 304",        Yogunluk = 7.90 },
                new Malzeme { Ad = "Paslanmaz 316",        Yogunluk = 8.00 },
                new Malzeme { Ad = "Alüminyum 1050",       Yogunluk = 2.71 },
                new Malzeme { Ad = "Alüminyum 5754",       Yogunluk = 2.66 },
                new Malzeme { Ad = "Bakır",                Yogunluk = 8.96 },
                new Malzeme { Ad = "Pirinç",               Yogunluk = 8.50 }
            };
        }
    }

    // Kullanicinin ekledigi malzemeler; kullanici profilinde saklanir ve
    // her acilista varsayilanlarin arkasina eklenir.
    // Dosya bicimi: her satirda "Ad|Yogunluk" (yogunluk nokta ile).
    public static class MalzemeDeposu
    {
        private static string DosyaYolu()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Macria", "malzemeler.txt");
        }

        public static List<Malzeme> Tumu()
        {
            var liste = Malzeme.Varsayilanlar();
            liste.AddRange(Ozeller());
            return liste;
        }

        public static List<Malzeme> Ozeller()
        {
            var liste = new List<Malzeme>();

            try
            {
                string yol = DosyaYolu();
                if (!System.IO.File.Exists(yol)) return liste;

                foreach (string satir in System.IO.File.ReadAllLines(yol))
                {
                    int ayrac = satir.LastIndexOf('|');
                    if (ayrac <= 0) continue;

                    string ad = satir.Substring(0, ayrac).Trim();
                    string sayi = satir.Substring(ayrac + 1).Trim();

                    double yogunluk;
                    if (ad.Length == 0 ||
                        !double.TryParse(sayi, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture,
                                         out yogunluk) ||
                        yogunluk <= 0)
                        continue;

                    liste.Add(new Malzeme { Ad = ad, Yogunluk = yogunluk, Ozel = true });
                }
            }
            catch { }

            return liste;
        }

        private static void OzelleriYaz(List<Malzeme> ozeller)
        {
            try
            {
                string yol = DosyaYolu();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(yol));

                var satirlar = new List<string>();
                foreach (Malzeme m in ozeller)
                    satirlar.Add(m.Ad + "|" + m.Yogunluk.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));

                System.IO.File.WriteAllLines(yol, satirlar);
            }
            catch { }
        }

        // Ayni adli ozel malzeme varsa yogunlugu guncellenir
        public static void Ekle(string ad, double yogunluk)
        {
            var ozeller = Ozeller();

            var mevcut = ozeller.Find(m =>
                string.Equals(m.Ad, ad, StringComparison.CurrentCultureIgnoreCase));

            if (mevcut != null) mevcut.Yogunluk = yogunluk;
            else ozeller.Add(new Malzeme { Ad = ad, Yogunluk = yogunluk, Ozel = true });

            OzelleriYaz(ozeller);
        }

        public static void Sil(string ad)
        {
            var ozeller = Ozeller();
            ozeller.RemoveAll(m =>
                string.Equals(m.Ad, ad, StringComparison.CurrentCultureIgnoreCase));
            OzelleriYaz(ozeller);
        }

        public static bool VarsayilanAdi(string ad)
        {
            return Malzeme.Varsayilanlar().Exists(m =>
                string.Equals(m.Ad, ad, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    // Maliyet tablosunun bir satiri.
    //
    // Agirlik ve kesim boyu, CATIA'dan okunan iki sayidan turetiliyor:
    // parcanin hacmi (m3) ve toplam yuzey alani (m2).
    //
    //   Duz alan  A_duz = Hacim / kalinlik
    //   Kesim boyu    P = (ToplamAlan - 2 * A_duz) / kalinlik
    //
    // Ikinci esitlik, kalinligi sabit her sac parca icin gecerli: yuzeyin
    // tamami iki genis yuz artik cevre boyunca kalinlik kadar genisleyen bir
    // seritten olusur. Parca bukulu olsa da hacim ve alan korundugu icin
    // sonuc acilmis (flat pattern) olcusunu verir.
    public class CostRow : INotifyPropertyChanged
    {
        public string ProductName { get; set; } = "";
        public string PartName { get; set; } = "";
        public double Thickness { get; set; }
        public int Quantity { get; set; }

        // CATIA'dan okunan ham degerler (SI) — tabloya baglanabilmesi icin ozellik
        public double? HacimM3 { get; set; }
        public double? AlanM2 { get; set; }

        private double? _birimAgirlik;
        private double? _toplamAgirlik;
        private double? _kesimBoyu;
        private double? _toplamKesim;
        private double? _malzemeMaliyet;
        private double? _kesimMaliyet;
        private double? _toplamMaliyet;
        private string _durum = "";

        public double? BirimAgirlik { get { return _birimAgirlik; } }
        public double? ToplamAgirlik { get { return _toplamAgirlik; } }
        public double? KesimBoyu { get { return _kesimBoyu; } }
        public double? ToplamKesim { get { return _toplamKesim; } }
        public double? MalzemeMaliyet { get { return _malzemeMaliyet; } }
        public double? KesimMaliyet { get { return _kesimMaliyet; } }
        public double? ToplamMaliyet { get { return _toplamMaliyet; } }
        public string Durum { get { return _durum; } }

        public bool OlculduMu { get { return HacimM3.HasValue; } }

        // Kullanicinin kendi sutunlarinin sonuclari (anahtar -> deger).
        // Tabloya "Ozel[ad]" olarak baglanir; bu yuzden alan degil ozellik.
        public Dictionary<string, double?> Ozel { get; } =
            new Dictionary<string, double?>(StringComparer.Ordinal);

        // Sutun anahtarina gore hucre degeri: yazi sutunlarinda string,
        // sayi sutunlarinda double? doner.
        internal object Deger(string anahtar)
        {
            switch (anahtar)
            {
                case "urun": return ProductName;
                case "parca": return PartName;
                case "durum": return Durum;

                case "kalinlik": return Thickness > 0 ? Thickness : (double?)null;
                case "adet": return (double)Quantity;
                case "hacim": return HacimM3;
                case "alan": return AlanM2;

                case "birimAgirlik": return BirimAgirlik;
                case "toplamAgirlik": return ToplamAgirlik;
                case "kesimBoyu": return KesimBoyu;
                case "toplamKesim": return ToplamKesim;
                case "malzemeMaliyet": return MalzemeMaliyet;
                case "kesimMaliyet": return KesimMaliyet;
                case "toplamMaliyet": return ToplamMaliyet;
            }

            double? ozel;
            return Ozel.TryGetValue(anahtar, out ozel) ? ozel : null;
        }

        // Formullerin gordugu degerler
        internal Dictionary<string, double?> Degiskenler(
            double yogunluk, double kgFiyat, double kesimFiyat)
        {
            return new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            {
                { "hacim", HacimM3 },
                { "alan", AlanM2 },
                { "kalinlik", Thickness > 0 ? Thickness : (double?)null },
                { "adet", Quantity },
                { "birimAgirlik", _birimAgirlik },
                { "toplamAgirlik", _toplamAgirlik },
                { "kesimBoyu", _kesimBoyu },
                { "toplamKesim", _toplamKesim },
                { "malzemeMaliyet", _malzemeMaliyet },
                { "kesimMaliyet", _kesimMaliyet },
                { "toplamMaliyet", _toplamMaliyet },
                { "yogunluk", yogunluk },
                { "kgFiyat", kgFiyat },
                { "kesimFiyat", kesimFiyat }
            };
        }

        // Ozel sutunlar sirayla hesaplanir; onceki sutunun sonucu sonrakinin
        // formulunde kullanilabilir.
        internal void OzelHesapla(List<SutunTanimi> sutunlar,
                                  Dictionary<string, double?> parametreler,
                                  double yogunluk, double kgFiyat, double kesimFiyat)
        {
            Ozel.Clear();

            Dictionary<string, double?> degerler =
                Degiskenler(yogunluk, kgFiyat, kesimFiyat);

            foreach (var p in parametreler) degerler[p.Key] = p.Value;

            foreach (SutunTanimi s in sutunlar)
            {
                if (s.Tur != SutunTuru.Ozel) continue;

                string hata;
                double? sonuc = Formul.Hesapla(s.Formul, degerler, out hata);

                Ozel[s.Anahtar] = hata == null ? sonuc : null;
                degerler[s.Anahtar] = Ozel[s.Anahtar];
            }
        }

        public void OlcumTemizle(string durum)
        {
            HacimM3 = null;
            AlanM2 = null;
            _durum = durum;
            Hesapla(0, 0, 0);
        }

        public void OlcumYaz(double hacimM3, double alanM2)
        {
            HacimM3 = hacimM3;
            AlanM2 = alanM2;
            _durum = "Ölçüldü";
        }

        // Yogunluk g/cm3, fiyatlar birim para / kg ve birim para / m
        public void Hesapla(double yogunluk, double kgFiyat, double kesimFiyat)
        {
            if (!HacimM3.HasValue || Thickness <= 0)
            {
                // Olculdu ama kalinlik okunamadiysa satir hesaplanamaz
                if (HacimM3.HasValue && Thickness <= 0) _durum = "Kalınlık Yok";

                _birimAgirlik = null; _toplamAgirlik = null;
                _kesimBoyu = null; _toplamKesim = null;
                _malzemeMaliyet = null; _kesimMaliyet = null; _toplamMaliyet = null;
                Bildir();
                return;
            }

            double hacim = HacimM3.Value;          // m3
            double kalinlikM = Thickness / 1000.0; // mm -> m

            // 1 m3 x (g/cm3) = 1000 kg
            _birimAgirlik = hacim * yogunluk * 1000.0;
            _toplamAgirlik = _birimAgirlik * Quantity;

            if (AlanM2.HasValue)
            {
                double duzAlan = hacim / kalinlikM;
                double cevre = (AlanM2.Value - 2.0 * duzAlan) / kalinlikM;

                // Olcum tutarsizsa (negatif cevre) kesim boyu yazilmaz
                _kesimBoyu = cevre > 0 ? (double?)cevre : null;
                _toplamKesim = _kesimBoyu.HasValue
                    ? (double?)(_kesimBoyu.Value * Quantity)
                    : null;
            }
            else
            {
                _kesimBoyu = null;
                _toplamKesim = null;
            }

            _malzemeMaliyet = _toplamAgirlik * kgFiyat;
            _kesimMaliyet = _toplamKesim.HasValue
                ? (double?)(_toplamKesim.Value * kesimFiyat)
                : 0.0;

            _toplamMaliyet = (_malzemeMaliyet ?? 0) + (_kesimMaliyet ?? 0);

            Bildir();
        }

        private void Bildir()
        {
            var h = PropertyChanged;
            if (h == null) return;

            foreach (string ad in new[]
            {
                "BirimAgirlik", "ToplamAgirlik", "KesimBoyu", "ToplamKesim",
                "MalzemeMaliyet", "KesimMaliyet", "ToplamMaliyet", "Durum", "OlculduMu"
            })
                h(this, new PropertyChangedEventArgs(ad));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
