using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Macria
{
    // Tablo Ayarlari: sutunlarin gorunurlugu/sirasi/basligi, kullanicinin
    // kendi formullu sutunlari ve formullerde kullanacagi parametreler.
    public partial class TabloAyarlariWindow : Window
    {
        private readonly ObservableCollection<SutunSatiri> _sutunlar =
            new ObservableCollection<SutunSatiri>();

        private readonly ObservableCollection<ParametreSatiri> _parametreler =
            new ObservableCollection<ParametreSatiri>();

        public TabloAyarlariWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            listeSutunlar.ItemsSource = _sutunlar;
            listeParametreler.ItemsSource = _parametreler;

            Yukle(TabloDeposu.Sutunlar, TabloDeposu.Parametreler);
            YardimiYaz();
        }

        private void Yukle(List<SutunTanimi> sutunlar, List<ParametreTanimi> parametreler)
        {
            _sutunlar.Clear();
            foreach (SutunTanimi s in sutunlar) _sutunlar.Add(new SutunSatiri(s.Kopya()));

            _parametreler.Clear();
            foreach (ParametreTanimi p in parametreler)
                _parametreler.Add(new ParametreSatiri(p.Kopya()));
        }

        private void YardimiYaz()
        {
            var liste = new List<object>();

            foreach (string[] d in TabloDeposu.HazirDegiskenler)
                liste.Add(new { Ad = d[0], Aciklama = d[1] });

            listeDegiskenler.ItemsSource = liste;
            txtFonksiyonlar.Text = "Fonksiyonlar: " + Formul.FonksiyonListesi();
        }

        // ================= SUTUN DUGMELERI =================

        private static SutunSatiri Satir(object sender)
        {
            var oge = sender as FrameworkElement;
            return oge == null ? null : oge.DataContext as SutunSatiri;
        }

        private void btnYukari_Click(object sender, RoutedEventArgs e)
        {
            SutunSatiri s = Satir(sender);
            int i = s == null ? -1 : _sutunlar.IndexOf(s);

            if (i > 0) _sutunlar.Move(i, i - 1);
        }

        private void btnAsagi_Click(object sender, RoutedEventArgs e)
        {
            SutunSatiri s = Satir(sender);
            int i = s == null ? -1 : _sutunlar.IndexOf(s);

            if (i >= 0 && i < _sutunlar.Count - 1) _sutunlar.Move(i, i + 1);
        }

        private void btnSutunSil_Click(object sender, RoutedEventArgs e)
        {
            SutunSatiri s = Satir(sender);
            if (s == null || !s.Ozel) return;

            if (!OnayWindow.Sor(this, "Sütunu Sil",
                    "\"" + s.Baslik + "\" sütunu silinecek. Formülü de kaybolur.", "Sil"))
                return;

            _sutunlar.Remove(s);
        }

        private void btnSutunEkle_Click(object sender, RoutedEventArgs e)
        {
            string anahtar = YeniAnahtar();

            _sutunlar.Add(new SutunSatiri(new SutunTanimi
            {
                Anahtar = anahtar,
                Baslik = "Yeni Sütun",
                Tur = SutunTuru.Ozel,
                Ondalik = 2,
                Genislik = 95,
                Formul = "toplamMaliyet"
            }));

            txtDurum.Text = "";
        }

        private string YeniAnahtar()
        {
            for (int i = 1; ; i++)
            {
                string aday = "ozel" + i;
                bool kullanilmis = false;

                foreach (SutunSatiri s in _sutunlar)
                    if (string.Equals(s.Anahtar, aday, StringComparison.OrdinalIgnoreCase))
                    {
                        kullanilmis = true;
                        break;
                    }

                if (!kullanilmis) return aday;
            }
        }

        // ================= PARAMETRE DUGMELERI =================

        private void btnParametreEkle_Click(object sender, RoutedEventArgs e)
        {
            int sira = _parametreler.Count + 1;

            _parametreler.Add(new ParametreSatiri(new ParametreTanimi
            {
                Anahtar = "parametre" + sira,
                Ad = "Yeni Parametre",
                Birim = "",
                Deger = 0
            }));

            txtDurum.Text = "";
        }

        private void btnParametreSil_Click(object sender, RoutedEventArgs e)
        {
            var oge = sender as FrameworkElement;
            var p = oge == null ? null : oge.DataContext as ParametreSatiri;

            if (p != null) _parametreler.Remove(p);
        }

        // ================= VARSAYILAN / KAYDET =================

        private void btnVarsayilan_Click(object sender, RoutedEventArgs e)
        {
            if (!OnayWindow.Sor(this, "Varsayılanlara Dön",
                    "Sütun düzeni sıfırlanacak; eklediğiniz sütunlar ve parametreler silinecek.",
                    "Sıfırla"))
                return;

            Yukle(TabloDeposu.Varsayilanlar(), new List<ParametreTanimi>());
            txtDurum.Text = "";
        }

        private void btnKaydet_Click(object sender, RoutedEventArgs e)
        {
            var kullanilanAdlar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] d in TabloDeposu.HazirDegiskenler) kullanilanAdlar.Add(d[0]);

            var parametreler = new List<ParametreTanimi>();

            foreach (ParametreSatiri p in _parametreler)
            {
                string hata;
                ParametreTanimi tanim = p.Tanim(kullanilanAdlar, out hata);

                if (hata != null) { Uyar(hata); return; }

                kullanilanAdlar.Add(tanim.Anahtar);
                parametreler.Add(tanim);
            }

            // Ozel sutun adlari da formullerde kullanilabilir
            foreach (SutunSatiri s in _sutunlar)
            {
                if (!s.Ozel) continue;

                string ad = (s.Anahtar ?? "").Trim();
                if (!GecerliAd(ad)) { Uyar("Geçersiz sütun adı: \"" + ad + "\". Harfle başlamalı, boşluk içermemeli."); return; }
                if (!kullanilanAdlar.Add(ad)) { Uyar("Bu ad zaten kullanılıyor: " + ad); return; }
            }

            var sutunlar = new List<SutunTanimi>();

            foreach (SutunSatiri s in _sutunlar)
            {
                string hata;
                SutunTanimi tanim = s.Tanim(kullanilanAdlar, out hata);

                if (hata != null) { Uyar(hata); return; }

                sutunlar.Add(tanim);
            }

            if (sutunlar.FindAll(s => s.Gorunur).Count == 0)
            {
                Uyar("En az bir sütun görünür olmalı.");
                return;
            }

            TabloDeposu.Kaydet(sutunlar, parametreler);

            DialogResult = true;
            Close();
        }

        private void Uyar(string mesaj)
        {
            txtDurum.Text = mesaj;
            System.Media.SystemSounds.Exclamation.Play();
        }

        internal static bool GecerliAd(string ad)
        {
            if (string.IsNullOrEmpty(ad) || !char.IsLetter(ad[0])) return false;

            foreach (char c in ad)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;

            return true;
        }

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btnVazgec_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    // ================= SATIR MODELLERI =================

    public class SutunSatiri
    {
        internal SutunTanimi Kaynak;

        // Basliktaki {pb} yer tutucusu kullaniciya para birimiyle gosterilir;
        // baslik degistirilmediyse yer tutucu korunur
        private readonly string _gosterilenBaslik;

        internal SutunSatiri(SutunTanimi kaynak)
        {
            Kaynak = kaynak;

            Gorunur = kaynak.Gorunur;
            _gosterilenBaslik = kaynak.GorunenBaslik(Ayarlar.ParaBirimi);
            Baslik = _gosterilenBaslik;
            Anahtar = kaynak.Anahtar;
            Formul = kaynak.Formul;
            Ondalik = kaynak.Ondalik.ToString(CultureInfo.InvariantCulture);
        }

        public bool Gorunur { get; set; }
        public string Baslik { get; set; }
        public string Anahtar { get; set; }
        public string Formul { get; set; }
        public string Ondalik { get; set; }

        internal bool Ozel { get { return Kaynak.Tur == SutunTuru.Ozel; } }

        public string TurEtiketi
        {
            get
            {
                switch (Kaynak.Tur)
                {
                    case SutunTuru.Veri: return "CATIA";
                    case SutunTuru.Hesaplanan: return "Hesaplanan";
                    default: return "Özel";
                }
            }
        }

        public Brush TurRengi
        {
            get
            {
                switch (Kaynak.Tur)
                {
                    case SutunTuru.Veri:
                        return (Brush)Application.Current.FindResource("LogInfoBrush");
                    case SutunTuru.Hesaplanan:
                        return (Brush)Application.Current.FindResource("WarnTextBrush");
                    default:
                        return (Brush)Application.Current.FindResource("LogSuccessBrush");
                }
            }
        }

        // Hazir sutunlarda hesabin nasil yapildigi yazili; ozel sutunda
        // aciklama yerine formul kutusu gosterilir
        public string Aciklama { get { return Kaynak.Formul; } }

        public Visibility AciklamaGorunur
        {
            get { return Ozel ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility FormulGorunur
        {
            get { return Ozel ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility SilGorunur
        {
            get { return Ozel ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility OndalikGorunur
        {
            get { return Kaynak.Metin ? Visibility.Collapsed : Visibility.Visible; }
        }

        internal SutunTanimi Tanim(HashSet<string> bilinenAdlar, out string hata)
        {
            hata = null;

            SutunTanimi t = Kaynak.Kopya();
            t.Gorunur = Gorunur;

            string yazilan = (Baslik ?? "").Trim();
            t.Baslik = yazilan == _gosterilenBaslik ? Kaynak.Baslik : yazilan;

            if (t.Baslik.Length == 0)
            {
                hata = "Sütun başlığı boş olamaz.";
                return null;
            }

            int ondalik;
            if (!int.TryParse((Ondalik ?? "").Trim(), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out ondalik) ||
                ondalik < 0 || ondalik > 6)
            {
                hata = "\"" + t.Baslik + "\" için ondalık basamak 0 ile 6 arasında olmalı.";
                return null;
            }

            t.Ondalik = ondalik;

            if (Ozel)
            {
                t.Anahtar = (Anahtar ?? "").Trim();
                t.Formul = (Formul ?? "").Trim();

                if (t.Formul.Length == 0)
                {
                    hata = "\"" + t.Baslik + "\" sütununun formülü boş.";
                    return null;
                }

                // Tam ad: bu sinifta "Formul" adinda bir ozellik de var
                string formulHatasi;
                if (!Macria.Formul.Gecerli(t.Formul, bilinenAdlar, out formulHatasi))
                {
                    hata = "\"" + t.Baslik + "\" formülü: " + formulHatasi;
                    return null;
                }
            }

            return t;
        }
    }

    public class ParametreSatiri
    {
        internal ParametreTanimi Kaynak;

        internal ParametreSatiri(ParametreTanimi kaynak)
        {
            Kaynak = kaynak;

            Ad = kaynak.Ad;
            Anahtar = kaynak.Anahtar;
            Birim = kaynak.Birim;
            Deger = kaynak.Deger.ToString("0.####", CultureInfo.CurrentCulture);
        }

        public string Ad { get; set; }
        public string Anahtar { get; set; }
        public string Birim { get; set; }
        public string Deger { get; set; }

        internal ParametreTanimi Tanim(HashSet<string> bilinenAdlar, out string hata)
        {
            hata = null;

            var t = new ParametreTanimi
            {
                Ad = (Ad ?? "").Trim(),
                Anahtar = (Anahtar ?? "").Trim(),
                Birim = (Birim ?? "").Trim()
            };

            if (t.Ad.Length == 0)
            {
                hata = "Parametre adı boş olamaz.";
                return null;
            }

            if (!TabloAyarlariWindow.GecerliAd(t.Anahtar))
            {
                hata = "Geçersiz parametre adı: \"" + t.Anahtar + "\". Harfle başlamalı, boşluk içermemeli.";
                return null;
            }

            if (bilinenAdlar.Contains(t.Anahtar))
            {
                hata = "Bu ad zaten kullanılıyor: " + t.Anahtar;
                return null;
            }

            double deger;
            string ham = (Deger ?? "").Trim();

            if (!double.TryParse(ham, NumberStyles.Float, CultureInfo.CurrentCulture, out deger) &&
                !double.TryParse(ham.Replace(',', '.'), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out deger))
            {
                hata = "\"" + t.Ad + "\" için sayı okunamadı: " + ham;
                return null;
            }

            t.Deger = deger;
            return t;
        }
    }
}
