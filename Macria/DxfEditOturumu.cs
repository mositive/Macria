using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace Macria
{
    // Kimlik, ekrandaki sıra/handle değil; değişmez kaynak belgesi ve byte aralığıdır.
    internal sealed class DxfKaynakKayit
    {
        internal DxfKaynakKayit(DxfKaynakBelge belge, string tip, int baslangic, int bitis,
            int ikiliSirasi, string bolum, List<(int Kod, string Deger)> kodlar)
        {
            Belge = belge;
            Tip = tip;
            Baslangic = baslangic;
            Bitis = bitis;
            IkiliSirasi = ikiliSirasi;
            Bolum = bolum;
            Kodlar = kodlar;
            Handle = kodlar.FirstOrDefault(x => x.Kod == 5 || x.Kod == 105).Deger ?? "";
        }

        public string Tip { get; }
        public int Baslangic { get; }
        public int Bitis { get; }
        public string Handle { get; }
        internal bool Yeni => Baslangic < 0;
        public string? DuzenlemeEngeli { get; internal set; }
        internal DxfKaynakBelge Belge { get; }
        internal int IkiliSirasi { get; }
        internal string Bolum { get; }
        internal List<(int Kod, string Deger)> Kodlar { get; }
    }

    // Orijinal baytları korur; yalnızca silme/değer yamaları ve yeni kayıt ekleme yapar.
    internal sealed class DxfKaynakBelge
    {
        private readonly byte[] _orijinal;
        private readonly Dictionary<int, DxfKaynakKayit> _entitiyKayitlari = new();
        private readonly HashSet<DxfKaynakKayit> _kayitlar = new();
        private readonly HashSet<DxfKaynakKayit> _yeniKayitlar = new();
        private List<Ikili> _ikililer = new();
        private int _entitiesSonu;
        private int? _handseedSirasi;
        private ulong _sonrakiHandle = 1;
        private string? _yeniKayitEngeli;
        private bool _altSinifKullan;
        private string _satirSonu = "\r\n";

        private DxfKaynakBelge(byte[] orijinal)
        {
            _orijinal = (byte[])orijinal.Clone();
        }

        private readonly record struct Satir(int Baslangic, int Bitis, string Metin);
        private readonly record struct Ikili(int Kod, string Deger, int Baslangic, int Sira,
            int DegerBaslangici, int DegerBitisi);

        public static DxfKaynakBelge? Olustur(byte[] orijinal, out string? hata)
        {
            hata = null;
            try
            {
                if (orijinal.Length == 0) throw new FormatException("DXF dosyası boş.");
                if (orijinal.AsSpan().StartsWith(Encoding.ASCII.GetBytes("AutoCAD Binary DXF")))
                    throw new FormatException("Binary DXF düzenleme desteklenmiyor; yalnızca ASCII DXF destekleniyor.");
                if (orijinal[0] == 0xEF || orijinal[0] == 0xFF || orijinal[0] == 0xFE
                    || Array.IndexOf(orijinal, (byte)0) >= 0)
                    throw new FormatException("BOM/UTF-16/UTF-32 veya binary içeren DXF güvenli düzenleme için desteklenmiyor.");

                var satirlar = new List<Satir>();
                int bas = 0;
                while (bas < orijinal.Length)
                {
                    int son = bas;
                    while (son < orijinal.Length && orijinal[son] != 10 && orijinal[son] != 13) son++;
                    string metin = Encoding.Latin1.GetString(orijinal, bas, son - bas);
                    int bitis = son;
                    if (bitis < orijinal.Length && orijinal[bitis++] == 13
                        && bitis < orijinal.Length && orijinal[bitis] == 10) bitis++;
                    satirlar.Add(new Satir(bas, bitis, metin));
                    bas = bitis;
                }
                // EOF sonundaki boş satırlar tutulur fakat yeni bir group-code sayılmaz.
                int satirSayisi = satirlar.Count;
                while (satirSayisi > 0 && string.IsNullOrWhiteSpace(satirlar[satirSayisi - 1].Metin)) satirSayisi--;
                if (satirSayisi % 2 != 0) throw new FormatException("DXF group-code/değer satır çiftleri eksik.");

                var ikililer = new List<Ikili>();
                for (int i = 0; i < satirSayisi; i += 2)
                {
                    if (!int.TryParse(satirlar[i].Metin.Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int kod) || kod < 0 || kod > 1071)
                        throw new FormatException($"DXF group-code satırı geçersiz: {i + 1}.");
                    Satir degerSatiri = satirlar[i + 1];
                    int sol = 0, sag = degerSatiri.Metin.Length;
                    while (sol < sag && char.IsWhiteSpace(degerSatiri.Metin[sol])) sol++;
                    while (sag > sol && char.IsWhiteSpace(degerSatiri.Metin[sag - 1])) sag--;
                    ikililer.Add(new Ikili(kod, degerSatiri.Metin.Trim(), satirlar[i].Baslangic, i / 2,
                        degerSatiri.Baslangic + sol, degerSatiri.Baslangic + sag));
                }
                var belge = new DxfKaynakBelge(orijinal);
                belge._ikililer = ikililer;
                Satir ilkSatir = satirlar[0];
                int ilkMetinSonu = ilkSatir.Baslangic + ilkSatir.Metin.Length;
                if (ilkSatir.Bitis > ilkMetinSonu)
                    belge._satirSonu = Encoding.ASCII.GetString(orijinal, ilkMetinSonu, ilkSatir.Bitis - ilkMetinSonu);
                var tumKayitlar = new List<DxfKaynakKayit>();
                var bolumler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string bolum = "";
                bool eof = false;
                bool blokAcik = false;
                int at = 0;
                while (at < ikililer.Count)
                {
                    Ikili ilk = ikililer[at];
                    if (ilk.Kod != 0)
                    {
                        if (ilk.Kod != 999) throw new FormatException("DXF kayıt sınırı güvenli biçimde belirlenemedi.");
                        at++;
                        continue;
                    }
                    int sonraki = at + 1;
                    while (sonraki < ikililer.Count && ikililer[sonraki].Kod != 0) sonraki++;
                    string tip = ilk.Deger.ToUpperInvariant();
                    if (tip.Length == 0 || eof) throw new FormatException("DXF EOF/kayıt yapısı geçersiz.");
                    var kodlar = new List<(int Kod, string Deger)>();
                    for (int k = at + 1; k < sonraki; k++) kodlar.Add((ikililer[k].Kod, ikililer[k].Deger));

                    if (tip == "SECTION")
                    {
                        if (bolum.Length != 0 || kodlar.Count == 0 || kodlar[0].Kod != 2)
                            throw new FormatException("DXF SECTION yapısı geçersiz.");
                        bolum = kodlar[0].Deger.ToUpperInvariant();
                        if (bolum.Length == 0 || !bolumler.Add(bolum))
                            throw new FormatException("DXF bölüm adı boş veya yinelenmiş.");
                        // HEADER içindeki hard pointer'ları da gör; $HANDSEED ise entity handle'ı değildir.
                        int bitis = sonraki < ikililer.Count ? ikililer[sonraki].Baslangic : orijinal.Length;
                        tumKayitlar.Add(new DxfKaynakKayit(belge, tip, ilk.Baslangic, bitis,
                            ilk.Sira, bolum, kodlar));
                    }
                    else if (tip == "ENDSEC")
                    {
                        if (bolum.Length == 0 || blokAcik) throw new FormatException("DXF ENDSEC/BLOCK yapısı geçersiz.");
                        if (bolum == "ENTITIES") belge._entitiesSonu = ilk.Baslangic;
                        bolum = "";
                    }
                    else if (tip == "EOF")
                    {
                        if (bolum.Length != 0 || kodlar.Any(x => x.Kod != 999))
                            throw new FormatException("DXF EOF yapısı geçersiz.");
                        eof = true;
                    }
                    else
                    {
                        if (bolum.Length == 0) throw new FormatException("DXF kaydı herhangi bir SECTION içinde değil.");
                        if (bolum == "BLOCKS")
                        {
                            if (tip == "BLOCK")
                            {
                                if (blokAcik) throw new FormatException("DXF BLOCK sınırları geçersiz.");
                                blokAcik = true;
                            }
                            else if (tip == "ENDBLK")
                            {
                                if (!blokAcik) throw new FormatException("DXF ENDBLK sınırı geçersiz.");
                                blokAcik = false;
                            }
                        }
                        int bitis = sonraki < ikililer.Count ? ikililer[sonraki].Baslangic : orijinal.Length;
                        var kayit = new DxfKaynakKayit(belge, tip, ilk.Baslangic, bitis,
                            ilk.Sira, bolum, kodlar);
                        tumKayitlar.Add(kayit);
                        belge._kayitlar.Add(kayit);
                        if (bolum == "ENTITIES") belge._entitiyKayitlari.Add(ilk.Sira, kayit);
                    }
                    at = sonraki;
                }
                if (!eof || !bolumler.Contains("ENTITIES"))
                    throw new FormatException("DXF ENTITIES veya EOF bölümü eksik.");
                belge.BagimliliklariDenetle(tumKayitlar);
                belge.YeniKayitKurallariniBelirle(tumKayitlar);
                return belge;
            }
            catch (FormatException ex)
            {
                hata = ex.Message;
                return null;
            }
        }

        private void BagimliliklariDenetle(List<DxfKaynakKayit> tumKayitlar)
        {
            var handlelar = new Dictionary<string, List<DxfKaynakKayit>>(StringComparer.OrdinalIgnoreCase);
            var referanslar = new Dictionary<string, HashSet<DxfKaynakKayit>>(StringComparer.OrdinalIgnoreCase);
            foreach (DxfKaynakKayit kayit in tumKayitlar)
            {
                foreach ((int kod, string deger) in kayit.Kodlar)
                {
                    if (kod == 5 || kod == 105)
                    {
                        if (kayit.Tip == "SECTION") continue;
                        string kimlik = HandleAnahtari(deger);
                        if (!handlelar.TryGetValue(kimlik, out var liste)) handlelar.Add(kimlik, liste = new());
                        liste.Add(kayit);
                    }
                    else if ((kod >= 320 && kod <= 369) || (kod >= 390 && kod <= 399)
                        || kod == 480 || kod == 481 || kod == 1005)
                    {
                        string kimlik = HandleAnahtari(deger);
                        if (!referanslar.TryGetValue(kimlik, out var liste)) referanslar.Add(kimlik, liste = new());
                        liste.Add(kayit);
                    }
                }
            }
            foreach (DxfKaynakKayit kayit in _entitiyKayitlari.Values)
            {
                if (kayit.Tip != "LINE" && kayit.Tip != "CIRCLE" && kayit.Tip != "ARC")
                    kayit.DuzenlemeEngeli = "Bu entity tipi ilk aşama DXF Edit Modunda desteklenmiyor.";
                else if (DuzlemDisi(kayit))
                    kayit.DuzenlemeEngeli = "3B/yükselti/kalınlık veya varsayılan dışı OCS yönü bulunan entity bu 2B düzenleme aşamasında desteklenmiyor.";
                else if (kayit.Kodlar.Any(x => x.Kod == 102 || x.Kod == 350 || x.Kod == 360))
                    kayit.DuzenlemeEngeli = "Reactor/extension dictionary veya sahiplik bağlantısı bulunan entity güvenli silme için desteklenmiyor.";
                else if (kayit.Kodlar.Any(x => x.Kod == 5 || x.Kod == 105)
                    && (kayit.Kodlar.Count(x => x.Kod == 5 || x.Kod == 105) != 1
                    || kayit.Handle.Length == 0 || !kayit.Handle.All(Uri.IsHexDigit)
                    || HandleAnahtari(kayit.Handle) == "0" || handlelar[HandleAnahtari(kayit.Handle)].Count != 1))
                    kayit.DuzenlemeEngeli = "Entity handle'ı geçersiz veya yinelenmiş; güvenli silme engellendi.";
                else if (kayit.Handle.Length != 0 && referanslar.TryGetValue(HandleAnahtari(kayit.Handle), out var bagli)
                    && bagli.Any(x => !ReferenceEquals(x, kayit)))
                    kayit.DuzenlemeEngeli = "Başka DXF kayıtları bu entity'ye referans veriyor; bağlı kayıtları bozmamak için silme engellendi.";
            }
        }

        private static string HandleAnahtari(string handle)
        {
            string sonuc = handle.Trim().TrimStart('0').ToUpperInvariant();
            return sonuc.Length == 0 ? "0" : sonuc;
        }

        private static bool DuzlemDisi(DxfKaynakKayit kayit)
        {
            foreach ((int kod, string deger) in kayit.Kodlar)
            {
                if (kod != 30 && kod != 31 && kod != 38 && kod != 39
                    && kod != 210 && kod != 220 && kod != 230) continue;
                if (!double.TryParse(deger, NumberStyles.Float, CultureInfo.InvariantCulture, out double sayi)
                    || !double.IsFinite(sayi) || sayi != (kod == 230 ? 1 : 0)) return true;
            }
            return false;
        }

        private void YeniKayitKurallariniBelirle(List<DxfKaynakKayit> kayitlar)
        {
            ulong enBuyuk = 0;
            foreach (DxfKaynakKayit kayit in kayitlar)
                foreach ((int kod, string deger) in kayit.Kodlar.Where(x =>
                    ((x.Kod == 5 || x.Kod == 105) && kayit.Tip != "SECTION") ||
                    (x.Kod >= 320 && x.Kod <= 369) || (x.Kod >= 390 && x.Kod <= 399) ||
                    x.Kod == 480 || x.Kod == 481 || x.Kod == 1005))
                {
                    if (!ulong.TryParse(deger, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong handle))
                        _yeniKayitEngeli = "Belgede geçersiz/aşırı büyük handle var; yeni kayıt ekleme güvenli değil.";
                    else enBuyuk = Math.Max(enBuyuk, handle);
                }
            DxfKaynakKayit? header = kayitlar.FirstOrDefault(x => x.Tip == "SECTION" && x.Bolum == "HEADER");
            if (header != null)
            {
                var headerIkilileri = _ikililer.Where(x => x.Baslangic >= header.Baslangic && x.Baslangic < header.Bitis).ToArray();
                Ikili[] surumler = headerIkilileri.Where(x => x.Kod == 9 && x.Deger == "$ACADVER").ToArray();
                if (surumler.Length == 1 && surumler[0].Sira + 1 < _ikililer.Count)
                {
                    Ikili surum = _ikililer[surumler[0].Sira + 1];
                    if (surum.Kod == 1 && surum.Deger.StartsWith("AC", StringComparison.Ordinal)
                        && int.TryParse(surum.Deger.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int kod)
                        && kod >= 1009)
                        _altSinifKullan = kod >= 1012;
                    else _yeniKayitEngeli = "DXF sürümü yeni kayıt ekleme için güvenle belirlenemedi.";
                }
                else _yeniKayitEngeli = "DXF $ACADVER sürümü eksik/yinelenmiş; yeni kayıt ekleme desteklenmiyor.";
                Ikili[] seedler = headerIkilileri.Where(x => x.Kod == 9 && x.Deger == "$HANDSEED").ToArray();
                if (seedler.Length > 1) _yeniKayitEngeli = "DXF $HANDSEED yinelenmiş; yeni handle üretimi engellendi.";
                else if (seedler.Length == 1)
                {
                    int sira = seedler[0].Sira + 1;
                    if (sira >= _ikililer.Count || _ikililer[sira].Kod != 5
                        || !ulong.TryParse(_ikililer[sira].Deger, NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture, out ulong seed) || seed == 0)
                        _yeniKayitEngeli = "DXF $HANDSEED geçersiz; yeni handle üretimi engellendi.";
                    else
                    {
                        _handseedSirasi = sira;
                        _sonrakiHandle = seed;
                    }
                }
            }
            else _yeniKayitEngeli = "DXF HEADER sürümü yok; yeni kayıt ekleme desteklenmiyor.";
            if (enBuyuk == ulong.MaxValue) _yeniKayitEngeli = "DXF handle aralığı tükenmiş.";
            else _sonrakiHandle = Math.Max(_sonrakiHandle, enBuyuk + 1);
        }

        private static readonly HashSet<int> OrtakKodlar = new()
        {
            330, 67, 410, 8, 6, 62, 420, 430, 440, 48, 60, 370, 390, 347, 284
        };

        internal void YeniKayitOnDogrula(DxfEntity entity, DxfKaynakKayit ozellikKaynak)
        {
            if (_yeniKayitEngeli != null) throw new InvalidOperationException(_yeniKayitEngeli);
            if (!KayitGecerli(ozellikKaynak)) throw new InvalidOperationException("Yeni entity özellik kaynağı bu belgeye ait değil.");
            if (ozellikKaynak.Kodlar.Where(x => OrtakKodlar.Contains(x.Kod)).GroupBy(x => x.Kod).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Yinelenen ortak entity özellikleri güvenli miras alınamıyor.");
            GeometriDogrula(entity);
            if (entity.Tip != "LINE" && entity.Tip != "ARC")
                throw new InvalidOperationException("Bu aşamada yalnızca yeni LINE/ARC kayıtları destekleniyor.");
            if (_sonrakiHandle >= ulong.MaxValue)
                throw new InvalidOperationException("Yeni entity ve sonraki $HANDSEED için handle alanı kalmadı.");
        }

        internal DxfKaynakKayit YeniKayit(DxfEntity entity, DxfKaynakKayit ozellikKaynak)
        {
            YeniKayitOnDogrula(entity, ozellikKaynak);
            string handle = (_sonrakiHandle++).ToString("X", CultureInfo.InvariantCulture);
            var kodlar = new List<(int Kod, string Deger)> { (5, handle) };
            kodlar.AddRange(ozellikKaynak.Kodlar.Where(x => OrtakKodlar.Contains(x.Kod)));
            if (kodlar.All(x => x.Kod != 8)) kodlar.Add((8, "0"));
            var kayit = new DxfKaynakKayit(this, entity.Tip, -1, -1, -1, "ENTITIES", kodlar);
            _yeniKayitlar.Add(kayit);
            _kayitlar.Add(kayit);
            return kayit;
        }

        internal ulong HandleKontrolNoktasi => _sonrakiHandle;
        internal void YeniKayitlariGeriAl(ulong kontrol)
        {
            foreach (DxfKaynakKayit kayit in _yeniKayitlar.Where(x => ulong.Parse(x.Handle,
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) >= kontrol).ToArray())
            {
                _yeniKayitlar.Remove(kayit);
                _kayitlar.Remove(kayit);
            }
            _sonrakiHandle = kontrol;
        }

        internal static bool OrtakDuzlemAyni(DxfKaynakKayit bir, DxfKaynakKayit iki)
        {
            foreach (int kod in new[] { 8, 330, 67, 410 })
            {
                string varsayilan = kod == 8 ? "0" : kod == 67 ? "0" : "";
                string a = bir.Kodlar.FirstOrDefault(x => x.Kod == kod).Deger ?? varsayilan;
                string b = iki.Kodlar.FirstOrDefault(x => x.Kod == kod).Deger ?? varsayilan;
                if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static void GeometriDogrula(DxfEntity entity)
        {
            if (entity.Noktalar.Length < 2 || entity.Noktalar.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                throw new InvalidOperationException("Entity geometrisi eksik veya sonlu değil.");
            if (entity.Tip == "LINE" && (entity.Noktalar.Length != 2 || (entity.Noktalar[1] - entity.Noktalar[0]).Length <= 1e-9))
                throw new InvalidOperationException("Sıfır/eksik LINE geometrisi kaydedilemez.");
            if (entity.Tip == "ARC" && (!double.IsFinite(entity.Merkez.X) || !double.IsFinite(entity.Merkez.Y)
                || !double.IsFinite(entity.Radius) || entity.Radius <= 0 || !double.IsFinite(entity.BaslangicAcisi)
                || !double.IsFinite(entity.BitisAcisi)))
                throw new InvalidOperationException("ARC merkezi/radius/açıları geçersiz.");
        }

        internal void DegisiklikOnDogrula(DxfKaynakKayit kayit, DxfEntity entity)
        {
            if (!KayitGecerli(kayit) || entity.Tip != kayit.Tip || entity.Tip != "LINE")
                throw new InvalidOperationException("Yalnızca mevcut 2B LINE geometri değerleri düzenlenebilir.");
            GeometriDogrula(entity);
            if (!kayit.Yeni)
                foreach (int kod in new[] { 10, 20, 11, 21 }) DegerIkilisi(kayit, kod);
        }

        private Ikili DegerIkilisi(DxfKaynakKayit kayit, int kod)
        {
            Ikili[] adaylar = _ikililer.Where(x => x.Baslangic > kayit.Baslangic && x.Baslangic < kayit.Bitis && x.Kod == kod).ToArray();
            if (adaylar.Length != 1 || adaylar[0].DegerBitisi <= adaylar[0].DegerBaslangici
                || !double.TryParse(adaylar[0].Deger, NumberStyles.Float, CultureInfo.InvariantCulture, out double deger)
                || !double.IsFinite(deger))
                throw new InvalidOperationException($"LINE group-code {kod} eksik/yinelenmiş/geçersiz; güvenli değer yaması yapılamıyor.");
            return adaylar[0];
        }

        public DxfKaynakKayit? KayitBul(int originalPairIndex)
        {
            return _entitiyKayitlari.GetValueOrDefault(originalPairIndex);
        }

        internal bool KayitGecerli(DxfKaynakKayit kayit)
        {
            return ReferenceEquals(kayit.Belge, this) && _kayitlar.Contains(kayit)
                && kayit.Bolum == "ENTITIES" && kayit.DuzenlemeEngeli == null;
        }

        internal byte[] KaynakKopyasi() => (byte[])_orijinal.Clone();

        // Aynı byte-yama yolu: dokunulmayan kayıtlar/bölümler birebir korunur.
        internal byte[] DuzenlemeleriUygula(IEnumerable<DxfKaynakKayit> silinenler,
            IReadOnlyDictionary<DxfKaynakKayit, DxfEntity> degisenler, IEnumerable<DxfEntity> eklenenler)
        {
            var yamalar = new List<(int Bas, int Son, byte[] Veri)>();
            var silinen = silinenler.ToHashSet();
            foreach (DxfKaynakKayit kayit in silinen.Where(x => !x.Yeni))
            {
                if (!KayitGecerli(kayit)) throw new InvalidOperationException("Geçersiz silme kaydı.");
                yamalar.Add((kayit.Baslangic, kayit.Bitis, Array.Empty<byte>()));
            }
            foreach (var (kayit, entity) in degisenler.Where(x => !x.Key.Yeni && !silinen.Contains(x.Key)))
            {
                DegisiklikOnDogrula(kayit, entity);
                foreach (var (kod, deger) in new[] { (10, entity.Noktalar[0].X), (20, entity.Noktalar[0].Y),
                    (11, entity.Noktalar[^1].X), (21, entity.Noktalar[^1].Y) })
                {
                    Ikili ikili = DegerIkilisi(kayit, kod);
                    double eski = double.Parse(ikili.Deger, CultureInfo.InvariantCulture);
                    if (eski != deger) yamalar.Add((ikili.DegerBaslangici, ikili.DegerBitisi,
                        Encoding.ASCII.GetBytes(deger.ToString("R", CultureInfo.InvariantCulture))));
                }
            }
            DxfEntity[] yeni = eklenenler.Where(x => !silinen.Contains(x.KaynakKayit!)).ToArray();
            if (yeni.Length > 0)
            {
                var metin = new StringBuilder();
                foreach (DxfEntity entity in yeni)
                {
                    DxfKaynakKayit kayit = entity.KaynakKayit!;
                    if (!_yeniKayitlar.Contains(kayit)) throw new InvalidOperationException("Geçersiz yeni kayıt.");
                    void Ekle(int kod, string deger) => metin.Append(kod.ToString(CultureInfo.InvariantCulture))
                        .Append(_satirSonu).Append(deger).Append(_satirSonu);
                    void Sayi(int kod, double deger) => Ekle(kod, deger.ToString("R", CultureInfo.InvariantCulture));
                    Ekle(0, entity.Tip);
                    Ekle(5, kayit.Handle);
                    foreach (var p in kayit.Kodlar.Where(x => x.Kod == 330)) Ekle(p.Kod, p.Deger);
                    if (_altSinifKullan) Ekle(100, "AcDbEntity");
                    foreach (var p in kayit.Kodlar.Where(x => x.Kod != 5 && x.Kod != 330)) Ekle(p.Kod, p.Deger);
                    if (_altSinifKullan) Ekle(100, entity.Tip == "LINE" ? "AcDbLine" : "AcDbCircle");
                    Point p0 = entity.Tip == "LINE" ? entity.Noktalar[0] : entity.Merkez;
                    Sayi(10, p0.X); Sayi(20, p0.Y); Sayi(30, 0);
                    if (entity.Tip == "LINE")
                    {
                        Sayi(11, entity.Noktalar[^1].X); Sayi(21, entity.Noktalar[^1].Y); Sayi(31, 0);
                    }
                    else
                    {
                        Sayi(40, entity.Radius);
                        if (_altSinifKullan) Ekle(100, "AcDbArc");
                        Sayi(50, entity.BaslangicAcisi); Sayi(51, entity.BitisAcisi);
                    }
                }
                yamalar.Add((_entitiesSonu, _entitiesSonu, Encoding.Latin1.GetBytes(metin.ToString())));
                if (_handseedSirasi.HasValue)
                {
                    Ikili seed = _ikililer[_handseedSirasi.Value];
                    ulong sonraki = yeni.Max(x => ulong.Parse(x.KaynakKayit!.Handle,
                        NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)) + 1;
                    if (sonraki > ulong.Parse(seed.Deger, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture))
                        yamalar.Add((seed.DegerBaslangici, seed.DegerBitisi,
                            Encoding.ASCII.GetBytes(sonraki.ToString("X", CultureInfo.InvariantCulture))));
                }
            }
            using var sonuc = new MemoryStream();
            int onceki = 0;
            foreach (var yama in yamalar.OrderBy(x => x.Bas).ThenBy(x => x.Son))
            {
                if (yama.Bas < onceki || yama.Son > _orijinal.Length || yama.Son < yama.Bas)
                    throw new InvalidOperationException("Çakışan/geçersiz DXF değer yamaları.");
                sonuc.Write(_orijinal, onceki, yama.Bas - onceki);
                sonuc.Write(yama.Veri);
                onceki = yama.Son;
            }
            sonuc.Write(_orijinal, onceki, _orijinal.Length - onceki);
            return sonuc.ToArray();
        }

        public byte[] SilmeleriUygula(IEnumerable<DxfKaynakKayit> silinenler)
        {
            return DuzenlemeleriUygula(silinenler, new Dictionary<DxfKaynakKayit, DxfEntity>(), Array.Empty<DxfEntity>());
        }
    }

    internal sealed class DxfEditOturumu
    {
        private readonly DxfCizim _orijinalModel;
        private readonly DxfKaynakBelge? _kaynak;
        private readonly string _ilkYol;
        private sealed class Durum
        {
            public readonly HashSet<DxfKaynakKayit> Silinen = new();
            public readonly Dictionary<DxfKaynakKayit, DxfEntity> Degisen = new();
            public readonly List<DxfEntity> Eklenen = new();
            public byte[]? Icerik;
            public Durum Kopya()
            {
                var d = new Durum();
                d.Silinen.UnionWith(Silinen);
                foreach (var p in Degisen) d.Degisen.Add(p.Key, p.Value);
                d.Eklenen.AddRange(Eklenen);
                return d;
            }
        }
        private Durum _durum = new();
        private Durum _kaydedilen = new();
        private readonly Stack<Durum> _geri = new();
        private readonly Stack<Durum> _yinele = new();
        private byte[] _beklenenIcerik;

        public DxfEditOturumu(DxfCizim model, string yol)
        {
            _orijinalModel = model;
            _kaynak = model.KaynakBelge;
            _ilkYol = Path.GetFullPath(yol);
            Yol = _ilkYol;
            Engel = _kaynak == null ? model.DuzenlemeEngeli ?? "Bu DXF'in güvenli kaynak kayıtları bulunamadı." : null;
            _beklenenIcerik = _kaynak?.KaynakKopyasi() ?? Array.Empty<byte>();
        }

        public bool Degisti => _kaynak != null && !DurumIcerigi().AsSpan().SequenceEqual(_beklenenIcerik);
        public bool GeriAlabilir => _geri.Count != 0;
        public bool Yineleabilir => _yinele.Count != 0;
        public string Yol { get; private set; }
        public string? Engel { get; }
        public bool OrijinalHedef => string.Equals(Yol, _ilkYol, StringComparison.OrdinalIgnoreCase);

        public bool Sil(DxfEntity entity, out string? hata)
            => Sil(new[] { entity }, out hata);

        private string? EntityEngeli(DxfEntity entity, HashSet<DxfEntity> mevcut)
        {
            if (!mevcut.Contains(entity)) return "Seçili entity bu oturumun güncel geometrisine ait değil.";
            if (entity.KaynakKayit == null) return "BLOCK/INSERT veya kaynak kaydı eşlenemeyen entity salt okunur.";
            return entity.KaynakKayit.DuzenlemeEngeli ??
                (!_kaynak!.KayitGecerli(entity.KaynakKayit) ? "Entity kaydı bu DXF belgesine ait değil." : null);
        }

        public bool Sil(IEnumerable<DxfEntity> entityler, out string? hata)
        {
            hata = Engel;
            if (hata != null) return false;
            DxfEntity[] secim = entityler.Distinct().ToArray();
            if (secim.Length == 0) { hata = "Önce entity seçin."; return false; }
            var mevcut = Onizleme().Entityler.ToHashSet();
            foreach (DxfEntity entity in secim)
            {
                hata = EntityEngeli(entity, mevcut);
                if (hata != null) return false;
            }
            Durum yeni = _durum.Kopya();
            foreach (DxfEntity entity in secim) yeni.Silinen.Add(entity.KaynakKayit!);
            Commit(yeni);
            return true;
        }

        public bool Uygula(DxfKosePlani plan, out string? hata)
        {
            hata = Engel;
            if (hata != null) return false;
            ulong kontrol = _kaynak!.HandleKontrolNoktasi;
            try
            {
                if (plan.Degisen.Count == 0)
                    throw new InvalidOperationException("Köşe işlem planı boş.");
                var mevcut = Onizleme().Entityler.ToHashSet();
                DxfKaynakKayit? duzlem = null;
                foreach (var (eski, yeni) in plan.Degisen)
                {
                    string? engel = EntityEngeli(eski, mevcut);
                    if (engel != null) throw new InvalidOperationException(engel);
                    _kaynak!.DegisiklikOnDogrula(eski.KaynakKayit!, yeni);
                    duzlem ??= eski.KaynakKayit;
                    if (!DxfKaynakBelge.OrtakDuzlemAyni(duzlem!, eski.KaynakKayit!))
                        throw new InvalidOperationException("Köşe LINE'ları aynı layer/model-paper-space/sahiplik düzleminde olmalı.");
                }
                foreach (DxfYeniEntity ek in plan.Eklenen)
                {
                    if (!plan.Degisen.ContainsKey(ek.OzellikKaynak))
                        throw new InvalidOperationException("Yeni entity özellik kaynağı işlemdeki LINE'lardan biri olmalı.");
                    _kaynak!.YeniKayitOnDogrula(ek.Entity, ek.OzellikKaynak.KaynakKayit!);
                }
                Durum durum = _durum.Kopya();
                foreach (var (eski, yeni) in plan.Degisen)
                {
                    DxfEntity kopya = Kopyala(yeni, eski.KaynakKayit!);
                    if (eski.KaynakKayit!.Yeni)
                        durum.Eklenen[durum.Eklenen.IndexOf(eski)] = kopya;
                    else durum.Degisen[eski.KaynakKayit] = kopya;
                }
                foreach (DxfYeniEntity ek in plan.Eklenen)
                    durum.Eklenen.Add(Kopyala(ek.Entity, _kaynak!.YeniKayit(ek.Entity, ek.OzellikKaynak.KaynakKayit!)));
                // Bütün kayıt/değer yamaları commit'ten önce doğrulanır.
                durum.Icerik = _kaynak!.DuzenlemeleriUygula(durum.Silinen, durum.Degisen, durum.Eklenen);
                if (durum.Icerik.AsSpan().SequenceEqual(DurumIcerigi()))
                    throw new InvalidOperationException("İşlem mevcut geometriyi değiştirmiyor.");
                Commit(durum);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                _kaynak!.YeniKayitlariGeriAl(kontrol);
                hata = ex.Message;
                return false;
            }
        }

        private static DxfEntity Kopyala(DxfEntity e, DxfKaynakKayit kayit) => new DxfEntity
        {
            Tip = e.Tip, Noktalar = (Point[])e.Noktalar.Clone(), Merkez = e.Merkez,
            Radius = e.Radius, BaslangicAcisi = e.BaslangicAcisi, BitisAcisi = e.BitisAcisi, KaynakKayit = kayit
        };

        private void Commit(Durum yeni)
        {
            _geri.Push(_durum);
            _durum = yeni;
            _yinele.Clear();
        }

        public bool GeriAl()
        {
            if (!_geri.TryPop(out Durum? durum)) return false;
            _yinele.Push(_durum);
            _durum = durum;
            return true;
        }

        public bool Yinele()
        {
            if (!_yinele.TryPop(out Durum? durum)) return false;
            _geri.Push(_durum);
            _durum = durum;
            return true;
        }

        public void Vazgec()
        {
            _durum = _kaydedilen.Kopya();
            _geri.Clear();
            _yinele.Clear();
        }

        public DxfCizim Onizleme()
        {
            var sonuc = new DxfCizim
            {
                KaynakBelge = _kaynak,
                DuzenlemeEngeli = Engel,
                NesneSayisi = Math.Max(0, _orijinalModel.NesneSayisi -
                    _durum.Silinen.Count(x => !x.Yeni) + _durum.Eklenen.Count(x => !_durum.Silinen.Contains(x.KaynakKayit!)))
            };
            var silinenYollar = new HashSet<Point[]>();
            foreach (DxfEntity entity in _orijinalModel.Entityler)
            {
                if (entity.KaynakKayit != null && _durum.Silinen.Contains(entity.KaynakKayit))
                    silinenYollar.Add(entity.Noktalar);
                else if (entity.KaynakKayit != null && _durum.Degisen.TryGetValue(entity.KaynakKayit, out DxfEntity? degisen))
                {
                    silinenYollar.Add(entity.Noktalar);
                    sonuc.Entityler.Add(degisen);
                }
                else sonuc.Entityler.Add(entity);
            }
            sonuc.Entityler.AddRange(_durum.Eklenen.Where(x => !_durum.Silinen.Contains(x.KaynakKayit!)));
            var yeniYollar = _durum.Degisen.Where(x => !_durum.Silinen.Contains(x.Key)).Select(x => x.Value.Noktalar)
                .Concat(_durum.Eklenen.Where(x => !_durum.Silinen.Contains(x.KaynakKayit!)).Select(x => x.Noktalar));
            foreach (Point[] yol in _orijinalModel.Yollar.Where(x => !silinenYollar.Contains(x)).Concat(yeniYollar))
            {
                if (silinenYollar.Contains(yol)) continue;
                // Paylaşılan entity metadata'sını/array kimliğini değiştirmeden filtrele.
                sonuc.Yollar.Add(yol);
                foreach (Point p in yol)
                {
                    sonuc.MinX = Math.Min(sonuc.MinX, p.X);
                    sonuc.MaxX = Math.Max(sonuc.MaxX, p.X);
                    sonuc.MinY = Math.Min(sonuc.MinY, p.Y);
                    sonuc.MaxY = Math.Max(sonuc.MaxY, p.Y);
                }
            }
            return sonuc;
        }

        public byte[] Cikti()
        {
            if (_kaynak == null) throw new InvalidOperationException(Engel);
            return (byte[])DurumIcerigi().Clone();
        }

        private byte[] DurumIcerigi() => _durum.Icerik ??=
            _kaynak!.DuzenlemeleriUygula(_durum.Silinen, _durum.Degisen, _durum.Eklenen);

        public string Kaydet(bool uzerineYazmaOnayi)
        {
            if (!uzerineYazmaOnayi) throw new InvalidOperationException("DXF'in üzerine yazmak için kullanıcı onayı gerekli.");
            if (_kaynak == null) throw new InvalidOperationException(Engel);
            if (!Degisti) return "";
            byte[] cikti = Cikti();
            string? gecici = null;
            string? yedek = null;
            try
            {
                if ((File.GetAttributes(Yol) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Bağlantı/reparse-point DXF dosyasının üzerine güvenli kayıt desteklenmiyor; Farklı Kaydet kullanın.");
                byte[] disk;
                // Yedek/geçici çıktı hazırlanırken dış yazmayı engelle. Windows
                // ReplaceFile hedefe yazma erişimi istediği için commit öncesi kapat.
                using (var kaynakDosya = new FileStream(Yol, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete))
                {
                    disk = AkisiOku(kaynakDosya);
                    if (!disk.AsSpan().SequenceEqual(_beklenenIcerik))
                        throw new IOException("DXF dosyası dışarıdan değiştirilmiş. Üzerine yazılmadı; Farklı Kaydet kullanın.");
                    yedek = YedekOlustur(Yol, disk);
                    gecici = GeciciYaz(Yol, cikti);
                }
                // Dosyanın dışarıdan atomik değiştirilmesini de commit öncesi yakala.
                if (!File.ReadAllBytes(Yol).AsSpan().SequenceEqual(disk))
                    throw new IOException("DXF kayıt sırasında dışarıdan değiştirildi; üzerine yazılmadı.");
                File.Replace(gecici, Yol, null);
                gecici = null;
                _beklenenIcerik = cikti;
                _kaydedilen = _durum.Kopya();
                return yedek;
            }
            finally
            {
                if (gecici != null) KendiGeciciDosyasiniSil(gecici, Yol);
            }
        }

        public void FarkliKaydet(string yeniYol)
        {
            if (_kaynak == null) throw new InvalidOperationException(Engel);
            string hedef = Path.GetFullPath(yeniYol);
            if (string.Equals(hedef, _ilkYol, StringComparison.OrdinalIgnoreCase)
                || string.Equals(hedef, Yol, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Farklı Kaydet yeni bir dosya gerektirir; kaynak dosya için onaylı Kaydet kullanılmalıdır.");
            if (File.Exists(hedef) || Directory.Exists(hedef))
                throw new IOException("Farklı Kaydet var olan dosyanın üzerine yazmaz; yeni bir dosya adı seçin.");
            byte[] cikti = Cikti();
            string? gecici = null;
            try
            {
                gecici = GeciciYaz(hedef, cikti);
                File.Move(gecici, hedef, false);
                gecici = null;
                Yol = hedef;
                _beklenenIcerik = cikti;
                _kaydedilen = _durum.Kopya();
            }
            finally
            {
                if (gecici != null) KendiGeciciDosyasiniSil(gecici, hedef);
            }
        }

        private static byte[] AkisiOku(FileStream akis)
        {
            if (akis.Length > int.MaxValue) throw new IOException("DXF dosyası bu düzenleme altyapısı için çok büyük.");
            var sonuc = new byte[(int)akis.Length];
            akis.ReadExactly(sonuc);
            return sonuc;
        }

        private static string YedekOlustur(string yol, byte[] icerik)
        {
            string zaman = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            for (int sayi = 0; sayi < 10000; sayi++)
            {
                string yedek = sayi == 0 ? yol + ".bak" : yol + "." + zaman + "." + sayi + ".bak";
                FileStream akis;
                try
                {
                    akis = new FileStream(yedek, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        4096, FileOptions.WriteThrough);
                }
                catch (IOException) when (File.Exists(yedek) || Directory.Exists(yedek)) { continue; }
                bool tamam = false;
                try
                {
                    using (akis)
                    {
                        akis.Write(icerik);
                        akis.Flush(true);
                    }
                    tamam = true;
                    return yedek;
                }
                finally
                {
                    if (!tamam)
                    {
                        try { File.Delete(yedek); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
            }
            throw new IOException("Çakışmasız DXF yedek dosyası oluşturulamadı; kaynak dosya değiştirilmedi.");
        }

        private static string GeciciYaz(string hedef, byte[] icerik)
        {
            string dizin = Path.GetDirectoryName(Path.GetFullPath(hedef))!;
            string gecici = Path.Combine(dizin, ".macria-dxf-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool olusturuldu = false;
            try
            {
                using var akis = new FileStream(gecici, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough);
                olusturuldu = true;
                akis.Write(icerik);
                akis.Flush(true);
                return gecici;
            }
            catch
            {
                if (olusturuldu) KendiGeciciDosyasiniSil(gecici, hedef);
                throw;
            }
        }

        private static void KendiGeciciDosyasiniSil(string gecici, string hedef)
        {
            string yol = Path.GetFullPath(gecici);
            string dizin = Path.GetDirectoryName(Path.GetFullPath(hedef))!;
            if (!string.Equals(Path.GetDirectoryName(yol), dizin, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(yol).StartsWith(".macria-dxf-", StringComparison.Ordinal)
                || !yol.EndsWith(".tmp", StringComparison.Ordinal)) return;
            try { File.Delete(yol); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
