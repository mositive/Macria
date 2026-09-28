using System.Globalization;

namespace Macria
{
    // DXF dosya adi tek yerden uretilir: hem disa aktarim hem de dosyayi
    // sonradan arayan onizleme ve yerlesim ayni adi kurmak zorunda.
    internal static class DxfAdi
    {
        public static string Uret(string urunAdi, double kalinlik, int adet)
        {
            string k = kalinlik.ToString("0.##", CultureInfo.InvariantCulture);
            return Temizle(urunAdi) + "_" + k + "mm_" + adet + "adet.dxf";
        }

        // Reference Title icinde Windows dosya adinda kullanilamayan
        // karakterler bulunabilir; export bu yuzden durmasin
        private static string Temizle(string deger)
        {
            string temiz = (deger ?? "").Trim();

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                temiz = temiz.Replace(c, '_');

            return temiz.Length == 0 ? "REFERENCE_TITLE_OKUNAMADI" : temiz;
        }
    }
}
