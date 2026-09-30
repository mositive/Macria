using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Macria
{
    // Eski "Akilli Renklendirme" ekrani kaldirildi. Buradaki yardimcilar
    // (referans anahtari, yaprak toplama, kilit, COM hata ayrimi) Renklendirme
    // 2.0 ve renk teshisleri tarafindan kullaniliyor.
    public partial class MainWindow
    {
        private readonly AkilliRenklendirmeKilidi _akilliRenklendirmeKilidi =
            new AkilliRenklendirmeKilidi();

        private static async Task<List<object>> AkilliRenklendirmeYapraklariniTopla(dynamic root)
        {
            var sonuc = new List<object>();
            var yigin = new Stack<object>();
            dynamic kokAltlar = root.Occurrences;
            int kokAltSayisi = Convert.ToInt32(kokAltlar.Count);

            for (int i = kokAltSayisi; i >= 1; i--)
                yigin.Push((object)kokAltlar.Item(i));

            int islenen = 0;
            while (yigin.Count > 0)
            {
                object occurrence = yigin.Pop();
                dynamic dugum = occurrence;
                int altSayisi = AkilliRenklendirmeAltOccurrenceSayisi(dugum);

                if (altSayisi == 0)
                {
                    sonuc.Add(occurrence);
                }
                else
                {
                    dynamic altlar = dugum.Occurrences;
                    for (int i = altSayisi; i >= 1; i--)
                        yigin.Push((object)altlar.Item(i));
                }

                islenen++;
                if (islenen % 25 == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }

            return sonuc;
        }

        private static string? AkilliRenklendirmeReferansAnahtari(object occurrence)
        {
            dynamic dugum = occurrence;
            object? instance = dugum.PLMEntity;
            if (instance == null) return null;

            object? referans = ((dynamic)instance).ReferenceInstanceOf;
            if (referans == null) return null;

            string externalId = AkilliRenklendirmePlmDegeri(referans, "PLM_ExternalID");
            string version = AkilliRenklendirmePlmDegeri(referans, "V_version");
            if (string.IsNullOrWhiteSpace(version))
                version = AkilliRenklendirmePlmDegeri(referans, "revision");

            return AkilliRenklendirmeMantigi.ReferansAnahtari(externalId, version);
        }

        private static int AkilliRenklendirmeAltOccurrenceSayisi(dynamic occurrence)
        {
            try
            {
                dynamic altlar = occurrence.Occurrences;
                return altlar == null ? 0 : Math.Max(0, Convert.ToInt32(altlar.Count));
            }
            catch (Exception ex)
            {
                if (KritikComHatasiMi(ex)) throw;
                return 0;
            }
        }

        private static string AkilliRenklendirmePlmDegeri(object referans, string alan)
        {
            try
            {
                object? deger = ((dynamic)referans).GetAttributeValue(alan);
                string? metin = Convert.ToString(deger);
                if (!string.IsNullOrWhiteSpace(metin)) return metin.Trim();
            }
            catch (Exception ex)
            {
                if (KritikComHatasiMi(ex)) throw;
            }

            try
            {
                dynamic d = referans;
                object? deger = alan switch
                {
                    "PLM_ExternalID" => d.PLM_ExternalID,
                    "V_version" => d.V_version,
                    "revision" => d.revision,
                    _ => null
                };
                return Convert.ToString(deger)?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                if (KritikComHatasiMi(ex)) throw;
                return "";
            }
        }

        private static string OccurrenceAdi(object occurrence)
        {
            try
            {
                string? ad = Convert.ToString(((dynamic)occurrence).Name);
                return string.IsNullOrWhiteSpace(ad) ? "(adsız occurrence)" : ad.Trim();
            }
            catch { return "(adsız occurrence)"; }
        }

        private static bool KritikComHatasiMi(Exception ex)
        {
            var com = ex as COMException;
            if (com == null) return false;

            uint kod = unchecked((uint)com.HResult);
            return kod == 0x80010108 || // RPC_E_DISCONNECTED
                   kod == 0x800706BA || // RPC server unavailable
                   kod == 0x800401FD;   // CO_E_OBJNOTCONNECTED
        }
    }
}
