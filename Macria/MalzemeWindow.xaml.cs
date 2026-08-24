using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Macria
{
    // Ozel malzeme ekleme penceresi. Gecerli ad ve yogunluk girilirse
    // sonuc Ad/Yogunluk ozelliklerinde birakilir, DialogResult true doner.
    public partial class MalzemeWindow : Window
    {
        public string MalzemeAd { get; private set; } = "";
        public double MalzemeYogunluk { get; private set; }

        public MalzemeWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            Loaded += (s, e) => txtAd.Focus();
        }

        private void btnEkle_Click(object sender, RoutedEventArgs e)
        {
            string ad = (txtAd.Text ?? "").Trim();

            double yogunluk;
            string sayi = (txtYogunlukYeni.Text ?? "").Trim();

            bool sayiOk =
                double.TryParse(sayi, NumberStyles.Float, CultureInfo.CurrentCulture, out yogunluk) ||
                double.TryParse(sayi.Replace(',', '.'), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out yogunluk);

            if (ad.Length == 0)
            {
                Hata("Malzeme adı boş olamaz.");
                return;
            }

            if (MalzemeDeposu.VarsayilanAdi(ad))
            {
                Hata("Bu ad hazır malzemelerde zaten var. Farklı bir ad kullanın.");
                return;
            }

            if (!sayiOk || yogunluk <= 0 || yogunluk > 25)
            {
                Hata("Geçerli bir yoğunluk girin (örn. 7,85).");
                return;
            }

            MalzemeAd = ad;
            MalzemeYogunluk = yogunluk;

            DialogResult = true;
            Close();
        }

        private void Hata(string mesaj)
        {
            txtHata.Text = mesaj;
            txtHata.Visibility = Visibility.Visible;
        }

        private void SadeceOndalik(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if ((c < '0' || c > '9') && c != ',' && c != '.') { e.Handled = true; return; }
        }

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btnVazgec_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
