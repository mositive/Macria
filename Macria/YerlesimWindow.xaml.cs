using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Macria
{
    // Plakalarin ve uzerlerindeki parcalarin gorunumu.
    //
    // Tuval milimetre uzerinden calisir: ic tuvalin koordinati dogrudan mm,
    // yakinlastirma ve kaydirma bir RenderTransform ile yapilir. Boylece fare
    // konumu hicbir donusum hesabi olmadan mm olarak okunur.
    //
    // Vurus denetimi WPF'e birakilmaz; parcalar sinir kutulariyla elle
    // taranir. Kontur ic ice bircok acik parcadan olustugu icin dolgu
    // uzerinden vurus almak guvenilmez olurdu.
    public partial class YerlesimWindow : Window
    {
        private const double ParkAraligi = 60;

        // Etiketler plakanin ustune yaziliyor. Punto ekran olcegine gore
        // degistigi icin ayrilan yer sabit olamaz; plaka boyuna oranlanir.
        private double EtiketPayi
        {
            get { return Math.Max(120, _model.PlakaYuk * 0.14); }
        }

        // Plakalar arasi bosluk da ayni etiketi barindirmali
        private double PlakaAraligi
        {
            get { return EtiketPayi + 40; }
        }

        private readonly YerlesimModel _model;

        private readonly Dictionary<YerlesimParca, System.Windows.Shapes.Path> _gorseller =
            new Dictionary<YerlesimParca, System.Windows.Shapes.Path>();

        // Parcanin dort yanindaki payi gosteren kesikli cerceve
        private readonly Dictionary<YerlesimParca, System.Windows.Shapes.Rectangle> _halkalar =
            new Dictionary<YerlesimParca, System.Windows.Shapes.Rectangle>();

        private readonly List<TextBlock> _etiketler = new List<TextBlock>();

        // Kutulari doldururken Ayar_TextChanged bos yere calismasin
        private bool _kuruluyor = true;

        private YerlesimParca _secili;

        // Parca surukleme
        private YerlesimParca _tasinan;
        private Point _fareBasi;
        private Point _parcaBasi;      // dunya koordinati
        private int _eskiPlaka;
        private double _eskiX, _eskiY;
        private Point _geciciDunya;
        private bool _geciciUygun;

        // Gorunum kaydirma
        private bool _kaydiriliyor;
        private Point _kaydirBasi;
        private double _kaydirBasX, _kaydirBasY;

        private double _parkYuksekligi;

        // Fire agirligi ve bedeli icin; maliyet sayfasiyla ayni ayarlar
        private double _yogunluk = 7.85;
        private double _kgFiyat;

        private Brush _plakaFirca;
        private Brush _plakaKenar;
        private Brush _parcaKenar;
        private Brush _seciliKenar;
        private Brush _hataKenar;
        private Brush _yaziFirca;
        private Brush _solukYazi;

        internal YerlesimWindow(IEnumerable<SheetRow> satirlar)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            FircalariKur();

            // DXF'ler burada okunur; dosya sayisi cok olabilir
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                _model = YerlesimKurucu.Kur(satirlar,
                                            Ayarlar.PlakaBoy, Ayarlar.PlakaEn,
                                            Ayarlar.ParcaPayi, Ayarlar.PlakaKenarPayi,
                                            Ayarlar.SonCiktiKlasoru);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            txtBoy.Text = Yaz(Ayarlar.PlakaBoy);
            txtEn.Text = Yaz(Ayarlar.PlakaEn);
            txtPay.Text = Yaz(Ayarlar.ParcaPayi);
            txtKenar.Text = Yaz(Ayarlar.PlakaKenarPayi);

            MalzemeleriDoldur();

            _yogunluk = Ayarlar.Yogunluk;
            _kgFiyat = Ayarlar.KgFiyat;

            txtKgFiyat.Text = Yaz(_kgFiyat);
            txtKgEtiket.Text = "Kg Fiyatı (" + Ayarlar.ParaBirimi + ")";

            _kuruluyor = false;

            // Ilk acilista otomatik dizilir; kullanici sonra elle duzeltir
            YerlesimCozucu.Otomatik(_model);

            Loaded += (s, e) =>
            {
                Ciz();
                Sigdir();
            };

            KeyDown += Pencere_KeyDown;
            Closed += (s, e) => Ayarlar.Kaydet();
        }

        // ================= PLAKA AYARLARI =================
        //
        // Plaka olcusu ya da bosluk degisince eldeki yerlesim anlamini
        // yitirir; bu yuzden ayar degistiginde her sey yeniden dizilir.
        private void Ayar_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_kuruluyor || _model == null) return;

            double boy, en, pay, kenar;

            if (!Oku(txtBoy.Text, out boy) || boy <= 0 ||
                !Oku(txtEn.Text, out en) || en <= 0)
            {
                txtAyarUyari.Text = "Plaka ölçüsü sıfırdan büyük olmalı.";
                return;
            }

            if (!Oku(txtPay.Text, out pay) || pay < 0 ||
                !Oku(txtKenar.Text, out kenar) || kenar < 0)
            {
                txtAyarUyari.Text = "Pay ve kenar payı negatif olamaz.";
                return;
            }

            if (2 * kenar >= en || 2 * kenar >= boy)
            {
                txtAyarUyari.Text = "Kenar payı plakadan büyük.";
                return;
            }

            txtAyarUyari.Text = "";

            _model.PlakaGen = boy;
            _model.PlakaYuk = en;
            _model.VarsayilanPay = pay;
            _model.Kenar = kenar;

            // Varsayilan degisince butun parcalar yeni payi alir
            foreach (YerlesimParca p in _model.Parcalar) p.Pay = pay;

            Ayarlar.PlakaBoy = boy;
            Ayarlar.PlakaEn = en;
            Ayarlar.ParcaPayi = pay;
            Ayarlar.PlakaKenarPayi = kenar;

            YerlesimCozucu.Otomatik(_model);

            Ciz();
            Sec(null);
            Sigdir();
        }

        // Secili cesidin payi. Tek bir ornegi degil, o parcadan gelen butun
        // ornekleri etkiler: ayni parcanin kopyalari farkli pay tasiyamaz.
        private void SeciliPay_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_kuruluyor || _model == null || _secili == null) return;

            double pay;

            if (!Oku(txtSeciliPay.Text, out pay) || pay < 0)
            {
                txtAyarUyari.Text = "Pay negatif olamaz.";
                return;
            }

            txtAyarUyari.Text = "";

            int tur = _secili.RenkIndeks;

            foreach (YerlesimParca p in _model.Parcalar)
                if (p.RenkIndeks == tur) p.Pay = pay;

            // Aciklik degisti, yerlesim bastan kurulur
            YerlesimParca secili = _secili;

            YerlesimCozucu.Otomatik(_model);

            Ciz();
            Sec(secili);
        }

        // ================= MALZEME VE FIYAT =================
        //
        // Yogunluk ve kg fiyati maliyet sayfasiyla ortak; burada degistirilirse
        // orada da gecerli olur, iki yerde ayri sayi tutmanin anlami yok.

        private void MalzemeleriDoldur()
        {
            MalzemeleriDoldur(Ayarlar.MalzemeAdi);
        }

        // Listeyi varsayilan + ozel malzemelerden kurar ve verilen adi secer
        private void MalzemeleriDoldur(string secilecekAd)
        {
            List<Malzeme> malzemeler = MalzemeDeposu.Tumu();
            cmbMalzeme.ItemsSource = malzemeler;

            var secili = malzemeler.Find(m => m.Ad == secilecekAd) ?? malzemeler[0];

            cmbMalzeme.SelectedItem = secili;
            btnMalzemeSil.IsEnabled = secili.Ozel;
        }

        // Ozel malzemeler maliyet sayfasiyla ayni depoda tutulur; buradan
        // eklenen orada da gorunur
        private void btnMalzemeEkle_Click(object sender, RoutedEventArgs e)
        {
            var pencere = new MalzemeWindow { Owner = this };
            if (pencere.ShowDialog() != true) return;

            MalzemeDeposu.Ekle(pencere.MalzemeAd, pencere.MalzemeYogunluk);

            _kuruluyor = true;
            MalzemeleriDoldur(pencere.MalzemeAd);
            _kuruluyor = false;

            MalzemeyiUygula();
        }

        private void btnMalzemeSil_Click(object sender, RoutedEventArgs e)
        {
            var m = cmbMalzeme.SelectedItem as Malzeme;
            if (m == null || !m.Ozel) return;

            MalzemeDeposu.Sil(m.Ad);

            _kuruluyor = true;
            MalzemeleriDoldur("");
            _kuruluyor = false;

            MalzemeyiUygula();
        }

        private void cmbMalzeme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_kuruluyor || _model == null) return;
            MalzemeyiUygula();
        }

        private void MalzemeyiUygula()
        {
            var m = cmbMalzeme.SelectedItem as Malzeme;

            // Ozel malzeme silinebilir, hazir olanlar silinemez
            btnMalzemeSil.IsEnabled = m != null && m.Ozel;

            if (m == null || _model == null) return;

            _yogunluk = m.Yogunluk;

            Ayarlar.MalzemeAdi = m.Ad;
            Ayarlar.Yogunluk = m.Yogunluk;

            // Geometri degismedi ama plaka etiketlerindeki fire agirligi da
            // yogunluga bagli, o yuzden tuval tazelenir
            YerlesimParca secili = _secili;

            Ciz();
            Sec(secili);
        }

        private void Fiyat_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_kuruluyor || _model == null) return;

            double fiyat;

            if (!Oku(txtKgFiyat.Text, out fiyat) || fiyat < 0)
            {
                // Bos ya da gecersizken bedel gosterilmez
                _kgFiyat = 0;
                HesapYaz();
                return;
            }

            _kgFiyat = fiyat;
            Ayarlar.KgFiyat = fiyat;

            HesapYaz();
        }

        // Kullanici virgul de nokta da yazabilir
        private static bool Oku(string metin, out double deger)
        {
            deger = 0;
            if (string.IsNullOrWhiteSpace(metin)) return false;

            string temiz = metin.Trim();

            if (double.TryParse(temiz, NumberStyles.Float, CultureInfo.CurrentCulture,
                                out deger)) return true;

            return double.TryParse(temiz, NumberStyles.Float, CultureInfo.InvariantCulture,
                                   out deger);
        }

        private static string Yaz(double deger)
        {
            return deger.ToString("0.##", CultureInfo.CurrentCulture);
        }

        private void FircalariKur()
        {
            _plakaFirca = (Brush)FindResource("SurfaceBrush");
            _plakaKenar = (Brush)FindResource("BorderBrush");
            _yaziFirca = (Brush)FindResource("TextSecondaryBrush");
            _solukYazi = (Brush)FindResource("TextDisabledBrush");

            _parcaKenar = Donmus(Color.FromRgb(0xC8, 0xD6, 0xE8));
            _seciliKenar = Donmus(Color.FromRgb(0x6C, 0xB2, 0xFF));
            _hataKenar = Donmus(Color.FromRgb(0xF0, 0x5B, 0x50));
        }

        private static Brush Donmus(Color renk)
        {
            var f = new SolidColorBrush(renk);
            f.Freeze();
            return f;
        }

        // Her parca cesidi grafiklerdeki paletten kendi rengini alir.
        // Ayni cesidin butun ornekleri ayni renkte oldugu icin plakada
        // hangi parcadan kac tane var, sayilmadan gorulur.
        private readonly Dictionary<int, Brush> _dolgular = new Dictionary<int, Brush>();

        private Brush Dolgu(int indeks)
        {
            Brush hazir;
            if (_dolgular.TryGetValue(indeks, out hazir)) return hazir;

            Color c = GrafikCizer.Palet[indeks % GrafikCizer.Palet.Length];

            Brush f = Donmus(Color.FromArgb(0x9E, c.R, c.G, c.B));
            _dolgular[indeks] = f;

            return f;
        }

        // Pay cercevesi parcanin kendi renginde ama soluk: hangi aciklik
        // kime ait, karismaz
        private readonly Dictionary<int, Brush> _payFircalari = new Dictionary<int, Brush>();

        private Brush PayFircasi(int indeks)
        {
            Brush hazir;
            if (_payFircalari.TryGetValue(indeks, out hazir)) return hazir;

            Color c = GrafikCizer.Palet[indeks % GrafikCizer.Palet.Length];

            Brush f = Donmus(Color.FromArgb(0x88, c.R, c.G, c.B));
            _payFircalari[indeks] = f;

            return f;
        }

        // ================= DUNYA OLCULERI =================

        private double PlakaUst(int plaka)
        {
            return EtiketPayi + plaka * (_model.PlakaYuk + PlakaAraligi);
        }

        private double ParkUst()
        {
            return EtiketPayi + _model.Plakalar.Count * (_model.PlakaYuk + PlakaAraligi);
        }

        private double DunyaGenislik { get { return _model.PlakaGen; } }

        private double DunyaYukseklik { get { return ParkUst() + _parkYuksekligi; } }

        // Parcanin sol ust kosesinin dunya konumu
        private Point Dunya(YerlesimParca p)
        {
            double ust = p.Plaka >= 0 ? PlakaUst(p.Plaka) : ParkUst();
            return new Point(p.X, ust + p.Y);
        }

        // Bekleme alanindaki parcalar satir satir dizilir
        private void ParkiDuzenle()
        {
            double x = 0, y = ParkAraligi, satirYuksekligi = 0;

            foreach (YerlesimParca p in _model.Bekleyenler())
            {
                double g = p.EtkinGenislik;
                double h = p.EtkinYukseklik;

                if (x > 0 && x + g > DunyaGenislik)
                {
                    x = 0;
                    y += satirYuksekligi + ParkAraligi;
                    satirYuksekligi = 0;
                }

                p.X = x;
                p.Y = y;

                x += g + ParkAraligi;
                if (h > satirYuksekligi) satirYuksekligi = h;
            }

            _parkYuksekligi = y + satirYuksekligi + ParkAraligi;

            // Bos olsa da alan gorunur kalsin
            if (_parkYuksekligi < 400) _parkYuksekligi = 400;
        }

        // ================= CIZIM =================

        private void Ciz()
        {
            ParkiDuzenle();

            ic.Children.Clear();
            _gorseller.Clear();
            _halkalar.Clear();
            _etiketler.Clear();

            ic.Width = DunyaGenislik;
            ic.Height = DunyaYukseklik;

            for (int i = 0; i < _model.Plakalar.Count; i++) PlakaCiz(i);

            ParkCiz();

            foreach (YerlesimParca p in _model.Parcalar) ParcaCiz(p);

            OlcekUygula();
            OzetiYaz();

            bool bos = _model.Parcalar.Count == 0;

            txtBosMesaj.Visibility = bos ? Visibility.Visible : Visibility.Collapsed;

            if (bos)
                txtBosMesaj.Text =
                    "Yerleştirilecek Parça Yok\n\n" +
                    "Görsel yerleşim, dışa aktarılmış DXF dosyalarındaki gerçek " +
                    "ölçüleri kullanır. Önce Toplu DXF Export sayfasından " +
                    "parçaları dışa aktarın.";
        }

        private void PlakaCiz(int indeks)
        {
            double ust = PlakaUst(indeks);

            var zemin = new System.Windows.Shapes.Rectangle
            {
                Width = _model.PlakaGen,
                Height = _model.PlakaYuk,
                Fill = _plakaFirca,
                Stroke = _plakaKenar,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(zemin, 0);
            Canvas.SetTop(zemin, ust);
            ic.Children.Add(zemin);

            // Kenar payi: parca buraya girmez
            if (_model.Kenar > 0)
            {
                var ickenar = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(0, _model.PlakaGen - 2 * _model.Kenar),
                    Height = Math.Max(0, _model.PlakaYuk - 2 * _model.Kenar),
                    Stroke = _solukYazi,
                    StrokeDashArray = new DoubleCollection { 6, 6 },
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(ickenar, _model.Kenar);
                Canvas.SetTop(ickenar, ust + _model.Kenar);
                ic.Children.Add(ickenar);
            }

            int adet = 0;
            foreach (YerlesimParca p in _model.PlakadakiParcalar(indeks)) adet++;

            EtiketEkle("Plaka " + (indeks + 1) + "  ·  " +
                       Say(_model.Plakalar[indeks].Kalinlik, 2) + " mm  ·  " +
                       adet + " parça  ·  Doluluk %" +
                       Say(_model.Doluluk(indeks) * 100, 0) + "  ·  Fire " +
                       Say(_model.FireAgirligiKg(indeks, _yogunluk), 0) + " kg",
                       _yaziFirca, ust);
        }

        // Etiket plakanin hemen ustune oturur. Punto yakinlastirmayla
        // degistigi icin konumu OlcekUygula her seferinde tazeler.
        private void EtiketEkle(string metin, Brush firca, double capa)
        {
            var etiket = new TextBlock
            {
                Text = metin,
                Foreground = firca,
                IsHitTestVisible = false,
                Tag = capa
            };

            Canvas.SetLeft(etiket, 0);
            Canvas.SetTop(etiket, capa - EtiketPayi);

            ic.Children.Add(etiket);
            _etiketler.Add(etiket);
        }

        private void ParkCiz()
        {
            double ust = ParkUst();

            var alan = new System.Windows.Shapes.Rectangle
            {
                Width = DunyaGenislik,
                Height = _parkYuksekligi,
                Stroke = _plakaKenar,
                StrokeDashArray = new DoubleCollection { 8, 8 },
                IsHitTestVisible = false
            };

            Canvas.SetLeft(alan, 0);
            Canvas.SetTop(alan, ust);
            ic.Children.Add(alan);

            int sayi = 0;
            foreach (YerlesimParca p in _model.Bekleyenler()) sayi++;

            EtiketEkle(sayi > 0
                           ? "Bekleme Alanı  ·  " + sayi + " parça"
                           : "Bekleme Alanı  ·  boş",
                       sayi > 0 ? (Brush)FindResource("WarnTextBrush") : _solukYazi,
                       ust);
        }

        private void ParcaCiz(YerlesimParca p)
        {
            // Pay cercevesi once eklenir ki kontur uzerinde kalsin
            var halka = new System.Windows.Shapes.Rectangle
            {
                Stroke = PayFircasi(p.RenkIndeks),
                StrokeDashArray = new DoubleCollection { 4, 4 },
                IsHitTestVisible = false
            };

            ic.Children.Add(halka);
            _halkalar[p] = halka;

            var sekil = new System.Windows.Shapes.Path
            {
                Data = p.Kontur,
                Fill = Dolgu(p.RenkIndeks),
                Stroke = _parcaKenar,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };

            ic.Children.Add(sekil);
            _gorseller[p] = sekil;

            KonumUygula(p);
        }

        // Kontur (0,0)-(Genislik,Yukseklik) kutusunda duruyor; donus ve
        // otelemeyi RenderTransform yapar, yerlesim etkilenmez.
        private void KonumUygula(YerlesimParca p)
        {
            System.Windows.Shapes.Path sekil;
            if (!_gorseller.TryGetValue(p, out sekil)) return;

            Point d = ReferenceEquals(p, _tasinan) ? _geciciDunya : Dunya(p);

            // 90 derece cevirme: (x,y) -> (Yukseklik - y, x)
            Matrix m = p.Donuk
                ? new Matrix(0, 1, -1, 0, p.Yukseklik, 0)
                : Matrix.Identity;

            m.Translate(d.X, d.Y);

            sekil.RenderTransform = new MatrixTransform(m);

            bool secili = ReferenceEquals(p, _secili);
            bool hatali = ReferenceEquals(p, _tasinan) && !_geciciUygun;

            sekil.Stroke = hatali ? _hataKenar : (secili ? _seciliKenar : _parcaKenar);

            // Pay cercevesi parcayla birlikte tasinir
            System.Windows.Shapes.Rectangle halka;
            if (!_halkalar.TryGetValue(p, out halka)) return;

            if (p.Pay <= 0)
            {
                halka.Visibility = Visibility.Collapsed;
                return;
            }

            halka.Visibility = Visibility.Visible;
            halka.Width = p.EtkinGenislik + 2 * p.Pay;
            halka.Height = p.EtkinYukseklik + 2 * p.Pay;

            Canvas.SetLeft(halka, d.X - p.Pay);
            Canvas.SetTop(halka, d.Y - p.Pay);

            halka.Stroke = hatali ? _hataKenar : PayFircasi(p.RenkIndeks);
        }

        // Cizgiler ekranda hep ayni incelikte gorunsun; tuval mm olcegindedir
        private void OlcekUygula()
        {
            double s = olcek.ScaleX;
            if (s <= 0) return;

            double kalinlik = 1.0 / s;
            double kalin = 2.0 / s;

            foreach (UIElement e in ic.Children)
            {
                var sekil = e as System.Windows.Shapes.Shape;
                if (sekil == null) continue;

                sekil.StrokeThickness =
                    ReferenceEquals(sekil, SeciliSekil()) ? kalin : kalinlik;

                if (sekil.StrokeDashArray != null && sekil.StrokeDashArray.Count > 0)
                    sekil.StrokeThickness = kalinlik;
            }

            // Etiketler mm uzayinda; ekranda sabit puntoda gorunsun diye
            // olcegin tersiyle buyutulur. Ust siniri ayrilan yer belirler,
            // yoksa uzaklastikca yazi plakanin uzerine biner.
            double punto = Math.Min(15.0 / s, EtiketPayi * 0.62);

            foreach (TextBlock yazi in _etiketler)
            {
                yazi.FontSize = punto;

                double capa = yazi.Tag is double ? (double)yazi.Tag : 0;
                Canvas.SetTop(yazi, capa - punto * 1.35);
            }
        }

        private System.Windows.Shapes.Path SeciliSekil()
        {
            System.Windows.Shapes.Path sekil;

            if (_secili != null && _gorseller.TryGetValue(_secili, out sekil))
                return sekil;

            return null;
        }

        // ================= GORUNUM =================

        private void Sigdir()
        {
            if (tuval.ActualWidth < 10 || tuval.ActualHeight < 10) return;
            if (DunyaGenislik <= 0 || DunyaYukseklik <= 0) return;

            double s = Math.Min(tuval.ActualWidth / DunyaGenislik,
                                tuval.ActualHeight / DunyaYukseklik) * 0.94;

            if (s <= 0) return;

            olcek.ScaleX = s;
            olcek.ScaleY = s;

            kaydir.X = (tuval.ActualWidth - DunyaGenislik * s) / 2;
            kaydir.Y = (tuval.ActualHeight - DunyaYukseklik * s) / 2;

            _sigdirildi = true;
            OlcekUygula();
        }

        private bool _sigdirildi;

        // Ilk gercek olcu geldiginde bir kez sigdirilir; sonraki boyut
        // degisimleri kullanicinin yakinlastirmasini bozmaz
        private void tuval_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_sigdirildi) return;
            if (tuval.ActualWidth < 10 || tuval.ActualHeight < 10) return;

            Sigdir();
        }

        private void btnSigdir_Click(object sender, RoutedEventArgs e)
        {
            Sigdir();
        }

        private void tuval_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double eski = olcek.ScaleX;
            if (eski <= 0) return;

            double yeni = eski * Math.Pow(1.15, e.Delta / 120.0);

            double enAz = 0.02, enCok = 4.0;
            if (yeni < enAz) yeni = enAz;
            if (yeni > enCok) yeni = enCok;

            double k = yeni / eski;
            if (Math.Abs(k - 1) < 1e-9) return;

            // Farenin altindaki nokta sabit kalsin
            Point f = e.GetPosition(tuval);

            kaydir.X = f.X - (f.X - kaydir.X) * k;
            kaydir.Y = f.Y - (f.Y - kaydir.Y) * k;

            olcek.ScaleX = yeni;
            olcek.ScaleY = yeni;

            OlcekUygula();
            e.Handled = true;
        }

        // ================= FARE =================

        private void tuval_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point mm = e.GetPosition(ic);

            YerlesimParca vurulan = Bul(mm);

            if (vurulan != null)
            {
                Sec(vurulan);

                _tasinan = vurulan;
                _fareBasi = mm;
                _parcaBasi = Dunya(vurulan);
                _geciciDunya = _parcaBasi;
                _geciciUygun = true;

                _eskiPlaka = vurulan.Plaka;
                _eskiX = vurulan.X;
                _eskiY = vurulan.Y;

                // Tasinan parca payiyla birlikte ustte gorunsun
                System.Windows.Shapes.Rectangle halka;

                if (_halkalar.TryGetValue(vurulan, out halka))
                {
                    ic.Children.Remove(halka);
                    ic.Children.Add(halka);
                }

                System.Windows.Shapes.Path sekil = _gorseller[vurulan];
                ic.Children.Remove(sekil);
                ic.Children.Add(sekil);

                tuval.CaptureMouse();
                return;
            }

            Sec(null);

            _kaydiriliyor = true;
            _kaydirBasi = e.GetPosition(tuval);
            _kaydirBasX = kaydir.X;
            _kaydirBasY = kaydir.Y;

            tuval.CaptureMouse();
            tuval.Cursor = Cursors.SizeAll;
        }

        private void tuval_MouseMove(object sender, MouseEventArgs e)
        {
            if (_tasinan != null)
            {
                Point mm = e.GetPosition(ic);

                double dx = mm.X - _fareBasi.X;
                double dy = mm.Y - _fareBasi.Y;

                _geciciDunya = new Point(_parcaBasi.X + dx, _parcaBasi.Y + dy);

                int hedef;
                double hx, hy;

                _geciciUygun = HedefCoz(_tasinan, _geciciDunya, out hedef, out hx, out hy);

                KonumUygula(_tasinan);
                DurumYaz(_geciciUygun
                    ? null
                    : "Buraya konulamaz — plakanın dışında, başka parçayla çakışıyor " +
                      "ya da kalınlık tutmuyor.");
                return;
            }

            if (!_kaydiriliyor) return;

            Point f = e.GetPosition(tuval);

            kaydir.X = _kaydirBasX + (f.X - _kaydirBasi.X);
            kaydir.Y = _kaydirBasY + (f.Y - _kaydirBasi.Y);
        }

        private void tuval_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_tasinan != null)
            {
                YerlesimParca p = _tasinan;
                _tasinan = null;

                tuval.ReleaseMouseCapture();

                int hedef;
                double hx, hy;

                if (HedefCoz(p, _geciciDunya, out hedef, out hx, out hy))
                {
                    p.Plaka = hedef;
                    p.X = hx;
                    p.Y = hy;

                    _model.BosPlakalariAt();
                    Ciz();
                    Sec(p);
                }
                else
                {
                    // Gecersiz birakma eski yerine doner
                    p.Plaka = _eskiPlaka;
                    p.X = _eskiX;
                    p.Y = _eskiY;

                    KonumUygula(p);
                    DurumYaz("Taşıma geri alındı.");
                }

                return;
            }

            if (!_kaydiriliyor) return;

            _kaydiriliyor = false;
            tuval.ReleaseMouseCapture();
            tuval.Cursor = null;
        }

        // Birakilan noktaya gore hedef plakayi ve yerel konumu bulur.
        // Plakalarin disinda kalan her yer bekleme alanidir.
        private bool HedefCoz(YerlesimParca p, Point dunya,
                              out int plaka, out double x, out double y)
        {
            plaka = -1;
            x = dunya.X;
            y = dunya.Y - ParkUst();

            double merkezY = dunya.Y + p.EtkinYukseklik / 2;

            for (int i = 0; i < _model.Plakalar.Count; i++)
            {
                double ust = PlakaUst(i);

                if (merkezY < ust || merkezY > ust + _model.PlakaYuk) continue;

                double yerelX = Yasla(dunya.X, p, i, true);
                double yerelY = Yasla(dunya.Y - ust, p, i, false);

                if (!_model.Uygun(p, i, yerelX, yerelY)) return false;

                plaka = i;
                x = yerelX;
                y = yerelY;
                return true;
            }

            // Bekleme alani her zaman kabul eder
            return true;
        }

        // Kenarlara ve komsu parcalara yapistirir: elle dizerken bosluk
        // payini goz karariyla tutturmak zor.
        private double Yasla(double deger, YerlesimParca p, int plaka, bool yatay)
        {
            double olcu = yatay ? p.EtkinGenislik : p.EtkinYukseklik;
            double sinir = yatay ? _model.PlakaGen : _model.PlakaYuk;

            // Kenara dayarken plakanin kenar payi ile parcanin kendi payi toplanir
            var adaylar = new List<double>
            {
                _model.Kenar + p.Pay,
                sinir - _model.Kenar - p.Pay - olcu
            };

            foreach (YerlesimParca o in _model.PlakadakiParcalar(plaka))
            {
                if (ReferenceEquals(o, p)) continue;

                double bas = yatay ? o.X : o.Y;
                double son = bas + (yatay ? o.EtkinGenislik : o.EtkinYukseklik);

                // Iki parca arasindaki aciklik ikisinin payinin toplami
                double aciklik = p.Pay + o.Pay;

                adaylar.Add(son + aciklik);          // komsunun arkasina
                adaylar.Add(bas - aciklik - olcu);   // komsunun onune
                adaylar.Add(bas);                    // basi hizala
                adaylar.Add(son - olcu);             // sonu hizala
            }

            double enIyi = Math.Round(deger);
            double enYakin = 6.0;   // mm

            foreach (double a in adaylar)
            {
                double uzaklik = Math.Abs(a - deger);

                if (uzaklik < enYakin)
                {
                    enYakin = uzaklik;
                    enIyi = a;
                }
            }

            return enIyi;
        }

        // Ustteki parca once bulunsun diye liste tersten taranir
        private YerlesimParca Bul(Point mm)
        {
            for (int i = _model.Parcalar.Count - 1; i >= 0; i--)
            {
                YerlesimParca p = _model.Parcalar[i];
                Point d = Dunya(p);

                if (mm.X >= d.X && mm.X <= d.X + p.EtkinGenislik &&
                    mm.Y >= d.Y && mm.Y <= d.Y + p.EtkinYukseklik)
                    return p;
            }

            return null;
        }

        // ================= SECIM VE ISLEMLER =================

        private void Sec(YerlesimParca p)
        {
            YerlesimParca eski = _secili;
            _secili = p;

            if (eski != null) KonumUygula(eski);
            if (p != null) KonumUygula(p);

            OlcekUygula();

            btnDondur.IsEnabled = p != null;
            btnBekle.IsEnabled = p != null && p.Plaka >= 0;

            // Kutuyu doldurmak SeciliPay_TextChanged'i tetiklemesin
            _kuruluyor = true;
            txtSeciliPay.IsEnabled = p != null;
            txtSeciliPay.Text = p != null ? Yaz(p.Pay) : "";
            _kuruluyor = false;

            DurumYaz(null);
        }

        private void Pencere_KeyDown(object sender, KeyEventArgs e)
        {
            // Ayar kutusuna yazarken kisayol calismasin
            if (Keyboard.FocusedElement is TextBox) return;

            if (_secili == null) return;

            if (e.Key == Key.R) { Dondur(); e.Handled = true; }
            else if (e.Key == Key.Delete) { Beklet(); e.Handled = true; }
        }

        private void btnDondur_Click(object sender, RoutedEventArgs e)
        {
            Dondur();
        }

        private void Dondur()
        {
            if (_secili == null) return;

            YerlesimParca p = _secili;

            if (p.Plaka < 0)
            {
                p.Donuk = !p.Donuk;
                Ciz();
                Sec(p);
                return;
            }

            // Cevrilmis hali ayni yere sigmiyorsa dokunulmaz
            bool eski = p.Donuk;
            p.Donuk = !p.Donuk;

            if (!_model.Uygun(p, p.Plaka, p.X, p.Y))
            {
                p.Donuk = eski;
                DurumYaz("Çevrilmiş hali bu yere sığmıyor.");
                return;
            }

            KonumUygula(p);
            Ciz();
            Sec(p);
        }

        private void btnBekle_Click(object sender, RoutedEventArgs e)
        {
            Beklet();
        }

        private void Beklet()
        {
            if (_secili == null || _secili.Plaka < 0) return;

            _secili.Plaka = -1;

            _model.BosPlakalariAt();

            YerlesimParca p = _secili;
            Ciz();
            Sec(p);
        }

        private void btnOtomatik_Click(object sender, RoutedEventArgs e)
        {
            YerlesimCozucu.Otomatik(_model);

            Ciz();
            Sec(null);
            Sigdir();

            DurumYaz("Otomatik yerleştirildi.");
        }

        // ================= OZET =================

        private void OzetiYaz()
        {
            int bekleyen = 0;
            foreach (YerlesimParca p in _model.Bekleyenler()) bekleyen++;

            double parcaAlani = 0;
            foreach (YerlesimParca p in _model.Parcalar)
                // Plaka etiketleriyle ayni olcu: sinir kutusu degil gercek alan
                if (p.Plaka >= 0) parcaAlani += p.GercekAlan;

            double plakaAlani = _model.Plakalar.Count * _model.PlakaGen * _model.PlakaYuk;

            double doluluk = plakaAlani > 0 ? parcaAlani / plakaAlani * 100 : 0;

            // Kac ayri kalinlik var: plaka sayisinin asil sebebi cogu zaman bu
            var kalinliklar = new List<double>();

            foreach (YerlesimParca p in _model.Parcalar)
                if (!kalinliklar.Contains(p.Kalinlik)) kalinliklar.Add(p.Kalinlik);

            txtAltBaslik.Text =
                Say(_model.PlakaGen, 0) + " × " + Say(_model.PlakaYuk, 0) +
                " mm  ·  " + _model.Plakalar.Count + " plaka  ·  " +
                _model.Parcalar.Count + " parça  ·  " +
                kalinliklar.Count + " kalınlık  ·  Doluluk %" + Say(doluluk, 0) +
                (bekleyen > 0 ? "  ·  " + bekleyen + " parça bekliyor" : "");

            // Disarida kalanlar gecici mesajlarin altinda kaybolmasin
            var disarida = new List<string>();

            if (_model.DxfsizCesit > 0)
                disarida.Add(_model.DxfsizCesit + " çeşit (" + _model.DxfsizAdet +
                             " parça) DXF'i bulunamadı");

            if (_model.OkunamayanCesit > 0)
                disarida.Add(_model.OkunamayanCesit + " çeşidin DXF'i okunamadı");

            if (_model.AdetSiniriAsildi)
                disarida.Add("parça sayısı sınırı aşıldı");

            if (_model.AlaniTahminiCesit > 0)
                disarida.Add(_model.AlaniTahminiCesit +
                             " çeşitte kapalı kontur bulunamadı, alan sınır " +
                             "kutusundan alındı (fire olduğundan az görünür)");

            if (disarida.Count > 0)
                txtAltBaslik.Text += "  ·  Dışarıda: " + string.Join(", ", disarida);

            HesapYaz();
            DurumYaz(null);
        }

        // ================= FIRE =================
        //
        // Yerlesim her degistiginde yeniden hesaplanir. Maliyet sayfasindaki
        // tahminden farki, verim yuzdesi gibi bir varsayima dayanmamasi:
        // buradaki fire olculen bir sayidir.
        private void HesapYaz()
        {
            double sacKg = 0, fireKg = 0;
            double sacAlan = 0, parcaAlan = 0;

            for (int i = 0; i < _model.Plakalar.Count; i++)
            {
                sacKg += _model.PlakaAgirligiKg(i, _yogunluk);
                fireKg += _model.FireAgirligiKg(i, _yogunluk);

                sacAlan += _model.PlakaAlaniMm2 / 1e6;
                parcaAlan += _model.ParcaAlaniMm2(i) / 1e6;
            }

            double parcaKg = sacKg - fireKg;
            double fireAlan = sacAlan - parcaAlan;

            double fireOran = sacKg > 0 ? fireKg / sacKg * 100 : 0;
            double parcaOran = sacKg > 0 ? parcaKg / sacKg * 100 : 0;

            txtSac.Text = Say(sacKg, 0) + " kg";
            txtSacAlt.Text = _model.Plakalar.Count + " plaka  ·  " +
                             Say(sacAlan, 1) + " m²";

            txtParcaKg.Text = Say(parcaKg, 0) + " kg";
            txtParcaAlt.Text = "%" + Say(parcaOran, 0) + "  ·  " +
                               Say(parcaAlan, 1) + " m²";

            txtFire.Text = Say(fireKg, 0) + " kg";
            txtFireAlt.Text = "%" + Say(fireOran, 0) + "  ·  " +
                              Say(fireAlan, 1) + " m²";

            if (_kgFiyat > 0)
            {
                txtFireBedel.Text = Say(fireKg * _kgFiyat, 0) + " " + Ayarlar.ParaBirimi;
                txtFireBedelAlt.Text = Say(fireKg, 0) + " kg × " +
                                       Say(_kgFiyat, 2) + " " + Ayarlar.ParaBirimi + "/kg";
            }
            else
            {
                txtFireBedel.Text = "—";
                txtFireBedelAlt.Text =
                    "Kg fiyatı girilirse firenin parasal karşılığı burada görünür.";
            }
        }

        private void DurumYaz(string mesaj)
        {
            if (mesaj != null)
            {
                txtDurum.Foreground = (Brush)FindResource("WarnTextBrush");
                txtDurum.Text = mesaj;
                return;
            }

            txtDurum.Foreground = (Brush)FindResource("TextSecondaryBrush");

            if (_secili != null)
            {
                txtDurum.Text =
                    _secili.Ad + "  ·  " +
                    Say(_secili.EtkinGenislik, 1) + " × " +
                    Say(_secili.EtkinYukseklik, 1) + " mm  ·  " +
                    Say(_secili.Kalinlik, 2) + " mm sac  ·  Pay " +
                    Say(_secili.Pay, 1) + " mm" +
                    (_secili.Donuk ? "  ·  90° çevrili" : "") +
                    (_secili.Plaka >= 0 ? "  ·  Plaka " + (_secili.Plaka + 1)
                                        : "  ·  bekleme alanında");
                return;
            }

            txtDurum.Text =
                "Her kalınlık ayrı plakaya gider  ·  " +
                "Dikdörtgen yerleşim; konturlar iç içe geçirilmez";
        }

        // ================= GORUNTU =================

        private void btnResim_Click(object sender, RoutedEventArgs e)
        {
            if (_model.Parcalar.Count == 0) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Görüntüsü (*.png)|*.png",
                FileName = "Yerlesim.png"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                Kaydet(dlg.FileName);
                DurumYaz("Görüntü kaydedildi: " + System.IO.Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex)
            {
                DurumYaz("Görüntü kaydedilemedi: " + ex.Message);
            }
        }

        // Tuval mm olceginde durdugu icin disari alirken gecici olarak
        // donusum kaldirilir, cizgi kalinliklari da ona gore ayarlanir.
        private void Kaydet(string yol)
        {
            Transform eskiDonusum = ic.RenderTransform;
            double eskiOlcek = olcek.ScaleX;

            // Genis kenar 2000 piksel olacak sekilde
            double s = 2000.0 / Math.Max(DunyaGenislik, 1);
            if (s * DunyaYukseklik > 6000) s = 6000.0 / DunyaYukseklik;

            try
            {
                olcek.ScaleX = s;
                olcek.ScaleY = s;
                OlcekUygula();

                ic.RenderTransform = Transform.Identity;
                ic.UpdateLayout();

                int gen = Math.Max(1, (int)(DunyaGenislik * s));
                int yuk = Math.Max(1, (int)(DunyaYukseklik * s));

                var gorsel = new DrawingVisual();

                using (DrawingContext dc = gorsel.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(s, s));

                    dc.DrawRectangle((Brush)FindResource("BgBrush"), null,
                                     new Rect(0, 0, DunyaGenislik, DunyaYukseklik));

                    dc.DrawRectangle(new VisualBrush(ic), null,
                                     new Rect(0, 0, DunyaGenislik, DunyaYukseklik));

                    dc.Pop();
                }

                var resim = new RenderTargetBitmap(gen, yuk, 96, 96, PixelFormats.Pbgra32);
                resim.Render(gorsel);

                var kodlayici = new PngBitmapEncoder();
                kodlayici.Frames.Add(BitmapFrame.Create(resim));

                using (var akis = System.IO.File.Create(yol)) kodlayici.Save(akis);
            }
            finally
            {
                ic.RenderTransform = eskiDonusum;
                olcek.ScaleX = eskiOlcek;
                olcek.ScaleY = eskiOlcek;
                OlcekUygula();
            }
        }

        // ================= YARDIMCILAR =================

        private static string Say(double deger, int ondalik)
        {
            return deger.ToString("N" + ondalik, CultureInfo.CurrentCulture);
        }

        private void btnKucult_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void btnBuyut_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void btnKapat_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Sayi kutularina harf girilmesin; virgul ve nokta serbest
        private void SadeceOndalik(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if ((c < '0' || c > '9') && c != ',' && c != '.') { e.Handled = true; return; }
        }
    }
}
