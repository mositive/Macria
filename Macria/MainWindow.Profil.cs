using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Macria
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<ProfilRow> _profilRows =
            new ObservableCollection<ProfilRow>();
        private ICollectionView? _profilView;
        private bool _profilGizliDahil = true;
        private bool _profilIslemde;
        private string _sonProfilRaporu = "";

        private sealed class ProfilTeshisSonucu
        {
            public bool Basarili;
            public string DurumKodu = "Belirsiz";
            public string Durum = "Karar verilemedi";
            public string Aciklama = "";
            public int Guven;
            public int BodyCount;
            public string BodyNames = "";
            public string FeatureKaniti = "";
            public int FeaturePuani;
            public int ShapeCount;
            public int FaceCount = -1;
            public string FaceTeshisi = "";
            public bool OlcumVar;
            public double Hacim;
            public double Alan;
            public string OlcumYontemi = "";
            public double TahminiEtMm;
            public bool AtaletVar;
            public double[] AtaletMomentleri = Array.Empty<double>();
            public double AtaletOrani;
            public bool SinirKutusuVar;
            public double[] SinirOlculeriMm = Array.Empty<double>();
            public string SinirKutusuKaynak = "";
            public string MetaOlcu = "";
            public string Not = "";
            public string Hata = "";
            public object? InertiaObject;
            public object? MeasureObject;
            public object? ActivePart;
            public object? MainBody;
        }

        // MainWindow kurucusundan bir kez cagrilir.
        private void ProfilKur()
        {
            _profilView = CollectionViewSource.GetDefaultView(_profilRows);
            _profilView.Filter = FilterProfilRow;
            gridProfil.ItemsSource = _profilView;
            ProfilButonlariniGuncelle();
            ProfilOzetiniTemizle();

#if DEBUG
            // CATIA olmayan bilgisayarda F10, profil sekmesini ornek satirlarla
            // doldurur. STEP butonu gercek CATIA referansi olmadigi icin calismaz.
            PreviewKeyDown += DebugProfilPenceresi_KeyDown;
#endif
        }

#if DEBUG
        private void DebugProfilPenceresi_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F10 || _profilIslemde || _exporting) return;

            e.Handled = true;
            _profilRows.Clear();

            var duz = new ProfilRow
            {
                ProductName = "TEST-100X50X5",
                PartName = "3D Shape00001001",
                ReferenceName = "prd-test-profile-001",
                Description = "100x50x5 Kutu Profil",
                Revision = "A",
                Quantity = 4,
                BodyCount = 1,
                BodyNames = "PartBody"
            };
            duz.TeshisTamamlandi(
                "Kuvvetli", "Kuvvetli profil adayı",
                "Tek Body, uzun geometri ve çok yüzlü içi boş kesit kanıtları bulundu.",
                88, "1850 × 100 × 50 mm", "≈ 5 mm", "42,7", "18",
                "Shell.1; Pad.1", "Düz kutu profil; STEP deneme satırı.");

            var yay = new ProfilRow
            {
                ProductName = "TEST-R16500",
                PartName = "3D Shape00001002",
                ReferenceName = "prd-test-profile-002",
                Description = "80x80x3 R16500",
                Revision = "A",
                Quantity = 1,
                BodyCount = 1,
                BodyNames = "Parça Gövdesi"
            };
            yay.TeshisTamamlandi(
                "Aday", "Profil adayı",
                "Geçmişsiz katı; çok yüzlü ve ince cidarlı geometri bulundu.",
                67, "3200 × 160 × 80 mm", "≈ 3,1 mm", "18,4", "22",
                "Unsur geçmişi okunamadı", "Yay profil; düzleştirme bu sürümde yapılmaz.");

            var belirsiz = new ProfilRow
            {
                ProductName = "TEST-KATI-PARCA",
                PartName = "3D Shape00001003",
                ReferenceName = "prd-test-profile-003",
                Description = "İşlenmiş katı parça",
                Revision = "B",
                Quantity = 2,
                BodyCount = 1,
                BodyNames = "PartBody"
            };
            belirsiz.TeshisTamamlandi(
                "Belirsiz", "Karar verilemedi",
                "İç boşluk ve sabit profil kesiti doğrulanamadı.",
                24, "220 × 90 × 65 mm", "—", "2,1", "6",
                "Pad.1", "STEP yalnızca kullanıcı onayıyla alınabilir.");

            _profilRows.Add(duz);
            _profilRows.Add(yay);
            _profilRows.Add(belirsiz);
            _profilView?.Refresh();
            ProfilOzetiniYaz();
            parcaSekmeleri.SelectedIndex = 2;
            LogInfo("Profil Demo Verisi Yüklendi — F10 (CATIA kullanılmadı).");
        }
#endif

        private static ProfilRow ProfilSatiriOlustur(
            string productName, string partName, string referenceName,
            string description, string revision, int quantity,
            bool gizli, object? partObj, object? repRef,
            int bodyCount, string bodyNames)
        {
            int featurePuani;
            int shapeCount;
            string featureKaniti = ProfilFeatureKaniti(
                partObj, out featurePuani, out shapeCount);
            string metaOlcu = ProfilOlcuIfadesiBul(
                productName + " " + description);

            int puan = featurePuani;
            if (bodyCount == 1) puan += 5;
            if (!string.IsNullOrWhiteSpace(metaOlcu)) puan += 20;
            puan = Math.Min(55, puan);

            var row = new ProfilRow
            {
                ProductName = productName ?? "",
                PartName = partName ?? "",
                ReferenceName = referenceName ?? "",
                Description = description ?? "",
                Revision = revision ?? "",
                Quantity = Math.Max(1, quantity),
                BodyCount = bodyCount,
                BodyNames = bodyNames ?? "",
                GizliPhysicalProductMu = gizli,
                RepRef = repRef
            };

            var notlar = new List<string>();
            if (bodyCount > 1)
                notlar.Add("Çoklu Body; otomatik profil kararı verilmedi");
            else if (bodyCount == 1)
                notlar.Add("Tek katı Body");
            else
                notlar.Add("Katı Body bilgisi okunamadı");

            if (shapeCount == 0)
                notlar.Add("Unsur geçmişi yok veya COM üzerinden görünmüyor");
            if (!string.IsNullOrWhiteSpace(metaOlcu))
                notlar.Add("Tanımda ölçü: " + metaOlcu);
            if (gizli) notlar.Add("Gizlenmiş öğe");

            if (bodyCount > 1)
            {
                row.HizliTeshis(
                    "CokluBody", "Çoklu Body — kontrol gerekli",
                    "Birden fazla katı Body bulundu. Yanlış geometriyi STEP'e almamak için kullanıcı kontrolü gerekir.",
                    featureKaniti, Math.Min(35, puan), string.Join(" · ", notlar));
            }
            else if (puan >= 35)
            {
                row.HizliTeshis(
                    "Aday", "Profil adayı",
                    "Ürün bilgisi veya unsur ağacında profil olabileceğine dair kanıt bulundu. Kesin karar için detaylı teşhis gerekir.",
                    featureKaniti, puan, string.Join(" · ", notlar));
            }
            else
            {
                row.HizliTeshis(
                    "Bekliyor", "Teşhis bekliyor",
                    "Bu, sac olmayan bir katı parçadır. Geçmişsiz kutu profilleri kaçırmamak için listeye alınmıştır.",
                    featureKaniti, puan, string.Join(" · ", notlar));
            }

            return row;
        }

        private static string ProfilFeatureKaniti(
            object? partObj, out int puan, out int shapeCount)
        {
            puan = 0;
            shapeCount = 0;
            if (partObj == null) return "";

            dynamic? bodies = null;
            try { bodies = ((dynamic)partObj).Bodies; }
            catch { }
            if (bodies == null) return "";

            int bodyCount;
            try { bodyCount = Convert.ToInt32(bodies.Count); }
            catch { return ""; }

            var bulunan = new List<string>();
            var benzersiz = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int b = 1; b <= bodyCount; b++)
            {
                dynamic? body = null;
                try { body = bodies.Item(b); }
                catch { continue; }
                if (body == null) continue;

                dynamic? shapes = null;
                try { shapes = body.Shapes; }
                catch { }
                if (shapes == null) continue;

                int count;
                try { count = Convert.ToInt32(shapes.Count); }
                catch { continue; }
                shapeCount += count;

                for (int i = 1; i <= count; i++)
                {
                    object? shape = null;
                    try { shape = shapes.Item(i); }
                    catch { continue; }
                    if (shape == null) continue;

                    string name = "";
                    try { name = Convert.ToString(((dynamic)shape).Name)?.Trim() ?? ""; }
                    catch { }
                    string tip = ComProbe.TipAdi(shape);
                    string folded = Fold(name + " " + tip);

                    int eklenecek = 0;
                    if (ProfilKelimeVar(folded,
                        "shell", "kabuk", "sweep", "supur", "rib", "nervur",
                        "thicksurface", "kalinyuzey", "closesurface", "kapaliyuzey"))
                        eklenecek = 25;
                    else if (ProfilKelimeVar(folded,
                        "pad", "extrusion", "ekstruzyon"))
                        eklenecek = 8;

                    if (eklenecek <= 0) continue;

                    puan = Math.Max(puan, eklenecek);
                    string kanit = string.IsNullOrWhiteSpace(name) ? tip : name;
                    if (benzersiz.Add(kanit) && bulunan.Count < 6)
                        bulunan.Add(kanit);
                }
            }

            return string.Join("; ", bulunan);
        }

        private static bool ProfilKelimeVar(string kaynak, params string[] kelimeler)
        {
            foreach (string kelime in kelimeler)
                if (kaynak.Contains(kelime)) return true;
            return false;
        }

        private static string ProfilOlcuIfadesiBul(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            Match match = Regex.Match(
                text,
                @"(?<!\d)(\d{1,5}(?:[.,]\d+)?)\s*[xX×]\s*(\d{1,5}(?:[.,]\d+)?)(?:\s*[xX×]\s*(\d{1,4}(?:[.,]\d+)?))?(?!\d)");

            return match.Success ? match.Value.Trim() : "";
        }

        private bool FilterProfilRow(object item)
        {
            ProfilRow? row = item as ProfilRow;
            if (row == null) return false;
            if (!_profilGizliDahil && row.GizliPhysicalProductMu) return false;
            if (_searchText.Length == 0) return true;

            return ContainsText(row.ProductName, _searchText) ||
                   ContainsText(row.ReferenceName, _searchText) ||
                   ContainsText(row.Description, _searchText) ||
                   ContainsText(row.Revision, _searchText) ||
                   ContainsText(row.PartName, _searchText) ||
                   ContainsText(row.Durum, _searchText) ||
                   ContainsText(row.Not, _searchText);
        }

        private void ProfilGizliOgeFiltresi_Click(object sender, RoutedEventArgs e)
        {
            _profilGizliDahil = chkProfilGizliDahil.IsChecked == true;
            _profilView?.Refresh();

            if (!_profilGizliDahil && gridProfil.SelectedItem is ProfilRow secili &&
                secili.GizliPhysicalProductMu)
                gridProfil.SelectedItem = null;

            LogInfo(_profilGizliDahil
                ? "Kutu Profil — Gizlenmiş öğeler listeye dahil."
                : "Kutu Profil — Gizlenmiş öğeler listeden çıkarıldı.");
            ProfilButonlariniGuncelle();
        }

        private void gridProfil_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ProfilButonlariniGuncelle();
        }

        private void ProfilButonlariniGuncelle()
        {
            bool secimVar = gridProfil != null && gridProfil.SelectedItem is ProfilRow;
            bool serbest = !_profilIslemde && !_exporting;
            if (btnProfilTeshis != null) btnProfilTeshis.IsEnabled = secimVar && serbest;
            if (btnProfilStep != null) btnProfilStep.IsEnabled = secimVar && serbest;
            if (btnGeometryLabTest != null) btnGeometryLabTest.IsEnabled = secimVar && serbest;
            if (btnProfilRaporAc != null)
                btnProfilRaporAc.IsEnabled = serbest &&
                                             !string.IsNullOrWhiteSpace(_sonProfilRaporu) &&
                                             File.Exists(_sonProfilRaporu);
        }

        private void ProfilOzetiniTemizle()
        {
            if (txtProfilOzeti == null) return;
            txtProfilOzeti.Text = "";
            txtProfilOzeti.Visibility = Visibility.Collapsed;
        }

        private void ProfilOzetiniYaz()
        {
            if (txtProfilOzeti == null) return;

            int toplam = 0, kuvvetli = 0, aday = 0, bekleyen = 0, coklu = 0;
            foreach (ProfilRow row in _profilRows)
            {
                toplam += row.Quantity;
                if (row.DurumKodu == "Kuvvetli") kuvvetli += row.Quantity;
                else if (row.DurumKodu == "Aday") aday += row.Quantity;
                else if (row.DurumKodu == "CokluBody") coklu += row.Quantity;
                else bekleyen += row.Quantity;
            }

            txtProfilOzeti.Text =
                "Sac olmayan katı: " + toplam + " adet" +
                "   •   Kuvvetli aday: " + kuvvetli +
                "   •   Aday: " + aday +
                "   •   Teşhis bekleyen: " + bekleyen +
                "   •   Çoklu Body: " + coklu;
            txtProfilOzeti.Visibility = Visibility.Visible;
        }

        private async void btnProfilTeshis_Click(object sender, RoutedEventArgs e)
        {
            if (_profilIslemde || _exporting) return;
            if (!(gridProfil.SelectedItem is ProfilRow row))
            {
                LogInfo("Profil Teşhisi İçin Listeden Bir Parça Seçin.");
                return;
            }

            if (row.RepRef == null)
            {
                string neden = "Parça referansı yok. F10 demo satırlarında gerçek CATIA işlemi yapılamaz.";
                row.TeshisHatasi(neden);
                LogError(neden);
                return;
            }

            _catia = GetCatia() ?? _catia;
            if (_catia == null)
            {
                row.TeshisHatasi("CATIA bağlantısı kurulamadı.");
                LogError("Profil Teşhisi Başlatılamadı — CATIA bağlantısı yok.");
                return;
            }

            var sure = System.Diagnostics.Stopwatch.StartNew();
            _profilIslemde = true;
            row.TeshisBasladi();
            ProfilButonlariniGuncelle();
            ShowPipStart(row.ProductName, "Profil Teşhisi");
            LogInfo("Profil Teşhisi Başladı: " + row.ProductName);

            bool ok = false;
            try
            {
                ok = await ProfilDetayliTeshisEt(row);
            }
            catch (Exception ex)
            {
                row.TeshisHatasi("Profil teşhis hatası: " + ex.Message);
                LogError("Profil Teşhis Hatası: " + ex.Message);
            }
            finally
            {
                _profilIslemde = false;
                ProfilButonlariniGuncelle();
                ProfilOzetiniYaz();
                IslemSuresiniYaz("Profil Teşhisi", sure);
            }

            await FinishPip(
                ok ? ExportPipWindow.PipState.Done : ExportPipWindow.PipState.Error,
                ok ? row.Durum : "Teşhis tamamlanamadı");
        }

        private async Task<bool> ProfilDetayliTeshisEt(ProfilRow row)
        {
            dynamic catia = _catia;
            bool pencereAcildi = false;
            var sonuc = new ProfilTeshisSonucu();

            try
            {
                LogInfo("Profil Parçası Açılıyor...");
                dynamic svc = catia.ActiveEditor.GetService("PLMOpenService");
                object? newEd = null;
                svc.PLMOpenInNewWindow(row.RepRef, ref newEd);
                pencereAcildi = true;
                await Task.Delay(2500);

                object? acikParca = null;
                try { acikParca = catia.ActiveEditor.ActiveObject; }
                catch { }
                if (acikParca == null)
                    throw new InvalidOperationException("Açılan parçanın ActiveObject nesnesi alınamadı.");

                sonuc.ActivePart = acikParca;
                try { sonuc.MainBody = ((dynamic)acikParca).MainBody; }
                catch { }

                string bodyNames;
                int bodyCount;
                if (TryGetSolidBodyInfo(acikParca, out bodyCount, out bodyNames))
                {
                    sonuc.BodyCount = bodyCount;
                    sonuc.BodyNames = bodyNames;
                    row.BodyCount = bodyCount;
                    row.BodyNames = bodyNames;
                }
                else
                {
                    sonuc.BodyCount = row.BodyCount;
                    sonuc.BodyNames = row.BodyNames;
                }

                sonuc.FeatureKaniti = ProfilFeatureKaniti(
                    acikParca, out sonuc.FeaturePuani, out sonuc.ShapeCount);
                sonuc.MetaOlcu = ProfilOlcuIfadesiBul(
                    row.ProductName + " " + row.Description);

                double hacim, alan;
                string yontem;
                object olcumHedefi = acikParca;
                bool olculdu = Olcumler(catia, olcumHedefi, out hacim, out alan, out yontem);
                if (!olculdu && sonuc.MainBody != null)
                {
                    olcumHedefi = sonuc.MainBody;
                    olculdu = Olcumler(catia, olcumHedefi, out hacim, out alan, out yontem);
                    if (olculdu) yontem += " (MainBody)";
                }

                sonuc.OlcumVar = olculdu;
                sonuc.Hacim = hacim;
                sonuc.Alan = alan;
                sonuc.OlcumYontemi = yontem;
                if (olculdu && alan > 0 && hacim > 0)
                    sonuc.TahminiEtMm = ProfilUzunluguMmYap(2.0 * hacim / alan);

                ProfilOlcumNesneleriniAl(
                    catia, olcumHedefi, out sonuc.InertiaObject,
                    out sonuc.MeasureObject);

                string ataletNedeni;
                double[] moments;
                object? ataletHedefi = sonuc.InertiaObject;
                if (ProfilSayisalDiziOku(
                        ataletHedefi, "GetPrincipalMoments", 3,
                        out moments, out ataletNedeni))
                {
                    sonuc.AtaletVar = true;
                    sonuc.AtaletMomentleri = moments;
                    sonuc.AtaletOrani = ProfilOran(moments);
                }

                string kutuNedeni;
                double[] olculer;
                string kutuKaynak;
                if (ProfilSinirKutusuOku(
                        new object?[]
                        {
                            sonuc.MeasureObject, sonuc.InertiaObject,
                            sonuc.MainBody, sonuc.ActivePart
                        },
                        out olculer, out kutuKaynak, out kutuNedeni))
                {
                    sonuc.SinirKutusuVar = true;
                    sonuc.SinirOlculeriMm = ProfilBoyutlariniMmYap(olculer);
                    sonuc.SinirKutusuKaynak = kutuKaynak;
                }

                sonuc.FaceCount = ProfilYuzleriniSay(
                    catia, out sonuc.FaceTeshisi);

                ProfilPuanla(sonuc);
                if (row.GizliPhysicalProductMu)
                    sonuc.Not = string.IsNullOrWhiteSpace(sonuc.Not)
                        ? "Gizlenmiş öğe"
                        : sonuc.Not + " · Gizlenmiş öğe";

                LogInfo("Profil kanıtı — Body: " + sonuc.BodyCount +
                        (string.IsNullOrWhiteSpace(sonuc.BodyNames)
                            ? ""
                            : " (" + sonuc.BodyNames + ")") +
                        ", Shape: " + sonuc.ShapeCount +
                        ", Yüz: " + (sonuc.FaceCount >= 0
                            ? sonuc.FaceCount.ToString()
                            : "okunamadı") + ".");
                LogInfo("Profil ölçümü — Sınır: " +
                        ProfilBoyutMetni(sonuc.SinirOlculeriMm) +
                        ", 2V/A yaklaşık et: " +
                        (sonuc.TahminiEtMm > 0
                            ? ProfilSayi(sonuc.TahminiEtMm) + " mm"
                            : "okunamadı") +
                        ", Atalet oranı: " +
                        (sonuc.AtaletVar
                            ? ProfilSayi(sonuc.AtaletOrani)
                            : "okunamadı") + ".");

                string aciklama = sonuc.Aciklama;
                row.TeshisTamamlandi(
                    sonuc.DurumKodu,
                    sonuc.Durum,
                    aciklama,
                    sonuc.Guven,
                    ProfilBoyutMetni(sonuc.SinirOlculeriMm),
                    sonuc.TahminiEtMm > 0
                        ? "≈ " + ProfilSayi(sonuc.TahminiEtMm) + " mm"
                        : "—",
                    sonuc.AtaletVar ? ProfilSayi(sonuc.AtaletOrani) : "—",
                    sonuc.FaceCount >= 0 ? sonuc.FaceCount.ToString() : "—",
                    sonuc.FeatureKaniti,
                    sonuc.Not);

                _sonProfilRaporu = ProfilRaporuYaz(row, sonuc, catia);
                if (!string.IsNullOrWhiteSpace(_sonProfilRaporu))
                    LogSuccess("Profil Teşhis Raporu Yazıldı: " + _sonProfilRaporu);

                if (sonuc.DurumKodu == "Kuvvetli")
                    LogSuccess("Profil Teşhisi Tamamlandı — " + row.ProductName +
                               ": " + sonuc.Durum + " (%" + sonuc.Guven + ").");
                else
                    LogInfo("Profil Teşhisi Tamamlandı — " + row.ProductName +
                            ": " + sonuc.Durum + " (%" + sonuc.Guven + ").");

                sonuc.Basarili = true;
                return true;
            }
            catch (Exception ex)
            {
                sonuc.Hata = ex.Message;
                row.TeshisHatasi("Profil geometrisi okunamadı: " + ex.Message);
                LogError("Profil Geometrisi Okunamadı — " + row.ProductName +
                         ": " + ex.Message);

                try
                {
                    _sonProfilRaporu = ProfilRaporuYaz(row, sonuc, catia);
                }
                catch { }
                return false;
            }
            finally
            {
                try { catia.ActiveEditor.Selection.Clear(); } catch { }
                if (pencereAcildi)
                {
                    try { catia.ActiveWindow.Close(); } catch { }
                    await WaitForAssembly(15000);
                    await Task.Delay(800);
                }
            }
        }

        private static void ProfilPuanla(ProfilTeshisSonucu sonuc)
        {
            int puan = 0;
            var nedenler = new List<string>();

            if (sonuc.BodyCount == 1)
            {
                puan += 5;
                nedenler.Add("tek katı Body");
            }
            else if (sonuc.BodyCount > 1)
            {
                nedenler.Add("çoklu Body");
            }
            else
            {
                nedenler.Add("Body bilgisi okunamadı");
            }

            if (sonuc.FeaturePuani > 0)
            {
                puan += sonuc.FeaturePuani;
                nedenler.Add("profil unsur kanıtı");
            }
            else if (sonuc.ShapeCount == 0)
            {
                nedenler.Add("geçmişsiz/As Result olabilir");
            }

            if (!string.IsNullOrWhiteSpace(sonuc.MetaOlcu))
            {
                puan += 20;
                nedenler.Add("tanımda profil ölçüsü " + sonuc.MetaOlcu);
            }

            if (sonuc.OlcumVar && sonuc.TahminiEtMm >= 0.5 &&
                sonuc.TahminiEtMm <= 20)
            {
                puan += 15;
                nedenler.Add("ince cidar oranı");
            }

            if (sonuc.AtaletVar)
            {
                if (sonuc.AtaletOrani >= 25)
                {
                    puan += 30;
                    nedenler.Add("çok uzun geometri");
                }
                else if (sonuc.AtaletOrani >= 8)
                {
                    puan += 20;
                    nedenler.Add("uzun geometri");
                }
                else if (sonuc.AtaletOrani >= 3)
                {
                    puan += 10;
                    nedenler.Add("orta derecede uzun geometri");
                }
            }

            if (sonuc.SinirKutusuVar && sonuc.SinirOlculeriMm.Length >= 3)
            {
                double[] d = (double[])sonuc.SinirOlculeriMm.Clone();
                Array.Sort(d);
                double oran = d[1] > 0 ? d[2] / d[1] : 0;
                if (oran >= 3)
                {
                    puan += 15;
                    nedenler.Add("sınır kutusu uzun");
                }
                else if (oran >= 1.8)
                {
                    puan += 8;
                    nedenler.Add("sınır kutusu uzamış");
                }
            }

            if (sonuc.FaceCount >= 10)
            {
                puan += 15;
                nedenler.Add("çok yüzlü geometri");
            }
            else if (sonuc.FaceCount >= 7)
            {
                puan += 5;
                nedenler.Add("yüz sayısı uygun olabilir");
            }
            else if (sonuc.FaceCount >= 0)
            {
                nedenler.Add("iç boşluk yüzlerle doğrulanamadı");
            }

            puan = Math.Max(0, Math.Min(95, puan));
            sonuc.Guven = puan;

            if (sonuc.BodyCount > 1)
            {
                sonuc.DurumKodu = "CokluBody";
                sonuc.Durum = "Çoklu Body — kontrol gerekli";
                sonuc.Aciklama =
                    "Birden fazla katı Body bulundu. Macria hangi gövdenin profil olduğunu kesinleştiremedi.";
            }
            else if (puan >= 60)
            {
                sonuc.DurumKodu = "Kuvvetli";
                sonuc.Durum = "Kuvvetli profil adayı";
                sonuc.Aciklama =
                    "Geometri ölçümleri kutu profil ihtimalini güçlü biçimde destekliyor. Bu sürüm henüz sanal kesitle kesin iç boşluk doğrulaması yapmaz.";
            }
            else if (puan >= 35)
            {
                sonuc.DurumKodu = "Aday";
                sonuc.Durum = "Profil adayı";
                sonuc.Aciklama =
                    "Bazı profil kanıtları bulundu ancak kullanıcı kontrolü gerekir.";
            }
            else
            {
                sonuc.DurumKodu = "Belirsiz";
                sonuc.Durum = "Karar verilemedi";
                sonuc.Aciklama =
                    "Sabit ve içi boş kutu profil kesiti henüz doğrulanamadı. STEP yalnızca kullanıcı onayıyla alınmalıdır.";
            }

            sonuc.Not = string.Join(" · ", nedenler);
        }

        private static void ProfilOlcumNesneleriniAl(
            dynamic catia, object hedef, out object? inertia, out object? measure)
        {
            inertia = null;
            measure = null;

            dynamic? editor = null;
            try { editor = catia.ActiveEditor; }
            catch { }
            if (editor == null) return;

            try
            {
                dynamic servis = editor.GetService("InertiaService");
                inertia = servis.GetInertiaElement(hedef);
            }
            catch { }

            try
            {
                dynamic servis = editor.GetService("MeasureService");
                measure = servis.GetMeasureItem(hedef);
            }
            catch { }
        }

        private static double ProfilUzunluguMmYap(double value)
        {
            value = Math.Abs(value);
            if (value <= 0) return 0;

            // Inertia/Measure servisleri uzunlugu genellikle metre cinsinden
            // dondurur. Bazi V5 otomasyonlarinda mm gelir; iki aralik da taninir.
            if (value < 0.05) return value * 1000.0;
            return value;
        }

        private static double[] ProfilBoyutlariniMmYap(double[] values)
        {
            if (values == null || values.Length < 3) return Array.Empty<double>();
            var d = new[] { Math.Abs(values[0]), Math.Abs(values[1]), Math.Abs(values[2]) };
            double max = Math.Max(d[0], Math.Max(d[1], d[2]));
            double min = Math.Min(d[0], Math.Min(d[1], d[2]));

            // Ornegin 2.0 x 0.1 x 0.05 m -> mm. Zaten 2000 x 100 x 50
            // geliyorsa dokunulmaz.
            if (max > 0 && max <= 20 && min < 1)
            {
                d[0] *= 1000;
                d[1] *= 1000;
                d[2] *= 1000;
            }

            return d;
        }

        private static string ProfilBoyutMetni(double[] values)
        {
            if (values == null || values.Length < 3) return "—";
            double[] d = (double[])values.Clone();
            Array.Sort(d);
            Array.Reverse(d);
            return ProfilSayi(d[0]) + " × " + ProfilSayi(d[1]) +
                   " × " + ProfilSayi(d[2]) + " mm";
        }

        private static string ProfilSayi(double value)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }

        private static double ProfilOran(double[] values)
        {
            if (values == null || values.Length == 0) return 0;
            double min = double.MaxValue;
            double max = 0;
            foreach (double value in values)
            {
                double v = Math.Abs(value);
                if (v <= 1e-20) continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            return min == double.MaxValue || min <= 0 ? 0 : max / min;
        }

        private static bool ProfilSayisalDiziOku(
            object? nesne, string metot, int adet,
            out double[] values, out string neden)
        {
            values = Array.Empty<double>();
            neden = "";
            if (nesne == null)
            {
                neden = "nesne yok";
                return false;
            }

            Type tip = nesne.GetType();

            // 1) Metot diziyi dogrudan donduruyorsa.
            try
            {
                object? sonuc = tip.InvokeMember(
                    metot, BindingFlags.InvokeMethod, null, nesne, null);
                if (ProfilDiziyeCevir(sonuc, adet, out values)) return true;
            }
            catch (Exception ex)
            {
                neden = Kisa(ex.InnerException?.Message ?? ex.Message);
            }

            // 2) CATIA'nin klasik "Method array" kalibi.
            foreach (object buffer in new object[] { new double[adet], new object[adet] })
            {
                try
                {
                    object?[] args = { buffer };
                    var mod = new ParameterModifier(1);
                    mod[0] = true;

                    tip.InvokeMember(
                        metot, BindingFlags.InvokeMethod, null, nesne, args,
                        new[] { mod }, CultureInfo.InvariantCulture, null);

                    if (ProfilDiziyeCevir(args[0], adet, out values) ||
                        ProfilDiziyeCevir(buffer, adet, out values)) return true;
                }
                catch (Exception ex)
                {
                    neden = Kisa(ex.InnerException?.Message ?? ex.Message);
                }
            }

            // 3) Her eleman ayri out parametresi olarak tanimlanmissa.
            try
            {
                object?[] args = new object?[adet];
                var mod = new ParameterModifier(adet);
                for (int i = 0; i < adet; i++)
                {
                    args[i] = 0.0;
                    mod[i] = true;
                }

                tip.InvokeMember(
                    metot, BindingFlags.InvokeMethod, null, nesne, args,
                    new[] { mod }, CultureInfo.InvariantCulture, null);

                values = new double[adet];
                for (int i = 0; i < adet; i++)
                    values[i] = Convert.ToDouble(args[i], CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                neden = Kisa(ex.InnerException?.Message ?? ex.Message);
                return false;
            }
        }

        private static bool ProfilDiziyeCevir(
            object? value, int minCount, out double[] values)
        {
            values = Array.Empty<double>();
            if (!(value is Array arr) || arr.Length < minCount) return false;

            try
            {
                values = new double[minCount];
                for (int i = 0; i < minCount; i++)
                    values[i] = Convert.ToDouble(arr.GetValue(i), CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                values = Array.Empty<double>();
                return false;
            }
        }

        private static bool ProfilSinirKutusuOku(
            object?[] adaylar, out double[] dimensions, out string kaynak,
            out string neden)
        {
            dimensions = Array.Empty<double>();
            kaynak = "";
            neden = "";

            foreach (object? aday in adaylar)
            {
                if (aday == null) continue;
                string tip = ComProbe.TipAdi(aday);

                foreach (string metot in new[] { "GetBoundingBox", "GetBox", "GetExtents" })
                {
                    double[] raw;
                    string hata;
                    if (!ProfilSayisalDiziOku(aday, metot, 6, out raw, out hata))
                    {
                        if (!string.IsNullOrWhiteSpace(hata))
                            neden = tip + "." + metot + ": " + hata;
                        continue;
                    }

                    // En yaygin sira: xmin,ymin,zmin,xmax,ymax,zmax.
                    double dx = Math.Abs(raw[3] - raw[0]);
                    double dy = Math.Abs(raw[4] - raw[1]);
                    double dz = Math.Abs(raw[5] - raw[2]);
                    if (dx <= 0 || dy <= 0 || dz <= 0) continue;

                    dimensions = new[] { dx, dy, dz };
                    kaynak = tip + "." + metot;
                    return true;
                }
            }

            return false;
        }

        private static int ProfilYuzleriniSay(dynamic catia, out string teshis)
        {
            teshis = "";
            dynamic? selection = null;
            try { selection = catia.ActiveEditor.Selection; }
            catch (Exception ex)
            {
                teshis = "Selection alınamadı: " + Kisa(ex.Message);
                return -1;
            }
            if (selection == null) return -1;

            foreach (string sorgu in new[] { "Topology.Face,all", "CATGmoSearch.Face,all" })
            {
                try
                {
                    selection.Clear();
                    selection.Search(sorgu);

                    int count;
                    try { count = Convert.ToInt32(selection.Count2); }
                    catch { count = Convert.ToInt32(selection.Count); }

                    if (count <= 0) continue;

                    var tipler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int bakilacak = Math.Min(count, 20);
                    for (int i = 1; i <= bakilacak; i++)
                    {
                        object? value = null;
                        try { value = selection.Item2(i).Value; }
                        catch
                        {
                            try { value = selection.Item(i).Value; }
                            catch { }
                        }
                        if (value != null) tipler.Add(ComProbe.TipAdi(value));
                    }

                    teshis = sorgu + " → " + count + " yüz" +
                             (tipler.Count > 0 ? "; tipler: " + string.Join(", ", tipler) : "");
                    selection.Clear();
                    return count;
                }
                catch (Exception ex)
                {
                    teshis = sorgu + ": " + Kisa(ex.Message);
                }
                finally
                {
                    try { selection.Clear(); } catch { }
                }
            }

            return -1;
        }

        private static string ProfilRaporuYaz(
            ProfilRow row, ProfilTeshisSonucu sonuc, dynamic catia)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MACRIA — KUTU PROFİL TEŞHİS RAPORU");
            sb.AppendLine("Sürüm: " + AboutWindow.SurumMetni());
            sb.AppendLine("Tarih: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Parça Kodu (Title): " + row.ProductName);
            sb.AppendLine("PLM Kimliği (Name): " + row.ReferenceName);
            sb.AppendLine("3D Shape: " + row.PartName);
            sb.AppendLine("Tanım: " + row.Description);
            sb.AppendLine("Revizyon: " + row.Revision);
            sb.AppendLine("Adet: " + row.Quantity);
            sb.AppendLine("Gizli Physical Product: " +
                          (row.GizliPhysicalProductMu ? "EVET" : "HAYIR"));
            sb.AppendLine();

            sb.AppendLine("SONUÇ");
            sb.AppendLine("Durum: " + sonuc.Durum);
            sb.AppendLine("Güven: %" + sonuc.Guven);
            sb.AppendLine("Açıklama: " + sonuc.Aciklama);
            sb.AppendLine("Not: " + sonuc.Not);
            if (!string.IsNullOrWhiteSpace(sonuc.Hata))
                sb.AppendLine("Hata: " + sonuc.Hata);
            sb.AppendLine();

            sb.AppendLine("GEOMETRİ KANITLARI");
            sb.AppendLine("Body sayısı: " + sonuc.BodyCount);
            sb.AppendLine("Body adları: " +
                          (string.IsNullOrWhiteSpace(sonuc.BodyNames) ? "(okunamadı)" : sonuc.BodyNames));
            sb.AppendLine("Shape sayısı: " + sonuc.ShapeCount);
            sb.AppendLine("Feature kanıtı: " +
                          (string.IsNullOrWhiteSpace(sonuc.FeatureKaniti) ? "(yok)" : sonuc.FeatureKaniti));
            sb.AppendLine("Metadata ölçüsü: " +
                          (string.IsNullOrWhiteSpace(sonuc.MetaOlcu) ? "(yok)" : sonuc.MetaOlcu));
            sb.AppendLine("Yüz sayısı: " +
                          (sonuc.FaceCount < 0 ? "(okunamadı)" : sonuc.FaceCount.ToString()));
            sb.AppendLine("Yüz teşhisi: " +
                          (string.IsNullOrWhiteSpace(sonuc.FaceTeshisi) ? "(yok)" : sonuc.FaceTeshisi));
            sb.AppendLine();

            sb.AppendLine("ÖLÇÜMLER");
            sb.AppendLine("Ölçüm var: " + (sonuc.OlcumVar ? "EVET" : "HAYIR"));
            sb.AppendLine("Yöntem: " + sonuc.OlcumYontemi);
            sb.AppendLine("Hacim (ham COM): " + sonuc.Hacim.ToString("G17", CultureInfo.InvariantCulture));
            sb.AppendLine("Alan (ham COM): " + sonuc.Alan.ToString("G17", CultureInfo.InvariantCulture));
            sb.AppendLine("2V/A tahmini et: " +
                          (sonuc.TahminiEtMm > 0 ? ProfilSayi(sonuc.TahminiEtMm) + " mm" : "(hesaplanamadı)"));
            sb.AppendLine("Ana atalet momentleri: " +
                          (sonuc.AtaletVar
                              ? string.Join(", ", Array.ConvertAll(
                                  sonuc.AtaletMomentleri,
                                  v => v.ToString("G17", CultureInfo.InvariantCulture)))
                              : "(okunamadı)"));
            sb.AppendLine("Atalet oranı max/min: " +
                          (sonuc.AtaletVar ? sonuc.AtaletOrani.ToString("G17", CultureInfo.InvariantCulture) : "(yok)"));
            sb.AppendLine("Sınır kutusu: " + ProfilBoyutMetni(sonuc.SinirOlculeriMm));
            sb.AppendLine("Sınır kutusu kaynağı: " + sonuc.SinirKutusuKaynak);
            sb.AppendLine();

            sb.AppendLine("COM NESNE TEŞHİSİ");
            ProfilNesnesiniRaporla(sb, "ActiveEditor.ActiveObject", sonuc.ActivePart);
            ProfilNesnesiniRaporla(sb, "MainBody", sonuc.MainBody);
            ProfilNesnesiniRaporla(sb, "InertiaElement", sonuc.InertiaObject);
            ProfilNesnesiniRaporla(sb, "MeasureItem", sonuc.MeasureObject);
            try { ProfilNesnesiniRaporla(sb, "ActiveEditor", (object)catia.ActiveEditor); }
            catch { }

            string masaustu = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string dosya = "macria_profil_teshis_" +
                           ProfilDosyaParcasi(row.ProductName) + "_" +
                           DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            string yol = Path.Combine(masaustu, dosya);
            File.WriteAllText(yol, sb.ToString(), Encoding.UTF8);
            return yol;
        }

        private static void ProfilNesnesiniRaporla(
            StringBuilder sb, string etiket, object? nesne)
        {
            if (nesne == null)
            {
                sb.AppendLine(etiket + ": (alınamadı)");
                return;
            }

            string tip = ComProbe.TipAdi(nesne);
            List<string> uyeler = ComProbe.UyeAdlari(nesne);
            var ilgili = new List<string>();
            foreach (string uye in uyeler)
            {
                string f = uye.ToLowerInvariant();
                if (f.Contains("volume") || f.Contains("area") ||
                    f.Contains("inertia") || f.Contains("principal") ||
                    f.Contains("bound") || f.Contains("box") ||
                    f.Contains("face") || f.Contains("edge") ||
                    f.Contains("body") || f.Contains("shape") ||
                    f.Contains("export") || f.Contains("save"))
                    ilgili.Add(uye);
            }

            sb.AppendLine(etiket + " [" + tip + "] — " + uyeler.Count + " üye");
            sb.AppendLine("  İlgili üyeler: " +
                          (ilgili.Count == 0 ? "(yok)" : string.Join(", ", ilgili)));
        }

        private void btnProfilRaporAc_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_sonProfilRaporu) ||
                !File.Exists(_sonProfilRaporu))
            {
                LogInfo("Açılacak Profil Teşhis Raporu Yok.");
                return;
            }

            OpenExported(_sonProfilRaporu);
        }

        private async void btnProfilStep_Click(object sender, RoutedEventArgs e)
        {
            if (_profilIslemde || _exporting) return;
            if (!(gridProfil.SelectedItem is ProfilRow row))
            {
                LogInfo("STEP İçin Listeden Bir Profil Adayı Seçin.");
                return;
            }

            if (row.RepRef == null)
            {
                row.StepBasarisiz("Parça referansı yok; F10 demo satırı STEP'e aktarılamaz.");
                LogError(row.StepAciklamasi);
                return;
            }

            if (row.DurumKodu != "Kuvvetli")
            {
                bool devam = OnayWindow.Sor(
                    this,
                    "Profil Kararı Kesin Değil",
                    row.ProductName + " için durum: " + row.Durum + ".\n\n" +
                    "Bu sürüm profili sanal kesitle kesin doğrulamaz. STEP, parçanın " +
                    "CATIA'daki mevcut geometrisini olduğu gibi kaydeder; eğri profil " +
                    "düzleştirilmez. Devam etmek istiyor musunuz?",
                    "STEP Kaydet", "Vazgeç");
                if (!devam) return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Seçili Profil Adayını STEP Olarak Kaydet",
                Filter = "STEP (*.stp)|*.stp|STEP (*.step)|*.step",
                DefaultExt = ".stp",
                AddExtension = true,
                FileName = ProfilStepDosyaAdi(row)
            };
            if (dlg.ShowDialog() != true) return;

            if (!FareUyarisiniGoster()) return;

            _catia = GetCatia() ?? _catia;
            if (_catia == null)
            {
                row.StepBasarisiz("CATIA bağlantısı kurulamadı.");
                LogError("STEP Export Başlatılamadı — CATIA bağlantısı yok.");
                return;
            }

            var sure = System.Diagnostics.Stopwatch.StartNew();
            _profilIslemde = true;
            _stopRequested = false;
            row.StepBasladi();
            ProfilButonlariniGuncelle();
            ShowPipStart(row.ProductName, "STEP Export");
            LogInfo("Profil STEP Export Başladı: " + row.ProductName);

            bool ok = false;
            string hata = "";
            try
            {
                SetExporting(true);
                ok = await ProfilStepExportEt(row.RepRef, dlg.FileName);
                if (!ok)
                    hata = _stopRequested
                        ? "STEP işlemi kullanıcı tarafından durduruldu."
                        : "STEP oluşturulamadı. Konsoldaki 'STEP yolu' satırlarını ve teşhis raporunu kontrol edin.";
            }
            catch (Exception ex)
            {
                hata = "STEP export hatası: " + ex.Message;
                LogError(hata);
            }
            finally
            {
                SetExporting(false);
                _profilIslemde = false;
                ProfilButonlariniGuncelle();
                IslemSuresiniYaz("Profil STEP Export", sure);
            }

            if (ok)
            {
                row.StepBasarili(dlg.FileName);
                LogSuccess("Profil STEP Yazıldı: " + dlg.FileName);
                if (chkOpenAfter.IsChecked == true) OpenExported(dlg.FileName);
                await FinishPip(ExportPipWindow.PipState.Done, row.ProductName);
            }
            else
            {
                row.StepBasarisiz(hata);
                LogError(hata);
                await FinishPip(ExportPipWindow.PipState.Error, row.ProductName);
            }
        }

        private static string ProfilStepDosyaAdi(ProfilRow row)
        {
            string ad = ProfilDosyaParcasi(row.ProductName);
            if (!string.IsNullOrWhiteSpace(row.Revision))
                ad += "_Rev" + ProfilDosyaParcasi(row.Revision);
            return ad + ".stp";
        }

        private static string ProfilDosyaParcasi(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "Profil" : value.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                text = text.Replace(c, '_');
            return text;
        }

        private async Task<bool> ProfilStepExportEt(object repRef, string fullPath)
        {
            dynamic catia = _catia;
            bool pencereAcildi = false;

            try
            {
                try
                {
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    string alt = Path.ChangeExtension(fullPath, ".step");
                    if (!string.Equals(alt, fullPath, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(alt)) File.Delete(alt);
                }
                catch { }

                LogInfo("STEP — Parça Açılıyor...");
                dynamic svc = catia.ActiveEditor.GetService("PLMOpenService");
                object? newEd = null;
                svc.PLMOpenInNewWindow(repRef, ref newEd);
                pencereAcildi = true;
                await Task.Delay(2500);

                object? activeObject = null;
                object? activeDocument = null;
                try { activeObject = catia.ActiveEditor.ActiveObject; } catch { }
                try { activeDocument = catia.ActiveDocument; } catch { }

                OnayIzleyiciBaslat();

                // V5 ve ActiveDocument sunan kurulumlarda en temiz yol.
                var dogrudanAdaylar = new List<(string Ad, object? Nesne)>
                {
                    ("ActiveDocument", activeDocument),
                    ("Yeni Editor", newEd),
                    ("ActiveObject", activeObject)
                };

                foreach ((string ad, object? nesne) in dogrudanAdaylar)
                {
                    if (nesne == null) continue;
                    string neden;
                    if (!ProfilExportDataDene(nesne, fullPath, out neden))
                    {
                        LogInfo("STEP yolu — " + ad + ".ExportData kullanılamadı: " + neden);
                        continue;
                    }

                    LogInfo("STEP yolu — " + ad + ".ExportData çağrıldı; dosya bekleniyor.");
                    if (await ProfilStepDosyasiniBekle(fullPath, 30000))
                    {
                        LogSuccess("STEP yolu başarılı — " + ad + ".ExportData");
                        return true;
                    }

                    LogInfo("STEP yolu — ExportData döndü ancak dosya oluşmadı.");
                }

                // 3DEXPERIENCE R2020x'te ActiveDocument/ExportData genellikle
                // sunulmaz. Mevcut DXF altyapisindaki panel + Windows Save As
                // kontroluyle guvenli bir yedek yol denenir.
                bool uiOk = await ProfilStepUiYolunuDene(catia, fullPath);
                if (uiOk) return true;

                LogError("STEP yollarının hiçbiri dosya oluşturamadı. " +
                         "Konsol satırlarını ve masaüstündeki profil teşhis raporunu paylaşın.");
                return false;
            }
            finally
            {
                OnayIzleyiciDurdur();
                if (pencereAcildi)
                {
                    try { catia.ActiveWindow.Close(); } catch { }
                    await WaitForAssembly(15000);
                    await Task.Delay(800);
                }
            }
        }

        private static bool ProfilExportDataDene(
            object nesne, string fullPath, out string neden)
        {
            neden = "";
            Type tip = nesne.GetType();
            string stem = Path.Combine(
                Path.GetDirectoryName(fullPath) ?? "",
                Path.GetFileNameWithoutExtension(fullPath));

            // V5 Document.ExportData genellikle uzantisiz hedef bekler ve
            // uzantiyi formata gore kendisi ekler. Bazi kurulumlar ise tam
            // dosya yolunu kabul eder. Iki bicimi de kontrollu olarak dene.
            foreach (string hedef in new[] { stem, fullPath })
            {
                foreach (string format in new[] { "stp", "step", "STEP" })
                {
                    try
                    {
                        tip.InvokeMember(
                            "ExportData", BindingFlags.InvokeMethod, null, nesne,
                            new object?[] { hedef, format });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        neden = Kisa(ex.InnerException?.Message ?? ex.Message);
                    }
                }
            }
            return false;
        }

        private async Task<bool> ProfilStepUiYolunuDene(dynamic catia, string fullPath)
        {
            IntPtr hCatia = PencereAraclari.AnaPencere();
            if (hCatia == IntPtr.Zero) hCatia = FindWindow(null, "3DEXPERIENCE");

            // 3DEXPERIENCE'ta "Save As STEP" ve "Save As" kayitli birer
            // StartCommand adi degildir. Bu nedenle R2020x kurulumunda
            // "command not found / komut bulunamadi" hatasi veriyordu.
            // Native uygulamadaki gercek genel disari aktarma komutu Export'tur;
            // STEP turunu acilan Windows diyalogunda ayrica seciyoruz.
            string[] komutlar = { "Export" };
            foreach (string komut in komutlar)
            {
                if (_stopRequested) return false;

                try
                {
                    LogInfo("STEP yolu — CATIA komutu deneniyor: " + komut);
                    ForceForeground(hCatia);
                    await Task.Delay(500);
                    catia.StartCommand(komut);
                }
                catch (Exception ex)
                {
                    LogInfo("STEP komutu kullanılamadı (" + komut + "): " +
                            Kisa(ex.Message));
                    continue;
                }

                await Task.Delay(Ayarlar.PanelBekleme);

                // Export komutu kimi CATIA surumlerinde diyalogu gec acar.
                // DXF paneline ait ogretilmis Save As koordinatini burada
                // kullanma: o koordinat STEP icin yanlis dugmeye basabilir.
                IntPtr hSave = await WaitForSaveDialog(8000);

                if (hSave == IntPtr.Zero)
                {
                    LogInfo("STEP yolu — " + komut +
                            " sonrasında kaydetme penceresi bulunamadı.");
                    PressEscape();
                    await Task.Delay(800);
                    continue;
                }

                string turAdi;
                bool turSecildi = ProfilStepTurunuSec(hSave, out turAdi);
                if (!turSecildi)
                {
                    LogError("STEP dosya türü kaydetme penceresinde bulunamadı; " +
                             "yanlış format kaydetmemek için işlem iptal edildi.");
                    ForceForeground(hSave);
                    PressEscape();
                    await Task.Delay(800);
                    continue;
                }

                LogInfo("STEP dosya türü seçildi: " + turAdi);

                IntPtr edit = FindFileNameEdit(hSave);
                if (edit == IntPtr.Zero)
                {
                    LogError("STEP — Dosya adı alanı bulunamadı.");
                    ForceForeground(hSave);
                    PressEscape();
                    continue;
                }

                SendMessage(edit, WM_SETTEXT, IntPtr.Zero, fullPath);
                await Task.Delay(300);

                IntPtr saveButton = GetDlgItem(hSave, 1);
                if (saveButton != IntPtr.Zero)
                    SendMessage(saveButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                else
                {
                    ForceForeground(hSave);
                    PressEnter();
                }

                if (await ProfilStepDosyasiniBekle(fullPath, 30000))
                {
                    LogSuccess("STEP yolu başarılı — CATIA " + komut + ".");
                    await WaitForNoSaveDialog(8000);
                    return true;
                }

                LogInfo("STEP yolu — Kaydet komutundan sonra dosya oluşmadı.");
                PressEscape();
            }

            return false;
        }

        private static async Task<bool> ProfilStepDosyasiniBekle(
            string fullPath, int timeoutMs)
        {
            if (await WaitForFile(fullPath, timeoutMs)) return true;

            // ExportData uygulamalari dosya uzantisini bazen kendisi ekler.
            // Tam uzantili ad verildiginde "parca.stp.stp" uretebilen CATIA
            // surumlerini de yakala ve kullanicinin sectigi ada normalize et.
            string stem = Path.Combine(
                Path.GetDirectoryName(fullPath) ?? "",
                Path.GetFileNameWithoutExtension(fullPath));
            string[] alternatifler =
            {
                string.Equals(Path.GetExtension(fullPath), ".stp", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(fullPath, ".step")
                    : Path.ChangeExtension(fullPath, ".stp"),
                fullPath + ".stp",
                fullPath + ".step",
                stem + ".stp",
                stem + ".step"
            };

            foreach (string alternative in alternatifler)
            {
                if (string.Equals(alternative, fullPath, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(alternative)) continue;

                try
                {
                    if (!File.Exists(fullPath)) File.Move(alternative, fullPath);
                    return File.Exists(fullPath);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private const uint CB_GETCOUNT = 0x0146;
        private const uint CB_GETLBTEXT = 0x0148;
        private const uint CB_GETLBTEXTLEN = 0x0149;
        private const uint CB_SETCURSEL = 0x014E;
        private const int CBN_SELCHANGE = 1;

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr ProfilSendMessageText(
            IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        private static bool ProfilStepTurunuSec(IntPtr hDlg, out string secilen)
        {
            secilen = "";
            IntPtr comboFound = IntPtr.Zero;
            int indexFound = -1;
            string textFound = "";

            EnumChildWindows(hDlg, (ch, l) =>
            {
                if (!string.Equals(GetCls(ch), "ComboBox", StringComparison.OrdinalIgnoreCase))
                    return true;

                int count = (int)SendMessage(
                    ch, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0 || count > 500) return true;

                for (int i = 0; i < count; i++)
                {
                    int len = (int)SendMessage(
                        ch, CB_GETLBTEXTLEN, (IntPtr)i, IntPtr.Zero);
                    if (len <= 0 || len > 1000) continue;

                    var sb = new StringBuilder(len + 2);
                    ProfilSendMessageText(ch, CB_GETLBTEXT, (IntPtr)i, sb);
                    string item = sb.ToString();
                    if (!ProfilStepMetniMi(item)) continue;

                    comboFound = ch;
                    indexFound = i;
                    textFound = item;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (comboFound != IntPtr.Zero && indexFound >= 0)
            {
                SendMessage(comboFound, CB_SETCURSEL, (IntPtr)indexFound, IntPtr.Zero);
                int id = GetDlgCtrlID(comboFound);
                IntPtr parent = GetParent(comboFound);
                if (parent != IntPtr.Zero)
                {
                    int wParam = (id & 0xFFFF) | (CBN_SELCHANGE << 16);
                    PostMessage(parent, WM_COMMAND, (IntPtr)wParam, comboFound);
                }

                secilen = textFound;
                return true;
            }

            // Yeni Windows diyaloglarinda ComboBox, Win32 alt penceresi degil
            // UI Automation ogesi olabilir.
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hDlg);
                AutomationElementCollection combos = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ComboBox));

                foreach (AutomationElement combo in combos)
                {
                    object pattern;
                    if (combo.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                    {
                        string current = ((ValuePattern)pattern).Current.Value ?? "";
                        if (ProfilStepMetniMi(current))
                        {
                            secilen = current;
                            return true;
                        }
                    }

                    if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
                    {
                        ((ExpandCollapsePattern)pattern).Expand();
                        Thread.Sleep(180);

                        AutomationElementCollection items = AutomationElement.RootElement.FindAll(
                            TreeScope.Descendants,
                            new PropertyCondition(
                                AutomationElement.ControlTypeProperty,
                                ControlType.ListItem));

                        foreach (AutomationElement item in items)
                        {
                            string name = "";
                            try { name = item.Current.Name ?? ""; } catch { }
                            if (!ProfilStepMetniMi(name)) continue;

                            object selectPattern;
                            if (item.TryGetCurrentPattern(
                                    SelectionItemPattern.Pattern, out selectPattern))
                            {
                                ((SelectionItemPattern)selectPattern).Select();
                                secilen = name;
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool ProfilStepMetniMi(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string f = text.ToLowerInvariant();
            return f.Contains("step") || f.Contains("*.stp") || f.EndsWith(".stp");
        }
    }
}
