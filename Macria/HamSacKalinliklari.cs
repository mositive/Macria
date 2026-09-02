using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Macria
{
    // Parca bazinda kullanicinin girdigi ham sac kalinligini saklar.
    // Anahtar, ayni Part farkli urunler altinda kullanilabildigi icin
    // ProductName + PartName ciftidir.
    internal static class HamSacKalinliklari
    {
        private const char Ayirici = '\u001F';
        private static readonly Dictionary<string, double> Degerler =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static bool _yuklendi;

        private static string DosyaYolu()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Macria",
                "ham-sac-kalinliklari.txt");
        }

        private static string Anahtar(string productName, string partName)
        {
            return (productName ?? "").Trim() + Ayirici +
                   (partName ?? "").Trim();
        }

        public static double Getir(
            string productName, string partName, double varsayilan)
        {
            Yukle();

            double value;
            if (Degerler.TryGetValue(Anahtar(productName, partName), out value) &&
                Gecerli(value) &&
                value + 0.0001 >= varsayilan)
                return value;

            return varsayilan;
        }

        public static void Ayarla(
            string productName,
            string partName,
            double value,
            double modelKalinligi)
        {
            Yukle();

            string key = Anahtar(productName, partName);

            // Ham sac model kalinligiyla ayniysa gereksiz override tutulmaz.
            if (Math.Abs(value - modelKalinligi) < 0.0001)
                Degerler.Remove(key);
            else
                Degerler[key] = value;
        }

        public static bool Kaydet(out string hata)
        {
            hata = "";
            Yukle();

            try
            {
                string path = DosyaYolu();
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                var keys = new List<string>(Degerler.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);

                var lines = new List<string>();
                foreach (string key in keys)
                {
                    int position = key.IndexOf(Ayirici);
                    if (position < 0) continue;

                    string productName = key.Substring(0, position);
                    string partName = key.Substring(position + 1);

                    lines.Add(
                        Kodla(productName) + "\t" +
                        Kodla(partName) + "\t" +
                        Degerler[key].ToString(CultureInfo.InvariantCulture));
                }

                File.WriteAllLines(path, lines, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                hata = ex.Message;
                return false;
            }
        }

        public static string Goster(double value)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }

        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string clean = text.Trim();
            if (clean.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(0, clean.Length - 2).Trim();

            // Hem Turkce 1,2 hem Ingilizce 1.2 girisi kabul edilir.
            string invariantText = clean.Replace(',', '.');

            if (!double.TryParse(
                    invariantText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) &&
                !double.TryParse(
                    clean,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out value))
                return false;

            if (value < 0) return false;
            return Gecerli(value);
        }

        private static bool Gecerli(double value)
        {
            return value >= 0.05 && value <= 1000;
        }

        private static void Yukle()
        {
            if (_yuklendi) return;
            _yuklendi = true;

            try
            {
                string path = DosyaYolu();
                if (!File.Exists(path)) return;

                foreach (string line in File.ReadAllLines(path))
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length != 3) continue;

                    string productName = Coz(fields[0]);
                    string partName = Coz(fields[1]);

                    double value;
                    if (!double.TryParse(
                            fields[2],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out value) ||
                        !Gecerli(value))
                        continue;

                    Degerler[Anahtar(productName, partName)] = value;
                }
            }
            catch { }
        }

        private static string Kodla(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Coz(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return ""; }
        }
    }
}
