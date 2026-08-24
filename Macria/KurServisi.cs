using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Macria
{
    // Doviz kurlari. Once internetten cekilir; internet yoksa (sirket agi,
    // kapali makine) en son basarili cekimin degerleri kullanilir. Hic
    // cekim yapilmamis bir makinede asagidaki gomulu degerler devreye girer
    // ve arayuzde "cevrimdisi" olarak tarihiyle birlikte gosterilir.
    internal static class KurServisi
    {
        // Gomulu (hard coded) yedek kurlar ve gecerli olduklari gun
        private const double GomuluEurTry = 56.2318;
        private const double GomuluUsdTry = 48.0655;
        private static readonly DateTime GomuluTarih = new DateTime(2026, 8, 21);

        public static double EurTry { get; private set; }
        public static double UsdTry { get; private set; }

        public static double EurUsd
        {
            get { return UsdTry > 0 ? EurTry / UsdTry : 0; }
        }

        // Kurlarin gecerli oldugu gun (canli cekimde API'nin verdigi gun)
        public static DateTime Tarih { get; private set; }

        // Bu oturumda internetten cekilebildi mi
        public static bool Canli { get; private set; }

        // Degerlerin nereden geldigi: API adi ya da gomulu/kayitli
        public static string Kaynak { get; private set; } = "";

        // Kaynak adlari ve adresleri (arayuzde ipucu olarak gosterilir)
        private const string FrankfurterAd = "Frankfurter (ECB)";
        private const string FrankfurterAdres = "https://api.frankfurter.app";
        private const string ErApiAd = "ExchangeRate-API";
        private const string ErApiAdres = "https://open.er-api.com";
        private const string GomuluAd = "Macria Gömülü Değerleri";

        // Kurlarin geldigi adres; gomulu degerlerde bos doner
        public static string KaynakAdresi
        {
            get
            {
                if (Kaynak == FrankfurterAd) return FrankfurterAdres;
                if (Kaynak == ErApiAd) return ErApiAdres;
                return "";
            }
        }

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static KurServisi()
        {
            // Profilde kayitli son kurlar varsa onlarla, yoksa gomulu degerlerle basla
            if (Ayarlar.KurEurTry > 0 && Ayarlar.KurUsdTry > 0)
            {
                EurTry = Ayarlar.KurEurTry;
                UsdTry = Ayarlar.KurUsdTry;
                Tarih = Ayarlar.KurTarihi;
                Kaynak = Ayarlar.KurKaynagi.Length > 0 ? Ayarlar.KurKaynagi : GomuluAd;
            }
            else
            {
                EurTry = GomuluEurTry;
                UsdTry = GomuluUsdTry;
                Tarih = GomuluTarih;
                Kaynak = GomuluAd;
            }
        }

        // ================= CEVIRI =================

        // 1 birim para biriminin TL karsiligi
        private static double TlKarsiligi(string paraBirimi)
        {
            switch (paraBirimi)
            {
                case "€": return EurTry;
                case "$": return UsdTry;
                default: return 1.0;   // TL
            }
        }

        public static double Cevir(double tutar, string kaynak, string hedef)
        {
            if (kaynak == hedef) return tutar;

            double hedefKur = TlKarsiligi(hedef);
            if (hedefKur <= 0) return tutar;

            return tutar * TlKarsiligi(kaynak) / hedefKur;
        }

        // "1 € = 49,2000 ₺" gibi tek satirlik kur ozeti
        public static string CiftMetni(string kaynak, string hedef)
        {
            return "1 " + kaynak + " = " +
                   Cevir(1, kaynak, hedef).ToString("N4", CultureInfo.CurrentCulture) +
                   " " + hedef;
        }

        public static string OzetMetni()
        {
            return CiftMetni("€", "₺") + "   ·   " +
                   CiftMetni("$", "₺") + "   ·   " +
                   CiftMetni("€", "$");
        }

        // "Canlı · Frankfurter · 21.08.2026" gibi tek satirlik kaynak bilgisi
        public static string KaynakMetni()
        {
            return (Canli ? "Canlı · " : "Çevrimdışı · ") + Kaynak + " · " +
                   (Canli ? "" : "Son Güncelleme ") +
                   Tarih.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
        }

        // Kur seridinin ipucunda gosterilen ayrintili aciklama
        public static string KaynakAyrintisi()
        {
            string adres = KaynakAdresi;

            return "Veri Kaynağı: " + Kaynak +
                   (adres.Length > 0 ? "\n" + adres : "") +
                   "\nKur Tarihi: " + Tarih.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) +
                   "\nDurum: " + (Canli
                       ? "Bu oturumda internetten alındı."
                       : "İnternete ulaşılamadı, kayıtlı değerler kullanılıyor.");
        }

        // ================= CEKME =================

        // Basarisiz olursa mevcut (kayitli ya da gomulu) degerler korunur
        public static async Task<bool> Yenile()
        {
            if (await Frankfurter()) return true;
            if (await ErApi()) return true;

            Canli = false;
            return false;
        }

        // api.frankfurter.app — anahtar istemez, ECB verisi
        private static async Task<bool> Frankfurter()
        {
            try
            {
                string cevap = await _http.GetStringAsync(
                    "https://api.frankfurter.app/latest?from=EUR&to=USD,TRY");

                using (JsonDocument belge = JsonDocument.Parse(cevap))
                {
                    JsonElement kok = belge.RootElement;
                    JsonElement kurlar = kok.GetProperty("rates");

                    double eurUsd = kurlar.GetProperty("USD").GetDouble();
                    double eurTry = kurlar.GetProperty("TRY").GetDouble();
                    if (eurUsd <= 0 || eurTry <= 0) return false;

                    DateTime gun;
                    if (!DateTime.TryParse(kok.GetProperty("date").GetString(),
                                           CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out gun))
                        gun = DateTime.Today;

                    Yaz(eurTry, eurTry / eurUsd, gun, FrankfurterAd);
                    return true;
                }
            }
            catch { return false; }
        }

        // open.er-api.com — birincisi engellenirse yedek kaynak
        private static async Task<bool> ErApi()
        {
            try
            {
                string cevap = await _http.GetStringAsync(
                    "https://open.er-api.com/v6/latest/EUR");

                using (JsonDocument belge = JsonDocument.Parse(cevap))
                {
                    JsonElement kok = belge.RootElement;
                    JsonElement kurlar = kok.GetProperty("rates");

                    double eurUsd = kurlar.GetProperty("USD").GetDouble();
                    double eurTry = kurlar.GetProperty("TRY").GetDouble();
                    if (eurUsd <= 0 || eurTry <= 0) return false;

                    DateTime gun = DateTime.Today;
                    JsonElement zaman;
                    if (kok.TryGetProperty("time_last_update_unix", out zaman))
                        gun = DateTimeOffset.FromUnixTimeSeconds(zaman.GetInt64()).LocalDateTime.Date;

                    Yaz(eurTry, eurTry / eurUsd, gun, ErApiAd);
                    return true;
                }
            }
            catch { return false; }
        }

        // Cekilen kurlar profile de yazilir; internet olmayan bir sonraki
        // acilista gomulu degerler yerine bunlar kullanilir
        private static void Yaz(double eurTry, double usdTry, DateTime gun, string kaynak)
        {
            EurTry = eurTry;
            UsdTry = usdTry;
            Tarih = gun;
            Kaynak = kaynak;
            Canli = true;

            Ayarlar.KurEurTry = eurTry;
            Ayarlar.KurUsdTry = usdTry;
            Ayarlar.KurTarihi = gun;
            Ayarlar.KurKaynagi = kaynak;
            Ayarlar.Kaydet();
        }
    }
}
