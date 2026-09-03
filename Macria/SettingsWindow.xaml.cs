using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Macria
{
    // Makineye ozel ayarlar penceresi. Asil isi "Konumu Ogret": kullanici
    // CATIA'da Save As butonunun uzerine gelip F8'e basar, Macria butonun
    // pencereye gore konumunu kaydeder ve bir daha klavyeye guvenmez.
    public partial class SettingsWindow : Window
    {
        private const int VK_F8 = 0x77;
        private CancellationTokenSource _ogretCts;

        // ogretmeyeBasla: rehberden gelindiginde F8 dinleyicisi kendiliginden acilir
        public SettingsWindow(bool ogretmeyeBasla = false)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            txtBekleme.Text = Ayarlar.PanelBekleme.ToString(CultureInfo.InvariantCulture);
            chkFareUyarisi.IsChecked = !Ayarlar.FareUyarisiGizle;
            chkBukumKapat.IsChecked = Ayarlar.BukumKapat;

            KonumYaz();
            BukumYaz();
            Closed += (s, e) => OgretmeyiDurdur();

            if (ogretmeyeBasla)
                Loaded += (s, e) => btnOgret_Click(btnOgret, null);
        }

        // Goruntu ogrenilemediyse sebebi burada tutulur
        private string _gorselNot = "";

        // Kayitli konumun tek satirlik ozeti
        private string DurumMetni()
        {
            if (!Ayarlar.KonumVar)
                return "Henüz öğretilmedi — export bu konum öğretilene kadar başlatılamaz.";

            string temel =
                "Öğretildi: pencere sol üstünden " +
                Ayarlar.Dx + ", " + Ayarlar.Dy + " piksel" +
                (Ayarlar.PencereSinifi.Length > 0
                    ? "  (" + Ayarlar.PencereSinifi + ")"
                    : "");

            if (_gorselNot.Length > 0) return temel + "\n" + _gorselNot;

            return temel + (SaveAsBulucu.VarMi()
                ? "\nDüğmenin görüntüsü de öğrenildi — panel taşınsa da bulunur."
                : "\nDüğme görüntüsü yok; yalnızca koordinata tıklanır.");
        }

        private void KonumYaz()
        {
            txtKonum.Text = DurumMetni();
            btnTemizle.IsEnabled = Ayarlar.KonumVar;
        }

        // ================= OGRETME =================

        // Save As dugmesi ile Bend Information kutusu ayni dongüyle
        // ogretiliyor: kullanici hedefin uzerine gelip F8'e basiyor.
        private async void btnOgret_Click(object sender, RoutedEventArgs e)
        {
            await Ogretme(false);
        }

        private async void btnBukumOgret_Click(object sender, RoutedEventArgs e)
        {
            await Ogretme(true);
        }

        private async Task Ogretme(bool bukum)
        {
            if (_ogretCts != null) { OgretmeyiDurdur(); return; }

            _ogretCts = new CancellationTokenSource();
            CancellationToken iptal = _ogretCts.Token;

            System.Windows.Controls.Button dugme = bukum ? btnBukumOgret : btnOgret;
            string eskiIcerik = bukum ? "Kutuyu Öğret" : "Konumu Öğret";

            string yonerge = bukum
                ? "CATIA'da paneli açın, fareyi işaretli \"Bend Information\" " +
                  "kutusunun üzerine getirin ve F8'e basın."
                : "CATIA'da paneli açın, fareyi Save As butonunun üzerine " +
                  "getirin ve F8'e basın.";

            dugme.Content = "Vazgeç";
            Topmost = true;

            // Vazgecildiginde yonergenin ekranda kalmamasi icin:
            // sonuc yazilmadan cikildiysa durum satiri eski haline doner
            bool sonucYazildi = false;

            try
            {
                for (int kalan = 60; kalan > 0; kalan--)
                {
                    Yaz(bukum, yonerge + "  (" + kalan + " sn)");

                    for (int i = 0; i < 10; i++)
                    {
                        if (iptal.IsCancellationRequested) return;

                        if ((PencereAraclari.GetAsyncKeyState(VK_F8) & 0x8000) != 0)
                        {
                            if (bukum) BukumuKaydet();
                            else NoktayiKaydet();

                            sonucYazildi = true;
                            return;
                        }

                        await Task.Delay(100, iptal);
                    }
                }

                Yaz(bukum, "Süre doldu, F8'e basılmadı.  " +
                           (bukum ? BukumDurumMetni() : DurumMetni()));

                sonucYazildi = true;
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_ogretCts != null) { _ogretCts.Dispose(); _ogretCts = null; }
                dugme.Content = eskiIcerik;
                Topmost = false;

                if (!sonucYazildi)
                {
                    if (bukum) BukumYaz();
                    else KonumYaz();
                }
            }
        }

        private void Yaz(bool bukum, string metin)
        {
            if (bukum) txtBukum.Text = metin;
            else txtKonum.Text = metin;
        }

        private void NoktayiKaydet()
        {
            PencereAraclari.POINT p;
            if (!PencereAraclari.GetCursorPos(out p))
            {
                txtKonum.Text = "Fare konumu okunamadı.";
                return;
            }

            IntPtr kok = PencereAraclari.KokPencere(PencereAraclari.WindowFromPoint(p));
            if (kok == IntPtr.Zero)
            {
                txtKonum.Text = "İmlecin altında bir pencere bulunamadı.";
                return;
            }

            PencereAraclari.RECT r;
            if (!PencereAraclari.GetWindowRect(kok, out r))
            {
                txtKonum.Text = "Pencere ölçüsü okunamadı.";
                return;
            }

            Ayarlar.PencereSinifi = PencereAraclari.SinifAdi(kok);
            Ayarlar.Dx = p.X - r.Left;
            Ayarlar.Dy = p.Y - r.Top;
            Ayarlar.PencereGenislik = r.Right - r.Left;
            Ayarlar.PencereYukseklik = r.Bottom - r.Top;
            Ayarlar.KonumVar = true;
            Ayarlar.Kaydet();

            // Koordinatin yaninda dugmenin goruntusu de saklanir; panel
            // tasinir ya da boyutu degisirse arama bunun uzerinden yapilir
            string gorselHata;

            if (!SaveAsBulucu.Ogret(p.X, p.Y, out gorselHata))
            {
                SaveAsBulucu.Sil();
                _gorselNot = "Görüntü öğrenilemedi: " + gorselHata;
            }
            else
            {
                _gorselNot = "";
            }

            KonumYaz();
            Activate();
        }

        // ================= BUKUM KUTUSU =================

        private string _bukumNot = "";

        private string BukumDurumMetni()
        {
            if (_bukumNot.Length > 0) return _bukumNot;

            return BukumBulucu.VarMi()
                ? "Öğretildi — kutu her export öncesi aranıp, işaretliyse kaldırılıyor."
                : "Henüz öğretilmedi — kutuya dokunulmuyor.";
        }

        private void BukumYaz()
        {
            txtBukum.Text = BukumDurumMetni();
            btnBukumTemizle.IsEnabled = BukumBulucu.VarMi();
        }

        private void BukumuKaydet()
        {
            PencereAraclari.POINT p;

            if (!PencereAraclari.GetCursorPos(out p))
            {
                txtBukum.Text = "Fare konumu okunamadı.";
                return;
            }

            string hata;

            if (!BukumBulucu.Ogret(p.X, p.Y, out hata))
            {
                BukumBulucu.Sil();
                _bukumNot = "Öğrenilemedi: " + hata;
            }
            else
            {
                _bukumNot = "";
            }

            BukumYaz();
            Activate();
        }

        private void btnBukumTemizle_Click(object sender, RoutedEventArgs e)
        {
            BukumBulucu.Sil();

            _bukumNot = "";
            BukumYaz();
        }

        private void OgretmeyiDurdur()
        {
            if (_ogretCts == null) return;
            try { _ogretCts.Cancel(); } catch { }
        }

        private void btnTemizle_Click(object sender, RoutedEventArgs e)
        {
            Ayarlar.KonumuTemizle();
            SaveAsBulucu.Sil();

            _gorselNot = "";
            KonumYaz();
        }

        // ================= KAYDET / KAPAT =================

        private void btnKaydet_Click(object sender, RoutedEventArgs e)
        {
            Ayarlar.PanelBekleme = Sayi(txtBekleme.Text, Ayarlar.PanelBekleme, 500, 30000);
            Ayarlar.FareUyarisiGizle = chkFareUyarisi.IsChecked != true;
            Ayarlar.BukumKapat = chkBukumKapat.IsChecked == true;
            Ayarlar.Kaydet();

            DialogResult = true;
            Close();
        }

        private static int Sayi(string s, int varsayilan, int enAz, int enCok)
        {
            int d;
            if (!int.TryParse((s ?? "").Trim(), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out d))
                return varsayilan;

            if (d < enAz) d = enAz;
            if (d > enCok) d = enCok;
            return d;
        }

        private void SadeceSayi(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (c < '0' || c > '9') { e.Handled = true; return; }
        }

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
