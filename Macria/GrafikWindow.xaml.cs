using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Macria
{
    // Maliyet tablosunun gorsel ozeti. Tablodaki sayilar dogru olsa da uzun
    // listede "agirlik nerede toplanmis, para nereye gidiyor" sorusu
    // okunmuyor; bu pencere onu tek bakista gosterir.
    public partial class GrafikWindow : Window
    {
        private readonly List<GrafikDeger> _kalinlik = new List<GrafikDeger>();
        private readonly List<GrafikDeger> _dagilim = new List<GrafikDeger>();
        private readonly List<GrafikDeger> _pahali = new List<GrafikDeger>();

        private readonly string _paraBirimi;
        private string _ortaMetin = "";

        internal GrafikWindow(IEnumerable<CostRow> satirlar, string paraBirimi)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            _paraBirimi = paraBirimi ?? "";

            Hazirla(satirlar);
        }

        // ================= VERI =================

        private void Hazirla(IEnumerable<CostRow> satirlar)
        {
            var olculen = new List<CostRow>();
            int toplamSatir = 0;

            foreach (CostRow r in satirlar)
            {
                toplamSatir++;
                if (r.ToplamAgirlik.HasValue) olculen.Add(r);
            }

            double toplamAgirlik = 0, malzeme = 0, kesim = 0;

            foreach (CostRow r in olculen)
            {
                toplamAgirlik += r.ToplamAgirlik ?? 0;
                malzeme += r.MalzemeMaliyet ?? 0;
                kesim += r.KesimMaliyet ?? 0;
            }

            double toplamMaliyet = malzeme + kesim;

            txtAltBaslik.Text =
                olculen.Count + " / " + toplamSatir + " Parça Ölçüldü   ·   " +
                GrafikCizer.Bicim(toplamAgirlik, 2) + " kg   ·   " +
                GrafikCizer.Bicim(toplamMaliyet, 2) + " " + _paraBirimi;

            KalinlikVerisi(olculen);
            DagilimVerisi(malzeme, kesim, toplamMaliyet);
            PahaliVerisi(olculen);

            txtDurum.Text = olculen.Count == 0
                ? "Ölçülmüş parça yok — önce CATIA'yı tarayıp hesaplayın."
                : "Grafikler yalnızca ölçülmüş parçaları içerir.";
        }

        // Kalinliga gore toplam agirlik; her cubugun altinda parca adedi
        private void KalinlikVerisi(List<CostRow> satirlar)
        {
            var toplamlar = new Dictionary<double, double>();
            var adetler = new Dictionary<double, int>();

            foreach (CostRow r in satirlar)
            {
                double k = r.Thickness > 0 ? Math.Round(r.Thickness, 2) : 0;

                if (!toplamlar.ContainsKey(k)) { toplamlar[k] = 0; adetler[k] = 0; }

                toplamlar[k] += r.ToplamAgirlik ?? 0;
                adetler[k]++;
            }

            var anahtarlar = new List<double>(toplamlar.Keys);
            anahtarlar.Sort();

            int sira = 0;

            foreach (double k in anahtarlar)
            {
                _kalinlik.Add(new GrafikDeger
                {
                    Etiket = k > 0
                        ? k.ToString("0.##", CultureInfo.CurrentCulture) + " mm"
                        : "Bilinmiyor",
                    AltEtiket = adetler[k] + " parça",
                    Deger = toplamlar[k],
                    Renk = GrafikCizer.SeriFircasi(sira++)
                });
            }
        }

        private void DagilimVerisi(double malzeme, double kesim, double toplam)
        {
            _dagilim.Add(new GrafikDeger
            {
                Etiket = "Malzeme",
                Deger = malzeme,
                Renk = GrafikCizer.SeriFircasi(0)
            });

            _dagilim.Add(new GrafikDeger
            {
                Etiket = "Kesim",
                Deger = kesim,
                Renk = GrafikCizer.SeriFircasi(2)
            });

            _ortaMetin = toplam > 0
                ? GrafikCizer.Bicim(toplam, 0) + " " + _paraBirimi
                : "";
        }

        private void PahaliVerisi(List<CostRow> satirlar)
        {
            var sirali = new List<CostRow>(satirlar);

            sirali.Sort((a, b) =>
                (b.ToplamMaliyet ?? 0).CompareTo(a.ToplamMaliyet ?? 0));

            int adet = Math.Min(10, sirali.Count);

            for (int i = 0; i < adet; i++)
            {
                if ((sirali[i].ToplamMaliyet ?? 0) <= 0) break;

                string ad = sirali[i].PartName;
                if (string.IsNullOrWhiteSpace(ad)) ad = sirali[i].ProductName;

                if (sirali[i].Quantity > 1) ad += "  ×" + sirali[i].Quantity;

                _pahali.Add(new GrafikDeger
                {
                    Etiket = ad,
                    Deger = sirali[i].ToplamMaliyet ?? 0,
                    Renk = GrafikCizer.SeriFircasi(3)
                });
            }

            if (_pahali.Count > 0)
                txtPahaliBaslik.Text = "En Yüksek Maliyetli " + _pahali.Count + " Parça";
        }

        // ================= CIZIM =================

        // Tuvaller pencereyle birlikte buyudugu icin her olcu degisiminde
        // yeniden cizilir; grafikler oranlarini korur.
        private void Tuval_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var tuval = sender as Canvas;
            if (tuval == null) return;

            if (tuval == tuvalKalinlik)
                GrafikCizer.DikeyCubuk(tuval, _kalinlik, "kg", 1);
            else if (tuval == tuvalDagilim)
                GrafikCizer.Halka(tuval, _dagilim, _paraBirimi, 0, _ortaMetin);
            else if (tuval == tuvalPahali)
                GrafikCizer.YatayCubuk(tuval, _pahali, _paraBirimi, 2);
        }

        // ================= GORUNTU =================

        private void btnResim_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int g = (int)Math.Round(pano.ActualWidth);
                int y = (int)Math.Round(pano.ActualHeight);

                if (g < 10 || y < 10)
                {
                    txtDurum.Text = "Grafik alanı çok küçük.";
                    return;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG Görüntüsü|*.png",
                    FileName = "Macria_Grafikler_" +
                               DateTime.Now.ToString("yyyyMMdd_HHmm",
                                                     CultureInfo.InvariantCulture) + ".png"
                };

                if (dlg.ShowDialog() != true) return;

                // 2 kat olcek: sunuma ve baskiya yetecek cozunurluk
                var resim = new RenderTargetBitmap(g * 2, y * 2, 192, 192,
                                                   PixelFormats.Pbgra32);
                resim.Render(pano);

                var kodlayici = new PngBitmapEncoder();
                kodlayici.Frames.Add(BitmapFrame.Create(resim));

                using (var dosya = new FileStream(dlg.FileName, FileMode.Create))
                    kodlayici.Save(dosya);

                txtDurum.Text = "Kaydedildi: " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex)
            {
                txtDurum.Text = "Görüntü kaydedilemedi: " + ex.Message;
            }
        }

        // ================= PENCERE =================

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
