using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Macria
{
    internal readonly struct AkilliRenk
    {
        public AkilliRenk(byte kirmizi, byte yesil, byte mavi)
        {
            Kirmizi = kirmizi;
            Yesil = yesil;
            Mavi = mavi;
        }

        public byte Kirmizi { get; }
        public byte Yesil { get; }
        public byte Mavi { get; }
    }

    internal sealed class AkilliRenklendirmeKilidi
    {
        private int _calisiyor;

        public bool Calisiyor => Volatile.Read(ref _calisiyor) != 0;

        public bool Baslat()
        {
            return Interlocked.CompareExchange(ref _calisiyor, 1, 0) == 0;
        }

        public void Bitir()
        {
            Volatile.Write(ref _calisiyor, 0);
        }
    }

    internal static class AkilliRenklendirmeMantigi
    {
        internal static string? ReferansAnahtari(string? externalId, string? version)
        {
            string kimlik = (externalId ?? "").Trim();
            if (kimlik.Length == 0) return null;

            return "PLM:" + kimlik.ToUpperInvariant() +
                   "|VERSION:" + (version ?? "").Trim().ToUpperInvariant();
        }

        internal static AkilliRenk RenkOlustur(string referansAnahtari)
        {
            if (string.IsNullOrWhiteSpace(referansAnahtari))
                throw new ArgumentException("Referans anahtarı boş olamaz.", nameof(referansAnahtari));

            byte[] ozet = SHA256.HashData(Encoding.UTF8.GetBytes(referansAnahtari));
            double ton = ((ozet[0] << 8) | ozet[1]) * 360.0 / 65536.0;
            double doygunluk = 0.62 + ozet[2] / 255.0 * 0.16;
            double parlaklik = 0.78 + ozet[3] / 255.0 * 0.14;
            return HsvToRgb(ton, doygunluk, parlaklik);
        }

        internal static List<T> YapraklariBul<T>(
            IEnumerable<T> kokler, Func<T, IEnumerable<T>> altDugumler)
        {
            var yapraklar = new List<T>();
            var yigin = new Stack<T>();

            foreach (T kok in kokler) yigin.Push(kok);
            while (yigin.Count > 0)
            {
                T dugum = yigin.Pop();
                var altlar = new List<T>(altDugumler(dugum) ?? Array.Empty<T>());
                if (altlar.Count == 0)
                {
                    yapraklar.Add(dugum);
                    continue;
                }

                for (int i = altlar.Count - 1; i >= 0; i--)
                    yigin.Push(altlar[i]);
            }

            return yapraklar;
        }

        private static AkilliRenk HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
            double m = v - c;
            double r, g, b;

            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return new AkilliRenk(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }
    }
}
