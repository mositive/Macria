using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Macria
{
    internal sealed class GrafikDeger
    {
        public string Etiket = "";
        public string AltEtiket = "";
        public double Deger;
        public Brush Renk;
    }

    // Grafikler bir Canvas uzerine elle cizilir. Dis kutuphane yok; uygulama
    // tek dosya olarak dagitildigi icin bagimlilik eklemek istemiyoruz.
    internal static class GrafikCizer
    {
        // Uygulamanin sicak antrasit paletiyle uyumlu seri renkleri
        public static readonly Color[] Palet =
        {
            Color.FromRgb(0x3E, 0x74, 0xBC),   // mavi
            Color.FromRgb(0x4E, 0x9A, 0x86),   // yesil
            Color.FromRgb(0xC0, 0x8E, 0x4A),   // amber
            Color.FromRgb(0xA9, 0x5A, 0x4E),   // kiremit
            Color.FromRgb(0x6E, 0x6A, 0xA8),   // mor
            Color.FromRgb(0x54, 0x8C, 0xA8),   // camgobegi
            Color.FromRgb(0x93, 0x8B, 0x5A),   // haki
            Color.FromRgb(0x9A, 0x5F, 0x86)    // erguvan
        };

        private static readonly Brush EksenFircasi =
            Yeni(Color.FromRgb(0x4A, 0x4A, 0x46));

        private static readonly Brush EtiketFircasi =
            Yeni(Color.FromRgb(0xA5, 0xA3, 0x99));

        private static readonly Brush SolukFirca =
            Yeni(Color.FromRgb(0x75, 0x73, 0x69));

        private static readonly Brush YaziFircasi =
            Yeni(Color.FromRgb(0xEC, 0xEA, 0xE2));

        public static Brush SeriFircasi(int sira)
        {
            return Yeni(Palet[sira % Palet.Length]);
        }

        private static Brush Yeni(Color c)
        {
            var f = new SolidColorBrush(c);
            f.Freeze();
            return f;
        }

        // ================= DIKEY CUBUK =================

        public static void DikeyCubuk(Canvas tuval, List<GrafikDeger> veriler,
                                      string birim, int ondalik)
        {
            tuval.Children.Clear();

            double g = tuval.ActualWidth, y = tuval.ActualHeight;
            if (g < 60 || y < 60) return;

            if (veriler == null || veriler.Count == 0) { BosYaz(tuval); return; }

            const double ust = 26, alt = 42, sol = 6, sag = 6;

            double enBuyuk = 0;
            foreach (GrafikDeger d in veriler) if (d.Deger > enBuyuk) enBuyuk = d.Deger;
            if (enBuyuk <= 0) { BosYaz(tuval); return; }

            double alan = g - sol - sag;
            double yukseklik = y - ust - alt;

            // Taban cizgisi
            var taban = new Line
            {
                X1 = sol, X2 = g - sag,
                Y1 = y - alt, Y2 = y - alt,
                Stroke = EksenFircasi,
                StrokeThickness = 1,
                SnapsToDevicePixels = true
            };
            tuval.Children.Add(taban);

            double adim = alan / veriler.Count;
            double kalinlik = Math.Min(52, Math.Max(8, adim * 0.62));

            for (int i = 0; i < veriler.Count; i++)
            {
                GrafikDeger d = veriler[i];

                double h = yukseklik * (d.Deger / enBuyuk);
                if (h < 2 && d.Deger > 0) h = 2;

                double x = sol + adim * i + (adim - kalinlik) / 2;

                var cubuk = new Rectangle
                {
                    Width = kalinlik,
                    Height = h,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = d.Renk ?? SeriFircasi(i)
                };

                Canvas.SetLeft(cubuk, x);
                Canvas.SetTop(cubuk, y - alt - h);
                tuval.Children.Add(cubuk);

                // Cubugun ustundeki deger
                TextBlock deger = Yazi(tuval, Bicim(d.Deger, ondalik), 11, YaziFircasi, true);
                Canvas.SetLeft(deger, x + (kalinlik - deger.DesiredSize.Width) / 2);
                Canvas.SetTop(deger, y - alt - h - deger.DesiredSize.Height - 4);
                tuval.Children.Add(deger);

                // Alt etiket
                TextBlock etiket = Yazi(tuval, d.Etiket, 11.5, EtiketFircasi, false);
                Canvas.SetLeft(etiket, x + (kalinlik - etiket.DesiredSize.Width) / 2);
                Canvas.SetTop(etiket, y - alt + 7);
                tuval.Children.Add(etiket);

                if (d.AltEtiket.Length > 0)
                {
                    TextBlock alt2 = Yazi(tuval, d.AltEtiket, 10.5, SolukFirca, false);
                    Canvas.SetLeft(alt2, x + (kalinlik - alt2.DesiredSize.Width) / 2);
                    Canvas.SetTop(alt2, y - alt + 22);
                    tuval.Children.Add(alt2);
                }
            }

            if (birim.Length > 0)
            {
                TextBlock b = Yazi(tuval, birim, 11, SolukFirca, false);
                Canvas.SetLeft(b, sol);
                Canvas.SetTop(b, 2);
                tuval.Children.Add(b);
            }
        }

        // ================= YATAY CUBUK =================

        public static void YatayCubuk(Canvas tuval, List<GrafikDeger> veriler,
                                      string birim, int ondalik)
        {
            tuval.Children.Clear();

            double g = tuval.ActualWidth, y = tuval.ActualHeight;
            if (g < 120 || y < 40) return;

            if (veriler == null || veriler.Count == 0) { BosYaz(tuval); return; }

            double enBuyuk = 0;
            foreach (GrafikDeger d in veriler) if (d.Deger > enBuyuk) enBuyuk = d.Deger;
            if (enBuyuk <= 0) { BosYaz(tuval); return; }

            // Etiket sutunu genisligi, en uzun etikete gore ama ust sinirli
            double etiketEni = 0;
            var etiketler = new TextBlock[veriler.Count];

            for (int i = 0; i < veriler.Count; i++)
            {
                etiketler[i] = Yazi(tuval, veriler[i].Etiket, 12, EtiketFircasi, false);
                if (etiketler[i].DesiredSize.Width > etiketEni)
                    etiketEni = etiketler[i].DesiredSize.Width;
            }

            etiketEni = Math.Min(etiketEni + 12, g * 0.42);

            double degerEni = 86;
            double alan = g - etiketEni - degerEni;
            if (alan < 40) return;

            double adim = y / veriler.Count;
            double kalinlik = Math.Min(22, Math.Max(6, adim * 0.56));

            for (int i = 0; i < veriler.Count; i++)
            {
                double orta = adim * i + adim / 2;
                double w = alan * (veriler[i].Deger / enBuyuk);
                if (w < 2 && veriler[i].Deger > 0) w = 2;

                TextBlock etiket = etiketler[i];
                etiket.MaxWidth = etiketEni - 12;
                etiket.TextTrimming = TextTrimming.CharacterEllipsis;
                etiket.ToolTip = veriler[i].Etiket;

                Canvas.SetLeft(etiket, 0);
                Canvas.SetTop(etiket, orta - etiket.DesiredSize.Height / 2);
                tuval.Children.Add(etiket);

                var cubuk = new Rectangle
                {
                    Width = w,
                    Height = kalinlik,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = veriler[i].Renk ?? SeriFircasi(i)
                };

                Canvas.SetLeft(cubuk, etiketEni);
                Canvas.SetTop(cubuk, orta - kalinlik / 2);
                tuval.Children.Add(cubuk);

                string metin = Bicim(veriler[i].Deger, ondalik);
                if (birim.Length > 0) metin += " " + birim;

                TextBlock deger = Yazi(tuval, metin, 11.5, YaziFircasi, true);
                Canvas.SetLeft(deger, etiketEni + w + 8);
                Canvas.SetTop(deger, orta - deger.DesiredSize.Height / 2);
                tuval.Children.Add(deger);
            }
        }

        // ================= HALKA =================

        public static void Halka(Canvas tuval, List<GrafikDeger> veriler,
                                 string birim, int ondalik, string ortaMetin)
        {
            tuval.Children.Clear();

            double g = tuval.ActualWidth, y = tuval.ActualHeight;
            if (g < 120 || y < 100) return;

            double toplam = 0;
            if (veriler != null)
                foreach (GrafikDeger d in veriler) if (d.Deger > 0) toplam += d.Deger;

            if (toplam <= 0) { BosYaz(tuval); return; }

            double aciklamaEni = Math.Min(190, g * 0.46);
            double cizimEni = g - aciklamaEni;

            double yaricap = Math.Min(cizimEni, y) / 2 - 8;
            if (yaricap < 24) { yaricap = Math.Max(18, Math.Min(cizimEni, y) / 2 - 2); }

            double icYaricap = yaricap * 0.62;
            var merkez = new Point(cizimEni / 2, y / 2);

            double aci = -90;   // tepe noktasindan baslar, saat yonunde ilerler

            for (int i = 0; i < veriler.Count; i++)
            {
                if (veriler[i].Deger <= 0) continue;

                double pay = veriler[i].Deger / toplam;
                double bitis = aci + pay * 360;

                // Tek dilim kalan durumda tam halka cizilir
                var sekil = new Path
                {
                    Fill = veriler[i].Renk ?? SeriFircasi(i),
                    Data = pay >= 0.9999
                        ? (Geometry)TamHalka(merkez, yaricap, icYaricap)
                        : Dilim(merkez, yaricap, icYaricap, aci, bitis)
                };

                tuval.Children.Add(sekil);
                aci = bitis;
            }

            if (ortaMetin.Length > 0)
            {
                TextBlock orta = Yazi(tuval, ortaMetin, 15, YaziFircasi, true);
                Canvas.SetLeft(orta, merkez.X - orta.DesiredSize.Width / 2);
                Canvas.SetTop(orta, merkez.Y - orta.DesiredSize.Height / 2);
                tuval.Children.Add(orta);
            }

            // Aciklama listesi
            double satirY = Math.Max(6, y / 2 - veriler.Count * 20);

            for (int i = 0; i < veriler.Count; i++)
            {
                if (veriler[i].Deger <= 0) continue;

                var kutu = new Rectangle
                {
                    Width = 10, Height = 10, RadiusX = 2, RadiusY = 2,
                    Fill = veriler[i].Renk ?? SeriFircasi(i)
                };

                Canvas.SetLeft(kutu, cizimEni + 6);
                Canvas.SetTop(kutu, satirY + 3);
                tuval.Children.Add(kutu);

                TextBlock ad = Yazi(tuval, veriler[i].Etiket, 12, EtiketFircasi, false);
                Canvas.SetLeft(ad, cizimEni + 22);
                Canvas.SetTop(ad, satirY);
                tuval.Children.Add(ad);

                string metin = Bicim(veriler[i].Deger, ondalik) +
                               (birim.Length > 0 ? " " + birim : "") +
                               "  ·  %" + Bicim(veriler[i].Deger / toplam * 100, 1);

                TextBlock deger = Yazi(tuval, metin, 11.5, YaziFircasi, true);
                Canvas.SetLeft(deger, cizimEni + 22);
                Canvas.SetTop(deger, satirY + 17);
                tuval.Children.Add(deger);

                satirY += 40;
            }
        }

        private static Geometry Dilim(Point merkez, double dis, double ic,
                                      double basAci, double sonAci)
        {
            Point d1 = Uzayda(merkez, dis, basAci);
            Point d2 = Uzayda(merkez, dis, sonAci);
            Point i1 = Uzayda(merkez, ic, sonAci);
            Point i2 = Uzayda(merkez, ic, basAci);

            bool genis = sonAci - basAci > 180;

            var sekil = new PathFigure { StartPoint = d1, IsClosed = true, IsFilled = true };

            sekil.Segments.Add(new ArcSegment(d2, new Size(dis, dis), 0,
                                              genis, SweepDirection.Clockwise, true));
            sekil.Segments.Add(new LineSegment(i1, true));
            sekil.Segments.Add(new ArcSegment(i2, new Size(ic, ic), 0,
                                              genis, SweepDirection.Counterclockwise, true));

            var geo = new PathGeometry();
            geo.Figures.Add(sekil);
            geo.Freeze();
            return geo;
        }

        private static Geometry TamHalka(Point merkez, double dis, double ic)
        {
            var geo = new GeometryGroup { FillRule = FillRule.EvenOdd };
            geo.Children.Add(new EllipseGeometry(merkez, dis, dis));
            geo.Children.Add(new EllipseGeometry(merkez, ic, ic));
            geo.Freeze();
            return geo;
        }

        private static Point Uzayda(Point merkez, double r, double aciDerece)
        {
            double a = aciDerece * Math.PI / 180.0;
            return new Point(merkez.X + r * Math.Cos(a), merkez.Y + r * Math.Sin(a));
        }

        // ================= YARDIMCI =================

        private static void BosYaz(Canvas tuval)
        {
            TextBlock t = Yazi(tuval, "Gösterilecek Veri Yok", 12.5, SolukFirca, false);

            Canvas.SetLeft(t, (tuval.ActualWidth - t.DesiredSize.Width) / 2);
            Canvas.SetTop(t, (tuval.ActualHeight - t.DesiredSize.Height) / 2);

            tuval.Children.Add(t);
        }

        // Olculebilmesi icin yazi tipi acikca verilir: tuvale eklenmeden once
        // olculdugu surece miras alinan degerler henuz gecerli degildir.
        private static TextBlock Yazi(Canvas tuval, string metin, double boy,
                                      Brush renk, bool kalin)
        {
            var t = new TextBlock
            {
                Text = metin,
                FontSize = boy,
                FontFamily = TextElement.GetFontFamily(tuval),
                FontWeight = kalin ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = renk
            };

            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return t;
        }

        public static string Bicim(double d, int ondalik)
        {
            return d.ToString("N" + ondalik, CultureInfo.CurrentCulture);
        }
    }
}
