using System.Windows;
using System.Windows.Input;

namespace Macria
{
    // Toplu export baslamadan once cikan bilgilendirme. Export sirasinda
    // fare imleci ogretilmis konuma tikladigi icin kullanicinin fareye
    // dokunmamasi gerekir; "bir daha gosterme" secimi profile yazilir.
    public partial class FareUyariWindow : Window
    {
        public bool BirDahaGosterme
        {
            get { return chkGizle.IsChecked == true; }
        }

        public FareUyariWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            // Windows'un uyari sesi: pencere gorunurken calsin
            Loaded += (s, e) => System.Media.SystemSounds.Exclamation.Play();
        }

        private void btnBaslat_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void btnVazgec_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) DialogResult = false;
            base.OnKeyDown(e);
        }
    }
}
