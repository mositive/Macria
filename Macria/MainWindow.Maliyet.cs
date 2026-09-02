using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Macria
{
    // Agirlik ve Maliyet sekmesi.
    //
    // Once montaj taranir (Toplu DXF Export ile ayni tarama), sonra her parca
    // sirayla acilip hacmi ve yuzey alani olculur. Agirlik ile kesim boyu bu
    // iki sayidan turetilir (bkz. CostRow).
    public partial class MainWindow
    {
        private readonly ObservableCollection<CostRow> _costRows =
            new ObservableCollection<CostRow>();

        private readonly Dictionary<string, object> _costRepRefs =
            new Dictionary<string, object>();

        private bool _maliyetCalisiyor;

        // Para birimi degisiminde fiyatlari cevirebilmek icin onceki secim
        private string _oncekiParaBirimi = "₺";
        private bool _kurDenendi;

        // ================= KURULUM =================

        private void MaliyetKur()
        {
            costGrid.ItemsSource = _costRows;

            MalzemeleriDoldur(Ayarlar.MalzemeAdi);

            txtYogunluk.Text = Ayarlar.Yogunluk.ToString("0.###", CultureInfo.CurrentCulture);
            txtKgFiyat.Text = Ayarlar.KgFiyat.ToString("0.##", CultureInfo.CurrentCulture);
            txtKesimFiyat.Text = Ayarlar.KesimFiyat.ToString("0.##", CultureInfo.CurrentCulture);

            // Kayitli para birimini sec; taninmiyorsa TL
            cmbParaBirimi.SelectedIndex = 0;
            foreach (ComboBoxItem oge in cmbParaBirimi.Items)
                if ((string)oge.Tag == Ayarlar.ParaBirimi) { oge.IsSelected = true; break; }

            _oncekiParaBirimi = Ayarlar.ParaBirimi;

            TabloDeposu.Yukle();
            IsiHaritasi.Acik = Ayarlar.IsiHaritasiAcik;
            TabloyuKur();
            IsiHaritasiniUygula();

            KurYaz();
            BasliklariYaz();
            OzetiYaz();
        }

        // ================= TABLO SUTUNLARI =================
        //
        // Sutunlar Tablo Ayarlari yapilandirmasindan uretilir: kullanici
        // sutun gizleyebilir, sirasini degistirebilir, kendi formullu
        // sutununu ekleyebilir.

        private readonly List<KeyValuePair<SutunTanimi, TextBlock>> _sutunBasliklari =
            new List<KeyValuePair<SutunTanimi, TextBlock>>();

        private void TabloyuKur()
        {
            costGrid.Columns.Clear();
            _sutunBasliklari.Clear();

            string pb = Ayarlar.ParaBirimi;

            foreach (SutunTanimi s in TabloDeposu.Sutunlar)
            {
                if (!s.Gorunur) continue;

                var baslik = new TextBlock
                {
                    Text = s.GorunenBaslik(pb),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = s.Metin ? TextAlignment.Left : TextAlignment.Right
                };

                _sutunBasliklari.Add(new KeyValuePair<SutunTanimi, TextBlock>(s, baslik));

                DataGridColumn sutun;

                if (s.Anahtar == "durum")
                {
                    sutun = new DataGridTemplateColumn
                    {
                        CellTemplate = (DataTemplate)FindResource("DurumHucre")
                    };
                }
                else
                {
                    var metinSutunu = new DataGridTextColumn();
                    var bag = new System.Windows.Data.Binding(BaglantiYolu(s));

                    if (!s.Metin) bag.StringFormat = "N" + s.Ondalik;
                    metinSutunu.Binding = bag;

                    if (!s.Metin)
                    {
                        metinSutunu.ElementStyle = (Style)FindResource(
                            s.Anahtar == "toplamMaliyet" ? "VurguHucre" : "SayiHucre");
                        metinSutunu.HeaderStyle = (Style)FindResource("SayiBaslik");
                        metinSutunu.CellStyle = IsiStili(s);
                    }

                    sutun = metinSutunu;
                }

                sutun.Header = baslik;

                if (s.Genislik > 0)
                {
                    sutun.Width = new DataGridLength(s.Genislik);
                }
                else
                {
                    sutun.Width = new DataGridLength(
                        s.Anahtar == "parca" ? 2 : 1.4, DataGridLengthUnitType.Star);
                    sutun.MinWidth = 90;
                }

                costGrid.Columns.Add(sutun);
            }
        }

        // Sayi hucrelerinin arka plani, sutunun kendi aralik icindeki yerine
        // gore tonlanir. Baglanti sutunun degerine kurulur; hangi sutun
        // oldugunu donusturucuye parametre soyler.
        private readonly IsiFircasi _isiFircasi = new IsiFircasi();

        private Style IsiStili(SutunTanimi s)
        {
            var bag = new System.Windows.Data.Binding(BaglantiYolu(s))
            {
                Converter = _isiFircasi,
                ConverterParameter = s.Anahtar
            };

            var stil = new Style(typeof(DataGridCell),
                                 (Style)FindResource(typeof(DataGridCell)));

            stil.Setters.Add(new Setter(DataGridCell.BackgroundProperty, bag));
            return stil;
        }

        private void btnIsiHaritasi_Click(object sender, RoutedEventArgs e)
        {
            Ayarlar.IsiHaritasiAcik = !Ayarlar.IsiHaritasiAcik;
            Ayarlar.Kaydet();

            IsiHaritasiniUygula();

            LogInfo(Ayarlar.IsiHaritasiAcik
                ? "Isı Haritası Açıldı — Hücreler Sütun İçindeki Değerine Göre Tonlanıyor."
                : "Isı Haritası Kapatıldı.");
        }

        private void IsiHaritasiniUygula()
        {
            IsiHaritasi.Acik = Ayarlar.IsiHaritasiAcik;

            // Acikken dugme hafif dolu gorunur (fare vurgusu sablonda kaliyor)
            btnIsiHaritasi.Background = Ayarlar.IsiHaritasiAcik
                ? (System.Windows.Media.Brush)FindResource("SurfaceBrush")
                : System.Windows.Media.Brushes.Transparent;

            IsiHaritasi.Olc(_costRows, TabloDeposu.Sutunlar);
            costGrid.Items.Refresh();
        }

        // Bu iki pencere de konsol gibi modelsiz acilir: kipli olsalardi simge
        // durumuna kucultuldugunde ana pencere kilitli kalirdi. Ikinci kez
        // acilmazlar, one getirilirler.
        private GrafikWindow _grafikWindow;
        private NestingWindow _nestingWindow;

        private void btnGrafikler_Click(object sender, RoutedEventArgs e)
        {
            if (OneGetir(_grafikWindow)) return;

            _grafikWindow = new GrafikWindow(_costRows, Ayarlar.ParaBirimi) { Owner = this };
            _grafikWindow.Closed += (s, ev) => _grafikWindow = null;
            _grafikWindow.Show();
        }

        private void btnNesting_Click(object sender, RoutedEventArgs e)
        {
            if (OneGetir(_nestingWindow)) return;

            _nestingWindow = new NestingWindow(_costRows, Ayarlar.Yogunluk,
                                               Ayarlar.KgFiyat, Ayarlar.ParaBirimi)
            { Owner = this };

            _nestingWindow.Closed += (s, ev) => _nestingWindow = null;
            _nestingWindow.Show();
        }

        // Acik pencere varsa simge durumundan cikarip one alir
        private static bool OneGetir(Window pencere)
        {
            if (pencere == null) return false;

            if (pencere.WindowState == WindowState.Minimized)
                pencere.WindowState = WindowState.Normal;

            pencere.Activate();
            return true;
        }

        private static string BaglantiYolu(SutunTanimi s)
        {
            switch (s.Anahtar)
            {
                case "urun": return "ProductName";
                case "parca": return "PartName";
                case "durum": return "Durum";
                case "kalinlik": return "Thickness";
                case "adet": return "Quantity";
                case "hacim": return "HacimM3";
                case "alan": return "AlanM2";
                case "birimAgirlik": return "BirimAgirlik";
                case "toplamAgirlik": return "ToplamAgirlik";
                case "kesimBoyu": return "KesimBoyu";
                case "toplamKesim": return "ToplamKesim";
                case "malzemeMaliyet": return "MalzemeMaliyet";
                case "kesimMaliyet": return "KesimMaliyet";
                case "toplamMaliyet": return "ToplamMaliyet";
            }

            return "Ozel[" + s.Anahtar + "]";
        }

        // Para birimi degisince baslik ve etiketler guncellenir
        private void BasliklariYaz()
        {
            string pb = Ayarlar.ParaBirimi;

            foreach (var ikili in _sutunBasliklari)
                ikili.Value.Text = ikili.Key.GorunenBaslik(pb);

            lblKgFiyat.Text = "Malzeme Fiyatı (" + pb + "/kg)";
            lblKesimFiyat.Text = "Kesim Fiyatı (" + pb + "/m)";
        }

        private void btnTabloAyarlari_Click(object sender, RoutedEventArgs e)
        {
            var pencere = new TabloAyarlariWindow { Owner = this };
            if (pencere.ShowDialog() != true) return;

            TabloyuKur();
            YenidenHesapla();

            LogSuccess("Tablo Ayarları Güncellendi — Sütun: " +
                       TabloDeposu.Sutunlar.FindAll(s => s.Gorunur).Count + " Görünür, " +
                       TabloDeposu.Sutunlar.FindAll(s => s.Tur == SutunTuru.Ozel).Count + " Özel.");
        }

        // ================= PARAMETRELER =================

        // Listeyi varsayilan + ozel malzemelerden kurar ve verilen adi secer
        private void MalzemeleriDoldur(string secilecekAd)
        {
            List<Malzeme> malzemeler = MalzemeDeposu.Tumu();
            cmbMalzeme.ItemsSource = malzemeler;

            cmbMalzeme.SelectedItem =
                malzemeler.Find(m => m.Ad == secilecekAd) ?? malzemeler[0];
        }

        private void cmbParaBirimi_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var oge = cmbParaBirimi.SelectedItem as ComboBoxItem;
            string yeni = oge != null ? (string)oge.Tag : _oncekiParaBirimi;
            string eski = _oncekiParaBirimi;
            _oncekiParaBirimi = yeni;

            if (IsLoaded && eski != yeni) FiyatlariCevirSor(eski, yeni);

            ParametreDegisti(null, null);
        }

        // ================= DOVIZ KURLARI =================

        private void KurYaz()
        {
            txtKurlar.Text = KurServisi.OzetMetni();
            txtKurKaynak.Text = KurServisi.KaynakMetni();

            string ipucu = KurServisi.KaynakAyrintisi();
            txtKurlar.ToolTip = ipucu;
            txtKurKaynak.ToolTip = ipucu;
        }

        private void btnKurYenile_Click(object sender, RoutedEventArgs e)
        {
            _ = KurleriYenile(true);
        }

        // elle: kullanici yenile dugmesine bastiysa sonuc konsola da yazilir
        private async Task KurleriYenile(bool elle)
        {
            _kurDenendi = true;
            btnKurYenile.IsEnabled = false;

            if (elle) LogInfo("Güncel Kurlar Alınıyor...");

            bool basarili = await KurServisi.Yenile();

            btnKurYenile.IsEnabled = true;
            KurYaz();

            if (basarili)
            {
                LogSuccess("Kurlar " + KurServisi.Kaynak + " Kaynağından Alındı (" +
                           KurServisi.KaynakAdresi + "): " + KurServisi.OzetMetni());
            }
            else
            {
                LogError("Kurlar İnternetten Alınamadı — Kullanılan Değerler: " +
                         KurServisi.KaynakMetni() + " · " + KurServisi.OzetMetni());
            }
        }

        // Maliyet sekmesi acilinca kurlar oturumda bir kez tazelenir
        private void KurlariTazele()
        {
            if (_kurDenendi) return;
            _ = KurleriYenile(false);
        }

        // Para birimi degisince girilmis fiyatlari kura gore cevirmeyi teklif eder
        private void FiyatlariCevirSor(string eski, string yeni)
        {
            double kg = Ondalik(txtKgFiyat.Text, 0);
            double kesim = Ondalik(txtKesimFiyat.Text, 0);

            if (kg <= 0 && kesim <= 0) return;

            double yeniKg = KurServisi.Cevir(kg, eski, yeni);
            double yeniKesim = KurServisi.Cevir(kesim, eski, yeni);

            var mesaj = new StringBuilder();
            mesaj.Append("Para birimi ").Append(eski).Append(" → ").Append(yeni)
                 .Append(" olarak değişti. Girdiğiniz fiyatlar güncel kura göre dönüştürülsün mü?")
                 .Append("\n\n");

            if (kg > 0)
                mesaj.Append("Malzeme:  ").Append(Fiyat(kg, eski)).Append("/kg   →   ")
                     .Append(Fiyat(yeniKg, yeni)).Append("/kg\n");

            if (kesim > 0)
                mesaj.Append("Kesim:  ").Append(Fiyat(kesim, eski)).Append("/m   →   ")
                     .Append(Fiyat(yeniKesim, yeni)).Append("/m\n");

            // Kur satirini her zaman 1'den buyuk tarafla yaz (1 € = 49,20 ₺ gibi)
            string a = eski, b = yeni;
            if (KurServisi.Cevir(1, eski, yeni) < 1) { a = yeni; b = eski; }

            mesaj.Append("\nKullanılan kur: ").Append(KurServisi.CiftMetni(a, b))
                 .Append("   (").Append(KurServisi.KaynakMetni()).Append(")");

            if (!OnayWindow.Sor(this, "Para Birimi Değişti", mesaj.ToString(),
                                "Dönüştür", "Değerleri Koru"))
            {
                LogInfo("Para Birimi " + yeni + " Olarak Değişti — Fiyatlar Olduğu Gibi Bırakıldı.");
                return;
            }

            if (kg > 0) txtKgFiyat.Text = yeniKg.ToString("0.##", CultureInfo.CurrentCulture);
            if (kesim > 0) txtKesimFiyat.Text = yeniKesim.ToString("0.##", CultureInfo.CurrentCulture);

            LogSuccess("Fiyatlar " + eski + " → " + yeni + " Kuruna Göre Dönüştürüldü (" +
                       KurServisi.CiftMetni(a, b) + ").");
        }

        // Toplam satirinda hangi sutunlar toplanir: adede bagli buyuklukler
        // ve kullanicinin kendi sutunlari. Kalinlik/birim agirlik toplanmaz.
        private static bool Toplanir(SutunTanimi s)
        {
            if (s.Tur == SutunTuru.Ozel) return true;

            switch (s.Anahtar)
            {
                case "adet":
                case "toplamAgirlik":
                case "toplamKesim":
                case "malzemeMaliyet":
                case "kesimMaliyet":
                case "toplamMaliyet":
                    return true;
            }

            return false;
        }

        private static string Fiyat(double deger, string birim)
        {
            return deger.ToString("N2", CultureInfo.CurrentCulture) + " " + birim;
        }

        private void btnYenidenHesapla_Click(object sender, RoutedEventArgs e)
        {
            ParametreDegisti(null, null);
            LogInfo("Tablo Yeniden Hesaplandı.");
        }

        private void cmbMalzeme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var m = cmbMalzeme.SelectedItem as Malzeme;
            if (m == null) return;

            txtYogunluk.Text = m.Yogunluk.ToString("0.###", CultureInfo.CurrentCulture);
            btnMalzemeSil.IsEnabled = m.Ozel;

            ParametreDegisti(null, null);
        }

        private void btnMalzemeEkle_Click(object sender, RoutedEventArgs e)
        {
            var pencere = new MalzemeWindow { Owner = this };
            if (pencere.ShowDialog() != true) return;

            MalzemeDeposu.Ekle(pencere.MalzemeAd, pencere.MalzemeYogunluk);
            MalzemeleriDoldur(pencere.MalzemeAd);

            LogSuccess("Özel Malzeme Eklendi: " + pencere.MalzemeAd + " (" +
                       pencere.MalzemeYogunluk.ToString("0.###", CultureInfo.CurrentCulture) +
                       " g/cm³)");
        }

        private void btnMalzemeSil_Click(object sender, RoutedEventArgs e)
        {
            var m = cmbMalzeme.SelectedItem as Malzeme;
            if (m == null || !m.Ozel) return;

            MalzemeDeposu.Sil(m.Ad);
            MalzemeleriDoldur("");

            LogInfo("Özel Malzeme Silindi: " + m.Ad);
        }

        // Herhangi bir parametre degisince tablo aninda yeniden hesaplanir
        private void ParametreDegisti(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            var secili = cmbMalzeme.SelectedItem as Malzeme;
            if (secili != null) Ayarlar.MalzemeAdi = secili.Ad;

            Ayarlar.Yogunluk = Ondalik(txtYogunluk.Text, Ayarlar.Yogunluk);
            Ayarlar.KgFiyat = Ondalik(txtKgFiyat.Text, 0);
            Ayarlar.KesimFiyat = Ondalik(txtKesimFiyat.Text, 0);

            var pbOge = cmbParaBirimi.SelectedItem as ComboBoxItem;
            if (pbOge != null) Ayarlar.ParaBirimi = (string)pbOge.Tag;

            Ayarlar.Kaydet();

            BasliklariYaz();
            YenidenHesapla();
        }

        private void YenidenHesapla()
        {
            var parametreler = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (ParametreTanimi p in TabloDeposu.Parametreler)
                parametreler[p.Anahtar] = p.Deger;

            foreach (CostRow r in _costRows)
            {
                r.Hesapla(Ayarlar.Yogunluk, Ayarlar.KgFiyat, Ayarlar.KesimFiyat);
                r.OzelHesapla(TabloDeposu.Sutunlar, parametreler,
                              Ayarlar.Yogunluk, Ayarlar.KgFiyat, Ayarlar.KesimFiyat);
            }

            // Isi haritasi olcekleri sutunlarin yeni degerlerine gore cikarilir
            IsiHaritasi.Olc(_costRows, TabloDeposu.Sutunlar);

            // Ozel sutunlar sozluk uzerinden baglandigi icin tazelenmeli
            costGrid.Items.Refresh();

            OzetiYaz();
        }

        private void OzetiYaz()
        {
            double agirlik = 0, kesim = 0, maliyet = 0;
            int olculen = 0;

            foreach (CostRow r in _costRows)
            {
                if (r.ToplamAgirlik.HasValue) { agirlik += r.ToplamAgirlik.Value; olculen++; }
                if (r.ToplamKesim.HasValue) kesim += r.ToplamKesim.Value;
                if (r.ToplamMaliyet.HasValue) maliyet += r.ToplamMaliyet.Value;
            }

            txtOzetAgirlik.Text = agirlik.ToString("N2", CultureInfo.CurrentCulture) + " kg";
            txtOzetKesim.Text = kesim.ToString("N1", CultureInfo.CurrentCulture) + " m";
            txtOzetMaliyet.Text = maliyet.ToString("N2", CultureInfo.CurrentCulture) +
                                  " " + Ayarlar.ParaBirimi;

            txtOzetSatir.Text = olculen + " / " + _costRows.Count;
        }

        private static double Ondalik(string s, double varsayilan)
        {
            double d;
            if (double.TryParse((s ?? "").Trim(), NumberStyles.Float,
                                CultureInfo.CurrentCulture, out d)) return d;

            if (double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out d)) return d;

            return varsayilan;
        }

        private void SadeceOndalik(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if ((c < '0' || c > '9') && c != ',' && c != '.') { e.Handled = true; return; }
        }

        // ================= TARAMA + OLCUM =================

        private async void btnCostScan_Click(object sender, RoutedEventArgs e)
        {
            if (_maliyetCalisiyor) return;

            _maliyetCalisiyor = true;
            _stopRequested = false;
            btnCostScan.IsEnabled = false;

            try
            {
                _costRows.Clear();
                _costRepRefs.Clear();
                OzetiYaz();

                LogInfo("Ağırlık ve Maliyet Taraması Başlatıldı.");

                ScanOutput result = await Task.Run(() => DoScan());
                foreach (var d in result.Diag) WriteDiag(d);

                if (result.Error != null) { LogError(result.Error); return; }

                _catia = GetCatia() ?? result.Catia;

                foreach (SheetRow row in result.Rows)
                    _costRows.Add(new CostRow
                    {
                        ProductName = row.ProductName,
                        PartName = row.PartName,
                        Thickness = row.Thickness,
                        Quantity = row.Quantity
                    });

                foreach (var kv in result.RepRefs) _costRepRefs[kv.Key] = kv.Value;

                LogSuccess("Tarama Tamamlandı — Sac Parça Çeşidi: " + _costRows.Count +
                           ", Toplam Adet: " + result.Total);

                OzetiYaz();
                await OlcumleriTopla();
            }
            catch (Exception ex)
            {
                LogError("Hata: " + ex.Message);
            }
            finally
            {
                _maliyetCalisiyor = false;
                btnCostScan.IsEnabled = true;
                HidePip();
            }
        }

        // Her parcayi sirayla acar, olcer, kapatir
        private async Task OlcumleriTopla()
        {
            if (_costRows.Count == 0) return;

            LogInfo("Ölçüm Başlıyor — Her Parça Sırayla Açılıp Kapatılacak.");
            ShowPipStart("Ölçüm", "Hesaplama");

            int sira = 0, basarili = 0;

            foreach (CostRow row in new List<CostRow>(_costRows))
            {
                if (_stopRequested)
                {
                    LogError("Ölçüm Kullanıcı Tarafından Durduruldu.");
                    break;
                }

                sira++;
                ShowPip(sira + "/" + _costRows.Count + " · " + row.PartName);

                object repRef;
                if (!_costRepRefs.TryGetValue(row.PartName, out repRef) || repRef == null)
                {
                    row.OlcumTemizle("Parça Referansı Yok");
                    LogError("Ölçülemedi (Referans Yok): " + row.PartName);
                    continue;
                }

                bool ok = await ParcayiOlc(repRef, row);
                if (ok) basarili++;
            }

            YenidenHesapla();

            if (basarili == _costRows.Count)
                LogSuccess("Ölçüm Tamamlandı — " + basarili + " Parça.");
            else
                LogError("Ölçüm Bitti — Başarılı: " + basarili + " / " + _costRows.Count);

            await FinishPip(
                basarili == _costRows.Count
                    ? ExportPipWindow.PipState.Done
                    : ExportPipWindow.PipState.Error,
                basarili + " / " + _costRows.Count + " Parça");
        }

        // Parcayi yeni pencerede acar, hacim ve alani okur, pencereyi kapatir
        private async Task<bool> ParcayiOlc(object repRef, CostRow row)
        {
            dynamic catia = _catia;
            if (catia == null)
            {
                row.OlcumTemizle("CATIA Bağlantısı Yok");
                return false;
            }

            try
            {
                double hacim, alan;
                string yontem;

                // 1) Parcayi acmadan, referans uzerinden dene. Tutarsa her
                //    parca icin 5-6 saniye kazanilir.
                bool okundu = Olcumler(catia, repRef, out hacim, out alan, out yontem);

                if (!okundu)
                {
                    dynamic svc = catia.ActiveEditor.GetService("PLMOpenService");
                    object newEd = null;
                    svc.PLMOpenInNewWindow(repRef, ref newEd);

                    await Task.Delay(2500);

                    // 2) Parca acikken olcum ACIK PARCANIN KENDISINE yapilir;
                    //    rep referansi burada is gormuyor (COM sorgusuyla goruldu)
                    object acikParca = null;
                    try { acikParca = catia.ActiveEditor.ActiveObject; } catch { }

                    okundu = acikParca != null &&
                             Olcumler(catia, acikParca, out hacim, out alan, out yontem);

                    // 3) Son care: ana govde
                    if (!okundu && acikParca != null)
                    {
                        object govde = null;
                        try { govde = ((dynamic)acikParca).MainBody; } catch { }

                        if (govde != null)
                        {
                            string yontem2;
                            okundu = Olcumler(catia, govde, out hacim, out alan, out yontem2);
                            if (okundu) yontem = yontem2 + " (MainBody)";
                        }
                    }

                    if (!okundu && !_olcumDokuldu)
                    {
                        _olcumDokuldu = true;
                        OlcumTeshisi(catia, repRef);
                    }

                    try { catia.ActiveWindow.Close(); } catch { }
                    await WaitForAssembly(15000);
                    await Task.Delay(800);
                }

                if (okundu)
                {
                    BirimeCevir(row.Thickness, ref hacim, ref alan, yontem);
                    row.OlcumYaz(hacim, alan);

                    LogInfo("Ölçüldü: " + row.PartName +
                            "  ·  Hacim " + hacim.ToString("G4", CultureInfo.CurrentCulture) +
                            " m³  ·  Alan " + alan.ToString("G4", CultureInfo.CurrentCulture) +
                            " m²  (" + yontem + ")");
                }
                else
                {
                    row.OlcumTemizle("Ölçülemedi");

                    // Uzun deneme dokumu bir kez yeter; sonrakiler kisa yazilir
                    LogError("Ölçülemedi: " + row.PartName +
                             (_ilkOlcumHatasiYazildi ? "" : " — " + yontem));

                    _ilkOlcumHatasiYazildi = true;
                }

                return okundu;
            }
            catch (Exception ex)
            {
                row.OlcumTemizle("Hata");
                LogError("Ölçüm Hatası (" + row.PartName + "): " + ex.Message);

                try { catia.ActiveWindow.Close(); } catch { }
                await WaitForAssembly(15000);
                return false;
            }
        }

        private bool _olcumDokuldu;
        private bool _ilkOlcumHatasiYazildi;

        // Parcanin hacmini ve toplam yuzey alanini okur.
        //
        // Yol, kullanicinin makinesinde COM sorgusuyla dogrulandi:
        // 3DEXPERIENCE'ta V5'teki SPAWorkbench/Analyze yok; olcum
        // ActiveEditor.GetService("InertiaService").GetInertiaElement(nesne)
        // ile yapiliyor ve GetVolume()/GetArea() METOT olarak cagriliyor.
        // Yedek olarak ayni sekilde dogrulanan MeasureService var.
        private static bool Olcumler(dynamic catia, object hedef,
                                     out double hacim, out double alan, out string yontem)
        {
            hacim = 0; alan = 0; yontem = "";
            var hatalar = new List<string>();

            dynamic editor;
            try { editor = catia.ActiveEditor; }
            catch (Exception ex)
            {
                yontem = "ActiveEditor: " + Kisa(ex.Message);
                return false;
            }

            // 1) InertiaService — hacim ve alani birlikte verir
            try
            {
                dynamic servis = editor.GetService("InertiaService");
                object inertia = servis.GetInertiaElement(hedef);

                string neden;
                if (SayiOku(inertia, "GetVolume", out hacim, out neden))
                {
                    SayiOku(inertia, "GetArea", out alan, out neden);

                    if (hacim > 0) { yontem = "InertiaService"; return true; }
                    hatalar.Add("InertiaService: hacim sıfır");
                }
                else hatalar.Add("InertiaService.GetVolume: " + neden);
            }
            catch (Exception ex) { hatalar.Add("InertiaService: " + Kisa(ex.Message)); }

            // 2) MeasureService (yedek)
            try
            {
                dynamic servis = editor.GetService("MeasureService");
                object olcum = servis.GetMeasureItem(hedef);

                string neden;
                if (SayiOku(olcum, "GetVolume", out hacim, out neden))
                {
                    SayiOku(olcum, "GetArea", out alan, out neden);

                    if (hacim > 0) { yontem = "MeasureService"; return true; }
                    hatalar.Add("MeasureService: hacim sıfır");
                }
                else hatalar.Add("MeasureService.GetVolume: " + neden);
            }
            catch (Exception ex) { hatalar.Add("MeasureService: " + Kisa(ex.Message)); }

            yontem = string.Join(" | ", hatalar);
            return false;
        }

        // COM metotlarindan sayi okur. CAA otomasyonunda bu metotlarin bir
        // kismi degeri dondurur, bir kismi out parametresiyle verir; ikisi
        // de denenir. Basarisizsa gercek COM hatasi 'neden'e yazilir.
        private static bool SayiOku(object nesne, string metot, out double deger, out string neden)
        {
            deger = 0;
            neden = "";
            if (nesne == null) { neden = "nesne yok"; return false; }

            Type tip = nesne.GetType();

            // 1) dogrudan donus degeri
            try
            {
                object r = tip.InvokeMember(metot,
                    System.Reflection.BindingFlags.InvokeMethod, null, nesne, null);

                if (r != null)
                {
                    deger = Convert.ToDouble(r, CultureInfo.InvariantCulture);
                    return true;
                }

                neden = "boş döndü";
            }
            catch (Exception ex)
            {
                neden = Kisa(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }

            // 2) out parametresi
            try
            {
                object[] args = { 0.0 };
                var mod = new System.Reflection.ParameterModifier(1);
                mod[0] = true;

                tip.InvokeMember(metot,
                    System.Reflection.BindingFlags.InvokeMethod, null, nesne, args,
                    new[] { mod }, null, null);

                deger = Convert.ToDouble(args[0], CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                if (neden.Length == 0)
                    neden = Kisa(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                return false;
            }
        }

        // Hicbir yol tutmazsa: eldeki nesnelerin gercek uye listelerini dok.
        // Hangi ozelligin hacmi tasidigi burada gorunur.
        private void OlcumTeshisi(dynamic catia, object repRef)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MACRIA — OLCUM TESHISI  " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            sb.AppendLine();

            NesneyiDok(sb, "CATIA", (object)catia);
            NesneyiDok(sb, "ActiveEditor", Guvenli(() => catia.ActiveEditor));
            NesneyiDok(sb, "ActiveEditor.ActiveObject", Guvenli(() => catia.ActiveEditor.ActiveObject));
            NesneyiDok(sb, "ActiveDocument", Guvenli(() => catia.ActiveDocument));
            NesneyiDok(sb, "ActiveWindow", Guvenli(() => catia.ActiveWindow));

            object part = Guvenli(() => ((dynamic)repRef).GetItem("Part"));
            NesneyiDok(sb, "RepRef.GetItem(Part)", part);

            if (part != null)
            {
                NesneyiDok(sb, "Part.Parent", Guvenli(() => ((dynamic)part).Parent));
                NesneyiDok(sb, "Part.MainBody", Guvenli(() => ((dynamic)part).MainBody));
            }

            try
            {
                string yol = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "macria_olcum_teshis_" + DateTime.Now.ToString("HHmmss") + ".txt");

                System.IO.File.WriteAllText(yol, sb.ToString());
                LogInfo("Ölçüm Teşhis Dökümü Yazıldı: " + yol);
            }
            catch (Exception ex)
            {
                LogError("Teşhis Dökümü Yazılamadı: " + ex.Message);
            }
        }

        private static object Guvenli(Func<object> al)
        {
            try { return al(); }
            catch { return null; }
        }

        private void NesneyiDok(StringBuilder sb, string etiket, object nesne)
        {
            if (nesne == null)
            {
                sb.AppendLine("==== " + etiket + ": (alınamadı)");
                sb.AppendLine();
                LogInfo("Teşhis · " + etiket + ": (alınamadı)");
                return;
            }

            string tip = ComProbe.TipAdi(nesne);
            List<string> uyeler = ComProbe.UyeAdlari(nesne);

            sb.AppendLine("==== " + etiket + "  [" + tip + "]  (" + uyeler.Count + " üye)");
            foreach (string u in uyeler) sb.AppendLine("    " + u);
            sb.AppendLine();

            // Konsola sadece olcumle ilgili olabilecek uyeler
            var ilginc = new List<string>();
            foreach (string u in uyeler)
            {
                string f = u.ToLowerInvariant();
                if (f.Contains("volume") || f.Contains("area") || f.Contains("mass") ||
                    f.Contains("inertia") || f.Contains("analy") || f.Contains("measur") ||
                    f.Contains("workbench") || f.Contains("densit") || f.Contains("weight"))
                    ilginc.Add(u);
            }

            LogInfo("Teşhis · " + etiket + " [" + tip + "] " + uyeler.Count + " üye" +
                    (ilginc.Count > 0 ? " — İlgili: " + string.Join(", ", ilginc) : ""));
        }

        private static string Kisa(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 70 ? s.Substring(0, 70) + "..." : s;
        }

        // CATIA COM'u SI dondurur ama surumden surume degisebiliyor.
        // Duz sac icin ToplamAlan / (Hacim / kalinlik) orani her zaman 2'ye
        // yakindir; oran tutmuyorsa degerler mm cinsinden gelmis demektir.
        private void BirimeCevir(double kalinlikMm, ref double hacim, ref double alan,
                                 string yontem)
        {
            if (kalinlikMm <= 0 || hacim <= 0 || alan <= 0) return;

            double kalinlikM = kalinlikMm / 1000.0;

            double oranSi = alan / (hacim / kalinlikM);
            if (oranSi > 1.2 && oranSi < 8.0) return;   // zaten SI

            // mm3 / mm2 kabul edip SI'ya cevir
            double hacimSi = hacim / 1e9;
            double alanSi = alan / 1e6;
            double oranMm = alanSi / (hacimSi / kalinlikM);

            if (oranMm > 1.2 && oranMm < 8.0)
            {
                LogInfo("Ölçüm Birimi mm Olarak Algılandı, m'ye Çevrildi (" + yontem + ").");
                hacim = hacimSi;
                alan = alanSi;
                return;
            }

            LogError("Ölçüm Oranı Beklenenin Dışında (Alan/DüzAlan = " +
                     oranSi.ToString("0.##", CultureInfo.CurrentCulture) +
                     ") — Kesim Boyu Güvenilir Olmayabilir.");
        }

        // ================= DISARI AKTARMA =================

        private void btnCostExport_Click(object sender, RoutedEventArgs e)
        {
            if (_costRows.Count == 0)
            {
                LogInfo("Dışarı Aktarılacak Hesaplama Yok — Önce CATIA'yı Tarayın.");
                return;
            }

            ContextMenu menu = btnCostExport.ContextMenu;
            if (menu == null) return;

            menu.PlacementTarget = btnCostExport;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void mnuExcelAktar_Click(object sender, RoutedEventArgs e)
        {
            string yol = DosyaSor("xlsx", "Excel Çalışma Kitabı (*.xlsx)|*.xlsx");
            if (yol == null) return;

            try
            {
                ExcelYazici.Yaz(RaporHazirla(), yol);
                LogSuccess("Excel Dosyası Oluşturuldu: " + yol);
            }
            catch (Exception ex)
            {
                LogError("Excel Dosyası Yazılamadı: " + ex.Message);
            }
        }

        private void mnuPdfAktar_Click(object sender, RoutedEventArgs e)
        {
            string yol = DosyaSor("pdf", "PDF Belgesi (*.pdf)|*.pdf");
            if (yol == null) return;

            try
            {
                PdfYazici.Yaz(RaporHazirla(), yol);
                LogSuccess("PDF Belgesi Oluşturuldu: " + yol);
            }
            catch (Exception ex)
            {
                LogError("PDF Belgesi Yazılamadı: " + ex.Message);
            }
        }

        private static string DosyaSor(string uzanti, string filtre)
        {
            var kutu = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Raporu Kaydet",
                Filter = filtre,
                DefaultExt = uzanti,
                AddExtension = true,
                FileName = "Macria_Maliyet_" +
                           DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) +
                           "." + uzanti
            };

            return kutu.ShowDialog() == true ? kutu.FileName : null;
        }

        // Tablodaki hesaplanmis degerleri bicimden bagimsiz rapora dokur
        private Rapor RaporHazirla()
        {
            string pb = Ayarlar.ParaBirimi;

            int olculen = 0;
            foreach (CostRow r in _costRows)
                if (r.OlculduMu) olculen++;

            var rapor = new Rapor
            {
                Baslik = "Ağırlık ve Maliyet Raporu",
                AltBaslik = olculen + " / " + _costRows.Count + " Parça Ölçüldü",
                Tarih = DateTime.Now
            };

            rapor.Bilgiler.Add(
                "Malzeme: " + Ayarlar.MalzemeAdi +
                "   ·   Yoğunluk: " + Ayarlar.Yogunluk.ToString("0.###", CultureInfo.CurrentCulture) +
                " g/cm³");

            rapor.Bilgiler.Add(
                "Malzeme Fiyatı: " + Ayarlar.KgFiyat.ToString("N2", CultureInfo.CurrentCulture) +
                " " + pb + "/kg   ·   Kesim Fiyatı: " +
                Ayarlar.KesimFiyat.ToString("N2", CultureInfo.CurrentCulture) + " " + pb + "/m");

            rapor.Bilgiler.Add("Kur: " + KurServisi.OzetMetni() +
                               "   (" + KurServisi.KaynakMetni() + ")");

            // Rapor sutunlari da Tablo Ayarlari'ndaki gorunur sutunlardir
            List<SutunTanimi> gorunur = TabloDeposu.Sutunlar.FindAll(s => s.Gorunur);

            foreach (SutunTanimi s in gorunur)
                rapor.Sutunlar.Add(new RaporSutun
                {
                    Ad = s.GorunenBaslik(pb),
                    Genislik = s.Genislik > 0
                        ? Math.Max(0.55, s.Genislik / 95.0)
                        : (s.Anahtar == "parca" ? 2.0 : 1.5),
                    Sayi = !s.Metin,
                    Ondalik = s.Ondalik,
                    Durum = s.Anahtar == "durum"
                });

            var toplamlar = new double[gorunur.Count];
            var toplanan = new bool[gorunur.Count];

            for (int i = 0; i < gorunur.Count; i++)
                toplanan[i] = Toplanir(gorunur[i]);

            double agirlik = 0, kesim = 0, toplam = 0;

            foreach (CostRow r in _costRows)
            {
                var satir = new object[gorunur.Count];

                for (int i = 0; i < gorunur.Count; i++)
                {
                    object deger = r.Deger(gorunur[i].Anahtar);
                    satir[i] = deger;

                    if (toplanan[i] && deger is double)
                        toplamlar[i] += (double)deger;
                }

                rapor.Satirlar.Add(satir);

                if (r.ToplamAgirlik.HasValue) agirlik += r.ToplamAgirlik.Value;
                if (r.ToplamKesim.HasValue) kesim += r.ToplamKesim.Value;
                if (r.ToplamMaliyet.HasValue) toplam += r.ToplamMaliyet.Value;
            }

            var toplamSatiri = new object[gorunur.Count];
            for (int i = 0; i < gorunur.Count; i++)
                toplamSatiri[i] = toplanan[i] ? (object)toplamlar[i] : null;

            // "TOPLAM" etiketi ilk yazi sutununa yazilir ki bir sayiyi ezmesin
            int etiketYeri = gorunur.FindIndex(s => s.Metin);
            toplamSatiri[etiketYeri >= 0 ? etiketYeri : 0] = "TOPLAM";

            rapor.Toplam = toplamSatiri;

            // Sayfanin ustundeki vurgulu kutular
            rapor.Ozetler.Add(new RaporOzet
            {
                Baslik = "Toplam Ağırlık",
                Deger = agirlik.ToString("N2", CultureInfo.CurrentCulture) + " kg"
            });
            rapor.Ozetler.Add(new RaporOzet
            {
                Baslik = "Toplam Kesim",
                Deger = kesim.ToString("N1", CultureInfo.CurrentCulture) + " m"
            });
            rapor.Ozetler.Add(new RaporOzet
            {
                Baslik = "Toplam Maliyet",
                Deger = toplam.ToString("N2", CultureInfo.CurrentCulture) + " " + pb
            });

            return rapor;
        }

        // ================= TABLOYU TEMIZLE =================

        private void btnCostClear_Click(object sender, RoutedEventArgs e)
        {
            if (_costRows.Count == 0) return;

            if (!OnayWindow.Sor(this, "Tabloyu Temizle",
                    "Tablodaki " + _costRows.Count + " parça ve yapılan ölçümler silinecek. " +
                    "Yeniden hesaplamak için CATIA'yı tekrar taramanız gerekir.",
                    "Temizle"))
                return;

            _costRows.Clear();
            _costRepRefs.Clear();
            OzetiYaz();
            LogInfo("Maliyet Tablosu Temizlendi.");
        }
    }
}
