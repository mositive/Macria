using System.Windows;
using System.Windows.Input;

namespace Macria
{
    // Export baslamadan once cikan bilgilendirme.
    //
    // Fare imleci kendi kendine hareket edip tikladigi icin ekran, bilgisayar
    // ele gecirilmis gibi gorunuyor. Once bunun ne oldugu anlatilir, sonra
    // fareye dokunmama uyarisi verilir. "Bir daha gosterme" secimi profile
    // yazilir; varsayilan olarak isaretsizdir.
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

            // Bilgi sesi; uyari sesi "bir sey ters gitti" izlenimi verirdi
            Loaded += (s, e) => System.Media.SystemSounds.Asterisk.Play();
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
