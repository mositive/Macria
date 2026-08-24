using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Macria
{
    // Uygulama kimligi, gelistiriciler ve calisma ortami bilgisini gosteren pencere.
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            txtVersion.Text = "Sürüm " + SurumMetni();
            txtRuntime.Text = RuntimeInformation.FrameworkDescription;
            txtOs.Text = RuntimeInformation.OSDescription;
            txtArch.Text = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() == "x64"
                ? "x64 (64-bit)"
                : RuntimeInformation.ProcessArchitecture.ToString();

            txtCopyright.Text = TelifMetni();
        }

        // csproj'daki Version degeri; yoksa assembly surumune duser
        internal static string SurumMetni()
        {
            var asm = Assembly.GetExecutingAssembly();

            var bilgi = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string s = bilgi == null ? null : bilgi.InformationalVersion;

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

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
