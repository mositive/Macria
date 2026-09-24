using System;
using System.Collections.Generic;
using System.IO;

namespace Macria
{
    internal class ParcaSutunTanimi
    {
        public string Anahtar = "";
        public string Baslik = "";
        public bool Gorunur = true;

        public ParcaSutunTanimi Kopya()
        {
            return (ParcaSutunTanimi)MemberwiseClone();
        }
    }

    // Sac Lazer Parca ve Urun Agaci Komplesi ayni sutun duzenini kullanir.
    // Yalnizca gorunurluk ve sira saklanir; CATIA alan adlari degistirilmez.
    internal static class ParcaSutunDeposu
    {
        public static List<ParcaSutunTanimi> Sutunlar = Varsayilanlar();

        public static List<ParcaSutunTanimi> Varsayilanlar()
        {
            return new List<ParcaSutunTanimi>
            {
                new ParcaSutunTanimi { Anahtar = "title", Baslik = "Parça Kodu (Title)" },
                new ParcaSutunTanimi { Anahtar = "name", Baslik = "PLM Kimliği (Name)" },
                new ParcaSutunTanimi { Anahtar = "description", Baslik = "Tanım (Description)" },
                new ParcaSutunTanimi { Anahtar = "revision", Baslik = "Revizyon" },
                new ParcaSutunTanimi { Anahtar = "thickness", Baslik = "Kalınlık (mm)" },
                new ParcaSutunTanimi { Anahtar = "raw", Baslik = "Ham Sac (mm)" },
                new ParcaSutunTanimi { Anahtar = "quantity", Baslik = "Adet" }
            };
        }

        private static string DosyaYolu()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Macria", "parca-sutunlari.txt");
        }

        public static void Yukle()
        {
            List<ParcaSutunTanimi> varsayilanlar = Varsayilanlar();
            var bilinenler = new Dictionary<string, ParcaSutunTanimi>(
                StringComparer.OrdinalIgnoreCase);

            foreach (ParcaSutunTanimi s in varsayilanlar)
                bilinenler[s.Anahtar] = s;

            var sirali = new List<ParcaSutunTanimi>();
            var okunan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string path = DosyaYolu();
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string[] fields = line.Split('|');
                        if (fields.Length < 2) continue;

                        ParcaSutunTanimi? kaynak;
                        if (!bilinenler.TryGetValue(fields[0], out kaynak)) continue;
                        if (!okunan.Add(kaynak.Anahtar)) continue;

                        ParcaSutunTanimi kopya = kaynak.Kopya();
                        kopya.Gorunur = fields[1] == "1";
                        sirali.Add(kopya);
                    }
                }
            }
            catch
            {
                sirali.Clear();
                okunan.Clear();
            }

            foreach (ParcaSutunTanimi s in varsayilanlar)
                if (!okunan.Contains(s.Anahtar)) sirali.Add(s);

            if (sirali.Count == 0 || sirali.FindAll(s => s.Gorunur).Count == 0)
                sirali = varsayilanlar;

            Sutunlar = sirali;
        }

        public static bool Kaydet(List<ParcaSutunTanimi> sutunlar, out string hata)
        {
            hata = "";

            try
            {
                string path = DosyaYolu();
                string? folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);

                var lines = new List<string>();
                foreach (ParcaSutunTanimi s in sutunlar)
                    lines.Add(s.Anahtar + "|" + (s.Gorunur ? "1" : "0"));

                File.WriteAllLines(path, lines);

                Sutunlar = new List<ParcaSutunTanimi>();
                foreach (ParcaSutunTanimi s in sutunlar) Sutunlar.Add(s.Kopya());
                return true;
            }
            catch (Exception ex)
            {
                hata = ex.Message;
                return false;
            }
        }
    }
}
