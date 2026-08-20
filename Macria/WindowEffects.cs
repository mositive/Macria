using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Macria
{
    // Pencere kosesi yuvarlatma. WPF'in kendi CornerRadius'u yalnizca cizilen icerigi
    // etkiler; pencerenin kendisini DWM kirptigi icin kose tercihi isletim sistemine
    // bildirilmelidir. Windows 11 (derleme 22000+) gerekir; oncesinde sessizce yok sayilir.
    internal static class WindowEffects
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        private const int DWMWCP_DEFAULT = 0;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        // Pencere henuz olusmamis olabilir; oyleyse tutamac hazir olunca uygulanir.
        public static void RoundCorners(Window pencere, bool kucuk = true)
        {
            if (pencere == null) return;

            IntPtr h = new WindowInteropHelper(pencere).Handle;
            if (h == IntPtr.Zero)
            {
                pencere.SourceInitialized += (s, e) => Uygula(
                    new WindowInteropHelper(pencere).Handle, kucuk);
                return;
            }

            Uygula(h, kucuk);
        }

        private static void Uygula(IntPtr hwnd, bool kucuk)
        {
            if (hwnd == IntPtr.Zero) return;

            int tercih = kucuk ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;

            try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref tercih, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
