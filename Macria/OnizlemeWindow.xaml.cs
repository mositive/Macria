using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Macria
{
    // Onizlemenin genis gorunumu. Konsol penceresi gibi ana pencereden
    // beslenir: listede secim degistikce MainWindow buraya yeni cizimi
    // yazar, pencere kendi basina dosya okumaz.
    //
    // Kucuk panelden farki, cizimin yakinlastirilip kaydirilabilmesi;
    // delik ve kucuk kertikler ancak boyle gorulur.
    public partial class OnizlemeWindow : Window
    {
        private const double EnAz = 0.25;
        private const double EnCok = 32.0;
        // Birim yonlerin capraz carpimi: sin(aci), yaklasik 0.0057 derece.
        private const double ParalellikToleransi = 1e-4;
        // Birim yonun kucuk bileseni; sadece eksene cok yakin ciftleri duzelt.
        private const double EksenHizalamaToleransi = 1e-4;

        private string _yol = "";
        private string _beslemeYolu = "";
        private DxfEditOturumu? _editOturumu;
        private bool _editModu;
        private bool _editOturumuBaslatildi;
        private bool _kapanisOnaylandi;
        internal event Action<string>? Kaydedildi;
        internal event Action<string>? OrijinalEditKaydedildi;
        private DxfCizim _cizimModel;
        private DxfEntity? _seciliEntity;
        private readonly HashSet<DxfEntity> _editSecimi = new();
        private readonly Dictionary<DxfEntity, Point> _editTiklamaTaraflari = new();
        private enum KoseKomutTuru { Yok, Pah, Radius, Birlestir }
        private enum KoseKomutAsamasi { CornerOrFirstLineWaiting, SecondLineWaiting, Prepared }
        private KoseKomutTuru _aktifKoseKomutu;
        private KoseKomutAsamasi _koseKomutAsamasi;
        private DxfEntity? _komutIlkCizgisi;
        private Point? _komutIlkTarafi;
        private DxfKosePlani? _kosePlani;
        private readonly List<Point> _komutKoseleri = new();
        private readonly List<(DxfEntity A, DxfEntity B, Point Kose)> _koseAdaylari = new();
        private readonly Dictionary<DxfEntity, (List<DxfEntity> Loop, bool Guvenilir, string? Hata)> _konturOnbellek = new();
        private DxfEntity[]? _hazirKoseCizgileri;
        // Komut hedefi/vurgusu normal Ctrl/rectangle seçiminden bağımsızdır.
        private DxfEntity[] _komutCizgileri = Array.Empty<DxfEntity>();
        private DxfEntity[]? _komutKonturu;
        private bool _komutHedefSecildi;
        private Point? _sonKoseMouse;
        private bool _koseSecenekleriAyarlaniyor;
        private bool _tumKonturOnizlemeHazir;
        private int _komutKonturSayisi;
        private string? _koseHatasi;
        private const double KoseHitPiksel = 10;
        private const double KoseAdayFarkiPiksel = 2;
        private const int EnCokMouseKoseCizgisi = 512;
        private bool _cerceveSeciliyor;
        private bool _cerceveyeEkle;
        private string _kip = "Sec";
        private Point? _ilkMesafeNoktasi;
        private Point? _sonMesafeNoktasi;
        private Point? _olcuOnizlemeNoktasi;
        private Point? _olcuBaslangicNoktasi;
        private DxfEntity? _ilkOlcuCizgisi;
        private DxfEntity? _ikinciOlcuCizgisi;
        private Vector? _cizgiOlcuYonu;
        private CizgiOlcuAsamasi _cizgiOlcuAsamasi = CizgiOlcuAsamasi.FirstLineWaiting;
        private double _cizgiOlcuUzantiOfseti;
        private double _cizgiOlcuYerlesimOfseti;
        private readonly List<SnapNoktasi> _snapNoktalari = new List<SnapNoktasi>();
        private SnapNoktasi? _aktifSnap;
        private readonly MatrixTransform _modelToScreen = new MatrixTransform();
        private double _modelOlcek = 1;
        private Rect _editReferansBounds = Rect.Empty;

        private sealed class SnapNoktasi
        {
            public Point Nokta;
            public string Tip = "";
            public int Oncelik;
        }

        private enum CizgiOlcuAsamasi
        {
            FirstLineWaiting,
            SecondLineWaiting,
            PlacementWaiting,
            Completed
        }

        private enum CizgiOlcuEkseni
        {
            Egik,
            YatayCizgiler,
            DikeyCizgiler
        }

        private bool _surukleniyor;
        private Point _basildigiNokta;
        private double _basXKaydir;
        private double _basYKaydir;

        public OnizlemeWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);
            Closing += (s, e) => { if (!KapanisaIzinVer()) e.Cancel = true; };
            cizimAlani.LostMouseCapture += (s, e) =>
            {
                if (!_surukleniyor) return;
                _surukleniyor = false;
                _cerceveSeciliyor = false;
                secimCercevesi.Visibility = Visibility.Collapsed;
                cizimAlani.Cursor = null;
            };

            Sigdir();
            EditDurumunuGuncelle();
        }

        // ================= ANA PENCEREDEN GELENLER =================

        internal void Goster(string parca, DxfCizim cizimModel, string olcu, string dosyaYolu)
        {
            if (cizimModel == null)
            {
                Bosalt(parca, "Çizilebilir DXF bulunamadı.", dosyaYolu);
                return;
            }
            if (!BelgeDegisimineIzinVer(dosyaYolu)) return;
            txtParca.Text = string.IsNullOrEmpty(parca) ? "—" : parca;
            _yol = dosyaYolu ?? "";
            _beslemeYolu = _yol;
            _editOturumu = new DxfEditOturumu(cizimModel, _yol);
            _editOturumuBaslatildi = false;
            chkEditModu.IsChecked = false;
            _editSecimi.Clear();
            _cizimModel = cizimModel;
            _seciliEntity = null;
            _ilkMesafeNoktasi = null;
            _sonMesafeNoktasi = null;
            _olcuOnizlemeNoktasi = null;
            _olcuBaslangicNoktasi = null;
            CizgiOlcuSeciminiTemizle();
            SnapNoktalariniHazirla();

            txtDosya.Text = _yol;
            txtDosya.ToolTip = _yol.Length > 0 ? _yol : null;
            btnAc.IsEnabled = _yol.Length > 0;

            cizim.Data = cizimModel.Geometri();
            _editReferansBounds = cizim.Data?.Bounds ?? Rect.Empty;
            cizim.RenderTransform = _modelToScreen;
            seciliCizim.Data = null;
            seciliCizim.Visibility = Visibility.Collapsed;
            seciliCizim.RenderTransform = Transform.Identity;
            OlcuGeometrisiniTemizle();
            cizim.Visibility = Visibility.Visible;
            txtMesaj.Visibility = Visibility.Collapsed;

            txtOlcu.Text = olcu ?? "";
            txtEntityInfo.Text = "";
            SnapGoster(null);

            // Yeni parca gelince eski yakinlastirma anlamini yitirir
            Sigdir();
            ModelDonusumunuGuncelle();
            EditDurumunuGuncelle();
        }

        public void Bosalt(string parca, string mesaj, string dosyaYolu)
        {
            if (!BelgeDegisimineIzinVer(dosyaYolu)) return;
            txtParca.Text = string.IsNullOrEmpty(parca) ? "—" : parca;
            _yol = dosyaYolu ?? "";
            _beslemeYolu = _yol;
            _editOturumu = null;
            _editOturumuBaslatildi = false;
            chkEditModu.IsChecked = false;
            _editSecimi.Clear();

            txtDosya.Text = _yol;
            txtDosya.ToolTip = _yol.Length > 0 ? _yol : null;
            btnAc.IsEnabled = _yol.Length > 0;

            cizim.Data = null;
            _editReferansBounds = Rect.Empty;
            cizim.RenderTransform = Transform.Identity;
            seciliCizim.Data = null;
            seciliCizim.Visibility = Visibility.Collapsed;
            seciliCizim.RenderTransform = Transform.Identity;
            cizim.Visibility = Visibility.Collapsed;

            txtMesaj.Visibility = Visibility.Visible;
            txtMesaj.Text = mesaj;

            txtOlcu.Text = "";
            txtEntityInfo.Text = "";
            _cizimModel = null;
            _seciliEntity = null;
            _ilkMesafeNoktasi = null;
            _sonMesafeNoktasi = null;
            _olcuOnizlemeNoktasi = null;
            _olcuBaslangicNoktasi = null;
            CizgiOlcuSeciminiTemizle();
            _snapNoktalari.Clear();
            SnapGoster(null);
            OlcuGeometrisiniTemizle();
            Sigdir();
            EditDurumunuGuncelle();
        }

        // ================= KONTROLLU DXF EDIT OTURUMU =================

        private void EditModu_Degisti(object sender, RoutedEventArgs e)
        {
            _editModu = chkEditModu.IsChecked == true && _editOturumu != null &&
                        _editOturumu.Engel == null;
            if (chkEditModu.IsChecked == true && !_editModu)
            {
                chkEditModu.IsChecked = false;
                txtEntityInfo.Text = _editOturumu?.Engel ?? "Düzenlenebilir ASCII DXF bulunamadı.";
            }
            if (_editModu)
            {
                _editOturumuBaslatildi = true;
                btnSec_Click(sender, e);
            }
            btnMesafe.IsEnabled = !_editModu;
            btnCizgiCizgi.IsEnabled = !_editModu;
            if (!_editModu) btnTemizle_Click(this, new RoutedEventArgs());
            EditDurumunuGuncelle();
        }

        private void EditDurumunuGuncelle()
        {
            bool dirty = _editOturumu?.Degisti == true;
            Title = "Macria — DXF Önizleme" + (dirty ? " *" : "");
            txtBaslik.Text = "DXF Önizleme" + (dirty ? " *" : "");
            txtEditDurumu.Visibility = _editModu ? Visibility.Visible : Visibility.Collapsed;
            chkEditModu.IsEnabled = _editOturumu != null && _editOturumu.Engel == null;
            chkEditModu.ToolTip = _editOturumu?.Engel ??
                "Kaynak 2B LINE / CIRCLE / ARC seçme/silme, LINE köşe işlemleri; BLOCK/INSERT salt okunur";
            btnSil.IsEnabled = _editModu && _editSecimi.Count > 0 &&
                _editSecimi.All(x => x.KaynakKayit != null && x.KaynakKayit.DuzenlemeEngeli == null);
            btnPah.IsEnabled = btnRadius.IsEnabled = _editModu;
            btnBirlestir.IsEnabled = _editModu;
            editAraclari.Visibility = _editModu ? Visibility.Visible : Visibility.Collapsed;
            btnSec.Visibility = _editModu ? Visibility.Collapsed : Visibility.Visible;
            btnUndo.IsEnabled = _editModu && _editOturumu?.GeriAlabilir == true;
            btnRedo.IsEnabled = _editModu && _editOturumu?.Yineleabilir == true;
            btnKaydet.IsEnabled = _editModu && dirty;
            btnFarkliKaydet.IsEnabled = _editModu && _editOturumu != null;
            EditAracVurgusunuGuncelle();
        }

        private void EditAracVurgusunuGuncelle()
        {
            foreach (var arac in new[] { (btnEditSec, KoseKomutTuru.Yok), (btnBirlestir, KoseKomutTuru.Birlestir),
                (btnPah, KoseKomutTuru.Pah), (btnRadius, KoseKomutTuru.Radius) })
            {
                bool aktif = _editModu && _aktifKoseKomutu == arac.Item2;
                arac.Item1.Background = aktif ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
                arac.Item1.BorderBrush = (Brush)FindResource(aktif ? "AccentHoverBrush" : "BorderBrush");
                arac.Item1.BorderThickness = new Thickness(1);
            }
        }

        private void btnSil_Click(object sender, RoutedEventArgs e) => SeciliEntityyiSil();
        private void btnUndo_Click(object sender, RoutedEventArgs e) => EditGeriAl();
        private void btnRedo_Click(object sender, RoutedEventArgs e) => EditYinele();
        private void btnKaydet_Click(object sender, RoutedEventArgs e) => EditKaydet();

        private void EditSec(DxfEntity? entity, bool ekle)
        {
            if (!_editModu) return;
            if (!ekle) { _editSecimi.Clear(); _editTiklamaTaraflari.Clear(); }
            if (entity != null)
            {
                if (!_editSecimi.Add(entity) && ekle)
                { _editSecimi.Remove(entity); _editTiklamaTaraflari.Remove(entity); }
            }
            EditSecimGoster();
        }

        private void EditCerceveSec(Rect modelKutusu, bool ekle)
        {
            if (!_editModu || _cizimModel == null) return;
            if (!ekle) { _editSecimi.Clear(); _editTiklamaTaraflari.Clear(); }
            foreach (DxfEntity entity in _cizimModel.Entityler)
                if (DxfKoseGeometrisi.Cercevede(entity, modelKutusu)) _editSecimi.Add(entity);
            EditSecimGoster();
        }

        private void EditSecimGoster()
        {
            _seciliEntity = _editSecimi.FirstOrDefault();
            SeciliGeometriyiGuncelle();
            txtEntityInfo.Text = _editSecimi.Count == 1
                ? EntityMetni(_seciliEntity!) + " · EDIT: Ctrl ile ekle/çıkar · Sil / Birleştir / Pah / Radius"
                : _editSecimi.Count + " öğe seçildi · Ctrl ile ekle/çıkar";
            if (_editSecimi.Any(x => x.KaynakKayit == null || x.KaynakKayit.DuzenlemeEngeli != null))
                txtEntityInfo.Text += " · Salt okunur/düzenleme engelli öğe var; işlem uygulanmaz.";
            EditDurumunuGuncelle();
        }

        private void btnPah_Click(object sender, RoutedEventArgs e) => KoseKomutuBaslat(KoseKomutTuru.Pah);
        private void btnRadius_Click(object sender, RoutedEventArgs e) => KoseKomutuBaslat(KoseKomutTuru.Radius);
        private void btnBirlestir_Click(object sender, RoutedEventArgs e) => KoseKomutuBaslat(KoseKomutTuru.Birlestir);
        private void btnKoseIptal_Click(object sender, RoutedEventArgs e) => btnTemizle_Click(sender, e);
        private void btnKoseUygula_Click(object sender, RoutedEventArgs e) => HazirKoseyiUygula();

        private void KoseKomutuBaslat(KoseKomutTuru tur)
        {
            if (!_editModu || _editOturumu == null) return;
            DxfEntity[] secim = _editSecimi.ToArray();
            var taraflar = new Dictionary<DxfEntity, Point>(_editTiklamaTaraflari);
            btnTemizle_Click(this, new RoutedEventArgs());
            _aktifKoseKomutu = tur;
            _koseKomutAsamasi = KoseKomutAsamasi.CornerOrFirstLineWaiting;
            _koseSecenekleriAyarlaniyor = true;
            rbTekKose.IsChecked = true;
            txtKoseKomutu.Text = tur == KoseKomutTuru.Radius ? "R:" : tur == KoseKomutTuru.Pah ? "Pah:" : "Birleştir";
            txtKoseDegeri.Visibility = txtKoseBirimi.Visibility = koseScopeSecimi.Visibility =
                tur == KoseKomutTuru.Birlestir ? Visibility.Collapsed : Visibility.Visible;
            koseSecenekleri.Visibility = Visibility.Visible;
            _koseSecenekleriAyarlaniyor = false;
            EditAracVurgusunuGuncelle();
            DxfEntity[] cizgiler = _cizimModel.Entityler.Where(DuzenlenebilirLine).ToArray();
            _koseAdaylari.Clear();
            if (cizgiler.Length <= EnCokMouseKoseCizgisi)
                for (int i = 0; i < cizgiler.Length; i++)
                    for (int j = i + 1; j < cizgiler.Length; j++)
                        if (DxfKoseGeometrisi.Kesisim(cizgiler[i], cizgiler[j], out Point kose))
                            _koseAdaylari.Add((cizgiler[i], cizgiler[j], kose));
            if (secim.Length == 2 && secim.All(DuzenlenebilirLine))
            {
                _hazirKoseCizgileri = secim;
                _komutHedefSecildi = true;
                foreach (var p in taraflar) _editTiklamaTaraflari[p.Key] = p.Value;
                HazirSecimiOnizle();
                return;
            }
            txtEntityInfo.Text = tur == KoseKomutTuru.Birlestir
                ? "Birleştir · Korunacak iki LINE'a sırayla tıklayın (ESC: iptal)."
                : "Köşeyi veya iki LINE'ı seçin.";
            if (cizgiler.Length > EnCokMouseKoseCizgisi)
                txtEntityInfo.Text += " · Bu büyük çizimde köşe taraması kapalı; iki LINE'ı sırayla seçin.";
        }

        private static bool DuzenlenebilirLine(DxfEntity e) => e.Tip == "LINE" &&
            e.KaynakKayit != null && e.KaynakKayit.DuzenlemeEngeli == null;

        private bool KoseDegeriniOku(out double deger)
        {
            deger = 0;
            if (_aktifKoseKomutu == KoseKomutTuru.Birlestir) return true;
            return double.TryParse(txtKoseDegeri.Text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out deger) && double.IsFinite(deger) && deger > DxfKoseGeometrisi.UzunlukToleransi;
        }

        private bool IkiLinePlani(DxfEntity a, DxfEntity b, Point? tarafA, Point? tarafB,
            out DxfKosePlani? plan, out string? hata)
        {
            plan = null;
            hata = null;
            if (!KoseDegeriniOku(out double deger))
            { hata = "Sıfırdan büyük, sonlu bir mm değeri girin."; return false; }
            return _aktifKoseKomutu == KoseKomutTuru.Birlestir
                ? DxfKoseGeometrisi.Birlestir(a, b, tarafA, tarafB, out plan, out hata)
                : _aktifKoseKomutu == KoseKomutTuru.Radius
                    ? DxfKoseGeometrisi.Fillet(a, b, deger, tarafA, tarafB, out plan, out hata)
                    : DxfKoseGeometrisi.Pah(a, b, deger, tarafA, tarafB, out plan, out hata);
        }

        private void KoseSecenekleri_Degisti(object sender, TextChangedEventArgs e)
        {
            if (_koseSecenekleriAyarlaniyor || _aktifKoseKomutu == KoseKomutTuru.Yok) return;
            _koseHatasi = null;
            _tumKonturOnizlemeHazir = false;
            if (_hazirKoseCizgileri != null) HazirSecimiOnizle();
            else if (_komutKonturu != null) KonturuOnizle(_komutKonturu);
            else if (rbTumKonturlar.IsChecked == true) KoseKomutOnizle(default);
            else if (_sonKoseMouse.HasValue) KoseKomutOnizle(_sonKoseMouse.Value);
            else KosePreviewTemizle();
            if (!KoseDegeriniOku(out _)) txtEntityInfo.Text = "Sıfırdan büyük, sonlu bir mm değeri girin.";
            else KoseStatusGuncelle();
        }

        private void KoseScope_Degisti(object sender, RoutedEventArgs e)
        {
            if (_koseSecenekleriAyarlaniyor || _aktifKoseKomutu == KoseKomutTuru.Yok) return;
            _hazirKoseCizgileri = null;
            _komutKonturu = null;
            _sonKoseMouse = null;
            _koseHatasi = null;
            _komutHedefSecildi = rbTumKonturlar.IsChecked == true;
            _tumKonturOnizlemeHazir = false;
            _komutIlkCizgisi = null;
            _komutIlkTarafi = null;
            _koseKomutAsamasi = KoseKomutAsamasi.CornerOrFirstLineWaiting;
            KosePreviewTemizle();
            _editSecimi.Clear();
            _editTiklamaTaraflari.Clear();
            SeciliGeometriyiGuncelle();
            KoseStatusGuncelle();
            if (rbTumKonturlar.IsChecked == true) KoseKomutOnizle(default);
        }

        private void HazirSecimiOnizle()
        {
            if (_hazirKoseCizgileri == null) return;
            DxfEntity a = _hazirKoseCizgileri[0], b = _hazirKoseCizgileri[1];
            Point? ta = _editTiklamaTaraflari.TryGetValue(a, out Point pa) ? pa : null;
            Point? tb = _editTiklamaTaraflari.TryGetValue(b, out Point pb) ? pb : null;
            IkiLinePlani(a, b, ta, tb, out DxfKosePlani? plan, out _koseHatasi);
            KosePreviewGoster(plan, new[] { a, b });
            _koseKomutAsamasi = KoseKomutAsamasi.Prepared;
        }

        private void KoseKomutOnizle(Point modelNoktasi)
        {
            if (!_editModu || _aktifKoseKomutu == KoseKomutTuru.Yok) return;
            _sonKoseMouse = modelNoktasi;
            if (_komutKonturu != null) return;
            if (_koseKomutAsamasi == KoseKomutAsamasi.Prepared && _hazirKoseCizgileri != null) return;
            if (rbTumKonturlar.IsChecked == true && _tumKonturOnizlemeHazir) return;
            _koseHatasi = null;
            if (!KoseDegeriniOku(out double deger))
            { _koseHatasi = "Sıfırdan büyük, sonlu bir mm değeri girin."; KosePreviewGoster(null, Array.Empty<DxfEntity>()); return; }
            if (_aktifKoseKomutu != KoseKomutTuru.Birlestir && rbTumKonturlar.IsChecked == true)
            {
                DxfKoseGeometrisi.TumGuvenilirKonturlar(_cizimModel, deger, _aktifKoseKomutu == KoseKomutTuru.Radius,
                    out DxfKosePlani? plan, out _koseHatasi);
                _komutKonturSayisi = 0;
                var ziyaret = new HashSet<DxfEntity>();
                foreach (DxfEntity e in plan?.Degisen.Keys.ToArray() ?? Array.Empty<DxfEntity>())
                    if (!ziyaret.Contains(e) && DxfKoseGeometrisi.KonturBul(_cizimModel, e, out var loop, out _))
                    { ziyaret.UnionWith(loop); _komutKonturSayisi++; }
                if (plan == null && _koseHatasi == "Kapalı kontur güvenilir şekilde oluşturulamadı.")
                    _koseHatasi = "Uygulanabilir güvenilir kapalı LINE konturu bulunamadı.";
                KosePreviewGoster(plan, plan?.Degisen.Keys.ToArray() ?? Array.Empty<DxfEntity>());
                _tumKonturOnizlemeHazir = true;
                return;
            }
            DxfEntity? hit = EntityBul(modelNoktasi, true);
            if (_aktifKoseKomutu != KoseKomutTuru.Birlestir && rbSeciliKontur.IsChecked == true)
            {
                DxfKosePlani? plan = null;
                var loop = new List<DxfEntity>();
                bool guvenilir = false;
                if (hit != null)
                {
                    if (!_konturOnbellek.TryGetValue(hit, out var bulunan))
                    {
                        bool valid = DxfKoseGeometrisi.KonturBul(_cizimModel, hit, out List<DxfEntity> yollar, out string? hata);
                        bulunan = (yollar, valid, hata);
                        _konturOnbellek[hit] = bulunan;
                        foreach (DxfEntity e in yollar) _konturOnbellek[e] = bulunan;
                    }
                    loop = bulunan.Loop;
                    guvenilir = bulunan.Guvenilir;
                    _koseHatasi = bulunan.Hata;
                }
                if (guvenilir)
                    DxfKoseGeometrisi.TumKoseler(loop, deger, _aktifKoseKomutu == KoseKomutTuru.Radius, out plan, out _koseHatasi);
                KosePreviewGoster(plan, guvenilir ? loop.ToArray() : Array.Empty<DxfEntity>());
                return;
            }
            if (_koseKomutAsamasi == KoseKomutAsamasi.SecondLineWaiting && _komutIlkCizgisi != null)
            {
                DxfKosePlani? plan = null;
                if (hit != null && hit != _komutIlkCizgisi && DuzenlenebilirLine(hit))
                    IkiLinePlani(_komutIlkCizgisi, hit, _komutIlkTarafi, modelNoktasi, out plan, out _koseHatasi);
                KosePreviewGoster(plan, hit != null && hit != _komutIlkCizgisi ? new[] { _komutIlkCizgisi, hit } : new[] { _komutIlkCizgisi });
                return;
            }
            if (_aktifKoseKomutu != KoseKomutTuru.Birlestir)
            {
                if (EkranNoktasi(modelNoktasi) is not Point ekran) return;
                if (ModelNoktasi(ekran + new Vector(-KoseHitPiksel, -KoseHitPiksel)) is not Point min ||
                    ModelNoktasi(ekran + new Vector(KoseHitPiksel, KoseHitPiksel)) is not Point max) return;
                Rect aramaKutusu = new Rect(min, max);
                var yakin = _koseAdaylari.Where(x => aramaKutusu.Contains(x.Kose))
                    .Select(x => (Aday: x, Uzaklik: EkranNoktasi(x.Kose) is Point p ? Mesafe(p, ekran) : double.PositiveInfinity))
                    .Where(x => x.Uzaklik <= KoseHitPiksel).OrderBy(x => x.Uzaklik).ToArray();
                var gecerli = new List<(DxfKosePlani Plan, DxfEntity A, DxfEntity B, double Uzaklik)>();
                foreach (var x in yakin)
                    if (IkiLinePlani(x.Aday.A, x.Aday.B, modelNoktasi, modelNoktasi, out DxfKosePlani? plan, out string? hata))
                        gecerli.Add((plan!, x.Aday.A, x.Aday.B, x.Uzaklik));
                    else _koseHatasi ??= hata;
                if (gecerli.Count > 1 && gecerli[1].Uzaklik - gecerli[0].Uzaklik <= KoseAdayFarkiPiksel)
                { _koseHatasi = "Birden fazla yakın köşe adayı var; iki LINE'ı sırayla seçin."; KosePreviewGoster(null, Array.Empty<DxfEntity>()); return; }
                if (gecerli.Count > 0)
                {
                    var x = gecerli[0]; _koseHatasi = null;
                    KosePreviewGoster(x.Plan, new[] { x.A, x.B });
                    return;
                }
                if (yakin.Length == 1)
                { KosePreviewGoster(null, new[] { yakin[0].Aday.A, yakin[0].Aday.B }); return; }
            }
            KosePreviewGoster(null, Array.Empty<DxfEntity>());
        }

        private void KonturuOnizle(DxfEntity[] loop)
        {
            DxfKosePlani? plan = null;
            _koseHatasi = null;
            if (!KoseDegeriniOku(out double deger)) _koseHatasi = "Sıfırdan büyük, sonlu bir mm değeri girin.";
            else DxfKoseGeometrisi.TumKoseler(loop, deger, _aktifKoseKomutu == KoseKomutTuru.Radius, out plan, out _koseHatasi);
            KosePreviewGoster(plan, loop);
        }

        private void KoseStatusGuncelle()
        {
            if (_koseHatasi != null) { txtEntityInfo.Text = _koseHatasi; return; }
            if (_kosePlani != null && !btnKoseUygula.IsEnabled)
            { txtEntityInfo.Text = "Önizleme adayı. Hedefi seçmek için tıklayın."; return; }
            if (_kosePlani != null && btnKoseUygula.IsEnabled)
            {
                if (rbTumKonturlar.IsChecked == true && _aktifKoseKomutu != KoseKomutTuru.Birlestir)
                {
                    txtEntityInfo.Text = $"{_komutKonturSayisi} güvenilir kontur / {_kosePlani.Eklenen.Count} köşe hazır.";
                }
                else txtEntityInfo.Text = rbSeciliKontur.IsChecked == true && _aktifKoseKomutu != KoseKomutTuru.Birlestir
                    ? $"{_kosePlani.Eklenen.Count} köşe hazır. Önizlemeyi kontrol edip Uygula'ya basın."
                    : "Önizleme hazır. Uygulamak için Uygula'ya basın.";
                return;
            }
            txtEntityInfo.Text = rbTumKonturlar.IsChecked == true && _aktifKoseKomutu != KoseKomutTuru.Birlestir
                ? "Uygulanabilir güvenilir kapalı LINE konturu bulunamadı."
                : rbSeciliKontur.IsChecked == true && _aktifKoseKomutu != KoseKomutTuru.Birlestir
                    ? "Kapalı konturun bir kenarına tıklayın."
                    : _komutIlkCizgisi != null ? "İkinci LINE'ın korunacak tarafına tıklayın."
                    : "Köşeyi veya iki LINE'ı seçin.";
        }

        private void KoseKomutTikla(Point modelNoktasi)
        {
            if (!_editModu || _aktifKoseKomutu == KoseKomutTuru.Yok) return;
            if (_komutKonturu != null)
            { _komutKonturu = null; _komutHedefSecildi = false; }
            if (_hazirKoseCizgileri != null && !_komutKoseleri.Any(p =>
                EkranNoktasi(p) is Point a && EkranNoktasi(modelNoktasi) is Point b && Mesafe(a, b) <= KoseHitPiksel))
            {
                _hazirKoseCizgileri = null;
                _komutHedefSecildi = false;
                _koseKomutAsamasi = KoseKomutAsamasi.CornerOrFirstLineWaiting;
                _komutIlkCizgisi = null;
                _komutIlkTarafi = null;
                _editTiklamaTaraflari.Clear();
            }
            KoseKomutOnizle(modelNoktasi);
            if (_komutCizgileri.Length >= 2)
            {
                _komutHedefSecildi = true;
                if (rbSeciliKontur.IsChecked == true && _aktifKoseKomutu != KoseKomutTuru.Birlestir)
                { _komutKonturu = _komutCizgileri.ToArray(); _koseKomutAsamasi = KoseKomutAsamasi.Prepared; }
                else if (rbTumKonturlar.IsChecked != true || _aktifKoseKomutu == KoseKomutTuru.Birlestir)
                {
                    _hazirKoseCizgileri = _komutCizgileri.ToArray();
                    foreach (DxfEntity line in _hazirKoseCizgileri)
                        _editTiklamaTaraflari[line] = line == _komutIlkCizgisi && _komutIlkTarafi.HasValue ? _komutIlkTarafi.Value : modelNoktasi;
                    _koseKomutAsamasi = KoseKomutAsamasi.Prepared;
                }
                btnKoseUygula.IsEnabled = _kosePlani != null;
            }
            if (_kosePlani != null)
            {
                _koseKomutAsamasi = KoseKomutAsamasi.Prepared;
                KoseStatusGuncelle();
                return;
            }
            if (rbSeciliKontur.IsChecked == true || rbTumKonturlar.IsChecked == true)
            { if (_koseHatasi == null) _koseHatasi = "Kapalı kontur güvenilir şekilde oluşturulamadı."; KoseStatusGuncelle(); return; }
            if (_koseHatasi != null) { txtEntityInfo.Text = _koseHatasi; return; }
            DxfEntity? hit = EntityBul(modelNoktasi, true);
            if (hit == null || !DuzenlenebilirLine(hit))
            { txtEntityInfo.Text = "Düzenlenebilir bir LINE seçin."; return; }
            if (_komutIlkCizgisi == null)
            {
                _komutIlkCizgisi = hit;
                _komutIlkTarafi = modelNoktasi;
                _koseKomutAsamasi = KoseKomutAsamasi.SecondLineWaiting;
                KosePreviewGoster(null, new[] { hit });
                txtEntityInfo.Text = "İlk LINE seçildi · İkinci LINE'ın korunacak tarafına tıklayın (ESC: iptal).";
            }
            else txtEntityInfo.Text = hit == _komutIlkCizgisi ? "İki farklı LINE seçin." : "Bu iki LINE için güvenilir köşe bulunamadı.";
        }

        private void KosePreviewGoster(DxfKosePlani? plan, DxfEntity[] cizgiler)
        {
            KosePreviewTemizle();
            _kosePlani = plan;
            _komutCizgileri = cizgiler;
            foreach (DxfEntity a in cizgiler)
                foreach (DxfEntity b in cizgiler)
                    if (a != b && DxfKoseGeometrisi.Kesisim(a, b, out Point kose) &&
                        (cizgiler.Length == 2 || a.Noktalar.Any(p => (p - kose).Length <= DxfKoseGeometrisi.UzunlukToleransi)))
                        if (!_komutKoseleri.Any(p => (p - kose).Length <= DxfKoseGeometrisi.UzunlukToleransi)) _komutKoseleri.Add(kose);
            if (plan != null)
            {
                var geometry = new GeometryGroup();
                foreach (DxfEntity e in plan.Degisen.Values) geometry.Children.Add(EntityGeometrisi(e));
                foreach (DxfYeniEntity e in plan.Eklenen) geometry.Children.Add(EntityGeometrisi(e.Entity));
                geometry.Freeze();
                koseOnizlemesi.Data = geometry;
                koseOnizlemesi.Visibility = Visibility.Visible;
            }
            btnKoseUygula.IsEnabled = plan != null && _komutHedefSecildi;
            SeciliGeometriyiGuncelle();
            KoseStatusGuncelle();
        }

        private void KosePreviewTemizle()
        {
            _kosePlani = null;
            _komutCizgileri = Array.Empty<DxfEntity>();
            _komutKoseleri.Clear();
            koseOnizlemesi.Data = null;
            koseOnizlemesi.Visibility = Visibility.Collapsed;
            koseIsareti.Data = null;
            koseIsareti.Visibility = Visibility.Collapsed;
            btnKoseUygula.IsEnabled = false;
        }

        private void KoseMarkeriniGuncelle()
        {
            if (_komutKoseleri.Count == 0) return;
            double r = 4 / (_modelOlcek * Math.Max(0.001, olcek.ScaleX));
            var geometry = new GeometryGroup();
            foreach (Point p in _komutKoseleri) geometry.Children.Add(new EllipseGeometry(new Point(p.X, -p.Y), r, r));
            geometry.Freeze();
            koseIsareti.Data = geometry;
            koseIsareti.RenderTransform = koseOnizlemesi.RenderTransform = _modelToScreen;
            koseIsareti.StrokeThickness = koseOnizlemesi.StrokeThickness = 1 / olcek.ScaleX;
            koseIsareti.Visibility = Visibility.Visible;
        }

        private bool HazirKoseyiUygula()
        {
            if (!_editModu || _editOturumu == null || _kosePlani == null || !btnKoseUygula.IsEnabled) return false;
            if (!_editOturumu.Uygula(_kosePlani, out string? hata))
            { txtEntityInfo.Text = hata ?? "Köşe işlemi uygulanamadı."; return false; }
            string ad = _aktifKoseKomutu.ToString();
            KoseKomutTuru komut = _aktifKoseKomutu;
            bool seciliKontur = rbSeciliKontur.IsChecked == true, tumKonturlar = rbTumKonturlar.IsChecked == true;
            EditOnizlemesiniGuncelle(ad + " uygulandı · Henüz dosyaya yazılmadı.");
            KoseKomutuBaslat(komut);
            if (seciliKontur) rbSeciliKontur.IsChecked = true;
            else if (tumKonturlar) rbTumKonturlar.IsChecked = true;
            txtEntityInfo.Text = ad + " uygulandı · Henüz dosyaya yazılmadı. " + txtEntityInfo.Text;
            return true;
        }

        // UI ve test aynı doğrulama/transaction yolunu kullanır.
        private bool KoseUygula(DxfEntity[] secim, double deger, bool fillet, bool tumu)
        {
            if (!_editModu || _editOturumu == null) return false;
            DxfKosePlani? plan = null;
            string? hata = null;
            bool basarili = tumu
                ? DxfKoseGeometrisi.TumKoseler(secim, deger, fillet, out plan, out hata)
                : secim.Length == 2 && (fillet
                    ? DxfKoseGeometrisi.Fillet(secim[0], secim[1], deger, out plan, out hata)
                    : DxfKoseGeometrisi.Pah(secim[0], secim[1], deger, out plan, out hata));
            if (!basarili) { txtEntityInfo.Text = hata ?? "İki LINE seçin."; return false; }
            if (!_editOturumu.Uygula(plan!, out hata))
            {
                txtEntityInfo.Text = hata ?? "Köşe işlemi uygulanamadı.";
                return false;
            }
            EditOnizlemesiniGuncelle((fillet ? "Radius" : "Pah") + " " +
                deger.ToString("N2") + " mm uygulandı · Henüz dosyaya yazılmadı.");
            return true;
        }

        private void SeciliEntityyiSil()
        {
            if (!_editModu || _editOturumu == null || _editSecimi.Count == 0) return;
            int adet = _editSecimi.Count;
            if (!_editOturumu.Sil(_editSecimi, out string? hata))
            {
                txtEntityInfo.Text = hata ?? "Seçili entity silinemiyor.";
                return;
            }
            EditOnizlemesiniGuncelle(adet + " öğe silindi · Henüz dosyaya yazılmadı.");
        }

        private void EditGeriAl()
        {
            if (_editModu && _editOturumu?.GeriAl() == true)
                EditOnizlemesiniGuncelle("Edit işlemi geri alındı.");
        }

        private void EditYinele()
        {
            if (_editModu && _editOturumu?.Yinele() == true)
                EditOnizlemesiniGuncelle("Edit işlemi yinelendi.");
        }

        private void EditOnizlemesiniGuncelle(string mesaj)
        {
            if (_editOturumu == null) return;
            _cizimModel = _editOturumu.Onizleme();
            // Secim/olcum yalnizca ekrandadir; edit Undo gecmisine girmez.
            btnTemizle_Click(this, new RoutedEventArgs());
            cizim.Data = _cizimModel.Geometri();
            cizim.RenderTransform = _modelToScreen;
            cizim.Visibility = Visibility.Visible;
            txtMesaj.Visibility = _cizimModel.Bos ? Visibility.Visible : Visibility.Collapsed;
            txtMesaj.Text = "Çizilebilir entity kalmadı · Undo ile geri alabilirsiniz.";
            SnapNoktalariniHazirla();
            // Ortak matris ve pan/zoom korunur; edit oturumu baslangic bounds'unu kullanir.
            txtOlcu.Text = _cizimModel.Genislik.ToString("N1") + " × " +
                           _cizimModel.Yukseklik.ToString("N1") + " mm  ·  " +
                           _cizimModel.NesneSayisi + " nesne";
            txtEntityInfo.Text = mesaj;
            EditDurumunuGuncelle();
        }

        private bool EditKaydet()
        {
            if (!_editModu || _editOturumu == null) return false;
            if (!_editOturumu.Degisti) return true;
            if (MessageBox.Show(this,
                "DXF dosyasının üzerine yazılacak:\n" + _editOturumu.Yol +
                "\n\nÖnce aynı klasörde .bak yedeği oluşturulacak. Onaylıyor musunuz?",
                "DXF Kaydet", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            return EditOnayliKaydet();
        }

        // Sadece EditKaydet'teki açık kullanıcı onayından sonra çağrılır.
        private bool EditOnayliKaydet()
        {
            if (!_editModu || _editOturumu == null) return false;
            if (!_editOturumu.Degisti) return true;
            string yedek;
            try
            {
                yedek = _editOturumu.Kaydet(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "DXF kaydedilemedi; mevcut dosya korunuyor.\n" + ex.Message,
                    "DXF Kaydet", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            // Commit tamamlandı. Liste/önizleme bildirim hatası kayıt hatası değildir.
            string hedef = _editOturumu.Yol;
            try
            {
                if (yedek.Length > 0 && _editOturumu.OrijinalHedef) OrijinalEditKaydedildi?.Invoke(hedef);
                EditKayitSonucunuGuncelle("Kaydedildi · Yedek: " + yedek);
            }
            catch (Exception ex)
            {
                _yol = hedef;
                txtDosya.Text = hedef;
                txtDosya.ToolTip = hedef;
                txtEntityInfo.Text = "DXF kaydedildi · Arayüz bildirimi güncellenemedi: " + ex.Message;
                EditDurumunuGuncelle();
            }
            return true;
        }

        private void btnFarkliKaydet_Click(object sender, RoutedEventArgs e)
        {
            if (!_editModu || _editOturumu == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "DXF Farklı Kaydet — yeni dosya",
                Filter = "ASCII DXF (*.dxf)|*.dxf", DefaultExt = ".dxf", AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(_editOturumu.Yol) + "_edited.dxf",
                InitialDirectory = Path.GetDirectoryName(_editOturumu.Yol) ?? "",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                _editOturumu.FarkliKaydet(dialog.FileName);
                EditKayitSonucunuGuncelle("Yeni dosyaya kaydedildi · Ana listedeki kaynak yolu değiştirilmedi.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Farklı Kaydet başarısız; mevcut dosyaya yazılmadı.\n" + ex.Message,
                    "DXF Farklı Kaydet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditKayitSonucunuGuncelle(string mesaj)
        {
            if (_editOturumu == null) return;
            _yol = _editOturumu.Yol;
            txtDosya.Text = _yol;
            txtDosya.ToolTip = _yol;
            txtEntityInfo.Text = mesaj;
            EditDurumunuGuncelle();
            Kaydedildi?.Invoke(_yol);
        }

        private bool BelgeDegisimineIzinVer(string? yeniYol)
        {
            // Save As sonrasi bile ana listeden ayni kaynak feed'i edit belgesini ezmez.
            if (_editOturumuBaslatildi && string.Equals(_beslemeYolu, yeniYol,
                                                       StringComparison.OrdinalIgnoreCase)) return false;
            return KaydedilmemisDegisiklikleriCoz();
        }

        internal bool KapanisaIzinVer()
        {
            if (_kapanisOnaylandi) return true;
            if (!KaydedilmemisDegisiklikleriCoz()) return false;
            _kapanisOnaylandi = true;
            return true;
        }

        private bool KaydedilmemisDegisiklikleriCoz()
        {
            if (_editOturumu?.Degisti != true) return true;
            int secim = KaydedilmemisOnayi();
            if (secim == 0) return false;
            if (secim == 2) { _editOturumu.Vazgec(); return true; }
            // Kaydet secimi edit kapaliyken de acik Edit Modu'na gecis iznidir.
            chkEditModu.IsChecked = true;
            return EditKaydet();
        }

        private int KaydedilmemisOnayi()
        {
            int secim = 0;
            var dialog = new Window
            {
                Owner = this, Title = "DXF değişiklikleri", ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                Background = Background, Foreground = Foreground, FontFamily = FontFamily
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Kaydedilmemiş DXF değişiklikleri var.", Margin = new Thickness(0, 0, 0, 16)
            });
            var dugmeler = new StackPanel { Orientation = Orientation.Horizontal };
            string[] adlar = { "Kaydet", "Kaydetmeden Çık", "İptal" };
            int[] degerler = { 1, 2, 0 };
            for (int i = 0; i < adlar.Length; i++)
            {
                int deger = degerler[i];
                var dugme = new Button
                {
                    Content = adlar[i], Style = (Style)FindResource("SecondaryButton"),
                    Padding = new Thickness(14, 0, 14, 0), Height = 32,
                    Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0), IsCancel = deger == 0
                };
                dugme.Click += (s, e) => { secim = deger; dialog.Close(); };
                dugmeler.Children.Add(dugme);
            }
            panel.Children.Add(dugmeler);
            dialog.Content = panel;
            dialog.ShowDialog();
            return secim;
        }

        // ================= YAKINLASTIRMA =================

        private void Sigdir()
        {
            olcek.ScaleX = 1;
            olcek.ScaleY = 1;
            kaydir.X = 0;
            kaydir.Y = 0;

            CizgiKalinligi();
            YakinlikYaz();
        }

        // Cizgi ekranda hep ayni incelikte kalsin diye donusum tersine cevrilir
        private void CizgiKalinligi()
        {
            cizim.StrokeThickness = 1.0 / olcek.ScaleX;
            ModelDonusumunuGuncelle();
            SeciliGeometriyiGuncelle();
        }

        // Tum cizim katmanlarinin tek model->ekran kaynagi. Ana Path de
        // Stretch ile ayri bir donusum uygulamak yerine bu matrisi kullanir.
        private void ModelDonusumunuGuncelle()
        {
            Rect bounds = _editOturumuBaslatildi ? _editReferansBounds :
                          (cizim.Data == null ? Rect.Empty : cizim.Data.Bounds);
            if (bounds.Width <= 0 || bounds.Height <= 0 ||
                cizimAlani.ActualWidth <= 0 || cizimAlani.ActualHeight <= 0) return;

            _modelOlcek = Math.Min(cizimAlani.ActualWidth / bounds.Width,
                                   cizimAlani.ActualHeight / bounds.Height);
            double offsetX = (cizimAlani.ActualWidth - bounds.Width * _modelOlcek) / 2.0 -
                             bounds.Left * _modelOlcek;
            double offsetY = (cizimAlani.ActualHeight - bounds.Height * _modelOlcek) / 2.0 -
                             bounds.Top * _modelOlcek;
            _modelToScreen.Matrix = new Matrix(_modelOlcek, 0, 0, _modelOlcek,
                                                offsetX, offsetY);
        }

        private void cizimAlani_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ModelDonusumunuGuncelle();
            SeciliGeometriyiGuncelle();
        }

        private void YakinlikYaz()
        {
            txtYakinlik.Text = "%" + Math.Round(olcek.ScaleX * 100).ToString("0");
        }

        private void cizimAlani_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (cizim.Visibility != Visibility.Visible) return;
            if (_cerceveSeciliyor)
            {
                _surukleniyor = false;
                _cerceveSeciliyor = false;
                secimCercevesi.Visibility = Visibility.Collapsed;
                cizimAlani.ReleaseMouseCapture();
                cizimAlani.Cursor = null;
            }

            double eski = olcek.ScaleX;
            double yeni = eski * Math.Pow(1.15, e.Delta / 120.0);

            if (yeni < EnAz) yeni = EnAz;
            if (yeni > EnCok) yeni = EnCok;

            double k = yeni / eski;
            if (Math.Abs(k - 1) < 1e-9) return;

            // Farenin altindaki nokta yerinde kalsin: donusumun merkezi
            // katmanin ortasi oldugu icin kaydirma da ayni oranda tasinir
            Point m = e.GetPosition(cizimAlani);
            double cx = cizimAlani.ActualWidth / 2;
            double cy = cizimAlani.ActualHeight / 2;

            kaydir.X = (m.X - cx) * (1 - k) + kaydir.X * k;
            kaydir.Y = (m.Y - cy) * (1 - k) + kaydir.Y * k;

            olcek.ScaleX = yeni;
            olcek.ScaleY = yeni;

            CizgiKalinligi();
            YakinlikYaz();

            if (_kip == "CizgiCizgi")
                CizgiOlcuYerlesiminiGuncelle(ModelNoktasi(m));
            if (_aktifKoseKomutu != KoseKomutTuru.Yok && ModelNoktasi(m) is Point kp)
                KoseKomutOnizle(kp);

            e.Handled = true;
        }

        private void cizimAlani_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (cizim.Visibility != Visibility.Visible) return;

            // Cift tiklama sigdirmaya doner
            if (!_editModu && e.ClickCount == 2 &&
                !(_kip == "CizgiCizgi" && _cizgiOlcuAsamasi == CizgiOlcuAsamasi.PlacementWaiting))
            {
                Sigdir();
                return;
            }

            _surukleniyor = true;
            _basildigiNokta = e.GetPosition(cizimAlani);
            _cerceveSeciliyor = _editModu && _aktifKoseKomutu == KoseKomutTuru.Yok &&
                EntityBul(ModelNoktasi(_basildigiNokta)) == null;
            _cerceveyeEkle = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _basXKaydir = kaydir.X;
            _basYKaydir = kaydir.Y;

            cizimAlani.CaptureMouse();
            cizimAlani.Cursor = _cerceveSeciliyor ? Cursors.Cross : Cursors.SizeAll;
        }

        private void cizimAlani_MouseMove(object sender, MouseEventArgs e)
        {
            Point simdi = e.GetPosition(cizimAlani);

            if (!_surukleniyor)
            {
                if (_aktifKoseKomutu != KoseKomutTuru.Yok && ModelNoktasi(simdi) is Point kp)
                {
                    KoseKomutOnizle(kp);
                    KoseStatusGuncelle();
                }
                else if (_kip == "Mesafe")
                {
                    Point? modelNoktasi = ModelNoktasi(simdi);
                    SnapNoktasi? snap = modelNoktasi.HasValue ? SnapBul(modelNoktasi.Value, simdi) : null;
                    SnapGoster(snap);

                    if (_ilkMesafeNoktasi.HasValue && modelNoktasi.HasValue)
                    {
                        _olcuOnizlemeNoktasi = snap == null ? modelNoktasi.Value : snap.Nokta;
                        OlcuGeometrisiniGuncelle();
                    }
                }
                else if (_kip == "CizgiCizgi")
                    CizgiOlcuYerlesiminiGuncelle(ModelNoktasi(simdi));
                return;
            }

            if (_cerceveSeciliyor)
            {
                Rect kutu = new Rect(_basildigiNokta, simdi);
                Canvas.SetLeft(secimCercevesi, kutu.Left);
                Canvas.SetTop(secimCercevesi, kutu.Top);
                secimCercevesi.Width = kutu.Width;
                secimCercevesi.Height = kutu.Height;
                secimCercevesi.Visibility = Visibility.Visible;
                return;
            }
            kaydir.X = _basXKaydir + (simdi.X - _basildigiNokta.X);
            kaydir.Y = _basYKaydir + (simdi.Y - _basildigiNokta.Y);
        }

        private void cizimAlani_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_surukleniyor) return;

            Point ekranNoktasi = e.GetPosition(cizimAlani);
            bool tiklama = Mesafe(ekranNoktasi, _basildigiNokta) < 3;

            _surukleniyor = false;
            cizimAlani.ReleaseMouseCapture();
            cizimAlani.Cursor = null;

            if (_cerceveSeciliyor)
            {
                _cerceveSeciliyor = false;
                secimCercevesi.Visibility = Visibility.Collapsed;
                Point? ilk = ModelNoktasi(_basildigiNokta), son = ModelNoktasi(ekranNoktasi);
                if (!tiklama && ilk.HasValue && son.HasValue)
                    EditCerceveSec(new Rect(ilk.Value, son.Value), _cerceveyeEkle);
                else if (!_cerceveyeEkle) EditSec(null, false);
                e.Handled = true;
                return;
            }

            if (!tiklama) return;

            Point? modelNoktasi = ModelNoktasi(ekranNoktasi);
            if (_aktifKoseKomutu != KoseKomutTuru.Yok && modelNoktasi.HasValue)
            { KoseKomutTikla(modelNoktasi.Value); e.Handled = true; return; }
            if (_kip == "Mesafe" && modelNoktasi.HasValue)
            {
                SnapNoktasi? snap = SnapBul(modelNoktasi.Value, ekranNoktasi);
                MesafeNoktasi(snap == null ? modelNoktasi.Value : snap.Nokta);
            }
            else if (_kip == "Sec")
            {
                DxfEntity? entity = EntityBul(modelNoktasi);
                if (_editModu)
                {
                    EditSec(entity, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
                    if (entity != null && modelNoktasi.HasValue && _editSecimi.Contains(entity))
                        _editTiklamaTaraflari[entity] = modelNoktasi.Value;
                }
                else Sec(entity);
            }
            else if (_kip == "CizgiCizgi")
                CizgiOlcuTikla(modelNoktasi);

            e.Handled = true;
        }

        private void btnSigdir_Click(object sender, RoutedEventArgs e)
        {
            Sigdir();
        }

        private void btnSec_Click(object sender, RoutedEventArgs e)
        {
            if (_aktifKoseKomutu != KoseKomutTuru.Yok) btnTemizle_Click(sender, e);
            _kip = "Sec";
            _ilkMesafeNoktasi = null;
            _sonMesafeNoktasi = null;
            _olcuOnizlemeNoktasi = null;
            _olcuBaslangicNoktasi = null;
            CizgiOlcuSeciminiTemizle();
            OlcuGeometrisiniTemizle();
            SnapGoster(null);
            btnSec.Background = (Brush)FindResource("SurfaceBrush");
            btnMesafe.Background = Brushes.Transparent;
            btnCizgiCizgi.Background = Brushes.Transparent;
            txtEntityInfo.Text = "LINE, CIRCLE veya ARC seçmek için çizime tıklayın.";
        }

        private void btnMesafe_Click(object sender, RoutedEventArgs e)
        {
            _kip = "Mesafe";
            _ilkMesafeNoktasi = null;
            _sonMesafeNoktasi = null;
            _olcuOnizlemeNoktasi = null;
            _olcuBaslangicNoktasi = null;
            CizgiOlcuSeciminiTemizle();
            OlcuGeometrisiniTemizle();
            SnapGoster(null);
            btnMesafe.Background = (Brush)FindResource("SurfaceBrush");
            btnSec.Background = Brushes.Transparent;
            btnCizgiCizgi.Background = Brushes.Transparent;
            txtEntityInfo.Text = "İki noktaya tıklayın.";
        }

        private void btnCizgiCizgi_Click(object sender, RoutedEventArgs e)
        {
            _kip = "CizgiCizgi";
            _ilkMesafeNoktasi = null;
            _sonMesafeNoktasi = null;
            _olcuOnizlemeNoktasi = null;
            _olcuBaslangicNoktasi = null;
            CizgiOlcuSeciminiTemizle();
            _seciliEntity = null;
            seciliCizim.Data = null;
            seciliCizim.Visibility = Visibility.Collapsed;
            OlcuGeometrisiniTemizle();
            // LINE secimi snap degil, mevcut segment hit-test'ini kullanir.
            SnapGoster(null);
            btnCizgiCizgi.Background = (Brush)FindResource("SurfaceBrush");
            btnSec.Background = Brushes.Transparent;
            btnMesafe.Background = Brushes.Transparent;
            txtEntityInfo.Text = "İlk LINE'ı seçin, ardından paralel ikinci LINE'a tıklayın.";
        }

        private void btnTemizle_Click(object sender, RoutedEventArgs e)
        {
            _aktifKoseKomutu = KoseKomutTuru.Yok;
            _koseKomutAsamasi = KoseKomutAsamasi.CornerOrFirstLineWaiting;
            _komutIlkCizgisi = null;
            _komutIlkTarafi = null;
            _hazirKoseCizgileri = null;
            _komutKonturu = null;
            _komutHedefSecildi = false;
            _sonKoseMouse = null;
            _koseHatasi = null;
            _tumKonturOnizlemeHazir = false;
            _koseAdaylari.Clear();
            _konturOnbellek.Clear();
            _editTiklamaTaraflari.Clear();
            KosePreviewTemizle();
            koseSecenekleri.Visibility = Visibility.Collapsed;
            EditAracVurgusunuGuncelle();
            _editSecimi.Clear();
            _cerceveSeciliyor = false;
            _surukleniyor = false;
            cizimAlani.ReleaseMouseCapture();
            cizimAlani.Cursor = null;
            secimCercevesi.Visibility = Visibility.Collapsed;
            _kip = "Sec";
            _seciliEntity = null;
            _ilkMesafeNoktasi = null;
            _sonMesafeNoktasi = null;
            _olcuOnizlemeNoktasi = null;
            _olcuBaslangicNoktasi = null;
            CizgiOlcuSeciminiTemizle();
            seciliCizim.Data = null;
            seciliCizim.Visibility = Visibility.Collapsed;
            seciliCizim.RenderTransform = Transform.Identity;
            OlcuGeometrisiniTemizle();
            SnapGoster(null);
            btnSec.Background = (Brush)FindResource("SurfaceBrush");
            btnMesafe.Background = Brushes.Transparent;
            btnCizgiCizgi.Background = Brushes.Transparent;
            txtEntityInfo.Text = "";
            EditDurumunuGuncelle();
        }

        private void CizgiOlcuSeciminiTemizle()
        {
            if (_ilkOlcuCizgisi != null)
            {
                seciliCizim.Data = null;
                seciliCizim.Visibility = Visibility.Collapsed;
            }
            _ilkOlcuCizgisi = null;
            _ikinciOlcuCizgisi = null;
            _cizgiOlcuYonu = null;
            _cizgiOlcuAsamasi = CizgiOlcuAsamasi.FirstLineWaiting;
            _cizgiOlcuUzantiOfseti = 0;
            _cizgiOlcuYerlesimOfseti = 0;
        }

        private void CizgiOlcuTikla(Point? modelNoktasi)
        {
            if (_cizgiOlcuAsamasi == CizgiOlcuAsamasi.PlacementWaiting)
            {
                if (!modelNoktasi.HasValue) return;
                // Ucuncu tik geometri secmez; bos alanda da yerlestirebilir.
                CizgiOlcuYerlesiminiGuncelle(modelNoktasi);
                _cizgiOlcuAsamasi = CizgiOlcuAsamasi.Completed;
                CizgiOlcuSonucunuYaz();
                return;
            }

            CizgiOlcuSec(EntityBul(modelNoktasi, true));
            CizgiOlcuYerlesiminiGuncelle(modelNoktasi);
        }

        private void CizgiOlcuYerlesiminiGuncelle(Point? modelNoktasi)
        {
            if (_cizgiOlcuAsamasi != CizgiOlcuAsamasi.PlacementWaiting ||
                !modelNoktasi.HasValue || !_olcuBaslangicNoktasi.HasValue ||
                !_sonMesafeNoktasi.HasValue || !_cizgiOlcuYonu.HasValue) return;

            Point ilk = _olcuBaslangicNoktasi.Value, son = _sonMesafeNoktasi.Value;
            Point orta = new Point((ilk.X + son.X) / 2, (ilk.Y + son.Y) / 2);
            // Ortak ScreenToModel kullanilir. Yatay ciftte X, dikey ciftte Y
            // yerlestirmeyi belirler; egik ciftin eski normal davranisi korunur.
            CizgiOlcuEkseni eksen = CizgiOlcuEkseniniBul();
            if (eksen == CizgiOlcuEkseni.YatayCizgiler)
                _cizgiOlcuYerlesimOfseti = modelNoktasi.Value.X - orta.X;
            else if (eksen == CizgiOlcuEkseni.DikeyCizgiler)
                _cizgiOlcuYerlesimOfseti = modelNoktasi.Value.Y - orta.Y;
            else
                _cizgiOlcuYerlesimOfseti = Vector.Multiply(
                    modelNoktasi.Value - orta, _cizgiOlcuYonu.Value);
            OlcuGeometrisiniGuncelle();
        }

        private CizgiOlcuEkseni CizgiOlcuEkseniniBul()
        {
            if (_ilkOlcuCizgisi == null || _ikinciOlcuCizgisi == null ||
                !CizgiYonu(_ilkOlcuCizgisi, out Vector ilkYon) ||
                !CizgiYonu(_ikinciOlcuCizgisi, out Vector ikinciYon)) return CizgiOlcuEkseni.Egik;

            if (Math.Abs(ilkYon.Y) <= EksenHizalamaToleransi &&
                Math.Abs(ikinciYon.Y) <= EksenHizalamaToleransi) return CizgiOlcuEkseni.YatayCizgiler;
            if (Math.Abs(ilkYon.X) <= EksenHizalamaToleransi &&
                Math.Abs(ikinciYon.X) <= EksenHizalamaToleransi) return CizgiOlcuEkseni.DikeyCizgiler;
            return CizgiOlcuEkseni.Egik;
        }

        private static Point CizgiUzerindeEksenProjeksiyonu(DxfEntity entity, double konum, bool xEkseni)
        {
            Point bas = entity.Noktalar[0], son = entity.Noktalar[entity.Noktalar.Length - 1];
            double basEksen = xEkseni ? bas.X : bas.Y;
            double sonEksen = xEkseni ? son.X : son.Y;
            // Eksen sinifi bu bilesenin sifir olmadigini garanti eder.
            // Ic konumda LINE uzerindeki projection; disarida en yakin segment ucu.
            double t = Math.Max(0, Math.Min(1, (konum - basEksen) / (sonEksen - basEksen)));
            return bas + (son - bas) * t;
        }

        private void OnizlemeWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter/mouse yalnızca girdi/target içindir; commit yalnızca Uygula düğmesinden.
            if (_editModu && e.Key != Key.Escape && (e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox) &&
                !(e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)) return;
            if (_editModu && e.Key == Key.Escape)
            {
                btnTemizle_Click(this, new RoutedEventArgs());
                txtEntityInfo.Text = "Edit seçimi iptal edildi; geometri değiştirilmedi.";
                e.Handled = true;
                return;
            }
            // Edit disinda bu kisayollar dosyayi veya edit gecmisini degistiremez.
            if (_editModu)
            {
                if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
                {
                    SeciliEntityyiSil();
                    e.Handled = true;
                    return;
                }
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    if (e.Key == Key.Z) { EditGeriAl(); e.Handled = true; return; }
                    if (e.Key == Key.Y) { EditYinele(); e.Handled = true; return; }
                    if (e.Key == Key.S) { EditKaydet(); e.Handled = true; return; }
                }
            }
            if (e.Key != Key.Escape || _kip != "CizgiCizgi" ||
                _cizgiOlcuAsamasi != CizgiOlcuAsamasi.PlacementWaiting) return;

            _surukleniyor = false;
            cizimAlani.ReleaseMouseCapture();
            cizimAlani.Cursor = null;
            CizgiOlcuSeciminiTemizle();
            _ilkMesafeNoktasi = null;
            _sonMesafeNoktasi = null;
            _olcuOnizlemeNoktasi = null;
            _olcuBaslangicNoktasi = null;
            OlcuGeometrisiniTemizle();
            SnapGoster(null);
            txtEntityInfo.Text = "Ölçü yerleştirme iptal edildi · İlk LINE'ı seçin.";
            e.Handled = true;
        }

        private void CizgiOlcuSec(DxfEntity? entity)
        {
            if (_cizgiOlcuAsamasi == CizgiOlcuAsamasi.PlacementWaiting) return;
            if (entity == null)
            {
                txtEntityInfo.Text = "Ölçmek için bir LINE seçin.";
                return;
            }
            if (!CizgiYonu(entity, out _))
            {
                txtEntityInfo.Text = "Sıfır uzunluklu LINE ölçülemez.";
                return;
            }

            // Tamamlanan olcuden sonraki tiklama yeni bir cift baslatir.
            if (_ilkOlcuCizgisi == null || _cizgiOlcuAsamasi == CizgiOlcuAsamasi.Completed)
            {
                CizgiOlcuSeciminiTemizle();
                _ilkOlcuCizgisi = entity;
                _olcuBaslangicNoktasi = null;
                _sonMesafeNoktasi = null;
                _cizgiOlcuAsamasi = CizgiOlcuAsamasi.SecondLineWaiting;
                SeciliGeometriyiGuncelle();
                txtEntityInfo.Text = "İlk LINE seçildi · Paralel ikinci LINE'ı seçin.";
                return;
            }

            if (ReferenceEquals(_ilkOlcuCizgisi, entity))
            {
                txtEntityInfo.Text = "Aynı LINE iki kez seçilemez · Farklı bir LINE seçin.";
                return;
            }

            if (!ParalelCizgiOlcusu(_ilkOlcuCizgisi, entity,
                    out Point ilk, out Point son, out Vector normal))
            {
                txtEntityInfo.Text = "Seçilen çizgiler paralel değil.";
                return;
            }

            _ikinciOlcuCizgisi = entity;
            _cizgiOlcuYonu = normal;
            _olcuBaslangicNoktasi = ilk;
            _sonMesafeNoktasi = son;
            // Eski otomatik yerlestirmeyi sadece baslangicta hesapla ve
            // modelde sakla; sabit olcu zoom sonrasi yeniden yerlestirilmez.
            Vector uzantiYonu = new Vector(normal.Y, -normal.X);
            double disKenar = 0;
            foreach (DxfEntity? cizgi in new[] { _ilkOlcuCizgisi, _ikinciOlcuCizgisi })
            {
                if (cizgi == null) continue;
                foreach (Point nokta in cizgi.Noktalar)
                    disKenar = Math.Max(disKenar, Vector.Multiply(nokta - ilk, uzantiYonu));
            }
            _cizgiOlcuUzantiOfseti = disKenar +
                18.0 / (_modelOlcek * Math.Max(0.001, olcek.ScaleX));
            _cizgiOlcuYerlesimOfseti = 0;
            if (CizgiOlcuEkseniniBul() != CizgiOlcuEkseni.Egik)
                _cizgiOlcuYerlesimOfseti = _cizgiOlcuUzantiOfseti;
            _cizgiOlcuAsamasi = CizgiOlcuAsamasi.PlacementWaiting;
            SeciliGeometriyiGuncelle();
            CizgiOlcuSonucunuYaz();
        }

        private void CizgiOlcuSonucunuYaz()
        {
            if (!_olcuBaslangicNoktasi.HasValue || !_sonMesafeNoktasi.HasValue ||
                !_cizgiOlcuYonu.HasValue) return;
            Point ilk = _olcuBaslangicNoktasi.Value, son = _sonMesafeNoktasi.Value;
            Vector normal = _cizgiOlcuYonu.Value;
            double mesafe = Math.Abs(Vector.Multiply(son - ilk, normal));
            txtEntityInfo.Text = "Çizgi-Çizgi · Dik mesafe: " +
                mesafe.ToString("N2", CultureInfo.CurrentCulture) +
                " mm" + (_cizgiOlcuAsamasi == CizgiOlcuAsamasi.PlacementWaiting
                    ? " · Konumu mouse ile belirleyin; 3. tık: sabitle, ESC: iptal."
                    : " · Yeni ölçüm için bir LINE seçin.");
        }

        private static bool CizgiYonu(DxfEntity entity, out Vector yon)
        {
            yon = new Vector();
            if (entity.Tip != "LINE" || entity.Noktalar.Length < 2) return false;
            yon = entity.Noktalar[entity.Noktalar.Length - 1] - entity.Noktalar[0];
            if (yon.LengthSquared < 1e-12) return false;
            yon.Normalize();
            // Ters endpoint sirasi olcu yerlestirmesini degistirmesin.
            if ((Math.Abs(yon.X) >= Math.Abs(yon.Y) && yon.X < 0) ||
                (Math.Abs(yon.Y) > Math.Abs(yon.X) && yon.Y < 0)) yon = -yon;
            return true;
        }

        private static bool ParalelCizgiOlcusu(DxfEntity ilk, DxfEntity ikinci,
            out Point ilkNokta, out Point sonNokta, out Vector normal)
        {
            ilkNokta = sonNokta = new Point();
            normal = new Vector();
            if (!CizgiYonu(ilk, out Vector yon) ||
                !CizgiYonu(ikinci, out Vector ikinciYon) ||
                Math.Abs(Vector.CrossProduct(yon, ikinciYon)) > ParalellikToleransi)
                return false;

            Point p = ilk.Noktalar[0], q = ikinci.Noktalar[0];
            Point pSon = ilk.Noktalar[ilk.Noktalar.Length - 1];
            Point qSon = ikinci.Noktalar[ikinci.Noktalar.Length - 1];
            double pT = Vector.Multiply(pSon - p, yon);
            double qT = Vector.Multiply(q - p, yon);
            double qSonT = Vector.Multiply(qSon - p, yon);
            double ortakBas = Math.Max(Math.Min(0, pT), Math.Min(qT, qSonT));
            double ortakSon = Math.Min(Math.Max(0, pT), Math.Max(qT, qSonT));
            // Ortusen segmentlerin ortasinda referans al. Ortusme yoksa
            // CAD paralel-dogru olcusu uzantilari kullanir, endpoint mesafesi degil.
            double t = ortakBas <= ortakSon ? (ortakBas + ortakSon) / 2 : pT / 2;
            ilkNokta = p + yon * t;
            double ikinciT = Vector.Multiply(ilkNokta - q, yon) /
                             Vector.Multiply(ikinciYon, yon);
            sonNokta = q + ikinciYon * ikinciT;
            normal = new Vector(-yon.Y, yon.X);
            return true;
        }

        private void Sec(DxfEntity? entity)
        {
            if (_editModu) { EditSec(entity, false); return; }
            _seciliEntity = entity;
            seciliCizim.Data = entity == null ? null : EntityGeometrisi(entity);
            seciliCizim.Visibility = entity == null
                ? Visibility.Collapsed : Visibility.Visible;
            SeciliGeometriyiGuncelle();
            txtEntityInfo.Text = entity == null
                ? "Geometri seçilmedi."
                : EntityMetni(entity);
            if (_editModu && entity != null)
            {
                string? engel = entity.KaynakKayit == null
                    ? "BLOCK/INSERT geometrisi bu aşamada salt okunur."
                    : entity.KaynakKayit.DuzenlemeEngeli;
                txtEntityInfo.Text += engel == null ? " · EDIT: Sil / Delete" : " · " + engel;
            }
            EditDurumunuGuncelle();
        }

        private void MesafeNoktasi(Point nokta)
        {
            if (!_ilkMesafeNoktasi.HasValue)
            {
                _ilkMesafeNoktasi = nokta;
                _sonMesafeNoktasi = null;
                _olcuOnizlemeNoktasi = null;
                _olcuBaslangicNoktasi = nokta;
                OlcuGeometrisiniGuncelle();
                txtEntityInfo.Text = "İlk nokta: " + NoktaMetni(nokta) +
                                      " · İkinci noktayı seçin.";
                return;
            }

            Point ilk = _ilkMesafeNoktasi.Value;
            double dx = Math.Abs(nokta.X - ilk.X);
            double dy = Math.Abs(nokta.Y - ilk.Y);
            bool yatay = dx >= dy;
            double mesafe = yatay ? dx : dy;
            double gercekMesafe = Mesafe(ilk, nokta);
            _sonMesafeNoktasi = nokta;
            _olcuOnizlemeNoktasi = null;
            OlcuGeometrisiniGuncelle();
            txtEntityInfo.Text = "Mesafe: " + mesafe.ToString("N2", CultureInfo.CurrentCulture) +
                                 " mm · " + NoktaMetni(_ilkMesafeNoktasi.Value) +
                                 " -> " + NoktaMetni(nokta);
            _ilkMesafeNoktasi = null;
            txtEntityInfo.Text = (yatay ? "Yatay" : "Dikey") + " olcu: " +
                                 mesafe.ToString("N2", CultureInfo.CurrentCulture) + " mm" +
                                 " | Gercek: " +
                                 gercekMesafe.ToString("N2", CultureInfo.CurrentCulture) + " mm";
        }

        // ================= SNAP =================

        private void SnapNoktalariniHazirla()
        {
            _snapNoktalari.Clear();
            if (_cizimModel == null) return;

            var cizgiler = new List<DxfEntity>();
            foreach (DxfEntity entity in _cizimModel.Entityler)
            {
                Point[] noktalar = entity.Noktalar ?? Array.Empty<Point>();
                if (entity.Tip == "LINE" && noktalar.Length >= 2)
                {
                    SnapEkle(noktalar[0], "Endpoint", 0);
                    SnapEkle(noktalar[noktalar.Length - 1], "Endpoint", 0);
                    SnapEkle(new Point((noktalar[0].X + noktalar[noktalar.Length - 1].X) / 2,
                                       (noktalar[0].Y + noktalar[noktalar.Length - 1].Y) / 2),
                             "Midpoint", 2);
                    cizgiler.Add(entity);
                }
                else if (entity.Tip == "ARC" && noktalar.Length >= 2)
                {
                    SnapEkle(noktalar[0], "Endpoint", 0);
                    SnapEkle(noktalar[noktalar.Length - 1], "Endpoint", 0);
                    SnapEkle(entity.Merkez, "Center", 3);
                }
                else if (entity.Tip == "CIRCLE")
                {
                    SnapEkle(entity.Merkez, "Center", 3);
                }
            }

            for (int i = 0; i < cizgiler.Count; i++)
                for (int j = i + 1; j < cizgiler.Count; j++)
                {
                    Point kesisim;
                    Point[] a = cizgiler[i].Noktalar;
                    Point[] b = cizgiler[j].Noktalar;
                    if (DogruParcasiKesisimi(a[0], a[a.Length - 1], b[0], b[b.Length - 1], out kesisim))
                        SnapEkle(kesisim, "Intersection", 1);
                }
        }

        private void SnapEkle(Point nokta, string tip, int oncelik)
        {
            _snapNoktalari.Add(new SnapNoktasi { Nokta = nokta, Tip = tip, Oncelik = oncelik });
        }

        private SnapNoktasi? SnapBul(Point modelNoktasi, Point ekranNoktasi)
        {
            const double toleransPiksel = 10;
            SnapNoktasi? enYakin = null;
            double enKisa = double.MaxValue;

            foreach (SnapNoktasi aday in _snapNoktalari)
                SnapAdayiniDegerlendir(aday, ekranNoktasi, toleransPiksel, ref enYakin, ref enKisa);

            if (_cizimModel != null)
                foreach (DxfEntity entity in _cizimModel.Entityler)
                {
                    if (entity.Tip != "LINE" && entity.Tip != "CIRCLE" && entity.Tip != "ARC") continue;
                    Point? enYakinNokta = EntityEnYakinNoktasi(entity, modelNoktasi);
                    if (enYakinNokta.HasValue)
                        SnapAdayiniDegerlendir(new SnapNoktasi
                        {
                            Nokta = enYakinNokta.Value,
                            Tip = "Nearest",
                            Oncelik = 4
                        }, ekranNoktasi, toleransPiksel, ref enYakin, ref enKisa);
                }

            return enYakin;
        }

        private void SnapAdayiniDegerlendir(SnapNoktasi aday, Point ekranNoktasi,
                                            double toleransPiksel, ref SnapNoktasi? enYakin,
                                            ref double enKisa)
        {
            Point? adayEkran = EkranNoktasi(aday.Nokta);
            if (!adayEkran.HasValue) return;

            double uzaklik = Mesafe(adayEkran.Value, ekranNoktasi);
            if (uzaklik > toleransPiksel) return;
            if (uzaklik < enKisa - 0.01 ||
                (Math.Abs(uzaklik - enKisa) <= 0.01 &&
                 (enYakin == null || aday.Oncelik < enYakin.Oncelik)))
            {
                enYakin = aday;
                enKisa = uzaklik;
            }
        }

        private static bool DogruParcasiKesisimi(Point a, Point b, Point c, Point d,
                                                  out Point kesisim)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double cdx = d.X - c.X, cdy = d.Y - c.Y;
            double payda = abx * cdy - aby * cdx;
            if (Math.Abs(payda) < 1e-12) { kesisim = new Point(); return false; }

            double acx = c.X - a.X, acy = c.Y - a.Y;
            double t = (acx * cdy - acy * cdx) / payda;
            double u = (acx * aby - acy * abx) / payda;
            if (t < 0 || t > 1 || u < 0 || u > 1) { kesisim = new Point(); return false; }

            kesisim = new Point(a.X + t * abx, a.Y + t * aby);
            return true;
        }

        private static Point? EntityEnYakinNoktasi(DxfEntity entity, Point nokta)
        {
            if (entity.Tip == "CIRCLE" && entity.Radius > 0)
            {
                double dx = nokta.X - entity.Merkez.X, dy = nokta.Y - entity.Merkez.Y;
                double uzunluk = Math.Sqrt(dx * dx + dy * dy);
                if (uzunluk > 1e-12)
                    return new Point(entity.Merkez.X + dx * entity.Radius / uzunluk,
                                     entity.Merkez.Y + dy * entity.Radius / uzunluk);
            }

            Point[] noktalar = entity.Noktalar ?? Array.Empty<Point>();
            if (noktalar.Length < 2) return null;
            Point enYakin = noktalar[0];
            double enKisa = double.MaxValue;
            for (int i = 1; i < noktalar.Length; i++)
            {
                Point aday = SegmentEnYakinNokta(nokta, noktalar[i - 1], noktalar[i]);
                double uzaklik = Mesafe(nokta, aday);
                if (uzaklik < enKisa) { enKisa = uzaklik; enYakin = aday; }
            }
            return enYakin;
        }

        private static Point SegmentEnYakinNokta(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double uzunlukKaresi = dx * dx + dy * dy;
            if (uzunlukKaresi < 1e-12) return a;
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / uzunlukKaresi;
            t = Math.Max(0, Math.Min(1, t));
            return new Point(a.X + t * dx, a.Y + t * dy);
        }

        private DxfEntity? EntityBul(Point? nokta, bool yalnizLine = false)
        {
            if (!nokta.HasValue || _cizimModel == null) return null;

            double tolerans = 8.0 / Math.Max(0.001, _modelOlcek * olcek.ScaleX);
            DxfEntity? enYakin = null;
            double enKisa = double.MaxValue;

            foreach (DxfEntity entity in _cizimModel.Entityler)
            {
                if (entity.Tip != "LINE" && entity.Tip != "CIRCLE" && entity.Tip != "ARC")
                    continue;
                if (yalnizLine && entity.Tip != "LINE") continue;

                double uzaklik = EntityUzakligi(entity, nokta.Value);
                if (uzaklik <= tolerans && uzaklik < enKisa)
                {
                    enKisa = uzaklik;
                    enYakin = entity;
                }
            }

            return enYakin;
        }

        private static double EntityUzakligi(DxfEntity entity, Point nokta)
        {
            double enKisa = double.MaxValue;
            Point[] noktalar = entity.Noktalar ?? Array.Empty<Point>();

            for (int i = 1; i < noktalar.Length; i++)
                enKisa = Math.Min(enKisa,
                    SegmentUzakligi(nokta, noktalar[i - 1], noktalar[i]));

            return enKisa;
        }

        private static double SegmentUzakligi(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double uzunlukKaresi = dx * dx + dy * dy;
            if (uzunlukKaresi < 1e-12) return Mesafe(p, a);

            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / uzunlukKaresi;
            t = Math.Max(0, Math.Min(1, t));
            return Mesafe(p, new Point(a.X + t * dx, a.Y + t * dy));
        }

        private Point? EkranNoktasi(Point modelNoktasi)
        {
            if (cizim.Data == null || _modelOlcek <= 0) return null;
            return cizim.TranslatePoint(new Point(modelNoktasi.X, -modelNoktasi.Y),
                                        cizimAlani);
        }

        private void SnapGoster(SnapNoktasi? snap)
        {
            _aktifSnap = snap;
            if (snap == null)
            {
                snapIsareti.Data = null;
                snapIsareti.Visibility = Visibility.Collapsed;
                txtSnap.Text = "";
                return;
            }

            if (cizim.Data == null || _modelOlcek <= 0) return;

            double r = 5.0 / (_modelOlcek * Math.Max(0.001, olcek.ScaleX));
            Point p = new Point(snap.Nokta.X, -snap.Nokta.Y);
            var geo = new StreamGeometry();
            using (StreamGeometryContext context = geo.Open())
            {
                context.BeginFigure(new Point(p.X - r, p.Y), false, false);
                context.LineTo(new Point(p.X, p.Y - r), true, false);
                context.LineTo(new Point(p.X + r, p.Y), true, false);
                context.LineTo(new Point(p.X, p.Y + r), true, false);
                context.LineTo(new Point(p.X - r, p.Y), true, false);
            }
            geo.Freeze();
            snapIsareti.Data = geo;
            snapIsareti.RenderTransform = _modelToScreen;
            snapIsareti.Visibility = Visibility.Visible;
            txtSnap.Text = "Snap: " + snap.Tip;
        }

        private Point? ModelNoktasi(Point ekranNoktasi)
        {
            if (_cizimModel == null || cizim.Data == null) return null;

            Point pathNoktasi = cizimAlani.TranslatePoint(ekranNoktasi, cizim);
            return new Point(pathNoktasi.X, -pathNoktasi.Y);
        }

        private static Geometry EntityGeometrisi(DxfEntity entity)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                Point[] points = entity.Noktalar ?? Array.Empty<Point>();
                if (points.Length > 1)
                {
                    context.BeginFigure(new Point(points[0].X, -points[0].Y), false, false);
                    for (int i = 1; i < points.Length; i++)
                        context.LineTo(new Point(points[i].X, -points[i].Y), true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private void SeciliGeometriyiGuncelle()
        {
            KoseMarkeriniGuncelle();
            if (_editModu)
            {
                var secim = new GeometryGroup();
                foreach (DxfEntity entity in _aktifKoseKomutu == KoseKomutTuru.Yok ? _editSecimi : (IEnumerable<DxfEntity>)_komutCizgileri)
                    secim.Children.Add(EntityGeometrisi(entity));
                secim.Freeze();
                seciliCizim.Data = secim.Children.Count == 0 ? null : secim;
                seciliCizim.Visibility = secim.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                seciliCizim.RenderTransform = _modelToScreen;
            }
            else if (_kip == "CizgiCizgi")
            {
                var secim = new GeometryGroup();
                if (_ilkOlcuCizgisi != null) secim.Children.Add(EntityGeometrisi(_ilkOlcuCizgisi));
                if (_ikinciOlcuCizgisi != null) secim.Children.Add(EntityGeometrisi(_ikinciOlcuCizgisi));
                secim.Freeze();
                seciliCizim.Data = secim.Children.Count == 0 ? null : secim;
                seciliCizim.Visibility = secim.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            if ((_seciliEntity == null && _ilkOlcuCizgisi == null) || cizim.Data == null)
            {
                OlcuGeometrisiniGuncelle();
                return;
            }
            seciliCizim.RenderTransform = _modelToScreen;
            OlcuGeometrisiniGuncelle();
        }

        private void OlcuGeometrisiniTemizle()
        {
            mesafeNoktasiIsareti.Data = null;
            mesafeNoktasiIsareti.Visibility = Visibility.Collapsed;
            mesafeCizgisi.Data = null;
            mesafeCizgisi.Visibility = Visibility.Collapsed;
            olcuYazisi.Data = null;
            olcuYazisi.Visibility = Visibility.Collapsed;
        }

        // Olcu katmani secim katmaninin model->ekran donusumunu kullanir;
        // bu nedenle zoom veya pan sonrasinda geometriyle hizali kalir.
        private void OlcuGeometrisiniGuncelle()
        {
            if (!_olcuBaslangicNoktasi.HasValue)
            {
                OlcuGeometrisiniTemizle();
                return;
            }

            if (cizim.Data == null || _modelOlcek <= 0) return;

            Point ilk = _olcuBaslangicNoktasi.Value;
            Point? hedef = _sonMesafeNoktasi ?? _olcuOnizlemeNoktasi;
            double yaricap = 5.0 / (_modelOlcek * Math.Max(0.001, olcek.ScaleX));
            if (_cizgiOlcuYonu.HasValue)
            {
                mesafeNoktasiIsareti.Data = null;
                mesafeNoktasiIsareti.Visibility = Visibility.Collapsed;
            }
            else
            {
                mesafeNoktasiIsareti.Data = new EllipseGeometry(
                    new Point(ilk.X, -ilk.Y), yaricap, yaricap);
                mesafeNoktasiIsareti.RenderTransform = _modelToScreen;
                mesafeNoktasiIsareti.Visibility = Visibility.Visible;
            }

            if (hedef.HasValue)
            {
                Point son = hedef.Value;
                bool yatay = Math.Abs(son.X - ilk.X) >= Math.Abs(son.Y - ilk.Y);
                double kayma = 18.0 / (_modelOlcek * Math.Max(0.001, olcek.ScaleX));
                double tick = 4.0 / (_modelOlcek * Math.Max(0.001, olcek.ScaleX));
                Point a, b;
                Point uzantiBaslangic1 = ilk, uzantiBaslangic2 = son;
                Vector olcuYonu;
                double deger;
                if (_cizgiOlcuYonu.HasValue)
                {
                    olcuYonu = _cizgiOlcuYonu.Value;
                    // Metin her zaman mevcut gercek dik mesafe hesabindan gelir.
                    deger = Math.Abs(Vector.Multiply(son - ilk, olcuYonu));
                    CizgiOlcuEkseni eksen = CizgiOlcuEkseniniBul();
                    if (eksen != CizgiOlcuEkseni.Egik &&
                        _ilkOlcuCizgisi != null && _ikinciOlcuCizgisi != null)
                    {
                        bool yatayCift = eksen == CizgiOlcuEkseni.YatayCizgiler;
                        double konum = (yatayCift ? (ilk.X + son.X) / 2 : (ilk.Y + son.Y) / 2) +
                                       _cizgiOlcuYerlesimOfseti;
                        uzantiBaslangic1 = CizgiUzerindeEksenProjeksiyonu(_ilkOlcuCizgisi, konum, yatayCift);
                        uzantiBaslangic2 = CizgiUzerindeEksenProjeksiyonu(_ikinciOlcuCizgisi, konum, yatayCift);
                        if (yatayCift)
                        {
                            a = new Point(konum, uzantiBaslangic1.Y);
                            b = new Point(konum, uzantiBaslangic2.Y);
                            olcuYonu = new Vector(0, 1);
                        }
                        else
                        {
                            a = new Point(uzantiBaslangic1.X, konum);
                            b = new Point(uzantiBaslangic2.X, konum);
                            olcuYonu = new Vector(1, 0);
                        }
                    }
                    else
                    {
                        Vector uzantiYonu = new Vector(olcuYonu.Y, -olcuYonu.X);
                        Vector uzanti = uzantiYonu * _cizgiOlcuUzantiOfseti +
                                        olcuYonu * _cizgiOlcuYerlesimOfseti;
                        a = ilk + uzanti;
                        b = son + uzanti;
                    }
                }
                else if (yatay)
                {
                    double y = Math.Max(ilk.Y, son.Y) + kayma;
                    a = new Point(ilk.X, y);
                    b = new Point(son.X, y);
                    olcuYonu = new Vector(1, 0);
                    deger = Math.Abs(son.X - ilk.X);
                }
                else
                {
                    double x = Math.Max(ilk.X, son.X) + kayma;
                    a = new Point(x, ilk.Y);
                    b = new Point(x, son.Y);
                    olcuYonu = new Vector(0, 1);
                    deger = Math.Abs(son.Y - ilk.Y);
                }

                var geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    // Extension lines, dimension line and short CAD-style ticks.
                    Cizgi(context, uzantiBaslangic1, a);
                    Cizgi(context, uzantiBaslangic2, b);
                    Cizgi(context, a, b);
                    Vector tickYonu = (olcuYonu + new Vector(-olcuYonu.Y, olcuYonu.X)) * tick;
                    Cizgi(context, a - tickYonu, a + tickYonu);
                    Cizgi(context, b - tickYonu, b + tickYonu);
                }
                geometry.Freeze();
                mesafeCizgisi.Data = geometry;
                mesafeCizgisi.RenderTransform = _modelToScreen;
                mesafeCizgisi.Visibility = Visibility.Visible;

                string metin = deger.ToString("N2", CultureInfo.CurrentCulture) + " mm";
                var yazi = new FormattedText(metin, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal,
                    FontWeights.SemiBold, FontStretches.Normal),
                    12.0 / (_modelOlcek * Math.Max(0.001, olcek.ScaleX)), Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                Point orta = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                Geometry yaziGeometrisi = yazi.BuildGeometry(new Point(
                    orta.X - yazi.Width / 2, -orta.Y - yazi.Height / 2));
                olcuYazisi.Data = yaziGeometrisi;
                olcuYazisi.RenderTransform = _modelToScreen;
                olcuYazisi.Visibility = Visibility.Visible;
            }
            else
            {
                mesafeCizgisi.Data = null;
                mesafeCizgisi.Visibility = Visibility.Collapsed;
                olcuYazisi.Data = null;
                olcuYazisi.Visibility = Visibility.Collapsed;
            }
        }

        private static void Cizgi(StreamGeometryContext context, Point bas, Point son)
        {
            context.BeginFigure(new Point(bas.X, -bas.Y), false, false);
            context.LineTo(new Point(son.X, -son.Y), true, false);
        }

        private static string EntityMetni(DxfEntity entity)
        {
            if (entity.Tip == "LINE")
            {
                Point bas = entity.Noktalar[0];
                Point son = entity.Noktalar[entity.Noktalar.Length - 1];
                return "Tip: LINE · Uzunluk: " + Mesafe(bas, son).ToString("N2") +
                       " mm · Başlangıç: " + NoktaMetni(bas) +
                       " · Bitiş: " + NoktaMetni(son);
            }

            if (entity.Tip == "CIRCLE")
                return "Tip: CIRCLE · Çap: Ø" + (entity.Radius * 2).ToString("N2") +
                       " mm · Radius: R" + entity.Radius.ToString("N2") +
                       " mm · Merkez: " + NoktaMetni(entity.Merkez);

            return "Tip: ARC · Radius: R" + entity.Radius.ToString("N2") +
                   " mm · Merkez: " + NoktaMetni(entity.Merkez) +
                   " · Başlangıç açısı: " + entity.BaslangicAcisi.ToString("N2") +
                   "° · Bitiş açısı: " + entity.BitisAcisi.ToString("N2") + "°";
        }

        private static string NoktaMetni(Point nokta)
        {
            return "X " + nokta.X.ToString("N2", CultureInfo.CurrentCulture) +
                   " / Y " + (-nokta.Y).ToString("N2", CultureInfo.CurrentCulture);
        }

        private static double Mesafe(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ================= PENCERE =================

        private void btnAc_Click(object sender, RoutedEventArgs e)
        {
            if (_yol.Length == 0) return;

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_yol) { UseShellExecute = true });
            }
            catch { }
        }

        private void btnMin_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void btnMax_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
