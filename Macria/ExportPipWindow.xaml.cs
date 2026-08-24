using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Macria
{
    // Export surerken tum pencerelerin ustunde duran kucuk ilerleme penceresi.
    // Odak calmaz; CATIA'ya gonderilen klavye otomasyonunu bozmaz.
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

                var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9));
                spin.RepeatBehavior = RepeatBehavior.Forever;
                pipSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
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
        public string GorevAdi { get; set; } = "DXF Export";

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

            bool calisiyor = state == PipState.Starting || state == PipState.Running;

            pipSpinner.Visibility = calisiyor ? Visibility.Visible : Visibility.Collapsed;
            pipStateIcon.Visibility = calisiyor ? Visibility.Collapsed : Visibility.Visible;
            btnStop.Visibility = calisiyor ? Visibility.Visible : Visibility.Collapsed;
            if (calisiyor) btnStop.IsEnabled = true;

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
                    pipStateIcon.Text = "\uE711"; // carpi
                    pipStateIcon.Foreground = (Brush)FindResource("LogErrorBrush");
                    break;
                case PipState.Stopped:
                    pipTitleText.Text = " · İptal Edildi";
                    pipStateIcon.Text = "\uE711";
                    pipStateIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xA0, 0x40));
                    break;
                case PipState.Done:
                    pipTitleText.Text = " · Sona Erdi";
                    pipStateIcon.Text = "\uE73E"; // onay isareti
                    pipStateIcon.Foreground = (Brush)FindResource("LogSuccessBrush");
                    break;
            }
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
