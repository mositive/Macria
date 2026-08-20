using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using Microsoft.Win32;

namespace Macria
{
    internal enum DiagLevel { Info, Success, Error }

    internal class DiagLine
    {
        public string Text;
        public DiagLevel Level;
        public DiagLine(string text, DiagLevel level) { Text = text; Level = level; }
    }

    // CATIA'ya COM ile baglanma ve baglanti kurulamadiginda nedenini tespit etme.
    //
    // Onemli: GetActiveObject calisan ornegi ROT'tan (Running Object Table) alir ve
    // bunun icin ProgID kaydina ihtiyaci yoktur; yalnizca CLSID yeter. CATIA kendini
    // ROT'a CLSID ile kaydettigi icin, kayit defterinde CATIA.Application ProgID'si
    // hic olmasa bile sabit CLSID ile baglanti kurulabilir.
    internal static class CatiaConnect
    {
        // Dassault Systemes CATIA / 3DEXPERIENCE Application COM sinifi.
        // HKLM\SOFTWARE\Classes\CATIA.Application\CLSID degerinden alindi.
        private static readonly Guid[] BilinenClsidler =
        {
            new Guid("87FD6F40-E252-11D5-8040-0010B5FA1031"),
        };

        private static readonly string[] BilinenProgIdler =
        {
            "CATIA.Application",
            "CATIA.Application.1",
        };

        [DllImport("oleaut32.dll")]
        private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgID(
            [MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid lpclsid);

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        private static DiagLine Info(string t) { return new DiagLine(t, DiagLevel.Info); }
        private static DiagLine Ok(string t) { return new DiagLine(t, DiagLevel.Success); }
        private static DiagLine Err(string t) { return new DiagLine(t, DiagLevel.Error); }

        private static void Yaz(List<DiagLine> log, DiagLine satir)
        {
            if (log != null) log.Add(satir);
        }

        // ================= BAGLANTI =================

        // log null olabilir; verilirse her adimda ne oldugu yazilir.
        public static object Connect(List<DiagLine> log)
        {
            // 1) Kayitli ProgID uzerinden (normal makinede burasi calisir)
            foreach (string progId in BilinenProgIdler)
            {
                Guid g;
                int hr = CLSIDFromProgID(progId, out g);
                if (hr < 0)
                {
                    Yaz(log, Info("ProgID Çözülemedi (" + progId + ") — " + Aciklama(hr)));
                    continue;
                }

                object o = Dene(g, "ProgID " + progId, log);
                if (o != null) return o;
            }

            // 2) Kayit defteri eksik olsa bile ROT sabit CLSID ile sorgulanabilir
            foreach (Guid g in BilinenClsidler)
            {
                object o = Dene(g, "Sabit CLSID " + g.ToString("B").ToUpperInvariant(), log);
                if (o != null) return o;
            }

            // 3) Son care: ROT'taki tum kayitlari tarayip CATIA nesnesini bul
            return RotTara(log);
        }

        private static object Dene(Guid clsid, string kaynak, List<DiagLine> log)
        {
            object obj;
            int hr = GetActiveObject(ref clsid, IntPtr.Zero, out obj);

            if (hr >= 0 && obj != null)
            {
                Yaz(log, Ok("CATIA'ya Bağlanıldı — " + kaynak + UygulamaAdi(obj)));
                return obj;
            }

            Yaz(log, Info(kaynak + " Başarısız — " + Aciklama(hr)));
            return null;
        }

        // Baglanilan uygulamanin adini parantez icinde dondurur (okunamazsa bos)
        private static string UygulamaAdi(object o)
        {
            try
            {
                dynamic d = o;
                string ad = Convert.ToString(d.Name);
                if (!string.IsNullOrEmpty(ad)) return " (" + ad + ")";
            }
            catch { }

            return "";
        }

        // ROT'taki her kaydi tek tek deneyip CATIA uygulama nesnesini bulur.
        // CATIA kendini "!{CLSID}" bicimli bir item moniker ile kaydettigi icin
        // gorunen adda "CATIA" gecmeyebilir; bu yuzden ada gore filtrelenmez.
        private static object RotTara(List<DiagLine> log)
        {
            IRunningObjectTable rot;
            IBindCtx ctx;

            int hr = GetRunningObjectTable(0, out rot);
            if (hr < 0 || rot == null)
            {
                Yaz(log, Err("Çalışan Nesne Tablosu (ROT) Okunamadı — " + Aciklama(hr)));
                return null;
            }

            if (CreateBindCtx(0, out ctx) < 0 || ctx == null) return null;

            IEnumMoniker sayac;
            rot.EnumRunning(out sayac);
            if (sayac == null) return null;

            var monikers = new IMoniker[1];
            var isimler = new List<string>();
            object bulunan = null;

            while (sayac.Next(1, monikers, IntPtr.Zero) == 0)
            {
                string ad;
                try { monikers[0].GetDisplayName(ctx, null, out ad); }
                catch { ad = "(adı okunamadı)"; }

                isimler.Add(ad ?? "");

                if (bulunan != null) continue;

                object o;
                try { rot.GetObject(monikers[0], out o); }
                catch { continue; }
                if (o == null) continue;

                object app = UygulamayaCik(o);
                if (app != null)
                {
                    bulunan = app;
                    Yaz(log, Ok("CATIA'ya Bağlanıldı — ROT Taraması: " + ad));
                }
            }

            if (bulunan == null)
            {
                if (isimler.Count == 0)
                {
                    Yaz(log, Err("ROT Boş — Bu Oturumda Hiçbir COM Nesnesi Kayıtlı Değil."));
                }
                else
                {
                    Yaz(log, Err("ROT'ta " + isimler.Count + " Kayıt Var, Hiçbiri CATIA Değil:"));
                    int n = Math.Min(isimler.Count, 40);
                    for (int i = 0; i < n; i++) Yaz(log, Info("   - " + isimler[i]));
                    if (isimler.Count > n)
                        Yaz(log, Info("   ... ve " + (isimler.Count - n) + " Kayıt Daha"));
                }
            }

            return bulunan;
        }

        // ROT'tan gelen nesne CATIA uygulamasi mi, yoksa CATIA belgesi mi?
        // Her iki halde de uygulama nesnesini dondurur; alakasiz nesnelerde null.
        private static object UygulamayaCik(object o)
        {
            if (CatiaMi(o)) return o;

            // Belge nesnesi olabilir; uzerinden uygulamaya cik
            try
            {
                dynamic d = o;
                object app = d.Application;
                if (app != null && CatiaMi(app)) return app;
            }
            catch { }

            return null;
        }

        // Word/Excel gibi baska COM sunucularini CATIA sanmamak icin
        // CATIA'ya ozgu uyeler uzerinden dogrulama yapilir.
        private static bool CatiaMi(object o)
        {
            // Application.Name V5'te "CATIA", 3DEXPERIENCE kurulumunda "3DEXPERIENCE" doner
            string[] uygulamaAdlari = { "CATIA", "3DEXPERIENCE" };

            dynamic d = o;

            try
            {
                string ad = Convert.ToString(d.Name);
                if (!string.IsNullOrEmpty(ad))
                {
                    foreach (string beklenen in uygulamaAdlari)
                        if (ad.IndexOf(beklenen, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                }
            }
            catch { }

            // SystemService yalnizca CATIA/3DEXPERIENCE Application nesnesinde bulunur
            try
            {
                object svc = d.SystemService;
                if (svc != null) return true;
            }
            catch { }

            return false;
        }

        // ================= TESHIS =================

        public static List<DiagLine> Teshis()
        {
            var l = new List<DiagLine>();
            l.Add(Info("-------- Bağlantı Teşhisi --------"));

            int benimOturum = -1;

            // 1) Macria tarafi
            try
            {
                var kimlik = WindowsIdentity.GetCurrent();
                bool yonetici = new WindowsPrincipal(kimlik).IsInRole(WindowsBuiltInRole.Administrator);
                benimOturum = Process.GetCurrentProcess().SessionId;

                l.Add(Info("Macria: " + (IntPtr.Size == 8 ? "64-bit" : "32-bit") +
                           ", Oturum " + benimOturum +
                           ", Yönetici: " + (yonetici ? "Evet" : "Hayır") +
                           ", Kullanıcı: " + kimlik.Name));
            }
            catch (Exception ex) { l.Add(Info("Macria Bilgisi Alınamadı: " + ex.Message)); }

            // 2) CATIA surecleri (isim tam eslesme yerine icerme ile aranir;
            //    3DEXPERIENCE kurulumunda surec adi CNEXT degil 3DEXPERIENCE olabilir)
            SurecleriYaz(l, benimOturum);

            // 3) COM kaydi - hangi kovanda oldugu ayri ayri kontrol edilir
            KayitKontrol(l);

            // 4) COM/DCOM politikasi
            PolitikaKontrol(l);

            l.Add(Info("----------------------------------"));
            return l;
        }

        private static void SurecleriYaz(List<DiagLine> l, int benimOturum)
        {
            string[] parcalar = { "CNEXT", "CATIA", "3DEXPERIENCE", "DSLauncher", "CATSTART" };
            int sayi = 0;

            Process[] hepsi;
            try { hepsi = Process.GetProcesses(); }
            catch (Exception ex)
            {
                l.Add(Err("Süreç Listesi Alınamadı: " + ex.Message));
                return;
            }

            foreach (var p in hepsi)
            {
                string ad;
                try { ad = p.ProcessName; }
                catch { continue; }

                bool eslesti = false;
                foreach (string parca in parcalar)
                {
                    if (ad.IndexOf(parca, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        eslesti = true;
                        break;
                    }
                }
                if (!eslesti) continue;

                sayi++;

                string yol = "";
                bool erisimYok = false;
                try { yol = p.MainModule.FileName; }
                catch { erisimYok = true; }

                int oturum = -1;
                try { oturum = p.SessionId; }
                catch { }

                l.Add(Info("Süreç: " + ad + ".exe (PID " + p.Id + ", Oturum " + oturum + ")" +
                           (erisimYok ? "" : " -> " + yol)));

                if (erisimYok)
                    l.Add(Err("   > Bu Sürece Erişilemiyor. CATIA Büyük İhtimalle Yönetici Olarak " +
                              "Çalışıyor; Farklı Yetki Seviyesinde ROT Görünmez."));

                if (oturum >= 0 && benimOturum >= 0 && oturum != benimOturum)
                    l.Add(Err("   > CATIA Farklı Windows Oturumunda Çalışıyor; COM ile Görülemez."));
            }

            if (sayi == 0)
                l.Add(Err("Çalışan CATIA Süreci Bulunamadı (CNEXT / CATIA / 3DEXPERIENCE). " +
                          "CATIA Gerçekten Açık mı?"));
        }

        private static void KayitKontrol(List<DiagLine> l)
        {
            bool bulundu = false;

            bulundu |= KovanaBak(l, RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM 64-bit");
            bulundu |= KovanaBak(l, RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM 32-bit");
            bulundu |= KovanaBak(l, RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU");

            if (!bulundu)
                l.Add(Err("CATIA.Application ProgID'si Hiçbir Kovanda Kayıtlı Değil. " +
                          "CATIA Kurulumu COM Kaydını Yapamamış (HKLM Yazma İzni Gerekir). " +
                          "Bu Tek Başına Engel Değildir; Sabit CLSID ile Bağlanmayı Denedik."));
        }

        private static bool KovanaBak(List<DiagLine> l, RegistryHive hive, RegistryView view, string etiket)
        {
            bool bulundu = false;

            try
            {
                using (var kok = RegistryKey.OpenBaseKey(hive, view))
                {
                    foreach (string progId in BilinenProgIdler)
                    {
                        using (var k = kok.OpenSubKey(@"SOFTWARE\Classes\" + progId + @"\CLSID"))
                        {
                            if (k == null) continue;

                            bulundu = true;
                            string clsid = Convert.ToString(k.GetValue(""));
                            l.Add(Ok("Kayıt Bulundu (" + etiket + "): " + progId + " -> " + clsid));

                            using (var s = kok.OpenSubKey(
                                       @"SOFTWARE\Classes\CLSID\" + clsid + @"\LocalServer32"))
                            {
                                if (s == null)
                                    l.Add(Err("   > LocalServer32 Kaydı Yok; COM Kaydı Eksik ya da Bozuk."));
                                else
                                    l.Add(Info("   LocalServer32 -> " + Convert.ToString(s.GetValue(""))));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                l.Add(Info("Kayıt Defteri Okunamadı (" + etiket + "): " + ex.Message));
            }

            return bulundu;
        }

        private static void PolitikaKontrol(List<DiagLine> l)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Ole"))
                {
                    if (k == null) return;

                    string enable = Convert.ToString(k.GetValue("EnableDCOM"));
                    if (!string.IsNullOrEmpty(enable))
                    {
                        if (enable.StartsWith("N", StringComparison.OrdinalIgnoreCase))
                            l.Add(Err("DCOM Devre Dışı (EnableDCOM = N); Politika ile Kapatılmış."));
                        else
                            l.Add(Info("DCOM Etkin (EnableDCOM = " + enable + ")."));
                    }

                    if (k.GetValue("MachineLaunchRestriction") != null)
                        l.Add(Info("DCOM Başlatma Kısıtlaması Tanımlı (MachineLaunchRestriction)."));
                }
            }
            catch (Exception ex) { l.Add(Info("DCOM Ayarı Okunamadı: " + ex.Message)); }
        }

        // ================= HRESULT =================

        public static string Aciklama(int hr)
        {
            string kod = "0x" + hr.ToString("X8");
            string ad;

            switch ((uint)hr)
            {
                case 0x800401E3:
                    ad = "MK_E_UNAVAILABLE: CATIA Çalışan Nesne Tablosunda Yok. " +
                         "CATIA Kapalı ya da Farklı Yetki Seviyesinde (Yönetici Olarak) Çalışıyor.";
                    break;
                case 0x80040154:
                    ad = "REGDB_E_CLASSNOTREG: CATIA.Application COM Sınıfı Kayıtlı Değil.";
                    break;
                case 0x800401F3:
                    ad = "CO_E_CLASSSTRING: ProgID Geçersiz ya da Kayıtlı Değil.";
                    break;
                case 0x80070005:
                    ad = "E_ACCESSDENIED: COM Erişimi Reddedildi. DCOM Güvenlik Ayarları " +
                         "ya da Grup Politikası Engelliyor.";
                    break;
                case 0x80080005:
                    ad = "CO_E_SERVER_EXEC_FAILURE: COM Sunucusu Başlatılamadı.";
                    break;
                case 0x80010001:
                    ad = "RPC_E_CALL_REJECTED: CATIA Meşgul ya da Açık Bir Diyalog Bekliyor.";
                    break;
                case 0x8001010E:
                    ad = "RPC_E_WRONG_THREAD: Yanlış COM Apartmanından Çağrı Yapıldı.";
                    break;
                default:
                    try { ad = Marshal.GetExceptionForHR(hr).Message; }
                    catch { ad = "Bilinmeyen Hata."; }
                    break;
            }

            return kod + " - " + ad;
        }
    }
}
