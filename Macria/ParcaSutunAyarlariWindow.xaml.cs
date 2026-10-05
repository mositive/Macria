using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Macria
{
    public partial class ParcaSutunAyarlariWindow : Window
    {
        private readonly ObservableCollection<ParcaSutunSatiri> _sutunlar =
            new ObservableCollection<ParcaSutunSatiri>();

        // Another table's columns (STEP / STP Analizi tabs); null: the parts table's.
        private readonly Func<List<ParcaSutunTanimi>>? _varsayilanlar;
        private readonly Func<List<ParcaSutunTanimi>, string?>? _kaydet;

        public ParcaSutunAyarlariWindow()
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            listeSutunlar.ItemsSource = _sutunlar;
            Yukle(ParcaSutunDeposu.Sutunlar);
        }

        /// <summary>The same editor for another table: `kaydet` returns null or the reason it failed.</summary>
        internal ParcaSutunAyarlariWindow(string baslik, string aciklama, List<ParcaSutunTanimi> sutunlar,
            Func<List<ParcaSutunTanimi>> varsayilanlar, Func<List<ParcaSutunTanimi>, string?> kaydet)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);
            Title = baslik;
            txtBaslik.Text = baslik;
            txtAciklama.Text = aciklama;
            _varsayilanlar = varsayilanlar;
            _kaydet = kaydet;
            listeSutunlar.ItemsSource = _sutunlar;
            Yukle(sutunlar);
        }

        private void Yukle(List<ParcaSutunTanimi> sutunlar)
        {
            _sutunlar.Clear();
            foreach (ParcaSutunTanimi s in sutunlar)
                _sutunlar.Add(new ParcaSutunSatiri(s.Kopya()));
        }

        private static ParcaSutunSatiri? Satir(object sender)
        {
            FrameworkElement? oge = sender as FrameworkElement;
            return oge == null ? null : oge.DataContext as ParcaSutunSatiri;
        }

        private void btnYukari_Click(object sender, RoutedEventArgs e)
        {
            ParcaSutunSatiri? satir = Satir(sender);
            int index = satir == null ? -1 : _sutunlar.IndexOf(satir);
            if (index > 0) _sutunlar.Move(index, index - 1);
        }

        private void btnAsagi_Click(object sender, RoutedEventArgs e)
        {
            ParcaSutunSatiri? satir = Satir(sender);
            int index = satir == null ? -1 : _sutunlar.IndexOf(satir);
            if (index >= 0 && index < _sutunlar.Count - 1)
                _sutunlar.Move(index, index + 1);
        }

        private void btnVarsayilan_Click(object sender, RoutedEventArgs e)
        {
            if (!OnayWindow.Sor(this, "Varsayılan Sütun Düzeni",
                    "Sütunların görünürlüğü ve sırası ilk ayarlarına dönecek.",
                    "Sıfırla"))
                return;

            Yukle(_varsayilanlar?.Invoke() ?? ParcaSutunDeposu.Varsayilanlar());
            txtDurum.Text = "";
        }

        private void btnKaydet_Click(object sender, RoutedEventArgs e)
        {
            var tanimlar = new List<ParcaSutunTanimi>();
            foreach (ParcaSutunSatiri s in _sutunlar)
                tanimlar.Add(s.Tanim());

            if (tanimlar.FindAll(s => s.Gorunur).Count == 0)
            {
                Uyar("En az bir sütun görünür olmalı.");
                return;
            }

            string hata = "";
            if (_kaydet != null ? (hata = _kaydet(tanimlar) ?? "").Length > 0 : !ParcaSutunDeposu.Kaydet(tanimlar, out hata))
            {
                Uyar("Sütun ayarları kaydedilemedi: " + hata);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Uyar(string mesaj)
        {
            txtDurum.Text = mesaj;
            System.Media.SystemSounds.Exclamation.Play();
        }

        private void Baslik_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btnVazgec_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class ParcaSutunSatiri
    {
        private readonly ParcaSutunTanimi _kaynak;

        internal ParcaSutunSatiri(ParcaSutunTanimi kaynak)
        {
            _kaynak = kaynak;
            Baslik = kaynak.Baslik;
            Gorunur = kaynak.Gorunur;
        }

        public string Baslik { get; set; }
        public bool Gorunur { get; set; }

        internal ParcaSutunTanimi Tanim()
        {
            ParcaSutunTanimi tanim = _kaynak.Kopya();
            tanim.Gorunur = Gorunur;
            return tanim;
        }
    }
}
