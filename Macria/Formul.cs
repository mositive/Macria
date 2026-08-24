using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Macria
{
    // Kullanicinin kendi sutunlari icin kucuk bir hesap makinesi.
    //
    // Desteklenen: + - * / % ^  parantez  sayilar (virgul ya da nokta)
    // degiskenler ve fonksiyonlar: min, max, mutlak, yuvarla, tavan, taban, kok
    //
    // Deger okunamayan (henuz olculmemis) bir degisken kullanilirsa sonuc
    // bos doner; bu hata degildir, hucre bos kalir.
    internal static class Formul
    {
        // Fonksiyon adlari (Turkce ve Ingilizce karsiliklari)
        private static readonly Dictionary<string, int> Fonksiyonlar =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "mutlak", 1 }, { "abs", 1 },
                { "kok", 1 }, { "sqrt", 1 },
                { "tavan", 1 }, { "ceil", 1 },
                { "taban", 1 }, { "floor", 1 },
                { "yuvarla", 2 }, { "round", 2 },
                { "min", 2 }, { "max", 2 }
            };

        public static string FonksiyonListesi()
        {
            return "mutlak(x), kok(x), tavan(x), taban(x), yuvarla(x; basamak), min(a; b), max(a; b) " +
                   "— fonksiyon değerleri noktalı virgülle ayrılır, ondalık için virgül ya da nokta kullanılır.";
        }

        // Ifadeyi hesaplar. Hata yoksa hata=null doner.
        // Sonuc null ise: ya deger eksik ya da tanimsiz islem (0'a bolme).
        public static double? Hesapla(string ifade, IDictionary<string, double?> degerler,
                                      out string hata)
        {
            hata = null;

            if (string.IsNullOrWhiteSpace(ifade))
            {
                hata = "Formül boş.";
                return null;
            }

            try
            {
                var okuyucu = new Okuyucu(ifade, degerler);
                double? sonuc = okuyucu.Ifade();

                okuyucu.SonaKadarOkunduMu();
                return sonuc;
            }
            catch (FormulHatasi ex)
            {
                hata = ex.Message;
                return null;
            }
        }

        // Diyalogda formulu denemek icin: butun degiskenlere 1 verilir
        public static bool Gecerli(string ifade, IEnumerable<string> degiskenler, out string hata)
        {
            var deneme = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in degiskenler) deneme[d] = 1.0;

            Hesapla(ifade, deneme, out hata);
            return hata == null;
        }

        private class FormulHatasi : Exception
        {
            public FormulHatasi(string mesaj) : base(mesaj) { }
        }

        // ================= COZUMLEYICI =================

        private class Okuyucu
        {
            private readonly string _metin;
            private readonly IDictionary<string, double?> _degerler;
            private int _yer;

            public Okuyucu(string metin, IDictionary<string, double?> degerler)
            {
                _metin = metin;
                _degerler = degerler;
            }

            public void SonaKadarOkunduMu()
            {
                Bosluk();
                if (_yer < _metin.Length)
                    throw new FormulHatasi("Beklenmeyen karakter: " + _metin[_yer]);
            }

            private void Bosluk()
            {
                while (_yer < _metin.Length && char.IsWhiteSpace(_metin[_yer])) _yer++;
            }

            private bool Gordu(char c)
            {
                Bosluk();
                if (_yer < _metin.Length && _metin[_yer] == c) { _yer++; return true; }
                return false;
            }

            // toplama / cikarma
            public double? Ifade()
            {
                double? sol = Carpma();

                while (true)
                {
                    Bosluk();
                    if (Gordu('+')) sol = Islem(sol, Carpma(), '+');
                    else if (Gordu('-')) sol = Islem(sol, Carpma(), '-');
                    else return sol;
                }
            }

            private double? Carpma()
            {
                double? sol = Us();

                while (true)
                {
                    Bosluk();
                    if (Gordu('*')) sol = Islem(sol, Us(), '*');
                    else if (Gordu('/')) sol = Islem(sol, Us(), '/');
                    else if (Gordu('%')) sol = Islem(sol, Us(), '%');
                    else return sol;
                }
            }

            private double? Us()
            {
                double? taban = Birim();

                Bosluk();
                if (Gordu('^')) return Islem(taban, Us(), '^');

                return taban;
            }

            private double? Birim()
            {
                Bosluk();

                if (_yer >= _metin.Length) throw new FormulHatasi("Formül yarım kaldı.");

                if (Gordu('-'))
                {
                    double? d = Birim();
                    return d.HasValue ? -d.Value : (double?)null;
                }

                if (Gordu('+')) return Birim();

                if (Gordu('('))
                {
                    double? ic = Ifade();
                    if (!Gordu(')')) throw new FormulHatasi("Kapanmayan parantez.");
                    return ic;
                }

                char c = _metin[_yer];

                if (char.IsDigit(c) || c == '.' || c == ',') return Sayi();
                if (AdHarfi(c)) return AdVeyaFonksiyon();

                throw new FormulHatasi("Anlaşılmayan karakter: " + c);
            }

            private static bool AdHarfi(char c)
            {
                return char.IsLetter(c) || c == '_';
            }

            private double? Sayi()
            {
                int bas = _yer;
                bool ondalikGecti = false;

                while (_yer < _metin.Length)
                {
                    char c = _metin[_yer];

                    if (char.IsDigit(c)) { _yer++; continue; }

                    // Virgul hem ondalik ayraci hem de fonksiyon ayraci olabilir:
                    // yalnizca hemen ardindan rakam geliyorsa ondalik sayilir
                    if ((c == '.' || c == ',') && !ondalikGecti &&
                        _yer + 1 < _metin.Length && char.IsDigit(_metin[_yer + 1]))
                    {
                        ondalikGecti = true;
                        _yer++;
                        continue;
                    }

                    break;
                }

                string ham = _metin.Substring(bas, _yer - bas).Replace(',', '.');

                double d;
                if (!double.TryParse(ham, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new FormulHatasi("Sayı okunamadı: " + ham);

                return d;
            }

            private double? AdVeyaFonksiyon()
            {
                int bas = _yer;
                while (_yer < _metin.Length && (AdHarfi(_metin[_yer]) || char.IsDigit(_metin[_yer])))
                    _yer++;

                string ad = _metin.Substring(bas, _yer - bas);

                Bosluk();
                if (_yer < _metin.Length && _metin[_yer] == '(')
                    return Fonksiyon(ad);

                double? deger;
                if (!_degerler.TryGetValue(ad, out deger))
                    throw new FormulHatasi("Bilinmeyen değişken: " + ad);

                return deger;
            }

            private double? Fonksiyon(string ad)
            {
                int beklenen;
                if (!Fonksiyonlar.TryGetValue(ad, out beklenen))
                    throw new FormulHatasi("Bilinmeyen fonksiyon: " + ad);

                if (!Gordu('(')) throw new FormulHatasi("Fonksiyon parantezi yok: " + ad);

                var argumanlar = new List<double?>();
                if (!Gordu(')'))
                {
                    // Ayrac ";" ya da ","  (2,5 gibi ondalik sayilarla karismasin
                    // diye noktali virgul onerilir)
                    do { argumanlar.Add(Ifade()); } while (Gordu(';') || Gordu(','));

                    if (!Gordu(')'))
                        throw new FormulHatasi("Kapanmayan parantez ya da eksik ayraç (;): " + ad);
                }

                if (argumanlar.Count != beklenen)
                    throw new FormulHatasi(ad + " fonksiyonu " + beklenen + " değer ister.");

                foreach (double? a in argumanlar)
                    if (!a.HasValue) return null;

                return Uygula(ad, argumanlar);
            }

            private static double? Uygula(string ad, List<double?> a)
            {
                switch (ad.ToLowerInvariant())
                {
                    case "mutlak":
                    case "abs": return Math.Abs(a[0].Value);

                    case "kok":
                    case "sqrt": return a[0].Value < 0 ? (double?)null : Math.Sqrt(a[0].Value);

                    case "tavan":
                    case "ceil": return Math.Ceiling(a[0].Value);

                    case "taban":
                    case "floor": return Math.Floor(a[0].Value);

                    case "yuvarla":
                    case "round":
                        int basamak = (int)Math.Max(0, Math.Min(15, a[1].Value));
                        return Math.Round(a[0].Value, basamak, MidpointRounding.AwayFromZero);

                    case "min": return Math.Min(a[0].Value, a[1].Value);
                    case "max": return Math.Max(a[0].Value, a[1].Value);
                }

                throw new FormulHatasi("Bilinmeyen fonksiyon: " + ad);
            }

            private static double? Islem(double? sol, double? sag, char islec)
            {
                if (!sol.HasValue || !sag.HasValue) return null;

                double a = sol.Value, b = sag.Value;

                switch (islec)
                {
                    case '+': return a + b;
                    case '-': return a - b;
                    case '*': return a * b;
                    case '/': return Math.Abs(b) < 1e-12 ? (double?)null : a / b;
                    case '%': return Math.Abs(b) < 1e-12 ? (double?)null : a % b;
                    case '^':
                        double us = Math.Pow(a, b);
                        return double.IsNaN(us) || double.IsInfinity(us) ? (double?)null : us;
                }

                return null;
            }
        }
    }
}
