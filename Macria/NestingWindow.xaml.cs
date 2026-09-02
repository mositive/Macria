using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Macria
{
    // Plaka tuketimi penceresi.
    //
    // Maliyet tablosu malzemeyi parca agirligi uzerinden hesapliyor; yani
    // hurdayi hic gormuyor. Burasi "aslinda kac plaka satin alinmasi
    // gerekiyor" sorusunu cevaplar ve iki rakamin farkini one cikarir.
    public partial class NestingWindow : Window
    {
        private readonly List<CostRow> _satirlar = new List<CostRow>();

        private readonly double _yogunluk;
        private readonly double _kgFiyat;
        private readonly string _paraBirimi;

        // Kutulari doldururken Ayar_TextChanged bos yere calismasin
        private bool _kuruluyor = true;

        internal NestingWindow(IEnumerable<CostRow> satirlar, double yogunluk,
                               double kgFiyat, string paraBirimi)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            if (satirlar != null)
                foreach (CostRow r in satirlar)
                    if (r != null) _satirlar.Add(r);

            _yogunluk = yogunluk;
            _kgFiyat = kgFiyat;
            _paraBirimi = paraBirimi ?? "";

            BasliklariKur();

            txtBoy.Text = Yaz(Ayarlar.PlakaBoy);
            txtEn.Text = Yaz(Ayarlar.PlakaEn);
            txtVerim.Text = Yaz(Ayarlar.NestingVerim);

            _kuruluyor = false;

            Hesapla();

            // Ayarlar her tus vurusunda degil, pencere kapanirken yazilir
            Closed += (s, e) => Ayarlar.Kaydet();
        }

        private void BasliklariKur()
        {
            string ek = _paraBirimi.Length > 0 ? " (" + _paraBirimi + ")" : "";

            colParcaMaliyet.Header = "Parça Bedeli" + ek;
            colPlakaMaliyet.Header = "Plaka Bedeli" + ek;
            colFark.Header = "Fark" + ek;
        }

        // ================= AYARLAR =================

        private void Ayar_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_kuruluyor) return;
            Hesapla();
        }

        private void Hazir_Click(object sender, RoutedEventArgs e)
        {
            var dugme = sender as Button;
            if (dugme == null) return;

            string etiket = dugme.Tag as string;
            if (string.IsNullOrEmpty(etiket)) return;

            string[] parca = etiket.Split('x');
            if (parca.Length != 2) return;

            _kuruluyor = true;
            txtBoy.Text = parca[0];
            txtEn.Text = parca[1];
            _kuruluyor = false;

            Hesapla();
        }

        // ================= HESAP =================

        private void Hesapla()
        {
            double boy, en, verim;

            bool boyTamam = Oku(txtBoy.Text, out boy) && boy > 0;
            bool enTamam = Oku(txtEn.Text, out en) && en > 0;
            bool verimTamam = Oku(txtVerim.Text, out verim) && verim > 0 && verim <= 100;

            if (!boyTamam || !enTamam)
            {
                Uyar("Plaka ölçüsü sıfırdan büyük olmalı.");
                return;
            }

            if (!verimTamam)
            {
                Uyar("Yerleşim verimi %1 ile %100 arasında olmalı.");
                return;
            }

            txtAyarUyari.Text = "";

            // Gecerli degerler ayarlara islenir, kayit pencere kapanirken
            Ayarlar.PlakaBoy = boy;
            Ayarlar.PlakaEn = en;
            Ayarlar.NestingVerim = verim;

            NestingSonuc sonuc = NestingHesap.Hesapla(
                _satirlar, boy, en, verim, _yogunluk, _kgFiyat);

            Goster(sonuc);
        }

        // Uyari varken eski tablo yaniltmasin
        private void Uyar(string mesaj)
        {
            txtAyarUyari.Text = mesaj;

            tablo.ItemsSource = null;
            txtBosluk.Visibility = Visibility.Visible;
            txtBosluk.Text = mesaj;

            txtToplamPlaka.Text = "—";
            txtToplamAlan.Text = "";
            txtToplamHurda.Text = "—";
            txtHurdaOran.Text = "";
            txtFark.Text = "—";
            txtFarkAciklama.Text = "";
            txtDurum.Text = "";
        }

        private void Goster(NestingSonuc sonuc)
        {
            tablo.ItemsSource = sonuc.Gruplar;

            txtAltBaslik.Text =
                "Kalınlığa Göre Tahmini Plaka İhtiyacı  ·  " +
                Say(sonuc.PlakaBoy, 0) + " × " + Say(sonuc.PlakaEn, 0) + " mm  ·  " +
                "Yerleşim Verimi %" + Say(sonuc.Verim * 100, 0);

            if (sonuc.Bos)
            {
                txtBosluk.Visibility = Visibility.Visible;
                txtBosluk.Text = sonuc.OlculmemisSatir > 0
                    ? "Hesaba Girecek Ölçülmüş Parça Yok\n" +
                      "Maliyet sayfasında önce CATIA'yı tarayıp hesaplayın."
                    : "Hesaplanmış Parça Yok";

                txtToplamPlaka.Text = "—";
                txtToplamAlan.Text = "";
                txtToplamHurda.Text = "—";
                txtHurdaOran.Text = "";
                txtFark.Text = "—";
                txtFarkAciklama.Text = "";
                txtDurum.Text = "";
                return;
            }

            txtBosluk.Visibility = Visibility.Collapsed;

            double plakaKg = 0;
            foreach (PlakaGrubu g in sonuc.Gruplar) plakaKg += g.ToplamPlakaKg;

            txtToplamPlaka.Text = sonuc.ToplamPlaka.ToString("N0", CultureInfo.CurrentCulture);
            txtToplamAlan.Text =
                Say(sonuc.ToplamPlaka * (sonuc.PlakaBoy / 1000.0) * (sonuc.PlakaEn / 1000.0), 1) +
                " m² sac  ·  parçalar " + Say(sonuc.ToplamDuzAlanM2, 1) + " m²";

            txtToplamHurda.Text = Say(sonuc.ToplamHurdaKg, 0) + " kg";
            txtHurdaOran.Text = plakaKg > 0
                ? "Satın alınan sacın %" + Say(sonuc.ToplamHurdaKg / plakaKg * 100, 0) + "'i"
                : "";

            if (_kgFiyat > 0)
            {
                txtFark.Text = (sonuc.ToplamFark >= 0 ? "+" : "") +
                               Say(sonuc.ToplamFark, 2) + " " + _paraBirimi;

                txtFarkAciklama.Text =
                    "Parça ağırlığına göre " + Say(sonuc.ToplamParcaMaliyet, 0) +
                    ", plaka satın alımına göre " + Say(sonuc.ToplamPlakaMaliyet, 0) +
                    " " + _paraBirimi + ".";
            }
            else
            {
                txtFark.Text = "—";
                txtFarkAciklama.Text =
                    "Maliyet ayarlarında kg fiyatı girilirse hurdanın parasal " +
                    "karşılığı burada görünür.";
            }

            txtDurum.Text = Durum(sonuc);
        }

        private static string Durum(NestingSonuc sonuc)
        {
            var parcalar = new List<string>();

            if (sonuc.OlculmemisSatir > 0)
                parcalar.Add(sonuc.OlculmemisSatir +
                             " satır hesaba katılmadı (ölçüm ya da kalınlık yok)");

            if (sonuc.SigmayanGrup > 0)
                parcalar.Add(sonuc.SigmayanGrup +
                             " kalınlıkta plakadan büyük parça var, sonuç güvenilmez");

            parcalar.Add("Tahmindir; gerçek yerleşim hesaplanmaz");

            return string.Join("  ·  ", parcalar);
        }

        // ================= YARDIMCILAR =================

        // Kullanici virgul de nokta da yazabilir
        private static bool Oku(string metin, out double deger)
        {
            deger = 0;
            if (string.IsNullOrWhiteSpace(metin)) return false;

            string temiz = metin.Trim();

            if (double.TryParse(temiz, NumberStyles.Float, CultureInfo.CurrentCulture,
                                out deger)) return true;

            return double.TryParse(temiz, NumberStyles.Float, CultureInfo.InvariantCulture,
                                   out deger);
        }

        private static string Say(double deger, int ondalik)
        {
            return deger.ToString("N" + ondalik, CultureInfo.CurrentCulture);
        }

        // Kutuya yazarken gereksiz sifir gosterilmez: 3000 mm "3000" olarak durur
        private static string Yaz(double deger)
        {
            return deger.ToString("0.##", CultureInfo.CurrentCulture);
        }

        // ================= PENCERE =================

        private void btnKucult_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void btnBuyut_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void btnKapat_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Sayi kutularina harf girilmesin; virgul ve nokta serbest
        private void SadeceOndalik(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if ((c < '0' || c > '9') && c != ',' && c != '.') { e.Handled = true; return; }
        }
    }
}
