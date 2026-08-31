using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Macria
{
    // Onizlemenin genis gorunumu. Konsol penceresi gibi ana pencereden
    // beslenir: listede secim degistikce MainWindow buraya yeni cizimi
    // yazar, pencere kendi basina dosya okumaz.
    //
    // Kucuk panelden farki, cizimin yakinlastirilip kaydirilabilmesi;
    // delik ve kucuk kertikler ancak boyle gorulur.
    public partial class OnizlemeWindow : Window
    {
        private const double EnAz = 0.25;
        private const double EnCok = 32.0;

        private string _yol = "";

        private bool _surukleniyor;
        private Point _basildigiNokta;
        private double _basXKaydir;
        private double _basYKaydir;

        public OnizlemeWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            Sigdir();
        }

        // ================= ANA PENCEREDEN GELENLER =================

        public void Goster(string parca, Geometry sekil, string olcu, string dosyaYolu)
        {
            txtParca.Text = string.IsNullOrEmpty(parca) ? "—" : parca;
            _yol = dosyaYolu ?? "";

            txtDosya.Text = _yol;
            txtDosya.ToolTip = _yol.Length > 0 ? _yol : null;
            btnAc.IsEnabled = _yol.Length > 0;

            cizim.Data = sekil;
            cizim.Visibility = Visibility.Visible;
            txtMesaj.Visibility = Visibility.Collapsed;

            txtOlcu.Text = olcu ?? "";

            // Yeni parca gelince eski yakinlastirma anlamini yitirir
            Sigdir();
        }

        public void Bosalt(string parca, string mesaj, string dosyaYolu)
        {
            txtParca.Text = string.IsNullOrEmpty(parca) ? "—" : parca;
            _yol = dosyaYolu ?? "";

            txtDosya.Text = _yol;
            txtDosya.ToolTip = _yol.Length > 0 ? _yol : null;
            btnAc.IsEnabled = _yol.Length > 0;

            cizim.Data = null;
            cizim.Visibility = Visibility.Collapsed;

            txtMesaj.Visibility = Visibility.Visible;
            txtMesaj.Text = mesaj;

            txtOlcu.Text = "";
            Sigdir();
        }

        // ================= YAKINLASTIRMA =================

        private void Sigdir()
        {
            olcek.ScaleX = 1;
            olcek.ScaleY = 1;
            kaydir.X = 0;
            kaydir.Y = 0;

            CizgiKalinligi();
            YakinlikYaz();
        }

        // Cizgi ekranda hep ayni incelikte kalsin diye donusum tersine cevrilir
        private void CizgiKalinligi()
        {
            cizim.StrokeThickness = 1.0 / olcek.ScaleX;
        }

        private void YakinlikYaz()
        {
            txtYakinlik.Text = "%" + Math.Round(olcek.ScaleX * 100).ToString("0");
        }

        private void cizimAlani_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (cizim.Visibility != Visibility.Visible) return;

            double eski = olcek.ScaleX;
            double yeni = eski * Math.Pow(1.15, e.Delta / 120.0);

            if (yeni < EnAz) yeni = EnAz;
            if (yeni > EnCok) yeni = EnCok;

            double k = yeni / eski;
            if (Math.Abs(k - 1) < 1e-9) return;

            // Farenin altindaki nokta yerinde kalsin: donusumun merkezi
            // katmanin ortasi oldugu icin kaydirma da ayni oranda tasinir
            Point m = e.GetPosition(cizimAlani);
            double cx = cizimAlani.ActualWidth / 2;
            double cy = cizimAlani.ActualHeight / 2;

            kaydir.X = (m.X - cx) * (1 - k) + kaydir.X * k;
            kaydir.Y = (m.Y - cy) * (1 - k) + kaydir.Y * k;

            olcek.ScaleX = yeni;
            olcek.ScaleY = yeni;

            CizgiKalinligi();
            YakinlikYaz();

            e.Handled = true;
        }

        private void cizimAlani_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (cizim.Visibility != Visibility.Visible) return;

            // Cift tiklama sigdirmaya doner
            if (e.ClickCount == 2)
            {
                Sigdir();
                return;
            }

            _surukleniyor = true;
            _basildigiNokta = e.GetPosition(cizimAlani);
            _basXKaydir = kaydir.X;
            _basYKaydir = kaydir.Y;

            cizimAlani.CaptureMouse();
            cizimAlani.Cursor = Cursors.SizeAll;
        }

        private void cizimAlani_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_surukleniyor) return;

            Point simdi = e.GetPosition(cizimAlani);

            kaydir.X = _basXKaydir + (simdi.X - _basildigiNokta.X);
            kaydir.Y = _basYKaydir + (simdi.Y - _basildigiNokta.Y);
        }

        private void cizimAlani_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_surukleniyor) return;

            _surukleniyor = false;
            cizimAlani.ReleaseMouseCapture();
            cizimAlani.Cursor = null;
        }

        private void btnSigdir_Click(object sender, RoutedEventArgs e)
        {
            Sigdir();
        }

        // ================= PENCERE =================

        private void btnAc_Click(object sender, RoutedEventArgs e)
        {
            if (_yol.Length == 0) return;

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_yol) { UseShellExecute = true });
            }
            catch { }
        }

        private void btnMin_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void btnMax_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
