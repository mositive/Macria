using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Macria
{
    public class SheetRow : INotifyPropertyChanged
    {
        private double _thickness;
        private string _hamSacKalinligiMetni = "";

        public string ProductName { get; set; } = "";
        public string PartName { get; set; } = "";

        public double Thickness
        {
            get { return _thickness; }
            set
            {
                if (Math.Abs(_thickness - value) < 0.0001) return;
                _thickness = value;
                OnPropertyChanged(nameof(Thickness));
                OnPropertyChanged(nameof(HamSacFarkliMi));
            }
        }

        public string HamSacKalinligiMetni
        {
            get { return _hamSacKalinligiMetni; }
            set
            {
                string newValue = value ?? "";
                if (_hamSacKalinligiMetni == newValue) return;
                _hamSacKalinligiMetni = newValue;
                OnPropertyChanged(nameof(HamSacKalinligiMetni));
                OnPropertyChanged(nameof(HamSacFarkliMi));
            }
        }

        public bool HamSacFarkliMi
        {
            get
            {
                double hamSac;
                return HamSacKalinliklari.TryParse(
                           HamSacKalinligiMetni, out hamSac) &&
                       Math.Abs(hamSac - Thickness) >= 0.0001;
            }
        }

        // DXF adinda ve yerlesimde kullanilan kalinlik: kullanici ham sac
        // girdiyse o, yoksa modelden okunan kalinlik. Tek karar noktasi
        // burasi olsun ki onizleme ve yerlesim de ayni dosyayi bulsun.
        public double HamSacKalinligi
        {
            get
            {
                double hamSac;
                return HamSacKalinliklari.TryParse(HamSacKalinligiMetni, out hamSac)
                    ? hamSac
                    : Thickness;
            }
        }

        public double UygulananHamSacKalinligi { get; set; }
        public int Quantity { get; set; }

        // Bu parcadan uretilen DXF'in yolu; onizleme buradan okur
        public string DxfYolu { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LogEntry
    {
        public string Text { get; set; } = "";
        public Brush Color { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<SheetRow> _rows = new ObservableCollection<SheetRow>();
        private readonly Dictionary<string, object> _repRefs = new Dictionary<string, object>();
        private object _catia;
        private System.ComponentModel.ICollectionView _view;
        private string _searchText = "";
        private readonly ObservableCollection<LogEntry> _logs = new ObservableCollection<LogEntry>();
        private ExportPipWindow _pip;
        private ConsoleWindow _logWindow;
        private bool _stopRequested;

        public MainWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            // Bekleme suresi, ogretilmis Save As konumu ve son kurlar
            // makineye ozel; her acilista kullanicinin profilinden okunur
            Ayarlar.Yukle();

            // Pencere kapanirken suren islem durdurulsun
            Closing += (s, e) => { _stopRequested = true; };

            _view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterRow;
            grid.ItemsSource = _view;

            logList.ItemsSource = _logs;
            txtMenuSurum.Text = "v" + AboutWindow.SurumMetni();
            MaliyetKur();
            KonsoluUygula();
            OnizlemeyiUygula();

            LogInfo("Macria Hazır — Sheet Metal filtresi TR/EN / Teşhis açık.");

            if (Ayarlar.KonumVar)
                LogInfo(SaveAsBulucu.VarMi()
                    ? "Save As Düğmesi Görüntüden Tanınacak."
                    : "Save As Konumu Öğretilmiş — Doğrudan Tıklanacak.");
        }

        // ================= KONSOL =================

        private void LogInfo(string message) { AddLog(message, "LogInfoBrush"); }
        private void LogSuccess(string message) { AddLog(message, "LogSuccessBrush"); }
        private void LogError(string message) { AddLog(message, "LogErrorBrush"); }

        private void AddLog(string message, string brushKey)
        {
            var entry = new LogEntry
            {
                Text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message,
                Color = (Brush)FindResource(brushKey)
            };
            _logs.Add(entry);
            logScroll.ScrollToEnd();

            // Pip acikken son konsol satirini orada da goster
            if (_pip != null)
                _pip.SetLastLog(entry.Text, entry.Color);

            // Konsol kapaliyken satir kosede kisa sureli bir kart olarak cikar
            Bildir(message, brushKey);
        }

        // ================= ARAMA =================

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = (txtSearch.Text ?? "").Trim();
            _view.Refresh();
        }

        private bool FilterRow(object item)
        {
            if (_searchText.Length == 0) return true;

            SheetRow row = item as SheetRow;
            if (row == null) return false;

            return ContainsText(row.ProductName, _searchText) ||
                   ContainsText(row.PartName, _searchText);
        }

        private static bool ContainsText(string source, string query)
        {
            return source != null &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        // ================= PENCERE KONTROLLERI =================

        // ================= KONSOL ARACLARI =================

        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logs.Clear();
        }

        // Ayni koleksiyonu paylasan genis konsol; ikinci kez acilmaz, one getirilir
        private void btnPopOutLog_Click(object sender, RoutedEventArgs e)
        {
            if (_logWindow != null)
            {
                if (_logWindow.WindowState == WindowState.Minimized)
                    _logWindow.WindowState = WindowState.Normal;

                _logWindow.Activate();
                return;
            }

            _logWindow = new ConsoleWindow(_logs) { Owner = this };
            _logWindow.Closed += (s, ev) => _logWindow = null;
            _logWindow.Show();
        }

        // ================= GEZINME =================
        //
        // Uygulama tek pencerede iki gorunum tutuyor: ana menu ve secilen
        // islem sayfasi. Sayfalar arasi gecis sadece gorunurluk degisimi;
        // liste, konsol ve suren islem yerinde kaliyor.

        // Gelen sayfa bu kadar piksel oteden kayarak girer
        private const double GecisKaymasi = 28;

        private static readonly Duration GirisSuresi =
            new Duration(TimeSpan.FromMilliseconds(220));
        private static readonly Duration CikisSuresi =
            new Duration(TimeSpan.FromMilliseconds(130));

        private void AnaMenuyeDon()
        {
            SayfayiKapat(exportView);
            SayfayiKapat(costView);
            SayfayiAc(menuView, menuKaydir, -GecisKaymasi);

            btnBack.Visibility = Visibility.Collapsed;
            btnSettings.Visibility = Visibility.Collapsed;
            btnTutorial.Visibility = Visibility.Collapsed;
            txtTitleBar.Text = "Macria";
        }

        private void SayfaAc(UIElement sayfa, TranslateTransform kaydir,
                             string baslik, bool ayarlarVar)
        {
            SayfayiKapat(menuView);
            SayfayiAc(sayfa, kaydir, GecisKaymasi);

            btnBack.Visibility = Visibility.Visible;
            btnSettings.Visibility = ayarlarVar ? Visibility.Visible : Visibility.Collapsed;
            btnTutorial.Visibility = btnSettings.Visibility;
            txtTitleBar.Text = "Macria — " + baslik;

            logScroll.ScrollToEnd();
        }

        // Gelen gorunum: yandan kayarak ve belirerek girer
        private static void SayfayiAc(UIElement gorunum, TranslateTransform kaydir,
                                      double baslangicX)
        {
            gorunum.Visibility = Visibility.Visible;

            gorunum.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, GirisSuresi));

            kaydir.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(baslangicX, 0, GirisSuresi)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        // Giden gorunum: yerinde solar, bitince gizlenir
        private static void SayfayiKapat(UIElement gorunum)
        {
            if (gorunum.Visibility != Visibility.Visible) return;

            var solma = new DoubleAnimation(1, 0, CikisSuresi);

            // Hizli gidip gelmede eski solma yeni acilan sayfayi gizlemesin
            solma.Completed += (s, e) =>
            {
                if (gorunum.Opacity < 0.01) gorunum.Visibility = Visibility.Collapsed;
            };

            gorunum.BeginAnimation(OpacityProperty, solma);
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            AnaMenuyeDon();
        }

        // ================= KONSOL =================

        // Konsol artik sayfaya degil basliktaki dugmeye bagli; her ekranda
        // ayni kaydi gosterir ve secim kullanicinin profilinde saklanir.
        private void btnConsole_Click(object sender, RoutedEventArgs e)
        {
            Ayarlar.KonsolAcik = consoleHost.Visibility != Visibility.Visible;
            Ayarlar.Kaydet();

            KonsoluUygula();
        }

        private void KonsoluUygula()
        {
            bool acik = Ayarlar.KonsolAcik;

            consoleHost.Visibility = acik ? Visibility.Visible : Visibility.Collapsed;
            konsolBosluk.Visibility = acik ? Visibility.Collapsed : Visibility.Visible;

            // Acikken dugme hafif dolu gorunur (fare vurgusu sablonda kaliyor)
            btnConsole.Background = acik
                ? (System.Windows.Media.Brush)FindResource("SurfaceBrush")
                : System.Windows.Media.Brushes.Transparent;

            // Konsol acikken kartlara gerek yok, ayni satirlar orada duruyor
            if (acik)
            {
                TumBildirimleriKapat();
                logScroll.ScrollToEnd();
            }
        }

        private void tileExport_Click(object sender, RoutedEventArgs e)
        {
            SayfaAc(exportView, exportKaydir, "Toplu DXF Export", true);

            // Ilk giriste rehber kendiliginden acilir
            if (!Ayarlar.RehberGosterildi)
            {
                Ayarlar.RehberGosterildi = true;
                Ayarlar.Kaydet();
                RehberiAc();
            }
        }

        // Rehberi gosterir; kullanici son adimda isterse ogretme moduna gecer
        private void RehberiAc()
        {
            var rehber = new TutorialWindow { Owner = this };
            rehber.ShowDialog();

            if (rehber.OgretmeIstendi)
            {
                var ayarlar = new SettingsWindow(ogretmeyeBasla: true) { Owner = this };
                ayarlar.ShowDialog();

                if (Ayarlar.KonumVar)
                    LogSuccess("Save As Konumu Öğretildi — Export Kullanıma Hazır.");
            }
        }

        private void btnTutorial_Click(object sender, RoutedEventArgs e)
        {
            RehberiAc();
        }

        // Export ancak konum ogretildiyse calisabilir
        private bool ExportIzinliMi()
        {
            if (!Ayarlar.KonumVar)
            {
                LogError("Export İçin Önce Save As Konumu Öğretilmeli — Rehber Açılıyor.");
                RehberiAc();

                return Ayarlar.KonumVar;
            }

            if (!SaveAsBulucu.VarMi()) return GorselYokUyarisi();

            return true;
        }

        // Konum, gorsel tanima eklenmeden onceki bir surumde ogretilmisse
        // yalnizca koordinat kayitlidir; dugme goruntusu yoktur. Bu durumda
        // panel en ufak kaydiginda tiklama sasar ve export bosuna calisir.
        // Sessizce devam etmek yerine sorulur.
        private bool GorselYokUyarisi()
        {
            LogError("Düğme Görüntüsü Öğretilmemiş — Yalnızca Sabit Koordinata Tıklanır.");

            bool ogret = OnayWindow.Sor(this,
                "Düğme Görüntüsü Eksik",
                "Save As konumunu daha eski bir Macria sürümünde öğretmişsiniz: " +
                "kayıtlı olan yalnızca bir koordinat, düğmenin görüntüsü yok.\n\n" +
                "Bu haliyle Macria hep aynı noktaya tıklar. CATIA penceresi " +
                "taşındıysa ya da paneli farklı bir yerde açtıysa tıklama boşa " +
                "gider ve export başarısız olur.\n\n" +
                "Konumu bir kez yeniden öğretirseniz düğmenin görüntüsü de " +
                "kaydedilir; bundan sonra panel nereye giderse gitsin bulunur.",
                "Şimdi Öğret", "Yine de Devam Et");

            if (!ogret) return true;

            var ayarlar = new SettingsWindow(ogretmeyeBasla: true) { Owner = this };
            ayarlar.ShowDialog();

            if (SaveAsBulucu.VarMi())
            {
                LogSuccess("Düğme Görüntüsü Öğretildi — Panel Kaysa Bile Bulunacak.");
                return true;
            }

            LogInfo("Görüntü Öğretilmedi — Export İptal Edildi.");
            return false;
        }

        // Export oncesi bilgilendirme. Fare devralindigi icin hem tekil hem
        // toplu aktarimdan once cikar. Kullanici "bir daha gosterme" dediyse
        // sessizce gecilir; vazgecerse export baslamaz.
        private bool FareUyarisiniGoster()
        {
            if (Ayarlar.FareUyarisiGizle) return true;

            var uyari = new FareUyariWindow { Owner = this };
            bool basla = uyari.ShowDialog() == true;

            if (basla && uyari.BirDahaGosterme)
            {
                Ayarlar.FareUyarisiGizle = true;
                Ayarlar.Kaydet();
                LogInfo("Export Bilgilendirmesi Bir Daha Gösterilmeyecek (Ayarlar'dan Geri Açılabilir).");
            }

            if (!basla) LogInfo("Export İptal Edildi.");

            return basla;
        }

        private void tileCost_Click(object sender, RoutedEventArgs e)
        {
            SayfaAc(costView, costKaydir, "Ağırlık ve Maliyet", false);
            KurlariTazele();
        }

        private void btnAbout_Click(object sender, RoutedEventArgs e)
        {
            var pencere = new AboutWindow { Owner = this };
            pencere.ShowDialog();
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var pencere = new SettingsWindow { Owner = this };
            if (pencere.ShowDialog() == true)
                LogInfo("Ayarlar Kaydedildi — Bekleme: " + Ayarlar.PanelBekleme + " ms" +
                        (Ayarlar.KonumVar ? ", Save As Konumu Öğretilmiş." : "."));
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

        // ================= EXPORT PIP GOSTERGESI =================

        private int _pipSession;

        // Gorev adi pencere gosterilmeden once verilir; aksi halde pip bir an
        // icin yanlis baslikla (varsayilan metniyle) cizilir
        private ExportPipWindow EnsurePip(string gorevAdi = null)
        {
            if (_pip == null)
            {
                _pip = new ExportPipWindow();
                if (gorevAdi != null) _pip.GorevAdi = gorevAdi;

                _pip.SetState(ExportPipWindow.PipState.Starting, "");
                _pip.StopRequested += OnPipStopRequested;
                _pip.Show();
            }
            else if (gorevAdi != null)
            {
                _pip.GorevAdi = gorevAdi;
            }

            return _pip;
        }

        private void ShowPipStart(string detail, string gorevAdi = "DXF Export")
        {
            _pipSession++;

            EnsurePip(gorevAdi).SetState(ExportPipWindow.PipState.Starting, detail);
        }

        private void ShowPip(string detail)
        {
            EnsurePip().SetState(ExportPipWindow.PipState.Running, detail);
        }

        // Sonuc durumunu bir sure gosterip pip'i kapatir
        private async System.Threading.Tasks.Task FinishPip(ExportPipWindow.PipState state, string detail)
        {
            if (_pip == null) return;

            _pip.SetState(state, detail);

            int session = _pipSession;
            await System.Threading.Tasks.Task.Delay(2500);
            if (_pipSession == session) HidePip();
        }

        private void OnPipStopRequested()
        {
            _stopRequested = true;
            if (_pip != null) _pip.SetDetail("Durduruluyor...");
            LogError("Acil Durdurma İstendi — İşlem Kesiliyor...");
        }

        private void HidePip()
        {
            if (_pip != null)
            {
                _pip.Close();
                _pip = null;
            }
        }

        // Export sonrasi dosyayi/klasoru varsayilan uygulamayla acar
        // ================= DXF ONIZLEME =================
        //
        // Listede secili parcanin acinimi sag panelde cizilir. Boylece "DXF
        // bos mu, sekil beklenen mi" sorusu dosyayi bir CAD'de acmadan
        // cevaplanir. Cizim, disa aktarilmis dosyadan okunur.

        private string _onizlemeSonYol = "";
        private OnizlemeWindow _onizlemeWindow;

        // Ayni cizimi gosteren genis pencere; ikinci kez acilmaz, one getirilir
        private void btnOnizlemePopOut_Click(object sender, RoutedEventArgs e)
        {
            if (_onizlemeWindow != null)
            {
                if (_onizlemeWindow.WindowState == WindowState.Minimized)
                    _onizlemeWindow.WindowState = WindowState.Normal;

                _onizlemeWindow.Activate();
                return;
            }

            _onizlemeWindow = new OnizlemeWindow { Owner = this };
            _onizlemeWindow.Closed += (s, ev) => _onizlemeWindow = null;
            _onizlemeWindow.Show();

            // Acilir acilmaz secili parcayi alsin diye onbellek bosaltilir
            _onizlemeSonYol = "";
            OnizlemeyiYenile();
        }

        private void btnOnizleme_Click(object sender, RoutedEventArgs e)
        {
            Ayarlar.OnizlemeAcik = !Ayarlar.OnizlemeAcik;
            Ayarlar.Kaydet();
            OnizlemeyiUygula();
        }

        private void OnizlemeyiUygula()
        {
            bool acik = Ayarlar.OnizlemeAcik;

            onizlemePanel.Visibility = acik ? Visibility.Visible : Visibility.Collapsed;

            btnOnizleme.Background = acik
                ? (Brush)FindResource("SurfaceBrush")
                : Brushes.Transparent;

            if (acik) OnizlemeyiYenile();
        }

        private void grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OnizlemeyiYenile();
        }

        private void OnizlemeyiYenile()
        {
            if (!IsLoaded) return;

            // Panel kapali olsa da ayri pencere aciksa cizim guncellenir
            if (!Ayarlar.OnizlemeAcik && _onizlemeWindow == null) return;

            var row = grid.SelectedItem as SheetRow;

            if (row == null)
            {
                OnizlemeBosalt("Listeden Bir Parça Seçin",
                               "Önizlemek için listeden bir satır seçin.");
                return;
            }

            txtOnizlemeParca.Text = row.PartName;

            string yol = OnizlemeDosyasi(row);

            if (yol == null)
            {
                OnizlemeBosalt(row.PartName,
                    "Bu parçanın DXF'i bulunamadı.\n" +
                    "Dışa aktardıktan sonra burada görünür; dosyalar başka " +
                    "bir klasördeyse \"Klasör Seç\" ile gösterin.");
                return;
            }

            // Ayni dosya yeniden cozulmesin
            if (yol == _onizlemeSonYol) return;
            _onizlemeSonYol = yol;

            string hata;
            DxfCizim cizim = DxfOkuyucu.Oku(yol, out hata);

            txtOnizlemeDosya.Text = System.IO.Path.GetFileName(yol);
            txtOnizlemeDosya.ToolTip = yol;
            btnOnizlemeAc.IsEnabled = true;

            if (cizim == null || cizim.Bos)
            {
                string sorun = hata ?? "Çizim okunamadı.";

                onizlemeCizim.Visibility = Visibility.Collapsed;
                onizlemeCizim.Data = null;

                txtOnizlemeMesaj.Visibility = Visibility.Visible;
                txtOnizlemeMesaj.Text = sorun;
                txtOnizlemeOlcu.Text = "";

                if (_onizlemeWindow != null)
                    _onizlemeWindow.Bosalt(row.PartName, sorun, yol);

                return;
            }

            // Geometri donduruldugu icin iki gorunum ayni sekli paylasabilir
            Geometry sekil = cizim.Geometri();

            onizlemeCizim.Data = sekil;
            onizlemeCizim.Visibility = Visibility.Visible;
            txtOnizlemeMesaj.Visibility = Visibility.Collapsed;

            string olcu =
                cizim.Genislik.ToString("N1", System.Globalization.CultureInfo.CurrentCulture) +
                " × " +
                cizim.Yukseklik.ToString("N1", System.Globalization.CultureInfo.CurrentCulture) +
                " mm   ·   " + cizim.NesneSayisi + " nesne";

            txtOnizlemeOlcu.Text = olcu;

            if (_onizlemeWindow != null)
                _onizlemeWindow.Goster(row.PartName, sekil, olcu, yol);
        }

        private void OnizlemeBosalt(string parca, string mesaj)
        {
            _onizlemeSonYol = "";

            txtOnizlemeParca.Text = parca;
            onizlemeCizim.Visibility = Visibility.Collapsed;
            onizlemeCizim.Data = null;

            txtOnizlemeMesaj.Visibility = Visibility.Visible;
            txtOnizlemeMesaj.Text = mesaj;

            txtOnizlemeOlcu.Text = "";
            txtOnizlemeDosya.Text = "";
            txtOnizlemeDosya.ToolTip = null;
            btnOnizlemeAc.IsEnabled = false;

            if (_onizlemeWindow != null)
                _onizlemeWindow.Bosalt(parca, mesaj, "");
        }

        // Once export sirasinda kaydedilen yol, yoksa son cikti klasorunde
        // ayni adla duran dosya aranir
        private static string OnizlemeDosyasi(SheetRow row)
        {
            try
            {
                if (!string.IsNullOrEmpty(row.DxfYolu) &&
                    System.IO.File.Exists(row.DxfYolu)) return row.DxfYolu;

                if (string.IsNullOrEmpty(Ayarlar.SonCiktiKlasoru)) return null;

                string aday = System.IO.Path.Combine(Ayarlar.SonCiktiKlasoru,
                                                     MakeFileName(row));

                if (!System.IO.File.Exists(aday)) return null;

                row.DxfYolu = aday;
                return aday;
            }
            catch
            {
                return null;
            }
        }

        private void btnOnizlemeKlasor_Click(object sender, RoutedEventArgs e)
        {
            var fd = new Microsoft.Win32.OpenFolderDialog();
            fd.Title = "DXF Dosyalarının Bulunduğu Klasör";

            if (!string.IsNullOrEmpty(Ayarlar.SonCiktiKlasoru))
                fd.InitialDirectory = Ayarlar.SonCiktiKlasoru;

            if (fd.ShowDialog() != true) return;

            Ayarlar.SonCiktiKlasoru = fd.FolderName;
            Ayarlar.Kaydet();

            // Yeni klasor gecerli olsun diye onceki eslesmeler birakilir
            foreach (SheetRow r in _rows) r.DxfYolu = null;

            _onizlemeSonYol = "";
            OnizlemeyiYenile();

            LogInfo("Önizleme Klasörü: " + fd.FolderName);
        }

        // Basarili bir disa aktarimdan sonra dosya onizlemeye baglanir
        private void OnizlemeyeYaz(SheetRow row, string yol)
        {
            row.DxfYolu = yol;

            try
            {
                string klasor = System.IO.Path.GetDirectoryName(yol);

                if (!string.IsNullOrEmpty(klasor) && klasor != Ayarlar.SonCiktiKlasoru)
                {
                    Ayarlar.SonCiktiKlasoru = klasor;
                    Ayarlar.Kaydet();
                }
            }
            catch { }

            if (ReferenceEquals(grid.SelectedItem, row))
            {
                _onizlemeSonYol = "";
                OnizlemeyiYenile();
            }
        }

        // ================= GORSEL YERLESIM =================

        // Parcalarin gercek olcusu ve konturu sadece DXF'te oldugu icin
        // yerlesim bu sayfadan acilir; kaynagi listedeki satirlar.
        private YerlesimWindow _yerlesimWindow;

        // Konsol gibi modelsiz acilir: kipli olsaydi simge durumuna
        // kucultuldugunde ana pencere kilitli kalir, ekranda tutunacak bir sey
        // kalmazdi. Ikinci kez acilmaz, one getirilir.
        private void btnYerlesim_Click(object sender, RoutedEventArgs e)
        {
            if (_yerlesimWindow != null)
            {
                if (_yerlesimWindow.WindowState == WindowState.Minimized)
                    _yerlesimWindow.WindowState = WindowState.Normal;

                _yerlesimWindow.Activate();
                return;
            }

            if (_rows.Count == 0)
            {
                LogError("Yerleşim İçin Önce CATIA'yı Tarayın.");
                return;
            }

            _yerlesimWindow = new YerlesimWindow(_rows) { Owner = this };
            _yerlesimWindow.Closed += (s, ev) => _yerlesimWindow = null;
            _yerlesimWindow.Show();
        }

        // Ham sac kutusuna tiklaninca satir da secilir; onizleme ve sag tik
        // islemleri kullanicinin duzenledigi parca uzerinde kalir.
        private void HamSacTextBox_GotKeyboardFocus(
            object sender, KeyboardFocusChangedEventArgs e)
        {
            DependencyObject current = sender as DependencyObject;

            while (current != null && !(current is DataGridRow))
            {
                if (current is Visual ||
                    current is System.Windows.Media.Media3D.Visual3D)
                    current = VisualTreeHelper.GetParent(current);
                else if (current is FrameworkContentElement contentElement)
                    current = contentElement.Parent;
                else
                    break;
            }

            DataGridRow dataGridRow = current as DataGridRow;
            if (dataGridRow == null) return;

            dataGridRow.IsSelected = true;
            grid.SelectedItem = dataGridRow.Item;
        }

        private void GridDegisikliginiTamamla()
        {
            try
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }
        }

        private bool HamSacSatiriniDogrula(SheetRow row, out double value)
        {
            value = 0;
            if (row != null &&
                HamSacKalinliklari.TryParse(row.HamSacKalinligiMetni, out value))
            {
                if (value + 0.0001 >= row.Thickness) return true;

                grid.SelectedItem = row;
                grid.ScrollIntoView(row);
                LogError(
                    "Ham Sac kalınlığı model kalınlığından küçük olamaz — " +
                    row.ProductName + ": Model " +
                    HamSacKalinliklari.Goster(row.Thickness) + " mm.");
                return false;
            }

            if (row != null)
            {
                grid.SelectedItem = row;
                grid.ScrollIntoView(row);
                LogError(
                    "Geçersiz Ham Sac kalınlığı — " + row.ProductName +
                    ". 0,05 ile 1000 mm arasında bir değer girin.");
            }

            return false;
        }

        private bool TumHamSacGirdileriniDogrula(
            out Dictionary<SheetRow, double> values)
        {
            values = new Dictionary<SheetRow, double>();
            GridDegisikliginiTamamla();

            foreach (SheetRow row in _rows)
            {
                double value;
                if (!HamSacSatiriniDogrula(row, out value)) return false;
                values[row] = value;
            }

            return true;
        }

        private static bool AyniDosyaYolu(string first, string second)
        {
            try
            {
                return string.Equals(
                    System.IO.Path.GetFullPath(first),
                    System.IO.Path.GetFullPath(second),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Kullanici daha once export almissa once o bagli yol, sonra son cikti
        // klasorundeki eski ham sac ve model kalinligi adlari denenir.
        private static string EskiDxfYolunuBul(SheetRow row)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(row.DxfYolu) &&
                    System.IO.File.Exists(row.DxfYolu))
                    return row.DxfYolu;

                if (string.IsNullOrWhiteSpace(Ayarlar.SonCiktiKlasoru)) return null;

                double eskiHamSac = row.UygulananHamSacKalinligi > 0
                    ? row.UygulananHamSacKalinligi
                    : row.Thickness;

                string eskiHamSacYolu = System.IO.Path.Combine(
                    Ayarlar.SonCiktiKlasoru,
                    MakeFileName(row, eskiHamSac));

                if (System.IO.File.Exists(eskiHamSacYolu)) return eskiHamSacYolu;

                string modelYolu = System.IO.Path.Combine(
                    Ayarlar.SonCiktiKlasoru,
                    MakeFileName(row, row.Thickness));

                return System.IO.File.Exists(modelYolu) ? modelYolu : null;
            }
            catch
            {
                return null;
            }
        }

        private void btnHamSacGuncelle_Click(object sender, RoutedEventArgs e)
        {
            if (_exporting) return;

            if (_rows.Count == 0)
            {
                LogInfo("Önce CATIA Taraması Yapın.");
                return;
            }

            Dictionary<SheetRow, double> values;
            if (!TumHamSacGirdileriniDogrula(out values)) return;

            int renamed = 0;
            int unchanged = 0;
            int renameError = 0;

            foreach (SheetRow row in _rows)
            {
                double value = values[row];
                string source = EskiDxfYolunuBul(row);

                HamSacKalinliklari.Ayarla(
                    row.ProductName,
                    row.PartName,
                    value,
                    row.Thickness);

                row.HamSacKalinligiMetni = HamSacKalinliklari.Goster(value);
                row.UygulananHamSacKalinligi = value;

                if (string.IsNullOrWhiteSpace(source)) continue;

                try
                {
                    string folder = System.IO.Path.GetDirectoryName(source);
                    if (string.IsNullOrWhiteSpace(folder)) continue;

                    string target = System.IO.Path.Combine(folder, MakeFileName(row, value));

                    if (AyniDosyaYolu(source, target))
                    {
                        row.DxfYolu = source;
                        unchanged++;
                    }
                    else if (System.IO.File.Exists(target))
                    {
                        // Var olan dosyanin ustune yazilmaz. Hedef zaten varsa
                        // onizleme ona baglanir, eski dosya guvenlik icin korunur.
                        row.DxfYolu = target;
                        unchanged++;
                        LogInfo(
                            "DXF hedef adı zaten mevcut; eski dosyaya dokunulmadı: " +
                            System.IO.Path.GetFileName(target));
                    }
                    else
                    {
                        System.IO.File.Move(source, target);
                        row.DxfYolu = target;
                        renamed++;
                        LogSuccess(
                            "DXF Adı Güncellendi: " +
                            System.IO.Path.GetFileName(source) + " → " +
                            System.IO.Path.GetFileName(target));
                    }
                }
                catch (Exception ex)
                {
                    renameError++;
                    LogError(
                        "DXF Adı Güncellenemedi — " + row.ProductName + ": " +
                        ex.Message);
                }
            }

            string saveError;
            if (!HamSacKalinliklari.Kaydet(out saveError))
            {
                LogError("Ham Sac değerleri kaydedilemedi: " + saveError);
                return;
            }

            grid.Items.Refresh();
            _onizlemeSonYol = "";
            OnizlemeyiYenile();

            LogSuccess(
                "Ham Sac Değerleri Kaydedildi — Satır: " + _rows.Count +
                ", DXF Adı Değişen: " + renamed +
                (unchanged > 0 ? ", Zaten Güncel: " + unchanged : "") +
                (renameError > 0 ? ", Hata: " + renameError : ""));
        }

        private void btnOnizlemeAc_Click(object sender, RoutedEventArgs e)
        {
            var row = grid.SelectedItem as SheetRow;
            if (row == null) return;

            string yol = OnizlemeDosyasi(row);
            if (yol != null) OpenExported(yol);
        }

        private static void OpenExported(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        // ================= TARAMA =================

        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            _rows.Clear();
            _repRefs.Clear();

            SetScanning(true);
            LogInfo("CATIA Taraması Başlatıldı.");

            try
            {
                ScanOutput result = await System.Threading.Tasks.Task.Run(() => DoScan());

                // Baglanti adimlari ve teshis satirlari konsola dokulur
                foreach (var d in result.Diag) WriteDiag(d);

                if (result.Error != null)
                {
                    LogError(result.Error);
                    return;
                }

                // COM baglantisini UI thread'inden tekrar al (apartman uyumu icin)
                _catia = GetCatia() ?? result.Catia;

                foreach (var row in result.Rows) _rows.Add(row);
                foreach (var kv in result.RepRefs) _repRefs[kv.Key] = kv.Value;

                LogSuccess("Tarama Tamamlandı — Sac Parça Çeşidi: " + _rows.Count +
                           ", Toplam Adet: " + result.Total);
            }
            catch (Exception ex)
            {
                LogError("Hata: " + ex.Message);
            }
            finally
            {
                SetScanning(false);
            }
        }

        private class ScanOutput
        {
            public List<SheetRow> Rows = new List<SheetRow>();
            public Dictionary<string, object> RepRefs = new Dictionary<string, object>();
            public object Catia;
            public int Total;
            public string Error;
            public List<DiagLine> Diag = new List<DiagLine>();
            public bool UrunDokuldu;    // teshis satiri bir kez yazilsin
            public bool ParcaDokuldu;
        }

        private void WriteDiag(DiagLine d)
        {
            if (d.Level == DiagLevel.Error) LogError(d.Text);
            else if (d.Level == DiagLevel.Success) LogSuccess(d.Text);
            else LogInfo(d.Text);
        }

        private ScanOutput DoScan()
        {
            var result = new ScanOutput();

            object catiaObj = CatiaConnect.Connect(result.Diag);
            if (catiaObj == null)
            {
                result.Diag.AddRange(CatiaConnect.Teshis());
                result.Error = "CATIA Bağlantısı Kurulamadı. Nedeni İçin Yukarıdaki Teşhis Satırlarına Bakın.";
                return result;
            }

            result.Catia = catiaObj;
            dynamic catia = catiaObj;
            dynamic editor = catia.ActiveEditor;
            dynamic root = editor.ActiveObject;

            if (!HasOccurrences(root))
            {
                result.Error = "Montaj Bulunamadı. Bir Physical Product Açın.";
                return result;
            }

            var found = new Dictionary<string, ScanItem>();
            ScanNode(root, "", found, result);

            foreach (var kv in found)
            {
                bool kalintiThickness;
                List<string> sacTeshisi;
                double thk = GetThickness(
                    kv.Value.Part, out kalintiThickness, out sacTeshisi);
                if (thk <= 0)
                {
                    if (kalintiThickness)
                    {
                        result.Diag.Add(new DiagLine(
                            "Sac filtresi dışladı — " + kv.Value.ProductName +
                            ": Thickness var, PartBody içinde tanınan Sheet Metal unsuru yok.",
                            DiagLevel.Info));

                        foreach (string satir in sacTeshisi)
                        {
                            result.Diag.Add(new DiagLine(
                                "   ↳ " + satir,
                                DiagLevel.Info));
                        }
                    }
                    continue;
                }

                double modelKalinligi = Math.Round(thk, 2);
                double hamSacKalinligi = HamSacKalinliklari.Getir(
                    kv.Value.ProductName,
                    kv.Value.PartName,
                    modelKalinligi);

                result.Rows.Add(new SheetRow
                {
                    ProductName = kv.Value.ProductName,
                    PartName = kv.Value.PartName,
                    Thickness = modelKalinligi,
                    HamSacKalinligiMetni = HamSacKalinliklari.Goster(hamSacKalinligi),
                    UygulananHamSacKalinligi = hamSacKalinligi,
                    Quantity = kv.Value.Count
                });

                result.RepRefs[kv.Value.PartName] = kv.Value.RepRef;
                result.Total += kv.Value.Count;
            }

            return result;
        }

        private bool _exporting;

        // Export surerken tarama ve yeni export baslatilamaz
        private void SetExporting(bool active)
        {
            _exporting = active;
            btnScan.IsEnabled = !active;
            btnExportAll.IsEnabled = !active;
            btnHamSacGuncelle.IsEnabled = !active;
            grid.IsEnabled = !active;
        }

        // Tarama sirasinda butondaki donen gostergeyi acip kapatir
        private void SetScanning(bool active)
        {
            btnScan.IsHitTestVisible = !active;
            btnExportAll.IsEnabled = !active;
            btnHamSacGuncelle.IsEnabled = !active;
            grid.IsEnabled = !active;

            scanIcon.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            scanSpinner.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            scanText.Text = active ? "Taranıyor..." : "CATIA'yı Tara";

            if (active)
            {
                var spin = new System.Windows.Media.Animation.DoubleAnimation(0, 360,
                    TimeSpan.FromSeconds(0.9));
                spin.RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever;
                scanSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
            }
            else
            {
                scanSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        // ================= CATIA BAGLANTISI =================

        // Baglanti mantigi CatiaConnect icinde; burasi sadece sessiz bir sarmalayici
        private static object GetCatia()
        {
            try { return CatiaConnect.Connect(null); }
            catch { return null; }
        }

        private static bool HasOccurrences(dynamic node)
        {
            try
            {
                dynamic subs = node.Occurrences;
                if (subs == null) return false;
                int c = subs.Count;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================= AGAC TARAMA =================

        private class ScanItem
        {
            public object Part;
            public object RepRef;
            public string ProductName = "";
            public string PartName = "";
            public int Count;
        }

        private void ScanNode(dynamic node, string parentProd,
                              Dictionary<string, ScanItem> found, ScanOutput result)
        {
            // Urun adi yalnizca dugumun VPMReference nesnesindeki Title'dan okunur.
            // node.Name instance adidir (orn. "hhh.1") ve parca/DXF adi icin kullanilmaz.
            object dugumRef = ReferansAl(node);
            string prodName = dugumRef == null ? "" : PlmBaslik(dugumRef);

            if (string.IsNullOrWhiteSpace(prodName))
            {
                // Title okunamazsa instance ada geri donmek ayni referansin .1, .2...
                // seklinde farkli adlarla listelenmesine neden olur. Bu nedenle yedek ad da
                // Reference uzerindeki kalici PLM kimliginden alinir.
                prodName = dugumRef == null ? "" : PlmDeger(dugumRef, "PLM_ExternalID");
            }

            if (string.IsNullOrWhiteSpace(prodName))
                prodName = string.IsNullOrWhiteSpace(parentProd)
                    ? "REFERENCE_TITLE_OKUNAMADI"
                    : parentProd;

            try
            {
                dynamic reps = node.RepOccurrences;
                if (reps != null)
                {
                    int cnt = reps.Count;
                    for (int i = 1; i <= cnt; i++)
                    {
                        string key = "";
                        object part = null;
                        object repRefObj = null;

                        try
                        {
                            dynamic repOcc = reps.Item(i);
                            dynamic repInst = repOcc.RelatedRepInstance;
                            dynamic repRef = repInst.ReferenceInstanceOf;
                            repRefObj = repRef;

                            if (!result.ParcaDokuldu)
                            {
                                result.ParcaDokuldu = true;
                                UyeDok(result.Diag, "Ürün Düğümü", (object)node, true);
                                if (dugumRef != null)
                                    UyeDok(result.Diag, "Ürün Referansı", dugumRef, true);
                                UyeDok(result.Diag, "Parça Occurrence", (object)repOcc);
                                UyeDok(result.Diag, "Parça Instance", (object)repInst);
                                UyeDok(result.Diag, "Parça Referansı", repRefObj, true);
                            }

                            key = PlmBaslik(repRefObj);
                            part = ParcaNesnesiAl(repRefObj);
                        }
                        catch { }

                        // Drawing gibi Part/CATIAPart nesnesi olmayan representation'lar
                        // sac taramasina ve adet hesabina girmemeli.
                        if (key.Length == 0 || part == null || repRefObj == null) continue;

                        // Ayni parca farkli urunler altinda ayri satir olsun diye
                        // urun+parca ciftiyle grupla
                        string mapKey = prodName + "||" + key;

                        ScanItem item;
                        if (found.TryGetValue(mapKey, out item))
                        {
                            item.Count++;
                        }
                        else
                        {
                            found[mapKey] = new ScanItem
                            {
                                Part = part,
                                RepRef = repRefObj,
                                ProductName = prodName,
                                PartName = key,
                                Count = 1
                            };
                        }
                    }
                }
            }
            catch { }

            try
            {
                dynamic subs = node.Occurrences;
                if (subs != null)
                {
                    int cnt = subs.Count;
                    for (int i = 1; i <= cnt; i++)
                        ScanNode(subs.Item(i), prodName, found, result);
                }
            }
            catch { }
        }

        private static object ParcaNesnesiAl(object repRef)
        {
            if (repRef == null) return null;

            try
            {
                object part = ((dynamic)repRef).GetItem("Part");
                if (part != null) return part;
            }
            catch { }

            // Bazi 3DEXPERIENCE surumlerinde ayni nesne CATIAPart adi ile acilir.
            try
            {
                object part = ((dynamic)repRef).GetItem("CATIAPart");
                if (part != null) return part;
            }
            catch { }

            return null;
        }

        // ================= PLM AD/BASLIK =================
        //
        // 3DEXPERIENCE'ta Properties > Reference bolumundeki alanlar:
        //   Title -> V_Name         (kullanicinin verdigi referans basligi)
        //   Name  -> PLM_ExternalID (kalici PLM kimligi)
        // Liste ve DXF adi icin yalnizca Reference Title kullanilir.

        private static readonly string[] BaslikUyeleri = { "V_Name", "Title" };
        private static readonly string[] TumUyeler = { "V_Name", "Title", "Name", "PLM_ExternalID" };

        // dynamic uzerinde uye adi degisken olamaz; her aday ayri ayri denenir
        private static string PlmDeger(object nesne, string uye)
        {
            if (nesne == null) return "";

            // VPMReference alanlari bircok 3DEXPERIENCE surumunde normal COM
            // property olarak degil GetAttributeValue ile sunulur. Ozellikle ekranda
            // "Title" olarak gorunen alanin teknik adi V_Name'dir.
            try
            {
                dynamic d = nesne;
                object attr = d.GetAttributeValue(uye);
                string text = attr == null ? "" : Convert.ToString(attr).Trim();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch { }

            try
            {
                dynamic d = nesne;
                object v = null;

                switch (uye)
                {
                    case "V_Name": v = d.V_Name; break;
                    case "Title": v = d.Title; break;
                    case "Name": v = d.Name; break;
                    case "PLM_ExternalID": v = d.PLM_ExternalID; break;
                }

                return v == null ? "" : Convert.ToString(v).Trim();
            }
            catch { return ""; }
        }

        private static string PlmBaslik(object nesne)
        {
            if (nesne == null) return "";

            foreach (string uye in BaslikUyeleri)
            {
                string v = PlmDeger(nesne, uye);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }

            return "";
        }

        // Hangi uyenin ne dondurdugunu konsola yazar; tarama basina bir kez cagrilir
        private static void UyeDok(List<DiagLine> diag, string etiket, object nesne,
                                   bool tamListe = false)
        {
            var parcalar = new List<string>();

            foreach (string uye in TumUyeler)
            {
                string v = PlmDeger(nesne, uye);
                parcalar.Add(uye + "=" + (v.Length == 0 ? "(yok)" : "\"" + v + "\""));
            }

            string tip = ComProbe.TipAdi(nesne);
            diag.Add(new DiagLine(etiket + " [" + tip + "]: " + string.Join("  ", parcalar),
                                  DiagLevel.Info));

            if (!tamListe) return;

            // Tip kutuphanesinden gercek uye listesini oku; tahmin gerekmesin
            var adlar = ComProbe.UyeAdlari(nesne);
            if (adlar.Count == 0)
            {
                diag.Add(new DiagLine("   " + etiket + " üye listesi okunamadı.", DiagLevel.Info));
                return;
            }

            var ilginc = ComProbe.IlginçUyeler(adlar);
            diag.Add(new DiagLine("   " + etiket + " üyeleri (" + adlar.Count + "): " +
                                  string.Join(", ", ilginc.Count > 0 ? ilginc : adlar),
                                  DiagLevel.Info));
        }

        // Bir occurrence'in VPMReference nesnesi; Title mutlaka buradan okunur.
        private static object ReferansAl(dynamic node)
        {
            // VPMOccurrence -> VPMInstance -> VPMReference
            // 3DEXPERIENCE Product Modeler'in standart occurrence yolu budur.
            try
            {
                dynamic ins = node.InstanceOccurrenceOf;
                object r = InstanceReferansi(ins);
                if (r != null) return r;
            }
            catch { }

            // Bazi COM surumleri parametresiz uyeleri method olarak cagirir.
            try
            {
                dynamic ins = node.InstanceOccurrenceOf();
                object r = InstanceReferansi(ins);
                if (r != null) return r;
            }
            catch { }

            // 2018x/2020x gibi bazi on-premise surumlerde occurrence'in
            // VPMInstance baglantisi PLMEntity adi ile acilir.
            try
            {
                dynamic ins = node.PLMEntity;
                object r = InstanceReferansi(ins);
                if (r != null) return r;
            }
            catch { }

            // Root occurrence'in instance'i yoktur; kendi reference baglantisi vardir.
            try { object r = node.ReferenceRootOccurrenceOf; if (r != null) return r; } catch { }
            try { object r = node.ReferenceRootOccurrenceOf(); if (r != null) return r; } catch { }

            // Eski/alternatif API adlariyla geriye uyumluluk.
            try
            {
                dynamic ins = node.RelatedInstance;
                object r = InstanceReferansi(ins);
                if (r != null) return r;
            }
            catch { }

            try { object r = node.ReferenceInstanceOf; if (r != null) return r; } catch { }
            try { object r = node.Reference; if (r != null) return r; } catch { }

            return null;
        }

        private static object InstanceReferansi(object instance)
        {
            if (instance == null) return null;

            try
            {
                dynamic ins = instance;
                object r = ins.ReferenceInstanceOf;
                if (r != null) return r;
            }
            catch { }

            try
            {
                dynamic ins = instance;
                object r = ins.ReferenceInstanceOf();
                if (r != null) return r;
            }
            catch { }

            return null;
        }

        // ================= KALINLIK / GERCEK SHEET METAL DOGRULAMA =================
        //
        // Bir parcayi sac kabul etmek icin iki kosul ayni Part icinde saglanmalidir:
        // 1) Sheet Metal Parameters altinda gecerli Thickness bulunmali.
        // 2) PartBody altinda gercek bir Sheet Metal feature bulunmali.
        //
        // Sheet Metal feature'dan once veya sonra Pad/Pocket gibi Part Design
        // ozellikleri bulunabilir; siralama sonucu degistirmez. Yalnizca Thickness
        // kalmis ve govdesinde hic Sheet Metal feature olmayan parcalar elenir.

        private static readonly string[] ThicknessNames =
        {
            "thickness", "kalinlik", "epaisseur", "dicke", "spessore",
            "espesor", "espessura", "dikte", "tjocklek", "tykkelse"
        };

        // CATIA agacindaki feature adi arayuz diline gore degisebilir. Sol taraf
        // Fold() ile normalize edilmis gorunen ad, sag taraf dil-bagimsiz kanonik addir.
        // Yalnizca gercek Sheet Metal komutlari bulunur; Pad/Pocket/Hole gibi genel
        // Part Design komutlari bilerek bu sozluge alinmaz.
        private static readonly Dictionary<string, string> SheetMetalFeatureAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Temel duvarlar ve bukumler
                { "wall", "Wall" },
                { "sheetmetalwall", "Wall" },
                { "duvar", "Wall" },

                { "wallonedge", "Wall on Edge" },
                { "edgewall", "Wall on Edge" },
                { "sheetmetalwallonedge", "Wall on Edge" },
                { "kenardakiduvar", "Wall on Edge" },
                { "kenardaduvar", "Wall on Edge" },
                { "kenaruzerindeduvar", "Wall on Edge" },
                { "kenaruzerindekiduvar", "Wall on Edge" },

                { "bend", "Bend" },
                { "sheetmetalbend", "Bend" },
                { "bukum", "Bend" },
                { "bendfromflat", "Bend From Flat" },
                { "duzdenbukum", "Bend From Flat" },

                { "flange", "Flange" },
                { "sheetmetalflange", "Flange" },
                { "flans", "Flange" },
                { "userflange", "User Flange" },
                { "kullaniciflans", "User Flange" },
                { "kullaniciflansi", "User Flange" },
                { "loftedflange", "Lofted Flange" },
                { "loftflansi", "Lofted Flange" },
                { "gecisflansi", "Lofted Flange" },

                { "hem", "Hem" },
                { "kivirma", "Hem" },
                { "kenarkivirma", "Hem" },
                { "teardrop", "Teardrop" },
                { "gozyasi", "Teardrop" },
                { "damla", "Teardrop" },
                { "fold", "Fold" },
                { "katla", "Fold" },
                { "katlama", "Fold" },
                { "unfold", "Unfold" },
                { "acinim", "Unfold" },
                { "acilim", "Unfold" },
                { "acma", "Unfold" },

                // Ana sac olusturma komutlari
                { "extrusion", "Extrusion" },
                { "extruzyon", "Extrusion" },
                { "ekstruzyon", "Extrusion" },
                { "sheetmetalextrusion", "Extrusion" },
                { "sheetmetalextruzyon", "Extrusion" },
                { "sheetmetalekstruzyon", "Extrusion" },
                { "web", "Web" },
                { "sheetmetalweb", "Web" },
                { "perde", "Web" },
                { "rolledwall", "Rolled Wall" },
                { "ruloduvar", "Rolled Wall" },
                { "haddelenmisduvar", "Rolled Wall" },
                { "sweptwall", "Swept Wall" },
                { "supurmeduvar", "Swept Wall" },
                { "supurulmusduvar", "Swept Wall" },
                { "joggle", "Joggle" },
                { "kademelendirme", "Joggle" },
                { "ofsetbukum", "Joggle" },
                { "zofset", "Joggle" },

                // Kesimler, koseler ve sac delikleri
                { "cutout", "Cutout" },
                { "sheetmetalcutout", "Cutout" },
                { "kesim", "Cutout" },
                { "kesme", "Cutout" },
                { "corner", "Corner" },
                { "sheetmetalcorner", "Corner" },
                { "kose", "Corner" },
                { "extrudedhole", "Extruded Hole" },
                { "sheetmetalextrudedhole", "Extruded Hole" },
                { "extruzyonludelik", "Extruded Hole" },
                { "ekstruzyonludelik", "Extruded Hole" },
                { "flangedcutout", "Flanged Cutout" },
                { "flanslikesim", "Flanged Cutout" },
                { "flanslikesme", "Flanged Cutout" },
                { "circularcutout", "Circular Cutout" },
                { "daireselkesim", "Circular Cutout" },
                { "daireselkesme", "Circular Cutout" },
                { "sheetmetalhole", "Sheet Metal Hole" },
                { "sacmetaldeligi", "Sheet Metal Hole" },
                { "sacmetalideligi", "Sheet Metal Hole" },
                { "saclevhadeligi", "Sheet Metal Hole" },
                { "sacdeligi", "Sheet Metal Hole" },

                // Damgalar ve sekillendirme komutlari
                { "bead", "Bead" },
                { "kordon", "Bead" },
                { "boncuk", "Bead" },
                { "kabartma", "Bead" },
                { "louver", "Louver" },
                { "panjur", "Louver" },
                { "menfez", "Louver" },
                { "dowel", "Dowel" },
                { "kavela", "Dowel" },
                { "pimkabartma", "Dowel" },
                { "surfacestamp", "Surface Stamp" },
                { "yuzeydamga", "Surface Stamp" },
                { "yuzeydamgasi", "Surface Stamp" },
                { "curvestamp", "Curve Stamp" },
                { "egridamga", "Curve Stamp" },
                { "egridamgasi", "Curve Stamp" },
                { "circularstamp", "Circular Stamp" },
                { "daireseldamga", "Circular Stamp" },
                { "stiffeningrib", "Stiffening Rib" },
                { "takviyenervuru", "Stiffening Rib" },
                { "sertlestirmekaburgasi", "Stiffening Rib" },
                { "bridge", "Bridge" },
                { "kopru", "Bridge" },

                // Mevcut geometriden sac tanima
                { "recognize", "Sheet Metal Recognition" },
                { "recognition", "Sheet Metal Recognition" },
                { "sheetmetalrecognize", "Sheet Metal Recognition" },
                { "sheetmetalrecognition", "Sheet Metal Recognition" },
                { "recognizesheetmetal", "Sheet Metal Recognition" },
                { "tani", "Sheet Metal Recognition" },
                { "tanima", "Sheet Metal Recognition" },
                { "sacmetalitanima", "Sheet Metal Recognition" },
                { "sactanima", "Sheet Metal Recognition" }
            };

        private static double GetThickness(
            object partObj,
            out bool kalintiThickness,
            out List<string> teshis)
        {
            kalintiThickness = false;
            teshis = new List<string>();
            if (partObj == null) return 0;

            dynamic parameters = null;

            try { parameters = ((dynamic)partObj).Parameters; }
            catch { return 0; }

            if (parameters == null) return 0;

            int parameterCount;
            try { parameterCount = Convert.ToInt32(parameters.Count); }
            catch { return 0; }

            double thicknessMm = 0;
            bool thicknessFound = false;
            bool sheetMetalFeatureFound = false;
            int activityAdayi = 0;

            // Bazi Sheet Metal komutlari (ozellikle Recognize) Activity parametresi
            // uretmeyebilir. Once PartBody'nin Shapes koleksiyonuna dogrudan bakilir.
            string agactakiFeature;
            if (TryFindSheetMetalFeatureInPartBody(partObj, out agactakiFeature))
            {
                sheetMetalFeatureFound = true;
                teshis.Add("PartBody ağacında Sheet Metal unsuru: " + agactakiFeature);
            }

            for (int i = 1; i <= parameterCount; i++)
            {
                dynamic parameter = null;
                try { parameter = parameters.Item(i); }
                catch { continue; }

                if (parameter == null) continue;

                string parameterName = GetParameterName(parameter);
                if (string.IsNullOrWhiteSpace(parameterName)) continue;

                object rawValue = GetParameterRawValue(parameter);
                string displayValue = GetParameterDisplayValue(parameter);

                // Yalnizca Activity ile biten yollara bagli kalma. Feature'a ait
                // herhangi bir parametre yolu da Sheet Metal unsurunun varligini
                // kanitlar. Bu nedenle Part Design / Sheet Metal sirasi onemsizdir.
                bool featurePartBodyAltinda;
                string yoldakiFeature;
                if (!sheetMetalFeatureFound &&
                    SheetMetalFeatureYolunuCoz(
                        parameterName,
                        out featurePartBodyAltinda,
                        out yoldakiFeature) &&
                    featurePartBodyAltinda &&
                    yoldakiFeature.Length > 0)
                {
                    sheetMetalFeatureFound = true;
                    teshis.Add(
                        "Sheet Metal feature yolu: \"" + parameterName + "\"" +
                        " | Tanınan=" + yoldakiFeature);
                }

                if (!thicknessFound && IsSheetMetalThicknessParameter(parameterName))
                {
                    double candidateMm = NormalizeLengthMillimeters(rawValue, displayValue);

                    teshis.Add(
                        "Thickness yolu: \"" + parameterName + "\"" +
                        " | Raw=" + ParametreDegerMetni(rawValue) +
                        " | Display=" + BosIseYok(displayValue) +
                        " | Normalize=" + candidateMm.ToString(
                            System.Globalization.CultureInfo.InvariantCulture) + " mm");

                    if (candidateMm >= 0.05 && candidateMm <= 100)
                    {
                        thicknessMm = candidateMm;
                        thicknessFound = true;
                    }
                }

                bool partBodyAltinda;
                string taninanFeature;

                if (ActivityYolunuCoz(
                        parameterName, out partBodyAltinda, out taninanFeature))
                {
                    activityAdayi++;
                    bool aktif = IsTrueParameter(parameter);
                    bool sheetMetal = partBodyAltinda && taninanFeature.Length > 0;

                    if (teshis.Count < 60)
                    {
                        teshis.Add(
                            "Activity adayı: \"" + parameterName + "\"" +
                            " | Raw=" + ParametreDegerMetni(rawValue) +
                            " | Display=" + BosIseYok(displayValue) +
                            " | Aktif=" + (aktif ? "EVET" : "HAYIR") +
                            " | PartBody=" + (partBodyAltinda ? "EVET" : "HAYIR") +
                            " | Tanınan Sheet Metal feature=" +
                            (taninanFeature.Length == 0 ? "YOK" : taninanFeature));
                    }

                    // Activity bilgisi teshis icin tutulur. Kullanici kuralina gore
                    // feature PartBody icinde varsa, Activity olmasa veya False olsa
                    // bile parca Sheet Metal kabul edilir.
                    if (!sheetMetalFeatureFound && sheetMetal)
                        sheetMetalFeatureFound = true;
                }

                if (thicknessFound && sheetMetalFeatureFound)
                    return thicknessMm;
            }

            // Thickness var fakat PartBody'de Sheet Metal feature yoksa kalintidir.
            kalintiThickness = thicknessFound && !sheetMetalFeatureFound;

            if (kalintiThickness && activityAdayi == 0)
            {
                teshis.Add(
                    "Activity ile biten hiçbir parametre yolu bulunamadı; " +
                    "CATIA'nın gerçek feature yolu bu sürümde farklı olabilir.");
            }
            else if (kalintiThickness)
            {
                teshis.Add(
                    "Activity adayı sayısı: " + activityAdayi +
                    ". PartBody içinde tanınan Sheet Metal feature bulunamadı.");
            }

            return 0;
        }

        private static string ParametreDegerMetni(object value)
        {
            if (value == null) return "(yok)";

            try
            {
                string text = Convert.ToString(value).Trim();
                return text.Length == 0 ? "(boş)" : "\"" + text + "\"";
            }
            catch { return "(okunamadı)"; }
        }

        private static string BosIseYok(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(yok)" : "\"" + value + "\"";
        }

        private static string GetParameterName(dynamic parameter)
        {
            try
            {
                object value = parameter.Name;
                return value == null ? "" : Convert.ToString(value).Trim();
            }
            catch { return ""; }
        }

        private static object GetParameterRawValue(dynamic parameter)
        {
            try { return parameter.Value; }
            catch { return null; }
        }

        private static string GetParameterDisplayValue(dynamic parameter)
        {
            try
            {
                object value = parameter.ValueAsString;
                if (value != null) return Convert.ToString(value).Trim();
            }
            catch { }

            try
            {
                object value = parameter.ValueAsString();
                if (value != null) return Convert.ToString(value).Trim();
            }
            catch { }

            return "";
        }

        private static bool IsSheetMetalThicknessParameter(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return false;

            string foldedName = Fold(parameterName);

            // CATIA otomasyonunda genellikle teknik ad (Sheet Metal Parameters)
            // gelir; yerellestirilmis Turkce gorunum icin Sac Parametreleri de taninir.
            bool sheetMetalPath =
                foldedName.Contains("sheetmetalparameters") ||
                foldedName.Contains("sacparametreleri");

            return sheetMetalPath && IsThicknessName(LastSegment(parameterName));
        }

        private static double NormalizeLengthMillimeters(object rawValue, string displayValue)
        {
            double displayNumber;
            string displayUnit;

            if (TryReadLengthDisplay(displayValue, out displayNumber, out displayUnit))
            {
                switch (displayUnit)
                {
                    case "mm": return displayNumber;
                    case "cm": return displayNumber * 10.0;
                    case "m": return displayNumber * 1000.0;
                    case "um": return displayNumber / 1000.0;
                    case "in": return displayNumber * 25.4;
                }

                if (displayNumber >= 0.05 && displayNumber <= 100)
                    return displayNumber;
            }

            double rawNumber;
            if (!TryConvertDouble(rawValue, out rawNumber)) return 0;

            rawNumber = Math.Abs(rawNumber);

            // Bazi COM surumleri Length.Value degerini metre, bazilari mm dondurur.
            if (rawNumber >= 0.00005 && rawNumber < 0.05)
                return rawNumber * 1000.0;

            if (rawNumber >= 0.05 && rawNumber <= 100)
                return rawNumber;

            return 0;
        }

        private static bool TryReadLengthDisplay(
            string text, out double number, out string unit)
        {
            number = 0;
            unit = "";

            if (string.IsNullOrWhiteSpace(text)) return false;

            string normalized = text.Trim().ToLowerInvariant()
                .Replace("µ", "u")
                .Replace("μ", "u");

            var match = System.Text.RegularExpressions.Regex.Match(
                normalized,
                @"[-+]?\d+(?:[.,]\d+)?(?:[eE][-+]?\d+)?");

            if (!match.Success) return false;

            string numericText = match.Value.Replace(',', '.');

            if (!double.TryParse(
                    numericText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number))
                return false;

            if (normalized.Contains("mm")) unit = "mm";
            else if (normalized.Contains("cm")) unit = "cm";
            else if (normalized.Contains("um")) unit = "um";
            else if (normalized.Contains("inch") || normalized.Contains(" in")) unit = "in";
            else if (System.Text.RegularExpressions.Regex.IsMatch(
                         normalized, @"(^|[^a-z])m($|[^a-z])")) unit = "m";

            number = Math.Abs(number);
            return true;
        }

        private static bool TryConvertDouble(object value, out double number)
        {
            number = 0;
            if (value == null) return false;

            try
            {
                number = Convert.ToDouble(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch { }

            string text;
            try { text = Convert.ToString(value); }
            catch { return false; }

            if (string.IsNullOrWhiteSpace(text)) return false;

            return double.TryParse(
                text.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out number);
        }

        // Herhangi bir parametre yolunda PartBody ve Sheet Metal feature arar.
        // Activity zorunlu degildir; Recognize gibi komutlar farkli parametreler
        // uretebilir veya hic Activity parametresi uretmeyebilir.
        private static bool SheetMetalFeatureYolunuCoz(
            string parameterName,
            out bool partBodyAltinda,
            out string taninanFeature)
        {
            partBodyAltinda = false;
            taninanFeature = "";

            if (string.IsNullOrWhiteSpace(parameterName)) return false;

            string normalizedPath = parameterName.Replace('/', '\\');
            string[] segments = normalizedPath.Split('\\');
            if (segments.Length < 2) return false;

            int bodyIndex = -1;
            for (int i = 0; i < segments.Length; i++)
            {
                string bodyName = Fold(segments[i]);
                if (bodyName == "partbody" || bodyName == "parcagovdesi")
                {
                    bodyIndex = i;
                    partBodyAltinda = true;
                    break;
                }
            }

            if (bodyIndex < 0) return true;

            // Son segment parametrenin kendisidir; yalnizca ust yol feature olabilir.
            for (int i = bodyIndex + 1; i < segments.Length - 1; i++)
            {
                string canonicalName;
                if (!SheetMetalFeatureAliases.TryGetValue(
                        Fold(segments[i]), out canonicalName))
                    continue;

                taninanFeature = segments[i] + " → " + canonicalName;
                break;
            }

            return true;
        }

        // PartBody.Shapes dogrudan taranir. Bu, Parameters koleksiyonunda feature
        // yolu gorunmeyen Recognize ve benzeri Sheet Metal komutlarini da yakalar.
        private static bool TryFindSheetMetalFeatureInPartBody(
            object partObj, out string taninanFeature)
        {
            taninanFeature = "";
            if (partObj == null) return false;

            dynamic bodies = null;
            try { bodies = ((dynamic)partObj).Bodies; }
            catch { }

            // Dil ve kullanici tarafindan verilen govde adindan bagimsiz ana yol.
            dynamic mainBody = null;
            try { mainBody = ((dynamic)partObj).MainBody; }
            catch { }

            if (mainBody == null && bodies != null)
            {
                try { mainBody = bodies.MainBody; }
                catch { }
            }

            if (mainBody != null &&
                TryFindSheetMetalFeatureInBody(mainBody, out taninanFeature))
                return true;

            if (bodies == null) return false;

            int bodyCount;
            try { bodyCount = Convert.ToInt32(bodies.Count); }
            catch { return false; }

            for (int bodyNo = 1; bodyNo <= bodyCount; bodyNo++)
            {
                dynamic body = null;
                try { body = bodies.Item(bodyNo); }
                catch { continue; }

                if (body == null) continue;

                string bodyName = "";
                try { bodyName = Convert.ToString(body.Name).Trim(); }
                catch { }

                string foldedBody = Fold(bodyName);
                if (foldedBody != "partbody" && foldedBody != "parcagovdesi")
                    continue;

                if (TryFindSheetMetalFeatureInBody(body, out taninanFeature))
                    return true;
            }

            return false;
        }

        private static bool TryFindSheetMetalFeatureInBody(
            dynamic body, out string taninanFeature)
        {
            taninanFeature = "";
            if (body == null) return false;

            dynamic shapes = null;
            try { shapes = body.Shapes; }
            catch { return false; }

            if (shapes == null) return false;

            int shapeCount;
            try { shapeCount = Convert.ToInt32(shapes.Count); }
            catch { return false; }

            // Shapes sirayla gezilir fakat herhangi bir eslesme yeterlidir.
            // Part Design unsurunun once veya sonra olmasi sonucu degistirmez.
            for (int shapeNo = 1; shapeNo <= shapeCount; shapeNo++)
            {
                dynamic shape = null;
                try { shape = shapes.Item(shapeNo); }
                catch { continue; }

                if (shape == null) continue;

                string shapeName = "";
                try { shapeName = Convert.ToString(shape.Name).Trim(); }
                catch { }

                string canonicalName;
                if (!SheetMetalFeatureAliases.TryGetValue(
                        Fold(shapeName), out canonicalName))
                    continue;

                taninanFeature = shapeName + " → " + canonicalName;
                return true;
            }

            return false;
        }

        // Activity parametresinin tam yolunu teshis eder. Kabul kuralini Activity
        // belirlemez; bu bilgi yalnizca konsolda gorunur.
        private static bool ActivityYolunuCoz(
            string parameterName,
            out bool partBodyAltinda,
            out string taninanFeature)
        {
            partBodyAltinda = false;
            taninanFeature = "";

            if (string.IsNullOrWhiteSpace(parameterName)) return false;

            string normalizedPath = parameterName.Replace('/', '\\');
            string[] segments = normalizedPath.Split('\\');
            if (segments.Length < 2) return false;

            string activityName = Fold(segments[segments.Length - 1]);
            if (activityName != "activity" &&
                activityName != "aktivite" &&
                activityName != "etkinlik")
                return false;

            SheetMetalFeatureYolunuCoz(
                parameterName, out partBodyAltinda, out taninanFeature);
            return true;
        }

        private static bool IsTrueParameter(dynamic parameter)
        {
            object rawValue = GetParameterRawValue(parameter);

            if (rawValue is bool) return (bool)rawValue;

            double numericValue;
            if (TryConvertDouble(rawValue, out numericValue))
                return Math.Abs(numericValue) > double.Epsilon;

            string rawText = "";
            try { rawText = rawValue == null ? "" : Convert.ToString(rawValue).Trim(); }
            catch { }

            return IsTrueText(rawText) || IsTrueText(GetParameterDisplayValue(parameter));
        }

        private static bool IsTrueText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            string text = value.Trim().ToLowerInvariant();
            return text == "true" || text == "1" || text == "-1" ||
                   text == "yes" || text == "evet" ||
                   text == "vrai" || text == "wahr";
        }

        private static bool IsThicknessName(string leaf)
        {
            string folded = Fold(leaf);
            if (folded.Contains("thick")) return true;

            foreach (string name in ThicknessNames)
                if (folded == Fold(name)) return true;

            return false;
        }

        private static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            StringBuilder result = new StringBuilder();
            foreach (char original in text)
            {
                char ch = original;
                if (ch == 'Ç' || ch == 'ç') ch = 'c';
                else if (ch == 'Ğ' || ch == 'ğ') ch = 'g';
                else if (ch == 'İ' || ch == 'I' || ch == 'ı') ch = 'i';
                else if (ch == 'Ö' || ch == 'ö') ch = 'o';
                else if (ch == 'Ş' || ch == 'ş') ch = 's';
                else if (ch == 'Ü' || ch == 'ü') ch = 'u';

                if (ch < 128 && char.IsLetter(ch))
                    result.Append(char.ToLowerInvariant(ch));
            }

            return result.ToString();
        }

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            path = path.Replace('/', '\\');
            int position = path.LastIndexOf('\\');
            if (position < 0) return path;

            return path.Substring(position + 1);
        }

        // ================= WINDOWS API =================

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Windows'un on plan kilidini asarak pencereyi gercekten one getirir
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private static bool ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            if (GetForegroundWindow() == hWnd) return true;

            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint me = GetCurrentThreadId();

            AttachThreadInput(me, fg, true);

            // SADECE simge durumundaysa geri yukle, aksi halde boyuta dokunma
            if (IsIconic(hWnd)) ShowWindow(hWnd, 9);

            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            AttachThreadInput(me, fg, false);

            System.Threading.Thread.Sleep(200);
            return GetForegroundWindow() == hWnd;
        }

        // Pano kilitliyse birkac kez dener
        private static bool SafeSetClipboard(string text)
        {
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(150);
                }
            }
            return false;
        }

        // Windows kaydetme penceresi kapanana kadar bekler
        private async System.Threading.Tasks.Task<bool> WaitForNoSaveDialog(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                IntPtr h = IntPtr.Zero;
                EnumWindows((w, l) =>
                {
                    if (!IsWindowVisible(w)) return true;
                    if (GetCls(w) != "#32770") return true;
                    if (GetDlgItem(w, 1) != IntPtr.Zero) { h = w; return false; }
                    return true;
                }, IntPtr.Zero);

                if (h == IntPtr.Zero) return true;

                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return false;
        }

        // Montaj penceresine donuldu mu (Part kapandi mi)
        private async System.Threading.Tasks.Task<bool> WaitForAssembly(int timeoutMs)
        {
            dynamic catia = _catia;
            int waited = 0;
            while (waited < timeoutMs)
            {
                try
                {
                    dynamic obj = catia.ActiveEditor.ActiveObject;
                    if (HasOccurrences(obj)) return true;
                }
                catch { }

                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return false;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint WM_SETTEXT = 0x000C;
        private const uint BM_CLICK = 0x00F5;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static string GetText(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetCls(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static void SelectAll()
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);              // Ctrl down
            keybd_event(0x41, 0, 0, UIntPtr.Zero);              // A down
            keybd_event(0x41, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            System.Threading.Thread.Sleep(150);
        }

        // ================= DXF EXPORT =================

        // Sag tiklanan satiri secili yapar; boylece menu her zaman dogru parcayla calisir
        private void grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is DataGridRow))
            {
                if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
                    dep = VisualTreeHelper.GetParent(dep);
                else if (dep is FrameworkContentElement fce)
                    dep = fce.Parent;
                else
                    break;
            }

            if (dep is DataGridRow row)
                row.IsSelected = true;
        }

        private async void mnuExportDxf_Click(object sender, RoutedEventArgs e)
        {
            if (_exporting) return;
            if (!ExportIzinliMi()) return;

            if (grid.SelectedItem == null)
            {
                LogInfo("Önce Listeden Bir Parça Seçin.");
                return;
            }

            SheetRow row = (SheetRow)grid.SelectedItem;

            GridDegisikliginiTamamla();
            double hamSacKalinligi;
            if (!HamSacSatiriniDogrula(row, out hamSacKalinligi)) return;

            if (!_repRefs.ContainsKey(row.PartName) || _repRefs[row.PartName] == null)
            {
                LogError("Parça Referansı Bulunamadı: " + row.PartName);
                return;
            }

            // Tek parcada da fare devralindigi icin bilgilendirme burada da
            // cikar; dosya adi sorulmadan once, bosuna soru sorulmasin
            if (!FareUyarisiniGoster()) return;

            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Filter = "DXF|*.dxf";
            dlg.FileName = MakeFileName(row, hamSacKalinligi);
            if (dlg.ShowDialog() != true) return;

            try
            {
                LogInfo("DXF Export Başladı: " + row.PartName);
                _stopRequested = false;
                ShowPipStart(row.PartName);

                // Islem devam ettigi surece fiziksel girdi kilitli (Macria pencereleri haric)
                SetExporting(true);
                LogInfo("İşlem Sürerken Fare ve Klavyeye Dokunmayın — Odak Kayarsa Export Bozulur.");

                bool ok;
                try
                {
                    ok = await ExportOne(_repRefs[row.PartName], dlg.FileName);
                }
                finally
                {
                    SetExporting(false);
                }

                if (_stopRequested)
                {
                    LogError("Export Durduruldu: " + row.PartName);
                    await FinishPip(ExportPipWindow.PipState.Stopped, row.PartName);
                }
                else if (ok)
                {
                    OnizlemeyeYaz(row, dlg.FileName);

                    if (chkOpenAfter.IsChecked == true)
                        OpenExported(dlg.FileName);
                    await FinishPip(ExportPipWindow.PipState.Done, row.PartName);
                }
                else
                {
                    await FinishPip(ExportPipWindow.PipState.Error, row.PartName);
                }
            }
            catch (Exception ex)
            {
                LogError("Hata: " + ex.Message);
                await FinishPip(ExportPipWindow.PipState.Error, ex.Message);
            }
        }

        private static string MakeFileName(SheetRow row)
        {
            return MakeFileName(row, row.HamSacKalinligi);
        }

        // Ad uretimi tek yerde (DxfAdi): onizleme ve yerlesim de ayni adi
        // kurmak zorunda, yoksa dosyayi bulamazlar.
        private static string MakeFileName(SheetRow row, double hamSacKalinligi)
        {
            return DxfAdi.Uret(row.ProductName, hamSacKalinligi, row.Quantity);
        }

        private bool _ilkKayitYapildi = false;


        private async System.Threading.Tasks.Task<bool> ExportOne(object repRef, string fullPath)
        {
            if (_stopRequested) return false;

            dynamic catia = _catia;

        
            try { if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath); } catch { }

            // Onay kutulari export boyunca arka planda yanitlanir
            OnayIzleyiciBaslat();

            try
            {
                return await ExportOneIc(repRef, fullPath);
            }
            finally
            {
                OnayIzleyiciDurdur();
            }
        }

        private async System.Threading.Tasks.Task<bool> ExportOneIc(object repRef, string fullPath)
        {
            dynamic catia = _catia;

            // 1) parcayi yeni pencerede ac
            LogInfo("Parça Açılıyor...");
            dynamic svc = catia.ActiveEditor.GetService("PLMOpenService");
            object newEd = null;
            svc.PLMOpenInNewWindow(repRef, ref newEd);

            await System.Threading.Tasks.Task.Delay(2500);

            // Parca acildi: pip "Basladi" durumundan "Suruyor" durumuna gecsin
            if (_pip != null)
                _pip.SetStateKeepDetail(ExportPipWindow.PipState.Running);

            // CATIA ana penceresi: baslik surumden surume degisiyor
            // ("3DEXPERIENCE", "3DEXPERIENCE R2022x", belge adi eklenmis hali...).
            // Bu yuzden baslikla degil, surec adiyla bulunuyor.
            IntPtr hCatia = PencereAraclari.AnaPencere();
            if (hCatia == IntPtr.Zero) hCatia = FindWindow(null, "3DEXPERIENCE");

            if (hCatia == IntPtr.Zero)
                LogError("CATIA Ana Penceresi Bulunamadı — Odak Verilemeyebilir.");
            else
                LogInfo("CATIA Penceresi: \"" + PencereAraclari.BaslikMetni(hCatia) + "\"");

            IntPtr hSave = IntPtr.Zero;

            // 2-4) komut + Save As butonuna basma
            for (int deneme = 1; deneme <= 3 && hSave == IntPtr.Zero; deneme++)
            {
                if (_stopRequested) return false;

                LogInfo("DXF Komutu (Deneme " + deneme + ")...");

                ForceForeground(hCatia);
                await System.Threading.Tasks.Task.Delay(600);

                catia.StartCommand("Save As DXF");

                // Sheet metal uyarisi gibi kutular arka plandaki izleyici
                // tarafindan yanitlanir; burada sadece panelin acilmasi beklenir
                await System.Threading.Tasks.Task.Delay(Ayarlar.PanelBekleme);

                hSave = await SaveAsBas(hCatia, deneme);

                if (hSave == IntPtr.Zero)
                {
                    PressEscape();
                    await System.Threading.Tasks.Task.Delay(1500);
                }
            }



            // Acil durdurma: acik kalan kaydetme penceresini kapatip cik
            if (_stopRequested)
            {
                if (hSave != IntPtr.Zero)
                {
                    ForceForeground(hSave);
                    PressEscape();
                }
                return false;
            }

            // 5) tam yolu yaz ve kaydet
            ForceForeground(hSave);
            await System.Threading.Tasks.Task.Delay(600);

            SelectAll();
            SendText(fullPath);
            await System.Threading.Tasks.Task.Delay(400);
            PressEnter();

            // 6) dosya olusana kadar bekle
            LogInfo("Dosya Bekleniyor...");
            bool ok = await WaitForFile(fullPath, 20000);
            if (ok) _ilkKayitYapildi = true;

            // 7) pencereler kapansin, sonra parcayi kapat
            await WaitForNoSaveDialog(10000);
            await System.Threading.Tasks.Task.Delay(1000);

            try { catia.ActiveWindow.Close(); } catch { }

            bool geriDondu = await WaitForAssembly(15000);
            if (!geriDondu)
            {
                LogInfo("Montaja Dönülemedi, Bekleniyor...");
                await System.Threading.Tasks.Task.Delay(3000);
            }
            await System.Threading.Tasks.Task.Delay(1500);

            if (ok) LogSuccess("Yazıldı: " + fullPath);
            else LogError("Dosya Oluşmadı: " + fullPath);
            return ok;
        }

        // ================= SAVE AS BUTONUNA BASMA =================
        //
        // Panel kendi ciziyor: Windows'a ne UIA agaci ne de metinli alt
        // pencere veriyor, bu yuzden dugme nesne olarak tetiklenemiyor.
        // Klavye/Win32/UIA denemeleri kurulumdan kuruluma tutarsiz oldugu icin
        // kaldirildi; geriye gercek fare tiklamasi kaldi.
        //
        // Nokta once dugmenin goruntusunden aranir (bkz. SaveAsBulucu): panel
        // tasinmis ya da boyutu degismis olsa bile bulunur. Benzerlik esigin
        // altindaysa korlemesine tiklanmaz, ogretilmis koordinata donulur;
        // o da yoksa hic tiklanmaz.
        private bool SaveAsNoktasi(out int x, out int y, out bool gorseldenBulundu)
        {
            x = 0; y = 0;
            gorseldenBulundu = false;

            if (SaveAsBulucu.VarMi())
            {
                double skor;

                if (SaveAsBulucu.Bul(PencereAraclari.HedefPencere(), out x, out y, out skor))
                {
                    LogSuccess("Save As Düğmesi Görüntüden Bulundu — Benzerlik %" +
                               Math.Round(skor * 100) + ", Konum: " + x + ", " + y);

                    gorseldenBulundu = true;
                    return true;
                }

                LogInfo("Düğme Görüntüden Bulunamadı, Öğretilmiş Konuma Dönülüyor.");
            }

            if (!PencereAraclari.OgretilmisNokta(out x, out y))
            {
                LogError("Save As Konumu Öğretilmemiş — Ayarlar'dan Konumu Öğretin.");
                return false;
            }

            LogInfo("Öğretilmiş Konuma Tıklanıyor: " + x + ", " + y);
            return true;
        }

        private async System.Threading.Tasks.Task<IntPtr> SaveAsBas(IntPtr hCatia, int deneme)
        {
            int ox, oy;
            bool gorselden;

            if (!SaveAsNoktasi(out ox, out oy, out gorselden)) return IntPtr.Zero;

            // Goruntu henuz ogretilmemisse, tiklamadan hemen once dugmenin
            // resmi alinir. Panel su an acik oldugu icin dogru an burasi;
            // ornek yalnizca tiklama tutarsa saklanir.
            SaveAsBulucu.OrnekAdayi aday = null;

            if (!gorselden && !SaveAsBulucu.VarMi())
            {
                string adayHata;
                aday = SaveAsBulucu.AdayAl(ox, oy, out adayHata);
            }

            ClickAt(ox, oy);

            IntPtr h = await WaitForSaveDialog(6000);

            if (h != IntPtr.Zero)
            {
                // Tiklama tuttu: demek ki o goruntu gercekten Save As dugmesi
                if (aday != null && SaveAsBulucu.AdayiSakla(aday))
                    LogSuccess("Düğmenin Görüntüsü Öğrenildi — Bundan Sonra Panel " +
                               "Kaysa Bile Bulunacak.");

                return h;
            }

            LogError("Kaydetme Penceresi Açılmadı (Deneme " + deneme + ").");

            // Ekranda bekleyen bir onay kutusu var mi, butonlari nasil gorunuyor
            GorunurDiyaloglariYaz();

            return IntPtr.Zero;
        }

        // Odaktaki UI ogesinin adi ve ekrandaki dikdortgeni
        private static bool OdakBilgisi(out string ad, out Rect kutu)
        {
            ad = "";
            kutu = Rect.Empty;

            try
            {
                var el = AutomationElement.FocusedElement;
                if (el == null) return false;

                ad = (el.Current.Name ?? "").Trim();
                kutu = el.Current.BoundingRectangle;
                return !kutu.IsEmpty;
            }
            catch
            {
                return false;
            }
        }

        // Odaktaki ogeyi Invoke deseniyle calistirir (varsa)
        private static bool OdaktakiniInvokeEt()
        {
            try
            {
                var el = AutomationElement.FocusedElement;
                if (el == null) return false;

                object pat;
                if (!el.TryGetCurrentPattern(InvokePattern.Pattern, out pat)) return false;

                ((InvokePattern)pat).Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================= SAVE AS BULMA (UIA) =================

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        private const uint MOUSEEVENTF_LEFTUP = 0x04;

        // Sentetik sol tik (injected oldugu icin girdi kilidinden gecer)
        private static void ClickAt(int x, int y)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(60);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        // UIA agacinda adi "Save As" olan ogeyi arar; Invoke edemezse ortasina tiklar
        private static bool TryClickSaveAsUia(IntPtr hCatia)
        {
            var kokler = new List<IntPtr>();
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero) kokler.Add(fg);
            if (hCatia != IntPtr.Zero && !kokler.Contains(hCatia)) kokler.Add(hCatia);

            var adKosulu = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, "save as",
                    PropertyConditionFlags.IgnoreCase),
                new PropertyCondition(AutomationElement.NameProperty, "save as...",
                    PropertyConditionFlags.IgnoreCase),
                new PropertyCondition(AutomationElement.NameProperty, "farklı kaydet",
                    PropertyConditionFlags.IgnoreCase));

            foreach (IntPtr kok in kokler)
            {
                try
                {
                    var root = AutomationElement.FromHandle(kok);
                    var bulunan = root.FindAll(TreeScope.Descendants, adKosulu);

                    foreach (AutomationElement el in bulunan)
                    {
                        object pat;
                        if (el.TryGetCurrentPattern(InvokePattern.Pattern, out pat))
                        {
                            ((InvokePattern)pat).Invoke();
                            return true;
                        }

                        var r = el.Current.BoundingRectangle;
                        if (!r.IsEmpty && r.Width > 0)
                        {
                            ClickAt((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
                            return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        // Panel acikken pencere/UIA agacini dosyaya doker (teshis icin, bir kez)
        private static string DumpPanelTree(IntPtr hCatia)
        {
            var sb = new StringBuilder();
            uint catiaPid = 0;
            if (hCatia != IntPtr.Zero) GetWindowThreadProcessId(hCatia, out catiaPid);

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;

                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (catiaPid != 0 && pid != catiaPid) return true;

                sb.AppendLine("==== PENCERE: \"" + GetText(h) + "\"  class=" + GetCls(h));

                int n = 0;
                EnumChildWindows(h, (ch, l2) =>
                {
                    n++;
                    if (n > 300) return false;
                    sb.AppendLine("  [win32] class=" + GetCls(ch) +
                                  "  text=\"" + GetText(ch) + "\"");
                    return true;
                }, IntPtr.Zero);

                try
                {
                    var root = AutomationElement.FromHandle(h);
                    var butonlar = root.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                    int m = 0;
                    foreach (AutomationElement b in butonlar)
                    {
                        m++;
                        if (m > 250) break;
                        string ad = "";
                        try { ad = b.Current.Name; } catch { }
                        sb.AppendLine("  [uia-buton] \"" + ad + "\"");
                    }
                }
                catch { }

                sb.AppendLine();
                return true;
            }, IntPtr.Zero);

            string yol = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "macria_panel_dump_" + DateTime.Now.ToString("HHmmss") + ".txt");
            System.IO.File.WriteAllText(yol, sb.ToString());
            return yol;
        }

        private bool _dumpYazildi;

        // "&Save As" gibi hizlandirici isaretlerini temizler
        private static string CleanButtonText(string s)
        {
            return (s ?? "").Replace("&", "").Trim();
        }

        private static bool IsSaveAsText(string text)
        {
            string f = CleanButtonText(text).ToLowerInvariant();
            return f == "save as" || f == "save as..." || f == "saveas" ||
                   f == "farklı kaydet" || f == "farkli kaydet";
        }

        // Gorunur tum pencerelerin cocuklarinda "Save As" yazili butonu arar.
        // Once class'i Button olan tercih edilir; bulunamazsa ayni yazili
        // herhangi bir kontrol dondurulur.
        private static IntPtr FindSaveAsButton()
        {
            IntPtr adayButon = IntPtr.Zero;
            IntPtr adayDiger = IntPtr.Zero;

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;

                EnumChildWindows(h, (ch, l2) =>
                {
                    if (!IsWindowVisible(ch)) return true;
                    if (!IsSaveAsText(GetText(ch))) return true;

                    if (GetCls(ch).IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        adayButon = ch;
                        return false;
                    }

                    if (adayDiger == IntPtr.Zero) adayDiger = ch;
                    return true;
                }, IntPtr.Zero);

                return adayButon == IntPtr.Zero;
            }, IntPtr.Zero);

            return adayButon != IntPtr.Zero ? adayButon : adayDiger;
        }

        // ================= ONAY KUTULARI =================
        //
        // Parcanin son ozelligi sheet metal degilse (ornegin sonradan PartDesign
        // Pocket eklenmisse) "Save As DXF" komutu su soruyu sorar:
        //   "... doesn't contain last feature as sheet metal feature.
        //    The resulting dxf could be not pertinent. Do you want to generate it?"
        // Evet denmezse akis burada kilitlenir.

        private static readonly string[] OnayEvet =
        { "evet", "yes", "oui", "ja", "si", "sim", "tamam", "ok" };

        private static readonly string[] OnayHayir =
        { "hayır", "hayir", "no", "non", "nein", "nao", "hayır." };

        private static bool ButonMetniEsler(string text, string[] kume)
        {
            string f = CleanButtonText(text).ToLowerInvariant();
            if (f.Length == 0) return false;

            foreach (string k in kume)
                if (f == k) return true;

            return false;
        }

        private const int IDYES = 6;
        private const int IDNO = 7;
        private const uint WM_COMMAND = 0x0111;

        // Onay kutusu arar; bulursa Evet'e basar ve kutunun basligini dondurur.
        // Iki tespit yolu var:
        //   1) Klasik mesaj kutusu (#32770) — IDYES/IDNO kimlikleriyle
        //   2) Metinle — Evet ve Hayir yazili iki dugmeyi birlikte tasiyan pencere
        // Iki dugmeyi birden sart kosmak, tek basina "Tamam" tasiyan alakasiz
        // panellerin yanlislikla onaylanmasini onler.
        private static string TryConfirmDialog()
        {
            IntPtr hDlg = IntPtr.Zero;
            IntPtr hEvet = IntPtr.Zero;
            string baslik = "";

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;

                // 1) Standart mesaj kutusu kimlikleri
                if (GetCls(h) == "#32770")
                {
                    IntPtr yes = GetDlgItem(h, IDYES);
                    IntPtr no = GetDlgItem(h, IDNO);

                    if (yes != IntPtr.Zero && no != IntPtr.Zero)
                    {
                        hDlg = h;
                        hEvet = yes;
                        baslik = GetText(h);
                        return false;
                    }
                }

                // 2) Buton metinlerine gore
                IntPtr evet = IntPtr.Zero;
                bool hayirVar = false;

                EnumChildWindows(h, (ch, l2) =>
                {
                    if (!IsWindowVisible(ch)) return true;
                    if (GetCls(ch).IndexOf("Button", StringComparison.OrdinalIgnoreCase) < 0)
                        return true;

                    string t = GetText(ch);

                    if (evet == IntPtr.Zero && ButonMetniEsler(t, OnayEvet)) evet = ch;
                    else if (ButonMetniEsler(t, OnayHayir)) hayirVar = true;

                    return true;
                }, IntPtr.Zero);

                if (evet != IntPtr.Zero && hayirVar)
                {
                    hDlg = h;
                    hEvet = evet;
                    baslik = GetText(h);
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (hEvet == IntPtr.Zero) return null;

            SendMessage(hEvet, BM_CLICK, IntPtr.Zero, IntPtr.Zero);

            // BM_CLICK bazi kaliplarda yutulur; komutu diyaloga da bildir
            if (hDlg != IntPtr.Zero)
                PostMessage(hDlg, WM_COMMAND, (IntPtr)IDYES, hEvet);

            return baslik.Length > 0 ? baslik : "(başlıksız)";
        }

        // Export suresince arka planda calisip cikan onay kutularini yanitlar.
        // Kutunun ne zaman ciktigini onceden bilemedigimiz icin sabit bir
        // bekleme penceresi yerine surekli izleme kullanilir.
        private System.Threading.CancellationTokenSource _onayCts;

        private void OnayIzleyiciBaslat()
        {
            OnayIzleyiciDurdur();

            _onayCts = new System.Threading.CancellationTokenSource();
            var token = _onayCts.Token;

            System.Threading.Tasks.Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    string baslik = null;
                    try { baslik = TryConfirmDialog(); }
                    catch { }

                    if (baslik != null)
                    {
                        string b = baslik;
                        try { await Dispatcher.InvokeAsync(() => LogInfo("Onay Kutusu Yanıtlandı — Evet: " + b)); }
                        catch { }
                    }

                    try { await System.Threading.Tasks.Task.Delay(400, token); }
                    catch { return; }
                }
            });
        }

        private void OnayIzleyiciDurdur()
        {
            if (_onayCts == null) return;

            try { _onayCts.Cancel(); } catch { }
            _onayCts = null;
        }

        // Gorunur diyaloglari ve buton yazilarini konsola dokerek neyin
        // tespit edilemedigini gosterir
        private void GorunurDiyaloglariYaz()
        {
            var satirlar = new List<string>();

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;

                string cls = GetCls(h);
                if (cls != "#32770" && GetText(h).Length == 0) return true;

                var butonlar = new List<string>();

                EnumChildWindows(h, (ch, l2) =>
                {
                    if (!IsWindowVisible(ch)) return true;
                    if (GetCls(ch).IndexOf("Button", StringComparison.OrdinalIgnoreCase) < 0)
                        return true;

                    string t = CleanButtonText(GetText(ch));
                    if (t.Length > 0 && butonlar.Count < 8) butonlar.Add(t);

                    return true;
                }, IntPtr.Zero);

                if (butonlar.Count > 0 && satirlar.Count < 6)
                    satirlar.Add("\"" + GetText(h) + "\" [" + cls + "] → " +
                                 string.Join(" | ", butonlar));

                return true;
            }, IntPtr.Zero);

            if (satirlar.Count == 0)
            {
                LogInfo("Görünür Diyalog Bulunamadı (Butonlar Win32 Penceresi Değil).");
                return;
            }

            LogInfo("Görünür Diyaloglar:");
            foreach (string s in satirlar) LogInfo("   " + s);
        }

        // Acikca Iptal/Kapat benzeri bir buton adi mi?
        private static bool IsCancelName(string name)
        {
            if (name.Length == 0) return false;
            string f = name.ToLowerInvariant();
            return f.Contains("cancel") || f.Contains("iptal") ||
                   f.Contains("vazgeç") || f.Contains("vazgec") ||
                   f == "close" || f == "kapat" || f == "no" || f == "hayır" || f == "hayir";
        }

        private static void PressTab()
        {
            keybd_event(0x09, 0, 0, UIntPtr.Zero);
            keybd_event(0x09, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void PressSpace()
        {
            keybd_event(0x20, 0, 0, UIntPtr.Zero);
            keybd_event(0x20, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void PressEscape()
        {
            keybd_event(0x1B, 0, 0, UIntPtr.Zero);
            keybd_event(0x1B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // Windows kaydetme dialogunda dosya adi alanini bulur
        private static IntPtr FindFileNameEdit(IntPtr hDlg)
        {
            // once dogrudan
            IntPtr h = GetDlgItem(hDlg, 1001);
            if (h != IntPtr.Zero && GetCls(h) == "Edit") return h;

            // sonra ComboBox icindeki Edit
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(hDlg, (ch, l) =>
            {
                if (GetCls(ch) == "ComboBox")
                {
                    EnumChildWindows(ch, (cch, l2) =>
                    {
                        if (GetCls(cch) == "Edit")
                        {
                            found = cch;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                    if (found != IntPtr.Zero) return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero) return found;

            // son care: id'si 1001 olan herhangi bir Edit
            EnumChildWindows(hDlg, (ch, l) =>
            {
                if (GetCls(ch) == "Edit" && GetDlgCtrlID(ch) == 1001)
                {
                    found = ch;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        private async void btnExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (_exporting) return;
            if (!ExportIzinliMi()) return;

            if (_rows.Count == 0)
            {
                LogInfo("Önce Tarama Yapın.");
                return;
            }

            Dictionary<SheetRow, double> hamSacDegerleri;
            if (!TumHamSacGirdileriniDogrula(out hamSacDegerleri)) return;

            if (!FareUyarisiniGoster()) return;

            // klasoru bir kez sor
            var fd = new Microsoft.Win32.OpenFolderDialog();
            fd.Title = "Çıktı Klasörünü Seçin";
            if (fd.ShowDialog() != true) return;

            string folder = fd.FolderName;

            // Onizleme, aktarilmayan parcalari da bu klasorde arayabilsin
            Ayarlar.SonCiktiKlasoru = folder;
            Ayarlar.Kaydet();

            LogInfo("Toplu DXF Export Başladı: " + _rows.Count + " Parça");
            _stopRequested = false;
            ShowPipStart("Hazırlanıyor...");

            // Islem devam ettigi surece fiziksel girdi kilitli (Macria pencereleri haric)
            SetExporting(true);
            LogInfo("İşlem Sürerken Fare ve Klavyeye Dokunmayın — Odak Kayarsa Export Bozulur.");

            int ok = 0;
            var failed = new List<string>();

            try
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_stopRequested)
                    {
                        LogError("Toplu Export Durduruldu (" + i + "/" + _rows.Count + " Tamamlandı).");
                        break;
                    }

                    SheetRow row = _rows[i];

                    if (!_repRefs.ContainsKey(row.PartName) || _repRefs[row.PartName] == null)
                    {
                        failed.Add(row.PartName);
                        continue;
                    }

                    string path = System.IO.Path.Combine(
                        folder,
                        MakeFileName(row, hamSacDegerleri[row]));

                    LogInfo("(" + (i + 1) + "/" + _rows.Count + ") " + row.PartName);
                    ShowPip("(" + (i + 1) + "/" + _rows.Count + ") " + row.PartName);

                    try
                    {
                        bool done = await ExportOne(_repRefs[row.PartName], path);

                        if (done) { ok++; OnizlemeyeYaz(row, path); }
                        else if (!_stopRequested) failed.Add(row.PartName);
                    }
                    catch
                    {
                        failed.Add(row.PartName);
                    }

                    await System.Threading.Tasks.Task.Delay(800);
                }
            }
            catch (Exception ex)
            {
                LogError("Hata: " + ex.Message);
            }
            finally
            {
                SetExporting(false);
            }

            if (ok > 0)
                LogSuccess("Toplu Export Bitti — Başarılı: " + ok + " / " + _rows.Count);
            else
                LogError("Toplu Export Bitti — Başarılı: 0 / " + _rows.Count);
            if (failed.Count > 0)
                LogError("Başarısız: " + string.Join(", ", failed));

            string pipOzet = "Başarılı: " + ok + " / " + _rows.Count;
            if (_stopRequested)
                await FinishPip(ExportPipWindow.PipState.Stopped, pipOzet);
            else if (ok > 0)
                await FinishPip(ExportPipWindow.PipState.Done, pipOzet);
            else
                await FinishPip(ExportPipWindow.PipState.Error, pipOzet);

            // Toplu exportta tek tek dosya yerine cikti klasorunu ac
            if (ok > 0 && chkOpenAfter.IsChecked == true)
                OpenExported(folder);
        }

        private async System.Threading.Tasks.Task<IntPtr> WaitForSaveDialog(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                IntPtr found = IntPtr.Zero;

                EnumWindows((h, l) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    if (GetCls(h) != "#32770") return true;

                    // Kaydet butonu (id=1) yeterli sart
                    if (GetDlgItem(h, 1) != IntPtr.Zero)
                    {
                        found = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (found != IntPtr.Zero) return found;

                // Kaydet penceresi yerine bir onay kutusu cikmis olabilir
                string onay = TryConfirmDialog();
                if (onay != null)
                    LogInfo("Onay Kutusu Yanıtlandı — Evet: " + onay);

                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return IntPtr.Zero;
        }

        private static async System.Threading.Tasks.Task<bool> WaitForFile(string path, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                try { if (System.IO.File.Exists(path)) return true; } catch { }
                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return false;
        }

        private static bool SendText(string text)
        {
            if (!SafeSetClipboard(text)) return false;

            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(0x56, 0, 0, UIntPtr.Zero);
            keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            System.Threading.Thread.Sleep(200);
            return true;
        }
        private static void PressEnter()
        {
            keybd_event(0x0D, 0, 0, UIntPtr.Zero);
            keybd_event(0x0D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

    }
}
