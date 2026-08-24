using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Macria
{
    // Raporu A4 yatay PDF olarak yazar.
    //
    // Sayfalar once WPF ile cizilir, sonra kayipsiz (Flate) goruntu olarak
    // PDF'e gomulur. Bunun sebebi yazi tipi: PDF'in hazir yazi tipleri
    // "ğ, ı, ş" harflerini tasimaz; sayfayi Windows'un kendi yazi tipiyle
    // cizip gomunce Turkce metin oldugu gibi cikar.
    internal static class PdfYazici
    {
        // A4 yatay: 841.89 x 595.28 punto  =  1122.5 x 793.7 piksel (96 dpi)
        private const double SayfaG = 1122.5;
        private const double SayfaY = 793.7;
        private const double PuntoG = 841.89;
        private const double PuntoY = 595.28;

        private const double Kenar = 40;
        private const double BantY = 74;        // ilk sayfadaki baslik bandi
        private const double BantYDevam = 44;
        private const double SatirY = 21;
        private const double BaslikSatirY = 36;
        private const double AltBilgiY = 30;

        // Cikti cozunurlugu (2 = 192 dpi)
        private const int Olcek = 2;

        private static readonly FontFamily Yazi = new FontFamily("Segoe UI");

        private static readonly Brush Koyu = Firca("#22242A");     // baslik bandi
        private static readonly Brush Metin = Firca("#16181D");
        private static readonly Brush Soluk = Firca("#5A6068");
        private static readonly Brush BantSoluk = Firca("#A8ADB6");
        private static readonly Brush Cizgi = Firca("#E2E5E9");
        private static readonly Brush Zebra = Firca("#F8F9FB");
        private static readonly Brush CipZemin = Firca("#F1F3F7");
        private static readonly Brush Vurgu = Firca("#2E64AC");
        private static readonly Brush VurguSolgun = Firca("#EFF3FA");
        private static readonly Brush Yesil = Firca("#1D6F42");
        private static readonly Brush Kirmizi = Firca("#B0322A");
        private static readonly Brush Beyaz = Brushes.White;

        // Ozet kutularinin zemin ve yazi renkleri (sirayla kullanilir)
        private static readonly Brush[] KutuZemin =
        {
            Firca("#EFF3FA"), Firca("#EEF6F1"), Firca("#FBF2E9"), Firca("#F3F0FA")
        };

        private static readonly Brush[] KutuYazi =
        {
            Firca("#2E64AC"), Firca("#1D6F42"), Firca("#A85B12"), Firca("#5B4BA8")
        };

        private static Brush Firca(string renk)
        {
            var f = new SolidColorBrush((Color)ColorConverter.ConvertFromString(renk));
            f.Freeze();
            return f;
        }

        // ================= GIRIS =================

        public static void Yaz(Rapor rapor, string yol)
        {
            // Toplam satiri son satir olarak eklenir, vurgulu cizilir
            var satirlar = new List<object[]>(rapor.Satirlar);
            int toplamIndeks = -1;

            if (rapor.Toplam != null)
            {
                satirlar.Add(rapor.Toplam);
                toplamIndeks = satirlar.Count - 1;
            }

            List<int> sayfaBoylari = SayfalaraBol(rapor, satirlar.Count);

            var resimler = new List<byte[]>();
            int sira = 0;

            for (int sayfa = 0; sayfa < sayfaBoylari.Count; sayfa++)
            {
                var dilim = satirlar.GetRange(sira, sayfaBoylari[sayfa]);

                FrameworkElement gorsel = SayfaCiz(rapor, dilim, toplamIndeks - sira,
                                                   sayfa == 0, sayfa + 1, sayfaBoylari.Count);

                resimler.Add(Goruntule(gorsel));
                sira += sayfaBoylari[sayfa];
            }

            PdfYaz(resimler, yol);
        }

        // Ust blogun gercek yuksekligi olculerek her sayfaya kac satir
        // sigacagi bulunur
        private static List<int> SayfalaraBol(Rapor rapor, int satirSayisi)
        {
            double icerikG = SayfaG - 2 * Kenar;

            double ilkUst = Olc(UstBlok(rapor, true), icerikG);
            double devamUst = Olc(UstBlok(rapor, false), icerikG);

            var sayfalar = new List<int>();
            int kalan = satirSayisi;

            for (int sayfa = 0; ; sayfa++)
            {
                double bant = sayfa == 0 ? BantY : BantYDevam;
                double ust = sayfa == 0 ? ilkUst : devamUst;

                double tabloAlani = SayfaY - bant - 16 - Kenar - ust - AltBilgiY - BaslikSatirY;
                int kapasite = Math.Max(1, (int)(tabloAlani / SatirY));

                if (kalan <= kapasite) { sayfalar.Add(Math.Max(0, kalan)); break; }

                sayfalar.Add(kapasite);
                kalan -= kapasite;
            }

            return sayfalar;
        }

        private static double Olc(FrameworkElement e, double genislik)
        {
            if (e == null) return 0;

            e.Measure(new Size(genislik, double.PositiveInfinity));
            return e.DesiredSize.Height;
        }

        // ================= SAYFA =================

        private static FrameworkElement SayfaCiz(Rapor rapor, List<object[]> satirlar,
                                                 int toplamIndeksi, bool ilkSayfa,
                                                 int sayfaNo, int sayfaSayisi)
        {
            var kok = new Grid
            {
                Width = SayfaG,
                Height = SayfaY,
                Background = Beyaz,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };

            kok.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            kok.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---- Baslik bandi ----
            FrameworkElement bant = Bant(rapor, ilkSayfa, sayfaNo, sayfaSayisi);
            Grid.SetRow(bant, 0);
            kok.Children.Add(bant);

            // ---- Govde ----
            var govde = new Grid { Margin = new Thickness(Kenar, 16, Kenar, Kenar) };
            govde.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            govde.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            govde.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(govde, 1);
            kok.Children.Add(govde);

            FrameworkElement ust = UstBlok(rapor, ilkSayfa);
            if (ust != null)
            {
                Grid.SetRow(ust, 0);
                govde.Children.Add(ust);
            }

            FrameworkElement tablo = Tablo(rapor, satirlar, toplamIndeksi);
            tablo.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(tablo, 1);
            govde.Children.Add(tablo);

            FrameworkElement alt = AltBilgi(sayfaNo, sayfaSayisi);
            Grid.SetRow(alt, 2);
            govde.Children.Add(alt);

            kok.Measure(new Size(SayfaG, SayfaY));
            kok.Arrange(new Rect(0, 0, SayfaG, SayfaY));
            kok.UpdateLayout();

            return kok;
        }

        // Koyu baslik bandi ve altindaki ince vurgu seridi
        private static FrameworkElement Bant(Rapor rapor, bool ilkSayfa,
                                             int sayfaNo, int sayfaSayisi)
        {
            var sarmal = new StackPanel();

            var bant = new Border
            {
                Background = Koyu,
                Height = (ilkSayfa ? BantY : BantYDevam) - 3,
                Padding = new Thickness(Kenar, 0, Kenar, 0)
            };

            var duzen = new Grid();
            bant.Child = duzen;

            var sol = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Logo bulunamazsa (ornegin tasarim testinde) sadece yazi kalir
            try
            {
                sol.Children.Add(new Image
                {
                    Source = new BitmapImage(
                        new Uri("pack://application:,,,/Assets/macria-logo.png")),
                    Width = ilkSayfa ? 26 : 18,
                    Height = ilkSayfa ? 26 : 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });
            }
            catch { }

            sol.Children.Add(new TextBlock
            {
                Text = "MACRIA",
                FontFamily = Yazi,
                FontSize = ilkSayfa ? 15 : 12,
                FontWeight = FontWeights.Bold,
                Foreground = Beyaz,
                VerticalAlignment = VerticalAlignment.Center
            });

            sol.Children.Add(new Border
            {
                Width = 1,
                Height = ilkSayfa ? 22 : 16,
                Background = Firca("#454952"),
                Margin = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            sol.Children.Add(new TextBlock
            {
                Text = ilkSayfa ? rapor.Baslik : rapor.Baslik + "  (devam)",
                FontFamily = Yazi,
                FontSize = ilkSayfa ? 15 : 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ilkSayfa ? Beyaz : BantSoluk,
                VerticalAlignment = VerticalAlignment.Center
            });

            duzen.Children.Add(sol);

            var sag = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (ilkSayfa)
            {
                sag.Children.Add(new TextBlock
                {
                    Text = rapor.Tarih.ToString("dd.MM.yyyy  HH:mm", CultureInfo.CurrentCulture),
                    FontFamily = Yazi,
                    FontSize = 11.5,
                    Foreground = Beyaz,
                    HorizontalAlignment = HorizontalAlignment.Right
                });

                if (rapor.AltBaslik.Length > 0)
                    sag.Children.Add(new TextBlock
                    {
                        Text = rapor.AltBaslik,
                        FontFamily = Yazi,
                        FontSize = 9.5,
                        Foreground = BantSoluk,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
            }
            else
            {
                sag.Children.Add(new TextBlock
                {
                    Text = "Sayfa " + sayfaNo + " / " + sayfaSayisi,
                    FontFamily = Yazi,
                    FontSize = 10,
                    Foreground = BantSoluk
                });
            }

            duzen.Children.Add(sag);

            sarmal.Children.Add(bant);
            sarmal.Children.Add(new Border { Background = Vurgu, Height = 3 });

            return sarmal;
        }

        // Ozet kutulari + parametre etiketleri (yalnizca ilk sayfada)
        private static FrameworkElement UstBlok(Rapor rapor, bool ilkSayfa)
        {
            if (!ilkSayfa) return null;

            var blok = new StackPanel();

            if (rapor.Ozetler.Count > 0)
            {
                var kutular = new Grid { Margin = new Thickness(0, 0, 0, 12) };

                for (int i = 0; i < rapor.Ozetler.Count; i++)
                    kutular.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });

                for (int i = 0; i < rapor.Ozetler.Count; i++)
                {
                    RaporOzet ozet = rapor.Ozetler[i];

                    var icerik = new StackPanel();
                    icerik.Children.Add(new TextBlock
                    {
                        Text = ozet.Baslik,
                        FontFamily = Yazi,
                        FontSize = 9.5,
                        Foreground = Soluk
                    });
                    icerik.Children.Add(new TextBlock
                    {
                        Text = ozet.Deger,
                        FontFamily = Yazi,
                        FontSize = 19,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = KutuYazi[i % KutuYazi.Length],
                        Margin = new Thickness(0, 2, 0, 0)
                    });

                    var kutu = new Border
                    {
                        Background = KutuZemin[i % KutuZemin.Length],
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(14, 10, 14, 11),
                        Margin = new Thickness(i == 0 ? 0 : 5, 0,
                                               i == rapor.Ozetler.Count - 1 ? 0 : 5, 0),
                        Child = icerik
                    };

                    Grid.SetColumn(kutu, i);
                    kutular.Children.Add(kutu);
                }

                blok.Children.Add(kutular);
            }

            if (rapor.Bilgiler.Count > 0)
            {
                var cipler = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

                foreach (string bilgi in rapor.Bilgiler)
                    cipler.Children.Add(new Border
                    {
                        Background = CipZemin,
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(10, 5, 10, 6),
                        Margin = new Thickness(0, 0, 7, 6),
                        Child = new TextBlock
                        {
                            Text = bilgi,
                            FontFamily = Yazi,
                            FontSize = 9.5,
                            Foreground = Firca("#3C4250")
                        }
                    });

                blok.Children.Add(cipler);
            }

            return blok;
        }

        private static FrameworkElement AltBilgi(int sayfaNo, int sayfaSayisi)
        {
            var sarmal = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            sarmal.Children.Add(new Border { Background = Cizgi, Height = 1 });

            var satir = new Grid { Margin = new Thickness(0, 7, 0, 0) };

            satir.Children.Add(new TextBlock
            {
                Text = "Macria  ·  BMC  ·  CATIA Sac Parça Ağırlık ve Maliyet Hesabı",
                FontFamily = Yazi,
                FontSize = 9,
                Foreground = Soluk
            });

            satir.Children.Add(new TextBlock
            {
                Text = "Sayfa " + sayfaNo + " / " + sayfaSayisi,
                FontFamily = Yazi,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Vurgu,
                HorizontalAlignment = HorizontalAlignment.Right
            });

            sarmal.Children.Add(satir);
            return sarmal;
        }

        // ================= TABLO =================

        private static FrameworkElement Tablo(Rapor rapor, List<object[]> satirlar,
                                              int toplamIndeksi)
        {
            var tablo = new Grid();

            foreach (RaporSutun s in rapor.Sutunlar)
                tablo.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(s.Genislik, GridUnitType.Star)
                });

            tablo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BaslikSatirY) });
            for (int i = 0; i < satirlar.Count; i++)
                tablo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SatirY) });

            // Vurgulu baslik seridi
            var seritZemin = new Border { Background = Vurgu };
            Grid.SetRow(seritZemin, 0);
            Grid.SetColumnSpan(seritZemin, rapor.Sutunlar.Count);
            tablo.Children.Add(seritZemin);

            for (int i = 0; i < rapor.Sutunlar.Count; i++)
            {
                RaporSutun s = rapor.Sutunlar[i];

                var hucre = new TextBlock
                {
                    Text = s.Ad,
                    FontFamily = Yazi,
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Beyaz,
                    Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 13,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    TextAlignment = s.Sayi ? TextAlignment.Right : TextAlignment.Left
                };

                Grid.SetRow(hucre, 0);
                Grid.SetColumn(hucre, i);
                tablo.Children.Add(hucre);
            }

            for (int r = 0; r < satirlar.Count; r++)
            {
                object[] veri = satirlar[r];
                bool toplam = r == toplamIndeksi;

                var zemin = new Border
                {
                    Background = toplam ? VurguSolgun : (r % 2 == 1 ? Zebra : Beyaz),
                    BorderBrush = toplam ? Vurgu : Cizgi,
                    BorderThickness = toplam
                        ? new Thickness(0, 1, 0, 0)
                        : new Thickness(0, 0, 0, 1)
                };

                Grid.SetRow(zemin, r + 1);
                Grid.SetColumnSpan(zemin, rapor.Sutunlar.Count);
                tablo.Children.Add(zemin);

                for (int i = 0; i < rapor.Sutunlar.Count && i < veri.Length; i++)
                {
                    RaporSutun s = rapor.Sutunlar[i];
                    string metin = HucreMetni(veri[i], s);

                    var hucre = new TextBlock
                    {
                        Text = metin,
                        FontFamily = Yazi,
                        FontSize = 9.5,
                        FontWeight = toplam ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = HucreRengi(s, metin, toplam),
                        Margin = new Thickness(8, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextAlignment = s.Sayi ? TextAlignment.Right : TextAlignment.Left
                    };

                    Grid.SetRow(hucre, r + 1);
                    Grid.SetColumn(hucre, i);
                    tablo.Children.Add(hucre);
                }
            }

            return new Border
            {
                BorderBrush = Cizgi,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Child = tablo
            };
        }

        private static Brush HucreRengi(RaporSutun sutun, string metin, bool toplam)
        {
            if (toplam) return Vurgu;

            if (sutun.Durum && metin.Length > 0)
                return metin == "Ölçüldü" ? Yesil : Kirmizi;

            return Metin;
        }

        private static string HucreMetni(object deger, RaporSutun sutun)
        {
            if (deger == null) return "";

            if (deger is double)
                return ((double)deger).ToString("N" + sutun.Ondalik, CultureInfo.CurrentCulture);

            return Convert.ToString(deger, CultureInfo.CurrentCulture);
        }

        // ================= GORUNTU =================

        // Sayfayi RGB piksellere cevirir
        private static byte[] Goruntule(FrameworkElement gorsel)
        {
            int g = (int)(SayfaG * Olcek);
            int y = (int)(SayfaY * Olcek);

            var hedef = new RenderTargetBitmap(g, y, 96.0 * Olcek, 96.0 * Olcek,
                                               PixelFormats.Pbgra32);
            hedef.Render(gorsel);

            var bgra = new byte[g * y * 4];
            hedef.CopyPixels(bgra, g * 4, 0);

            var rgb = new byte[g * y * 3];
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                rgb[j] = bgra[i + 2];
                rgb[j + 1] = bgra[i + 1];
                rgb[j + 2] = bgra[i];
            }

            return rgb;
        }

        // ================= PDF DOSYASI =================

        private static void PdfYaz(List<byte[]> sayfaPikselleri, string yol)
        {
            int g = (int)(SayfaG * Olcek);
            int y = (int)(SayfaY * Olcek);

            using (var dosya = new FileStream(yol, FileMode.Create, FileAccess.Write))
            {
                var konumlar = new List<long>();
                long uzunluk = 0;

                Action<string> yazMetin = s =>
                {
                    byte[] b = Encoding.Latin1.GetBytes(s);
                    dosya.Write(b, 0, b.Length);
                    uzunluk += b.Length;
                };

                Action<byte[]> yazHam = b =>
                {
                    dosya.Write(b, 0, b.Length);
                    uzunluk += b.Length;
                };

                // Nesne numaralari: 1 katalog, 2 sayfa agaci,
                // her sayfa icin sirayla sayfa / icerik / goruntu
                int sayfaSayisi = sayfaPikselleri.Count;

                yazMetin("%PDF-1.4\n%âãÏÓ\n");

                konumlar.Add(uzunluk);
                yazMetin("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                var cocuklar = new StringBuilder();
                for (int i = 0; i < sayfaSayisi; i++)
                    cocuklar.Append(3 + i * 3).Append(" 0 R ");

                konumlar.Add(uzunluk);
                yazMetin("2 0 obj\n<< /Type /Pages /Count " + sayfaSayisi +
                         " /Kids [ " + cocuklar.ToString().Trim() + " ] >>\nendobj\n");

                string olcuKutusu = "[ 0 0 " +
                    PuntoG.ToString("0.##", CultureInfo.InvariantCulture) + " " +
                    PuntoY.ToString("0.##", CultureInfo.InvariantCulture) + " ]";

                for (int i = 0; i < sayfaSayisi; i++)
                {
                    int sayfaNo = 3 + i * 3;
                    int icerikNo = sayfaNo + 1;
                    int goruntuNo = sayfaNo + 2;

                    konumlar.Add(uzunluk);
                    yazMetin(sayfaNo + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox " +
                             olcuKutusu + " /Resources << /XObject << /Im0 " + goruntuNo +
                             " 0 R >> >> /Contents " + icerikNo + " 0 R >>\nendobj\n");

                    string icerik = "q\n" +
                        PuntoG.ToString("0.##", CultureInfo.InvariantCulture) + " 0 0 " +
                        PuntoY.ToString("0.##", CultureInfo.InvariantCulture) + " 0 0 cm\n" +
                        "/Im0 Do\nQ\n";

                    konumlar.Add(uzunluk);
                    yazMetin(icerikNo + " 0 obj\n<< /Length " + icerik.Length + " >>\nstream\n" +
                             icerik + "endstream\nendobj\n");

                    byte[] sikistirilmis = ZlibSikistir(sayfaPikselleri[i]);

                    konumlar.Add(uzunluk);
                    yazMetin(goruntuNo + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + g +
                             " /Height " + y + " /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
                             "/Filter /FlateDecode /Length " + sikistirilmis.Length +
                             " >>\nstream\n");
                    yazHam(sikistirilmis);
                    yazMetin("\nendstream\nendobj\n");
                }

                long capraz = uzunluk;
                int nesneSayisi = konumlar.Count + 1;

                yazMetin("xref\n0 " + nesneSayisi + "\n");
                yazMetin("0000000000 65535 f \n");

                foreach (long k in konumlar)
                    yazMetin(k.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");

                yazMetin("trailer\n<< /Size " + nesneSayisi + " /Root 1 0 R >>\nstartxref\n" +
                         capraz + "\n%%EOF\n");
            }
        }

        // PDF'in FlateDecode'u zlib bicimi ister: 2 bayt baslik + veri + Adler-32
        private static byte[] ZlibSikistir(byte[] veri)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);

                using (var sikistirici = new DeflateStream(ms, CompressionLevel.Optimal, true))
                    sikistirici.Write(veri, 0, veri.Length);

                uint adler = Adler32(veri);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);

                return ms.ToArray();
            }
        }

        private static uint Adler32(byte[] veri)
        {
            uint a = 1, b = 0;

            foreach (byte d in veri)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }

            return (b << 16) | a;
        }
    }
}
