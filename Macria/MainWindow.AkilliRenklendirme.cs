using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Macria
{
    public partial class MainWindow
    {
        private readonly AkilliRenklendirmeKilidi _akilliRenklendirmeKilidi =
            new AkilliRenklendirmeKilidi();

        private async void btnAkilliRenklendirme_Click(object sender, RoutedEventArgs e)
        {
            if (!_akilliRenklendirmeKilidi.Baslat()) return;

            if (!OnayWindow.Sor(
                    this,
                    "Akıllı Renklendirme",
                    "Aktif CATIA montajındaki parçaların görünüm renkleri değiştirilecek. Devam edilsin mi?",
                    "Devam Et"))
            {
                _akilliRenklendirmeKilidi.Bitir();
                return;
            }

            btnAkilliRenklendirme.IsEnabled = false;
            var sure = System.Diagnostics.Stopwatch.StartNew();
            LogInfo("Akıllı Renklendirme Başlatıldı.");

            try
            {
                object? catiaObj = GetCatia();
                if (catiaObj == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic catia = catiaObj;
                dynamic editor = catia.ActiveEditor;
                dynamic root = editor.ActiveObject;
                if (!HasOccurrences(root))
                    throw new InvalidOperationException("Aktif CATIA nesnesi bir Physical Product montajı değil.");

                dynamic selection = editor.Selection;
                List<object> oncekiSecim = SecimiSakla(selection);

                try
                {
                    List<object> yapraklar = await AkilliRenklendirmeYapraklariniTopla(root);
                    AkilliRenklendirmeSonucu sonuc = AkilliRenklendirmeMantigi.Isle(
                        yapraklar,
                        AkilliRenklendirmeReferansAnahtari,
                        (occurrence, renk) => OccurrenceBoya(selection, occurrence, renk),
                        (occurrence, ex) => LogError(
                            "Akıllı Renklendirme — " + OccurrenceAdi(occurrence) +
                            ": " + Kisa(ex.Message)),
                        KritikComHatasiMi);

                    LogSuccess(
                        "Akıllı Renklendirme tamamlandı.\n" +
                        "Benzersiz parça: " + sonuc.BenzersizReferansSayisi + "\n" +
                        "Boyanan örnek: " + sonuc.BoyananOccurrenceSayisi + "\n" +
                        "Atlanan: " + sonuc.AtlananSayisi + "\n" +
                        "Hata: " + sonuc.HataSayisi);
                }
                finally
                {
                    SecimiGeriYukle(selection, oncekiSecim);
                }
            }
            catch (Exception ex)
            {
                LogError("Akıllı Renklendirme tamamlanamadı: " + Kisa(ex.Message));
            }
            finally
            {
                btnAkilliRenklendirme.IsEnabled = true;
                _akilliRenklendirmeKilidi.Bitir();
                IslemSuresiniYaz("Akıllı Renklendirme", sure);
            }
        }

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

        private static void OccurrenceBoya(
            dynamic selection, object occurrence, AkilliRenk renk)
        {
            try
            {
                selection.Clear();
                selection.Add(occurrence);
                dynamic visualProperties = selection.VisProperties;
                visualProperties.SetRealColor(
                    (int)renk.Kirmizi, (int)renk.Yesil, (int)renk.Mavi, 1);
            }
            finally
            {
                try { selection.Clear(); } catch { }
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
