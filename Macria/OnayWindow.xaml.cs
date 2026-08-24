using System.Windows;
using System.Windows.Input;

namespace Macria
{
    // Tema ile uyumlu kucuk evet/hayir penceresi. Uygulama sistem
    // MessageBox'ini kullanmadigi icin onaylar buradan gecer.
    public partial class OnayWindow : Window
    {
        private OnayWindow(string baslik, string mesaj, string onayMetni, string redMetni)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            txtBaslik.Text = baslik;
            txtMesaj.Text = mesaj;
            btnOnay.Content = onayMetni;
            btnVazgec.Content = redMetni;

            // Windows'un uyari sesi: pencere gorunurken calsin
            Loaded += (s, e) => System.Media.SystemSounds.Exclamation.Play();
        }

        public static bool Sor(Window sahip, string baslik, string mesaj,
                               string onayMetni, string redMetni = "Vazgeç")
        {
            var pencere = new OnayWindow(baslik, mesaj, onayMetni, redMetni) { Owner = sahip };
            return pencere.ShowDialog() == true;
        }

        private void btnOnay_Click(object sender, RoutedEventArgs e)
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
