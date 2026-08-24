using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Macria
{
    // Maliyet tablosunda sayi hucrelerinin arkasini degere gore tonlar.
    //
    // Olcekleme her sutunda kendi icinde yapilir: bir sutunun en buyugu en
    // koyu, en kucugu neredeyse renksiz olur. Boylece kalinlik ile maliyet
    // gibi buyukluk siralari farkli sutunlar birbirini bastirmaz.
    internal static class IsiHaritasi
    {
        private struct Aralik
        {
            public double Alt;
            public double Ust;
        }

        private static readonly Dictionary<string, Aralik> _araliklar =
            new Dictionary<string, Aralik>(StringComparer.Ordinal);

        public static bool Acik;

        // Tonlamanin iki ucu: dusuk deger sicak kum, yuksek deger kizil.
        // Koyu arka planda okunurlugu bozmamak icin saydamlik dusuk tutulur.
        private static readonly Color AltRenk = Color.FromRgb(0xB8, 0x87, 0x3F);
        private static readonly Color UstRenk = Color.FromRgb(0xB4, 0x46, 0x38);

        private const byte AltSaydam = 8;
        private const byte UstSaydam = 88;

        // Tablo her hesaplandiginda sutun araliklari yeniden cikarilir
        public static void Olc(IEnumerable<CostRow> satirlar, List<SutunTanimi> sutunlar)
        {
            _araliklar.Clear();

            foreach (SutunTanimi s in sutunlar)
            {
                if (s.Metin || !s.Gorunur || s.Anahtar == "durum") continue;

                double alt = double.MaxValue, ust = double.MinValue;

                foreach (CostRow r in satirlar)
                {
                    double? d = Deger(r.Deger(s.Anahtar));
                    if (!d.HasValue) continue;

                    if (d.Value < alt) alt = d.Value;
                    if (d.Value > ust) ust = d.Value;
                }

                if (alt > ust) continue;   // sutunda hic deger yok

                _araliklar[s.Anahtar] = new Aralik { Alt = alt, Ust = ust };
            }
        }

        public static void Temizle()
        {
            _araliklar.Clear();
        }

        // Tonlama kapaliyken de saydam bir firca doner: bos birakilirsa hucre
        // fare tiklamalarini gecirmez ve satir secimi bozulur.
        public static Brush Firca(string anahtar, double? deger)
        {
            if (!Acik || !deger.HasValue) return Brushes.Transparent;

            Aralik a;
            if (!_araliklar.TryGetValue(anahtar, out a)) return Brushes.Transparent;

            double genislik = a.Ust - a.Alt;

            // Tek degerli sutunda siralama diye bir sey yok, tonlanmaz
            if (genislik <= 1e-12) return Brushes.Transparent;

            double t = (deger.Value - a.Alt) / genislik;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            var renk = Color.FromArgb(
                (byte)(AltSaydam + (UstSaydam - AltSaydam) * t),
                (byte)(AltRenk.R + (UstRenk.R - AltRenk.R) * t),
                (byte)(AltRenk.G + (UstRenk.G - AltRenk.G) * t),
                (byte)(AltRenk.B + (UstRenk.B - AltRenk.B) * t));

            var firca = new SolidColorBrush(renk);
            firca.Freeze();
            return firca;
        }

        internal static double? Deger(object ham)
        {
            if (ham == null) return null;

            if (ham is double) return (double)ham;
            if (ham is int) return (int)ham;
            if (ham is string) return null;

            try { return Convert.ToDouble(ham, CultureInfo.InvariantCulture); }
            catch { return null; }
        }
    }

    // Hucre arka planini uretir. Baglantinin kaynagi sutunun kendi degeri,
    // parametresi ise hangi sutun oldugunu soyleyen anahtardir.
    internal sealed class IsiFircasi : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
                              CultureInfo culture)
        {
            string anahtar = parameter as string;
            if (string.IsNullOrEmpty(anahtar)) return Brushes.Transparent;

            return IsiHaritasi.Firca(anahtar, IsiHaritasi.Deger(value));
        }

        public object ConvertBack(object value, Type targetType, object parameter,
                                  CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
