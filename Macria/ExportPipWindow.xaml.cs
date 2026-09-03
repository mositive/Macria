using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Macria
{
    // Export surerken tum pencerelerin ustunde duran kucuk ilerleme penceresi.
    // Odak calmaz; CATIA'ya gonderilen klavye otomasyonunu bozmaz.
    //
    // Calisma animasyonu ise gore degisir:
    //   DXF aktarimi  -> parcadan klasore ucan sayfalar (dosya kopyalama gibi)
    //   Olcum/hesap   -> nefes alan olcum cubuklari
    public partial class ExportPipWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public ExportPipWindow()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                PlaceBottomRight();
                AnimasyonuUygula();
            };

            SizeChanged += (s, e) => PlaceBottomRight();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Tiklansa bile aktif olmasin, Alt-Tab listesinde gorunmesin
            IntPtr h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE,
                GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        private void PlaceBottomRight()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16;
            Top = wa.Bottom - ActualHeight - 16;
        }

        public enum PipState { Starting, Running, Error, Stopped, Done }

        // Basliktaki is adi; export akisi "DXF Export", olcum akisi "Hesaplama"
        private string _gorevAdi = "DXF Export";

        public string GorevAdi
        {
            get { return _gorevAdi; }
            set
            {
                _gorevAdi = value ?? "";
                AnimasyonuUygula();
            }
        }

        // Animasyon secimi tek yerde: gorev adinda "hesap" gecen akislar olcum
        // sayilir, geri kalani DXF aktarimidir.
        private bool HesaplamaMi
        {
            get
            {
                return _gorevAdi.IndexOf("hesap",
                    StringComparison.CurrentCultureIgnoreCase) >= 0;
            }
        }

        // ================= ANIMASYONLAR =================

        private bool _calisiyor;

        private void AnimasyonuUygula()
        {
            if (!IsLoaded) return;

            bool hesap = HesaplamaMi;

            animDxf.Visibility = _calisiyor && !hesap
                ? Visibility.Visible : Visibility.Collapsed;

            animHesap.Visibility = _calisiyor && hesap
                ? Visibility.Visible : Visibility.Collapsed;

            if (!_calisiyor) { AnimasyonlariDurdur(); return; }

            if (hesap) { DxfDurdur(); HesapBaslat(); }
            else { HesapDurdur(); DxfBaslat(); }
        }

        // ================= DXF: ACINIMIN CIZILMESI =================
        //
        // Export'un fiilen yaptigi is anlatilir: bukulu parcadan acinim
        // cikarilir, konturu adim adim cizilir, kontur kapaninca bukum
        // cizgisi ve delikler yerine oturur.

        private const double Dongu = 2.45;   // saniye

        private void DxfBaslat()
        {
            var sure = new Duration(TimeSpan.FromSeconds(Dongu));

            // Kontur: kesikli desenin kaymasiyla adim adim ortaya cikar.
            // Desen uzunlugu konturun cevresine esit; tek cizgi bastan sona
            // acilir. Cizim, delikler yerlesecek kadar sure kalsin diye
            // dongunun %80'inde biter.
            var cizim = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            cizim.KeyFrames.Add(new LinearDoubleKeyFrame(106, Zaman(0)));
            cizim.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(0.80)));
            cizim.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(1)));

            dxfKontur.BeginAnimation(Shape.StrokeDashOffsetProperty, cizim);

            // Kontur tamamlanip kisa bir an durduktan sonra silinir
            var konturSol = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            konturSol.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(0)));
            konturSol.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(0.90)));
            konturSol.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(1)));

            dxfKontur.BeginAnimation(OpacityProperty, konturSol);

            // Kontur kapaninca ic detaylar sirayla yerine oturur
            BukumuAc();
            DeligiAc(delikA, delikAOlcek, 0.84);
            DeligiAc(delikB, delikBOlcek, 0.88);

            OkuYakip(okA, 0.00);
            OkuYakip(okB, 0.14);
            OkuYakip(okC, 0.28);
        }

        // Bukum cizgisi kontur bittikten sonra belirir
        private void BukumuAc()
        {
            var gorun = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(0)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(0.80)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, Zaman(0.86)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, Zaman(0.92)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(1)));

            bukumCizgisi.BeginAnimation(OpacityProperty, gorun);
        }

        private void DeligiAc(UIElement delik, ScaleTransform olcek, double an)
        {
            var gorun = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(0)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(an)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(an + 0.03)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(0.93)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(1)));

            delik.BeginAnimation(OpacityProperty, gorun);

            var zipla = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            zipla.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.3, Zaman(0)));
            zipla.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.3, Zaman(an)));
            zipla.KeyFrames.Add(new EasingDoubleKeyFrame(1, Zaman(an + 0.06))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.9 } });

            olcek.BeginAnimation(ScaleTransform.ScaleXProperty, zipla);
            olcek.BeginAnimation(ScaleTransform.ScaleYProperty, zipla);
        }

        // Uc ok sirayla parlar: akis yonunu gosterir
        private void OkuYakip(UIElement ok, double gecikme)
        {
            var parla = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(gecikme),
                Duration = new Duration(TimeSpan.FromSeconds(0.95))
            };

            parla.KeyFrames.Add(new LinearDoubleKeyFrame(0.2,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
            parla.KeyFrames.Add(new LinearDoubleKeyFrame(1,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.22))));
            parla.KeyFrames.Add(new LinearDoubleKeyFrame(0.2,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))));
            parla.KeyFrames.Add(new LinearDoubleKeyFrame(0.2,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.95))));

            ok.BeginAnimation(OpacityProperty, parla);
        }

        // Anahtar kare zamani: dongunun orani olarak verilir
        private static KeyTime Zaman(double oran)
        {
            return KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Dongu * oran));
        }

        private void DxfDurdur()
        {
            dxfKontur.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            dxfKontur.BeginAnimation(OpacityProperty, null);

            bukumCizgisi.BeginAnimation(OpacityProperty, null);

            delikA.BeginAnimation(OpacityProperty, null);
            delikB.BeginAnimation(OpacityProperty, null);

            delikAOlcek.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            delikAOlcek.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            delikBOlcek.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            delikBOlcek.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            okA.BeginAnimation(OpacityProperty, null);
            okB.BeginAnimation(OpacityProperty, null);
            okC.BeginAnimation(OpacityProperty, null);
        }

        // ================= HESAP: AGIRLIK VE PARA =================
        //
        // DXF ile ayni kurgu: ayni parca, ayni akis, farkli cikti. Once
        // agirlik yukaridan gelip yerine oturur, ardindan maliyeti temsil
        // eden paralar yanina duser.

        private void HesapBaslat()
        {
            // Agirlik yukaridan iner ve hafif bir tasmayla oturur
            var inis = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            inis.KeyFrames.Add(new DiscreteDoubleKeyFrame(-16, Zaman(0)));
            inis.KeyFrames.Add(new EasingDoubleKeyFrame(0, Zaman(0.22))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 } });

            agirlikKay.BeginAnimation(TranslateTransform.YProperty, inis);

            var agirlikGorun = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            agirlikGorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(0)));
            agirlikGorun.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(0.10)));
            agirlikGorun.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(0.92)));
            agirlikGorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(0.99)));

            agirlikGrup.BeginAnimation(OpacityProperty, agirlikGorun);

            ParaKoy(paraArka, paraArkaOlcek, 0.42);
            ParaKoy(paraOn, paraOnOlcek, 0.56);

            // Denge kurulunca birimler kisaca ziplar: degerler bulundu
            var zipla = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            zipla.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, Zaman(0)));
            zipla.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, Zaman(0.72)));
            zipla.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, Zaman(0.78))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            zipla.KeyFrames.Add(new EasingDoubleKeyFrame(1, Zaman(0.88))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.8 } });

            kgOlcek.BeginAnimation(ScaleTransform.ScaleXProperty, zipla);
            kgOlcek.BeginAnimation(ScaleTransform.ScaleYProperty, zipla);

            OkuYakip(okD, 0.00);
            OkuYakip(okE, 0.14);
            OkuYakip(okF, 0.28);
        }

        // Para kucuk bir ziplamayla belirir
        private void ParaKoy(UIElement para, ScaleTransform olcek, double an)
        {
            var gorun = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            gorun.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Zaman(0)));
            gorun.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Zaman(an)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(an + 0.03)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(1, Zaman(0.92)));
            gorun.KeyFrames.Add(new LinearDoubleKeyFrame(0, Zaman(0.97)));

            para.BeginAnimation(OpacityProperty, gorun);

            var dusme = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            dusme.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.4, Zaman(0)));
            dusme.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.4, Zaman(an)));
            dusme.KeyFrames.Add(new EasingDoubleKeyFrame(1, Zaman(an + 0.06))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1 } });

            olcek.BeginAnimation(ScaleTransform.ScaleXProperty, dusme);
            olcek.BeginAnimation(ScaleTransform.ScaleYProperty, dusme);
        }

        private void HesapDurdur()
        {
            agirlikKay.BeginAnimation(TranslateTransform.YProperty, null);
            agirlikGrup.BeginAnimation(OpacityProperty, null);

            paraArka.BeginAnimation(OpacityProperty, null);
            paraOn.BeginAnimation(OpacityProperty, null);

            foreach (ScaleTransform o in new[] { paraArkaOlcek, paraOnOlcek, kgOlcek })
            {
                o.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                o.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }

            okD.BeginAnimation(OpacityProperty, null);
            okE.BeginAnimation(OpacityProperty, null);
            okF.BeginAnimation(OpacityProperty, null);
        }

        private void AnimasyonlariDurdur()
        {
            DxfDurdur();
            HesapDurdur();
        }

        // ================= DURUM =================

        public void SetDetail(string text)
        {
            pipDetail.Text = text;
        }

        // Konsolun son satirini pip'te gosterir (rengiyle birlikte)
        public void SetLastLog(string text, Brush color)
        {
            pipLastLog.Text = text ?? "";
            if (color != null) pipLastLog.Foreground = color;
        }

        public void SetStateKeepDetail(PipState state)
        {
            SetState(state, pipDetail.Text);
        }

        public void SetState(PipState state, string detail)
        {
            pipDetail.Text = detail ?? "";

            _calisiyor = state == PipState.Starting || state == PipState.Running;

            pipStateIcon.Visibility = _calisiyor ? Visibility.Collapsed : Visibility.Visible;
            btnStop.Visibility = _calisiyor ? Visibility.Visible : Visibility.Collapsed;
            if (_calisiyor) btnStop.IsEnabled = true;

            AnimasyonuUygula();

            switch (state)
            {
                case PipState.Starting:
                    pipTitleText.Text = " · " + GorevAdi + " Başlatılıyor...";
                    break;
                case PipState.Running:
                    pipTitleText.Text = " · " + GorevAdi + " Sürüyor";
                    break;
                case PipState.Error:
                    pipTitleText.Text = " · Hata Oluştu";
                    pipStateIcon.Text = KARPI;
                    pipStateIcon.Foreground = (Brush)FindResource("LogErrorBrush");
                    break;
                case PipState.Stopped:
                    pipTitleText.Text = " · İptal Edildi";
                    pipStateIcon.Text = KARPI;
                    pipStateIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xA0, 0x40));
                    break;
                case PipState.Done:
                    pipTitleText.Text = " · Sona Erdi";
                    pipStateIcon.Text = ONAY;
                    pipStateIcon.Foreground = (Brush)FindResource("LogSuccessBrush");
                    BitisSelami();
                    break;
            }
        }

        // Segoe MDL2 kod noktalari
        private const string KARPI = "";
        private const string ONAY = "";

        // Bitiste onay isareti kisaca buyuyup yerine oturur
        private void BitisSelami()
        {
            var buyu = new DoubleAnimation(0.6, 1, new Duration(TimeSpan.FromSeconds(0.32)))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            };

            var olcek = new ScaleTransform(1, 1);

            pipStateIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            pipStateIcon.RenderTransform = olcek;

            olcek.BeginAnimation(ScaleTransform.ScaleXProperty, buyu);
            olcek.BeginAnimation(ScaleTransform.ScaleYProperty, buyu);
        }

        // Acil durdurma istegi; MainWindow dinler
        public event Action StopRequested;

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            btnStop.IsEnabled = false;
            StopRequested?.Invoke();
        }
    }
}
