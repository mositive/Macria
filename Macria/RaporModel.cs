using System;
using System.Collections.Generic;

namespace Macria
{
    // Disari aktarilacak tablonun bicimden bagimsiz tanimi. Excel ve PDF
    // yazicilari ayni modeli okur; boylece iki cikti da ayni sayilari gosterir.
    internal class RaporSutun
    {
        public string Ad = "";
        public double Genislik = 1;   // goreli sutun genisligi
        public bool Sayi;             // sagayasli ve sayi bicimli mi
        public int Ondalik = 2;       // Sayi ise basamak sayisi
        public bool Durum;            // basarili/basarisiz renklendirmesi
    }

    // Sayfanin ustundeki vurgulu ozet kutulari
    internal class RaporOzet
    {
        public string Baslik = "";
        public string Deger = "";
    }

    internal class Rapor
    {
        public string Baslik = "";
        public string AltBaslik = "";
        public DateTime Tarih = DateTime.Now;

        // Basligin altindaki parametre satirlari (malzeme, fiyatlar, kur...)
        public List<string> Bilgiler = new List<string>();

        public List<RaporOzet> Ozetler = new List<RaporOzet>();

        public List<RaporSutun> Sutunlar = new List<RaporSutun>();

        // Hucreler: string, double, null. null bos hucre demek.
        public List<object[]> Satirlar = new List<object[]>();

        // Kalin yazilan toplam satiri; yoksa null
        public object[] Toplam;
    }
}
