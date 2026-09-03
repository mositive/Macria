using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Macria
{
    // Ekrandan goruntu yakalama ve kucuk bir ornegi bu goruntu icinde arama.
    //
    // 3DEXPERIENCE panelleri kendi ciziyor: Windows'a ne UIA agaci ne de
    // metinli alt pencere veriyor, bu yuzden ne dugme ne de onay kutusu
    // "nesne" olarak sorgulanabiliyor. Geriye kalan tek saglam yol piksele
    // bakmak. Save As dugmesi (bkz. SaveAsBulucu) ve Bend Information onay
    // kutusu (bkz. BukumBulucu) ayni makineyi kullaniyor.
    //
    // Arama uc kademeli: once 8 kat kucultulmus goruntude kaba tarama, sonra
    // 2 kat kucukte ve tam cozunurlukte dar pencerede hassaslastirma. Tek
    // kademeli tam tarama saniyeler surerdi.
    internal static class GorselEslesme
    {
        // Bulunan eslesme. Taranan alanin pikselleri de birlikte doner:
        // cagiran, eslesmenin yanindaki bir noktanin rengine bakmak icin
        // ekrani ikinci kez yakalamak zorunda kalmasin.
        internal sealed class Eslesme
        {
            public int Sol;             // ornegin ekrandaki sol ust kosesi
            public int Ust;
            public int Gen;
            public int Yuk;
            public double Skor;

            public byte[] AlanBgra;     // taranan alanin pikselleri
            public int AlanSol;
            public int AlanUst;
            public int AlanGen;
            public int AlanYuk;

            public int MerkezX { get { return Sol + Gen / 2; } }
            public int MerkezY { get { return Ust + Yuk / 2; } }

            // Ekran noktasinin cevresindeki karenin ortalama parlakligi.
            // Onay kutusunun icini okumak icin kullaniliyor.
            public bool OrtalamaGri(int ekranX, int ekranY, int yari, out double ort)
            {
                ort = 0;

                if (AlanBgra == null) return false;

                long toplam = 0;
                int adet = 0;

                for (int j = -yari; j <= yari; j++)
                {
                    int y = ekranY + j - AlanUst;
                    if (y < 0 || y >= AlanYuk) continue;

                    for (int i = -yari; i <= yari; i++)
                    {
                        int x = ekranX + i - AlanSol;
                        if (x < 0 || x >= AlanGen) continue;

                        int k = (y * AlanGen + x) * 4;

                        toplam += (AlanBgra[k] + AlanBgra[k + 1] * 2 + AlanBgra[k + 2]) >> 2;
                        adet++;
                    }
                }

                if (adet == 0) return false;

                ort = (double)toplam / adet;
                return true;
            }
        }

        // ================= ARAMA =================

        // Ornegi pencerenin icinde arar; bulamazsa null doner
        public static Eslesme Bul(byte[] ornekBgra, int og, int oy,
                                  IntPtr pencere, double esik)
        {
            if (ornekBgra == null || og < 4 || oy < 4) return null;

            int sol, ust, gen, yuk;
            if (!AramaAlani(pencere, out sol, out ust, out gen, out yuk)) return null;
            if (gen < og || yuk < oy) return null;

            byte[] alanBgra = EkranAl(sol, ust, gen, yuk);
            if (alanBgra == null) return null;

            Gri alan = Griye(alanBgra, gen, yuk);
            Gri ornek = Griye(ornekBgra, og, oy);

            // Kaba tarama: 8 kat kucukte
            Gri alan8 = Kucult(Kucult(Kucult(alan)));
            Gri ornek8 = Kucult(Kucult(Kucult(ornek)));

            int bx, by;
            double s8 = EnIyi(alan8, ornek8, 0, 0, alan8.G, alan8.Y, out bx, out by);

            if (s8 <= 0) return null;

            // 2 kat kucukte dar pencerede duzelt
            Gri alan2 = Kucult(alan);
            Gri ornek2 = Kucult(ornek);

            // Kaba kademedeki 1 piksellik sapma tam cozunurlukte 8 piksel
            // ediyor; duzeltme penceresi buna gore genis tutulur
            int ix = bx * 4, iy = by * 4;

            double s2 = EnIyi(alan2, ornek2,
                              ix - 10, iy - 10, 21, 21, out bx, out by);

            if (s2 <= 0) return null;

            // Tam cozunurlukte son duzeltme
            ix = bx * 2; iy = by * 2;

            double skor = EnIyi(alan, ornek, ix - 6, iy - 6, 13, 13, out bx, out by);
            if (skor < esik) return null;

            return new Eslesme
            {
                Sol = sol + bx,
                Ust = ust + by,
                Gen = og,
                Yuk = oy,
                Skor = skor,
                AlanBgra = alanBgra,
                AlanSol = sol,
                AlanUst = ust,
                AlanGen = gen,
                AlanYuk = yuk
            };
        }

        // Panelin dikdortgeni; okunamazsa tum sanal ekran taranir
        public static bool AramaAlani(IntPtr pencere,
                                      out int sol, out int ust,
                                      out int gen, out int yuk)
        {
            int vx = GetSystemMetrics(76);
            int vy = GetSystemMetrics(77);
            int vg = GetSystemMetrics(78);
            int vyuk = GetSystemMetrics(79);

            sol = vx; ust = vy; gen = vg; yuk = vyuk;

            PencereAraclari.RECT r;

            if (pencere != IntPtr.Zero &&
                PencereAraclari.GetWindowRect(pencere, out r) &&
                r.Right > r.Left && r.Bottom > r.Top)
            {
                sol = r.Left;
                ust = r.Top;
                gen = r.Right - r.Left;
                yuk = r.Bottom - r.Top;
            }

            // Sanal ekranin disina tasan kisim kirpilir
            if (sol < vx) { gen -= vx - sol; sol = vx; }
            if (ust < vy) { yuk -= vy - ust; ust = vy; }
            if (sol + gen > vx + vg) gen = vx + vg - sol;
            if (ust + yuk > vy + vyuk) yuk = vy + vyuk - ust;

            return gen > 0 && yuk > 0;
        }

        // ================= ESLESTIRME =================

        private sealed class Gri
        {
            public int G;
            public int Y;
            public byte[] P;
        }

        private static Gri Griye(byte[] bgra, int g, int y)
        {
            var gri = new Gri { G = g, Y = y, P = new byte[g * y] };

            for (int i = 0, j = 0; i < gri.P.Length; i++, j += 4)
            {
                // Kaba parlaklik; kanallarin tam agirligi burada onemli degil
                gri.P[i] = (byte)((bgra[j] + bgra[j + 1] * 2 + bgra[j + 2]) >> 2);
            }

            return gri;
        }

        private static Gri Kucult(Gri k)
        {
            int g = k.G / 2, y = k.Y / 2;
            if (g < 1) g = 1;
            if (y < 1) y = 1;

            var s = new Gri { G = g, Y = y, P = new byte[g * y] };

            for (int j = 0; j < y; j++)
            {
                int k0 = (j * 2) * k.G;
                int k1 = (j * 2 + 1) * k.G;

                for (int i = 0; i < g; i++)
                {
                    int a = i * 2;
                    s.P[j * g + i] = (byte)((k.P[k0 + a] + k.P[k0 + a + 1] +
                                             k.P[k1 + a] + k.P[k1 + a + 1]) >> 2);
                }
            }

            return s;
        }

        // Verilen pencerede en iyi eslesmeyi arar; donen deger benzerlik.
        //
        // Olcut, ortalamasi cikarilmis normalize edilmis korelasyon: parlaklik
        // farkindan etkilenmez, sadece desene bakar.
        private static double EnIyi(Gri alan, Gri ornek,
                                    int basX, int basY, int genislik, int yukseklik,
                                    out int enX, out int enY)
        {
            enX = 0; enY = 0;

            if (basX < 0) { genislik += basX; basX = 0; }
            if (basY < 0) { yukseklik += basY; basY = 0; }

            int sonX = Math.Min(basX + genislik, alan.G - ornek.G + 1);
            int sonY = Math.Min(basY + yukseklik, alan.Y - ornek.Y + 1);

            if (sonX <= basX || sonY <= basY) return 0;

            int n = ornek.G * ornek.Y;
            if (n < 4) return 0;

            double ortO = 0;
            for (int i = 0; i < n; i++) ortO += ornek.P[i];
            ortO /= n;

            double varO = 0;
            for (int i = 0; i < n; i++)
            {
                double d = ornek.P[i] - ortO;
                varO += d * d;
            }

            if (varO <= 1e-9) return 0;

            double enIyi = -1;

            for (int y = basY; y < sonY; y++)
            {
                for (int x = basX; x < sonX; x++)
                {
                    double toplam = 0;

                    for (int j = 0; j < ornek.Y; j++)
                    {
                        int a = (y + j) * alan.G + x;
                        for (int i = 0; i < ornek.G; i++) toplam += alan.P[a + i];
                    }

                    double ortA = toplam / n;

                    double carpim = 0, varA = 0;

                    for (int j = 0; j < ornek.Y; j++)
                    {
                        int a = (y + j) * alan.G + x;
                        int o = j * ornek.G;

                        for (int i = 0; i < ornek.G; i++)
                        {
                            double da = alan.P[a + i] - ortA;

                            carpim += da * (ornek.P[o + i] - ortO);
                            varA += da * da;
                        }
                    }

                    if (varA <= 1e-9) continue;

                    double s = carpim / Math.Sqrt(varA * varO);

                    if (s > enIyi)
                    {
                        enIyi = s;
                        enX = x;
                        enY = y;
                    }
                }
            }

            return enIyi < 0 ? 0 : enIyi;
        }

        // Tek renk bir alan hicbir seye benzemez, her yere de benzer
        public static bool Duz(byte[] bgra)
        {
            byte enAz = 255, enCok = 0;

            for (int j = 0; j < bgra.Length; j += 4)
            {
                byte v = bgra[j + 1];
                if (v < enAz) enAz = v;
                if (v > enCok) enCok = v;
            }

            return enCok - enAz < 12;
        }

        // ================= DOSYA =================

        // %AppData%\Macria altindaki ornek dosyasinin yolu
        public static string OrnekYolu(string ad)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Macria", ad);
        }

        public static void PngYaz(byte[] bgra, int g, int y, string yol)
        {
            string klasor = Path.GetDirectoryName(yol);
            if (!string.IsNullOrEmpty(klasor)) Directory.CreateDirectory(klasor);

            BitmapSource kaynak = BitmapSource.Create(
                g, y, 96, 96, PixelFormats.Bgra32, null, bgra, g * 4);

            var kodlayici = new PngBitmapEncoder();
            kodlayici.Frames.Add(BitmapFrame.Create(kaynak));

            using (FileStream akis = File.Create(yol)) kodlayici.Save(akis);
        }

        public static bool PngOku(string yol, out byte[] bgra, out int g, out int y)
        {
            bgra = null; g = 0; y = 0;

            try
            {
                if (!File.Exists(yol)) return false;

                BitmapFrame kare;

                using (FileStream akis = File.OpenRead(yol))
                {
                    var cozucu = new PngBitmapDecoder(
                        akis, BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    kare = cozucu.Frames[0];
                }

                var donusturulmus = new FormatConvertedBitmap(
                    kare, PixelFormats.Bgra32, null, 0);

                g = donusturulmus.PixelWidth;
                y = donusturulmus.PixelHeight;

                if (g < 4 || y < 4) return false;

                bgra = new byte[g * y * 4];
                donusturulmus.CopyPixels(bgra, g * 4, 0);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================= EKRAN =================

        // Ekranin verilen dikdortgenini 32 bit BGRA olarak alir
        public static byte[] EkranAl(int x, int y, int g, int yuk)
        {
            if (g <= 0 || yuk <= 0) return null;

            IntPtr ekran = IntPtr.Zero, bellek = IntPtr.Zero, resim = IntPtr.Zero;

            try
            {
                ekran = CreateDC("DISPLAY", null, null, IntPtr.Zero);
                if (ekran == IntPtr.Zero) return null;

                bellek = CreateCompatibleDC(ekran);
                resim = CreateCompatibleBitmap(ekran, g, yuk);

                if (bellek == IntPtr.Zero || resim == IntPtr.Zero) return null;

                IntPtr eski = SelectObject(bellek, resim);

                // CAPTUREBLT katmanli pencereleri de alir
                bool tamam = BitBlt(bellek, 0, 0, g, yuk, ekran, x, y,
                                    SRCCOPY | CAPTUREBLT);

                SelectObject(bellek, eski);

                if (!tamam) return null;

                var bilgi = new BITMAPINFO();
                bilgi.biSize = Marshal.SizeOf(typeof(BITMAPINFO));
                bilgi.biWidth = g;
                bilgi.biHeight = -yuk;      // eksi: satirlar yukaridan asagi
                bilgi.biPlanes = 1;
                bilgi.biBitCount = 32;
                bilgi.biCompression = 0;

                var veri = new byte[g * yuk * 4];

                int satir = GetDIBits(bellek, resim, 0, (uint)yuk, veri, ref bilgi, 0);

                return satir > 0 ? veri : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (resim != IntPtr.Zero) DeleteObject(resim);
                if (bellek != IntPtr.Zero) DeleteDC(bellek);
                if (ekran != IntPtr.Zero) DeleteDC(ekran);
            }
        }

        // Dikdortgenin koselerinden biri bile Macria'ya aitse true
        public static bool KendiPenceremizVar(int x, int y, int g, int yuk)
        {
            uint bizim = (uint)Environment.ProcessId;

            int[] nx = { x, x + g - 1, x, x + g - 1, x + g / 2 };
            int[] ny = { y, y, y + yuk - 1, y + yuk - 1, y + yuk / 2 };

            for (int i = 0; i < nx.Length; i++)
            {
                var p = new PencereAraclari.POINT { X = nx[i], Y = ny[i] };

                IntPtr h = PencereAraclari.WindowFromPoint(p);
                if (h == IntPtr.Zero) continue;

                uint pid;
                GetWindowThreadProcessId(h, out pid);

                if (pid == bizim) return true;
            }

            return false;
        }

        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;

            // 32 bit BI_RGB'de palet yok; yapiya fazladan alan eklenirse
            // biSize bozulur ve GetDIBits basarisiz olur
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr CreateDC(string surucu, string aygit,
                                              string baglanti, IntPtr veri);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int g, int y);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr nesne);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr nesne);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hedef, int hx, int hy, int g, int y,
                                          IntPtr kaynak, int kx, int ky, int islem);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr resim, uint ilk,
                                            uint satir, byte[] veri,
                                            ref BITMAPINFO bilgi, uint kullanim);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int indeks);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    }
}
