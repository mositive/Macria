using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Macria
{
    // CATIA penceresini bulmak ve ogretilmis Save As noktasini ekran
    // koordinatina cevirmek icin kullanilan Win32 yardimcilari.
    internal static class PencereAraclari
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        private const uint GA_ROOT = 2;

        public static string SinifAdi(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string BaslikMetni(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static IntPtr KokPencere(IntPtr h)
        {
            if (h == IntPtr.Zero) return IntPtr.Zero;
            IntPtr kok = GetAncestor(h, GA_ROOT);
            return kok == IntPtr.Zero ? h : kok;
        }

        // CATIA/3DEXPERIENCE surecine ait mi?
        private static bool CatiaSureci(uint pid, Dictionary<uint, string> onbellek)
        {
            string ad;
            if (!onbellek.TryGetValue(pid, out ad))
            {
                ad = "";
                try { ad = Process.GetProcessById((int)pid).ProcessName; }
                catch { }
                onbellek[pid] = ad;
            }

            if (ad.Length == 0) return false;
            string f = ad.ToUpperInvariant();

            return f.Contains("CNEXT") || f.Contains("CATIA") ||
                   f.Contains("3DEXPERIENCE") || f.Contains("DSLAUNCHER");
        }

        // CATIA surecine ait gorunur ust duzey pencereler
        public static List<IntPtr> CatiaPencereleri()
        {
            var liste = new List<IntPtr>();
            var onbellek = new Dictionary<uint, string>();

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;

                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (!CatiaSureci(pid, onbellek)) return true;

                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                if (r.Right - r.Left < 60 || r.Bottom - r.Top < 40) return true;

                liste.Add(h);
                return true;
            }, IntPtr.Zero);

            return liste;
        }

        // CATIA'nin ana penceresi: sureci CATIA olan en buyuk gorunur pencere.
        // Baslik "3DEXPERIENCE" olmak zorunda degil; surumden surume degisiyor.
        public static IntPtr AnaPencere()
        {
            IntPtr en = IntPtr.Zero;
            long enAlan = 0;

            foreach (IntPtr h in CatiaPencereleri())
            {
                RECT r;
                if (!GetWindowRect(h, out r)) continue;

                long alan = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (alan > enAlan) { enAlan = alan; en = h; }
            }

            return en;
        }

        // Ogretme aninda kaydedilen pencereye en yakin pencereyi bulur
        private static IntPtr KayitliPencere()
        {
            if (Ayarlar.PencereSinifi.Length == 0) return AnaPencere();

            IntPtr enIyi = IntPtr.Zero;
            long enFark = long.MaxValue;

            foreach (IntPtr h in CatiaPencereleri())
            {
                if (!string.Equals(SinifAdi(h), Ayarlar.PencereSinifi, StringComparison.Ordinal))
                    continue;

                RECT r;
                if (!GetWindowRect(h, out r)) continue;

                long fark = Math.Abs((r.Right - r.Left) - Ayarlar.PencereGenislik) +
                            Math.Abs((r.Bottom - r.Top) - Ayarlar.PencereYukseklik);

                if (fark < enFark) { enFark = fark; enIyi = h; }
            }

            return enIyi != IntPtr.Zero ? enIyi : AnaPencere();
        }

        // Ogretilmis Save As noktasinin su anki ekran koordinati
        public static bool OgretilmisNokta(out int x, out int y)
        {
            x = 0; y = 0;
            if (!Ayarlar.KonumVar) return false;

            IntPtr h = KayitliPencere();
            if (h == IntPtr.Zero) return false;

            RECT r;
            if (!GetWindowRect(h, out r)) return false;

            x = r.Left + Ayarlar.Dx;
            y = r.Top + Ayarlar.Dy;

            // Nokta pencerenin disina dustuyse guvenilir degil
            if (x < r.Left || x > r.Right || y < r.Top || y > r.Bottom) return false;

            return true;
        }
    }
}
