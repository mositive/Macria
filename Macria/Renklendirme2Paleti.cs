using System;
using System.Collections.Generic;
using System.Linq;

namespace Macria
{
    internal readonly struct Renklendirme2Rengi : IEquatable<Renklendirme2Rengi>
    {
        public Renklendirme2Rengi(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
        public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";

        public bool Equals(Renklendirme2Rengi other) =>
            Red == other.Red && Green == other.Green && Blue == other.Blue;

        public override bool Equals(object? obj) => obj is Renklendirme2Rengi other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Red, Green, Blue);
        public override string ToString() => Red + "," + Green + "," + Blue + " (" + Hex + ")";
    }

    internal sealed class Renklendirme2RenkAtamasi
    {
        public Renklendirme2RenkAtamasi(string referenceKey, int paletteIndex, Renklendirme2Rengi color)
        {
            ReferenceKey = referenceKey;
            PaletteIndex = paletteIndex;
            Color = color;
        }

        public string ReferenceKey { get; }
        public int PaletteIndex { get; }
        public Renklendirme2Rengi Color { get; }
    }

    internal sealed class Renklendirme2RenkPlani
    {
        private Renklendirme2RenkPlani(
            IReadOnlyDictionary<string, Renklendirme2RenkAtamasi> assignments,
            IReadOnlyList<string> overflowReferenceKeys)
        {
            Assignments = assignments;
            OverflowReferenceKeys = overflowReferenceKeys;
        }

        public IReadOnlyDictionary<string, Renklendirme2RenkAtamasi> Assignments { get; }
        public IReadOnlyList<string> OverflowReferenceKeys { get; }

        public static Renklendirme2RenkPlani Olustur(
            IEnumerable<string> referenceKeys,
            int paletteCycleIndex = 0)
        {
            if (referenceKeys == null) throw new ArgumentNullException(nameof(referenceKeys));

            string[] keys = referenceKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            IReadOnlyList<Renklendirme2Rengi> palette = Renklendirme2Paleti.Renkler;
            int paletteOffset = palette.Count == 0
                ? 0
                : ((paletteCycleIndex % palette.Count) + palette.Count) % palette.Count;
            var assignments = new Dictionary<string, Renklendirme2RenkAtamasi>(StringComparer.Ordinal);
            int assignedCount = Math.Min(keys.Length, palette.Count);
            for (int index = 0; index < assignedCount; index++)
            {
                int paletteIndex = (index + paletteOffset) % palette.Count;
                assignments.Add(
                    keys[index],
                    new Renklendirme2RenkAtamasi(keys[index], paletteIndex, palette[paletteIndex]));
            }

            return new Renklendirme2RenkPlani(
                assignments,
                keys.Skip(assignedCount).ToArray());
        }
    }

    internal static class Renklendirme2Paleti
    {
        internal const int BaslangicRenkSayisi = 72;

        private static readonly IReadOnlyList<Renklendirme2Rengi> _renkler =
            OlusturBaslangicPaleti();

        public static IReadOnlyList<Renklendirme2Rengi> Renkler => _renkler;

        // Sabit aday uzayı ve en uzak-renk seçimi aynı girdide aynı 72 RGB'yi üretir.
        // Adaylar düşük parlaklık, beyaza yakınlık ve düşük doygunluk sınırlarıyla filtrelenir.
        internal static IReadOnlyList<Renklendirme2Rengi> OlusturBaslangicPaleti()
        {
            var candidates = new List<Renklendirme2Rengi>();
            for (int hue = 0; hue < 360; hue += 5)
            {
                foreach (double saturation in new[] { 0.58, 0.70, 0.82, 0.90 })
                {
                    foreach (double value in new[] { 0.68, 0.78, 0.88 })
                    {
                        Renklendirme2Rengi color = HsvToRgb(hue, saturation, value);
                        double luminance = RelativeLuminance(color);
                        int spread = Math.Max(color.Red, Math.Max(color.Green, color.Blue)) -
                                     Math.Min(color.Red, Math.Min(color.Green, color.Blue));
                        if (luminance >= 58 && luminance <= 210 && spread >= 72)
                            candidates.Add(color);
                    }
                }
            }

            candidates = candidates.Distinct().ToList();
            var selected = new List<Renklendirme2Rengi>(BaslangicRenkSayisi)
            {
                HsvToRgb(210, 0.82, 0.78)
            };
            candidates.Remove(selected[0]);

            while (selected.Count < BaslangicRenkSayisi)
            {
                if (candidates.Count == 0)
                    throw new InvalidOperationException("Renklendirme 2.0 başlangıç paleti üretilemedi.");

                int bestIndex = 0;
                int bestDistance = -1;
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    int minimumDistance = int.MaxValue;
                    foreach (Renklendirme2Rengi chosen in selected)
                    {
                        minimumDistance = Math.Min(
                            minimumDistance,
                            SquaredRgbDistance(candidates[candidateIndex], chosen));
                    }

                    if (minimumDistance > bestDistance)
                    {
                        bestDistance = minimumDistance;
                        bestIndex = candidateIndex;
                    }
                }

                selected.Add(candidates[bestIndex]);
                candidates.RemoveAt(bestIndex);
            }

            return selected.ToArray();
        }

        private static int SquaredRgbDistance(Renklendirme2Rengi left, Renklendirme2Rengi right)
        {
            int red = left.Red - right.Red;
            int green = left.Green - right.Green;
            int blue = left.Blue - right.Blue;
            return red * red + green * green + blue * blue;
        }

        private static double RelativeLuminance(Renklendirme2Rengi color) =>
            0.2126 * color.Red + 0.7152 * color.Green + 0.0722 * color.Blue;

        private static Renklendirme2Rengi HsvToRgb(double hue, double saturation, double value)
        {
            double chroma = value * saturation;
            double x = chroma * (1 - Math.Abs(hue / 60.0 % 2 - 1));
            double m = value - chroma;
            double red;
            double green;
            double blue;

            if (hue < 60) { red = chroma; green = x; blue = 0; }
            else if (hue < 120) { red = x; green = chroma; blue = 0; }
            else if (hue < 180) { red = 0; green = chroma; blue = x; }
            else if (hue < 240) { red = 0; green = x; blue = chroma; }
            else if (hue < 300) { red = x; green = 0; blue = chroma; }
            else { red = chroma; green = 0; blue = x; }

            return new Renklendirme2Rengi(
                (byte)Math.Round((red + m) * 255),
                (byte)Math.Round((green + m) * 255),
                (byte)Math.Round((blue + m) * 255));
        }
    }
}
