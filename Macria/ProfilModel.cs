using System;
using System.ComponentModel;

namespace Macria
{
    /// <summary>
    /// Kutu profil deneysel taramasinda ekranda gosterilen satir.
    /// Bu model kesin imalat karari vermez; geometri kanitlarini ve kullanicinin
    /// secebilecegi STEP cikti durumunu bir arada tutar.
    /// </summary>
    public sealed class ProfilRow : INotifyPropertyChanged
    {
        private string _durumKodu = "Bekliyor";
        private string _durum = "Teşhis bekliyor";
        private string _durumAciklamasi =
            "Kesin karar için satırı seçip Profil Teşhisi düğmesine basın.";
        private string _sinirOlculeri = "—";
        private string _tahminiEt = "—";
        private string _ataletOrani = "—";
        private string _yuzSayisi = "—";
        private string _featureKaniti = "—";
        private int _guven;
        private string _not = "";
        private string _stepDurumu = "";
        private string _stepAciklamasi = "";
        private int _bodyCount;
        private string _bodyNames = "";

        public string ProductName { get; set; } = "";
        public string PartName { get; set; } = "";
        public string ReferenceName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Revision { get; set; } = "";
        public int Quantity { get; set; }
        public int BodyCount
        {
            get { return _bodyCount; }
            set
            {
                if (_bodyCount == value) return;
                _bodyCount = value;
                Degisti(nameof(BodyCount));
                Degisti(nameof(CokluBodyMi));
            }
        }

        public string BodyNames
        {
            get { return _bodyNames; }
            set { DegerAyarla(ref _bodyNames, value, nameof(BodyNames)); }
        }
        public bool GizliPhysicalProductMu { get; set; }

        // WPF tablosuna baglanmaz; STEP ve detayli teshis sirasinda kullanilir.
        internal object? RepRef { get; set; }

        public string DurumKodu
        {
            get { return _durumKodu; }
            private set
            {
                if (_durumKodu == value) return;
                _durumKodu = value ?? "";
                Degisti(nameof(DurumKodu));
                Degisti(nameof(KuvvetliAdayMi));
                Degisti(nameof(HataMi));
                Degisti(nameof(CokluBodyMi));
            }
        }

        public string Durum
        {
            get { return _durum; }
            private set { DegerAyarla(ref _durum, value, nameof(Durum)); }
        }

        public string DurumAciklamasi
        {
            get { return _durumAciklamasi; }
            private set { DegerAyarla(ref _durumAciklamasi, value, nameof(DurumAciklamasi)); }
        }

        public string SinirOlculeri
        {
            get { return _sinirOlculeri; }
            private set { DegerAyarla(ref _sinirOlculeri, value, nameof(SinirOlculeri)); }
        }

        public string TahminiEt
        {
            get { return _tahminiEt; }
            private set { DegerAyarla(ref _tahminiEt, value, nameof(TahminiEt)); }
        }

        public string AtaletOrani
        {
            get { return _ataletOrani; }
            private set { DegerAyarla(ref _ataletOrani, value, nameof(AtaletOrani)); }
        }

        public string YuzSayisi
        {
            get { return _yuzSayisi; }
            private set { DegerAyarla(ref _yuzSayisi, value, nameof(YuzSayisi)); }
        }

        public string FeatureKaniti
        {
            get { return _featureKaniti; }
            private set { DegerAyarla(ref _featureKaniti, value, nameof(FeatureKaniti)); }
        }

        public int Guven
        {
            get { return _guven; }
            private set
            {
                int yeni = Math.Max(0, Math.Min(95, value));
                if (_guven == yeni) return;
                _guven = yeni;
                Degisti(nameof(Guven));
                Degisti(nameof(GuvenDisplay));
            }
        }

        public string GuvenDisplay
        {
            get { return Guven <= 0 ? "—" : "%" + Guven; }
        }

        public string Not
        {
            get { return _not; }
            private set { DegerAyarla(ref _not, value, nameof(Not)); }
        }

        public string StepDurumu
        {
            get { return _stepDurumu; }
            private set { DegerAyarla(ref _stepDurumu, value, nameof(StepDurumu)); }
        }

        public string StepAciklamasi
        {
            get { return _stepAciklamasi; }
            private set { DegerAyarla(ref _stepAciklamasi, value, nameof(StepAciklamasi)); }
        }

        public bool KuvvetliAdayMi { get { return DurumKodu == "Kuvvetli"; } }
        public bool HataMi { get { return DurumKodu == "Hata"; } }
        public bool CokluBodyMi { get { return BodyCount > 1 || DurumKodu == "CokluBody"; } }

        internal void HizliTeshis(string kod, string durum, string aciklama,
                                  string featureKaniti, int guven, string not)
        {
            DurumKodu = kod;
            Durum = durum;
            DurumAciklamasi = aciklama;
            FeatureKaniti = string.IsNullOrWhiteSpace(featureKaniti) ? "—" : featureKaniti;
            Guven = guven;
            Not = not ?? "";
        }

        internal void TeshisBasladi()
        {
            DurumKodu = "Isleniyor";
            Durum = "İnceleniyor…";
            DurumAciklamasi = "CATIA geometrisi okunuyor.";
        }

        internal void TeshisTamamlandi(
            string kod, string durum, string aciklama, int guven,
            string sinirOlculeri, string tahminiEt, string ataletOrani,
            string yuzSayisi, string featureKaniti, string not)
        {
            DurumKodu = kod;
            Durum = durum;
            DurumAciklamasi = aciklama;
            Guven = guven;
            SinirOlculeri = BosIseCizgi(sinirOlculeri);
            TahminiEt = BosIseCizgi(tahminiEt);
            AtaletOrani = BosIseCizgi(ataletOrani);
            YuzSayisi = BosIseCizgi(yuzSayisi);
            FeatureKaniti = BosIseCizgi(featureKaniti);
            Not = not ?? "";
        }

        internal void TeshisHatasi(string neden)
        {
            DurumKodu = "Hata";
            Durum = "Teşhis başarısız";
            DurumAciklamasi = string.IsNullOrWhiteSpace(neden)
                ? "Profil geometrisi okunamadı."
                : neden.Trim();
            Not = DurumAciklamasi;
        }

        internal void StepBasladi()
        {
            StepDurumu = "Kaydediliyor…";
            StepAciklamasi = "STEP dışa aktarma işlemi sürüyor.";
        }

        internal void StepBasarili(string yol)
        {
            StepDurumu = "✓ Kaydedildi";
            StepAciklamasi = string.IsNullOrWhiteSpace(yol)
                ? "STEP başarıyla oluşturuldu."
                : "STEP başarıyla oluşturuldu: " + yol;
        }

        internal void StepBasarisiz(string neden)
        {
            StepDurumu = "Başarısız";
            StepAciklamasi = string.IsNullOrWhiteSpace(neden)
                ? "STEP oluşturulamadı; konsoldaki teşhis adımlarını kontrol edin."
                : neden.Trim();
        }

        private static string BosIseCizgi(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        private void DegerAyarla(ref string alan, string value, string ad)
        {
            string yeni = value ?? "";
            if (alan == yeni) return;
            alan = yeni;
            Degisti(ad);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Degisti(string ad)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ad));
        }
    }
}
