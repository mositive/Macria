using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Macria
{
    // Save As konumunu ogretme rehberi. Kullanicinin cektigi dort ekran
    // goruntusuyle adim adim ilerler; son adimda ogretmeyi baslatir.
    public partial class TutorialWindow : Window
    {
        // Rehber bitiminde MainWindow ayarlar penceresini ogretme modunda acar
        public bool OgretmeIstendi { get; private set; }

        private class Adim
        {
            public string Baslik;
            public string Aciklama;
            public string Gorsel;
        }

        private static readonly Adim[] Adimlar =
        {
            new Adim
            {
                Baslik = "Sac Parçayı Seçin",
                Aciklama = "CATIA'da montaj ağacından herhangi bir sac parçayı (3D Shape) " +
                           "iki defa tıklayıp seçin.",
                Gorsel = "tutorial-1.png"
            },
            new Adim
            {
                Baslik = "Araçlar Sekmesine Geçin",
                Aciklama = "Ekranın altındaki araç çubuğunda \"Araçlar\" sekmesine tıklayın. " +
                           "DXF komutu bu sekmenin içinde bulunur. Eğer DXF komutu yoksa, lütfen \"Sheet Metal Design\" modunda olduğunuza emin olun. ",
                Gorsel = "tutorial-2.png"
            },
            new Adim
            {
                Baslik = "Save As DXF Komutunu Açın",
                Aciklama = "Araçlar sekmesindeki \"Save As DXF\" simgesine tıklayın. " +
                           "Save as Dxf paneli açılacaktır.",
                Gorsel = "tutorial-3.png"
            },
            new Adim
            {
                Baslik = "Fareyi Save As Üzerine Getirin  ve F8'e Basın",
                Aciklama = "Aşağıdaki \"Öğretmeye Başla\" düğmesine bastıktan sonra CATIA'ya geçin, " +
                           "fare imlecini paneldeki \"Save As\" düğmesinin ÜZERİNE getirin (BUTONA TIKLAMAYIN! SADECE İMLECİ BUTON ÜZERİNE GETİRİN) ve F8'e basın. " +
                           "Macria konumu kaydeder; bundan sonra her export'ta oraya kendisi tıklar. Bu işlemin tek seferlik olduğunu lütfen unutmayın.d",
                Gorsel = "tutorial-4.png"
            }
        };

        private int _adim;

        public TutorialWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            NoktalariKur();
            AdimiGoster(0);
        }

        private void NoktalariKur()
        {
            for (int i = 0; i < Adimlar.Length; i++)
                pnlNoktalar.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(4, 0, 4, 0),
                    Fill = (Brush)FindResource("TextDisabledBrush")
                });
        }

        private void AdimiGoster(int no)
        {
            _adim = no;
            Adim a = Adimlar[no];

            txtAdimNo.Text = (no + 1).ToString();
            txtAdimBaslik.Text = a.Baslik;
            txtAdimAciklama.Text = a.Aciklama;

            imgAdim.Source = new BitmapImage(
                new Uri("pack://application:,,,/Assets/" + a.Gorsel));

            for (int i = 0; i < pnlNoktalar.Children.Count; i++)
                ((Ellipse)pnlNoktalar.Children[i]).Fill = (Brush)FindResource(
                    i == no ? "AccentBrush" : "TextDisabledBrush");

            bool son = no == Adimlar.Length - 1;

            btnGeri.IsEnabled = no > 0;
            btnIleri.Visibility = son ? Visibility.Collapsed : Visibility.Visible;
            btnOgretmeyeBasla.Visibility = son ? Visibility.Visible : Visibility.Collapsed;
        }

        private void btnGeri_Click(object sender, RoutedEventArgs e)
        {
            if (_adim > 0) AdimiGoster(_adim - 1);
        }

        private void btnIleri_Click(object sender, RoutedEventArgs e)
        {
            if (_adim < Adimlar.Length - 1) AdimiGoster(_adim + 1);
        }

        private void btnOgretmeyeBasla_Click(object sender, RoutedEventArgs e)
        {
            OgretmeIstendi = true;
            Close();
        }

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btnKapat_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
