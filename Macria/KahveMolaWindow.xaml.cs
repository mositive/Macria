using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Macria
{
    // Toplu DXF boyunca teknik PiP'in ustunde duran esprili durum penceresi.
    // Odak almaz ve fare tiklarini alttaki pencereye aktarir; CATIA'nin
    // klavye/fare otomasyonuna karismaz.
    public partial class KahveMolaWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const double ArkadaslarDuragi = 417;

        private enum YuruyusAsamasi
        {
            ArkadaslaraYuruyor,
            ArkadaslardaBekliyor,
            BasaDonuyor
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly Window? _altPencere;
        private readonly ImageSource[] _yuruyusKareleri = new ImageSource[8];
        private readonly Border[] _ilerlemeBarlari;
        private DispatcherTimer? _yuruyusZamani;
        private int _kareIndeksi;
        private int _zamanAdimi;
        private int _asamaBekleme;
        private YuruyusAsamasi _yuruyusAsamasi = YuruyusAsamasi.ArkadaslaraYuruyor;
        private bool _tamamlandi;

        public KahveMolaWindow(Window? altPencere)
        {
            _altPencere = altPencere;
            InitializeComponent();

            _ilerlemeBarlari = new[] { bar1, bar2, bar3, bar4 };

            for (int i = 0; i < _yuruyusKareleri.Length; i++)
                _yuruyusKareleri[i] = YuruyusKaresiniYukle(i + 1);

            calisanKare.Source = _yuruyusKareleri[0];

            if (_altPencere != null)
            {
                _altPencere.LocationChanged += AltPencereDegisti;
                _altPencere.SizeChanged += AltPencereDegisti;
            }

            Loaded += (s, e) =>
            {
                AltPencereninUstuneYerlestir();
                AnimasyonuBaslat();
            };

            SizeChanged += (s, e) => AltPencereninUstuneYerlestir();
            Closed += (s, e) =>
            {
                AnimasyonuDurdur();

                if (_altPencere == null) return;
                _altPencere.LocationChanged -= AltPencereDegisti;
                _altPencere.SizeChanged -= AltPencereDegisti;
            };
        }

        private static ImageSource YuruyusKaresiniYukle(int sira)
        {
            var uri = new Uri(
                "pack://application:,,,/Assets/coffee-walk-" +
                sira.ToString("00") + ".png",
                UriKind.Absolute);

            var resim = new BitmapImage();
            resim.BeginInit();
            resim.UriSource = uri;
            resim.CacheOption = BitmapCacheOption.OnLoad;
            resim.EndInit();
            resim.Freeze();
            return resim;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE,
                GetWindowLong(h, GWL_EXSTYLE) |
                WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        }

        private void AltPencereDegisti(object? sender, EventArgs e)
        {
            AltPencereninUstuneYerlestir();
        }

        private void AltPencereninUstuneYerlestir()
        {
            var alan = SystemParameters.WorkArea;
            Left = Math.Max(alan.Left + 16, alan.Right - ActualWidth - 16);

            double altUst = _altPencere != null && _altPencere.IsVisible
                ? _altPencere.Top
                : alan.Bottom - 16;

            Top = Math.Max(alan.Top + 16, altUst - ActualHeight - 10);
        }

        public void IlerlemeyiGoster(int sira, int toplam, string ogeAdi)
        {
            if (_tamamlandi) return;

            double oran = toplam > 0
                ? Math.Max(0, Math.Min(1, (double)sira / toplam))
                : 0;
            BarlariGuncelle(oran);

            if (sira <= 0)
            {
                txtDurum.Text = toplam + " öğe işleme hazırlanıyor...";
                return;
            }

            string ad = string.IsNullOrWhiteSpace(ogeAdi) ? "Öğe" : ogeAdi.Trim();
            txtDurum.Text = sira + " / " + toplam + " işleniyor  ·  " + ad;
        }

        public void Tamamlandi(int basarili, int kontrolBekleyen)
        {
            _tamamlandi = true;
            AnimasyonuDurdur();
            BarlariGuncelle(1);

            txtBaslik.Text = "Kahven bittiyse işlem tamam!";
            txtBaslik.Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xF0, 0xC5));
            txtTamamIkonu.Visibility = Visibility.Visible;

            txtMesajBir.Text = basarili + " öğe başarıyla işlendi, " +
                              kontrolBekleyen + " öğe kontrol bekliyor.";
            txtMesajIki.Text = kontrolBekleyen > 0
                ? "Kontrol gereken öğeleri Durum ve Not sütunlarından inceleyebilirsin."
                : "Her şey hazır. Kahven soğumadan dönebilirsin. ☕";

            txtDurum.Text = kontrolBekleyen > 0
                ? "Tamamlandı  ·  " + kontrolBekleyen + " öğe kontrol bekliyor"
                : "Tamamlandı  ·  Tüm öğeler başarıyla işlendi";

            // Tam ekran fotoğraf açılmaz. Sonuç görünürken Enes, aynı sahnede
            // arkadaşlarının yanındaki doğal bitiş konumunda bekler.
            calisanKare.Source = _yuruyusKareleri[2];
            calisanKare.Opacity = 1;
            calisanKay.X = ArkadaslarDuragi;
            kart.BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0xD6, 0x99));
        }

        public void Durduruldu(int basarili, int kontrolBekleyen)
        {
            _tamamlandi = true;
            AnimasyonuDurdur();

            txtBaslik.Text = "Kahve molası yarıda kaldı!";
            txtBaslik.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x68));
            txtMesajBir.Text = "İşlem kullanıcı tarafından durduruldu.";
            txtMesajIki.Text = basarili + " öğe işlendi, " +
                               kontrolBekleyen + " öğe tamamlanmayı bekliyor.";
            txtDurum.Text = "Durduruldu";

        }

        private void BarlariGuncelle(double oran)
        {
            int dolu = oran <= 0 ? 0 : (int)Math.Ceiling(oran * _ilerlemeBarlari.Length);
            var yesil = new SolidColorBrush(Color.FromRgb(0x80, 0xC9, 0x8C));
            var bos = new SolidColorBrush(Color.FromRgb(0x72, 0x78, 0x76));

            for (int i = 0; i < _ilerlemeBarlari.Length; i++)
                _ilerlemeBarlari[i].Background = i < dolu ? yesil : bos;
        }

        private void AnimasyonuBaslat()
        {
            if (_tamamlandi) return;

            if (_yuruyusZamani == null)
            {
                _yuruyusZamani = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                _yuruyusZamani.Tick += YuruyusAdimi;
            }

            _yuruyusZamani.Start();
        }

        private void YuruyusAdimi(object? sender, EventArgs e)
        {
            if (_tamamlandi) return;

            // Ekibin yanında kısa süre selam verirken karakter sabit kalır.
            if (_asamaBekleme > 0)
            {
                _asamaBekleme--;
                if (_asamaBekleme == 0) BeklemeAsamasiniBitir();

                return;
            }

            _zamanAdimi++;

            // Her iki konum adımında bir gerçek yürüyüş karesine geçilir.
            // Konum daha sık güncellendiği için karakter takılmadan ilerler.
            if (_zamanAdimi % 2 == 0)
            {
                _kareIndeksi = (_kareIndeksi + 1) % _yuruyusKareleri.Length;
                calisanKare.Source = _yuruyusKareleri[_kareIndeksi];
            }

            // Temas karelerinde biraz daha kısa, geçiş karelerinde biraz daha
            // uzun yol alınır; bu da ayak kayması hissini azaltır.
            double[] adimlar = { 3.8, 4.5, 5.1, 4.6, 3.8, 4.5, 5.1, 4.6 };
            calisanKay.X = Math.Min(ArkadaslarDuragi,
                calisanKay.X + adimlar[_kareIndeksi]);

            if (calisanKay.X < ArkadaslarDuragi) return;

            calisanKare.Source = _yuruyusKareleri[2];

            if (_yuruyusAsamasi == YuruyusAsamasi.ArkadaslaraYuruyor)
            {
                _yuruyusAsamasi = YuruyusAsamasi.ArkadaslardaBekliyor;
                _asamaBekleme = 18;
            }
        }

        private void BeklemeAsamasiniBitir()
        {
            if (_yuruyusAsamasi == YuruyusAsamasi.ArkadaslardaBekliyor)
            {
                _yuruyusAsamasi = YuruyusAsamasi.BasaDonuyor;
                _asamaBekleme = 5;
                calisanKare.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)));
                return;
            }

            if (_yuruyusAsamasi != YuruyusAsamasi.BasaDonuyor) return;

            // Uzun süren aktarımlarda yolculuk görünmeden masanın başına alınır
            // ve ofis-ekip döngüsü yumuşakça yeniden başlar.
            calisanKare.BeginAnimation(OpacityProperty, null);
            calisanKare.Opacity = 0;
            calisanKay.X = 0;
            _kareIndeksi = 0;
            _zamanAdimi = 0;
            _yuruyusAsamasi = YuruyusAsamasi.ArkadaslaraYuruyor;
            calisanKare.Source = _yuruyusKareleri[0];

            calisanKare.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                {
                    FillBehavior = FillBehavior.Stop
                });
            calisanKare.Opacity = 1;
        }

        private void AnimasyonuDurdur()
        {
            if (_yuruyusZamani != null) _yuruyusZamani.Stop();

            calisanKare.BeginAnimation(OpacityProperty, null);
        }
    }
}
