using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Macria
{
    // Uygulama kimligi, gelistiriciler ve calisma ortami bilgisini gosteren pencere.
    // WPF UI (FluentWindow) yalnizca bu pencerede kullanilir; sozlukleri XAML'da pencere kaynaklarinda.
    // Mica kullanilmaz: sozlukler pencereye ozel oldugundan Mica sistemin acik temasini izliyor ve
    // WindowBackgroundManager.UpdateBackground (WPF-UI 4.3.0) koyu modu uygulamiyordu. Zemin BgBrush.
    public partial class AboutWindow : Wpf.Ui.Controls.FluentWindow
    {
        private const string KopyalaMetni = "Bilgileri kopyala";

        private readonly DispatcherTimer _kopyalandiZamanlayici =
            new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        public AboutWindow()
        {
            InitializeComponent();

            txtVersion.Text = "Sürüm " + SurumMetni();
            txtRuntime.Text = RuntimeInformation.FrameworkDescription;
            txtOs.Text = RuntimeInformation.OSDescription;
            txtArch.Text = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() == "x64"
                ? "x64 (64-bit)"
                : RuntimeInformation.ProcessArchitecture.ToString();

            txtCopyright.Text = TelifMetni();

            _kopyalandiZamanlayici.Tick += (s, e) =>
            {
                _kopyalandiZamanlayici.Stop();
                btnKopyala.Content = KopyalaMetni;
            };
            Closed += (s, e) => _kopyalandiZamanlayici.Stop();
        }

        // csproj'daki Version degeri; yoksa assembly surumune duser
        internal static string SurumMetni()
        {
            var asm = Assembly.GetExecutingAssembly();

            var bilgi = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string? s = bilgi == null ? null : bilgi.InformationalVersion;

            if (string.IsNullOrWhiteSpace(s))
            {
                var v = asm.GetName().Version;
                s = v == null ? "1.0.0" : v.ToString(3);
            }

            // "1.0.0+9f2c1a" gibi derleme ekini kirp
            int art = s.IndexOf('+');
            if (art > 0) s = s.Substring(0, art);

            return s;
        }

        private static string TelifMetni()
        {
            var asm = Assembly.GetExecutingAssembly();
            var telif = asm.GetCustomAttribute<AssemblyCopyrightAttribute>();

            if (telif != null && !string.IsNullOrWhiteSpace(telif.Copyright))
                return telif.Copyright;

            return "© " + DateTime.Now.Year + " Emre Koçak, Enes Yeşilöz";
        }

        // Destek talebine yapistirilacak surum ve ortam ozeti
        private string BilgiMetni()
        {
            return "Macria " + SurumMetni() + Environment.NewLine +
                   "Çalışma Zamanı: " + txtRuntime.Text + Environment.NewLine +
                   "İşletim Sistemi: " + txtOs.Text + Environment.NewLine +
                   "Mimari: " + txtArch.Text;
        }

        private void btnKopyala_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(BilgiMetni());
                btnKopyala.Content = "Kopyalandı";
            }
            catch (COMException)
            {
                // Pano baska bir surec tarafindan kilitli olabilir
                btnKopyala.Content = "Kopyalanamadı";
            }

            _kopyalandiZamanlayici.Stop();
            _kopyalandiZamanlayici.Start();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
