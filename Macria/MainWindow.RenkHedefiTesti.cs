using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Macria
{
    public partial class MainWindow
    {
        // Bu üç komut yalnız CATIA V6 renk hedefini manuel doğrulamak içindir.
        // Akıllı Renklendirme akışını ve renk algoritmasını değiştirmez.
        private void RenkTestOccurrence_Click(object sender, RoutedEventArgs e) =>
            RenkHedefiniTestEt(CatiaColorTargetType.Occurrence);

        private void RenkTestPartBody_Click(object sender, RoutedEventArgs e) =>
            RenkHedefiniTestEt(CatiaColorTargetType.PartBody);

        private void RenkTestProduct_Click(object sender, RoutedEventArgs e) =>
            RenkHedefiniTestEt(CatiaColorTargetType.Product);

        private void RenkHedefiniTestEt(CatiaColorTargetType hedefTipi)
        {
            try
            {
                object? catiaNesnesi = GetCatia();
                if (catiaNesnesi == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                dynamic catia = catiaNesnesi;
                dynamic editor = catia.ActiveEditor;
                dynamic selection = editor.Selection;
                List<object> oncekiSecim = SecimiSakla(selection);

                try
                {
                    var service = new CatiaColorTargetService(
                        node => ReferansAl((dynamic)node));
                    CatiaColorTargetResult hedefSonucu = RenkTestHedefiniCoz(
                        service, editor, oncekiSecim, hedefTipi);
                    if (!hedefSonucu.Success || hedefSonucu.Target == null)
                    {
                        LogError("CATIA renk hedefi tanı — " + hedefSonucu.Error);
                        throw new InvalidOperationException(hedefSonucu.Error);
                    }

                    RenkTestNesneTanisiniYaz("Renk hedefi", hedefSonucu.Target.ComObject);
                    CatiaColorOperationResult okumaSonucu = service.TryReadRgb(selection, hedefSonucu.Target);
                    if (!okumaSonucu.Success || !okumaSonucu.Color.HasValue)
                    {
                        string okumaNedeni = okumaSonucu.Exception == null
                            ? okumaSonucu.Error
                            : RenkTestHataAyrintisi("VisProperties.GetRealColor", okumaSonucu.Exception);
                        LogError("CATIA renk hedefi tanı — " + okumaNedeni);
                        throw new InvalidOperationException(
                            "Hedefin mevcut RGB değeri okunamadığı için renk yazılmadı. " + okumaNedeni);
                    }

                    CatiaColorRgb oncekiRenk = okumaSonucu.Color.Value;

                    string hedefAdi = RenkTestHedefAdi(hedefTipi);
                    string uyari =
                        hedefAdi + " hedefinin mevcut RGB değeri okundu: " +
                        oncekiRenk.Red + ", " + oncekiRenk.Green + ", " + oncekiRenk.Blue +
                        " (durum=" + oncekiRenk.Status + ").\n\n" +
                        "Geçici test rengi gerçek CATIA görünümüne yazılacaktır. " +
                        "Mevcut Macria kodunda renk kalıtımını/görünüm ayarını eksiksiz geri yükleyen doğrulanmış bir CATIA API zinciri yoktur; " +
                        "testten sonra gerekirse CATIA üzerinden elle geri alın. Devam edilsin mi?";

                    if (!OnayWindow.Sor(this, "CATIA renk hedefi testi", uyari, "Test rengini uygula"))
                        return;

                    AkilliRenk testRengi = RenkTestRengi(hedefTipi);
                    CatiaColorOperationResult yazmaSonucu = service.TryApplyRgb(
                        selection, hedefSonucu.Target,
                        testRengi.Kirmizi, testRengi.Yesil, testRengi.Mavi);
                    if (!yazmaSonucu.Success)
                    {
                        string yazmaNedeni = yazmaSonucu.Exception == null
                            ? yazmaSonucu.Error
                            : RenkTestHataAyrintisi("VisProperties.SetRealColor", yazmaSonucu.Exception);
                        throw new InvalidOperationException(yazmaNedeni);
                    }
                    LogSuccess(
                        "CATIA renk hedefi testi uygulandı: " + hedefAdi + "\n" +
                        "Önceki RGB: " + oncekiRenk.Red + ", " + oncekiRenk.Green + ", " + oncekiRenk.Blue + "\n" +
                        "Test RGB: " + testRengi.Kirmizi + ", " + testRengi.Yesil + ", " + testRengi.Mavi + "\n" +
                        "Görsel etkiyi CATIA'da manuel doğrulayın; otomatik geri alma uygulanmadı.");
                }
                finally
                {
                    SecimiGeriYukle(selection, oncekiSecim);
                }
            }
            catch (Exception ex)
            {
                RenkTestHataTanisiniYaz("Renk hedefi testi", ex);
                LogError("CATIA renk hedefi testi uygulanamadı: " + Kisa(ex.Message));
            }
        }

        private static CatiaColorTargetResult RenkTestHedefiniCoz(
            CatiaColorTargetService service,
            dynamic editor,
            List<object> secim,
            CatiaColorTargetType hedefTipi)
        {
            if (hedefTipi == CatiaColorTargetType.PartBody)
            {
                object? activeObject = null;
                try { activeObject = editor.ActiveObject; } catch { }
                return service.ResolvePartBody(activeObject);
            }

            if (secim.Count != 1)
                return CatiaColorTargetResult.Fail(
                    "Occurrence ve Product testi için CATIA'da tam olarak bir hedef seçin.");

            object selectedObject = secim[0];
            return hedefTipi == CatiaColorTargetType.Occurrence
                ? service.ResolveOccurrence(selectedObject)
                : service.ResolveProduct(selectedObject);
        }

        private void RenkTestNesneTanisiniYaz(string asama, object? nesne)
        {
            if (nesne == null)
            {
                LogInfo("CATIA renk hedefi tanı — " + asama + ": (null)");
                return;
            }

            string tip = ComProbe.TipAdi(nesne);
            List<string> uyeler = ComProbe.UyeAdlari(nesne);
            string[] ilgiliAnahtarlar =
            {
                "plm", "occurrence", "instance", "reference", "body", "visual", "color", "real"
            };
            string ilgili = string.Join(", ", uyeler.Where(u =>
                ilgiliAnahtarlar.Any(a => u.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)));

            LogInfo(
                "CATIA renk hedefi tanı — " + asama +
                " | COM türü=" + tip +
                " | ilgili üyeler=" + (ilgili.Length == 0 ? "(yok)" : ilgili));
        }

        private void RenkTestHataTanisiniYaz(string asama, Exception ex)
        {
            LogError("CATIA renk hedefi tanı — " + RenkTestHataAyrintisi(asama, ex));
        }

        private static string RenkTestHataAyrintisi(string asama, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append("aşama=").Append(asama);

            Exception? anlik = ex;
            int derinlik = 0;
            while (anlik != null && derinlik < 4)
            {
                sb.Append("\n[").Append(derinlik).Append("] tür=")
                    .Append(anlik.GetType().FullName)
                    .Append(" | HRESULT=0x")
                    .Append(unchecked((uint)anlik.HResult).ToString("X8"))
                    .Append(" | mesaj=").Append(anlik.Message);
                anlik = anlik.InnerException;
                derinlik++;
            }

            sb.Append("\nException.ToString(): ").Append(ex);
            return sb.ToString();
        }

        private static AkilliRenk RenkTestRengi(CatiaColorTargetType hedefTipi)
        {
            return hedefTipi switch
            {
                CatiaColorTargetType.Occurrence => new AkilliRenk(230, 75, 75),
                CatiaColorTargetType.PartBody => new AkilliRenk(55, 175, 230),
                CatiaColorTargetType.Product => new AkilliRenk(240, 165, 55),
                _ => throw new ArgumentOutOfRangeException(nameof(hedefTipi))
            };
        }

        private static string RenkTestHedefAdi(CatiaColorTargetType hedefTipi)
        {
            return hedefTipi switch
            {
                CatiaColorTargetType.Occurrence => "A — seçili occurrence",
                CatiaColorTargetType.PartBody => "B — açık parçanın PartBody'si",
                CatiaColorTargetType.Product => "C — seçili alt Product",
                _ => "bilinmeyen hedef"
            };
        }
    }
}
