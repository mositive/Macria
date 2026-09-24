using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Macria
{
    // Bir plan yalnizca geometriyi tarif eder; dosyaya veya mevcut entity'lere yazmaz.
    internal sealed class DxfKosePlani
    {
        public readonly Dictionary<DxfEntity, DxfEntity> Degisen = new();
        public readonly List<DxfYeniEntity> Eklenen = new();
    }

    internal sealed class DxfYeniEntity
    {
        public DxfEntity Entity;
        public DxfEntity OzellikKaynak;

        public DxfYeniEntity(DxfEntity entity, DxfEntity ozellikKaynak)
        {
            Entity = entity;
            OzellikKaynak = ozellikKaynak;
        }
    }

    internal static class DxfKoseGeometrisi
    {
        // Model birimi mm; baglanti ve geometrik yakinlik icin tek uzunluk toleransi.
        internal const double UzunlukToleransi = 1e-7;
        private const double YonToleransi = 1e-10;
        internal const double KoseYonToleransi = 1e-6;
        private const int EnCokTopluCizgi = 2048;
        private const string KonturHatasi = "Kapalı kontur güvenilir şekilde oluşturulamadı.";

        public static bool Pah(DxfEntity a, DxfEntity b, double deger,
                               out DxfKosePlani? plan, out string? hata)
            => IkiCizgi(a, b, deger, false, out plan, out hata);

        public static bool Fillet(DxfEntity a, DxfEntity b, double deger,
                                  out DxfKosePlani? plan, out string? hata)
            => IkiCizgi(a, b, deger, true, out plan, out hata);

        public static bool Pah(DxfEntity a, DxfEntity b, double deger, Point? tarafA, Point? tarafB,
            out DxfKosePlani? plan, out string? hata)
            => IkiCizgi(a, b, deger, false, out plan, out hata, tarafA, tarafB);

        public static bool Fillet(DxfEntity a, DxfEntity b, double deger, Point? tarafA, Point? tarafB,
            out DxfKosePlani? plan, out string? hata)
            => IkiCizgi(a, b, deger, true, out plan, out hata, tarafA, tarafB);

        public static bool Kesisim(DxfEntity a, DxfEntity b, out Point kose)
        {
            kose = default;
            if (ReferenceEquals(a, b) || !CizgiGecerli(a) || !CizgiGecerli(b)) return false;
            Vector u = a.Noktalar[^1] - a.Noktalar[0], v = b.Noktalar[^1] - b.Noktalar[0];
            u.Normalize(); v.Normalize();
            double det = Capraz(u, v);
            if (Math.Abs(det) <= KoseYonToleransi) return false;
            kose = a.Noktalar[0] + u * (Capraz(b.Noktalar[0] - a.Noktalar[0], v) / det);
            return Sonlu(kose) && Math.Abs(Capraz(kose - b.Noktalar[0], v)) <= UzunlukToleransi;
        }

        public static bool Birlestir(DxfEntity a, DxfEntity b, Point? tarafA, Point? tarafB,
            out DxfKosePlani? plan, out string? hata)
        {
            plan = null;
            hata = null;
            if (!Kesisim(a, b, out Point kose))
                return Basarisiz("Çizgiler paralel/çakışık, near-parallel veya kesişim güvenilir değil.", out hata);
            if (!KoseUcu(a, kose, out int ia, out _, out _, tarafA) ||
                !KoseUcu(b, kose, out int ib, out _, out _, tarafB))
                return Basarisiz("Köşe tarafı belirsiz; korunacak iki kenara sırayla tıklayın.", out hata);
            if (Yakin(a.Noktalar[ia == 0 ? 0 : a.Noktalar.Length - 1], kose) &&
                Yakin(b.Noktalar[ib == 0 ? 0 : b.Noktalar.Length - 1], kose))
                return Basarisiz("Çizgiler zaten aynı keskin köşede birleşiyor.", out hata);
            var sonuc = new DxfKosePlani();
            sonuc.Degisen.Add(a, UcuDegistir(a, ia, kose));
            sonuc.Degisen.Add(b, UcuDegistir(b, ib, kose));
            plan = sonuc;
            return true;
        }

        private static bool IkiCizgi(DxfEntity a, DxfEntity b, double deger, bool fillet,
                                    out DxfKosePlani? plan, out string? hata, Point? tarafA = null, Point? tarafB = null)
        {
            plan = null;
            hata = null;
            if (!double.IsFinite(deger) || deger <= UzunlukToleransi)
                return Basarisiz("Değer sıfırdan büyük ve sonlu olmalı.", out hata);
            if (ReferenceEquals(a, b) || !CizgiGecerli(a) || !CizgiGecerli(b))
                return Basarisiz("İki farklı, sıfır uzunlukta olmayan LINE seçin.", out hata);

            Point a0 = a.Noktalar[0], a1 = a.Noktalar[^1];
            Point b0 = b.Noktalar[0], b1 = b.Noktalar[^1];
            Vector da = a1 - a0, db = b1 - b0;
            double la = da.Length, lb = db.Length;
            Vector ua = da / la, ub = db / lb;
            double determinant = Capraz(ua, ub);
            if (Math.Abs(determinant) <= KoseYonToleransi)
                return Basarisiz("Seçilen çizgiler paralel veya köşe sayısal olarak çözülemiyor.", out hata);

            if (!Kesisim(a, b, out Point kose)) return Basarisiz("Köşe koordinatı güvenilir şekilde hesaplanamadı.", out hata);
            if (!KoseUcu(a, kose, out int ia, out Point uzakA, out Vector rayA, tarafA) ||
                !KoseUcu(b, kose, out int ib, out Point uzakB, out Vector rayB, tarafB))
                return Basarisiz("Kesişim segmentin içindedir; köşe tarafı belirsiz. Korunacak kenarlara tıklayın.", out hata);

            double cosine = Math.Clamp(Vector.Multiply(rayA, rayB), -1, 1);
            double aci = Math.Acos(cosine);
            if (aci <= YonToleransi || Math.PI - aci <= YonToleransi)
                return Basarisiz("Çizgiler güvenilir bir köşe oluşturmuyor.", out hata);
            double trim = fillet ? deger / Math.Tan(aci / 2) : deger;
            Point yeniA = kose + rayA * trim;
            Point yeniB = kose + rayB * trim;
            // Sanal kose kabul edilir, ancak mevcut segmenti uzatmak veya ters cevirmek yasak.
            if (!double.IsFinite(trim) || trim >= Math.Min(la, (uzakA - kose).Length) - UzunlukToleransi ||
                trim >= Math.Min(lb, (uzakB - kose).Length) - UzunlukToleransi ||
                !TrimNoktasi(a, ia, yeniA) || !TrimNoktasi(b, ib, yeniB))
                return Basarisiz(fillet ? "Radius mevcut LINE segmentlerine sığmıyor." : "Pah mevcut LINE segmentlerine sığmıyor.", out hata);

            DxfEntity ek;
            if (fillet)
            {
                Vector bisector = rayA + rayB;
                if (bisector.Length <= YonToleransi) return Basarisiz("Fillet açıortayı hesaplanamadı.", out hata);
                bisector.Normalize();
                Point merkez = kose + bisector * (deger / Math.Sin(aci / 2));
                if (!Sonlu(merkez)) return Basarisiz("Fillet merkezi hesaplanamadı.", out hata);
                double bas = Aci(yeniA - merkez), son = Aci(yeniB - merkez);
                Point basNokta = yeniA, sonNokta = yeniB;
                if (PozitifAci(son - bas) > 180)
                {
                    (bas, son) = (son, bas);
                    (basNokta, sonNokta) = (sonNokta, basNokta);
                }
                double sweep = PozitifAci(son - bas);
                if (sweep <= YonToleransi || sweep >= 180 + YonToleransi)
                    return Basarisiz("Fillet yayı güvenilir şekilde hesaplanamadı.", out hata);
                int adim = Math.Max(8, (int)Math.Ceiling(sweep / 4));
                Point[] noktalar = new Point[adim + 1];
                for (int i = 0; i <= adim; i++) noktalar[i] = YayNoktasi(merkez, deger, bas + sweep * i / adim);
                noktalar[0] = basNokta;
                noktalar[^1] = sonNokta;
                ek = new DxfEntity { Tip = "ARC", Merkez = merkez, Radius = deger,
                                     BaslangicAcisi = bas, BitisAcisi = son, Noktalar = noktalar };
            }
            else
            {
                ek = new DxfEntity { Tip = "LINE", Noktalar = new[] { yeniA, yeniB } };
                if (!CizgiGecerli(ek)) return Basarisiz("Pah sıfıra yakın segment oluşturuyor.", out hata);
            }

            var sonuc = new DxfKosePlani();
            sonuc.Degisen.Add(a, UcuDegistir(a, ia, yeniA));
            sonuc.Degisen.Add(b, UcuDegistir(b, ib, yeniB));
            sonuc.Eklenen.Add(new DxfYeniEntity(ek, a));
            plan = sonuc;
            return true;
        }

        private static bool KoseUcu(DxfEntity e, Point kose, out int uc, out Point uzak, out Vector ray, Point? taraf = null)
        {
            Point p = e.Noktalar[0], q = e.Noktalar[^1];
            Vector d = q - p;
            double boy = d.Length;
            Vector yon = d / boy;
            double t = Vector.Multiply(kose - p, yon);
            uc = 0;
            uzak = q;
            ray = default;
            if (taraf.HasValue && !Sonlu(taraf.Value)) return false;
            double secim = taraf.HasValue ? Vector.Multiply(taraf.Value - kose, yon) : 0;
            if (t > UzunlukToleransi && t < boy - UzunlukToleransi)
            {
                if (!taraf.HasValue || Math.Abs(secim) <= UzunlukToleransi) return false;
                if (secim < 0) { uc = 1; uzak = p; }
            }
            else
            {
                // Segmentin köşeye göre bulunduğu yarı doğru tek anlamlıdır.
                if (t >= boy - UzunlukToleransi) { uc = 1; uzak = p; }
                if (taraf.HasValue && secim * Math.Sign(Vector.Multiply(uzak - kose, yon)) < -UzunlukToleransi)
                    return false;
            }
            ray = uzak - kose;
            if (ray.Length <= UzunlukToleransi) return false;
            ray.Normalize();
            return true;
        }

        private static bool TrimNoktasi(DxfEntity e, int uc, Point p)
        {
            if (!Sonlu(p)) return false;
            Point a = e.Noktalar[0], b = e.Noktalar[^1];
            Vector d = b - a;
            double boy = d.Length;
            Vector yon = d / boy;
            double t = Vector.Multiply(p - a, yon);
            if (Math.Abs(Capraz(p - a, yon)) > UzunlukToleransi) return false;
            return uc == 0 ? t >= -UzunlukToleransi && t < boy - UzunlukToleransi
                           : t > UzunlukToleransi && t <= boy + UzunlukToleransi;
        }

        private static DxfEntity UcuDegistir(DxfEntity e, int uc, Point p)
        {
            Point[] points = new[] { e.Noktalar[0], e.Noktalar[^1] };
            points[uc] = p;
            return new DxfEntity { Tip = "LINE", Noktalar = points, KaynakKayit = e.KaynakKayit };
        }

        public static Rect GercekBounds(DxfEntity e)
        {
            if (e == null) return Rect.Empty;
            if (e.Tip == "LINE")
            {
                if (e.Noktalar.Length < 2 || !Sonlu(e.Noktalar[0]) || !Sonlu(e.Noktalar[^1])) return Rect.Empty;
                return new Rect(e.Noktalar[0], e.Noktalar[^1]);
            }
            if (e.Tip != "CIRCLE" && e.Tip != "ARC") return Rect.Empty;
            if (!Sonlu(e.Merkez) || !double.IsFinite(e.Radius) || e.Radius <= 0) return Rect.Empty;
            if (e.Tip == "CIRCLE") return new Rect(e.Merkez.X - e.Radius, e.Merkez.Y - e.Radius, 2 * e.Radius, 2 * e.Radius);
            if (!double.IsFinite(e.BaslangicAcisi) || !double.IsFinite(e.BitisAcisi)) return Rect.Empty;
            Rect result = new Rect(YayNoktasi(e.Merkez, e.Radius, e.BaslangicAcisi), YayNoktasi(e.Merkez, e.Radius, e.BitisAcisi));
            for (int angle = 0; angle < 360; angle += 90)
                if (Yayda(e, angle)) result.Union(YayNoktasi(e.Merkez, e.Radius, angle));
            return result;
        }

        public static bool Cercevede(DxfEntity e, Rect modelBox)
        {
            if (modelBox.IsEmpty) return false;
            Rect bounds = GercekBounds(e);
            if (bounds.IsEmpty) return false;
            return bounds.Left >= modelBox.Left && bounds.Right <= modelBox.Right &&
                   bounds.Top >= modelBox.Top && bounds.Bottom <= modelBox.Bottom;
        }

        private sealed class Dugum
        {
            public Point Point;
            public readonly List<int> Kenarlar = new();
            public Dugum(Point point) { Point = point; }
        }

        private sealed class Kontur
        {
            public readonly List<int> Kenarlar = new();
            public readonly List<int> Dugumler = new();
            public double ImzaliAlan;
        }

        // Tek kenardan erişilen endpoint bileşeni; ekran şekline/layer adına göre tahmin yapılmaz.
        public static bool KonturBul(DxfCizim model, DxfEntity seed, out List<DxfEntity> cizgiler, out string? hata)
        {
            cizgiler = new List<DxfEntity>();
            hata = null;
            DxfEntity[] tum = model.Entityler.Where(x => x.Tip == "LINE").ToArray();
            if (tum.Length > EnCokTopluCizgi || !tum.Contains(seed) || !CizgiGecerli(seed))
                return Basarisiz(KonturHatasi, out hata);
            var bekleyen = new Queue<DxfEntity>();
            var bulunan = new HashSet<DxfEntity> { seed };
            bekleyen.Enqueue(seed);
            while (bekleyen.TryDequeue(out DxfEntity? e))
            {
                cizgiler.Add(e);
                foreach (DxfEntity diger in tum)
                    if (!bulunan.Contains(diger) && diger.Noktalar.Length >= 2 &&
                        new[] { e.Noktalar[0], e.Noktalar[^1] }.Any(p =>
                            Yakin(p, diger.Noktalar[0]) || Yakin(p, diger.Noktalar[^1])))
                    { bulunan.Add(diger); bekleyen.Enqueue(diger); }
            }
            if (cizgiler.Count < 3 || cizgiler.Any(x => !CizgiGecerli(x) ||
                x.KaynakKayit == null || x.KaynakKayit.DuzenlemeEngeli != null))
                return Basarisiz(KonturHatasi, out hata);
            DxfKaynakKayit duzlem = cizgiler[0].KaynakKayit!;
            if (cizgiler.Any(x => !DxfKaynakBelge.OrtakDuzlemAyni(duzlem, x.KaynakKayit!)))
                return Basarisiz(KonturHatasi, out hata);
            var dugumler = new List<Dugum>();
            for (int i = 0; i < cizgiler.Count; i++)
            {
                int a = DugumBul(dugumler, cizgiler[i].Noktalar[0]), b = DugumBul(dugumler, cizgiler[i].Noktalar[^1]);
                if (a < 0 || b < 0 || a == b) return Basarisiz(KonturHatasi, out hata);
                dugumler[a].Kenarlar.Add(i); dugumler[b].Kenarlar.Add(i);
            }
            if (dugumler.Any(x => x.Kenarlar.Count != 2) ||
                !Kesisimsiz(cizgiler.Select(x => new Parca(x, 0)).ToList()))
                return Basarisiz(KonturHatasi, out hata);
            // Kesişen/bağlanan dış geometri karma kontur ya da branch olabilir: kabul etme.
            foreach (DxfEntity dis in model.Entityler.Where(x => !bulunan.Contains(x)))
            {
                DxfEntity other = dis.Tip == "CIRCLE" ? new DxfEntity
                { Tip = "ARC", Merkez = dis.Merkez, Radius = dis.Radius, BaslangicAcisi = 0, BitisAcisi = 360 } : dis;
                if (other.Tip != "LINE" && other.Tip != "ARC") return Basarisiz(KonturHatasi, out hata);
                foreach (DxfEntity e in cizgiler)
                {
                    Rect box = GercekBounds(e); box.Inflate(UzunlukToleransi, UzunlukToleransi);
                    if (box.IntersectsWith(GercekBounds(other)) &&
                        (!Kesisimler(e, other, out List<Point> points) || points.Count > 0))
                        return Basarisiz(KonturHatasi, out hata);
                }
            }
            Rect konturBox = Rect.Empty;
            foreach (DxfEntity e in cizgiler) konturBox.Union(GercekBounds(e));
            konturBox.Inflate(UzunlukToleransi, UzunlukToleransi);
            var bilinenYollar = model.Entityler.Select(x => x.Noktalar).ToHashSet();
            foreach (Point[] yol in model.Yollar.Where(x => !bilinenYollar.Contains(x)))
            {
                Rect box = Rect.Empty;
                foreach (Point p in yol)
                {
                    if (!Sonlu(p)) return Basarisiz(KonturHatasi, out hata);
                    box.Union(p);
                }
                // Örneklenmiş spline/polyline'ın gerçek bağlantısını kanıtlayamayız.
                if (konturBox.IntersectsWith(box)) return Basarisiz(KonturHatasi, out hata);
            }
            return true;
        }

        public static bool TumGuvenilirKonturlar(DxfCizim model, double deger, bool fillet,
            out DxfKosePlani? plan, out string? hata)
        {
            plan = null;
            hata = null;
            if (model.Entityler.Count(x => x.Tip == "LINE") > EnCokTopluCizgi)
                return Basarisiz(KonturHatasi, out hata);
            var ziyaret = new HashSet<DxfEntity>();
            var secim = new List<DxfEntity>();
            foreach (DxfEntity seed in model.Entityler.Where(x => x.Tip == "LINE"))
            {
                if (ziyaret.Contains(seed)) continue;
                bool guvenilir = KonturBul(model, seed, out List<DxfEntity> loop, out _);
                ziyaret.Add(seed); ziyaret.UnionWith(loop);
                if (guvenilir) secim.AddRange(loop);
            }
            if (secim.Count == 0) return Basarisiz(KonturHatasi, out hata);
            // Tek ortak plan; bir uygun loop'ta bile değer sığmıyorsa hiçbirini değiştirme.
            return TumKoseler(secim, deger, fillet, out plan, out hata);
        }

        public static bool TumKoseler(IReadOnlyList<DxfEntity> selectedLines, double deger, bool fillet,
                                      out DxfKosePlani? plan, out string? hata)
        {
            plan = null;
            hata = null;
            if (!double.IsFinite(deger) || deger <= UzunlukToleransi)
                return Basarisiz("Değer sıfırdan büyük ve sonlu olmalı.", out hata);
            if (selectedLines == null || selectedLines.Count < 3)
                return Basarisiz(KonturHatasi, out hata);
            if (selectedLines.Count > EnCokTopluCizgi)
                return Basarisiz($"Güvenli toplu geometri sınırı {EnCokTopluCizgi} LINE'dır.", out hata);
            var seen = new HashSet<DxfEntity>();
            var nodes = new List<Dugum>();
            var ends = new (int A, int B)[selectedLines.Count];
            for (int i = 0; i < selectedLines.Count; i++)
            {
                DxfEntity e = selectedLines[i];
                if (!CizgiGecerli(e) || !seen.Add(e)) return Basarisiz(KonturHatasi + " Yalnızca farklı LINE segmentleri destekleniyor.", out hata);
                int a = DugumBul(nodes, e.Noktalar[0]), b = DugumBul(nodes, e.Noktalar[^1]);
                if (a < 0 || b < 0 || a == b) return Basarisiz(KonturHatasi + " Uç bağlantıları belirsiz.", out hata);
                ends[i] = (a, b);
                nodes[a].Kenarlar.Add(i);
                nodes[b].Kenarlar.Add(i);
            }
            if (nodes.Any(n => n.Kenarlar.Count != 2))
                return Basarisiz(KonturHatasi + " Açık veya dallanan bağlantı var.", out hata);

            var contours = new List<Kontur>();
            var visited = new bool[selectedLines.Count];
            var lineLoop = new int[selectedLines.Count];
            for (int first = 0; first < selectedLines.Count; first++)
            {
                if (visited[first]) continue;
                var contour = new Kontur();
                int startNode = ends[first].A, node = startNode, line = first;
                do
                {
                    if (visited[line]) return Basarisiz(KonturHatasi, out hata);
                    visited[line] = true;
                    lineLoop[line] = contours.Count;
                    contour.Kenarlar.Add(line);
                    contour.Dugumler.Add(node);
                    node = ends[line].A == node ? ends[line].B : ends[line].A;
                    line = nodes[node].Kenarlar[0] == line ? nodes[node].Kenarlar[1] : nodes[node].Kenarlar[0];
                } while (node != startNode);
                if (contour.Kenarlar.Count < 3 || line != first) return Basarisiz(KonturHatasi, out hata);
                // Translation-safe shoelace calculation. This is geometric orientation, NOT material-side classification.
                Point origin = nodes[contour.Dugumler[0]].Point;
                for (int i = 0; i < contour.Dugumler.Count; i++)
                    contour.ImzaliAlan += Capraz(nodes[contour.Dugumler[i]].Point - origin,
                                                nodes[contour.Dugumler[(i + 1) % contour.Dugumler.Count]].Point - origin) / 2;
                if (!double.IsFinite(contour.ImzaliAlan) || Math.Abs(contour.ImzaliAlan) <= UzunlukToleransi * UzunlukToleransi)
                    return Basarisiz(KonturHatasi + " Alan veya yön belirsiz.", out hata);
                contours.Add(contour);
            }

            var originalPieces = new List<Parca>();
            for (int i = 0; i < selectedLines.Count; i++) originalPieces.Add(new Parca(selectedLines[i], lineLoop[i]));
            if (!Kesisimsiz(originalPieces)) return Basarisiz(KonturHatasi + " Konturlar kesişiyor veya temas ediyor.", out hata);

            var sonuc = new DxfKosePlani();
            var startChange = new Dictionary<DxfEntity, Point>();
            var endChange = new Dictionary<DxfEntity, Point>();
            var addedPieces = new List<Parca>();
            foreach (Kontur contour in contours)
            {
                for (int i = 0; i < contour.Kenarlar.Count; i++)
                {
                    int previous = contour.Kenarlar[(i + contour.Kenarlar.Count - 1) % contour.Kenarlar.Count];
                    int current = contour.Kenarlar[i];
                    if (!IkiCizgi(selectedLines[previous], selectedLines[current], deger, fillet, out DxfKosePlani? corner, out hata))
                    {
                        hata = KonturHatasi + " " + hata;
                        return false;
                    }
                    foreach (var update in corner!.Degisen)
                    {
                        bool start = !Yakin(update.Key.Noktalar[0], update.Value.Noktalar[0]);
                        bool end = !Yakin(update.Key.Noktalar[^1], update.Value.Noktalar[^1]);
                        if (start == end) return Basarisiz(KonturHatasi + " Köşe trim ucu belirsiz.", out hata);
                        var changes = start ? startChange : endChange;
                        if (!changes.TryAdd(update.Key, start ? update.Value.Noktalar[0] : update.Value.Noktalar[^1]))
                            return Basarisiz(KonturHatasi + " Aynı uç iki kez değiştirilmek isteniyor.", out hata);
                    }
                    foreach (DxfYeniEntity added in corner.Eklenen)
                    {
                        sonuc.Eklenen.Add(added);
                        addedPieces.Add(new Parca(added.Entity, lineLoop[current]));
                    }
                }
            }
            var finalPieces = new List<Parca>();
            for (int i = 0; i < selectedLines.Count; i++)
            {
                DxfEntity e = selectedLines[i];
                if (!startChange.TryGetValue(e, out Point p) || !endChange.TryGetValue(e, out Point q))
                    return Basarisiz(KonturHatasi + " Her iki uç güvenilir şekilde çözülemedi.", out hata);
                Vector direction = e.Noktalar[^1] - e.Noktalar[0];
                direction.Normalize();
                if (Vector.Multiply(q - p, direction) <= UzunlukToleransi)
                    return Basarisiz("Pah/Radius komşu köşelerde çakışıyor; işlem uygulanmadı.", out hata);
                var changed = new DxfEntity { Tip = "LINE", Noktalar = new[] { p, q }, KaynakKayit = e.KaynakKayit };
                sonuc.Degisen.Add(e, changed);
                finalPieces.Add(new Parca(changed, lineLoop[i]));
            }
            finalPieces.AddRange(addedPieces);
            if (!Kesisimsiz(finalPieces) || !KapaliSonuc(finalPieces))
                return Basarisiz("Pah/Radius sonucu kontur kesişmesi, temas veya bağlantı belirsizliği oluşturuyor; işlem uygulanmadı.", out hata);
            plan = sonuc;
            return true;
        }

        private static int DugumBul(List<Dugum> nodes, Point p)
        {
            int found = -1;
            for (int i = 0; i < nodes.Count; i++)
                if (Yakin(nodes[i].Point, p))
                {
                    if (found >= 0) return -1;
                    found = i;
                }
            if (found >= 0) return found;
            nodes.Add(new Dugum(p));
            return nodes.Count - 1;
        }

        private sealed class Parca
        {
            public DxfEntity Entity;
            public int Kontur;
            public Parca(DxfEntity entity, int kontur) { Entity = entity; Kontur = kontur; }
        }

        private static bool KapaliSonuc(List<Parca> pieces)
        {
            var nodes = new List<Dugum>();
            for (int i = 0; i < pieces.Count; i++)
            {
                DxfEntity e = pieces[i].Entity;
                int a = DugumBul(nodes, e.Noktalar[0]), b = DugumBul(nodes, e.Noktalar[^1]);
                if (a < 0 || b < 0 || a == b) return false;
                nodes[a].Kenarlar.Add(i);
                nodes[b].Kenarlar.Add(i);
            }
            return nodes.All(n => n.Kenarlar.Count == 2 && pieces[n.Kenarlar[0]].Kontur == pieces[n.Kenarlar[1]].Kontur);
        }

        private static bool Kesisimsiz(List<Parca> pieces)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                Rect boxA = GercekBounds(pieces[i].Entity);
                boxA.Inflate(UzunlukToleransi, UzunlukToleransi);
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    if (!boxA.IntersectsWith(GercekBounds(pieces[j].Entity))) continue;
                    if (!Kesisimler(pieces[i].Entity, pieces[j].Entity, out List<Point> points)) return false;
                    foreach (Point point in points)
                        if (pieces[i].Kontur != pieces[j].Kontur || !Ucta(pieces[i].Entity, point) || !Ucta(pieces[j].Entity, point))
                            return false;
                }
            }
            return true;
        }

        // false: cakisan egri araliklari; true: sonlu matematiksel kesisim noktalarinin listesi.
        private static bool Kesisimler(DxfEntity a, DxfEntity b, out List<Point> points)
        {
            points = new List<Point>();
            if (a.Tip == "LINE" && b.Tip == "LINE") return CizgiCizgi(a, b, points);
            if (a.Tip == "ARC" && b.Tip == "LINE") (a, b) = (b, a);
            if (a.Tip == "LINE" && b.Tip == "ARC") return CizgiYay(a, b, points);
            return YayYay(a, b, points);
        }

        private static bool CizgiCizgi(DxfEntity a, DxfEntity b, List<Point> points)
        {
            Point p = a.Noktalar[0], q = b.Noktalar[0];
            Vector u = a.Noktalar[^1] - p, v = b.Noktalar[^1] - q;
            double la = u.Length, lb = v.Length;
            u /= la;
            v /= lb;
            double det = Capraz(u, v);
            if (Math.Abs(det) <= YonToleransi)
            {
                if (Math.Abs(Capraz(q - p, u)) > UzunlukToleransi) return true;
                double t0 = Vector.Multiply(q - p, u), t1 = Vector.Multiply(b.Noktalar[^1] - p, u);
                double lo = Math.Max(0, Math.Min(t0, t1)), hi = Math.Min(la, Math.Max(t0, t1));
                if (hi - lo > UzunlukToleransi) return false;
                if (hi >= lo - UzunlukToleransi) points.Add(p + u * ((lo + hi) / 2));
                return true;
            }
            double ta = Capraz(q - p, v) / det, tb = Capraz(q - p, u) / det;
            if (ta >= -UzunlukToleransi && ta <= la + UzunlukToleransi && tb >= -UzunlukToleransi && tb <= lb + UzunlukToleransi)
                points.Add(p + u * ta);
            return true;
        }

        private static bool CizgiYay(DxfEntity line, DxfEntity arc, List<Point> points)
        {
            Point p = line.Noktalar[0];
            Vector u = line.Noktalar[^1] - p;
            double length = u.Length;
            u /= length;
            Vector f = p - arc.Merkez;
            double along = Vector.Multiply(f, u);
            double perpendicular = Math.Abs(Capraz(f, u));
            if (perpendicular > arc.Radius + UzunlukToleransi) return true;
            double root = Math.Sqrt(Math.Max(0, (arc.Radius - perpendicular) * (arc.Radius + perpendicular)));
            foreach (double t in new[] { -along - root, -along + root })
            {
                if (t < -UzunlukToleransi || t > length + UzunlukToleransi) continue;
                Point point = p + u * t;
                if (Yayda(arc, Aci(point - arc.Merkez))) EkleTekil(points, point);
            }
            return true;
        }

        private static bool YayYay(DxfEntity a, DxfEntity b, List<Point> points)
        {
            Vector between = b.Merkez - a.Merkez;
            double d = between.Length;
            if (d <= UzunlukToleransi)
            {
                if (Math.Abs(a.Radius - b.Radius) > UzunlukToleransi) return true;
                // Esmerkezli yaylarda sadece tekil ortak uclar kabul edilir, ortak aci araligi reddedilir.
                if (YayIcinde(b, a.BaslangicAcisi + YayAcisi(a) / 2) ||
                    YayIcinde(a, b.BaslangicAcisi + YayAcisi(b) / 2) ||
                    YayIcinde(b, a.BaslangicAcisi) || YayIcinde(b, a.BitisAcisi) ||
                    YayIcinde(a, b.BaslangicAcisi) || YayIcinde(a, b.BitisAcisi)) return false;
                foreach (double angle in new[] { a.BaslangicAcisi, a.BitisAcisi })
                    if (Yayda(b, angle)) EkleTekil(points, YayNoktasi(a.Merkez, a.Radius, angle));
                return true;
            }
            if (d > a.Radius + b.Radius + UzunlukToleransi || d < Math.Abs(a.Radius - b.Radius) - UzunlukToleransi) return true;
            Vector direction = between / d;
            double x = ((a.Radius - b.Radius) * (a.Radius + b.Radius) + d * d) / (2 * d);
            double square = (a.Radius - x) * (a.Radius + x);
            if (square < -UzunlukToleransi * Math.Max(1, a.Radius)) return true;
            double y = Math.Sqrt(Math.Max(0, square));
            Point foot = a.Merkez + direction * x;
            Vector normal = new Vector(-direction.Y, direction.X);
            foreach (Point point in new[] { foot + normal * y, foot - normal * y })
                if (Yayda(a, Aci(point - a.Merkez)) && Yayda(b, Aci(point - b.Merkez))) EkleTekil(points, point);
            return true;
        }

        private static bool YayIcinde(DxfEntity arc, double angle)
        {
            double t = PozitifAci(angle - arc.BaslangicAcisi), sweep = YayAcisi(arc);
            double tolerance = UzunlukToleransi / arc.Radius * 180 / Math.PI;
            return t > tolerance && t < sweep - tolerance;
        }

        private static bool Yayda(DxfEntity arc, double angle)
        {
            double t = PozitifAci(angle - arc.BaslangicAcisi), sweep = YayAcisi(arc);
            double tolerance = UzunlukToleransi / arc.Radius * 180 / Math.PI;
            return t <= sweep + tolerance || 360 - t <= tolerance;
        }

        private static double YayAcisi(DxfEntity e)
        {
            double angle = PozitifAci(e.BitisAcisi - e.BaslangicAcisi);
            return angle <= YonToleransi ? 360 : angle;
        }

        private static bool CizgiGecerli(DxfEntity e)
            => e != null && e.Tip == "LINE" && e.Noktalar.Length >= 2 && Sonlu(e.Noktalar[0]) && Sonlu(e.Noktalar[^1]) &&
               (e.Noktalar[^1] - e.Noktalar[0]).Length > UzunlukToleransi;

        private static bool Sonlu(Point p)
            => double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) <= 1e12 && Math.Abs(p.Y) <= 1e12;
        private static bool Ucta(DxfEntity e, Point p) => Yakin(e.Noktalar[0], p) || Yakin(e.Noktalar[^1], p);
        private static bool Yakin(Point a, Point b) => (a - b).Length <= UzunlukToleransi;
        private static void EkleTekil(List<Point> points, Point point) { if (!points.Any(p => Yakin(p, point))) points.Add(point); }
        private static double Capraz(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
        private static double PozitifAci(double angle) { angle %= 360; return angle < 0 ? angle + 360 : angle; }
        private static double Aci(Vector v) => PozitifAci(Math.Atan2(v.Y, v.X) * 180 / Math.PI);
        private static Point YayNoktasi(Point center, double radius, double angle)
        {
            double radians = angle * Math.PI / 180;
            return center + new Vector(Math.Cos(radians), Math.Sin(radians)) * radius;
        }
        private static bool Basarisiz(string message, out string? hata) { hata = message; return false; }
    }
}
