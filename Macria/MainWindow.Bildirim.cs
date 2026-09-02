using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Macria
{
    // Kose bildirimleri.
    //
    // Uyarilarin cogu konsola yaziliyor ama konsol her zaman acik degil; acik
    // olmadiginda kullanici olan biteni kaciriyordu. Bu yuzden her konsol
    // satiri, konsol kapaliyken sag alt kosede kisa sureli bir kart olarak da
    // gosteriliyor.
    //
    // Konsol acikken hic gosterilmez: ayni sey iki yerde durmasin.
    public partial class MainWindow
    {
        // Ayni anda en fazla bu kadar kart durur; fazlasi ekrani kaplardi
        private const int EnCokKart = 3;

        private sealed class Bildirim
        {
            public Border Kart;
            public TextBlock Yazi;
            public TextBlock Sayac;
            public DispatcherTimer Zaman;
            public string Metin = "";
            public int Tekrar = 1;
        }

        private readonly List<Bildirim> _bildirimler = new List<Bildirim>();

        // Onem derecesine gore ekranda kalma suresi: hata daha uzun durur
        private static TimeSpan Sure(string fircaAnahtari)
        {
            switch (fircaAnahtari)
            {
                case "LogErrorBrush": return TimeSpan.FromSeconds(8);
                case "LogSuccessBrush": return TimeSpan.FromSeconds(4.5);
                default: return TimeSpan.FromSeconds(3);
            }
        }

        private void Bildir(string metin, string fircaAnahtari)
        {
            // Konsol aciksa satir zaten gorunuyor
            if (Ayarlar.KonsolAcik) return;

            // Export sirasindaki kucuk pencere de son satiri gosteriyor;
            // ayni bilgi ucuncu kez cikmasin
            if (_pip != null) return;

            if (bildirimKatmani == null) return;

            // Ust uste gelen ayni mesaj yeni kart acmaz, sayaci artar
            if (_bildirimler.Count > 0)
            {
                Bildirim son = _bildirimler[_bildirimler.Count - 1];

                if (son.Metin == metin)
                {
                    son.Tekrar++;
                    son.Sayac.Text = "×" + son.Tekrar;
                    son.Sayac.Visibility = Visibility.Visible;

                    SureyiBaslat(son, Sure(fircaAnahtari));
                    return;
                }
            }

            while (_bildirimler.Count >= EnCokKart) Kaldir(_bildirimler[0], false);

            Bildirim b = Kart(metin, fircaAnahtari);

            _bildirimler.Add(b);
            bildirimKatmani.Children.Add(b.Kart);

            Belir(b.Kart);
            SureyiBaslat(b, Sure(fircaAnahtari));
        }

        private Bildirim Kart(string metin, string fircaAnahtari)
        {
            Brush vurgu = (Brush)FindResource(fircaAnahtari);

            var b = new Bildirim { Metin = metin };

            b.Yazi = new TextBlock
            {
                Text = metin,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };

            b.Sayac = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = vurgu,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            // Soldaki renk seridi mesajin turunu gosterir
            var serit = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Background = vurgu,
                Margin = new Thickness(0, 0, 11, 0)
            };

            var icerik = new DockPanel();

            DockPanel.SetDock(serit, Dock.Left);
            DockPanel.SetDock(b.Sayac, Dock.Right);

            icerik.Children.Add(serit);
            icerik.Children.Add(b.Sayac);
            icerik.Children.Add(b.Yazi);

            b.Kart = new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 8, 0, 0),
                Child = icerik,
                Cursor = Cursors.Hand,
                ToolTip = "Konsolu Aç"
            };

            // Karta tiklamak konsolu acar: tam kaydi gormek isteyen oraya bakar
            b.Kart.MouseLeftButtonUp += (s, e) =>
            {
                Kaldir(b, false);

                if (!Ayarlar.KonsolAcik)
                {
                    Ayarlar.KonsolAcik = true;
                    Ayarlar.Kaydet();
                    KonsoluUygula();

                    // Konsol acildi, bekleyen kartlarin isi bitti
                    TumBildirimleriKapat();
                }
            };

            return b;
        }

        private void SureyiBaslat(Bildirim b, TimeSpan sure)
        {
            if (b.Zaman != null) b.Zaman.Stop();
            else
            {
                b.Zaman = new DispatcherTimer();
                b.Zaman.Tick += (s, e) => Kaldir(b, true);
            }

            b.Zaman.Interval = sure;
            b.Zaman.Start();
        }

        // Kayarak ve belirerek girer
        private static void Belir(UIElement oge)
        {
            var kaydir = new TranslateTransform();
            oge.RenderTransform = kaydir;

            var sure = new Duration(TimeSpan.FromMilliseconds(180));

            oge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, sure));

            kaydir.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(24, 0, sure)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void Kaldir(Bildirim b, bool solarak)
        {
            if (b == null || !_bildirimler.Contains(b)) return;

            if (b.Zaman != null) { b.Zaman.Stop(); b.Zaman = null; }

            _bildirimler.Remove(b);

            if (!solarak)
            {
                bildirimKatmani.Children.Remove(b.Kart);
                return;
            }

            var solma = new DoubleAnimation(b.Kart.Opacity, 0,
                new Duration(TimeSpan.FromMilliseconds(200)));

            solma.Completed += (s, e) => bildirimKatmani.Children.Remove(b.Kart);

            b.Kart.BeginAnimation(OpacityProperty, solma);
        }

        private void TumBildirimleriKapat()
        {
            for (int i = _bildirimler.Count - 1; i >= 0; i--)
                Kaldir(_bildirimler[i], false);
        }
    }
}
