using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Macria
{
    // Motor DXF'i aktarilirken hedefte ayni adli dosya varsa sorulur.
    // Varsayilan (Enter) Atla; pencereyi kapatmak ya da Esc Iptal demektir.
    public partial class DxfCakismaWindow : Window
    {
        private DxfCakismaSecimi _secim = DxfCakismaSecimi.Iptal;

        private DxfCakismaWindow(string hedef)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);
            txtMesaj.Text = "Hedef klasörde aynı adlı bir DXF zaten var:\n\n" + Path.GetFileName(hedef) +
                            "\n\n" + Path.GetDirectoryName(hedef) +
                            "\n\nÜzerine yazılsın mı, yoksa bu dosya atlansın mı?";
            Loaded += (s, e) => System.Media.SystemSounds.Exclamation.Play();
        }

        public static (DxfCakismaSecimi Secim, bool Tumune) Sor(Window sahip, string hedef)
        {
            var pencere = new DxfCakismaWindow(hedef) { Owner = sahip };
            pencere.ShowDialog();
            return (pencere._secim, pencere.chkTumune.IsChecked == true);
        }

        private void btnAtla_Click(object sender, RoutedEventArgs e) => Kapat(DxfCakismaSecimi.Atla);

        private void btnUzerineYaz_Click(object sender, RoutedEventArgs e) => Kapat(DxfCakismaSecimi.UzerineYaz);

        private void btnIptal_Click(object sender, RoutedEventArgs e) => Kapat(DxfCakismaSecimi.Iptal);

        private void Kapat(DxfCakismaSecimi secim)
        {
            _secim = secim;
            Close();
        }

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
