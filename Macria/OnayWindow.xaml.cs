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

        /// <summary>
        /// Several choices (e.g. Kaydet / Kaydetme / İptal): returns the index of
        /// the chosen one, or -1 when the window is closed or Esc is pressed.
        /// The last choice is the highlighted default.
        /// </summary>
        public static int Sec(Window sahip, string baslik, string mesaj, params string[] secenekler)
        {
            var pencere = new OnayWindow(baslik, mesaj, "", "") { Owner = sahip };
            var panel = (System.Windows.Controls.Panel)pencere.btnOnay.Parent;
            panel.Children.Clear();
            int secilen = -1;
            for (int i = 0; i < secenekler.Length; i++)
            {
                int indeks = i;
                bool son = i == secenekler.Length - 1;
                var dugme = new System.Windows.Controls.Button
                {
                    Content = secenekler[i],
                    Height = 32,
                    Padding = new Thickness(16, 0, 16, 0),
                    Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                    IsDefault = son,
                    Style = (Style)pencere.FindResource(son ? "PrimaryButton" : "SecondaryButton")
                };
                dugme.Click += (s, e) => { secilen = indeks; pencere.DialogResult = true; };
                panel.Children.Add(dugme);
            }
            return pencere.ShowDialog() == true ? secilen : -1;
        }

        /// <summary>
        /// A notice with one button and a check box under the message (e.g.
        /// "Bu proje için bir daha gösterme"); returns whether it was ticked.
        /// </summary>
        public static bool Bilgi(Window sahip, string baslik, string mesaj, string kutuMetni, string tamamMetni = "Tamam")
        {
            var pencere = new OnayWindow(baslik, mesaj, tamamMetni, "") { Owner = sahip };
            pencere.btnVazgec.Visibility = Visibility.Collapsed;
            pencere.btnOnay.IsDefault = true;
            var izgara = (System.Windows.Controls.Grid)pencere.txtMesaj.Parent;
            int sira = izgara.Children.IndexOf(pencere.txtMesaj);
            izgara.Children.Remove(pencere.txtMesaj);
            var panel = new System.Windows.Controls.StackPanel { Margin = pencere.txtMesaj.Margin };
            System.Windows.Controls.Grid.SetRow(panel, 1);
            pencere.txtMesaj.Margin = new Thickness(0);
            var kutu = new System.Windows.Controls.CheckBox { Content = kutuMetni, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(pencere.txtMesaj);
            panel.Children.Add(kutu);
            izgara.Children.Insert(sira, panel);
            OtomasyonModu.Gizle(pencere);
            pencere.ShowDialog();
            return kutu.IsChecked == true;
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
